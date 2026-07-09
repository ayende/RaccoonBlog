using AutoMapper;
using Microsoft.AspNetCore.Html;
using System;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper
{
	public class AutoMapperConfiguration : Profile
	{
	    public AutoMapperConfiguration()
	    {
	        // Global type converters - use ConvertUsing with lambda for AutoMapper 12.x compatibility
	        // Note: String -> IHtmlContent mapping is in PostsViewModelMapperProfile to avoid duplication
	        
	        CreateMap<Guid, string>().ConvertUsing((src, dest, context) => 
	            src.ToString());
	        
	        CreateMap<DateTimeOffset, DateTime>().ConvertUsing((src, dest, context) =>
	            src.DateTime);
        }
	}
}