using HibernatingRhinos.Loci.Common.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RaccoonBlog.Web.Helpers.Results;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RaccoonBlog.Web.Controllers
{
    public abstract partial class RaccoonController : Controller
    {
        protected IDocumentStore DocumentStore { get; }
        protected IDocumentSession RavenSession { get; }

        protected RaccoonController(IDocumentStore documentStore, IDocumentSession ravenSession)
        {
            DocumentStore = documentStore;
            RavenSession = ravenSession;
        }

        protected StatusCodeResult HttpNotModified()
        {
            return StatusCode(304);
        }

        protected IActionResult Xml(XDocument xml, string etag, DateTimeOffset? lastModified = null)
        {
            return new XmlResult(xml, etag, lastModified);
        }

        public const int DefaultPage = 1;

        private BlogConfig blogConfig;
        public BlogConfig BlogConfig
        {
            get
            {
                if (blogConfig == null)
                {
                    using (RavenSession.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
                    {
                        blogConfig = RavenSession.Load<BlogConfig>(BlogConfig.Key);
                    }

                    if (blogConfig == null && "welcome".Equals((string)RouteData.Values["controller"], StringComparison.OrdinalIgnoreCase) == false) // first launch
                    {
                        Response.Redirect("~/welcome");
                    }
                }
                return blogConfig;
            }
        }

        private List<Section> sections;
        public List<Section> Sections
        {
            get
            {
                if (sections == null)
                {
                    using (RavenSession.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
                    {
                        sections = RavenSession
                            .Query<Section>()
                            .ToList();
                    }
                }
                return sections;
            }
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            ViewBag.IsHomePage = false;
            base.OnActionExecuting(filterContext);
        }

        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            base.OnActionExecuted(filterContext);

            ViewBag.BlogConfig = BlogConfig;
            ViewBag.Sections = Sections;
        }

        public int PageSize
        {
            get { return BlogConfig.PostsOnPage; }
        }

        protected int CurrentPage
        {
            get
            {
                if (int.TryParse(Request.Query["page"], out var result))
                    return Math.Max(DefaultPage, result);
                return DefaultPage;
            }
        }

        protected new JsonNetResult Json(object data)
        {
            var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
            return new JsonNetResult(data, settings);
        }
    }
}

