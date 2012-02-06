using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.ComponentModel.DataAnnotations;
using RaccoonBlog.Web.Helpers.Validation;

namespace RaccoonBlog.Web.ViewModels
{
    public class BlogConfigViewModelWelcome
	{
        [Required]
		[Display(Name = "Blog title")]
		public string Title { get; set; }

		[Required]
		[Display(Name = "Owner Email")]
		public string OwnerEmail { get; set; }

		[Display(Name = "Slogan")]
		public string Subtitle { get; set; }

		[Required]
		[Display(Name = "Custom CSS")]
		public string CustomCss { get; set; }

		[Display(Name = "Copyright string")]
		public string Copyright { get; set; }

		[Display(Name = "Akismet Key")]
		public string AkismetKey { get; set; }

		[Display(Name = "Google-Analytics Key")]
		public string GoogleAnalyticsKey { get; set; }

		[Display(Name = "MetaDescription")]
		public string MetaDescription { get; set; }

    }

    public class BlogConfigViewModel
    {
        [HiddenInput]
        public string Id { get; set; }

        [Required]
        [Display(Name = "Blog title")]
        public string Title { get; set; }

        [Required]
        [Display(Name = "Owner Email")]
        public string OwnerEmail { get; set; }

        [Display(Name = "Slogan")]
        public string Subtitle { get; set; }

        [Required]
        [Display(Name = "Custom CSS")]
        public string CustomCss { get; set; }

        [Display(Name = "Copyright string")]
        public string Copyright { get; set; }

        [Display(Name = "Akismet Key")]
        public string AkismetKey { get; set; }

        [Display(Name = "Google-Analytics Key")]
        public string GoogleAnalyticsKey { get; set; }

        [Required]
        [NonEmptyGuid]
        [Display(Name = "RssFuturePostsKey")]
        public Guid RssFuturePostsKey { get; set; }

        [Display(Name = "RssFutureDaysAllowed")]
        public int RssFutureDaysAllowed { get; set; }

        [Display(Name = "MetaDescription")]
        public string MetaDescription { get; set; }

        [Display(Name = "MinNumberOfPostForSignificantTag")]
        public int MinNumberOfPostForSignificantTag { get; set; }

        [Display(Name = "NumberOfDayToCloseComments")]
        public int NumberOfDayToCloseComments { get; set; }
    }
}
