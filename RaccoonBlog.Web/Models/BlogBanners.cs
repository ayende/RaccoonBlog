using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace RaccoonBlog.Web.Models;

public class BlogBanners
{
    public const string DocumentId = "config/banners";
    public string Id { get; set; }
    public List<BannerItem> Banners { get; set; } = new();
}

public class BannerItem
{
    public string Id { get; set; }
    public string AttachmentName { get; set; }

    [Display(Name = "Resource URL")]
    public string ResourceUrl { get; set; }

    [Display(Name = "Image Description")]
    public string AltText { get; set; }

    [Display(Name = "Enabled")]
    public bool Enabled { get; set; }

    [Display(Name = "Start Date")]
    public DateTimeOffset? StartDate { get; set; }

    [Display(Name = "End Date")]
    public DateTimeOffset? EndDate { get; set; }

    public bool IsNewBanner() => string.IsNullOrEmpty(Id);
}
