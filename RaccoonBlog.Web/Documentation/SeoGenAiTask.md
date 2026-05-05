# SEO Analysis GenAI Task for RavenDB

## Overview

This GenAI task monitors the `Posts` collection and automatically generates SEO metadata for each blog post using a generative AI model. The AI analyzes the post's title and body, then produces:

- **Meta Description** (`SeoMetaDescription`) - A compelling, SEO-optimized meta description
- **Keywords** (`SeoKeywords`) - Relevant SEO keywords/keyphrases extracted from the content

The post's body content is never modified. All generated data is stored in dedicated SEO fields on the Post document.

## Configuration

### Via Client API (C#)

```csharp
var seoConfig = new GenAiConfiguration
{
    Name = "SEO Analysis",
    Identifier = "seo-analysis",
    ConnectionStringName = "OpenAI Generative",
    Collection = "Posts",
    GenAiTransformation = new GenAiTransformation
    {
        Script =
            """
            ai.genContext({
                Title: this.Title,
                Body: this.Body,
                Tags: this.Tags
            });
            """
    },
    Prompt =
        """
        You are an expert SEO analyst. Analyze the blog post provided and generate:

        1. A compelling meta description (max 160 characters) that accurately summarizes the post and includes relevant keywords to improve search engine ranking. Write for humans, not search engines.

        2. A list of 3-8 SEO keywords/keyphrases relevant to the post content. These should be terms people would search for to find this content. Include both short-tail and long-tail keywords where appropriate.

        The post content is provided below. Analyze the title, body text, and existing tags.
        """,
    SampleObject =
        """
        {
            "MetaDescription": "A concise, compelling meta description under 160 characters that summarizes the blog post for search engines.",
            "Keywords": [
                "primary keyword",
                "secondary keyword phrase",
                "related term"
            ]
        }
        """,
    UpdateScript =
        """
        this.SeoMetaDescription = $output.MetaDescription;
        this.SeoKeywords = $output.Keywords;
        this.SeoLastAnalyzedAt = new Date().toISOString();
        """
};

await documentStore.Maintenance.SendAsync(
    new AddGenAiOperation(seoConfig));
```

### Via RavenDB Studio

1. Navigate to your database in RavenDB Studio
2. Go to **Settings > AI Hub > AI Tasks**
3. Click **Add AI Task** and select **Generative AI**
4. Configure as follows:

| Field | Value |
|-------|-------|
| Name | SEO Analysis |
| Identifier | `seo-analysis` |
| Connection String | Your AI connection string (e.g., `OpenAI Generative`) |
| Collection | `Posts` |

**Context Script:**
```javascript
ai.genContext({
    Title: this.Title,
    Body: this.Body,
    Tags: this.Tags
});
```

**Prompt:**
```
You are an expert SEO analyst. Analyze the blog post provided and generate:

1. A compelling meta description (max 160 characters) that accurately summarizes the post and includes relevant keywords to improve search engine ranking. Write for humans, not search engines.

2. A list of 3-8 SEO keywords/keyphrases relevant to the post content. These should be terms people would search for to find this content. Include both short-tail and long-tail keywords where appropriate.

The post content is provided below. Analyze the title, body text, and existing tags.
```

**Sample Object:**
```json
{
    "MetaDescription": "A concise, compelling meta description under 160 characters that summarizes the blog post for search engines.",
    "Keywords": [
        "primary keyword",
        "secondary keyword phrase",
        "related term"
    ]
}
```

**Update Script:**
```javascript
this.SeoMetaDescription = $output.MetaDescription;
this.SeoKeywords = $output.Keywords;
this.SeoLastAnalyzedAt = new Date().toISOString();
```

## What Happens

1. When a post is created or modified, the GenAI task triggers
2. The context extraction script reads `Title`, `Body`, and `Tags` from the post
3. The AI model analyzes the content and generates SEO metadata
4. The update script writes the SEO data back to the Post document:
   - `SeoMetaDescription` - AI-generated meta description
   - `SeoKeywords` - AI-suggested keywords as a string array
   - `SeoLastAnalyzedAt` - Timestamp of the analysis

## Usage in the Blog

The application code uses these fields:

| Field | Where Used |
|-------|-----------|
| `SeoMetaDescription` | Replaces the auto-truncated meta description in `<meta name="description">`, Open Graph, and Twitter Card tags |
| `SeoKeywords` | Rendered as `<meta name="keywords">` and included in JSON-LD structured data |
| `SeoLastAnalyzedAt` | Displayed in admin views to show when SEO was last updated |

## Constraint: Content is Never Modified

The GenAI task does NOT modify the post's `Body`, `Title`, or other user-authored content. Only the `Seo*` fields are updated.
