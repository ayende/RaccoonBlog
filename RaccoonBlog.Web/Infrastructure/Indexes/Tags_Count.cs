using System;
using System.Linq;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents.Indexes;

namespace RaccoonBlog.Web.Infrastructure.Indexes
{
	public class Tags_Count : AbstractIndexCreationTask<Post, Tags_Count.ReduceResult>
	{
		public class ReduceResult
		{
			public string Name { get; set; }
			public int Count { get; set; }
			public DateTimeOffset LastSeenAt { get; set; }
		}

		public Tags_Count()
		{
			Map = posts => from post in posts
						   from tag in post.Tags
						   where !tag.StartsWith("@")
						   select new {Name = tag.ToString().ToLower(), Count = 1, LastSeenAt = post.PublishAt};
			Reduce = results => from tagCount in results
								group tagCount by tagCount.Name
								into g
								select new {Name = g.Key, Count = g.Sum(x => x.Count), LastSeenAt = g.Max(x=>(DateTimeOffset)x.LastSeenAt) };
		}
	}
}
