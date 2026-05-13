using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Raven.Client.Documents;
using Raven.Client.Exceptions;

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

        return session.Advanced.LoadStartingWith<DataProtectionKey>(IdPrefix)
            .Select(k => XElement.Parse(k.Xml))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var session = _documentStore.OpenSession();
        session.Advanced.UseOptimisticConcurrency = true;

        var id = IdPrefix + (string.IsNullOrEmpty(friendlyName) ? System.Guid.NewGuid().ToString() : friendlyName);
        try
        {
            session.Store(new DataProtectionKey { Id = id, Xml = element.ToString() });
            session.SaveChanges();
        }
        catch (ConcurrencyException)
        {
            // another instance already stored the same key - noop GetAllElements() will load the existing key
        }
    }
}

internal class DataProtectionKey
{
    public string Id { get; set; }
    public string Xml { get; set; }
}