﻿using System;
using System.Web.Mvc;
using HtmlAgilityPack;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web.Helpers
{
	public static class PostHelper
	{
		public static TimeToRead CalculateTimeToRead(MvcHtmlString body)
		{
			if (body == null)
				return TimeToRead.Empty;

			var bodyAsString = body.ToString();
			var parts = bodyAsString.Split(' ');

			var wordCount = parts.Length;
			var rate = wordCount / 200.0;
			var minutes = (int)rate;
			var seconds = (rate - minutes) * 0.6;
			var minutesAndSeconds = minutes + seconds;
			var timeToReadInMinutes = (int)Math.Ceiling(minutesAndSeconds);

			return new TimeToRead
			{
				TimeToReadInMinutes = timeToReadInMinutes,
				WordCount = wordCount
			};
		}

        public static string GetMetaDescription(MvcHtmlString body)
        {
            const int maxLength = 160;
            const string ellipsis = "...";

            var shortenedLength = maxLength - ellipsis.Length;

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(body.ToString());
            var htmlBody = htmlDoc.DocumentNode;
            var bodyText = htmlBody.InnerText;

            if (bodyText.Length < maxLength)
                return bodyText;

            var shortenedText = bodyText.Substring(0, shortenedLength);
            return $"{shortenedText}{ellipsis}";
        }

	    public static string Url(Post post)
	    {
            var slug = SlugConverter.TitleToSlug(post.Title);
            var id = post.GetIdForUrl();
            var blogUrl = ConfigurationHelper.MainBlogUrl.TrimEnd('/');
            return $"{blogUrl}/{id}/{slug}";
	    }

		public class TimeToRead
		{
			public static TimeToRead Empty = new TimeToRead { TimeToReadInMinutes = 0, WordCount = 0 };

			private int timeToReadInMinutes;

			public int TimeToReadInMinutes
			{
				get
				{
					return timeToReadInMinutes;
				}

				set
				{
					timeToReadInMinutes = Math.Max(1, value);
				}
			}

			public int WordCount { get; set; }
		}
	}
}