using System.ComponentModel.DataAnnotations;
using RaccoonBlog.Web.Helpers;

namespace RaccoonBlog.Web.Models
{
    public class BlogConfig : Model
    {
        public const string Key = "Blog/Config";

        public BlogConfig()
        {
            PostsOnPage = 10;
        }

        [Required]
        [Display(Name = "Blog title")]
        public string Title { get; set; }

        [Required]
        [Display(Name = "Owner Email")]
        public string OwnerEmail { get; set; }

        [Display(Name = "Twitter Login")]
        public string TwitterLogin { get; set; }

        [Display(Name = "Facebook Login")]
        public string FacebookLogin { get; set; }

        [Display(Name = "GitHub Login")]
        public string GithubLogin { get; set; }

        [Display(Name = "RSS Login")]
        public string RssLogin { get; set; }

        [Display(Name = "LinkedIn Login")]
        public string LinkedInLogin { get; set; }

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

        [Display(Name = "FuturePostsEncryptionKey")]
        public string FuturePostsEncryptionKey { get; set; }

        [Display(Name = "FuturePostsEncryptionIv")]
        [StringLength(16, ErrorMessage = "IV must be 16 characters long.", MinimumLength = 16)]
        public string FuturePostsEncryptionIv { get; set; }

        [Display(Name = "FuturePostsEncryptionSalt")]
        public string FuturePostsEncryptionSalt { get; set; }

        [Display(Name = "MetaDescription")]
        public string MetaDescription { get; set; }

        [Display(Name = "MinNumberOfPostForSignificantTag")]
        public int MinNumberOfPostForSignificantTag { get; set; }

        [Display(Name = "NumberOfDayToCloseComments")]
        public int NumberOfDayToCloseComments { get; set; }

        [Display(Name = "Posts On Page")]
        public int PostsOnPage { get; set; }

        [Display(Name = "Reddit User")]
        public string RedditUser { get; set; }

        [Display(Name = "Reddit Password")]
        public string RedditPassword { get; set; }

        [Display(Name = "Reddit Client App ID")]
        public string RedditClientAppId { get; set; }

        [Display(Name = "Reddit Client Secret")]
        public string RedditClientSecret { get; set; }

        [Display(Name = "Reddit Subreddits To Submit To On Publish - comma-separated e.g. /r/test,/r/example")]
        public string RedditSubredditsToSubmitToOnPublish { get; set; }

        [Display(Name = "Twitter API Key (RavenDB account)")]
        public string TwitterAccountRavendbApiKey { get; set; }

        [Display(Name = "Twitter API Secret (RavenDB account)")]
        public string TwitterAccountRavendbApiSecret { get; set; }

        [Display(Name = "Twitter Access Token (RavenDB account)")]
        public string TwitterAccountRavendbAccessToken { get; set; }

        [Display(Name = "Twitter Access Token Secret (RavenDB account)")]
        public string TwitterAccountRavendbAccessTokenSecret { get; set; }

        [Display(Name = "Twitter API Key (Personal account)")]
        public string TwitterAccountAyendeApiKey { get; set; }

        [Display(Name = "Twitter API Secret (Personal account)")]
        public string TwitterAccountAyendeApiSecret { get; set; }

        [Display(Name = "Twitter Access Token (Personal account)")]
        public string TwitterAccountAyendeAccessToken { get; set; }

        [Display(Name = "Twitter Access Token Secret (Personal account)")]
        public string TwitterAccountAyendeAccessTokenSecret { get; set; }

        [Display(Name = "Discord Webhook URL")]
        public string DiscordWebhookUrl { get; set; }

        [Display(Name = "GitHub Personal Access Token")]
        public string GitHubPersonalAccessToken { get; set; }

        [Display(Name = "GitHub Discussion Repository (owner/name)")]
        public string GitHubDiscussionRepo { get; set; }

        [Display(Name = "GitHub Discussion Category ID")]
        public string GitHubDiscussionCategoryId { get; set; }

        public static BlogConfig New()
        {
            return new BlogConfig
            {
                Id = BlogConfig.Key,
                CustomCss = "hibernatingrhinos",
                FuturePostsEncryptionKey = CryptographyUtil.GenerateKey(),
                FuturePostsEncryptionSalt = CryptographyUtil.GenerateRandomString(16),
                FuturePostsEncryptionIv = CryptographyUtil.GenerateIv()
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