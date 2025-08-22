// -----------------------------------------------------------------------
//  <copyright file="SocialController.cs" company="RavenDB LTD">
//      Copyright (c) RavenDB LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web.Controllers
{
	public partial class SocialController : RaccoonController
	{
		public virtual ActionResult Login(string provider, string redirectUrl)
		{
			Session["Dummy"] = "Dummy"; // OWIN External Login hack

			// Request a redirect to the external login provider
			return new ChallengeResult(provider, Url.Action("ExternalLoginCallback", "Social", new { ReturnUrl = redirectUrl }));
		}

		private IAuthenticationManager AuthenticationManager
		{
			get
			{
				return HttpContext.GetOwinContext().Authentication;
			}
		}


		private const string XsrfKey = "XsrfId";

		internal class ChallengeResult : HttpUnauthorizedResult
		{
			public ChallengeResult(string provider, string returnUrl)
				: this(provider, returnUrl, null)
			{
			}

			public ChallengeResult(string provider, string redirectUri, string userId)
			{
				LoginProvider = provider;
				RedirectUri = redirectUri;
				UserId = userId;
			}

			public string LoginProvider { get; set; }
			public string RedirectUri { get; set; }
			public string UserId { get; set; }

			public override void ExecuteResult(ControllerContext context)
			{
				var properties = new AuthenticationProperties { RedirectUri = RedirectUri };
				if (UserId != null)
				{
					properties.Dictionary[XsrfKey] = UserId;
				}
				context.HttpContext.GetOwinContext().Authentication.Challenge(properties, LoginProvider);
			}
		}

		private static void SetCommenterValuesFromResponse(ExternalLoginInfo response, Commenter commenter)
		{
			var claims = response.ExternalIdentity;

			var emailClaim = claims.FindFirst(ClaimTypes.Email) ??
                claims.FindFirst("email");
			var nameClaim = claims.FindFirst(ClaimTypes.Name);
			var urlClaim = claims.FindFirst("urn:google:profile");

			if (string.IsNullOrWhiteSpace(commenter.Email) && emailClaim != null && string.IsNullOrWhiteSpace(emailClaim.Value) == false)
				commenter.Email = emailClaim.Value;

			if (string.IsNullOrWhiteSpace(commenter.Name) && nameClaim != null && string.IsNullOrWhiteSpace(nameClaim.Value) == false)
				commenter.Name = nameClaim.Value;

			if (string.IsNullOrWhiteSpace(commenter.Url) && urlClaim != null && string.IsNullOrWhiteSpace(urlClaim.Value) == false)
				commenter.Url = urlClaim.Value;
		}
		
		[AllowAnonymous]
		public virtual async Task<ActionResult> ExternalLoginCallback(string returnUrl)
		{
			Session["Dummy"] = "Dummy"; // OWIN External Login hack

			Uri returnUri;
			Uri.TryCreate(returnUrl, UriKind.Absolute, out returnUri);

			var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync();
			if (loginInfo == null || returnUri == null)
			{
				return returnUri != null ? (ActionResult)Redirect(returnUri.AbsoluteUri) : RedirectToRoute("homepage");
			}

			var claimedIdentifier = loginInfo.Login.ProviderKey + "@" + loginInfo.Login.LoginProvider;
			var commenter = RavenSession.Query<Commenter>()
								.FirstOrDefault(c => c.OpenId == claimedIdentifier) ?? new Commenter
																					   {
																						   Key = Guid.NewGuid(),
																						   OpenId = claimedIdentifier,
																					   };

			SetCommenterValuesFromResponse(loginInfo, commenter);

			CommenterUtil.SetCommenterCookie(Response, commenter.Key.MapTo<string>());
			RavenSession.Store(commenter);

			return Redirect(returnUri.AbsoluteUri);
		}
	}
}
