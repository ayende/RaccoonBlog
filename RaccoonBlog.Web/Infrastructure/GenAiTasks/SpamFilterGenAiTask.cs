using System;
using NLog;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.AI;
using Raven.Client.Documents.Operations.AI.Agents;

namespace RaccoonBlog.Web.Infrastructure.GenAiTasks
{
    public static class SpamFilterGenAiTask
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static void Register(IDocumentStore store)
        {
            var config = new GenAiConfiguration
            {
                Name = "spam-filter",
                    Identifier = "spam-filter",
                    ConnectionStringName = "ai-chat",
                    Disabled = false,
                    Collection = "PostComments",
                    GenAiTransformation = new GenAiTransformation
                    {
                        Script = """
                            const post = load(this.Post.Id);
                            for(const comment of this.Comments) {
                                if(comment.SpamCheckStatus !== 'Pending')
                                    continue;

                                ai.genContext({
                                    Post: {Title: post.Title, Tags: post.Tags},
                                    Id: comment.Id,
                                    Author: comment.Author,
                                    Body: comment.Body,
                                    Email: comment.Email,
                                    Url: comment.Url,
                                    UserHostAddress: comment.UserHostAddress,
                                    UserAgent: comment.UserAgent,
                                    CommenterId: comment.CommenterId,
                                    PostCommentsId: id(this)
                                });
                            }
                            """
                    },
                    Queries = [
                        new AiAgentToolQuery
                        {
                            Name = "ReadPostComments",
                            Description = """
                                          Use this to read the _other_ comments on the same post to get more context. Returns all valid comments
                                          on this post, so you can check if this is replying to another comment, instead of trying to evaluate it in isolation.
                                          """,
                            ParametersSampleObject = "{}",
                            Query = """
                                    from PostComments as pc
                                    where id(pc) = $PostCommentsId
                                    select 
                                    {
                                        Comments: pc.Comments.filter(c=>c.SpamCheckStatus == 'Valid')
                                    }
                                    """
                        }
                    ],
                    Prompt = """
                        You are a spam filter for a technical blog. Analyze this blog comment.
                        A spam comment typically includes irrelevant or promotional content,
                        excessive links, misleading information, or advertising intent.

                        A legitimate comment engages with the post, asks relevant questions,
                        or provides relevant feedback. You can see the title and tags of the post this
                        comment is in reply to.

                        Comments that praise the post or author without adding any value, and has additional links, are spam.

                        Based on the comment content and metadata, determine if this comment is likely spam.
                        The expected language is English, if it is anything else, that is also a strong signal of spam.

                        If the comment may be spam, but can also be part of a conversation, you have the ReadPostComments
                        tool that you can invoke to get the other comments on the same post to get more context before making a decision.
                        """,
                    SampleObject = 
                        """
                        { "IsSpam": true, "Reason": "brief explanation why you think it is spam or not" }
                        """,
                    UpdateScript = """
                        const idx = this.Comments.findIndex(c => c.Id == $input.Id);
                        if (idx < 0) return;

                        if ($output.IsSpam) {
                            var c = this.Comments[idx];
                            c.SpamCheckStatus = 'Spam';
                            this.Comments.splice(idx, 1);
                            this.Spam.push(c);

                            var post = load(this.Post.Id);
                            if (post) {
                                post.CommentsCount--;
                                if (post.CommentsCount < 0) post.CommentsCount = 0;
                            }

                            var today = new Date().toISOString().split('T')[0];
                            var digestId = 'SpamDigests/' + today;
                            var tomorrow = new Date();
                            tomorrow.setDate(tomorrow.getDate() + 1);
                            tomorrow.setHours(8, 0, 0, 0);

                            var spamEntry = {
                                CommentId: $input.Id,
                                Author: $input.Author,
                                Body: $input.Body,
                                PostId: this.Post.Id,
                                PostTitle: post ? post.Title : '',
                                Timestamp: new Date().toISOString()
                            };

                            var dig = load(digestId) || {
                                Type: 'SpamDigest',
                                Subject: 'Spam digest for ' + today,
                                DigestDate: today,
                                SpamComments: [],
                                Count: 0,
                                '@metadata': {
                                    '@refresh': tomorrow.toISOString(),
                                    '@collection': 'EmailCommands'
                                }
                            };
                            dig.Count++;
                            dig.SpamComments.push(spamEntry);
                          
                            put(digestId, dig);
                        } else {
                            this.Comments[idx].SpamCheckStatus = 'Valid';

                            if ($input.CommenterId) {
                                var commenter = load($input.CommenterId);
                                if (commenter !== null && !commenter.IsTrustedCommenter) {
                                    commenter.IsTrustedCommenter = true;
                                    put($input.CommenterId, commenter);
                                }
                            }

                            var post = load(this.Post.Id);
                            var postTitle = post ? post.Title : '';
                            var postSlug = postTitle.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

                            var emailCmd = {
                                Type: 'NewComment',
                                View: 'NewComment',
                                ReplyTo: $input.Email || '',
                                Subject: 'Comment on: ' + postTitle + ' from ' + $input.Author,
                                CommentId: $input.Id,
                                Author: $input.Author || '',
                                CommentBody: $input.Body || '',
                                CommentEmail: $input.Email || '',
                                CommentUrl: $input.Url || '',
                                CreatedAt: new Date().toISOString(),
                                IpAddress: $input.UserHostAddress || '',
                                UserAgent: $input.UserAgent || '',
                                CommenterId: $input.CommenterId || '',
                                PostId: this.Post.Id || '',
                                PostTitle: postTitle,
                                PostSlug: postSlug,
                                Key: post ? post.ShowPostEvenIfPrivate : '',
                                '@metadata': { '@collection': 'EmailCommands' }
                            };
                            put('EmailCommands/new-comment-' + $input.Id, emailCmd);
                        }
                        """
            };

            GenAiTaskHelper.RegisterOrUpdate(store, config, _log, "spam filter");
        }
    }
}
