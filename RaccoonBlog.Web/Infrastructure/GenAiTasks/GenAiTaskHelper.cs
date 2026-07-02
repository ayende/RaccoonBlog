using System;
using NLog;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.AI;
using Raven.Client.Documents.Operations.AI.Agents;
using Raven.Client.Documents.Operations.OngoingTasks;

namespace RaccoonBlog.Web.Infrastructure.GenAiTasks
{
    public static class GenAiTaskHelper
    {
        /// <summary>
        /// Registers a GenAI task, or updates the existing one in place if it already exists so that
        /// changes to the script/prompt/config propagate on startup. AddGenAiOperation does not populate
        /// config.TaskId, so on the "already exists" path we look the task up by name to get its real id
        /// before calling UpdateGenAiOperation.
        /// </summary>
        public static void RegisterOrUpdate(
            IDocumentStore store,
            GenAiConfiguration config,
            Logger log,
            string friendlyName,
            StartingPointChangeVector startingPoint = null)
        {
            try
            {
                try
                {
                    if (startingPoint != null)
                        store.Maintenance.Send(new AddGenAiOperation(config, startingPoint));
                    else
                        store.Maintenance.Send(new AddGenAiOperation(config));

                    log.Info("GenAI {0} task created.", friendlyName);
                }
                catch (Exception addEx)
                {
                    // The task most likely already exists; look up its real id and update it in place.
                    var existing = store.Maintenance.Send(
                        new GetOngoingTaskInfoOperation(config.Name, OngoingTaskType.GenAi)) as GenAi;

                    if (existing == null)
                    {
                        log.Error(addEx, "GenAI {0} task add failed and no existing task was found to update.", friendlyName);
                        return;
                    }

                    store.Maintenance.Send(new UpdateGenAiOperation(existing.TaskId, config));
                    log.Info("GenAI {0} task updated (task id {1}).", friendlyName, existing.TaskId);
                }
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to create/update GenAI {0} task.", friendlyName);
            }
        }
    }
}
