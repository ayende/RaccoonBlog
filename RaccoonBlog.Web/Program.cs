using FluentScheduler;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog;
using NLog.Web;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Helpers.Binders;
using RaccoonBlog.Web.Infrastructure.Configuration;
using RaccoonBlog.Web.Infrastructure.DataProtection;
using RaccoonBlog.Web.Services;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.AI.Agents;
using Raven.Client.Documents.Session;
using Raven.Client.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WilderMinds.MetaWeblog;
using MetaWeblogService = RaccoonBlog.Web.Services.MetaWeblogService;

var builder = WebApplication.CreateBuilder(args);

// Initialize ConfigurationHelper early
ConfigurationHelper.Initialize(builder.Configuration);

// Configure NLog
builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.AddRouting(options => options.LowercaseUrls = true);

// Add services to the container
builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new GuidBinderProvider());
    options.ModelBinderProviders.Insert(0, new RemoveSpacesEnumBinderProvider());
})
.AddNewtonsoftJson();
builder.Services.AddScoped<MediaService>();
builder.Services.AddSingleton<BannerService>();
builder.Services.AddMetaWeblog<MetaWeblogService>();
builder.Services.AddSingleton<CacheSignalService>();
builder.Services.AddWebOptimizer(pipeline =>
{
    pipeline.AddJavaScriptBundle("/js/main-bundle.min.js", "js/moment.js", "js/lib/MarkdownDeepLib.min.js", "js/utils.js", "js/raccoon-blog.js", "js/setup.js", "js/jquery.twbsPagination.js", "js/jquery.validate.js", "js/jquery.validate.unobtrusive.js");
    pipeline.AddJavaScriptBundle("/admin/js/admin-scripts-bundle.min.js", "js/bootstrap.js");

    pipeline.AddLessBundle("/admin/css/admin.styles.css", "admin/css/admin.bundle.less")
            .MinifyCss();

    var env = builder.Environment;
    var cssRootPath = Path.Combine(env.WebRootPath, "css");

    if (Directory.Exists(cssRootPath))
    {
        var bundleFiles = Directory.GetFiles(cssRootPath, "*.bundle.less");

        foreach (var bundleFile in bundleFiles)
        {
            var fileName = Path.GetFileName(bundleFile);
            var themeName = fileName.Substring(0, fileName.IndexOf(".bundle.less"));

            pipeline.AddLessBundle($"/css/custom/{themeName}.css", $"css/{themeName}.bundle.less")
                    .MinifyCss();
        }
    }
});
builder.Services.AddOutputCache();
builder.Services.AddMemoryCache();
// Configure Session (required for session-based TempData)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});


builder.Services.AddAntiforgery(options =>
{

    options.Cookie.Name = ".RaccoonBlog.Antiforgery";

    options.Cookie.Path = "/blog";

    options.Cookie.HttpOnly = true;


    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Configure TempData to use JSON serialization instead of BSON
builder.Services.AddSingleton<TempDataSerializer, JsonTempDataSerializer>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<Recaptcha2Verifier>(client => {
    client.BaseAddress = new Uri("https://www.google.com");
});
builder.Services.AddScoped<Recaptcha2Helper>();
builder.Services.AddScoped<RaccoonBlog.Web.Helpers.SignInHelper>();
// Configure RavenDB DocumentStore
var ravenUrls = builder.Configuration["Raven:Urls"]?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? ["http://localhost:8080"];
var ravenDatabase = builder.Configuration["Raven:Database"] ?? "blog.ayende.com";

ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
ServicePointManager.CheckCertificateRevocationList = false;

var documentStore = new DocumentStore
{
    Urls = ravenUrls,
    Database = ravenDatabase,
    Conventions = new DocumentConventions
    {
        AggressiveCache = { Mode = AggressiveCacheMode.TrackChanges }
    }
};

// Certificate configuration
var certificatePath = builder.Configuration["Raven:CertificatePath"];
if (!string.IsNullOrEmpty(certificatePath))
{
    var certificatePassword = builder.Configuration["Raven:CertificatePassword"];
    documentStore.Certificate = new X509Certificate2(certificatePath, certificatePassword);
}

// Request timeout configuration
if (int.TryParse(builder.Configuration["Raven:RequestsTimeoutInSec"], out int timeoutSeconds))
{
    documentStore.Conventions.RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds);
}

documentStore.Initialize();

// Configure GenAI tasks and subscriptions
ConfigureRefreshAndGenAiTasks(documentStore);

builder.Services.AddSingleton<IDocumentStore>(documentStore);

builder.Services.AddDataProtection()
    .SetApplicationName("RaccoonBlog")
    .AddKeyManagementOptions(o => o.XmlRepository = new RavenDbXmlRepository(documentStore));

builder.Services.AddScoped<IDocumentSession>(ctx =>
{
    return ctx.GetRequiredService<IDocumentStore>().OpenSession();
});

builder.Services.AddScoped<RaccoonBlog.Web.Models.BlogConfig>(ctx =>
{
    var session = ctx.GetRequiredService<IDocumentSession>();
    using (session.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
    {
        return session.Load<RaccoonBlog.Web.Models.BlogConfig>("Blog/Config")
                ?? new RaccoonBlog.Web.Models.BlogConfig();
    }
});

// Configure Authentication
var authBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Path = "/blog";
        options.Cookie.Name = ".RaccoonBlog.Auth";
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.LogoutPath = "/admin/login/logout";
    });

