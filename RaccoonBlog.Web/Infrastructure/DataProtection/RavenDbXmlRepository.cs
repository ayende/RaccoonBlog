using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Raven.Client.Documents;

namespace RaccoonBlog.Web.Infrastructure.DataProtection;

public class RavenDbXmlRepository : IXmlRepository
{
    private const string IdPrefix = "DataProtectionKeys/";

    private readonly IDocumentStore _documentStore;

    public RavenDbXmlRepository(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var session = _documentStore.OpenSession();
        return session.Query<DataProtectionKey>()
            .Where(k => k.Id.StartsWith(IdPrefix))
            .ToList()
            .Select(k => XElement.Parse(k.Xml))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var session = _documentStore.OpenSession();
        var id = IdPrefix + (string.IsNullOrEmpty(friendlyName) ? System.Guid.NewGuid().ToString() : friendlyName);
        session.Store(new DataProtectionKey { Id = id, Xml = element.ToString() }, id);
        session.SaveChanges();
    }

    private class DataProtectionKey
    {
        public string Id { get; set; }
        public string Xml { get; set; }
    }
}
