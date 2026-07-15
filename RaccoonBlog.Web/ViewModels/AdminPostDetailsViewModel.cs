using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Html;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web.ViewModels
{
	public class AdminPostDetailsViewModel
	{
		public PostReference PreviousPost { get; set; }
		public PostReference NextPost { get; set; }

		public PostDetails Post { get; set; }
		public IList<Comment> Comments { get; set; }

		public bool AreCommentsClosed { get; set; }

		public class Comment
		{
			public int Id { get; set; }
			public IHtmlContent Body { get; set; }
			public string Author { get; set; }
			public string Url { get; set; }    // Look for HTML injection.
			public string EmailHash { get; set; }
			public DateTimeOffset CreatedAt { get; set; }
			public bool IsImportant { get; set; }
			public Models.SpamCheckStatus SpamCheckStatus { get; set; }
			public string Reason { get; set; }
		}

		public class PostDetails
		{
			public string Id { get; set; }
			public IHtmlContent Title { get; set; }
			public string Slug { get; set; }
			public IHtmlContent Body { get; set; }

			public DateTimeOffset CreatedAt { get; set; }
			public DateTimeOffset PublishedAt { get; set; }

			public ICollection<string> Tags { get; set; }

			public string Key { get; set; }

			public string SeoMetaDescription { get; set; }
			public ICollection<string> SeoKeywords { get; set; }
			public DateTimeOffset? SeoLastAnalyzedAt { get; set; }
		}
	}
}
