using System.ComponentModel;
using ApsSamples.Models;
using ApsSamples.Services;

namespace ApsSamples.Tools;

// Delegation tools: let an assistant hand tasks to the other assistants of the current project.
// A delegated task is recorded and queued, and DelegatedTaskProcessor runs it in the background as
// the delegate (its own service account and tools), then posts the result in this conversation.
public class DelegationTools(
    AgentToolContext context,
    IAgentRegistryService agentRegistry,
    IAgentTaskService taskService,
    IToolCatalogService toolCatalog,
    DelegatedTaskQueue queue,
    IUserSessionService session)
{
    [Description("Lists the other assistants of the current project that tasks can be delegated to. Returns each assistant's ID, name, role and the tool modules it has enabled. Use it to pick the assistant to pass to DelegateTask.")]
    public async Task<string> ListProjectAssistantsAsync()
    {
        try
        {
            var assistants = await GetDelegateCandidatesAsync();
            if (assistants.Count == 0)
            {
                return "There are no other assistants with a service account in this project to delegate to.";
            }

            var modulesByToolId = toolCatalog.GetAllTools()
                .ToDictionary(t => t.ToolId, t => t.Module, StringComparer.OrdinalIgnoreCase);

            return ToolResults.Json(assistants.Select(a => new
            {
                a.AgentId,
                a.AgentName,
                a.RoleName,
                ToolModules = a.EnabledToolIds
                    .Select(id => modulesByToolId.GetValueOrDefault(id))
                    .Where(m => !string.IsNullOrEmpty(m))
                    .Distinct()
                    .ToList()
            }));
        }
        catch (Exception ex)
        {
            return ToolResults.Error("list the project assistants", ex);
        }
    }

    [Description("Delegates a task to another assistant of the current project, which carries it out with its own tools and service account. Delegation is asynchronous: this returns a task ID at once, the task runs in the background, and the result is posted in this conversation when it completes or fails. Call ListProjectAssistants first to pick the assistant.")]
    public async Task<string> DelegateTaskAsync(
        [Description("The assistant to delegate to: its ID, name or role name, as returned by ListProjectAssistants.")] string assistant,
        [Description("Short title of the task, for example \"Create Building B folders\".")] string title,
        [Description("Self-contained instructions for the assistant: what to do and with which names, IDs or paths. The assistant doesn't see this conversation.")] string instructions,
        [Description("IDs of tasks returned by earlier DelegateTask calls in this conversation that must complete before this one starts. Leave empty to start at once.")] List<string>? dependsOnTaskIds = null)
    {
        if (!string.IsNullOrEmpty(context.DelegatedTaskId))
        {
            return "Delegated assistants can't delegate further.";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "Give the task a title.";
        }

        if (string.IsNullOrWhiteSpace(instructions))
        {
            return "Give the task instructions for the assistant.";
        }

        try
        {
            var conversationId = context.RequireConversationId();
            var projectId = context.RequireProjectId();
            var hubId = context.RequireHubId();

            var candidates = await GetDelegateCandidatesAsync();
            if (candidates.Count == 0)
            {
                return "There are no other assistants with a service account in this project to delegate to.";
            }

            var matches = ResolveAssistant(candidates, assistant);
            if (matches.Count != 1)
            {
                var names = string.Join(", ", candidates.Select(a => $"{a.AgentName} ({a.RoleName})"));
                return matches.Count == 0
                    ? $"No assistant matches '{assistant}'. Delegate to one of: {names}."
                    : $"'{assistant}' matches several assistants. Use the assistant's ID or name, one of: {names}.";
            }

            var target = matches[0];

            var dependencies = (dependsOnTaskIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var dependencyId in dependencies)
            {
                var dependency = await taskService.GetTaskAsync(dependencyId);
                if (dependency is null || dependency.ConversationId != conversationId)
                {
                    return $"There is no task with ID '{dependencyId}' in this conversation. Use the task IDs returned by DelegateTask.";
                }
            }

            var self = string.IsNullOrEmpty(context.AgentId) ? null : await agentRegistry.GetAgentAsync(context.AgentId);

            var task = await taskService.CreateTaskAsync(conversationId, projectId, title.Trim());
            task.AgentId = target.AgentId;
            task.AgentName = target.AgentName;
            task.DelegatedByAgentId = context.AgentId;
            task.DelegatedByAgentName = self?.AgentName;
            task.RequestedByUserId = session.UserId ?? string.Empty;
            task.HubId = hubId;
            task.Instructions = instructions.Trim();
            task.DependsOnTaskIds = dependencies;
            task.Status = AgentTaskStatus.Pending;
            task.ProgressDetail = $"Queued for {target.AgentName}.";
            await taskService.UpdateTaskAsync(task);

            queue.Enqueue(new DelegatedTaskWorkItem(task.TaskId));

            return $"Delegated '{task.Name}' to {target.AgentName} (task id: {task.TaskId}). " +
                "It runs in the background and the result will be posted in this conversation.";
        }
        catch (Exception ex)
        {
            return ToolResults.Error("delegate the task", ex);
        }
    }

    [Description("Lists the tasks delegated in this conversation, with each task's ID, title, assigned assistant, status, the task IDs it depends on, and its result or error.")]
    public async Task<string> GetDelegatedTasksAsync()
    {
        try
        {
            var conversationId = context.RequireConversationId();
            var tasks = (await taskService.GetTasksByConversationAsync(conversationId))
                .Where(t => !string.IsNullOrEmpty(t.DelegatedByAgentId))
                .ToList();

            if (tasks.Count == 0)
            {
                return "No tasks have been delegated in this conversation.";
            }

            return ToolResults.Json(tasks.Select(t => new
            {
                t.TaskId,
                Title = t.Name,
                AssignedTo = t.AgentName,
                Status = t.Status.ToString(),
                t.DependsOnTaskIds,
                t.Result,
                Error = t.ErrorDetail
            }));
        }
        catch (Exception ex)
        {
            return ToolResults.Error("list the delegated tasks", ex);
        }
    }

    // The project's assistants other than the caller that have a service account to run as.
    private async Task<List<AgentDefinition>> GetDelegateCandidatesAsync()
    {
        var agents = await agentRegistry.GetAgentsByProjectAsync(context.RequireProjectId());
        return agents
            .Where(a => a.AgentId != context.AgentId && !string.IsNullOrEmpty(a.SsaId))
            .ToList();
    }

    // Matches by ID first, then by name, then by role name (ignoring case). Small models often
    // pass a name or role instead of the ID.
    private static List<AgentDefinition> ResolveAssistant(List<AgentDefinition> candidates, string? assistant)
    {
        var value = assistant?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var byId = candidates.Where(a => string.Equals(a.AgentId, value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count > 0)
        {
            return byId;
        }

        var byName = candidates.Where(a => string.Equals(a.AgentName, value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count > 0)
        {
            return byName;
        }

        return candidates.Where(a => string.Equals(a.RoleName, value, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
