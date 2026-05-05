using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using HibernatingRhinos.Loci.Common.Utils;
using RaccoonBlog.Web.Models;
using Raven.Client;
using Raven.Client.Document;
using Raven.Client.Embedded;
using Sgml;

namespace RaccoonBlog.ImportFromSubtext
{
	internal class Program
	{
	    #region GetDocumentStore

	    private static IDocumentStore GetDocumentStore(bool isTest)
	    {
	        return isTest ? GetInMemoryDocumentStore() : GetDocumentStore();
	    }

	    private static IDocumentStore GetInMemoryDocumentStore()
	    {
	        var documentStore = new EmbeddableDocumentStore
	            {
	                RunInMemory = true
	            };

	        documentStore.RegisterListener(new NoStaleQueriesAllowed());
	        return documentStore;
	    }

		private static IDocumentStore GetDocumentStore()
	    {
	        return new DocumentStore
	            {
	                Url = "http://localhost:8080",
	                DefaultDatabase = "RaccoonBlog"
	            };
	    }

	    #endregion

		private static void Main(string[] args)
		{
            bool isTest = args.Length >= 1 && args[0].Equals("-t");
            using (var store = GetDocumentStore(isTest).Initialize())
			{
				ImportDatabase(store);
				CreateSections(store);
				CreateConfig(store);
			}
			Console.WriteLine("Done importing");
		}

		private static void CreateConfig(IDocumentStore store)
		{
			using (IDocumentSession s = store.OpenSession())
			{
				var config = BlogConfig.New();
				config.Id = BlogConfig.Key;
				config.CustomCss = "hibernatingrhinos";
				config.Subtitle = "Unnatural acts on source code";
				config.Title = "Ayende @ Rahien";
				config.Copyright = "Ayende Rahien";
				config.GenAiSpamCheckModel = "gpt-4";

				s.Store(config);
				s.SaveChanges();
			}
		}

		private static void CreateSections(IDocumentStore store)
		{
			Console.WriteLine("Creating sections");
			using (IDocumentSession s = store.OpenSession())
			{
				var sections = new[]
					{
						new Section {Title = "Future Posts", ControllerName = "Section", ActionName = "FuturePosts"},
						new Section {Title = "Statistics", ControllerName = "Section", ActionName = "PostsStatistics"},
						new Section {Title = "Tags", ControllerName = "Section", ActionName = "TagsList"},
						new Section {Title = "Archive", ControllerName = "Section", ActionName = "ArchivesList"},
					};

				var i = 0;
				foreach (var section in sections)
				{
					section.Position = i;
					section.IsActive = true;
					s.Store(section);
					i++;
				}
				s.SaveChanges();
			}
			Console.WriteLine("Finish creating sections");
		}

