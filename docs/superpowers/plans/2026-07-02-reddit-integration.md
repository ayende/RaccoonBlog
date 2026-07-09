# Reddit Integration + Social Posting Failure Notifications — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically submit published posts to Reddit under the existing `SocialPostingSubscription`, and email the owner (with full exception detail) whenever a Twitter or Reddit post fails.

**Architecture:** The RavenDB `social-posting` subscription already fires `SocialPostingSubscription.ProcessPost` for posts with `Social.GeneratedAt` set and a `@social`/`@social/reddit` tag. We implement the existing `TryPostToReddit` stub (RedditSharp 2.0, stateless — Reddit's own duplicate detection provides idempotency) and add a shared failure-email helper that enqueues a `SendEmailCommand` consumed by `EmailSubscription`.

**Tech Stack:** .NET 8, RavenDB.Client 7.2.1 (data subscriptions), RedditSharp 2.0.0, Scriban 7.2.0 (email templates), xUnit (tests).

## Global Constraints

- Target framework: `net8.0`.
- Email documents live in the RavenDB **`EmailCommands`** collection and deserialize to `RaccoonBlog.Web.Models.SendEmailCommand`; the `email-worker` subscription filters `from EmailCommands where Subject != null and not exists(@metadata.@refresh)`.
- Email HTML templates are embedded resources under `RaccoonBlog.Web/Infrastructure/EmailTemplates/*.html` (already globbed as `<EmbeddedResource>` in the csproj) and loaded by `EmailSubscription.LoadTemplate(fileName)` via resource-name suffix match.
- Scriban renders C# `PascalCase` members as `snake_case` (so `cmd.Network` → `{{ network }}`).
- Reddit credentials come from `BlogConfig`: `RedditUser`, `RedditPassword`, `RedditClientAppId`, `RedditClientSecret`, `RedditSubredditsToSubmitToOnPublish`. Parse subreddits with `RedditHelper.ParseSubreddits(blogConfig)`.
- RedditSharp API: `new BotWebAgent(user, password, clientId, clientSecret, redirectUri)` → `new Reddit(agent)` → `await reddit.GetSubredditAsync(name)` → `await subreddit.SubmitPostAsync(title, url, resubmit: false)`. Duplicate URLs throw `RedditSharp.DuplicateLinkException`.
- Do not persist anything back to the `Post` from the subscription (avoids re-triggering the Posts subscription / double-posting). Reddit idempotency relies on `resubmit: false` + `DuplicateLinkException`.

---

### Task 1: Social posting failure email template + command fields

**Files:**
- Create: `RaccoonBlog.Web/Infrastructure/EmailTemplates/SocialPostingFailure.html`
- Modify: `RaccoonBlog.Web/Models/SendEmailCommand.cs`
- Modify: `RaccoonBlog.Web/Infrastructure/EmailSubscription.cs` (`BuildEmailBody`)
- Test: `RaccoonBlog.IntegrationTests/EmailTemplates/SocialPostingFailureTemplateTests.cs`

**Interfaces:**
- Consumes: `EmailSubscription.LoadTemplate(string fileName)` (existing, private) — not called by the test; the test loads the embedded resource directly.
- Produces: `SendEmailCommand.Network`, `SendEmailCommand.Target`, `SendEmailCommand.ErrorMessage` (all `string`); embedded resource `...Infrastructure.EmailTemplates.SocialPostingFailure.html`; `BuildEmailBody` handles `Type == "SocialPostingFailure"`.

- [ ] **Step 1: Write the failing test**

(`Scriban` is available in the test project transitively through the `RaccoonBlog.Web` project reference. If the `Scriban` namespace does not resolve at compile time, add `<PackageReference Include="Scriban" Version="7.2.0" />` to `RaccoonBlog.IntegrationTests/RaccoonBlog.IntegrationTests.csproj`.)

Create `RaccoonBlog.IntegrationTests/EmailTemplates/SocialPostingFailureTemplateTests.cs`:

```csharp
using System;
using System.IO;
using RaccoonBlog.Web.Models;
using Scriban;
using Xunit;

namespace RaccoonBlog.IntegrationTests.EmailTemplates
{
    public class SocialPostingFailureTemplateTests
    {
        [Fact]
        public void Template_Embeds_Parses_And_Renders_Fields()
        {
            var asm = typeof(SendEmailCommand).Assembly;
            var resourceName = Array.Find(
                asm.GetManifestResourceNames(),
                n => n.EndsWith("EmailTemplates.SocialPostingFailure.html", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(resourceName);

            using var stream = asm.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var templateText = reader.ReadToEnd();

            var template = Template.Parse(templateText);
            Assert.False(template.HasErrors, string.Join("; ", template.Messages));

            var html = template.Render(new
            {
                blog_name = "Test Blog",
                network = "Reddit",
                target = "/r/programming",
                post_title = "My Post",
                post_id = "posts/1",
                post_slug = "my-post",
                error_message = "System.Exception: boom <fail>"
            });

            Assert.Contains("Reddit", html);
            Assert.Contains("/r/programming", html);
            Assert.Contains("My Post", html);
            Assert.Contains("boom", html);
            // error detail must be HTML-encoded (no raw angle brackets from the trace)
            Assert.Contains("&lt;fail&gt;", html);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test RaccoonBlog.IntegrationTests --filter "FullyQualifiedName~SocialPostingFailureTemplateTests"`
Expected: FAIL — `Assert.NotNull(resourceName)` fails because the template does not exist yet (`resourceName` is null).

- [ ] **Step 3: Create the embedded template**

Create `RaccoonBlog.Web/Infrastructure/EmailTemplates/SocialPostingFailure.html`:

```html
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
      <h2 style="margin:0 0 10px;color:#c0392b;font-size:18px;">Social posting to {{ network }} failed</h2>
      <p style="margin:0 0 10px;color:#2c3e50;">
        Post:
        <a href="https://ayende.com/blog/{{ post_id | string.replace 'posts/' '' }}/{{ post_slug }}" style="color:#2980b9;text-decoration:none;">{{ post_title }}</a>
      </p>
      {{ if target }}
      <p style="margin:0 0 10px;color:#7f8c8d;font-size:14px;">Target: {{ target }}</p>
      {{ end }}
      <hr style="border:none;border-top:1px solid #ecf0f1;margin:15px 0;" />
      <p style="margin:0 0 8px;color:#7f8c8d;font-size:13px;">Error detail:</p>
      <pre style="background:#f9f9f9;border-left:4px solid #c0392b;padding:15px;margin:0;color:#333;font-size:12px;line-height:1.5;white-space:pre-wrap;word-break:break-word;overflow-x:auto;">{{ error_message | html.escape }}</pre>
    </td>
  </tr>
  <tr>
    <td style="background:#ecf0f1;padding:15px 30px;text-align:center;font-size:12px;color:#95a5a6;">
      {{ blog_name }} &mdash; Social Posting Failure
    </td>
  </tr>
</table>
</td></tr>
</table>
</body>
</html>
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test RaccoonBlog.IntegrationTests --filter "FullyQualifiedName~SocialPostingFailureTemplateTests"`
Expected: PASS (1 passed).

- [ ] **Step 5: Add the command fields**

In `RaccoonBlog.Web/Models/SendEmailCommand.cs`, add three properties inside the `SendEmailCommand` class (next to the other scalar fields, before `DigestDate`):

```csharp
        public string Network { get; set; }
        public string Target { get; set; }
        public string ErrorMessage { get; set; }
```

- [ ] **Step 6: Wire the template into BuildEmailBody**

In `RaccoonBlog.Web/Infrastructure/EmailSubscription.cs`, update the template switch in `BuildEmailBody`:

```csharp
            var template = cmd.Type switch
            {
                "NewComment" => LoadTemplate("NewComment.html"),
                "SpamDigest" => LoadTemplate("SpamDigest.html"),
                "SocialPostingFailure" => LoadTemplate("SocialPostingFailure.html"),
                _ => "{{ subject }}"
            };
```

Then add the three fields to the Scriban render object in the same method (inside the `scribanTemplate.Render(new { ... })` anonymous object, alongside `cmd.PostSlug`):

```csharp
                cmd.Network,
                cmd.Target,
                cmd.ErrorMessage,
```

- [ ] **Step 7: Build and re-run the template test**

Run: `dotnet build RaccoonBlog.Web/RaccoonBlog.Web.csproj`
Expected: `Build succeeded. 0 Error(s)`.
Run: `dotnet test RaccoonBlog.IntegrationTests --filter "FullyQualifiedName~SocialPostingFailureTemplateTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add RaccoonBlog.Web/Infrastructure/EmailTemplates/SocialPostingFailure.html \
        RaccoonBlog.Web/Models/SendEmailCommand.cs \
        RaccoonBlog.Web/Infrastructure/EmailSubscription.cs \
        RaccoonBlog.IntegrationTests/EmailTemplates/SocialPostingFailureTemplateTests.cs
git commit -m "Add social posting failure email template + command fields"
```

---

### Task 2: Reddit posting + shared failure-email helper

**Files:**
- Modify: `RaccoonBlog.Web/Infrastructure/SocialPostingSubscription.cs` (implement `TryPostToReddit`, add `EnqueueSocialFailureEmail`)
- Modify: `RaccoonBlog.Web/Program.cs` (route `SendEmailCommand` to the `EmailCommands` collection)

**Interfaces:**
- Consumes: `SendEmailCommand.{Network,Target,ErrorMessage}` (Task 1); `RedditHelper.ParseSubreddits(BlogConfig)`; `PostHelper.Url(Post)`; `SlugConverter.TitleToSlug(string)`.
- Produces: `SocialPostingSubscription.EnqueueSocialFailureEmail(IDocumentStore store, Post post, string network, string target, string errorDetail)` (private static void) — used by Task 3.

- [ ] **Step 1: Route SendEmailCommand to the EmailCommands collection**

C#-stored `SendEmailCommand` would default to a `SendEmailCommands` collection, which the `email-worker` subscription (`from EmailCommands`) would never see. Fix the convention.

In `RaccoonBlog.Web/Program.cs`, find the `DocumentStore` construction (the `Conventions = new DocumentConventions { AggressiveCache = { ... } }` block) and add a `FindCollectionName` mapping:

```csharp
    Conventions = new DocumentConventions
    {
        AggressiveCache = { Mode = AggressiveCacheMode.TrackChanges },
        FindCollectionName = type => type == typeof(RaccoonBlog.Web.Models.SendEmailCommand)
            ? "EmailCommands"
            : DocumentConventions.DefaultGetCollectionName(type)
    }
```

(`DocumentConventions` is already imported via `using Raven.Client.Documents.Conventions;`.)

- [ ] **Step 2: Add the failure-email helper**

In `RaccoonBlog.Web/Infrastructure/SocialPostingSubscription.cs`, add the following private static method (place it after `TryPostToReddit`). Ensure `using RaccoonBlog.Web.Infrastructure.Common;` (for `SlugConverter`) and `using RaccoonBlog.Web.Models;` are present.

```csharp
        private static void EnqueueSocialFailureEmail(IDocumentStore store, Post post, string network, string target, string errorDetail)
        {
            try
            {
                using var session = store.OpenSession();
                var cmd = new SendEmailCommand
                {
                    Type = "SocialPostingFailure",
                    Subject = $"Social posting to {network} failed: {post.Title}",
                    Network = network,
                    Target = target ?? "",
                    ErrorMessage = errorDetail,
                    PostId = post.Id,
                    PostTitle = post.Title,
                    PostSlug = SlugConverter.TitleToSlug(post.Title),
                    CreatedAt = DateTimeOffset.Now
                };
                session.Store(cmd, "EmailCommands/social-failure-" + Guid.NewGuid().ToString("N"));
                session.SaveChanges();
                _log.Info("Queued {Network} failure email for {PostId}", network, post.Id);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to queue {Network} failure email for {PostId}", network, post.Id);
            }
        }
```

- [ ] **Step 3: Implement TryPostToReddit**

In the same file, replace the existing `TryPostToReddit` stub:

```csharp
        private static void TryPostToReddit(IDocumentStore store, Post post)
        {
            if (post.Integration?.Reddit?.Submitted == true)
                return;

            // TODO: Reddit integration is currently disabled in .NET 8 migration
            // (SubmitToRedditStrategy is wrapped in #if FALSE).
            // Re-enable when RedditSharp 2.0+ API is integrated.
            _log.Warn("Reddit posting disabled (pending RedditSharp 2.0 migration) for post {PostId}", post.Id);
        }
```

with the full implementation:

```csharp
        private static async Task TryPostToReddit(IDocumentStore store, Post post)
        {
            var title = post.Social?.RedditTitle;
            if (string.IsNullOrWhiteSpace(title))
                title = System.Net.WebUtility.HtmlDecode(post.Title);

            if (string.IsNullOrWhiteSpace(title))
            {
                _log.Info("No Reddit title available, skipping post {PostId}", post.Id);
                return;
            }

            using var session = store.OpenSession();
            var blogConfig = session.Load<BlogConfig>("Blog/Config");

            var subreddits = RedditHelper.ParseSubreddits(blogConfig);
            if (subreddits.Count == 0)
            {
                _log.Info("No subreddits configured, skipping Reddit post {PostId}", post.Id);
                return;
            }

            if (string.IsNullOrEmpty(blogConfig?.RedditUser) ||
                string.IsNullOrEmpty(blogConfig?.RedditPassword) ||
                string.IsNullOrEmpty(blogConfig?.RedditClientAppId) ||
                string.IsNullOrEmpty(blogConfig?.RedditClientSecret))
            {
                _log.Info("Reddit not fully configured, skipping post {PostId}", post.Id);
                return;
            }

            var postUrl = PostHelper.Url(post);

            RedditSharp.Reddit reddit;
            try
            {
                var agent = new RedditSharp.BotWebAgent(
                    blogConfig.RedditUser,
                    blogConfig.RedditPassword,
                    blogConfig.RedditClientAppId,
                    blogConfig.RedditClientSecret,
                    "http://localhost");
                reddit = new RedditSharp.Reddit(agent);
            }
            catch (Exception e)
            {
                _log.Error(e, "Reddit authentication failed for {PostId}", post.Id);
                EnqueueSocialFailureEmail(store, post, "Reddit", "", e.ToString());
                return;
            }

            foreach (var subredditName in subreddits)
            {
                try
                {
                    var subreddit = await reddit.GetSubredditAsync(subredditName);
                    await subreddit.SubmitPostAsync(title, postUrl, resubmit: false);
                    _log.Info("Submitted post {PostId} to {Subreddit}", post.Id, subredditName);
                }
                catch (RedditSharp.DuplicateLinkException)
                {
                    _log.Info("Post {PostId} already submitted to {Subreddit}", post.Id, subredditName);
                }
                catch (Exception e)
                {
                    _log.Error(e, "Failed to submit post {PostId} to {Subreddit}", post.Id, subredditName);
                    EnqueueSocialFailureEmail(store, post, "Reddit", subredditName, e.ToString());
                }
            }
        }
```

- [ ] **Step 4: Await TryPostToReddit in ProcessPost**

`ProcessPost` is already `async Task`. Update its Reddit call site so the now-async method is awaited:

```csharp
            if (hasReddit)
                await TryPostToReddit(store, post);
```

(Replace the existing `TryPostToReddit(store, post);` line. The `TryPostToTwitter` call below it already uses `await`.)

- [ ] **Step 5: Build**

Run: `dotnet build RaccoonBlog.Web/RaccoonBlog.Web.csproj`
Expected: `Build succeeded. 0 Error(s)`. (Confirms the RedditSharp types, `async` signature, and helper all compile.)

- [ ] **Step 6: Manual verification (integration — no automated coverage possible)**

This path calls the live Reddit API and RavenDB, so it cannot be unit-tested. Verify manually:
1. In RavenDB Studio set `Blog/Config` `RedditUser`/`RedditPassword`/`RedditClientAppId`/`RedditClientSecret` and `RedditSubredditsToSubmitToOnPublish` (e.g. a test subreddit you moderate).
2. Publish a post tagged `@social/reddit` and confirm the `social-media` GenAI task sets `Social.RedditTitle`/`Social.GeneratedAt`.
3. Confirm the link appears in the subreddit and the log shows `Submitted post ... to ...`.
4. Temporarily set a bad `RedditClientSecret`; republish; confirm a `EmailCommands/social-failure-*` document is created and the owner receives an email whose `<pre>` block contains the full stack trace.

- [ ] **Step 7: Commit**

```bash
git add RaccoonBlog.Web/Infrastructure/SocialPostingSubscription.cs RaccoonBlog.Web/Program.cs
git commit -m "Implement Reddit posting via RedditSharp 2.0 with failure notifications"
```

---

### Task 3: Twitter failure notifications

**Files:**
- Modify: `RaccoonBlog.Web/Infrastructure/SocialPostingSubscription.cs` (`TryPostToTwitter`)

**Interfaces:**
- Consumes: `EnqueueSocialFailureEmail(IDocumentStore, Post, string, string, string)` (Task 2).
- Produces: none.

- [ ] **Step 1: Notify on the non-success HTTP branch**

In `TryPostToTwitter`, the response is checked with `if (response.IsSuccessStatusCode)`. Replace the existing success/else block so the failure branch reads the response body and enqueues an email:

```csharp
                if (response.IsSuccessStatusCode)
                {
                    _log.Info("Tweet posted for {PostId}", post.Id);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var detail = $"Twitter API returned {(int)response.StatusCode} {response.StatusCode}.\nResponse body:\n{body}";
                    _log.Warn("Twitter API error for {PostId}: {Status}", post.Id, response.StatusCode);
                    EnqueueSocialFailureEmail(store, post, "Twitter", "", detail);
                }
```

- [ ] **Step 2: Notify on the exception branch**

In the same method, update the `catch (Exception e)` block to enqueue an email with the full trace:

```csharp
            catch (Exception e)
            {
                _log.Error(e, "Failed to post tweet for {PostId}", post.Id);
                EnqueueSocialFailureEmail(store, post, "Twitter", "", e.ToString());
            }
```

- [ ] **Step 3: Build**

Run: `dotnet build RaccoonBlog.Web/RaccoonBlog.Web.csproj`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Manual verification (integration)**

With a deliberately invalid `TwitterBearerToken`, publish a `@social/twitter`-tagged post and confirm a `EmailCommands/social-failure-*` document is created whose `ErrorMessage` contains the Twitter API status code and response body.

- [ ] **Step 5: Full solution build + test sweep**

Run: `dotnet build RaccoonBlog.sln`
Expected: `Build succeeded. 0 Error(s)`.
Run: `dotnet test RaccoonBlog.IntegrationTests --filter "FullyQualifiedName~AutoMapperConfigurationTester|FullyQualifiedName~SocialPostingFailureTemplateTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add RaccoonBlog.Web/Infrastructure/SocialPostingSubscription.cs
git commit -m "Email owner on Twitter posting failures with full error detail"
```

---

## Notes for the implementer

- The `social-posting` subscription query is unchanged; Reddit posting is gated by the `@social`/`@social/reddit` tag logic already in `ProcessPost` (`hasReddit`).
- `EnqueueSocialFailureEmail` writes only an `EmailCommands` document (separate collection) — it must never modify or `Store` the `Post`, or it would re-trigger the Posts subscription and re-run Twitter/Reddit.
- Reddit idempotency is intentionally external: `SubmitPostAsync(resubmit: false)` throws `DuplicateLinkException` for an already-submitted URL, which we swallow. Nothing is written to `Post.Integration.Reddit`; that model remains used only by the disabled `#if FALSE` `SubmitToRedditStrategy`.
- The `redirectUri` argument to `BotWebAgent` is unused by the password grant; the constant `"http://localhost"` is fine.
```
