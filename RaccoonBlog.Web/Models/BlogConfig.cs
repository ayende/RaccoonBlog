using System;
using System.ComponentModel.DataAnnotations;
using RaccoonBlog.Web.Helpers.Validation;

namespace RaccoonBlog.Web.Models
{
	public class BlogConfig : Model
	{
		public string Title { get; set; }

		public string OwnerEmail { get; set; }

		public string Subtitle { get; set; }

		public string CustomCss { get; set; }

		public string Copyright { get; set; }

		public string AkismetKey { get; set; }

		public string GoogleAnalyticsKey { get; set; }

		public Guid RssFuturePostsKey { get; set; }

		public int RssFutureDaysAllowed { get; set; }

		public string MetaDescription { get; set; }

		public int MinNumberOfPostForSignificantTag { get; set; }

		public int NumberOfDayToCloseComments { get; set; }

		public static BlogConfig New()
		{
			return new BlogConfig
			       	{
						Id = "Blog/Config",
			       		RssFuturePostsKey = Guid.NewGuid(),
						RssFutureDaysAllowed = 0,
						CustomCss = "hibernatingrhinos"
			       	};
		}

		public static BlogConfig NewDummy()
		{
			return new BlogConfig
			{
				Id = "Blog/Config/Dummy",
			};
		}
	}
}