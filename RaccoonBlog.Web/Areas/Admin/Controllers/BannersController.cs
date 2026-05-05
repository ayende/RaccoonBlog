using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.Services;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace RaccoonBlog.Web.Areas.Admin.Controllers
{
    public class BannersController : AdminController
    {
        private static readonly HashSet<string> AllowedImageTypes = new()
        {
            "image/jpeg", "image/png"
        };

        private readonly CacheSignalService _cacheSignal;

        public BannersController(IDocumentStore documentStore, IDocumentSession ravenSession, CacheSignalService cacheSignal)
            : base(documentStore, ravenSession)
        {
            _cacheSignal = cacheSignal;
        }

        public IActionResult Index()
        {
            var doc = RavenSession.Load<BlogBanners>(BlogBanners.DocumentId);
            return View("List", doc?.Banners ?? new System.Collections.Generic.List<BannerItem>());
        }

        [HttpGet]
        public IActionResult Add()
        {
            return View("Edit", new BannerItem());
        }

        [HttpGet]
        public IActionResult Edit(string id)
        {
            var doc = RavenSession.Load<BlogBanners>(BlogBanners.DocumentId);
            var banner = doc?.Banners.FirstOrDefault(b => b.Id == id);
            if (banner == null)
                return NotFound("Banner does not exist.");

            return View(banner);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update(BannerItem item, IFormFile image)
        {
            var doc = RavenSession.Load<BlogBanners>(BlogBanners.DocumentId);
            if (doc == null)
            {
                doc = new BlogBanners { Id = BlogBanners.DocumentId };
                RavenSession.Store(doc);
            }

            if (image != null && image.Length > 0 && !AllowedImageTypes.Contains(image.ContentType))
            {
                ModelState.AddModelError("image", "Only JPEG and PNG images are allowed.");
                return View("Edit", item);
            }

            var existing = string.IsNullOrEmpty(item.Id)
                ? null
                : doc.Banners.FirstOrDefault(b => b.Id == item.Id);

            if (existing == null)
            {
                if (image == null || image.Length == 0)
                {
                    ModelState.AddModelError("image", "Image is required for new banners.");
                    return View("Edit", item);
                }

                item.Id = Guid.NewGuid().ToString();
                item.AttachmentName = Guid.NewGuid().ToString("N") + Path.GetExtension(image.FileName).ToLowerInvariant();
                doc.Banners.Add(item);
            }
            else
            {
                existing.ResourceUrl = item.ResourceUrl;
                existing.AltText = item.AltText;
                existing.StartDate = item.StartDate;
                existing.EndDate = item.EndDate;

                if (image != null && image.Length > 0)
                {
                    RavenSession.Advanced.Attachments.Delete(BlogBanners.DocumentId, existing.AttachmentName);
                    existing.AttachmentName = Guid.NewGuid().ToString("N") + Path.GetExtension(image.FileName).ToLowerInvariant();
                }
            }

            if (image != null && image.Length > 0)
            {
                var attachmentName = item.AttachmentName ?? existing.AttachmentName;
                RavenSession.Advanced.Attachments.Store(BlogBanners.DocumentId, attachmentName, image.OpenReadStream(), image.ContentType);
            }

            _cacheSignal.Invalidate(CacheKeys.BannerArea);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Activate(string id, bool activate)
        {
            var doc = RavenSession.Load<BlogBanners>(BlogBanners.DocumentId);
            var banner = doc?.Banners.FirstOrDefault(b => b.Id == id);
            if (banner == null)
                return NotFound("Banner does not exist.");

            banner.Enabled = activate;

            _cacheSignal.Invalidate(CacheKeys.BannerArea);
            return StatusCode(StatusCodes.Status200OK);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string id)
        {
            var doc = RavenSession.Load<BlogBanners>(BlogBanners.DocumentId);
            var banner = doc?.Banners.FirstOrDefault(b => b.Id == id);
            if (banner == null)
                return NotFound("Banner does not exist.");

            doc.Banners.Remove(banner);
            RavenSession.Advanced.Attachments.Delete(BlogBanners.DocumentId, banner.AttachmentName);

            _cacheSignal.Invalidate(CacheKeys.BannerArea);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { Success = true });

            return RedirectToAction("Index");
        }
    }
}
