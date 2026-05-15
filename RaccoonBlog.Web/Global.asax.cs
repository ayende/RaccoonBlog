using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using DataAnnotationsExtensions.ClientValidation;
using FluentScheduler;
using NLog;
using RaccoonBlog.Web.Areas.Admin.Controllers;
using RaccoonBlog.Web.Controllers;
using RaccoonBlog.Web.Helpers.Binders;
using RaccoonBlog.Web.Infrastructure;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Infrastructure.Indexes;
using RaccoonBlog.Web.Infrastructure.Jobs;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.AI;
using Raven.Client.Documents.Operations.Refresh;
using Raven.Client.Documents.Session;
using Raven.Client.Http;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web
{
	public class MvcApplication : HttpApplication
	{
        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();

		public MvcApplication()
		{
			BeginRequest += (sender, args) =>
			{
				BundleConfig.RegisterThemeBundles(HttpContext.Current, BundleTable.Bundles);
				HttpContext.Current.Items["CurrentRequestRavenSession"] = RaccoonController.DocumentStore.OpenSession();
			};
			EndRequest += (sender, args) =>
			{
				using (var session = (IDocumentSession)HttpContext.Current.Items["CurrentRequestRavenSession"])
				{
					if (session == null)
						return;

					if (Server.GetLastError() != null)
						return;

					session.SaveChanges();
				}
			};
		}

		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();

			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			new RouteConfigurator(RouteTable.Routes).Configure();

			InitializeDocumentStore();
			LogManager.GetCurrentClassLogger().Info("Started Raccoon Blog");

			ModelBinders.Binders.Add(typeof(CommentCommandOptions), new RemoveSpacesEnumBinder());
			ModelBinders.Binders.Add(typeof(Guid), new GuidBinder());

			DataAnnotationsModelValidatorProviderExtensions.RegisterValidationExtensions();

			AutoMapperConfiguration.Configure();
			BundleConfig.RegisterBundles(BundleTable.Bundles);

			RaccoonController.DocumentStore = DocumentStore;

            ConfigureRefresh();
            CreateGenAiTask();
            CreateSeoGenAiTask();
            CreateSocialMediaGenAiTask();
            EmailSubscription.Start();

			JobManager.JobException += JobExceptionHandler;
			JobManager.Initialize(new SocialNetworkIntegrationJobsRegistry());
		}

	    public static IDocumentStore DocumentStore { get; private set; }

		static void JobExceptionHandler(JobExceptionInfo info)
		{
			_log.Fatal(info.Exception, $"Error executing background job {info.Name}.");
		}

	    private static void InitializeDocumentStore()
	    {
	        if (DocumentStore != null) return;

	        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
	        ServicePointManager.CheckCertificateRevocationList = false;
	        ServicePointManager.ServerCertificateValidationCallback += OnServerCertificateCustomValidationCallback;

	        var urls = WebConfigurationManager.AppSettings["Raven/Urls"];
	        var database = WebConfigurationManager.AppSettings["Raven/Database"];
	        var store = new DocumentStore
	        {
	            Urls = urls.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries),
	            Database = database,
	            Conventions = new DocumentConventions
	            {
	                AggressiveCache = { Mode = AggressiveCacheMode.DoNotTrackChanges }
	            }
	        };

	        var certificatePath = WebConfigurationManager.AppSettings["Raven/CertificatePath"];
	        if (certificatePath != null)
	        {
	            var certificatePassword = WebConfigurationManager.AppSettings["Raven/CertificatePassword"];
	            var certificate = new X509Certificate2(certificatePath, certificatePassword);
	            store.Certificate = certificate;
	        }

		    var requestsTimeout = WebConfigurationManager.AppSettings["Raven/RequestsTimeoutInSec"];
		    if (requestsTimeout != null && int.TryParse(requestsTimeout, out int seconds))
		    {
			    store.Conventions.RequestTimeout = TimeSpan.FromSeconds(seconds);
		    }

		    DocumentStore = store.Initialize();
	    }

	    private static bool OnServerCertificateCustomValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslpolicyerrors)
	    {
	        return true;
	    }

        private static void ConfigureRefresh()
        {
            var refreshConfig = new RefreshConfiguration
            {
                Disabled = false,
                RefreshFrequencyInSec = 60,
                MaxItemsToProcess = 500
            };
            DocumentStore.Maintenance.Send(new ConfigureRefreshOperation(refreshConfig));
            _log.Info("Document refresh enabled.");
        }

        // Both GenAI tasks expect an AI connection string named "ai-chat" to be configured
        // in RavenDB Studio before starting the application. The connection string should point
        // to a chat-capable model (e.g., OpenAI gpt-4o-mini or equivalent).

        private static void CreateGenAiTask()
        {
            try
            {
                var config = new GenAiConfiguration
                {
                    Name = "spam-filter",
                    Identifier = "spam-filter",
                    ConnectionStringName = "ai-chat",
                    Disabled = false,
                    Collection = "PostComments",
                    GenAiTransformation = new GenAiTransformation
                    {
                        Script = @"
for(const comment of this.Comments) {
    if(comment.SpamCheckStatus === 'Pending') {
        ai.genContext({
            Id: comment.Id, Author: comment.Author, Body: comment.Body,
            Email: comment.Email, Url: comment.Url,
            UserHostAddress: comment.UserHostAddress, UserAgent: comment.UserAgent,
            CommenterId: comment.CommenterId
        });
    }
}"
                    },
                    Prompt = @"
You are a spam filter for a technical blog. Analyze this blog comment.
A spam comment typically includes irrelevant or promotional content,
excessive links, misleading information, or advertising intent.
A legitimate comment engages with the post, asks relevant questions,
or provides constructive feedback.

Respond with JSON: { 'IsSpam': bool, 'Reason': 'brief explanation' }",
                    SampleObject = @"{ ""IsSpam"": true, ""Reason"": ""Promotional content with link"" }",
                    UpdateScript = @"
const idx = this.Comments.findIndex(c => c.Id == $input.Id);
if (idx < 0) return;

if ($output.IsSpam) {
    var c = this.Comments[idx];
    c.IsSpam = true;
    c.SpamCheckStatus = 'Spam';
    this.Comments.splice(idx, 1);
    this.Spam.push(c);

    var today = new Date().toISOString().split('T')[0];
    var digestId = 'SpamDigests/' + today;
    var tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    tomorrow.setHours(8, 0, 0, 0);

    var spamEntry = {
        CommentId: $input.Id, Author: $input.Author, Body: $input.Body,
        PostId: this.Post.Id, Timestamp: new Date().toISOString()
    };

    var post = load(this.Post.Id);
    if (post) {
        post.CommentsCount--;
        if (post.CommentsCount < 0) post.CommentsCount = 0;
    }

    var dig = load(digestId);
    if (dig) {
        dig.SpamComments.push(spamEntry);
        dig.Count++;
        put(digestId, dig);
    } else {
        put(digestId, {
            Type: 'SpamDigest',
            View: 'SpamDigest',
            DigestDate: today,
            SpamComments: [spamEntry],
            Count: 1,
            BlogName: '',
            SendTo: ''
        }, {
            '@collection': 'EmailCommands',
            '@refresh': tomorrow.toISOString()
        });
    }
} else {
    this.Comments[idx].SpamCheckStatus = 'Valid';

    if ($input.CommenterId) {
        var commenter = load($input.CommenterId);
        if (commenter) {
            commenter.IsTrustedCommenter = true;
        }
    }

    var blogConfig = load('Blog/Config');
    var blogName = blogConfig ? blogConfig.Title : '';
    var ownerEmail = blogConfig ? blogConfig.OwnerEmail : '';

    var post = load(this.Post.Id);
    var postTitle = post ? post.Title : '';

    put('EmailCommands/new-comment-' + $input.Id, {
        Type: 'NewComment',
        View: 'NewComment',
        ReplyTo: $input.Email || '',
        Subject: 'Comment on: ' + postTitle + ' from ' + $input.Author,
        SendTo: ownerEmail,
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
        BlogName: blogName,
        Key: post && post.ShowPostEvenIfPrivate ? post.ShowPostEvenIfPrivate.toString() : ''
    }, {
        '@collection': 'EmailCommands'
    });
}"
                };
                DocumentStore.Maintenance.Send(new AddGenAiOperation(config));
                _log.Info("GenAI spam filter task created.");
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to create GenAI spam filter task.");
            }
        }

        private static void CreateSeoGenAiTask()
        {
            try
            {
                var config = new GenAiConfiguration
                {
                    Name = "SEO Analysis",
                    Identifier = "seo-analysis",
                    ConnectionStringName = "ai-chat",
                    Disabled = false,
                    Collection = "Posts",
                    GenAiTransformation = new GenAiTransformation
                    {
                        Script =
"""
ai.genContext({
    Title: this.Title,
    Body: this.Body,
    Tags: this.Tags
});
"""
                    },
                    Prompt =
"""
You are an expert SEO analyst. Analyze the blog post provided and generate:

1. A compelling meta description (max 160 characters) that accurately summarizes the post and includes relevant keywords to improve search engine ranking. Write for humans, not search engines.

2. A list of 3-8 SEO keywords/keyphrases relevant to the post content. These should be terms people would search for to find this content. Include both short-tail and long-tail keywords where appropriate.

The post content is provided below. Analyze the title, body text, and existing tags.
""",
                    SampleObject =
"""
{
    "MetaDescription": "A concise, compelling meta description under 160 characters that summarizes the blog post for search engines.",
    "Keywords": [ "primary keyword", "secondary keyword phrase", "related term" ]
}
""",
                    UpdateScript =
"""
this.SeoMetaDescription = $output.MetaDescription;
this.SeoKeywords = $output.Keywords;
this.SeoLastAnalyzedAt = new Date().toISOString();
"""
                };
                DocumentStore.Maintenance.Send(new AddGenAiOperation(config));
                _log.Info("GenAI SEO analysis task created.");
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to create GenAI SEO analysis task.");
            }
        }

        private static void CreateSocialMediaGenAiTask()
        {
            try
            {
                var config = new GenAiConfiguration
                {
                    Name = "social-media",
                    Identifier = "social-media",
                    ConnectionStringName = "ai-chat",
                    Disabled = false,
                    Collection = "Posts",
                    GenAiTransformation = new GenAiTransformation
                    {
                        Script =
"""
if (!this.Social || !this.Social.GeneratedAt) {
    if (this.PublishAt && new Date(this.PublishAt) <= new Date()) {
        ai.genContext({
            Title: this.Title,
            Body: this.Body,
            Tags: this.Tags
        });
    }
}
"""
                    },
                    Prompt =
"""
You are a social media manager for a technical blog about software development,
databases, and distributed systems. Generate engaging social media text for the
blog post provided.

1. Twitter: A concise, engaging tweet (max 250 characters, excluding URL which
   will be appended automatically). Should hook technical readers. Include 1-2
   relevant hashtags if natural. Do not include a URL.

2. Reddit: A compelling submission title for a programming subreddit audience.
   Should be informative and spark discussion. No clickbait. Max 300 characters.
""",
                    SampleObject =
"""
{
    "TwitterText": "Concise engaging tweet text with #relevantHashtag",
    "RedditTitle": "Compelling Reddit submission title for technical audience"
}
""",
                    UpdateScript =
"""
this.Social = this.Social || {};
this.Social.TwitterText = $output.TwitterText;
this.Social.RedditTitle = $output.RedditTitle;
this.Social.GeneratedAt = new Date().toISOString();
"""
                };
                DocumentStore.Maintenance.Send(new AddGenAiOperation(config));
                _log.Info("GenAI social media task created.");
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to create GenAI social media task.");
            }
        }
    }
}
