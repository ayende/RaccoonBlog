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
                    await worker.Run(async batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            ProcessCommand(store, item.Result);
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

        private static void ProcessCommand(IDocumentStore store, SendEmailCommand cmd)
        {
            _log.Info("Processing email command: {Type} - {Subject}", cmd.Type, cmd.Subject);

            try
            {
                using (var client = new SmtpClient())
                {
                    var message = new MailMessage
                    {
                        IsBodyHtml = true,
                        Body = BuildEmailBody(cmd),
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

                _log.Info("Email sent: {Subject}", cmd.Subject);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to process email command: {Subject}", cmd.Subject);
            }
        }

        private static string BuildEmailBody(SendEmailCommand cmd)
        {
            if (cmd.Type == "NewComment")
            {
                return $"<h2>New comment on {cmd.PostTitle}</h2>" +
                       $"<p><strong>{cmd.Author}</strong> ({cmd.CommentEmail})</p>" +
                       $"<p>{cmd.CommentBody}</p>" +
                       $"<p>IP: {cmd.IpAddress} | UA: {cmd.UserAgent}</p>";
            }

            if (cmd.Type == "SpamDigest")
            {
                return $"<h2>Spam Digest for {cmd.DigestDate}</h2>" +
                       $"<p>{cmd.SpamComments?.Count ?? 0} comments flagged as spam.</p>";
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
                    Query = "from EmailCommands"
                });
                _log.Info("Created data subscription '{Name}'.", SubscriptionName);
            }
        }
    }
}
