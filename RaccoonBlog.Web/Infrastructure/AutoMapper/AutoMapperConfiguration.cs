using System;
using System.Web.Mvc;
using AutoMapper;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles;
using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper
{
	public class AutoMapperConfiguration
	{
		public static void Configure()
		{
			Mapper.CreateMap<string, MvcHtmlString>().ConvertUsing<MvcHtmlStringConverter>();
			Mapper.CreateMap<Guid, string>().ConvertUsing<GuidToStringConverter>();

			Mapper.CreateMap<DateTimeOffset, DateTime>().ConvertUsing<DateTimeTypeConverter>();


			// TODO: It would make sense to add all of those automatically with an IoC.
			Mapper.AddProfile(new PostViewModelMapperProfile());
			Mapper.AddProfile(new PostsViewModelMapperProfile());
			Mapper.AddProfile(new TagsListViewModelMapperProfile());
			Mapper.AddProfile(new SectionMapperProfile());
			Mapper.AddProfile(new EmailViewModelMapperProfile());

			Mapper.AddProfile(new UserAdminMapperProfile());
			Mapper.AddProfile(new PostsAdminViewModelMapperProfile());
            Mapper.AddProfile(new BlogConfigMapperProfile());
		}
	}
}