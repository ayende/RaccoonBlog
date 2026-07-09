using System;
using Microsoft.Extensions.Configuration;

namespace RaccoonBlog.Web.Helpers
{
	public static class ConfigurationHelper
	{
		private static IConfiguration _configuration;

		/// <summary>
		/// Initialize the configuration helper. Call this from Program.cs after building the app.
		/// </summary>
		public static void Initialize(IConfiguration configuration)
		{
			_configuration = configuration;
		}

		public static string MainBlogUrl => _configuration?["AppSettings:MainUrl"] ?? _configuration?["MainUrl"] ?? _configuration?["Raccoon:MainUrl"] ?? string.Empty;
	}
}