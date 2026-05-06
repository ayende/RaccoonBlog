using HibernatingRhinos.Loci.Common.Models;
using Microsoft.AspNetCore.Mvc;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using System;
using System.Linq;
using System.Xml.Linq;

namespace RaccoonBlog.Web.Controllers
{
    public partial class SyndicationController : RaccoonController
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private static readonly string EtagInitValue = Guid.NewGuid().ToString();
        public SyndicationController(IDocumentStore documentStore, IDocumentSession ravenSession) : base(documentStore, ravenSession)
        {
        }
        public virtual IActionResult Rsd()
        {
            var ns = XNamespace.Get("http://archipelago.phrasewise.com/rsd");

            return Xml(new XDocument(
                        new XElement(ns + "service",
                                     new XElement(ns + "engineName", "Raccoon Blog"),
                                     new XElement(ns + "engineLink", "http://hibernatingrhinos.com"),
                                     new XElement(ns + "homePageLink", Url.RelativeToAbsolute(Request, Url.RouteUrl("homepage"))),
                                     new XElement(ns + "apis",
                                                  new XElement(ns + "api",
                                                               new XAttribute("name", "MetaWeblog"),
                                                               new XAttribute("preferred", "true"),
                                                               new XAttribute("blogID", "0"),
                                                               new XAttribute("apiLink", Url.RelativeToAbsolute(Request, Url.Content("~/services/metaweblogapi.ashx")))
                                                    )
                                        )
                            )
                        ), typeof(SyndicationController).FullName);

        }

        private class QueryBehavior
        {
            public string Title;
            public int Take;
            public bool AddExpiredNote;
            public IRavenQueryable<Post> PostsQuery;
        }

        public virtual IActionResult Rss(string tag, string token)
        {
            var queryBehavior = SetQueryLimitsBasedOnToken(token, RavenSession.Query<Post>().Statistics(out var stats));

            var postsQuery = queryBehavior.PostsQuery;

            if (string.IsNullOrWhiteSpace(tag) == false)
                postsQuery = postsQuery.Where(x => x.TagsAsSlugs.Any(postTag => postTag == tag));

            var posts = postsQuery.OrderByDescending(x => x.PublishAt)
                .Take(queryBehavior.Take)
                .ToList();

            if (queryBehavior.AddExpiredNote)
            {
                posts.Insert(0, new Post
                {
                    Title = "Feed token expired, you are no longer able to read future posts",
                    Id = null,
                    ContentType = DynamicContentType.Html,
                    Body = "<p>This feed token has expired, and will no longer show any future posts.</p><p>You can still read current posts.</p>"
                });
            }

            var lastModified = posts.Count > 0 ? posts[0].PublishAt : (DateTimeOffset)stats.Timestamp;

            if (CheckConditionalHeaders(stats, lastModified, out var responseETagHeader))
            {
                Response.Headers["ETag"] = responseETagHeader;
                Response.Headers["Last-Modified"] = lastModified.ToString("R");
                return HttpNotModified();
            }

            var rss = new XDocument(
                new XElement("rss",
                             new XAttribute("version", "2.0"),
                             new XElement("channel",
                                          new XElement("title", queryBehavior.Title),
                                          new XElement("link", Url.RelativeToAbsolute(Request, Url.RouteUrl("homepage"))),
                                          new XElement("description", BlogConfig.MetaDescription ?? queryBehavior.Title),
                                          new XElement("copyright", String.Format("{0} (c) {1}", BlogConfig.Copyright, DateTime.Now.Year)),
                                          new XElement("ttl", "60"),
                                          from post in posts
                                          let postLink = GetPostLink(post)
                                          select new XElement("item",
                                                              new XElement("title", System.Net.WebUtility.HtmlDecode(post.Title)),
                                                              new XElement("description", post.CompiledContent(true)),
                                                              new XElement("link", postLink),
                                                                new XElement("guid", postLink),
                                                              new XElement("pubDate", post.PublishAt.ToString("R"))
                                            )
                                )
                    )
                );

            return Xml(rss, responseETagHeader, lastModified);
        }

