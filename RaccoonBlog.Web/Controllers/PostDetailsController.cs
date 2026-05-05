using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Infrastructure.Indexes;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;
using Raven.Client.Documents;

namespace RaccoonBlog.Web.Controllers
{
    public partial class PostDetailsController : RaccoonController
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public virtual ActionResult Details(string id, string slug, Guid key)
        {
            var post = RavenSession
                .Include<Post>(x => x.CommentsId)
                .Include(x => x.AuthorId)
                .Load("posts/" + id);

            if (post == null)
                return HttpNotFound();

            if (post.IsPublicPost(key) == false)
                return HttpNotFound();

            SeriesInfo seriesInfo = GetSeriesInfo(post.Title);

            var related = RavenSession.Query<Posts_ByVector.Query, Posts_ByVector>()
                .Where(p => p.PublishAt < DateTimeOffset.Now.AsMinutes())
                .VectorSearch(x => x.WithField(p => p.Vector), x => x.ForDocument(post.Id))
                .Take(3)
                .Skip(1)
                .Select(p => new PostReference { Id = p.Id, Title = p.Title, PublishedAt = p.PublishAt, Tags = p.Tags})
                .ToList();

            var comments = RavenSession.Load<PostComments>(post.CommentsId) ?? new PostComments();

            var currentCommenterId = GetCurrentCommenterId();

            var visibleComments = comments.Comments
                .Where(c => c.SpamCheckStatus != SpamCheckStatus.Pending
                            || Request.IsAuthenticated
                            || (currentCommenterId != null && c.CommenterId == currentCommenterId))
                .OrderBy(x => x.CreatedAt)
                .ToList();

            var vm = new PostViewModel
            {
                Post = post.MapTo<PostViewModel.PostDetails>(),
                Comments = visibleComments.MapTo<PostViewModel.Comment>(),
                NextPost = RavenSession.GetNextPrevPost(post, true),
                PreviousPost = RavenSession.GetNextPrevPost(post, false),
                AreCommentsClosed = comments.AreCommentsClosed(post, BlogConfig.NumberOfDayToCloseComments),
                SeriesInfo = seriesInfo,
                Related = related
            };

            vm.Post.Author = RavenSession.Load<User>(post.AuthorId).MapTo<PostViewModel.UserDetails>();

            var comment = TempData["new-comment"] as CommentInput;

            if (comment != null)
            {
                var newCommentEmailHash = EmailHashResolver.Resolve(comment.Email);
                var newCommentContent = MarkdownResolver.Resolve(comment.Body);
                if (vm.Comments.Any(x =>
                    x.Author == comment.Name
                    && x.EmailHash == newCommentEmailHash
                    && x.Body.ToString() == newCommentContent.ToString()) == false)
                {
                    vm.Comments.Add(new PostViewModel.Comment
                    {
                        CreatedAt = DateTimeOffset.Now.UtcDateTime.ToString(),
                        Author = comment.Name,
                        Body = newCommentContent,
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

        private string GetCurrentCommenterId()
        {
            var cookie = Request.Cookies[CommenterUtil.CommenterCookieName];
            if (cookie == null)
                return null;

            var commenter = RavenSession.GetCommenter(cookie.Value);
            return commenter?.Id;
        }

        [ValidateInput(false)]
        [HttpPost]
        public virtual async Task<ActionResult> Comment(CommentInput input, string id, Guid key)
        {
            if (ModelState.IsValid == false)
                return RedirectToAction("Details");

            if (IsIpAddressBlocked())
                return new HttpStatusCodeResult(HttpStatusCode.PaymentRequired);

            var post = RavenSession
                .Include<Post>(x => x.CommentsId)
                .Load("posts/" + id);

            if (post == null || post.IsPublicPost(key) == false)
                return HttpNotFound();

            var comments = RavenSession.Load<PostComments>(post.CommentsId);
            if (comments == null)
                return HttpNotFound();

            var commenter = RavenSession.GetCommenter(input.CommenterKey);
            if (commenter == null)
            {
                input.CommenterKey = Guid.NewGuid();
            }

            ValidateCommentsAllowed(post, comments);
            await ValidateCaptcha(input, commenter);

            if (ModelState.IsValid == false)
                return PostingCommentFailed(post, input, key);

            var comment = new PostComments.Comment
            {
                Id = comments.GenerateNewCommentId(),
                Author = input.Name,
                Body = input.Body,
                CreatedAt = DateTimeOffset.Now,
                Email = input.Email,
                Url = input.Url,
                Important = Request.IsAuthenticated,
                UserAgent = Request.UserAgent,
                UserHostAddress = Request.UserHostAddress,
                SpamCheckStatus = SpamCheckStatus.Pending,
            };

            if (Request.IsAuthenticated == false)
            {
                commenter ??= new Commenter { Key = input.CommenterKey ?? Guid.Empty };
                input.MapPropertiesToInstance(commenter);
                DocumentSession.Store(commenter);
                comment.CommenterId = commenter.Id;
            }

            post.CommentsCount++;
            comments.Comments.Add(comment);

            CommenterUtil.SetCommenterCookie(Response, input.CommenterKey.MapTo<string>());
            OutputCacheManager.RemoveItem(SectionController.NameConst, MVC.Section.ActionNames.List);

            return PostingCommentSucceeded(post, input);
        }

        private bool IsIpAddressBlocked()
        {
            var ip = Request.UserHostAddress;
            var blacklistId = BlackList.GetId(ip);
            return RavenSession.Advanced.Exists(blacklistId);
        }

        private ActionResult PostingCommentSucceeded(Post post, CommentInput input)
        {
            const string successMessage = "Your comment will be posted soon. Thanks!";
            if (Request.IsAjaxRequest())
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
            if (Request.IsAuthenticated ||
                (commenter != null && commenter.IsTrustedCommenter == true))
                return;

            await Recaptcha2Helper.Validate(ModelState).ConfigureAwait(false);
        }

        private ActionResult PostingCommentFailed(Post post, CommentInput input, Guid key)
        {
            if (Request.IsAjaxRequest())
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
            if (Request.IsAuthenticated)
            {
                var user = RavenSession.GetCurrentUser();
                vm.Input = user.MapTo<CommentInput>();
                vm.IsTrustedCommenter = true;
                vm.IsLoggedInCommenter = true;
                return;
            }

            var cookie = Request.Cookies[CommenterUtil.CommenterCookieName];
            _log.Debug("Cookie '" + CommenterUtil.CommenterCookieName + "': " + cookie);

            if (cookie == null) return;

            var commenter = RavenSession.GetCommenter(cookie.Value);
            if (commenter == null)
            {
                _log.Debug("Could not find commenter for '" + CommenterUtil.CommenterCookieName + "': " + cookie.Value);
                vm.IsLoggedInCommenter = false;
                Response.Cookies.Set(new HttpCookie(CommenterUtil.CommenterCookieName) { Expires = DateTime.Now.AddYears(-1) });
                return;
            }

            vm.IsLoggedInCommenter = string.IsNullOrWhiteSpace(commenter.OpenId) == false;
            _log.Debug("Commenter OpenId: " + commenter.OpenId);
            vm.Input = commenter.MapTo<CommentInput>();
            vm.IsTrustedCommenter = commenter.IsTrustedCommenter == true;
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
                        Title = HttpUtility.HtmlDecode(TitleConverter.ToPostTitle(s.Title)),
                        PublishAt = s.PublishAt
                    })
                    .OrderByDescending(p => p.PublishAt)
                    .ToList();
            }

            return postsInSeries;
        }
    }
}
