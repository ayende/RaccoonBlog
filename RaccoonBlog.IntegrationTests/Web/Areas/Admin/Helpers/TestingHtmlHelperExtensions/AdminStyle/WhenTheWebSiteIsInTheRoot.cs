using System.Web.Mvc;
using RaccoonBlog.Web.Areas.Admin.Helpers;
using Rhino.Mocks;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Web.Areas.Admin.Helpers.TestingHtmlHelperExtensions.AdminStyle
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
            MvcHtmlString path = HtmlHelperExtensions.AdminStyle(HtmlHelper, "test.css");
            Assert.Equal("/areas/admin/content/css/test.css", path.ToHtmlString());
        }
    }
}