		private static void ImportDatabase(IDocumentStore store)
		{
			Stopwatch sp = Stopwatch.StartNew();

			using (var e = new SubtextEntities())
			{
				Console.WriteLine("Starting...");

				IOrderedEnumerable<Post> theEntireDatabaseOhMygod = e.Posts
					.Include("Comments")
					.Include("Links")
					.Include("Links.Categories")
					.ToList()
					.OrderBy(x => x.DateSyndicated);

				Console.WriteLine("Loading data took {0:#,#} ms", sp.ElapsedMilliseconds);

				var usersList = new List<User>();
				using (IDocumentSession s = store.OpenSession())
				{
					var users = new[]
					{
						new {Email = "ayende@ayende.com", FullName = "Ayende Rahien", TwitterNick = "ayende", RelatedTwitterNick=(string)null},
						new {Email = "fitzchak@ayende.com", FullName = "Fitzchak Yitzchaki", TwitterNick = "fitzchak", RelatedTwitterNick="ayende"},
					};
					for (int i = 0; i < users.Length; i++)
					{
						var user = new User
							{
								Id = "users/" + (i + 1),
								Email = users[i].Email,
								FullName = users[i].FullName,
								TwitterNick = users[i].TwitterNick,
								RelatedTwitterNick = users[i].RelatedTwitterNick,
								Enabled = true,
							};
						user.SetPassword("123456");
						s.Store(user);
						usersList.Add(user);
					}
					s.SaveChanges();
				}

				foreach (Post post in theEntireDatabaseOhMygod)
				{
					var ravenPost = new Web.Models.Post
						{
							AuthorId = usersList
									.Where(u=> u.FullName == post.Author)
									.Select(u => u.Id)
									.FirstOrDefault() ?? 
									usersList.First().Id,
							CreatedAt = new DateTimeOffset(post.DateAdded),
							PublishAt = new DateTimeOffset(post.DateSyndicated ?? post.DateAdded),
							Body = post.Text,
							LegacySlug = post.EntryName,
							Title = HttpUtility.HtmlDecode(post.Title),
							Tags = post.Links.Select(x => x.Categories.Title)
								.Where(x => x != "Uncategorized")
								.ToArray(),
							AllowComments = true
						};

					var commentsCollection = new PostComments();
					commentsCollection.Comments = post.Comments
						.Where(comment => comment.StatusFlag == 1)
						.OrderBy(comment => comment.DateCreated)
						.Select(
							comment => new PostComments.Comment
								{
									Id = commentsCollection.GenerateNewCommentId(),
									Author = comment.Author,
									Body = ConvertCommentToMarkdown(comment.Body),
									CreatedAt = comment.DateCreated,
									Email = comment.Email,
									Url = comment.Url,
									Important = comment.IsBlogAuthor ?? false,
									UserAgent = comment.UserAgent,
									UserHostAddress = comment.IpAddress,
									IsSpam = false,
									CommenterId = null,
								}
						).ToList();
					commentsCollection.Spam = post.Comments
						.Where(comment => comment.StatusFlag != 1)
						.OrderBy(comment => comment.DateCreated)
						.Select(
							comment => new PostComments.Comment
								{
									Id = commentsCollection.GenerateNewCommentId(),
									Author = comment.Author,
									Body = ConvertCommentToMarkdown(comment.Body),
									CreatedAt = comment.DateCreated,
									Email = comment.Email,
									Url = comment.Url,
									Important = comment.IsBlogAuthor ?? false,
									UserAgent = comment.UserAgent,
									UserHostAddress = comment.IpAddress,
									IsSpam = true,
									CommenterId = null,
								}
						).ToList();

					ravenPost.CommentsCount = commentsCollection.Comments.Count;

					using (IDocumentSession s = store.OpenSession())
					{
						s.Store(commentsCollection);
						ravenPost.CommentsId = commentsCollection.Id;

						s.Store(ravenPost);
						commentsCollection.Post = new PostComments.PostReference
						{
							Id = ravenPost.Id,
							PublishAt = ravenPost.PublishAt
						};

						s.SaveChanges();
					}
				}
			}
			Console.WriteLine(sp.Elapsed);
		}

		private static string ConvertCommentToMarkdown(string body)
		{
			var sb = new StringBuilder();

			var sgmlReader = new SgmlReader
				{
					InputStream = new StringReader(body),
					DocType = "HTML",
					WhitespaceHandling = WhitespaceHandling.Significant,
					CaseFolding = CaseFolding.ToLower
				};

			bool outputEndElement = false;
			int indentLevel = 0;
			while (sgmlReader.Read())
			{
				switch (sgmlReader.NodeType)
				{
					case XmlNodeType.Text:
						if (indentLevel > 0)
							sb.Append("\t");
						sb.AppendLine(sgmlReader.Value);
						break;
					case XmlNodeType.Element:
						switch (sgmlReader.LocalName)
						{
							case "h1":
								sb.Append("## ");
								break;
							case "br":
								sb.AppendLine("  ");
								break;
							case "a":
								if (sgmlReader.MoveToAttribute("href"))
								{
									string url = sgmlReader.Value;
									sgmlReader.Read();

									sb.AppendFormat("[{0}]({1})", sgmlReader.Value, url);
								}
								break;
							case "html":
								break;
							case "strong":
							case "b":
								sb.AppendFormat("**{0}**", sgmlReader.Value);
								break;
							case "i":
							case "em":
								sb.AppendFormat("_{0}_", sgmlReader.Value);
								break;
							case "li":
								sb.AppendFormat("- {0}", sgmlReader.Value);
								break;
							case "pre":
							case "code":
							case "quote":
								indentLevel = 1;
								break;
							case "ul":
							case "ol":
							case "img":
								break;
							default:
								outputEndElement = true;
								sb.Append("<").Append(sgmlReader.LocalName);
								break;
						}
						break;
					case XmlNodeType.SignificantWhitespace:
					case XmlNodeType.Whitespace:
					case XmlNodeType.CDATA:
						break;
					case XmlNodeType.EndElement:
						indentLevel = 0;
						if (outputEndElement)
							sb.Append(">");
						outputEndElement = false;
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			return sb.ToString();
		}
	}
}
