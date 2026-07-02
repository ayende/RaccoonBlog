using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace RaccoonBlog.Web.Helpers
{
    public class Recaptcha2Helper
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContext;
        private readonly Recaptcha2Verifier _verifier;
        public const string ModelStateErrorKey = "CaptchaNotValid";

        public Recaptcha2Helper(IConfiguration config, IHttpContextAccessor httpContext, Recaptcha2Verifier verifier)
        {
            _config = config;
            _httpContext = httpContext;
            _verifier = verifier;
        }

        // reCAPTCHA is opt-in: it only applies when both keys are configured.
        // When unconfigured (e.g. local/dev), there is no widget to solve, so
        // validation is skipped rather than rejecting every comment.
        public bool IsConfigured =>
            !string.IsNullOrEmpty(_config["Recaptcha:SiteKey"]) &&
            !string.IsNullOrEmpty(_config["Recaptcha:Secret"]);

        public async Task<bool> Validate(ModelStateDictionary modelState)
        {
            if (IsConfigured == false) return true;

            var secret = _config["Recaptcha:Secret"];
            var token = _httpContext.HttpContext?.Request.Form["g-recaptcha-response"].ToString();

            var result = await _verifier.VerifyResponse(token, secret);
            if (result.IsValid) return true;

            modelState.AddModelError(ModelStateErrorKey, result.ErrorMessage);
            return false;
        }

        public IHtmlContent ScriptRef() =>
            IsConfigured
                ? new HtmlString("<script src='https://www.google.com/recaptcha/api.js'></script>")
                : HtmlString.Empty;

        public IHtmlContent Widget() =>
            IsConfigured
                ? new HtmlString($"<div class='g-recaptcha' data-sitekey='{_config["Recaptcha:SiteKey"]}'></div>")
                : HtmlString.Empty;
    }
}