using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ApsSamples.Services;

/// <summary>
/// Runs queued delegated tasks: waits for the task's dependencies, then runs the delegate's agent in
/// its own DI scope, so its tools act as the delegate (its own service account and enabled tools),
/// and posts the outcome in the originating conversation. Each work item runs on its own task; at
/// most <see cref="MaxConcurrentRuns"/> delegates run at once because they share one local model.
/// </summary>
public class DelegatedTaskProcessor(
    DelegatedTaskQueue queue,
    IAgentTaskService taskService,
    IAgentRegistryService agentRegistry,
    IAgentConversationService conversationService,
    IToolCatalogService toolCatalog,
    IChatClient chatClient,
    IServiceScopeFactory scopeFactory,
    ILogger<DelegatedTaskProcessor> logger) : BackgroundService
{
    private const int MaxConcurrentRuns = 2;

    private readonly SemaphoreSlim _runGate = new(MaxConcurrentRuns, MaxConcurrentRuns);

    // How often a waiting task checks its dependencies, and how long it waits for them in total.
    internal TimeSpan DependencyPollInterval { get; set; } = TimeSpan.FromSeconds(5);
    internal TimeSpan DependencyTimeout { get; set; } = TimeSpan.FromMinutes(60);

    // Upper bound for one delegate run, on top of the chat client's tool iteration limit.
    internal TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedTasksAsync();

        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                _ = Task.Run(() => ProcessAsync(item.TaskId, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task ProcessAsync(string taskId, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(taskId);
        if (task == null || task.Status != AgentTaskStatus.Pending)
        {
            return;
        }

        try
        {
            var dependencyError = await WaitForDependenciesAsync(task, cancellationToken);
            if (dependencyError != null)
            {
                logger.LogWarning("Delegated task {TaskId} for agent {AgentId} not run: a dependency did not complete", task.TaskId, task.AgentId);
                await FailAsync(task, dependencyError);
                return;
            }

            // Acquired after the dependency wait, so waiting tasks don't hold a run slot.
            await _runGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Still Pending: startup recovery re-queues it.
            return;
        }

        try
        {
            await RunAsync(task, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: left Running, and startup recovery marks it as interrupted.
            logger.LogInformation("Delegated task {TaskId} for agent {AgentId} stopped by app shutdown", task.TaskId, task.AgentId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Delegated task {TaskId} for agent {AgentId} timed out", task.TaskId, task.AgentId);
            await FailAsync(task, $"The task didn't finish within {RunTimeout.TotalMinutes:0} minutes.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delegated task {TaskId} for agent {AgentId} failed", task.TaskId, task.AgentId);
            await FailAsync(task, ex.Message);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task RunAsync(AgentTaskInfo task, CancellationToken cancellationToken)
    {
        task.Status = AgentTaskStatus.Running;
        task.ProgressDetail = "Running.";
        await taskService.UpdateTaskAsync(task);

        var def = await agentRegistry.GetAgentAsync(task.AgentId);
        if (def == null)
        {
            logger.LogWarning("Delegated task {TaskId}: agent {AgentId} not found", task.TaskId, task.AgentId);
            await FailAsync(task, $"The assistant {task.AgentName ?? task.AgentId} no longer exists.");
            return;
        }

        logger.LogInformation("Running delegated task {TaskId} as agent {AgentId}", task.TaskId, def.AgentId);

        using var scope = scopeFactory.CreateScope();

        // The delegate's own identity, in a context no chat turn shares.
        var context = scope.ServiceProvider.GetRequiredService<AgentToolContext>();
        context.HubId = task.HubId;
        context.ProjectId = task.ProjectId;
        context.ConversationId = task.ConversationId;
        context.AgentId = def.AgentId;
        context.SsaId = def.SsaId;
        context.DelegatedTaskId = task.TaskId;

        // Delegates never get the delegation tools, so they can't delegate further.
        var toolIds = def.EnabledToolIds.Where(id => !IsDelegationTool(id));
        var agent = chatClient.AsAIAgent(
            instructions: def.InstructionsText,
            name: def.AgentName,
            tools: toolCatalog.GetAIFunctions(toolIds, scope.ServiceProvider).Cast<AITool>().ToArray());

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunTimeout);

        var session = await agent.CreateSessionAsync(timeout.Token);
        var response = await agent.RunAsync([BuildPrompt(task)], session, cancellationToken: timeout.Token);

        var result = response.Text?.Trim();
        task.Result = string.IsNullOrEmpty(result) ? "Done." : result;
        task.Status = AgentTaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.ProgressDetail = "Completed.";
        await PostOutcomeAsync(task, $"✅ **{task.Name}** — {task.Result}", def.AgentName);

        logger.LogInformation("Delegated task {TaskId} for agent {AgentId} finished with status {Status}", task.TaskId, def.AgentId, task.Status);
    }

    // Returns null once every dependency completed, or the reason this task can't run.
    private async Task<string?> WaitForDependenciesAsync(AgentTaskInfo task, CancellationToken cancellationToken)
    {
        if (task.DependsOnTaskIds.Count == 0)
        {
            return null;
        }

        var deadline = DateTime.UtcNow + DependencyTimeout;
        while (true)
        {
            var waiting = false;
            foreach (var dependencyId in task.DependsOnTaskIds)
            {
                var dependency = await taskService.GetTaskAsync(dependencyId);
                switch (dependency?.Status)
                {
                    case AgentTaskStatus.Completed:
                        continue;
                    case AgentTaskStatus.Pending:
                    case AgentTaskStatus.Running:
                        waiting = true;
                        break;
                    default:
                        return $"Dependency '{dependency?.Name ?? dependencyId}' did not complete.";
                }
            }

            if (!waiting)
            {
                return null;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return $"Dependencies did not complete within {DependencyTimeout.TotalMinutes:0} minutes.";
            }

            await Task.Delay(DependencyPollInterval, cancellationToken);
        }
    }

    private async Task FailAsync(AgentTaskInfo task, string error)
    {
        task.Status = AgentTaskStatus.Failed;
        task.CompletedAt = DateTime.UtcNow;
        task.ErrorDetail = error;
        task.ProgressDetail = "Failed.";
        await PostOutcomeAsync(task, $"⚠️ **{task.Name}** — {error}", task.AgentName);
    }

    // Posts the outcome as the delegate, so the delegating assistant sees it on its next turn.
    private async Task PostOutcomeAsync(AgentTaskInfo task, string content, string? agentName)
    {
        var message = new ConversationMessage
        {
            Role = "assistant",
            Content = content,
            Timestamp = DateTime.UtcNow,
            AgentId = string.IsNullOrEmpty(task.AgentId) ? null : task.AgentId,
            AgentName = agentName
        };

        task.RelatedMessageId = message.Id;
        await taskService.UpdateTaskAsync(task);

        await conversationService.AddMessageAsync(task.ConversationId, message);
    }

    private static ChatMessage BuildPrompt(AgentTaskInfo task)
    {
        var delegatedBy = string.IsNullOrWhiteSpace(task.DelegatedByAgentName) ? "Another assistant" : task.DelegatedByAgentName;
        return new ChatMessage(ChatRole.User,
            $"""
            {delegatedBy} delegated a task to you.

            Task: {task.Name}

            Instructions:
            {task.Instructions}

            Carry this out with your tools. When done, reply with a short summary of what you did and the outcome, or explain what blocked you.
            """);
    }

    private bool IsDelegationTool(string toolId) =>
        toolCatalog.GetAllTools().Any(t =>
            string.Equals(t.ToolId, toolId, StringComparison.OrdinalIgnoreCase) && t.Module == ToolModules.Delegation);

    // The queue is in memory, so work queued or running when the app stopped is lost. Pending tasks
    // are queued again; running ones are failed rather than repeated, since their work may be partly done.
    private async Task RecoverInterruptedTasksAsync()
    {
        foreach (var task in await taskService.GetTasksByStatusAsync(AgentTaskStatus.Running))
        {
            if (!string.IsNullOrEmpty(task.DelegatedByAgentId))
            {
                logger.LogWarning("Delegated task {TaskId} for agent {AgentId} was interrupted by an app restart", task.TaskId, task.AgentId);
                await FailAsync(task, "Interrupted by an app restart.");
            }
        }

        foreach (var task in await taskService.GetTasksByStatusAsync(AgentTaskStatus.Pending))
        {
            if (!string.IsNullOrEmpty(task.DelegatedByAgentId))
            {
                queue.Enqueue(new DelegatedTaskWorkItem(task.TaskId));
            }
        }
    }
}
