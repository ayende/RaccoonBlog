using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RaccoonBlog.Web.Infrastructure.DataProtection;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Raven.Embedded;
using System;
using System.Linq;

namespace RaccoonBlog.IntegrationTests.Infrastructure
{
    /// <summary>
    /// Custom WebApplicationFactory for RaccoonBlog integration tests.
    /// Provides isolated RavenDB instances for each test.
    /// </summary>
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private IDocumentStore _testDocumentStore;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                // Remove the existing IDocumentStore registration
                var documentStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentStore));
                if (documentStoreDescriptor != null)
                {
                    services.Remove(documentStoreDescriptor);
                }

                // Remove the existing IDocumentSession registration
                var sessionDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDocumentSession));
                if (sessionDescriptor != null)
                {
                    services.Remove(sessionDescriptor);
                }

                // Remove the existing BlogConfig registration if any
                var blogConfigDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(RaccoonBlog.Web.Models.BlogConfig));
                if (blogConfigDescriptor != null)
                {
                    services.Remove(blogConfigDescriptor);
                }

                // Create a new test DocumentStore with a unique database name
                _testDocumentStore = EmbeddedServerHelper.Instance.GetDocumentStore($"TestDb_{Guid.NewGuid()}");
                
                IndexCreation.CreateIndexes(typeof(Program).Assembly, _testDocumentStore);
                
                // Configure test store to wait for non-stale results
                _testDocumentStore.OnBeforeQuery += (sender, args) =>
                {
                    args.QueryCustomization.WaitForNonStaleResults();
                };

                // Register the test DocumentStore
                services.AddSingleton(_testDocumentStore);
                
                services.Configure<KeyManagementOptions>(options =>
                {
                    options.XmlRepository = new RavenDbXmlRepository(_testDocumentStore);
                });

                // Register scoped IDocumentSession
                services.AddScoped<IDocumentSession>(provider =>
                {
                    var store = provider.GetRequiredService<IDocumentStore>();
                    return store.OpenSession();
                });

                // Register BlogConfig
                services.AddScoped<RaccoonBlog.Web.Models.BlogConfig>(provider =>
                {
                    var session = provider.GetRequiredService<IDocumentSession>();
                    using (session.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
                    {
                        return session.Load<RaccoonBlog.Web.Models.BlogConfig>("Blog/Config")
                               ?? new RaccoonBlog.Web.Models.BlogConfig();
                    }
                });
            });
        }

        /// <summary>
        /// Gets the test document store for setting up test data.
        /// </summary>
        public IDocumentStore GetDocumentStore() => _testDocumentStore;

        /// <summary>
        /// Helper method to setup test data before running tests.
        /// </summary>
        public void SetupData(Action<IDocumentSession> setupAction)
        {
            using var session = _testDocumentStore.OpenSession();
            setupAction(session);
            session.SaveChanges();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _testDocumentStore?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
