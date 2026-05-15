using System.Linq;
using AutoMapper;
using Microsoft.AspNetCore.Html;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.ViewModels;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles
{
	public class PostsViewModelMapperProfile : Profile
	{
	    public PostsViewModelMapperProfile()
	    {
			// Use inline lambda instead of MvcHtmlStringConverter for AutoMapper 12.x compatibility
			CreateMap<string, IHtmlContent>()
				.ConvertUsing((src, dest, context) => new HtmlString(src ?? string.Empty));

			CreateMap<Post, PostsViewModel.PostSummary>()
				.ForMember(x => x.Id, o => o.MapFrom(m => Post.GetIdForUrl(m.Id)))
				.ForMember(x => x.Slug, o => o.MapFrom(m => SlugConverter.TitleToSlug(m.Title)))
				.ForMember(x => x.Author, o => o.Ignore())
				.ForMember(x => x.PublishedAt, o => o.MapFrom(m => m.PublishAt))
				.ForMember(x=>x.Title, o => o.MapFrom(m => System.Net.WebUtility.HtmlDecode(m.Title)))
				.ForMember(x => x.Body, o => o.MapFrom(m => new HtmlString(ImageUrlRewriter.RewriteImageUrls(m.Body))))
				.ForMember(x => x.IsSerie, o => o.MapFrom(m => m.Title.Contains(":")))
				.ForMember(x => x.Tags, o => o.MapFrom(m => m.Tags.Where(t => !t.StartsWith("@"))))
				;

			CreateMap<User, PostsViewModel.PostSummary.UserDetails>();

			CreateMap<string, TagDetails>()
				.ForMember(x => x.Name, o => o.MapFrom(m => m))
				;
		}
	}
}
