using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using NLog;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;

namespace RaccoonBlog.Web.Infrastructure
{
    public static class EmailSubscription
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;

            var store = MvcApplication.DocumentStore;
            // Expects a data subscription named "email-worker" on the EmailCommands collection
            // to be created in RavenDB Studio before starting the application.
            var worker = store.Subscriptions.GetSubscriptionWorker<SendEmailCommand>("email-worker");

            worker.AfterAcknowledgment += (_, _) => _log.Info("Email subscription batch acknowledged.");

            Task.Run(async () =>
            {
                try
                {
                    await worker.Run(async batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            ProcessCommand(item.Result);
                        }

                        await Task.CompletedTask;
                    });
                }
                catch (Exception e)
                {
                    _log.Fatal(e, "Email subscription worker failed");
                }
            });
        }

        private static void ProcessCommand(SendEmailCommand cmd)
        {
            _log.Info("Processing email command: {Type} - {Subject}", cmd.Type, cmd.Subject);

            try
            {
                var viewName = cmd.View ?? "NewComment";
                object model;

                if (cmd.Type == "SpamDigest")
                {
                    model = new SpamDigestEmailViewModel
                    {
                        BlogName = cmd.BlogName,
                        Date = cmd.DigestDate,
                        TotalCount = cmd.SpamComments?.Count ?? 0,
                        Comments = cmd.SpamComments?.Select(c => new SpamDigestEmailViewModel.SpamCommentViewModel
                        {
                            CommentId = c.CommentId,
                            Author = c.Author,
                            Body = c.Body,
                            BodyPreview = c.Body?.Length > 500 ? c.Body[..500] + "..." : c.Body,
                            PostId = c.PostId,
                            PostTitle = c.PostTitle,
                            PostSlug = SlugConverter.TitleToSlug(c.PostTitle),
                            Timestamp = c.Timestamp
                        }).ToList()
                    };
                }
                else if (cmd.Type == "NewComment")
                {
                    model = new NewCommentEmailViewModel
                    {
                        Id = cmd.CommentId,
                        Author = cmd.Author,
                        Body = new MvcHtmlString(cmd.CommentBody),
                        Email = cmd.CommentEmail,
                        Url = cmd.CommentUrl,
                        CreatedAt = cmd.CreatedAt,
                        IsSpam = false,
                        PostId = cmd.PostId,
                        PostTitle = cmd.PostTitle,
                        PostSlug = cmd.PostSlug,
                        BlogName = cmd.BlogName,
                        Key = cmd.Key,
                        CommenterId = cmd.CommenterId,
                        IpAddress = cmd.IpAddress,
                        UserAgent = cmd.UserAgent
                    };
                }
                else if (cmd.Type == "RedditFailure" && !string.IsNullOrEmpty(cmd.ModelJson))
                {
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    model = serializer.DeserializeObject(cmd.ModelJson);
                    viewName = "RedditSubmissionFailed";
                }
                else
                {
                    _log.Warn("Unknown email command type: {Type}", cmd.Type);
                    return;
                }

                var body = RenderViewToString(viewName, model);

                using (var client = new SmtpClient())
                {
                    var message = new MailMessage
                    {
                        IsBodyHtml = true,
                        Body = body,
                        Subject = cmd.Subject
                    };

                    if (!string.IsNullOrEmpty(cmd.ReplyTo))
                    {
                        try { message.ReplyToList.Add(new MailAddress(cmd.ReplyTo)); }
                        catch { }
                    }

                    message.To.Add(cmd.SendTo);

                    client.Send(message);
                }

                using (var session = MvcApplication.DocumentStore.OpenSession())
                {
                    var doc = session.Load<SendEmailCommand>(cmd.GetDocumentId());
                    if (doc != null)
                    {
                        session.Delete(doc);
                        session.SaveChanges();
                    }
                }

                _log.Info("Email sent and command deleted: {Subject}", cmd.Subject);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to process email command: {Subject}", cmd.Subject);
            }
        }

        private static string RenderViewToString(string viewName, object model)
        {
            var controllerContext = new ControllerContext(
                new MailHttpContext(),
                new RouteData(),
                new MailController());

            var viewEngineResult = ViewEngines.Engines.FindView(controllerContext, viewName, "_Layout");
            if (viewEngineResult.View == null)
                throw new Exception($"Email template not found: {viewName}");

            using (var sw = new StringWriter())
            {
                var viewContext = new ViewContext(
                    controllerContext,
                    viewEngineResult.View,
                    new ViewDataDictionary(model),
                    new TempDataDictionary(),
                    sw);

                viewEngineResult.View.Render(viewContext, sw);
                return sw.GetStringBuilder().ToString();
            }
        }

        private static string GetDocumentId(this SendEmailCommand cmd)
        {
            return cmd.Type == "SpamDigest"
                ? $"SpamDigests/{cmd.DigestDate}"
                : $"EmailCommands/new-comment-{cmd.CommentId}";
        }

        private class MailController : Controller { }

        private class MailHttpContext : HttpContextBase
        {
            private readonly IDictionary _items = new System.Collections.Hashtable();
            public override IDictionary Items => _items;
            public override HttpRequestBase Request => new MailHttpRequest();
            public override HttpResponseBase Response => new MailHttpResponse();
            public override object GetService(Type serviceType) => null;
        }

        private class MailHttpRequest : HttpRequestBase
        {
            private static readonly Uri BlogUrl = new Uri(ConfigurationManager.AppSettings["MainUrl"] ?? "http://localhost/");
            public override Uri Url => BlogUrl;
            public override string UserAgent => "RaccoonBlog.Email/1.0";
            public override string ApplicationPath => BlogUrl.AbsolutePath;
            public override System.Collections.Specialized.NameValueCollection ServerVariables => new();
            public override System.Web.Caching.Cache Cache => HttpRuntime.Cache;
        }

        private class MailHttpResponse : HttpResponseBase
        {
            public override string ApplyAppPathModifier(string virtualPath) => virtualPath;
        }
    }
}
