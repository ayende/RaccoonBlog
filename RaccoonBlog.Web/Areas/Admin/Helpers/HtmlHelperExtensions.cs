using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Mvc;
using RaccoonBlog.Web.Areas.Admin.Models;

namespace RaccoonBlog.Web.Areas.Admin.Helpers
{
    public static class HtmlHelperExtensions
    {
        public static MvcHtmlString AdminImage(this HtmlHelper helper, string path)
        {
            return MvcHtmlString.Create(UrlHelper.GenerateContentUrl("~/areas/admin/content/images/" + path.Replace('\\', '/'), helper.ViewContext.HttpContext));
        }

        public static MvcHtmlString AdminScript(this HtmlHelper helper, string path)
        {
            return MvcHtmlString.Create(UrlHelper.GenerateContentUrl("~/areas/admin/content/js/" + path.Replace('\\', '/'), helper.ViewContext.HttpContext));
        }

        public static MvcHtmlString AdminStyle(this HtmlHelper helper, string path)
        {
            return MvcHtmlString.Create(UrlHelper.GenerateContentUrl("~/areas/admin/content/css/" + path.Replace('\\', '/'), helper.ViewContext.HttpContext));
        }

        public static MvcHtmlString AdminMenu(this HtmlHelper helper, IList<AdminMenu> menus)
        {
            if (menus == null)
                return MvcHtmlString.Empty;

            var sb = new StringBuilder();
            foreach (var m in menus)
            {
                sb.Append("<ul class=\"");
                sb.Append(m.IsCurrent ? "current" : "select");
                sb.AppendFormat(@"""><li><a href=""{1}""><b>{0}</b><!--[if IE 7]><!--></a><!--<![endif]-->", m.Title, string.IsNullOrWhiteSpace(m.Url) ? "#nogo" : m.Url);
                sb.AppendLine("<!--[if lte IE 6]><table><tr><td><![endif]-->");
                sb.AppendFormat(@"<div class=""select_sub{0}""><ul class=""sub"">", m.IsCurrent ? "_show" : string.Empty);

                if (m.SubMenus != null)
                {
                    foreach (var submenu in m.SubMenus)
                    {
                        sb.AppendFormat(@"<li{0}><a href=""{1}"">{2}</a></li>{3}",
                                        submenu.IsCurrent ? @" class=""sub_show""" : string.Empty,
                                        submenu.Url,
                                        submenu.Title,
                                        Environment.NewLine);
                    }
                }

                sb.AppendLine("</ul></div><!--[if lte IE 6]></td></tr></table></a><![endif]--></li></ul>");
                sb.AppendLine(@"<div class=""nav-divider"">&nbsp;</div>");
            }

            return MvcHtmlString.Create(sb.ToString());
        }
    }
}
