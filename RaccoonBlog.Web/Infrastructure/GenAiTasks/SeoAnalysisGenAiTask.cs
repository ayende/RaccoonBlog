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
            try
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

                        The post content is provided below. Analyze the title, body text, and existing tags.
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
                store.Maintenance.Send(new AddGenAiOperation(config));
                _log.Info("GenAI SEO analysis task created.");
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to create GenAI SEO analysis task.");
            }
        }
    }
}
