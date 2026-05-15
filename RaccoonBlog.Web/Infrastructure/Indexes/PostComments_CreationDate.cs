using System;
using System.Linq;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents.Indexes;

namespace RaccoonBlog.Web.Infrastructure.Indexes
{
	public class PostComments_CreationDate : AbstractIndexCreationTask<PostComments, PostComments_CreationDate.Result>
	{
		public class Result
		{
			public DateTimeOffset CreatedAt { get; set; }
			public int CommentId { get; set; }
			public string PostCommentsId { get; set; }
			public string PostId { get; set; }
			public DateTimeOffset PostPublishAt { get; set; }
		}

		public PostComments_CreationDate()
		{
		    Map = postComments => from postComment in postComments
		        from comment in postComment.Comments
		        where comment.IsSpam == false && comment.SpamCheckStatus != SpamCheckStatus.Pending
		        select new
		        {
		            comment.CreatedAt,
		            CommentId = comment.Id,
		            PostCommentsId = postComment.Id,
		            PostId = postComment.Post.Id,
		            PostPublishAt = postComment.Post.PublishAt
		        };

			Store(x => x.CreatedAt, FieldStorage.Yes);
			Store(x => x.CommentId, FieldStorage.Yes);
			Store(x => x.PostId, FieldStorage.Yes);
			Store(x => x.PostCommentsId, FieldStorage.Yes);
			Store(x => x.PostPublishAt, FieldStorage.Yes);
		}
	}
}