using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Mvc;
using System.Xml.Linq;
using System.Linq;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;
using Raven.Client.Linq;
using RaccoonBlog.Web.Helpers;

namespace RaccoonBlog.Web.Controllers
{
	public class SyndicationController : AbstractController
	{
		public ActionResult Rsd()
		{
			var ns = XNamespace.Get("http://archipelago.phrasewise.com/rsd");

			return Xml(new XDocument(
			           	new XElement(ns + "service",
			           	             new XElement(ns + "engineName", "Raccoon Blog"),
			           	             new XElement(ns + "engineLink", "http://hibernatingrhinos.com"),
			           	             new XElement(ns + "homePageLink", Url.RouteUrl("Default")),
			           	             new XElement(ns + "apis",
			           	                          new XElement(ns + "api",
			           	                                       new XAttribute("name", "MetaWeblog"),
			           	                                       new XAttribute("preferred", "true"),
			           	                                       new XAttribute("blogID", "0"),
			           	                                       new XAttribute("apiLink",Url.Content("~/services/metaweblogapi.ashx"))
			           	                          	)
			           	             	)
			           		)
			           	), typeof (SyndicationController).FullName);
		}

		public ActionResult Tag(string name)
		{
			RavenQueryStatistics stats;
			var posts = Session.Query<Post>()
				.Statistics(out stats)
				.Where(x => x.PublishAt < DateTimeOffset.Now && x.Tags.Any(tag => tag == name))
				.OrderByDescending(x => x.PublishAt)
				.Take(20)
				.ToList();

			return Rss(stats, posts);
		}

		public ActionResult Rss()
		{
			RavenQueryStatistics stats;
			var posts = Session.Query<Post>()
				.Statistics(out stats)
				.Where(x => x.PublishAt < DateTimeOffset.Now)
				.OrderByDescending(x => x.PublishAt)
				.Take(20)
				.ToList();

			return Rss(stats, posts);
		}

		private ActionResult Rss(RavenQueryStatistics stats, IEnumerable<Post> posts)
		{
			string requestETagHeader = Request.Headers["If-None-Match"] ?? string.Empty;
			var responseETagHeader = stats.Timestamp.ToString("o");
			if (requestETagHeader == responseETagHeader)
				return HttpNotModified();

			var rss = new XDocument(
				new XElement("rss",
				             new XAttribute("version", "2.0"),
				             new XElement("channel",
				                          new XElement("title", GetBlogTitle()),
                                          new XElement("link", String.Format("{0}{1}", ConfigurationManager.AppSettings["MainUrl"], Request.ApplicationPath)),
				                          new XElement("description", GetBlogTitle()),
				                          new XElement("copyright", String.Format("{0} (c) {1}", GetBlogCopyright(), DateTime.Now.Year)),
				                          new XElement("ttl", "60"),
				                          from post in posts
				                          select new XElement("item",
				                                              new XElement("title", post.Title),
				                                              new XElement("description", post.Body),
				                                              new XElement("link", GetPostLink(post)),
																new XElement("guid", GetPostLink(post)),
				                                              new XElement("pubDate", post.PublishAt.ToString("R"))
				                          	)
				             	)
					)
				);

			return Xml(rss, responseETagHeader);
		}

	    private string GetPostLink(Post post)
	    {
            var postReference = post.MapTo<PostReference>();
			return Url.RelativeToAbsolute(Url.Action("Details", "PostDetails", new { Id = postReference.DomainId, postReference.Slug }));
	    }

	    private string GetBlogCopyright()
		{
			return Session.Load<BlogConfig>("Blog/Config").Copyright;
		}

		private string GetBlogTitle()
		{
			return Session.Load<BlogConfig>("Blog/Config").Title;
		}

        public ActionResult LegacyRss()
        {
            return RedirectToActionPermanent("Rss", "Syndication");
        }
	}
}
