using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using HibernatingRhinos.Loci.Common.Extensions;
using RaccoonBlog.Web.Controllers;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Helpers.Attributes;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.Services;
using RaccoonBlog.Web.ViewModels;
using Raven.Abstractions.Commands;
using Raven.Abstractions.Data;
using Raven.Json.Linq;


namespace RaccoonBlog.Web.Areas.Admin.Controllers
{
	public class PostsController : AdminController
	{
		public ActionResult Index()
		{
			// the actual UI is handled via JavaScript
			return View("List");
		}

		[HttpGet]
		public ActionResult Add()
		{
			return View("Edit", new PostInput());
		}

		[HttpGet]
		public ActionResult Edit(int id)
		{
			var post = RavenSession.Load<Post>(id);
			if (post == null)
				return HttpNotFound("Post does not exist.");
			return View(post.MapTo<PostInput>());
		}

		[HttpPost]
		[ValidateInput(false)]
		public ActionResult Update(PostInput input)
		{
			if (!ModelState.IsValid)
				return View("Edit", input);

			var post = RavenSession.Load<Post>(input.Id) ?? new Post { CreatedAt = DateTimeOffset.Now };
			input.MapPropertiesToInstance(post);

			// Be able to record the user making the actual post
			var user = RavenSession.GetCurrentUser();
			if (string.IsNullOrEmpty(post.AuthorId))
			{
				post.AuthorId = user.Id;
			}
			else
			{
				post.LastEditedByUserId = user.Id;
				post.LastEditedAt = DateTimeOffset.Now;
			}

			if (post.PublishAt == DateTimeOffset.MinValue)
			{
				var postScheduleringStrategy = new PostSchedulingStrategy(RavenSession, DateTimeOffset.Now);
				post.PublishAt = postScheduleringStrategy.Schedule();
			}

			if (input.IsNewPost())
			{
				// Create the post comments object and link between it and the post
				var comments = new PostComments
				{
					Comments = new List<PostComments.Comment>(),
					Spam = new List<PostComments.Comment>(),
					Post = new PostComments.PostReference
					{
						Id = post.Id,
						PublishAt = post.PublishAt,
					}
				};

				RavenSession.Store(comments);
                post.CommentsId = comments.Id;
			}

            // Actually save the post now
            RavenSession.Store(post);

			return RedirectToAction("Details", new { Id = post.MapTo<PostReference>().DomainId });
		}

		public ActionResult Details(int id)
		{
			var post = RavenSession
				.Include<Post>(x => x.CommentsId)
				.Load(id);

			if (post == null)
				return HttpNotFound();

			var comments = RavenSession.Load<PostComments>(post.CommentsId);

			var vm = new AdminPostDetailsViewModel
					 {
						Post = post.MapTo<AdminPostDetailsViewModel.PostDetails>(),

						Comments = comments.Comments
							.Concat(comments.Spam)
							.OrderBy(comment => comment.CreatedAt)
							.MapTo<AdminPostDetailsViewModel.Comment>(),

						NextPost = RavenSession.GetNextPrevPost(post, true),
						PreviousPost = RavenSession.GetNextPrevPost(post, false),
						AreCommentsClosed = comments.AreCommentsClosed(post, BlogConfig.NumberOfDayToCloseComments),
					 };

			return View("Details", vm);
		}

		public ActionResult ListFeed(long start, long end)
		{
			var startAsDateTimeOffset = DateTimeOffsetUtil.ConvertFromUnixTimestamp(start);
			var endAsDateTimeOffset = DateTimeOffsetUtil.ConvertFromUnixTimestamp(end);

			var posts = RavenSession.Query<Post>()
				.Where(post => post.IsDeleted == false)
				.Where
				(
					post => post.PublishAt >= startAsDateTimeOffset &&
							post.PublishAt <= endAsDateTimeOffset
				)
				.OrderBy(post => post.PublishAt)
				.Take(256)
				.ToList();

			return Json(posts.MapTo<PostSummaryJson>());
		}

	
		[HttpPost]
		[AjaxOnly]
		public ActionResult SetPostDate(int id, long date)
		{
			var post = RavenSession
				.Include<Post>(x => x.CommentsId)
				.Load(id);
			if (post == null)
				return Json(new {success = false});

			post.PublishAt = post.PublishAt.WithDate(DateTimeOffsetUtil.ConvertFromJsTimestamp(date));
			RavenSession.Load<PostComments>(post.CommentsId).Post.PublishAt = post.PublishAt;

			return Json(new {success = true});
		}

		[HttpPost]
		public ActionResult CommentsAdmin(int id, CommentCommandOptions command, int[] commentIds)
		{
			if (commentIds == null || commentIds.Length == 0)
				ModelState.AddModelError("CommentIdsAreEmpty", "Not comments was selected.");

			var post = RavenSession.Load<Post>(id);
			if (post == null)
				return HttpNotFound();

			if (ModelState.IsValid == false)
			{
				if (Request.IsAjaxRequest())
					return Json(new {Success = false, message = ModelState.FirstErrorMessage()});

				return Details(id);
			}

			var comments = RavenSession.Load<PostComments>(post.CommentsId);
			switch (command)
			{
				case CommentCommandOptions.Delete:
					comments.Comments.RemoveAll(c => commentIds.Contains(c.Id));
					comments.Spam.RemoveAll(c => commentIds.Contains(c.Id));
					break;

				case CommentCommandOptions.MarkSpam:
					var spams = comments.Comments.Concat(comments.Spam)
						.Where(c => commentIds.Contains(c.Id))
						.ToArray();

					comments.Comments.RemoveAll(spams.Contains);
					comments.Spam.RemoveAll(spams.Contains);
					foreach (var comment in spams)
					{
						AkismetService.MarkSpam(comment);
					}
					break;

				case CommentCommandOptions.MarkHam:
					var ham = comments.Spam
						.Where(c => commentIds.Contains(c.Id))
						.ToArray();

					comments.Spam.RemoveAll(ham.Contains);
					comments.Comments.AddRange(ham);

					comments.Comments
						.Where(c => c.IsSpam)
						.ForEach(comment =>
						         	{
						         		comment.IsSpam = false;
						         		AkismetService.MarkHam(comment);
						         	});
					break;
				default:
					throw new InvalidOperationException(command + " command is not recognized.");
			}

			post.CommentsCount = comments.Comments.Count;

			if (Request.IsAjaxRequest())
			{
				return Json(new {Success = true});
			}
			return RedirectToAction("Details", new {id});
		}

		[HttpPost]
		public ActionResult Delete(int id)
		{
			var post = RavenSession.Load<Post>(id);
			post.IsDeleted = true;

			if (Request.IsAjaxRequest())
			{
				return Json(new {Success = true});
			}
			return RedirectToAction("Index");
		}

		[HttpGet]
		public ActionResult DeleteAllSpamComments()
		{
			return View();
		}

		[HttpPost]
		public ActionResult DeleteAllSpamComments(bool deleteAll)
		{
			var patchCommands = RavenSession.Query<PostComments>()
				.Where(x => x.Spam.Count > 0)
				.ToList()
				.Select(x => new PatchCommandData
							 {
								Key = x.Id,
								Patches = new[]
										  {
											new PatchRequest
											{
												Type = PatchCommandType.Set,
												Name = "Spam",
												Value = new RavenJArray(),
											},
										  }
							 });

			RavenSession.Advanced.DatabaseCommands.Batch(patchCommands);

			return View();
		}
	}

	public enum CommentCommandOptions
	{
		Delete,
		MarkHam,
		MarkSpam
	}
}