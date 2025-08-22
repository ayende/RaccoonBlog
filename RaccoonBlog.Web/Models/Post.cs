// //-----------------------------------------------------------------------
// // <copyright company="RavenDB LTD">
// //     Copyright (c) RavenDB LTD. All rights reserved.
// // </copyright>
// //-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using HibernatingRhinos.Loci.Common.Models;
using RaccoonBlog.Web.Infrastructure.Common;

namespace RaccoonBlog.Web.Models
{
	public class Post : IDynamicContent
	{
		public Post()
		{
			ContentType = DynamicContentType.Html;
		}

		public string Id { get; set; }

		public string Title { get; set; }
		public string LegacySlug { get; set; }

		public string Body { get; set; }
		public DynamicContentType ContentType { get; set; }
		public ICollection<string> Tags { get; set; }

		public string AuthorId { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
		public DateTimeOffset PublishAt { get; set; }
		public bool SkipAutoReschedule { get; set; }

		public string LastEditedByUserId { get; set; }
		public DateTimeOffset? LastEditedAt { get; set; }

		public bool AllowComments { get; set; }

        public SocialNetworkIntegration Integration { get; set; }

		private Guid _showPostEvenIfPrivate;
		public Guid ShowPostEvenIfPrivate
		{
			get
			{
				if (_showPostEvenIfPrivate == Guid.Empty)
					_showPostEvenIfPrivate = Guid.NewGuid();
				return _showPostEvenIfPrivate;
			}
			set { _showPostEvenIfPrivate = value; }
		}

		public int CommentsCount { get; set; }
		public string CommentsId { get; set; }

		public IEnumerable<string> TagsAsSlugs
		{
			get
			{
				if (Tags == null)
					yield break;
				foreach (var tag in Tags)
				{
					yield return SlugConverter.TitleToSlug(tag);
				}
			}
		}

		public bool IsPublicPost(Guid key)
		{
			if (PublishAt <= DateTimeOffset.Now)
				return true;

			return key != Guid.Empty && key == ShowPostEvenIfPrivate;
		}

	    public string GetIdForUrl()
	    {
	        return GetIdForUrl(Id);
	    }
        
	    public static string GetIdForUrl(string id)
	    {
	        var i = "posts/".Length;
	        return id.Substring(i);
	    }
	}

	public class PostInput
	{
		[HiddenInput]
		public string Id { get; set; }

		[Required]
		[Display(Name = "Title")]
		public string Title { get; set; }

		[AllowHtml]
		[Required]
		[Display(Name = "Body")]
		[DataType(DataType.MultilineText)]
		public string Body { get; set; }

		[Required]
		[Display(Name = "Content type")]
		public DynamicContentType ContentType { get; set; }

		[Display(Name = "Created At")]
		[DataType(DataType.DateTime)]
		public DateTimeOffset CreatedAt { get; set; }

		[Display(Name = "Publish At")]
		[DataType(DataType.DateTime)]
		public DateTimeOffset? PublishAt { get; set; }

		[Display(Name = "Tags")]
		public string Tags { get; set; }

		[Display(Name = "Allow Comments")]
		public bool AllowComments { get; set; }

		public bool IsNewPost()
		{
			return string.IsNullOrEmpty(Id);
		}
	}
}