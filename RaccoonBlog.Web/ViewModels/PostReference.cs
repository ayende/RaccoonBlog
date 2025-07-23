using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using System;
using System.Collections.Generic;

namespace RaccoonBlog.Web.ViewModels
{
	public class PostReference
	{
		// if Id is: posts/1024-A, than domainId will be: 1024-A
		public string Id { get; set; }
		public string Title { get; set; }
        
        public DateTimeOffset PublishedAt { get; set; }
        public ICollection<string> Tags { get; set; }

		private string _domainId;
		public string DomainId
		{
			get
			{
				if (_domainId == null)
					_domainId = Post.GetIdForUrl(Id);
				return _domainId;
			}
		}

		private string slug;
		public string Slug
		{
			get { return slug ?? (slug = SlugConverter.TitleToSlug(Title)); }
			set { slug = value; }
		}
	}
}
