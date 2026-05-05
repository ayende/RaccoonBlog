using System;
using System.ComponentModel.DataAnnotations;
using RaccoonBlog.Web.Helpers.Validation;

namespace RaccoonBlog.Web.ViewModels
{
	public class BlogConfigurationInput
	{
		[Required]
		[Display(Name = "Title")]
		public string Title { get; set; }

		[Required]
		[Display(Name = "Subtitle")]
		public string Subtitle { get; set; }

		[Display(Name = "CustomCss")]
		public string CustomCss { get; set; }

		[Display(Name = "Copyright")]
		public string Copyright { get; set; }

		[Display(Name = "GenAI Spam Check API Key")]
		public string GenAiSpamCheckApiKey { get; set; }

		[Display(Name = "GenAI Spam Check Endpoint")]
		public string GenAiSpamCheckEndpoint { get; set; }

		[Display(Name = "GenAI Spam Check Model")]
		public string GenAiSpamCheckModel { get; set; }

		[Display(Name = "GoogleAnalyticsKey")]
		public string GoogleAnalyticsKey { get; set; }

		[Required]
		[Display(Name = "FuturePostsEncryptionKey")]
		public string FuturePostsEncryptionKey { get; set; }

		[Required]
		[Display(Name = "FuturePostsEncryptionIV")]
		public string FuturePostsEncryptionIV { get; set; }

		[Display(Name = "MetaDescription")]
		public string MetaDescription { get; set; }

		[Display(Name = "MetaKeywords")]
		public string MetaKeywords { get; set; }

		[Display(Name = "MinNumberOfPostForSignificantTag")]
		public int MinNumberOfPostForSignificantTag { get; set; }

		[Display(Name = "NumberOfDayToCloseComments")]
		public int NumberOfDayToCloseComments { get; set; }
	}
}
