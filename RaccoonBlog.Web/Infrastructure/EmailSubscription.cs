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
                "NewComment" => NewCommentTemplate,
                "SpamDigest" => SpamDigestTemplate,
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
                cmd.DigestDate,
                cmd.SpamComments,
                blog_name = blogName,
                comment_count = cmd.SpamComments?.Count ?? 0
            });
        }

        private const string NewCommentTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0;padding:0;font-family:'Segoe UI',Tahoma,Geneva,Verdana,sans-serif;background:#f4f4f4;">
            <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f4;padding:20px 0;">
            <tr><td align="center">
            <table width="600" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:4px;overflow:hidden;">
              <tr>
                <td style="background:#2c3e50;padding:20px 30px;">
                  <h1 style="margin:0;color:#ffffff;font-size:22px;">{{ blog_name }}</h1>
                </td>
              </tr>
              <tr>
                <td style="padding:30px;">
                  <h2 style="margin:0 0 10px;color:#2c3e50;font-size:18px;">New Comment on:
                    <a href="https://ayende.com/blog/{{ post_id | string.replace 'posts/' '' }}/{{ post_slug }}?key={{ key }}" style="color:#2980b9;text-decoration:none;">{{ post_title }}</a>
                  </h2>
                  <hr style="border:none;border-top:1px solid #ecf0f1;margin:15px 0;" />
                  <table cellpadding="0" cellspacing="0" style="width:100%;margin-bottom:15px;">
                    <tr>
                      <td style="padding:5px 0;color:#7f8c8d;width:90px;vertical-align:top;">Author:</td>
                      <td style="padding:5px 0;color:#2c3e50;font-weight:bold;">{{ author }}</td>
                    </tr>
                    <tr>
                      <td style="padding:5px 0;color:#7f8c8d;vertical-align:top;">Email:</td>
                      <td style="padding:5px 0;color:#2c3e50;">{{ comment_email }}</td>
                    </tr>
                    {{ if comment_url }}
                    <tr>
                      <td style="padding:5px 0;color:#7f8c8d;vertical-align:top;">URL:</td>
                      <td style="padding:5px 0;"><a href="{{ comment_url }}" style="color:#2980b9;">{{ comment_url }}</a></td>
                    </tr>
                    {{ end }}
                  </table>
                  <div style="background:#f9f9f9;border-left:4px solid #2c3e50;padding:15px;margin:15px 0;color:#333;line-height:1.6;">
                    {{ comment_body }}
                  </div>
                  <table cellpadding="0" cellspacing="0" style="width:100%;margin:15px 0;font-size:12px;color:#95a5a6;">
                    <tr>
                      <td style="padding:3px 0;">IP: {{ ip_address }}</td>
                    </tr>
                    <tr>
                      <td style="padding:3px 0;">User-Agent: {{ user_agent }}</td>
                    </tr>
                  </table>
                  <hr style="border:none;border-top:1px solid #ecf0f1;margin:15px 0;" />
                  <table cellpadding="0" cellspacing="0">
                    <tr>
                      <td style="padding-right:10px;">
                        <a href="https://ayende.com/blog/{{ post_id | string.replace 'posts/' '' }}/{{ post_slug }}?key={{ key }}#comments" style="display:inline-block;padding:8px 16px;background:#2980b9;color:#ffffff;text-decoration:none;border-radius:3px;font-size:13px;">View Comment</a>
                      </td>
                      <td style="padding-right:10px;">
                        <a href="https://ayende.com/blog/admin/comments" style="display:inline-block;padding:8px 16px;background:#2c3e50;color:#ffffff;text-decoration:none;border-radius:3px;font-size:13px;">Admin</a>
                      </td>
                      {{ if ip_address }}
                      <td>
                        <a href="https://ayende.com/blog/admin/settings" style="display:inline-block;padding:8px 16px;background:#c0392b;color:#ffffff;text-decoration:none;border-radius:3px;font-size:13px;">Block IP</a>
                      </td>
                      {{ end }}
                    </tr>
                  </table>
                </td>
              </tr>
              <tr>
                <td style="background:#ecf0f1;padding:15px 30px;text-align:center;font-size:12px;color:#95a5a6;">
                  {{ blog_name }} &mdash; Comment Notification
                </td>
              </tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;

        private const string SpamDigestTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin:0;padding:0;font-family:'Segoe UI',Tahoma,Geneva,Verdana,sans-serif;background:#f4f4f4;">
            <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f4;padding:20px 0;">
            <tr><td align="center">
            <table width="600" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:4px;overflow:hidden;">
              <tr>
                <td style="background:#2c3e50;padding:20px 30px;">
                  <h1 style="margin:0;color:#ffffff;font-size:22px;">{{ blog_name }}</h1>
                </td>
              </tr>
              <tr>
                <td style="padding:30px;">
                  <h2 style="margin:0 0 5px;color:#2c3e50;font-size:18px;">Spam Digest</h2>
                  <p style="margin:0 0 20px;color:#7f8c8d;font-size:14px;">{{ digest_date }} &mdash; {{ comment_count }} comment{{ if comment_count != 1 }}s{{ end }} flagged as spam</p>
                  <hr style="border:none;border-top:1px solid #ecf0f1;margin:15px 0;" />
                  {{ if spam_comments }}
                  {{ for entry in spam_comments }}
                  <div style="background:#f9f9f9;border-left:4px solid #e74c3c;padding:12px 15px;margin:10px 0;">
                    <p style="margin:0 0 5px;font-weight:bold;color:#2c3e50;">{{ entry.author }}
                      {{ if entry.post_title }}<span style="font-weight:normal;color:#7f8c8d;font-size:12px;"> on {{ entry.post_title }}</span>{{ end }}
                    </p>
                    <p style="margin:0;color:#555;font-size:13px;line-height:1.5;">{{ entry.body | string.truncate 200 }}</p>
                  </div>
                  {{ end }}
                  {{ end }}
                  <hr style="border:none;border-top:1px solid #ecf0f1;margin:20px 0 15px;" />
                  <a href="https://ayende.com/blog/admin/comments" style="display:inline-block;padding:10px 20px;background:#2c3e50;color:#ffffff;text-decoration:none;border-radius:3px;font-size:13px;">Manage Spam</a>
                </td>
              </tr>
              <tr>
                <td style="background:#ecf0f1;padding:15px 30px;text-align:center;font-size:12px;color:#95a5a6;">
                  {{ blog_name }} &mdash; Spam Digest
                </td>
              </tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;

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
