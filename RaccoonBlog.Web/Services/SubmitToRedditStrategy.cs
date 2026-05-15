using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.Services.Reddit;
using RaccoonBlog.Web.ViewModels;
using Raven.Client.Documents.Session;
using RedditSharp;
using RedditSharp.Things;
using Post = RaccoonBlog.Web.Models.Post;
using PostSubmission = RaccoonBlog.Web.Models.SocialNetwork.Reddit.PostSubmission;
using SubmissionStatus = RaccoonBlog.Web.Models.SocialNetwork.Reddit.SubmissionStatus;

namespace RaccoonBlog.Web.Services
{
    public class SubmitToRedditStrategy
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private RedditSharp.Reddit _reddit;

        private Credentials _credentials;

        private IList<string> _subredditNames;

        private readonly IDocumentSession _session;

        public SubmitToRedditStrategy(IDocumentSession session)
        {
            _session = session;
            var blogConfig = _session.Load<BlogConfig>(BlogConfig.Key);
            _credentials = RedditHelper.GetCredentials(blogConfig);
            _subredditNames = RedditHelper.ParseSubreddits(blogConfig);

            InitializeRedditService();
        }

        private void InitializeRedditService()
        {
            _reddit = new RedditSharp.Reddit(WebAgent.RateLimitMode.SmallBurst);
            _reddit.CaptchaSolver = new FailingCaptchaSolver();
        }

        public void SubmitPostsToReddit(DateTimeOffset currentDateTimeOffset)
        {
            if (_subredditNames.Count == 0)
            {
                _log.Info("No subreddits to submit to configured.");
                return;
            }

            if (_credentials.IsValid == false)
            {
                _log.Info("Reddit credentials config options are invalid. Check your blog config.");
                return;
            }

            _log.Info($"Submitting #{RedditHelper.SendToRedditTag} posts to reddit.");

            var posts = RedditHelper.GetPostsForAutomaticRedditSubmission(_session, currentDateTimeOffset);

            if (posts.Any() == false)
            {
                _log.Info("No posts to submit to Reddit.");
                return;
            }

            _reddit.LogIn(_credentials.User, _credentials.Password);

            SubmitPosts(posts);
        }

        private void SubmitPosts(IList<Post> posts)
        {
            var subreddits = _subredditNames.Select(sr => _reddit.GetSubreddit(sr)).ToList();

            foreach (var post in posts)
            {
                InitRedditIntegration(post);

                foreach (var subreddit in subreddits)
                {
                    SubmitPostToSubreddit(post, subreddit);
                }
            }
        }

        private void HandleManualSubmission(Subreddit subreddit, Post post)
        {
            _log.Info($"Post was manually submitted \"{post.Title}\" to /r/{subreddit.Name}. Verifying.");

            var postUrl = PostHelper.Url(post);
            var posts = subreddit.Search($"url:{postUrl}");
            var postSubmission = post.Integration.Reddit.GetPostSubmissionForSubreddit(subreddit.Name);
            if (posts.Any())
            {
                _log.Info($"Post \"{post.Title}\" found in /r/{subreddit.Name}.");

                postSubmission.Status = SubmissionStatus.Submitted;
                return;
            }

            postSubmission.Attempts++;

            _log.Warn($"Post \"{post.Title}\" not found in /r/{subreddit.Name}.");
            if (postSubmission.Attempts >= 3)
            {
                postSubmission.Status = SubmissionStatus.ManualSubmissionFailure;
                postSubmission.Attempts = 0;
            }
        }

        private void SubmitPostToSubreddit(Post post, Subreddit subreddit)
        {
            var redditIntegration = post.Integration.Reddit;
            var postSubmission = redditIntegration.GetPostSubmissionForSubreddit(subreddit.Name) ??
                new PostSubmission()
                {
                    Subreddit = subreddit.Name
                };


            if (postSubmission.IsManualSubmissionPending)
            {
                HandleManualSubmission(subreddit, post);
                return;
            }

            if (postSubmission.ShouldTrySubmit == false)
                return;

            _log.Info($"Submitting \"{post.Title}\" to /r/{subreddit.Name}.");

            try
            {
                var title = post.Social?.RedditTitle ?? HttpUtility.HtmlDecode(post.Title);
                var redditPost = subreddit.SubmitPost(title, PostHelper.Url(post));
                if (redditPost == null)
                {
                    throw new Exception($"Got null from reddit submit for {post.Id}");
                }

                postSubmission.Status = SubmissionStatus.Submitted;

                _log.Info(
                    $"Post \"{post.Title}\" with ID {post.Id} has been successfully submitted to /r/{subreddit.Name} with permalink {redditPost.Permalink}.");
            }
            catch (DuplicateLinkException duplicateLinkException)
            {
                _log.InfoException($"Link to post \"{post.Title}\" with ID {post.Id} already submitted to Reddit.",
                    duplicateLinkException);
                postSubmission.Status = SubmissionStatus.Submitted;
            }
            catch (CaptchaFailedException)
            {
                postSubmission.Status = SubmissionStatus.CaptchaFailure;
            }
            catch (Exception exception)
            {
                _log.ErrorException($"Error submitting post \"{post.Title}\" with ID {post.Id} to /r/{subreddit.Name}", exception);
                postSubmission.Status = SubmissionStatus.UnknownFailure;
            }

            redditIntegration.RegisterPostSubmission(postSubmission);

            NotifyOnFailure(post, postSubmission);
        }

        private void NotifyOnFailure(Post post, PostSubmission postSubmission)
        {
            if (postSubmission.IsFailure == false)
                return;

            try
            {
                var subject = $"Failed submitting post to /r/{postSubmission.Subreddit}";
                var author = _session.Load<User>(post.AuthorId);

                var command = new SendEmailCommand
                {
                    Type = "RedditFailure",
                    View = "RedditSubmissionFailed",
                    Subject = subject,
                    SendTo = author.Email,
                    ReplyTo = null,
                    ModelJson = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new
                    {
                        Post = new
                        {
                            Id = post.GetIdForUrl(),
                            Title = HttpUtility.HtmlDecode(post.Title),
                            Slug = SlugConverter.TitleToSlug(post.Title)
                        },
                        Submission = new
                        {
                            Subreddit = postSubmission.Subreddit,
                            Status = postSubmission.Status.ToString(),
                            Attempts = postSubmission.Attempts
                        }
                    })
                };

                _session.Store(command);
                _session.Advanced.GetMetadataFor(command)["@collection"] = "EmailCommands";
                _log.Info("Queued Reddit failure email for: {Subject}", subject);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error notifying on Reddit submission failures.");
            }
        }

        private static void InitRedditIntegration(Post post)
        {
            if (post.Integration == null)
                post.Integration = new SocialNetworkIntegration();

            if (post.Integration.Reddit == null)
                post.Integration.Reddit = new Models.SocialNetwork.Reddit();

            if (post.Integration.Reddit.PostSubmissions == null)
                post.Integration.Reddit.PostSubmissions = new HashSet<PostSubmission>();
        }
    }
}
