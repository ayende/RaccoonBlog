using System;
using NLog;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;

namespace RaccoonBlog.Web.Services
{
    public static class SocialPublishingWorker
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string SubscriptionName = "SocialPublishingWorker";

        public static void Start(IDocumentStore store)
        {
            try
            {
                store.Subscriptions.Create(new SubscriptionCreationOptions
                {
                    Name = SubscriptionName,
                    Filter = "this.Status == 'Pending'"
                });
            }
            catch (SubscriptionAlreadyExistsException)
            {
                _log.Info("Subscription {0} already exists, connecting...", SubscriptionName);
            }

            var worker = store.Subscriptions.GetSubscriptionWorker<SocialPublishCommand>(
                new SubscriptionWorkerOptions(SubscriptionName)
                {
                    TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5),
                    MaxErtsPerSecond = 20
                });

            worker.OnSubscriptionConnectionRetry += exception =>
            {
                _log.Warn(exception, "Social publishing worker connection retry");
            };

            worker.AfterAcknowledgment += batch =>
            {
                _log.Info("Social publishing worker batch acknowledged, processed {0} items", batch.Items.Count);
            };

            worker.Run(ProcessBatch(store));
        }

        private static Action<SubscriptionBatch<SocialPublishCommand>> ProcessBatch(IDocumentStore store)
        {
            return batch =>
            {
                using (var session = store.OpenSession())
                {
                    foreach (var item in batch.Items)
                    {
                        var cmd = item.Result;

                        if (cmd.PublishAt > DateTimeOffset.Now)
                        {
                            _log.Debug("Skipping command {0} — PublishAt {1} is still in the future (waiting for @refresh)", cmd.Id, cmd.PublishAt);
                            continue;
                        }

                        _log.Info("Processing social publish command {0}: target={1}, account={2}, post={3}", cmd.Id, cmd.Target, cmd.Account, cmd.PostId);

                        try
                        {
                            ExecuteCommand(cmd, session);
                            cmd.Status = CommandStatus.Completed;
                            _log.Info("Successfully completed command {0}", cmd.Id);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, "Failed to execute social publish command {0}", cmd.Id);
                            cmd.Status = CommandStatus.Failed;
                            cmd.ErrorMessage = ex.Message;
                        }

                        cmd.CompletedAt = DateTimeOffset.Now;
                    }

                    session.SaveChanges();
                }
            };
        }

        private static void ExecuteCommand(SocialPublishCommand cmd, Raven.Client.Documents.Session.IDocumentSession session)
        {
            switch (cmd.Target?.ToLowerInvariant())
            {
                case "twitter":
                    new SubmitToTwitterStrategy().Publish(cmd, session);
                    break;

                case "reddit":
                    // TODO: Migrate existing Reddit logic to command-based dispatch
                    _log.Warn("Reddit target not yet migrated to command-based dispatch — command {0}", cmd.Id);
                    break;

                case "discord":
                case "github":
                    _log.Warn("Target '{0}' not yet implemented — command {1}", cmd.Target, cmd.Id);
                    break;

                default:
                    _log.Warn("Unknown target '{0}' — command {1}", cmd.Target, cmd.Id);
                    break;
            }
        }
    }
}
