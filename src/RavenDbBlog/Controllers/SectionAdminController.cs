﻿using System.Linq;
using System.Web.Mvc;
using RavenDbBlog.Core.Models;
using RavenDbBlog.Helpers;
using RavenDbBlog.Infrastructure.AutoMapper;
using RavenDbBlog.ViewModels;

namespace RavenDbBlog.Controllers
{
    public class SectionAdminController : AdminController
    {
        public ActionResult List()
        {
            var sections = Session.Query<Section>()
                .ToList();

            return View(sections.MapTo<SectionSummery>());
        }

        [HttpGet]
        public ActionResult Add()
        {
            return View("Edit", new SectionInput());
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var section = Session.Load<Section>(id);
            if (section == null)
                return HttpNotFound("Section does not exist.");
            return View(section.MapTo<SectionInput>());
        }

        [HttpPost]
        public ActionResult Update(SectionInput input)
        {
            if (ModelState.IsValid)
            {
                var section = Session.Load<Section>(input.Id) ?? new Section();
                input.MapPropertiestoInstance(section);
                Session.Store(section);
                return RedirectToAction("List");
            }
            return View("Edit", input);
        }

        [AjaxOnly]
        [HttpPost]
        public ActionResult SetPosition(int id, int newPosition)
        {
            var section = Session.Load<Section>(id);
            if (section == null)
                return Json(new {success = false, message = string.Format("There is no post with id {0}", id)});

            if (section.Position == newPosition)
                return Json(new {success = false, message = string.Format("The {0} section has already this position", section.Title)});

            if (section.Position > newPosition)
            {
                var sections = Session.Query<Section>()
                    .Where(s => s.Position >= newPosition && s.Position < section.Position)
                    .OrderBy(s => s.Position);

                foreach (var section1 in sections)
                {
                    section1.Position++;
                }
            }
            else if (section.Position < newPosition)
            {
                var sections = Session.Query<Section>()
                    .Where(s => s.Position < newPosition && s.Position >= section.Position)
                    .OrderBy(s => s.Position);

                foreach (var section1 in sections)
                {
                    section1.Position--;
                }
            }

            section.Position = newPosition;
            return Json(new { success = true });
        }
    }
}