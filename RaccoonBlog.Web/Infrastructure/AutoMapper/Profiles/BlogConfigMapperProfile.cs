using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using AutoMapper;
using RaccoonBlog.Web.ViewModels;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles
{
    public class BlogConfigMapperProfile : Profile
    {
        protected override void Configure()
        {
            Mapper.CreateMap<BlogConfig, BlogConfigViewModelWelcome>();

            Mapper.CreateMap<BlogConfigViewModelWelcome, BlogConfig>();

            Mapper.CreateMap<BlogConfigViewModel, BlogConfig>();

            Mapper.CreateMap<BlogConfig, BlogConfigViewModel>();
        }
    }
}