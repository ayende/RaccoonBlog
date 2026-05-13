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

		public XmlResult(XDocument document, string etag)
		{
			_document = document;
			_etag = etag;
		}

		public async Task ExecuteResultAsync(ActionContext context)
		{
			if (_etag != null)
			{
				context.HttpContext.Response.Headers["ETag"] = _etag;
			}

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