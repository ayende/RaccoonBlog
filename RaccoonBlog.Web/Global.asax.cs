using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using DataAnnotationsExtensions.ClientValidation;
using FluentScheduler;
using HibernatingRhinos.Loci.Common.Tasks;
using NLog;
using RaccoonBlog.Web.Areas.Admin.Controllers;
using RaccoonBlog.Web.Controllers;
using RaccoonBlog.Web.Helpers.Binders;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Infrastructure.Indexes;
using RaccoonBlog.Web.Infrastructure.Jobs;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Raven.Client.Http;

namespace RaccoonBlog.Web
{
	public class MvcApplication : HttpApplication
	{
        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();

		public MvcApplication()
		{
			BeginRequest += (sender, args) =>
			{
				BundleConfig.RegisterThemeBundles(HttpContext.Current, BundleTable.Bundles);
				HttpContext.Current.Items["CurrentRequestRavenSession"] = RaccoonController.DocumentStore.OpenSession();
			};
			EndRequest += (sender, args) =>
			{
				using (var session = (IDocumentSession)HttpContext.Current.Items["CurrentRequestRavenSession"])
				{
					if (session == null)
						return;

					if (Server.GetLastError() != null)
						return;

					session.SaveChanges();
				}
				TaskExecutor.StartExecuting();
			};
		}

		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();

			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			new RouteConfigurator(RouteTable.Routes).Configure();

			InitializeDocumentStore();
			LogManager.GetCurrentClassLogger().Info("Started Raccoon Blog");

			ModelBinders.Binders.Add(typeof(CommentCommandOptions), new RemoveSpacesEnumBinder());
			ModelBinders.Binders.Add(typeof(Guid), new GuidBinder());

			DataAnnotationsModelValidatorProviderExtensions.RegisterValidationExtensions();

			AutoMapperConfiguration.Configure();
			BundleConfig.RegisterBundles(BundleTable.Bundles);

			RaccoonController.DocumentStore = DocumentStore;
			TaskExecutor.DocumentStore = DocumentStore;

            JobManager.JobException += JobExceptionHandler;
            JobManager.Initialize(new SocialNetworkIntegrationJobsRegistry());
        }

	    public static IDocumentStore DocumentStore { get; private set; }

        static void JobExceptionHandler(JobExceptionInfo info)
        {
            _log.Fatal(info.Exception, $"Error executing background job {info.Name}.");
        }

	    private static void InitializeDocumentStore()
	    {
	        if (DocumentStore != null) return; // prevent misuse

	        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.CheckCertificateRevocationList = false;
            ServicePointManager.ServerCertificateValidationCallback += OnServerCertificateCustomValidationCallback;

	        var urls = WebConfigurationManager.AppSettings["Raven/Urls"];
	        var database = WebConfigurationManager.AppSettings["Raven/Database"];
	        var store = new DocumentStore
	        {
	            Urls = urls.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries),
	            Database = database,
                Conventions = new DocumentConventions
                {
                    AggressiveCache = { Mode = AggressiveCacheMode.DoNotTrackChanges }
                }
	        };

	        var certificatePath = WebConfigurationManager.AppSettings["Raven/CertificatePath"];
	        if (certificatePath != null)
	        {
	            var certificatePassword = WebConfigurationManager.AppSettings["Raven/CertificatePassword"];
	            var certificate = new X509Certificate2(certificatePath, certificatePassword);
	            store.Certificate = certificate;
	        }

		    var requestsTimeout = WebConfigurationManager.AppSettings["Raven/RequestsTimeoutInSec"];
		    if (requestsTimeout != null && int.TryParse(requestsTimeout, out int seconds))
		    {
			    store.Conventions.RequestTimeout = TimeSpan.FromSeconds(seconds);
		    }
	        
		    DocumentStore = store.Initialize();

	        // TryCreatingIndexesOrRedirectToErrorPage();
	    }

	    private static bool OnServerCertificateCustomValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslpolicyerrors)
	    {
	        return true;
	    }

	    private static void TryCreatingIndexesOrRedirectToErrorPage()
		{
			try
			{
				IndexCreation.CreateIndexes(typeof(Tags_Count).Assembly, DocumentStore);
                IndexCreation.CreateIndexes(typeof(Posts_Series).Assembly, DocumentStore);
			}
			catch (WebException e)
			{
				var socketException = e.InnerException as SocketException;
				if (socketException == null)
					throw;

				switch (socketException.SocketErrorCode)
				{
					case SocketError.AddressNotAvailable:
					case SocketError.NetworkDown:
					case SocketError.NetworkUnreachable:
					case SocketError.ConnectionAborted:
					case SocketError.ConnectionReset:
					case SocketError.TimedOut:
					case SocketError.ConnectionRefused:
					case SocketError.HostDown:
					case SocketError.HostUnreachable:
					case SocketError.HostNotFound:
						HttpContext.Current.Response.Redirect("~/RavenNotReachable.htm");
						break;
					default:
						throw;
				}
			}
		}
	}
}