using System.Web.Mvc;
using RaccoonBlog.Web.Areas.Admin.Helpers;
using Rhino.Mocks;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Web.Areas.Admin.Helpers.TestingHtmlHelperExtensions.AdminImage
{
    public class WhenTheWebSiteIsAnApplication : WhenTestingTheClass
    {
        public WhenTheWebSiteIsAnApplication()
        {
            HttpRequest.Stub(hr => hr.ApplicationPath).Return("/Not/The/Root");
        }

        [Fact]
        public void TheAdminImagePathShouldBeAtTheRoot()
        {
            MvcHtmlString path = HtmlHelperExtensions.AdminImage(HtmlHelper, "test.jpg");
            Assert.Equal("/Not/The/Root/areas/admin/content/images/test.jpg", path.ToHtmlString());
        }
    }
}