        private QueryBehavior SetQueryLimitsBasedOnToken(string token, IRavenQueryable<Post> postsQuery)
        {
            var behavior = new QueryBehavior
            {
                AddExpiredNote = false,
                Title = BlogConfig.Title,
                Take = 20
            };
            if (string.IsNullOrEmpty(token))
            {
                behavior.PostsQuery = postsQuery.Where(x => x.PublishAt < DateTimeOffset.Now.AsMinutes());
                return behavior;
            }

            int numberOfDays;
            string user;
            if (IsRssAccessTokenCurrent(token, out numberOfDays, out user))
            {
                behavior.Take = 50;
                behavior.Title = behavior.Title + " for " + user;
                behavior.PostsQuery = postsQuery.Where(x => x.PublishAt < DateTimeOffset.Now.AddDays(numberOfDays).AsMinutes());
            }
            else
            {
                behavior.Title = behavior.Title + " for " + user + " EXPIRED TOKEN";
                behavior.PostsQuery = postsQuery.Where(x => x.PublishAt < DateTimeOffset.Now.AsMinutes());
                behavior.AddExpiredNote = true;
            }

            return behavior;
        }

        private string GetPostLink(Post post)
        {
            if (post.Id == null) // invalid feed
                return Url.AbsoluteAction(Request, "Index", "Posts");
            return Url.AbsoluteAction(Request, "Details", "PostDetails", new { Id = post.GetIdForUrl(), Slug = SlugConverter.TitleToSlug(post.Title), Key = post.ShowPostEvenIfPrivate });
        }


        private bool IsRssAccessTokenCurrent(string token, out int numberOfDays, out string user)
        {
            try
            {
                var parsedToken = FutureRssAccessToken.Parse(token, BlogConfig);
                numberOfDays = parsedToken.NumberOfDays;
                user = parsedToken.User;

                if (parsedToken.IsExpired)
                    return false;

                return true;
            }
            catch (Exception exception)
            {
                _log.Warn($"Error decrypting RSS access token: {token}", exception);

                numberOfDays = 0;
                user = null;
                return false;
            }
        }

        public virtual IActionResult CommentsRss(int? id)
        {
            QueryStatistics stats = null;
            var commentsTuples = RavenSession.QueryForRecentComments(q =>
            {
                if (id != null)
                {
                    var postId = "posts/" + id;
                    q = q.Where(x => x.PostId == postId);
                }
                return q.Statistics(out stats).Take(30);
            });

            var lastModified = commentsTuples.Count > 0
                ? commentsTuples[0].Item1.CreatedAt
                : (DateTimeOffset)stats.Timestamp;

            if (CheckConditionalHeaders(stats, lastModified, out var responseETagHeader))
            {
                Response.Headers["ETag"] = responseETagHeader;
                Response.Headers["Last-Modified"] = lastModified.ToString("R");
                return HttpNotModified();
            }

            var rss = new XDocument(
            new XElement("rss",
                         new XAttribute("version", "2.0"),
                         new XElement("channel",
                                      new XElement("title", BlogConfig.Title),
                                      new XElement("link", Url.RelativeToAbsolute(Request, Url.RouteUrl("homepage"))),
                                      new XElement("description", BlogConfig.MetaDescription ?? BlogConfig.Title),
                                      new XElement("copyright", String.Format("{0} (c) {1}", BlogConfig.Copyright, DateTime.Now.Year)),
                                      new XElement("ttl", "60"),
                                      from commentsTuple in commentsTuples
                                      let comment = commentsTuple.Item1
                                      let post = commentsTuple.Item2
                                      let link = Url.AbsoluteAction(Request, "Details", "PostDetails", new { Id = post.GetIdForUrl(), Slug = SlugConverter.TitleToSlug(post.Title) }) + "#comment" + comment.Id
                                      select new XElement("item",
                                                          new XElement("title", comment.Author + " commented on " + post.Title),
                                                          new XElement("description", comment.Body),
                                                          new XElement("link", link),
                                                            new XElement("guid", link),
                                                          new XElement("pubDate", comment.CreatedAt.ToString("R"))
                                        )
                            )
                )
            );

            return Xml(rss, responseETagHeader, lastModified);
        }

        private bool CheckConditionalHeaders(QueryStatistics stats, DateTimeOffset lastModified, out string responseETagHeader)
        {
            responseETagHeader = stats.Timestamp.ToString("o") + EtagInitValue;

            if (Request.Headers["If-None-Match"].ToString() == responseETagHeader)
                return true;

            if (DateTimeOffset.TryParse(Request.Headers["If-Modified-Since"].ToString(), out var ifModifiedSince) && lastModified <= ifModifiedSince)
                return true;

            return false;
        }

        public virtual IActionResult LegacyRss()
        {
            return RedirectToActionPermanent("Rss", "Syndication");
        }
    }
}
