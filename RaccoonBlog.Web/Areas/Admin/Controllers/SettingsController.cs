using System.Web.Mvc;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;
using RaccoonBlog.Web.Infrastructure.AutoMapper;

namespace RaccoonBlog.Web.Areas.Admin.Controllers
{
	public class SettingsController : AdminController
	{
		[HttpGet]
		public ActionResult Index()
		{
			return View(BlogConfig.MapTo<BlogConfigViewModel>());
		}

		[HttpPost]
		public ActionResult Index(BlogConfigViewModel config)
		{
			if (ModelState.IsValid == false)
			{
				ViewBag.Message = ModelState.FirstErrorMessage();
				if (Request.IsAjaxRequest())
					return Json(new { Success = false, ViewBag.Message });
                return View(BlogConfig.MapTo<BlogConfigViewModel>());
			}

			RavenSession.Store(config.MapTo<BlogConfig>());

			ViewBag.Message = "Configurations successfully saved!";
			if (Request.IsAjaxRequest())
				return Json(new { Success = true, ViewBag.Message });
			return View(config);
		}
	}
}
