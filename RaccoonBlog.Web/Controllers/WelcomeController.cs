using System.Web.Mvc;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.ViewModels;

namespace RaccoonBlog.Web.Controllers
{
	public class WelcomeController : RaccoonController
	{
		//
		// GET: /Welcome/
		public ActionResult Index()
		{
			return AssertConfigurationIsNeeded() ?? View(BlogConfig.New().MapTo<BlogConfigViewModel>());
		}

		[HttpPost]
		public ActionResult CreateBlog(BlogConfigViewModel config)
		{
			var result = AssertConfigurationIsNeeded();
			if (result != null)
				return result;

			if (!ModelState.IsValid)
				return View("Index");

            var configuration = config.MapTo<BlogConfig>();

			// Create the blog by storing the config
            configuration.Id = "Blog/Config";
            RavenSession.Store(configuration);

			// Create default sections
			RavenSession.Store(new Section { Title = "Archive", IsActive = true, Position = 1, ControllerName = "Section", ActionName = "ArchivesList" });
			RavenSession.Store(new Section { Title = "Tags", IsActive = true, Position = 2, ControllerName = "Section", ActionName = "TagsList" });
			RavenSession.Store(new Section { Title = "Statistics", IsActive = true, Position = 3, ControllerName = "Section", ActionName = "PostsStatistics" });
			
			var user = new User
			{
				FullName = "Default User",
				Email = config.OwnerEmail,
				Enabled = true,
			}.SetPassword("raccoon");
			RavenSession.Store(user);

			return RedirectToAction("Success", config);
		}

		public ActionResult Success()
		{
			BlogConfig bc;

			// Bypass the aggressive caching to force loading the BlogConfig object,
			// otherwise we might get a null BlogConfig even though a valid one exists
			using (RavenSession.Advanced.DocumentStore.DisableAggressiveCaching())
			{
				bc = RavenSession.Load<BlogConfig>("Blog/Config");
			}

			return bc == null ? View("Index") : View(bc);
		}

		private ActionResult AssertConfigurationIsNeeded()
		{
			BlogConfig bc;

			// Bypass the aggressive caching to force loading the BlogConfig object,
			// otherwise we might get a null BlogConfig even though a valid one exists
			using (RavenSession.Advanced.DocumentStore.DisableAggressiveCaching())
			{
				bc = RavenSession.Load<BlogConfig>("Blog/Config");
			}

			if (bc != null)
			{
				return Redirect("/");
			}
			return null;
		}
	}
}
