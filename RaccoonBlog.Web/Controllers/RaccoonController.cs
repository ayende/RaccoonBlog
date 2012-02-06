using System;
using System.Web.Mvc;
using HibernatingRhinos.Loci.Common.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RaccoonBlog.Web.Helpers.Results;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;


namespace RaccoonBlog.Web.Controllers
{
	public abstract class RaccoonController : RavenController
	{
		public const int DefaultPage = 1;
		public const int PageSize = 25;

		private BlogConfig blogConfig;
		public BlogConfig BlogConfig
		{
			get
			{
				if (blogConfig == null)
				{
					using (RavenSession.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
					{
						blogConfig = RavenSession.Load<BlogConfig>("Blog/Config");
					}

					if (blogConfig == null && !"welcome".Equals((string)RouteData.Values["controller"], StringComparison.OrdinalIgnoreCase)) // first launch
					{
						HttpContext.Response.Redirect("/welcome", true);
					}
				}
				return blogConfig;
			}
		}

		protected override void OnActionExecuted(ActionExecutedContext filterContext)
		{
			base.OnActionExecuted(filterContext);

			ViewBag.BlogConfig = BlogConfig;
		}

		protected int CurrentPage
		{
			get
			{
				var s = Request.QueryString["page"];
				int result;
				if (int.TryParse(s, out result))
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
