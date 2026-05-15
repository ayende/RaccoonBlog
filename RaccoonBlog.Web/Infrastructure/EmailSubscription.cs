using System;
using System.Net.Mail;
using System.Threading.Tasks;
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

        private const string SubscriptionName = "email-worker";

        public static void Start(IDocumentStore store)
        {
            if (_started) return;
            _started = true;

            EnsureSubscriptionExists(store);
            var worker = store.Subscriptions.GetSubscriptionWorker<SendEmailCommand>(SubscriptionName);

            worker.AfterAcknowledgment += _ =>
            {
                _log.Info("Email subscription batch acknowledged.");
                return Task.CompletedTask;
            };

            Task.Run(async () =>
            {
                try
                {
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

                using (var client = new SmtpClient())
                {
                    var message = new MailMessage
                    {
                        IsBodyHtml = true,
                        Body = BuildEmailBody(cmd, blogName),
                        Subject = cmd.Subject
                    };

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
            if (cmd.Type == "NewComment")
            {
                return $"<h2>New comment on {cmd.PostTitle}</h2>" +
                       $"<p><em>{blogName}</em></p>" +
                       $"<p><strong>{cmd.Author}</strong> ({cmd.CommentEmail})</p>" +
                       $"<p>{cmd.CommentBody}</p>" +
                       $"<p>IP: {cmd.IpAddress} | UA: {cmd.UserAgent}</p>";
            }

            if (cmd.Type == "SpamDigest")
            {
                return $"<h2>Spam Digest — {blogName}</h2>" +
                       $"<p>{cmd.SpamComments?.Count ?? 0} comments flagged as spam on {cmd.DigestDate}.</p>";
            }

            return $"<p>{cmd.Subject}</p>";
        }

        private static void EnsureSubscriptionExists(IDocumentStore store)
        {
            try
            {
                store.Subscriptions.GetSubscriptionState(SubscriptionName);
            }
            catch (SubscriptionDoesNotExistException)
            {
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
}
