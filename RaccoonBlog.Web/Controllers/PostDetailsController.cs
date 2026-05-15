using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Infrastructure.Indexes;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RaccoonBlog.Web.Controllers
{
    public partial class PostDetailsController : RaccoonController
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private readonly IServiceProvider _serviceProvider;
        private readonly Recaptcha2Helper _recaptcha2Helper;

        public PostDetailsController(IServiceProvider serviceProvider, IDocumentStore documentStore, IDocumentSession ravenSession, Recaptcha2Helper recaptcha2Helper) : base(documentStore, ravenSession)
        {
            _serviceProvider = serviceProvider;
            _recaptcha2Helper = recaptcha2Helper;
        }

        public virtual IActionResult Details(string id, string slug, Guid key)
        {
            var post = RavenSession
                .Include<Post>(x => x.CommentsId)
                .Include(x => x.AuthorId)
                .Load("posts/" + id);

            if (post == null)
                return NotFound();

            if (post.IsPublicPost(key) == false)
                return NotFound();

            SeriesInfo seriesInfo = GetSeriesInfo(post.Title);

            var nowAsMinutes = DateTimeOffset.Now.AsMinutes();
            var tagsToSearch = post.Tags ?? Array.Empty<string>();

            var related = RavenSession.Query<Posts_ByVector.Query, Posts_ByVector>()
                                      .Where(p => p.PublishAt < DateTimeOffset.Now.AsMinutes())
                                      .VectorSearch(x => x.WithField(p => p.Vector), x => x.ForDocument(post.Id))
                                      .Take(3)
                                      .Skip(1) // skip the current post, always the best match :-)
                                      .Select(p => new PostReference { Id = p.Id, Title = p.Title, PublishedAt = p.PublishAt, Tags = p.Tags})
                                      .ToList();

            var comments = RavenSession.Load<PostComments>(post.CommentsId) ?? new PostComments();

            var currentCommenterId = GetCurrentCommenterId();
            var isAuthenticated = User.Identity.IsAuthenticated;

            var visibleComments = comments.Comments
                .Where(c =>
                    c.SpamCheckStatus != SpamCheckStatus.Pending ||
                    isAuthenticated ||
                    (!string.IsNullOrEmpty(currentCommenterId) && c.CommenterId == currentCommenterId))
                .OrderBy(x => x.CreatedAt)
                .MapTo<PostViewModel.Comment>();

            var vm = new PostViewModel
            {
                Post = post.MapTo<PostViewModel.PostDetails>(),
                Comments = visibleComments,
                NextPost = RavenSession.GetNextPrevPost(post, true),
                PreviousPost = RavenSession.GetNextPrevPost(post, false),
                AreCommentsClosed = comments.AreCommentsClosed(post, BlogConfig.NumberOfDayToCloseComments),
                SeriesInfo = seriesInfo,
                Related = related
            };

            vm.Post.Author = RavenSession.Load<User>(post.AuthorId).MapTo<PostViewModel.UserDetails>();

            CommentInput comment = null;
            if (TempData["new-comment"] != null)
            {
                var rawComment = TempData["new-comment"];
                if (rawComment is Newtonsoft.Json.Linq.JObject jObject)
                {
                    comment = jObject.ToObject<CommentInput>();
                }
                else if (rawComment is CommentInput ci)
                {
                    comment = ci;
                }
            }

            if (comment != null)
            {
                var newCommentEmailHash = EmailHashResolver.Resolve(comment.Email);
                var newCommentContent = MarkdownResolver.Resolve(comment.Body);
                
                var newCommentContentString = newCommentContent ?? string.Empty;
                
                if (vm.Comments.Any(x =>
                    x.Author == comment.Name
                    && x.EmailHash == newCommentEmailHash
                    && x.Body.ToString() == newCommentContentString) == false)
                {
                    vm.Comments.Add(new PostViewModel.Comment
                    {
                        CreatedAt = DateTimeOffset.Now.UtcDateTime.ToString(),
                        Author = comment.Name,
                        Body = new Microsoft.AspNetCore.Html.HtmlString(newCommentContent),
                        Id = -1,
                        Url = UrlResolver.Resolve(comment.Url),
                        Tooltip = "Comment by " + comment.Name,
                        EmailHash = newCommentEmailHash
                    });
                }
            }

            if (vm.Post.Slug != slug)
                return RedirectToActionPermanent("Details", new { id, vm.Post.Slug });

            SetWhateverUserIsTrustedCommenter(vm);

            return View("Details", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<IActionResult> Comment(CommentInput input, string id, Guid key)
        {
            if (ModelState.IsValid == false)
                return RedirectToAction("Details");

            if (IsIpAddressBlocked())
                return StatusCode(StatusCodes.Status402PaymentRequired);

            var post = RavenSession
                .Include<Post>(x => x.CommentsId)
                .Load("posts/" + id);

            if (post == null || post.IsPublicPost(key) == false)
                return NotFound();

            var comments = RavenSession.Load<PostComments>(post.CommentsId);
            if (comments == null)
                return NotFound();

            var commenter = RavenSession.GetCommenter(input.CommenterKey);
            if (commenter == null)
            {
                input.CommenterKey = Guid.NewGuid().ToString();
            }

            ValidateCommentsAllowed(post, comments);
            await ValidateCaptcha(input, commenter);

            if (ModelState.IsValid == false)
                return PostingCommentFailed(post, input, key);

            // Silently reject comments from repeat spam offenders
            // Show success to the user but don't actually save the comment
            if (commenter != null && commenter.NumberOfSpamComments > 4 && !User.Identity.IsAuthenticated)
            {
                CommenterUtil.SetCommenterCookie(Response, input.CommenterKey);
                return PostingCommentSucceeded(post, input);
            }

            // Create or update commenter
            var newCommenter = commenter ?? new Commenter { Key = Guid.Parse(input.CommenterKey) };
            newCommenter.IsTrustedCommenter = commenter?.IsTrustedCommenter;
            input.MapPropertiesToInstance(newCommenter);
            RavenSession.Store(newCommenter);

            // Create comment inline with Pending status for GenAI spam check
            var comment = new PostComments.Comment
            {
                Id = comments.GenerateNewCommentId(),
                Author = input.Name,
                Body = input.Body,
                CreatedAt = DateTimeOffset.Now,
                CommenterId = newCommenter.Id,
                Email = input.Email,
                Url = input.Url,
                Important = User.Identity.IsAuthenticated,
                UserHostAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].FirstOrDefault(),
                IsSpam = false,
                SpamCheckStatus = SpamCheckStatus.Pending
            };

            comments.Comments.Add(comment);
            post.CommentsCount = comments.Comments.Count;

            RavenSession.SaveChanges();

            CommenterUtil.SetCommenterCookie(Response, input.CommenterKey);

            return PostingCommentSucceeded(post, input);
        }

        private string GetCurrentCommenterId()
        {
            if (Request.Cookies.TryGetValue(CommenterUtil.CommenterCookieName, out var cookieValue))
            {
                var commenter = RavenSession.GetCommenter(cookieValue);
                return commenter?.Id;
            }
            return null;
        }

        private bool IsIpAddressBlocked()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            var blacklistId = BlackList.GetId(ip);

            var documentExists = RavenSession.Advanced.Exists(blacklistId);
            return documentExists;
        }

        private IActionResult PostingCommentSucceeded(Post post, CommentInput input)
        {
            const string successMessage = "Your comment will be posted soon. Thanks!";
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { Success = true, message = successMessage });

            TempData["new-comment"] = input;
            var postReference = post.MapTo<PostReference>();

            return Redirect(Url.Action("Details",
                new { Id = postReference.DomainId, postReference.Slug, key = post.ShowPostEvenIfPrivate }) + "#comments-form-location");
        }

        private void ValidateCommentsAllowed(Post post, PostComments comments)
        {
            if (comments.AreCommentsClosed(post, BlogConfig.NumberOfDayToCloseComments))
                ModelState.AddModelError("CommentsClosed", "This post is closed for new comments.");
            if (post.AllowComments == false)
                ModelState.AddModelError("CommentsClosed", "This post does not allow comments.");
        }

        private async Task ValidateCaptcha(CommentInput input, Commenter commenter)
        {
            if (User.Identity.IsAuthenticated ||
                (commenter != null && commenter.IsTrustedCommenter == true))
                return;

            await _recaptcha2Helper.Validate(ModelState).ConfigureAwait(false);
        }

        private IActionResult PostingCommentFailed(Post post, CommentInput input, Guid key)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { Success = false, message = ModelState.FirstErrorMessage() });

            var postReference = post.MapTo<PostReference>();
            var result = Details(postReference.DomainId, postReference.Slug, key);
            var model = result as ViewResult;
            if (model != null)
            {
                var viewModel = model.Model as PostViewModel;
                if (viewModel != null)
                    viewModel.Input = input;
            }
            return result;
        }

        private void SetWhateverUserIsTrustedCommenter(PostViewModel vm)
        {
            if (User.Identity.IsAuthenticated)
            {
                var user = RavenSession.GetCurrentUser(User);
                vm.Input = user.MapTo<CommentInput>();
                vm.IsTrustedCommenter = true;
                vm.IsLoggedInCommenter = true;
                return;
            }

            if (Request.Cookies.TryGetValue(CommenterUtil.CommenterCookieName, out var cookieValue))
            {
                _log.Debug("Cookie '" + CommenterUtil.CommenterCookieName + "': " + cookieValue);

                var commenter = RavenSession.GetCommenter(cookieValue);
                if (commenter == null)
                {
                    _log.Debug("Could not find commenter for '" + CommenterUtil.CommenterCookieName + "': " + cookieValue);
                    vm.IsLoggedInCommenter = false;
                    Response.Cookies.Delete(CommenterUtil.CommenterCookieName);
                    return;
                }

                vm.IsLoggedInCommenter = string.IsNullOrWhiteSpace(commenter.OpenId) == false;
                _log.Debug("Commenter OpenId: " + commenter.OpenId);
                vm.Input = commenter.MapTo<CommentInput>();
                vm.IsTrustedCommenter = commenter.IsTrustedCommenter == true;
            }
        }

        private SeriesInfo GetSeriesInfo(string title)
        {
            SeriesInfo seriesInfo = null;
            string seriesTitle = TitleConverter.ToSeriesTitle(title);

            if (!string.IsNullOrEmpty(seriesTitle))
            {
                var series = RavenSession.Query<Posts_Series.Result, Posts_Series>()
                    .Where(x => x.Series.StartsWith(seriesTitle) && x.Count > 1)
                    .OrderByDescending(x => x.MaxDate)
                    .FirstOrDefault();

                if (series == null)
                    return null;

                var postsInSeries = GetPostsForCurrentSeries(series);

                seriesInfo = new SeriesInfo
                {
                    SeriesId = series.SeriesId,
                    SeriesTitle = seriesTitle,
                    PostsInSeries = postsInSeries
                };
            }

            return seriesInfo;
        }

        private IList<PostInSeries> GetPostsForCurrentSeries(Posts_Series.Result series)
        {
            IList<PostInSeries> postsInSeries = null;

            if (series != null)
            {
                postsInSeries = series
                    .Posts
                    .Select(s => new PostInSeries
                    {
                        Id = Post.GetIdForUrl(s.Id),
                        Slug = SlugConverter.TitleToSlug(s.Title),
                        Title = System.Net.WebUtility.HtmlDecode(TitleConverter.ToPostTitle(s.Title)),
                        PublishAt = s.PublishAt
                    })
                    .OrderByDescending(p => p.PublishAt)
                    .ToList();
            }

            return postsInSeries;
        }
    }
}