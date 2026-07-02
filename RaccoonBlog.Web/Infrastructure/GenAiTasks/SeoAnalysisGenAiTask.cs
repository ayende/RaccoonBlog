using System;
using NLog;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.AI;

namespace RaccoonBlog.Web.Infrastructure.GenAiTasks
{
    public static class SeoAnalysisGenAiTask
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static void Register(IDocumentStore store)
        {
            var config = new GenAiConfiguration
            {
                Name = "SEO Analysis",
                    Identifier = "seo-analysis",
                    ConnectionStringName = "ai-chat",
                    Disabled = false,
                    Collection = "Posts",
                    GenAiTransformation = new GenAiTransformation
                    {
                        Script = """
                            ai.genContext({
                                Title: this.Title,
                                Body: this.Body,
                                Tags: this.Tags
                            });
                            """
                    },
                    Prompt = """
                        You are an expert SEO analyst. Analyze the blog post provided and generate:

                        1. A compelling meta description (max 160 characters) that accurately summarizes the post and includes relevant keywords to improve search engine ranking. Write for humans, not search engines.

                        2. A list of 3-8 SEO keywords/keyphrases relevant to the post content. These should be terms people would search for to find this content. Include both short-tail and long-tail keywords where appropriate.

                        You are provided with the title, body text, and existing tags for the post. 
                        """,
                    SampleObject = """
                        {
                            "MetaDescription": "A concise, compelling meta description under 160 characters.",
                            "Keywords": [ "primary keyword", "secondary keyword phrase", "related term" ]
                        }
                        """,
                    UpdateScript = """
                        this.Seo = {
                            MetaDescription: $output.MetaDescription,
                            Keywords: $output.Keywords,
                            LastAnalyzedAt: new Date().toISOString()
                        };
                        """
            };

            GenAiTaskHelper.RegisterOrUpdate(store, config, _log, "SEO analysis");
        }
    }
}
