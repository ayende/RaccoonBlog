using System;
using System.Collections.Generic;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers
{
	public class TagsResolver
	{
		private const string TagsSeparator = "|";

		public static string ResolveTags(ICollection<string> tags)
		{
			if (tags == null || tags.Count == 0)
				return string.Empty;

			return string.Join(TagsSeparator, tags);
		}

		public static ICollection<string> ResolveTagsInput(string tags)
		{
			if (string.IsNullOrEmpty(tags))
				return new string[0];

			return tags.Split(new[] { TagsSeparator }, StringSplitOptions.RemoveEmptyEntries);
		}
	}
}
