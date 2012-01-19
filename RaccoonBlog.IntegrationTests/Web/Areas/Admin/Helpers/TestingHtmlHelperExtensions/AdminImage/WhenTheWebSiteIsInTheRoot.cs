using System.Web.Mvc;
using RaccoonBlog.Web.Areas.Admin.Helpers;
using Rhino.Mocks;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Web.Areas.Admin.Helpers.TestingHtmlHelperExtensions.AdminImage
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

            MvcHtmlString path = HtmlHelperExtensions.AdminImage(HtmlHelper, "test.jpg");
            Assert.Equal("/areas/admin/content/images/test.jpg", path.ToHtmlString());
        }
    }
}
