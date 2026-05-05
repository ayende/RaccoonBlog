using System;
using System.Web;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles
{
	public class PostsAdminViewModelMapperProfile : AbstractProfile
	{
	    public PostsAdminViewModelMapperProfile()
	    {
	        CreateMap<Post, PostSummaryJson>()
	            .ForMember(x => x.Id, o => o.MapFrom(m => m.GetIdForUrl()))
	            .ForMember(x => x.Title, o => o.MapFrom(m => HttpUtility.HtmlDecode(m.Title)))
	            .ForMember(x => x.Start, o => o.MapFrom(m => m.PublishAt.ToString("yyyy-MM-ddTHH:mm:ssZ")))
	            .ForMember(x => x.Url, o => o.MapFrom(m => UrlHelper.Action("Details", "Posts", new {Id = m.GetIdForUrl(), Slug = SlugConverter.TitleToSlug(m.Title)})))
	            .ForMember(x => x.AllDay, o => o.UseValue(false))
	            ;

	        CreateMap<Post, PostInput>()
	            .ForMember(x => x.Id, o => o.MapFrom(m => m.GetIdForUrl()))
	            .ForMember(x => x.Tags, o => o.MapFrom(m => TagsResolver.ResolveTags(m.Tags)))
	            .ForMember(x => x.SeoKeywords, o => o.MapFrom(m => TagsResolver.ResolveTags(m.SeoKeywords)))
	            ;

	        CreateMap<PostInput, Post>()
	            .ForMember(x => x.Id, o => o.Ignore())
	            .ForMember(x => x.AuthorId, o => o.Ignore())
	            .ForMember(x => x.LegacySlug, o => o.Ignore())
	            .ForMember(x => x.ShowPostEvenIfPrivate, o => o.Ignore())
	            .ForMember(x => x.SkipAutoReschedule, o => o.Ignore())
	            .ForMember(x => x.CommentsCount, o => o.Ignore())
	            .ForMember(x => x.CommentsId, o => o.Ignore())
	            .ForMember(x => x.LastEditedByUserId, o => o.Ignore())
	            .ForMember(x => x.LastEditedAt, o => o.Ignore())
	            .ForMember(x => x.Tags, o => o.MapFrom(m => TagsResolver.ResolveTagsInput(m.Tags)))
	            .ForMember(x => x.SeoKeywords, o => o.MapFrom(m => TagsResolver.ResolveTagsInput(m.SeoKeywords)))
	            .ForMember(x => x.PublishAt, o => o.MapFrom(m => m.PublishAt.HasValue ? m.PublishAt.Value : DateTimeOffset.MinValue))
	            ;

	        CreateMap<Post, AdminPostDetailsViewModel.PostDetails>()
	            .ForMember(x => x.Id, o => o.MapFrom(m => m.GetIdForUrl()))
	            .ForMember(x => x.Slug, o => o.MapFrom(m => SlugConverter.TitleToSlug(m.Title)))
	            .ForMember(x => x.PublishedAt, o => o.MapFrom(m => m.PublishAt))
	            .ForMember(x => x.Key, o => o.MapFrom(m => m.ShowPostEvenIfPrivate))
	            .ForMember(x => x.SeoKeywords, o => o.MapFrom(m => m.SeoKeywords))
	            ;

			CreateMap<PostComments.Comment, AdminPostDetailsViewModel.Comment>()
	            .ForMember(x => x.Body, o => o.MapFrom(m => MarkdownResolver.Resolve(m.Body)))
	            .ForMember(x => x.EmailHash, o => o.MapFrom(m => EmailHashResolver.Resolve(m.Email)))
	            .ForMember(x => x.IsImportant, o => o.MapFrom(m => m.Important))
	            ;
	    }
	}
}