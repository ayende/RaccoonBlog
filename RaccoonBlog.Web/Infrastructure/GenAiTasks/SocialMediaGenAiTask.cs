using System;
using NLog;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.AI;
using Raven.Client.Documents.Operations.AI.Agents;

namespace RaccoonBlog.Web.Infrastructure.GenAiTasks
{
    public static class SocialMediaGenAiTask
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static void Register(IDocumentStore store)
        {
            var config = new GenAiConfiguration
            {
                Name = "social-media",
                    Identifier = "social-media",
                    ConnectionStringName = "ai-chat",
                    Disabled = false,
                    Collection = "Posts",
                    GenAiTransformation = new GenAiTransformation
                    {
                        Script = """
                            ai.genContext({
                                Title: this.Title,
                                Body: this.Body.substring(0, 16 * 1024),
                                Tags: this.Tags
                            });
                            """
                    },
                    Prompt = """
                        You are a social media manager for a technical blog about software development,
                        databases, and distributed systems. Generate engaging social media text for the
                        blog post provided.

                        1. Twitter: A concise, engaging tweet (max 250 characters, excluding URL which
                           will be appended automatically). Should hook technical readers. Include 1-2
                           relevant hashtags if natural. Do not include a URL.

                        2. Reddit: A compelling submission title for a programming subreddit audience.
                           Should be informative and spark discussion. No clickbait. Max 200 characters.
                        """,
                    SampleObject = """
                        {
                            "TwitterText": "Concise engaging tweet text with #relevantHashtag",
                            "RedditTitle": "Compelling Reddit submission title for technical audience"
                        }
                        """,
                    UpdateScript = """
                        this.Social = this.Social || {};
                        this.Social.TwitterText = $output.TwitterText;
                        this.Social.RedditTitle = $output.RedditTitle;
                        this.Social.GeneratedAt = new Date().toISOString();

                        getMetadata(this)['@refresh'] = this.PublishAt;
                        """
            };

            GenAiTaskHelper.RegisterOrUpdate(store, config, _log, "social media", StartingPointChangeVector.LastDocument);
        }
    }
}
