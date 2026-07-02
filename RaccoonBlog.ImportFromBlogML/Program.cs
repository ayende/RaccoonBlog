using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using BlogML.Xml;
using HibernatingRhinos.Loci.Common.Utils;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using Raven.Client;
using Sgml;

namespace RaccoonBlog.ImportFromBlogML
{
    public class Program : ImportBase
    {
        private readonly BlogMLBlog blog;
        private readonly bool isTest;

        public Program(BlogMLBlog blog, bool isTest)
        {
            this.blog = blog;
            this.isTest = isTest;
        }

        protected void CreateSections(IDocumentStore store)
        {
            using (var s = store.OpenSession())
            {
                s.Store(new Section { Title = "Archive", IsActive = true, Position = 1, ControllerName = "Section", ActionName = "ArchivesList" });
                s.Store(new Section { Title = "Tags", IsActive = true, Position = 2, ControllerName = "Section", ActionName = "TagsList" });
                s.Store(new Section { Title = "Statistics", IsActive = true, Position = 3, ControllerName = "Section", ActionName = "PostsStatistics" });
                s.Store(new Section { Title = "Future Posts", IsActive = true, Position = 4, ControllerName = "Section", ActionName = "FuturePosts" });
                s.SaveChanges();
            }
        }

        void Run()
        {
            using (var store = GetDocumentStore(isTest).Initialize())
            {
                ImportBlog(store, blog);
                ImportBlogPosts(store, blog);
                CreateSections(store);
            }
        }
        private static void Main(string[] args)
        {
            var blog = GetBlog(args[0]);
            bool isTest = args.Length >= 2 && args[1].Equals("-t");
            var runner = new Program(blog, isTest);
            runner.Run();
            Console.WriteLine("Done importing");
        }

        void ImportBlogPosts(IDocumentStore store, BlogMLBlog blog)
        {
            Stopwatch sp = Stopwatch.StartNew();

            var usersList = ImportUserList(store, blog);

            importBlogPosts(store, blog, usersList);

            Console.WriteLine(sp.Elapsed);
        }

        private void importBlogPosts(IDocumentStore store, BlogMLBlog blog, Dictionary<string, User> usersList)
        {
            foreach (var post in blog.Posts)
            {
                var authorId = getAuthorId(usersList, post);

                var ravenPost = new Post
                    {
                        AuthorId = authorId,
                        CreatedAt = new DateTimeOffset(post.DateCreated),
                        PublishAt = new DateTimeOffset(post.DateCreated),
                        Body = post.Content.Text,
                        LegacySlug = SlugConverter.TitleToSlug(post.PostName ?? post.Title),
                        Title = HttpUtility.HtmlDecode(post.Title),
                        Tags =
                            post.Categories.Cast<BlogMLCategoryReference>()
                                .Select(x => blog.Categories.First(c => c.ID == x.Ref).Title)
                                .ToArray(),
                        AllowComments = true
                    };

                var commentsCollection = new PostComments();
                commentsCollection.Spam = new List<PostComments.Comment>();
                commentsCollection.Comments = post.Comments.Cast<BlogMLComment>()
                                                  .Where(comment => comment.Approved)
                                                  .OrderBy(comment => comment.DateCreated)
                                                  .Select(
                                                      comment => new PostComments.Comment
                                                          {
                                                              Id = commentsCollection.GenerateNewCommentId(),
                                                              Author = comment.UserName,
                                                              Body = ConvertCommentToMarkdown(comment.Content.Text),
                                                              CreatedAt = comment.DateCreated,
                                                              Email = comment.UserEMail,
                                                              Url = comment.UserUrl,
                                                              Important =
                                                                  usersList.Any(
                                                                      u => u.Value.FullName == comment.UserName),
                                                              //UserAgent = comment.,
                                                              //UserHostAddress = comment.IpAddress,
                                                              SpamCheckStatus = SpamCheckStatus.Valid,
                                                              CommenterId = null,
                                                          }
                    ).ToList();
                commentsCollection.Spam = post.Comments.Cast<BlogMLComment>()
                                              .Where(comment => !comment.Approved)
                                              .OrderBy(comment => comment.DateCreated)
                                              .Select(
                                                  comment => new PostComments.Comment
                                                      {
                                                          Id = commentsCollection.GenerateNewCommentId(),
                                                          Author = comment.UserName,
                                                          Body = ConvertCommentToMarkdown(comment.Content.Text),
                                                          CreatedAt = comment.DateCreated,
                                                          Email = comment.UserEMail,
                                                          Url = comment.UserUrl,
                                                          Important =
                                                              usersList.Any(u => u.Value.FullName == comment.UserName),
                                                          //UserAgent = comment.UserAgent,
                                                          //UserHostAddress = comment.IpAddress,
                                                          SpamCheckStatus = SpamCheckStatus.Spam,
                                                          CommenterId = null,
                                                      }
                    ).Where(c => c.Body != null).ToList();

                ravenPost.CommentsCount = commentsCollection.Comments.Count;

                using (var s = store.OpenSession())
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

        private string getAuthorId(Dictionary<string, User> usersList, BlogMLPost post)
        {
            string authorId;
            User user;
            if (post.Authors.Count > 0 &&
                usersList.TryGetValue(post.Authors.Cast<BlogMLAuthorReference>().First().Ref, out user))
            {
                authorId = user.Id;
            }
            else
            {
                authorId = usersList.First().Value.Id;
            }
            return authorId;
        }

        private static Dictionary<string, User> ImportUserList(IDocumentStore store, BlogMLBlog blog)
        {
            var usersList = new Dictionary<string, User>();
            using (var s = store.OpenSession())
            {
                for (int i = 0; i < blog.Authors.Count; ++i)
                {
                    var user = new User
                        {
                            Id = "users/" + (i + 1),
                            FullName = blog.Authors[i].Title,
                            Email = blog.Authors[i].Email,
                            Enabled = blog.Authors[i].Approved,
                        };
                    user.SetPassword("123456");
                    s.Store(user);
                    usersList.Add(blog.Authors[i].ID, user);
                }
                s.SaveChanges();
            }
            return usersList;
        }

        void ImportBlog(IDocumentStore store, BlogMLBlog blog)
        {
            var config = BlogConfig.New();
            config.Title = blog.Title;
            config.Subtitle = blog.SubTitle;
            config.Copyright = String.Join(", ", blog.Authors.Select(author => author.Title));
            config.MetaDescription = String.Join(", ", blog.Categories.Select(category => category.Title));

            using (var session = store.OpenSession())
            {
                session.Store(config);
                session.SaveChanges();
            }
        }

        private static BlogMLBlog GetBlog(string filename)
        {
            using (var stream = File.OpenRead(filename))
            {
                return BlogMLSerializer.Deserialize(stream);
            }
        }

        string ConvertCommentToMarkdown(string body)
        {
            var sb = new StringBuilder();

            var sgmlReader = new SgmlReader
                {
                    InputStream = new StringReader(body),
                    DocType = "HTML",
                    WhitespaceHandling = WhitespaceHandling.Significant,
                    CaseFolding = CaseFolding.ToLower
                };

            try
            {
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
            catch
            {
                return null;
            }
        }
    }
}