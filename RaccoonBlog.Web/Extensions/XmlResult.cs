using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;

namespace HibernatingRhinos.Loci.Common.Extensions
{
	public class XmlResult : IActionResult
	{
		private readonly XDocument _document;
		private readonly string _etag;
		private readonly DateTimeOffset? _lastModified;

		public XmlResult(XDocument document, string etag, DateTimeOffset? lastModified = null)
		{
			_document = document;
			_etag = etag;
			_lastModified = lastModified;
		}

		public async Task ExecuteResultAsync(ActionContext context)
		{
			if (_etag != null)
				context.HttpContext.Response.Headers["ETag"] = _etag;

			if (_lastModified.HasValue)
				context.HttpContext.Response.Headers["Last-Modified"] = _lastModified.Value.ToString("R");

			context.HttpContext.Response.ContentType = "text/xml";

			await using (var xmlWriter = XmlWriter.Create(context.HttpContext.Response.Body, new XmlWriterSettings
			             {
				             Async = true,
				             Indent = false
			             }))
			{
				await _document.WriteToAsync(xmlWriter, context.HttpContext.RequestAborted);
				await xmlWriter.FlushAsync();
			}
		}
	}
}