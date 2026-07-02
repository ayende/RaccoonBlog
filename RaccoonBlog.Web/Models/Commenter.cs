using System;

namespace RaccoonBlog.Web.Models
{
	public class Commenter
	{
		public string Id { get; set; }
		public Guid Key { get; set; }
		public bool? IsTrustedCommenter { get; set; }

		public string Name { get; set; }
		public string Email { get; set; }
		public string Url { get; set; }


		public int NumberOfSpamComments { get; set; }
	}
}