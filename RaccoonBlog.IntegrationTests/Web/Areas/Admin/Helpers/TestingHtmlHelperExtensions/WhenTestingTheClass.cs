using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Rhino.Mocks;

namespace RaccoonBlog.IntegrationTests.Web.Areas.Admin.Helpers.TestingHtmlHelperExtensions
{
    public abstract class WhenTestingTheClass : IDisposable
    {
        protected HttpContextBase HttpContext { get; set; }
        protected ViewContext ViewContext { get; set; }
        protected HttpRequestBase HttpRequest { get; set; }
        protected HttpResponseBase HttpResponse { get; set; }
        protected RouteData RouteData { get; set; }
        protected ViewDataDictionary ViewDataDictionary { get; set; }
        protected RequestContext RequestContext { get; set; }

        protected HtmlHelper HtmlHelper { get; set; }

        public WhenTestingTheClass()
        {
            HttpRequest = MockRepository.GenerateMock<HttpRequestBase>();
            HttpResponse = MockRepository.GenerateMock<HttpResponseBase>();

            HttpResponse.Stub(hr => hr.ApplyAppPathModifier(Arg<string>.Is.Anything)).WhenCalled(mi => mi.ReturnValue = mi.Arguments[0]).Return(null);

            HttpContext = MockRepository.GenerateMock<HttpContextBase>();
            HttpContext.Stub(hc => hc.Request).Return(HttpRequest);
            HttpContext.Stub(hc => hc.Response).Return(HttpResponse);

            RouteData = new RouteData();
            RouteData.Values.Add("controller", "home");
            RouteData.Values.Add("action", "id");
            RouteData.Values.Add("id", 1);

            ViewDataDictionary = new ViewDataDictionary();

            RequestContext = new RequestContext(HttpContext, RouteData);
            ViewContext = MockRepository.GenerateMock<ViewContext>();

            ViewContext.Stub(vc => vc.HttpContext).Return(HttpContext);
            ViewContext.RequestContext = RequestContext;
            ViewContext.Stub(vc => vc.RouteData).Return(RouteData);
            ViewContext.Stub(vc => vc.ViewData).Return(ViewDataDictionary);


            HtmlHelper = new HtmlHelper(ViewContext, new ViewPage());
            if (HtmlHelper.RouteCollection.Count == 0)
            {
                HtmlHelper.RouteCollection.MapRoute(
                    "Default",                                              // Route name
                    "{controller}/{action}/{id}",                           // URL with parameters
                    new { controller = "Home", action = "Index", id = "" }  // Parameter defaults
                );
            }
        }

        public void Dispose()
        {
            return;
        }
    }
}