// Only add OAuth providers if credentials are configured
var googleClientId = builder.Configuration["Raccoon:OAuth:Google:ClientId"];
var googleClientSecret = builder.Configuration["Raccoon:OAuth:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });
}

var microsoftClientId = builder.Configuration["Raccoon:OAuth:Microsoft:ClientId"];
var microsoftClientSecret = builder.Configuration["Raccoon:OAuth:Microsoft:ClientSecret"];
if (!string.IsNullOrEmpty(microsoftClientId) && !string.IsNullOrEmpty(microsoftClientSecret))
{
    authBuilder.AddMicrosoftAccount(options =>
    {
        options.ClientId = microsoftClientId;
        options.ClientSecret = microsoftClientSecret;
    });
}

var facebookAppId = builder.Configuration["Raccoon:OAuth:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Raccoon:OAuth:Facebook:AppSecret"];
if (!string.IsNullOrEmpty(facebookAppId) && !string.IsNullOrEmpty(facebookAppSecret))
{
    authBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
    });
}

var twitterConsumerKey = builder.Configuration["Raccoon:OAuth:Twitter:ConsumerKey"];
var twitterConsumerSecret = builder.Configuration["Raccoon:OAuth:Twitter:ConsumerSecret"];
if (!string.IsNullOrEmpty(twitterConsumerKey) && !string.IsNullOrEmpty(twitterConsumerSecret))
{
    authBuilder.AddTwitter(options =>
    {
        options.ConsumerKey = twitterConsumerKey;
        options.ConsumerSecret = twitterConsumerSecret;
    });
}

// Configure AutoMapper using modern DI pattern for AutoMapper 15.x
// This automatically registers IMapper in DI and scans for profiles
builder.Services.AddAutoMapper(cfg =>
{
    // Add only specific profiles to avoid scanning classes that reference System.Web
    // Don't use assembly scanning as it will scan ALL types including MetaWeblog which has System.Web dependencies
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.AutoMapperConfiguration>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.PostViewModelMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.PostsViewModelMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.TagsListViewModelMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.SectionMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.EmailViewModelMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.SeriesMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.UserAdminMapperProfile>();
    cfg.AddProfile<RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.PostsAdminViewModelMapperProfile>();
});

// Initialize FluentScheduler jobs
JobManager.JobException += info =>
{
    var logger = LogManager.GetCurrentClassLogger();
    logger.Fatal(info.Exception, $"Error executing background job {info.Name}.");
};

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Initialize AutoMapper extensions with the IMapper instance from the ROOT service provider
// IMPORTANT: Do NOT use a scoped service provider here, as it will be disposed
// and AutoMapper will try to use the disposed provider for type converters
var mapper = app.Services.GetRequiredService<AutoMapper.IMapper>();
RaccoonBlog.Web.Infrastructure.AutoMapper.AutoMapperExtensions.Initialize(mapper);

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error/Index");
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;

    if (!path.StartsWith("/blog", StringComparison.OrdinalIgnoreCase))
    {
        var newPath = "/blog" + (path.EndsWith("/") ? path : path + "/");
        context.Response.Redirect(newPath);
        return;
    }
    await next();
});

