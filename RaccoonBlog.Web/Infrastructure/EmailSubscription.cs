using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NLog;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;

namespace RaccoonBlog.Web.Infrastructure
{
    public static class EmailSubscription
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static bool _started;
        private static IConfiguration _configuration;

        private const string SubscriptionName = "email-worker";

        public static void Start(IDocumentStore store, IConfiguration configuration)
        {
            if (_started) return;
            _started = true;
            _configuration = configuration;

            // Run all RavenDB interaction (subscription creation + the worker loop) on a
            // background task so app startup never blocks on — or crashes from — RavenDB being
            // unreachable. The worker comes up on its own once the server is available.
            Task.Run(async () =>
            {
                try
                {
                    EnsureSubscriptionExists(store);
                    var worker = store.Subscriptions.GetSubscriptionWorker<SendEmailCommand>(SubscriptionName);

                    worker.AfterAcknowledgment += _ =>
                    {
                        _log.Info("Email subscription batch acknowledged.");
                        return Task.CompletedTask;
                    };

                    await worker.Run(batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            ProcessCommand(store, item.Result);
                        }
                        return Task.CompletedTask;
                    });
                }
                catch (Exception e)
                {
                    _log.Fatal(e, "Email subscription worker failed");
                }
            });
        }

        private static void ProcessCommand(IDocumentStore store, SendEmailCommand cmd)
        {
            _log.Info("Processing email command: {Type} - {Subject}", cmd.Type, cmd.Subject);

            try
            {
                using var session = store.OpenSession();
                var blogConfig = session.Load<RaccoonBlog.Web.Models.BlogConfig>("Blog/Config");

                var sendTo = cmd.SendTo;
                if (string.IsNullOrEmpty(sendTo))
                    sendTo = blogConfig?.OwnerEmail;

                if (string.IsNullOrEmpty(sendTo))
                {
                    _log.Warn("No recipient for email command: {Subject}. Set SendTo on the command or OwnerEmail in BlogConfig.", cmd.Subject);
                    return;
                }

                var blogName = cmd.BlogName ?? blogConfig?.Title ?? "";

                var smtpHost = _configuration?["SmtpSettings:Host"];
                var smtpPort = _configuration?.GetValue<int>("SmtpSettings:Port", 587) ?? 587;
                var smtpUser = _configuration?["SmtpSettings:UserName"];
                var smtpPass = _configuration?["SmtpSettings:Password"];
                var enableSsl = _configuration?.GetValue<bool>("SmtpSettings:EnableSsl", true) ?? true;
                var fromEmail = _configuration?["SmtpSettings:From"];

                if (string.IsNullOrEmpty(smtpHost))
                {
                    _log.Warn("SmtpSettings:Host is not configured; cannot send email: {Subject}", cmd.Subject);
                    return;
                }

                using (var client = new SmtpClient(smtpHost, smtpPort))
                {
                    client.EnableSsl = enableSsl;
                    if (!string.IsNullOrEmpty(smtpUser))
                        client.Credentials = new NetworkCredential(smtpUser, smtpPass);

                    var message = new MailMessage
                    {
                        IsBodyHtml = true,
                        Body = BuildEmailBody(cmd, blogName),
                        Subject = cmd.Subject
                    };

                    if (!string.IsNullOrEmpty(fromEmail))
                        message.From = new MailAddress(fromEmail);

                    if (!string.IsNullOrEmpty(cmd.ReplyTo))
                    {
                        try { message.ReplyToList.Add(new MailAddress(cmd.ReplyTo)); }
                        catch { }
                    }

                    message.To.Add(sendTo);
                    client.Send(message);
                }

                _log.Info("Email sent: {Subject}", cmd.Subject);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to process email command: {Subject}", cmd.Subject);
            }
        }

        private static string BuildEmailBody(SendEmailCommand cmd, string blogName)
        {
            var template = cmd.Type switch
            {
                "NewComment" => LoadTemplate("NewComment.html"),
                "SpamDigest" => LoadTemplate("SpamDigest.html"),
                "SocialPostingFailure" => LoadTemplate("SocialPostingFailure.html"),
                _ => "{{ subject }}"
            };

            var scribanTemplate = Scriban.Template.Parse(template);
            return scribanTemplate.Render(new
            {
                cmd.Subject,
                cmd.Author,
                cmd.CommentBody,
                cmd.CommentEmail,
                cmd.CommentUrl,
                cmd.IpAddress,
                cmd.UserAgent,
                cmd.PostTitle,
                cmd.PostId,
                cmd.PostSlug,
                cmd.Key,
                cmd.Network,
                cmd.Target,
                cmd.ErrorMessage,
                cmd.DigestDate,
                cmd.SpamComments,
                blog_name = blogName,
                comment_count = cmd.SpamComments?.Count ?? 0
            });
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _templateCache = new();

        private static string LoadTemplate(string fileName)
        {
            return _templateCache.GetOrAdd(fileName, static name =>
            {
                var asm = typeof(EmailSubscription).Assembly;
                var resourceName = Array.Find(
                    asm.GetManifestResourceNames(),
                    n => n.EndsWith("EmailTemplates." + name, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    throw new InvalidOperationException($"Embedded email template '{name}' was not found.");

                using var stream = asm.GetManifestResourceStream(resourceName);
                using var reader = new System.IO.StreamReader(stream);
                return reader.ReadToEnd();
            });
        }

        private static void EnsureSubscriptionExists(IDocumentStore store)
        {
            try
            {
                if (store.Subscriptions.GetSubscriptionState(SubscriptionName) != null)
                    return;
            }
            catch (SubscriptionDoesNotExistException)
            {
            }

            store.Subscriptions.Create(new SubscriptionCreationOptions
            {
                Name = SubscriptionName,
                Query = "from EmailCommands where Subject != null and not exists(@metadata.@refresh)",
                ChangeVector = "LastDocument"
            });
            _log.Info("Created data subscription '{Name}'.", SubscriptionName);
        }
    }
}
