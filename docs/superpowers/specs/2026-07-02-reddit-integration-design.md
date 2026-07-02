# Reddit Integration + Social Posting Failure Notifications

**Date:** 2026-07-02
**Branch:** HRINT-4790-seo-analysis-rebased
**Status:** Approved design

## Goal

Re-enable automatic Reddit posting under the new subscription-based social architecture
(`SocialPostingSubscription`), using RedditSharp 2.0. Consume the `Social.RedditTitle`
already produced by the `social-media` GenAI task. Additionally, notify the blog owner by
email when a social post (Twitter **or** Reddit) fails, including the full exception detail.

## Context

- Live social posting runs in `RaccoonBlog.Web/Infrastructure/SocialPostingSubscription.cs`
  via a RavenDB data subscription over `Posts` where `Social.GeneratedAt != null`, the post
  is not disabled, and no `@refresh` is pending. `ProcessPost` is `async` and already calls
  `TryPostToReddit` (currently a stub) when the post carries a `@social`/`@social/reddit` tag.
- The `social-media` GenAI task generates `Social.TwitterText` and `Social.RedditTitle`.
  `TwitterText` is posted; `RedditTitle` is currently generated but unused.
- `RedditSharp` 2.0.0 is already referenced. The old `SubmitToRedditStrategy` (FluentScheduler
  polling, captcha handling, per-subreddit retry/manual-submission state machine) is disabled
  behind `#if FALSE` and depends on deleted infrastructure. It is not being revived.
- `BlogConfig` already has `RedditUser`, `RedditPassword`, `RedditClientAppId`,
  `RedditClientSecret`, and `RedditSubredditsToSubmitToOnPublish`.
- Email now flows through `SendEmailCommand` documents (`EmailCommands` collection) consumed by
  `EmailSubscription`, which renders embedded Scriban HTML templates under
  `Infrastructure/EmailTemplates/`.

## RedditSharp 2.0 API (verified against the referenced 2.0.0 assembly)

- Auth: `new BotWebAgent(username, password, clientId, clientSecret, redirectUri)` (password
  grant). `redirectUri` is unused by the password grant; a constant is acceptable.
- Client: `new Reddit(IWebAgent agent)`.
- Subreddit: `await reddit.GetSubredditAsync(name, validateName)`.
- Submit link: `await subreddit.SubmitPostAsync(title, url, captchaId, captchaAnswer, resubmit)`.
- Relevant exceptions: `DuplicateLinkException`, `RateLimitException`, `RedditException`,
  `RedditHttpException`, `CaptchaFailedException`.

## Design

### Reddit posting (`TryPostToReddit`)

Convert the stub to `private static async Task TryPostToReddit(IDocumentStore store, Post post)`,
mirroring `TryPostToTwitter`:

1. Determine the title: `post.Social?.RedditTitle`, falling back to `WebUtility.HtmlDecode(post.Title)`.
   If both are empty, log and return.
2. Open a session; load `BlogConfig`. Parse subreddits via `RedditHelper.ParseSubreddits`.
   If no subreddits configured, log and return.
3. Validate credentials: `RedditUser`, `RedditPassword`, `RedditClientAppId`,
   `RedditClientSecret` all present. If any missing, log "Reddit not configured" and return.
4. Build `BotWebAgent` (constant `redirectUri`) and `new Reddit(agent)`.
5. For each subreddit:
   - `var sub = await reddit.GetSubredditAsync(name);`
   - `await sub.SubmitPostAsync(title, PostHelper.Url(post), null, null, false);`
   - Success → log info (with permalink if available).
   - `DuplicateLinkException` → treat as already-posted; log info; continue (natural idempotency).
   - Any other exception → log error and enqueue a failure email (see below); continue to the
     next subreddit.

**Idempotency:** stateless. `resubmit: false` makes Reddit reject duplicate URLs per subreddit
(`DuplicateLinkException`), which we treat as success. Nothing is persisted to the `Post`, so
there is no extra `SaveChanges`, no Posts-subscription re-fire, and no double-tweet risk.

### Failure notifications (Twitter + Reddit)

Shared private helper:

```
EnqueueSocialFailureEmail(IDocumentStore store, Post post, string network, string target, string errorDetail)
```

Opens a session, `Store`s a `SendEmailCommand`, and `SaveChanges`. It writes an `EmailCommands`
document only (separate collection), so it does not modify the `Post` and does not re-trigger
the Posts subscription.

`SendEmailCommand` gains three fields: `Network`, `Target`, `ErrorMessage`. The failure command:
- `Type = "SocialPostingFailure"`
- `Subject = $"Social posting to {network} failed: {post.Title}"`
- `Network`, `Target` (subreddit name for Reddit; empty for Twitter)
- `ErrorMessage` = full detail
- `PostId`, `PostTitle`, `PostSlug` for context
- `SendTo` left null → `EmailSubscription` routes to `OwnerEmail`.

**Full error detail:**
- Exceptions: `exception.ToString()` (type + message + full stack trace + inner exceptions).
- Twitter non-2xx HTTP response (no exception): compose from the status code plus the response
  body (`await response.Content.ReadAsStringAsync()`).

Wire-up:
- `TryPostToTwitter`: enqueue a failure email in the `catch (Exception)` block (full trace) and
  in the existing non-success-status branch (status + body). Logging behavior unchanged.
- `TryPostToReddit`: enqueue on any non-`DuplicateLinkException` failure, one email per failed
  subreddit, with `Target` = subreddit name.

### Email template

- New `Infrastructure/EmailTemplates/SocialPostingFailure.html`, embedded like the existing
  templates (already covered by the `EmailTemplates\*.html` glob), rendering: blog name, network,
  post title (linked), target subreddit (when present), and the full error detail inside a
  monospace `<pre>` block, HTML-encoded.
- `EmailSubscription.BuildEmailBody`: add `"SocialPostingFailure" => LoadTemplate("SocialPostingFailure.html")`
  and include `cmd.Network`, `cmd.Target`, `cmd.ErrorMessage` in the Scriban render object
  (Scriban lowercases to `network`, `target`, `error_message`).

## Components changed

| File | Change |
| ---- | ------ |
| `Infrastructure/SocialPostingSubscription.cs` | Implement `TryPostToReddit`; add failure-email enqueue to Twitter + Reddit; add `EnqueueSocialFailureEmail` helper |
| `Models/SendEmailCommand.cs` | Add `Network`, `Target`, `ErrorMessage` |
| `Infrastructure/EmailSubscription.cs` | Add `SocialPostingFailure` case + render fields |
| `Infrastructure/EmailTemplates/SocialPostingFailure.html` | New embedded template |

No model migration required (additive fields only). No subscription query change.

## Out of scope (YAGNI)

Captcha handling, manual-submission verification, retry-attempt counting, and the
`Post.Integration.Reddit` tracking model (still referenced only by the `#if FALSE` code).

## Testing / verification

- Build the solution; keep the AutoMapper config test green.
- Confirm `SocialPostingFailure.html` embeds (resource-name suffix load path is already validated).
- Manual: with Reddit credentials + a configured subreddit, tag a post `@social/reddit` and
  confirm submission; with bad credentials, confirm the owner receives a failure email containing
  the full stack trace.