app.UsePathBase("/blog");

app.UseHttpsRedirection();
app.UseWebOptimizer();

app.UseStaticFiles();

app.UseRouting();

app.UseOutputCache();

// Add session middleware (must be before authentication and authorization)
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var session = context.RequestServices.GetRequiredService<IDocumentSession>();

    await next();

    if (context.Response.StatusCode < 400 && context.Request.Method != "GET")
    {
        session.SaveChanges();
    }
});

app.MapRaccoonBlogRoutes();

// Map controller routes - Use MapAreaControllerRoute for Admin area
app.MapAreaControllerRoute(
    name: "admin",
    areaName: "Admin",
    pattern: "Admin/{controller=Posts}/{action=Index}/{id?}/{slug?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Posts}/{action=Index}/{id?}");

app.UseMetaWeblog("/Services/MetaWeblogAPI.ashx");
    
app.Run();

static void ConfigureRefreshAndGenAiTasks(IDocumentStore store)
{
    var log = LogManager.GetCurrentClassLogger();

    // Enable document refresh (needed for future post @refresh triggers)
    try
    {
        var refreshConfig = new Raven.Client.Documents.Operations.Refresh.RefreshConfiguration
        {
            Disabled = false,
            RefreshFrequencyInSec = 60,
            MaxItemsToProcess = 500
        };
        store.Maintenance.Send(new Raven.Client.Documents.Operations.Refresh.ConfigureRefreshOperation(refreshConfig));
        log.Info("Document refresh enabled.");
    }
    catch (Exception e)
    {
        log.Error(e, "Failed to configure refresh.");
    }

    // All GenAI tasks expect an AI connection string named "ai-chat" to be configured
    // in RavenDB Studio before starting the application. The connection string should point
    // to a chat-capable model (e.g., OpenAI gpt-4o-mini or equivalent).

    // GenAI spam filter task on PostComments collection
    try
    {
        var config = new Raven.Client.Documents.Operations.AI.GenAiConfiguration
        {
            Name = "spam-filter",
            Identifier = "spam-filter",
            ConnectionStringName = "ai-chat",
            Disabled = false,
            Collection = "PostComments",
            GenAiTransformation = new Raven.Client.Documents.Operations.AI.GenAiTransformation
            {
                Script = """
                    const post = load(this.Post.Id);
                    for(const comment of this.Comments) {
                        if(comment.SpamCheckStatus === 'Pending') {
                            ai.genContext({
                                Post: {Title: post.Title, Tags: post.Tags},
                                Id: comment.Id, 
                                Author: comment.Author, 
                                Body: comment.Body,
                                Email: comment.Email,
                                Url: comment.Url,
                                UserHostAddress: comment.UserHostAddress, 
                                UserAgent: comment.UserAgent,
                                CommenterId: comment.CommenterId,
                                PostCommentsId: id(this)
                            });
                        }
                    }
                    """
            },
            Queries = [
                new AiAgentToolQuery
                {
                    Name = "ReadPostComments",
                    Description = """
                                  Use this to read the _other_ comments on the same post to get more context. Returns all valid comments
                                  on this post, so you can check is replying to another comment, instead of trying to evaluate it in isolation.
                                  """,
                    ParametersSampleObject = "{}",
                    Query = """
                            from PostComments where id() = $PostCommentsId
                            """
                }
            ],
            Prompt = """
                You are a spam filter for a technical blog. Analyze this blog comment.
                A spam comment typically includes irrelevant or promotional content,
                excessive links, misleading information, or advertising intent.
                A legitimate comment engages with the post, asks relevant questions,
                or provides relevant feedback. You can see the title and tags of the post this
                comment is in reply to.

                Based on the comment content and metadata, determine if this comment is likely spam.
                The expected language is English, if it is anything else, that is also a strong signal of spam.

                If the comment may be spam, but can also be part of a conversation, you have the ReadPostComments
                tool that you can invoke to get the other comments on the same post to get more context before making a decision.
                """,
            SampleObject = """
                { "IsSpam": true, "Reason": "brief explanation why you think it is spam or not" }
                """,
            UpdateScript = """
                const idx = this.Comments.findIndex(c => c.Id == $input.Id);
                if (idx < 0) return;

                if ($output.IsSpam) {
                    var c = this.Comments[idx];
                    c.IsSpam = true;
                    c.SpamCheckStatus = 'Spam';
                    this.Comments.splice(idx, 1);
                    this.Spam.push(c);

                    var post = load(this.Post.Id);
                    if (post) {
                        post.CommentsCount--;
                        if (post.CommentsCount < 0) post.CommentsCount = 0;
                    }

                    var today = new Date().toISOString().split('T')[0];
                    var digestId = 'SpamDigests/' + today;
                    var tomorrow = new Date();
                    tomorrow.setDate(tomorrow.getDate() + 1);
                    tomorrow.setHours(8, 0, 0, 0);

                    var spamEntry = {
                        CommentId: $input.Id, Author: $input.Author, Body: $input.Body,
                        PostId: this.Post.Id, Timestamp: new Date().toISOString()
                    };

                    var dig = load(digestId);
                    if (!dig) {
                        dig = {
                            Type: 'SpamDigest',
                            View: 'SpamDigest',
                            DigestDate: today,
                            SpamComments: [],
                            Count: 0
                        };
                    }
                    dig.SpamComments.push(spamEntry);
                    dig.Count++;
                    put(digestId, dig, {
                        '@collection': 'EmailCommands',
                        '@refresh': tomorrow.toISOString()
                    });
                } else {
                    this.Comments[idx].SpamCheckStatus = 'Valid';

                    if ($input.CommenterId) {
                        var commenter = load($input.CommenterId);
                        if (!commenter.IsTrustedCommenter) {
                            commenter.IsTrustedCommenter = true;
                            put($input.CommenterId, commenter);
                        }
                    }

                    var post = load(this.Post.Id);
                    var postTitle = post ? post.Title : '';
                    var blogConfig = load('Blog/Config');

                    put('EmailCommands/new-comment-' + $input.Id, {
                        Type: 'NewComment',
                        View: 'NewComment',
                        ReplyTo: $input.Email || '',
                        Subject: 'Comment on: ' + postTitle + ' from ' + $input.Author,
                        CommentId: $input.Id,
                        Author: $input.Author || '',
                        CommentBody: $input.Body || '',
                        CommentEmail: $input.Email || '',
                        CommentUrl: $input.Url || '',
                        CreatedAt: new Date().toISOString(),
                        IpAddress: $input.UserHostAddress || '',
                        UserAgent: $input.UserAgent || '',
                        CommenterId: $input.CommenterId || '',
                        PostId: this.Post.Id || '',
                        PostTitle: postTitle,
                        PostSlug: post ? post.Slug : '',
                        BlogName: blogConfig ? blogConfig.Title : '',
                        Key: post.ShowPostEvenIfPrivate
                    }, {
                        '@collection': 'EmailCommands'
                    });
                }
                """
        };
        store.Maintenance.Send(new Raven.Client.Documents.Operations.AI.AddGenAiOperation(config));
        log.Info("GenAI spam filter task created.");
    }
    catch (Exception e)
    {
        log.Error(e, "Failed to create GenAI spam filter task.");
    }

    // GenAI SEO analysis task on Posts collection
    try
    {
        var config = new Raven.Client.Documents.Operations.AI.GenAiConfiguration
        {
            Name = "SEO Analysis",
            Identifier = "seo-analysis",
            ConnectionStringName = "ai-chat",
            Disabled = false,
            Collection = "Posts",
            GenAiTransformation = new Raven.Client.Documents.Operations.AI.GenAiTransformation
            {
                Script = """
                    ai.genContext({
                        Title: this.Title,
                        Body: this.Body,
                        Tags: this.Tags
                    });
                    """
            },
            Prompt = """
                You are an expert SEO analyst. Analyze the blog post provided and generate:

                1. A compelling meta description (max 160 characters) that accurately summarizes the post and includes relevant keywords to improve search engine ranking. Write for humans, not search engines.

                2. A list of 3-8 SEO keywords/keyphrases relevant to the post content. These should be terms people would search for to find this content. Include both short-tail and long-tail keywords where appropriate.

                The post content is provided below. Analyze the title, body text, and existing tags.
                """,
            SampleObject = """
                {
                    "MetaDescription": "A concise, compelling meta description under 160 characters.",
                    "Keywords": [ "primary keyword", "secondary keyword phrase", "related term" ]
                }
                """,
            UpdateScript = """
                this.SeoMetaDescription = $output.MetaDescription;
                this.SeoKeywords = $output.Keywords;
                this.SeoLastAnalyzedAt = new Date().toISOString();
                """
        };
        store.Maintenance.Send(new Raven.Client.Documents.Operations.AI.AddGenAiOperation(config));
        log.Info("GenAI SEO analysis task created.");
    }
    catch (Exception e)
    {
        log.Error(e, "Failed to create GenAI SEO analysis task.");
    }

    // GenAI social media text generation task on Posts collection
    try
    {
        var config = new Raven.Client.Documents.Operations.AI.GenAiConfiguration
        {
            Name = "social-media",
            Identifier = "social-media",
            ConnectionStringName = "ai-chat",
            Disabled = false,
            Collection = "Posts",
            GenAiTransformation = new Raven.Client.Documents.Operations.AI.GenAiTransformation
            {
                Script = """
                    // Regenerate social text whenever a post is updated.
                    // On initial deployment, skip the historical backlog.
                    var metadata = getMetadata(this);
                    var lastModified = new Date(metadata['@last-modified']);

                    if (this.Social && this.Social.GeneratedAt) {
                        if (lastModified <= new Date(this.Social.GeneratedAt)) return;
                    } else {
                        var cutoff = new Date('2026-05-15T00:00:00Z');
                        if (lastModified < cutoff) return;
                    }

                    ai.genContext({
                        Title: this.Title,
                        Body: this.Body,
                        Tags: this.Tags
                    });
                    """
            },
            Prompt = """
                You are a social media manager for a technical blog about software development,
                databases, and distributed systems. Generate engaging social media text for the
                blog post provided.

                1. Twitter: A concise, engaging tweet (max 250 characters, excluding URL which
                   will be appended automatically). Should hook technical readers. Include 1-2
                   relevant hashtags if natural. Do not include a URL.

                2. Reddit: A compelling submission title for a programming subreddit audience.
                   Should be informative and spark discussion. No clickbait. Max 300 characters.
                """,
            SampleObject = """
                {
                    "TwitterText": "Concise engaging tweet text with #relevantHashtag",
                    "RedditTitle": "Compelling Reddit submission title for technical audience"
                }
                """,
            UpdateScript = """
                this.Social = this.Social || {};
                this.Social.TwitterText = $output.TwitterText;
                this.Social.RedditTitle = $output.RedditTitle;
                this.Social.GeneratedAt = new Date().toISOString();

                if (this.PublishAt) {
                    var publishDate = new Date(this.PublishAt);
                    if (publishDate > new Date()) {
                        var metadata = getMetadata(this);
                        metadata['@refresh'] = this.PublishAt;
                    }
                }
                """
        };
        store.Maintenance.Send(new Raven.Client.Documents.Operations.AI.AddGenAiOperation(config));
        log.Info("GenAI social media task created.");
    }
    catch (Exception e)
    {
        log.Error(e, "Failed to create GenAI social media task.");
    }

    // Start subscription workers
    RaccoonBlog.Web.Infrastructure.EmailSubscription.Start(store);
    RaccoonBlog.Web.Infrastructure.SocialPostingSubscription.Start(store);
}

// Custom JSON TempData Serializer to replace BSON serializer
public class JsonTempDataSerializer : TempDataSerializer
{
    private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Include
    };

    public override IDictionary<string, object> Deserialize(byte[] value)
    {
        if (value == null || value.Length == 0)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        var json = Encoding.UTF8.GetString(value);
        return JsonConvert.DeserializeObject<Dictionary<string, object>>(json, Settings)
            ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    public override byte[] Serialize(IDictionary<string, object> values)
    {
        if (values == null || values.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var json = JsonConvert.SerializeObject(values, Settings);
        return Encoding.UTF8.GetBytes(json);
    }
}

// Partial Program class to make it accessible for WebApplicationFactory in integration tests
public partial class Program { }
