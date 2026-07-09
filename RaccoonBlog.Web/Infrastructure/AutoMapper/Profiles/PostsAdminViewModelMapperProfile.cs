using System;
using System.Linq;
using System.Net;
using RaccoonBlog.Web.Helpers;
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
	            .ForMember(x => x.Title, o => o.MapFrom(m => WebUtility.HtmlDecode(m.Title)))
	            .ForMember(x => x.Start, o => o.MapFrom(m => m.PublishAt.ToString("yyyy-MM-ddTHH:mm:ssZ")))
	            .ForMember(x => x.Url, o => o.MapFrom(m => $"/admin/posts/details/{m.GetIdForUrl()}/{SlugConverter.TitleToSlug(m.Title)}"))
	            .ForMember(x => x.AllDay, o => o.MapFrom(_ => false))
	            ;

	        CreateMap<Post, PostInput>()
	            .ForMember(x => x.Id, o => o.MapFrom(m => m.GetIdForUrl()))
	            .ForMember(x => x.Tags, o => o.MapFrom(m => TagsResolver.ResolveTags(m.Tags)))
	            .ForMember(x => x.SeoMetaDescription, o => o.MapFrom(m => m.Seo != null ? m.Seo.MetaDescription : null))
	            .ForMember(x => x.SeoKeywords, o => o.MapFrom(m => m.Seo != null ? TagsResolver.ResolveTags(m.Seo.Keywords) : null))
	            .ForMember(x => x.SeoLastAnalyzedAt, o => o.MapFrom(m => m.Seo != null ? m.Seo.LastAnalyzedAt : null))
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
            .ForMember(x => x.Social, o => o.Ignore())
	            .ForMember(x => x.TagsAsSlugs, o => o.Ignore())
	            .ForMember(x => x.Tags, o => o.MapFrom(m => TagsResolver.ResolveTagsInput(m.Tags)))
	            .ForMember(x => x.Seo, o => o.MapFrom(m => new SeoMetadata
	            {
	                MetaDescription = m.SeoMetaDescription,
	                Keywords = TagsResolver.ResolveTagsInput(m.SeoKeywords),
	                LastAnalyzedAt = m.SeoLastAnalyzedAt
	            }))
	            .ForMember(x => x.PublishAt, o => o.MapFrom(m => m.PublishAt.HasValue ? m.PublishAt.Value : DateTimeOffset.MinValue))
	            ;

	        CreateMap<Post, AdminPostDetailsViewModel.PostDetails>()
	            .ForMember(x => x.Id, o => o.MapFrom(m => m.GetIdForUrl()))
	            .ForMember(x => x.Slug, o => o.MapFrom(m => SlugConverter.TitleToSlug(m.Title)))
	            .ForMember(x => x.PublishedAt, o => o.MapFrom(m => m.PublishAt))
	            .ForMember(x => x.Key, o => o.MapFrom(m => m.ShowPostEvenIfPrivate))
	            .ForMember(x => x.SeoMetaDescription, o => o.MapFrom(m => m.Seo != null ? m.Seo.MetaDescription : null))
	            .ForMember(x => x.SeoKeywords, o => o.MapFrom(m => m.Seo != null ? m.Seo.Keywords : null))
	            .ForMember(x => x.SeoLastAnalyzedAt, o => o.MapFrom(m => m.Seo != null ? m.Seo.LastAnalyzedAt : null))
	            ;

	        CreateMap<PostComments.Comment, AdminPostDetailsViewModel.Comment>()
	            .ForMember(x => x.Body, o => o.MapFrom(m => MarkdownResolver.Resolve(m.Body)))
	            .ForMember(x => x.EmailHash, o => o.MapFrom(m => EmailHashResolver.Resolve(m.Email)))
	            .ForMember(x => x.IsImportant, o => o.MapFrom(m => m.Important))
	            ;
	    }
	}
}