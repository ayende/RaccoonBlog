using Microsoft.AspNetCore.Mvc;
using RaccoonBlog.Web.Areas.Admin.Models;
using RaccoonBlog.Web.Areas.Admin.ViewModels;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Session;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RaccoonBlog.Web.Areas.Admin.Controllers
{
    public partial class SettingsController : AdminController
    {
        public SettingsController(IDocumentStore documentStore, IDocumentSession ravenSession)
        : base(documentStore, ravenSession)
        {
        }

        [HttpGet]
        public virtual IActionResult Index()
        {
            return View(BlogConfig);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual IActionResult Index(BlogConfig config)
        {
            if (ModelState.IsValid == false)
            {
                ViewBag.Message = ModelState.FirstErrorMessage();
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { Success = false, ViewBag.Message });
                return View(BlogConfig);
            }

            var current = RavenSession.Load<BlogConfig>(BlogConfig.Key);
            if (IsFuturePostsEncryptionOptionsChanged(current, config))
            {
                RemoveFutureRssAccessOnEncryptionConfigChange();
            }

            RavenSession.Advanced.Evict(current);
            RavenSession.Store(config, BlogConfig.Key);
            RavenSession.SaveChanges();

            ViewBag.Message = "Configurations successfully saved!";
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { Success = true, ViewBag.Message });
            return View(config);
        }

        private void RemoveFutureRssAccessOnEncryptionConfigChange()
        {
            DocumentStore.Operations.SendAsync(new DeleteByQueryOperation(new IndexQuery { Query = RavenSession.Query<FutureRssAccess>().ToString() }, new QueryOperationOptions
            {
                AllowStale = false
            }));
        }

        [HttpGet]
        public virtual IActionResult RssFutureAccess()
        {
            ValidateConfiguration();
            SetFutureRssAccessList(RavenSession);

            return View(FutureRssAccess.Default);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual IActionResult RssFutureAccess(FutureRssAccess input)
        {
            ValidateConfiguration();

            if (ExistsRssFutureAccess(input))
            {
                ModelState.AddModelError("Exists", "Access has already been set up with same parameters.");
            }

            if (ModelState.IsValid == false)
            {
                SetFutureRssAccessList(RavenSession);
                return View(input);
            }

            input.Token = GetFutureAccessToken(input.ExpiredOn, input.NumberOfFutureDays, input.User);

            RavenSession.Store(input);
            RavenSession.SaveChanges();

            SetFutureRssAccessList(RavenSession);

            return View(input);
        }

        private bool ExistsRssFutureAccess(FutureRssAccess input)
        {
            var exists = RavenSession.Query<FutureRssAccess>()
                .Any(x => x.ExpiredOn == input.ExpiredOn &&
                          x.NumberOfFutureDays == input.NumberOfFutureDays &&
                          x.User == input.User);
            return exists;
        }

        private bool IsFuturePostsEncryptionOptionsChanged(BlogConfig current, BlogConfig config)
        {
            return current.FuturePostsEncryptionKey != config.FuturePostsEncryptionKey ||
                   current.FuturePostsEncryptionIv != config.FuturePostsEncryptionIv ||
                   current.FuturePostsEncryptionSalt != config.FuturePostsEncryptionSalt;
        }

        private void SetFutureRssAccessList(IDocumentSession session)
        {
            ViewData["FutureRssAccessList"] = session.Query<FutureRssAccess>()
                .Customize(x => x.WaitForNonStaleResults())
                .OrderBy(x => x.ExpiredOn)
                .ToList();
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrEmpty(BlogConfig.FuturePostsEncryptionKey))
            {
                ModelState.AddModelError("BlogConfig.FuturePostsEncryptionKey", $"Future posts RSS token encryption key is not set.");
            }

            if (string.IsNullOrEmpty(BlogConfig.FuturePostsEncryptionIv))
            {
                ModelState.AddModelError("BlogConfig.FuturePostsEncryptionIv", $"Future posts RSS token encryption IV is not set.");
            }

            if (string.IsNullOrEmpty(BlogConfig.FuturePostsEncryptionSalt))
            {
                ModelState.AddModelError("BlogConfig.FuturePostsEncryptionSalt", $"Future posts RSS token encryption Salt is missing.");
            }
        }

        private string GetFutureAccessToken(DateTime expiresOn, int numberOfDays, string user)
        {
            return new FutureRssAccessToken(expiresOn, numberOfDays, user)
                .GetToken(BlogConfig);
        }
    }
}