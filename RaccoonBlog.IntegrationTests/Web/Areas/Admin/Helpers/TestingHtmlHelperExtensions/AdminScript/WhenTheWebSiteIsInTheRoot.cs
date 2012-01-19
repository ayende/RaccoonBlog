using System.Web.Mvc;
using RaccoonBlog.Web.Areas.Admin.Helpers;
using Rhino.Mocks;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Web.Areas.Admin.Helpers.TestingHtmlHelperExtensions.AdminScript
{
    public class WhenTheWebSiteIsInTheRoot : WhenTestingTheClass
    {
        public WhenTheWebSiteIsInTheRoot()
        {
            HttpRequest.Stub(hr => hr.ApplicationPath).Return("/");
        }

        [Fact]
        public void TheAdminImagePathShouldBeAtTheRoot()
        {
            MvcHtmlString path = HtmlHelperExtensions.AdminScript(HtmlHelper, "test.js");
            Assert.Equal("/areas/admin/content/js/test.js", path.ToHtmlString());
        }
    }
}
