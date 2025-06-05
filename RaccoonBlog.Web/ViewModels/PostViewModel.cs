using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace RaccoonBlog.Web.ViewModels
{
    public class PostViewModel
    {
        public PostReference PreviousPost { get; set; }
        public PostReference NextPost { get; set; }

        public PostDetails Post { get; set; }
        public IList<Comment> Comments { get; set; }
        public SeriesInfo SeriesInfo { get; set; }
        public CommentInput Input { get; set; }

        public bool AreCommentsClosed { get; set; }
        public bool IsTrustedCommenter { get; set; }
        public bool IsLoggedInCommenter { get; set; }

        public List<PostReference> Related { get; set; }

        public class Comment
        {
            public int Id { get; set; }
            public MvcHtmlString Body { get; set; }
            public string Author { get; set; }
            public string Tooltip { get; set; }
            public string Url { get; set; }    // Look for HTML injection.
            public string EmailHash { get; set; }
            public string CreatedAt { get; set; }
            public bool IsImportant { get; set; }
        }

        public class PostDetails
        {
            public string Id { get; set; }
            public Guid ShowPostEvenIfPrivate { get; set; }
            public string Title { get; set; }
            public string Slug { get; set; }
            public MvcHtmlString Body { get; set; }

            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset PublishedAt { get; set; }
            public bool IsCommentAllowed { get; set; }

            public ICollection<TagDetails> Tags { get; set; }

            public UserDetails Author { get; set; }
        }

        public class UserDetails
        {
            public string FullName { get; set; }

            public string TwitterNick { get; set; }
            public string RelatedTwitterNick { get; set; }
            public string RelatedTwitNickDes { get; set; }
        }


        public bool IsSeriesArticle()
        {
            return SeriesInfo != null &&
                   SeriesInfo.PostsInSeries != null;
        }

        public PostInSeries GetPreviousPostInSeries()
        {
            if (IsSeriesArticle() == false)
                return null;

            var postsInSeries = FilterPostsInSeries();
            var postIndexInSeries = GetCurrentPostIndexInSeries(postsInSeries);
            if (postIndexInSeries <= 0 || postsInSeries.Count <= 1)
                return null;

            return postsInSeries[postIndexInSeries - 1];
        }

        public PostInSeries GetNextPostInSeries()
        {
            if (IsSeriesArticle() == false)
                return null;

            var postsInSeries = FilterPostsInSeries();

            var postIndexInSeries = GetCurrentPostIndexInSeries(postsInSeries);
            var nextPostIndexInSeries = postIndexInSeries + 1;
            if (nextPostIndexInSeries >= postsInSeries.Count)
                return null;

            return postsInSeries[nextPostIndexInSeries];
        }

        private List<PostInSeries> FilterPostsInSeries()
        {
            return SeriesInfo
                .PostsInSeries
                .OrderBy(x => x.PublishAt)
                .Where(x => x.PublishAt <= DateTimeOffset.UtcNow)
                .ToList();
        }

        private int GetCurrentPostIndexInSeries(List<PostInSeries> postsInSeries)
        {
            var currentPostId = Post.Id;
            for (var index = 0; index < postsInSeries.Count; index++)
            {
                var postInSeries = postsInSeries[index];
                if (postInSeries.Id == currentPostId)
                    return index;
            }

            return 0;
        }
    }
}
