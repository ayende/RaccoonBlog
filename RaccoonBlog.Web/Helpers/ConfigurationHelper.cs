using System;
using Microsoft.Extensions.Configuration;

namespace RaccoonBlog.Web.Helpers
{
	public static class ConfigurationHelper
	{
		private static IConfiguration _configuration;
		private static Tuple<string, string> microsoftOAuthKeys;
		private static Tuple<string, string> googleOAuthKeys;

		/// <summary>
		/// Initialize the configuration helper. Call this from Program.cs after building the app.
		/// </summary>
		public static void Initialize(IConfiguration configuration)
		{
			_configuration = configuration;
		}

		public static Tuple<string, string> MicrosoftOAuthKeys
		{
			get
			{
				return microsoftOAuthKeys ?? (microsoftOAuthKeys = GetKeys("Microsoft", "ClientId", "ClientSecret"));
			}
		}

		public static Tuple<string, string> GoogleOAuthKeys
		{
			get
			{
				return googleOAuthKeys ?? (googleOAuthKeys = GetKeys("Google", "ClientId", "ClientSecret"));
			}
		}

		public static string MainBlogUrl => _configuration?["MainUrl"] ?? _configuration?["Raccoon:MainUrl"] ?? string.Empty;

		private static Tuple<string, string> GetKeys(string provider, string idKey, string secretKey)
		{
			if (_configuration == null)
				return null;

			var keyPrefix = $"Raccoon:OAuth:{provider}";
			var id = _configuration[$"{keyPrefix}:{idKey}"];
			var secret = _configuration[$"{keyPrefix}:{secretKey}"];

			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
				return null;

			return new Tuple<string, string>(id, secret);
		}
	}
}