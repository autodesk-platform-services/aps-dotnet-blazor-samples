using System.Runtime.CompilerServices;
using ApsSamples.Models;
using ApsSamples.Services;
using ApsSamples.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class DelegatedTaskProcessorTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private const string ProjectId = "b.project";

    public DelegatedTaskProcessorTests(TempWorkingDirectoryFixture _) { }

    private static readonly AgentDefinition Architect = new()
    {
        AgentId = "agent-architect",
        AgentName = "Architect Assistant",
        RoleName = "Architect",
        ProjectId = ProjectId,
        SsaId = "ssa-architect",
        InstructionsText = "You are the architect.",
        EnabledToolIds = ["create-folder-structure", "delegate-task", "list-folders", "get-delegated-tasks"]
    };

    private sealed record Harness(
        DelegatedTaskProcessor Processor,
        AgentTaskService TaskService,
        AgentConversationService ConversationService,
        DelegatedTaskQueue Queue,
        FakeChatClient ChatClient,
        RecordingToolCatalogService ToolCatalog,
        string ConversationId);

    private static async Task<Harness> NewHarnessAsync()
    {
        var conversationService = new AgentConversationService(NullLogger<AgentConversationService>.Instance);
        var conversation = await conversationService.CreateConversationAsync(ProjectId, "user-1", "agent-bim");
        var taskService = new AgentTaskService(conversationService, null!, NullLogger<AgentTaskService>.Instance);
        var queue = new DelegatedTaskQueue();
        var chatClient = new FakeChatClient();
        var toolCatalog = new RecordingToolCatalogService();
        var scopeFactory = new ServiceCollection()
            .AddScoped(_ => new AgentToolContext(null!, null!))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var processor = new DelegatedTaskProcessor(
            queue,
            taskService,
            new FakeAgentRegistryService(Architect),
            conversationService,
            toolCatalog,
            chatClient,
            scopeFactory,
            NullLogger<DelegatedTaskProcessor>.Instance)
        {
            DependencyPollInterval = TimeSpan.Zero
        };

        return new Harness(processor, taskService, conversationService, queue, chatClient, toolCatalog, conversation.ConversationId);
    }

    private static async Task<AgentTaskInfo> AddDelegatedTaskAsync(
        Harness h, string name, string agentId = "agent-architect", AgentTaskStatus status = AgentTaskStatus.Pending, params string[] dependsOn)
    {
        var task = await h.TaskService.CreateTaskAsync(h.ConversationId, ProjectId, name);
        task.AgentId = agentId;
        task.AgentName = agentId == Architect.AgentId ? Architect.AgentName : "Gone Assistant";
        task.DelegatedByAgentId = "agent-bim";
        task.DelegatedByAgentName = "BIM Manager Assistant";
        task.HubId = "b.hub";
        task.Instructions = "Create Building B/WIP/Architecture.";
        task.DependsOnTaskIds = dependsOn.ToList();
        task.Status = status;
        await h.TaskService.UpdateTaskAsync(task);
        return task;
    }

    private static async Task<List<ConversationMessage>> MessagesAsync(Harness h) =>
        (await h.ConversationService.GetConversationAsync(h.ConversationId))!.Messages;

    private static List<string> Drain(DelegatedTaskQueue queue)
    {
        var ids = new List<string>();
        while (queue.Reader.TryRead(out var item))
        {
            ids.Add(item.TaskId);
        }
        return ids;
    }

    [Fact]
    public async Task ProcessAsync_Success_CompletesTaskAndPostsResultAsDelegate()
    {
        var h = await NewHarnessAsync();
        h.ChatClient.Reply = "  Created the Building B folders.  ";
        var task = await AddDelegatedTaskAsync(h, "Create Building B folders");

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        var stored = (await h.TaskService.GetTaskAsync(task.TaskId))!;
        Assert.Equal(AgentTaskStatus.Completed, stored.Status);
        Assert.Equal("Created the Building B folders.", stored.Result);
        Assert.Equal("Completed.", stored.ProgressDetail);
        Assert.NotNull(stored.CompletedAt);

        var message = Assert.Single(await MessagesAsync(h));
        Assert.Equal("assistant", message.Role);
        Assert.Equal("✅ **Create Building B folders** — Created the Building B folders.", message.Content);
        Assert.Equal("agent-architect", message.AgentId);
        Assert.Equal("Architect Assistant", message.AgentName);
        Assert.Equal(message.Id, stored.RelatedMessageId);

        var prompt = Assert.Single(h.ChatClient.Requests).Last(m => m.Role == ChatRole.User).Text;
        Assert.Contains("BIM Manager Assistant delegated a task to you.", prompt);
        Assert.Contains("Task: Create Building B folders", prompt);
        Assert.Contains("Create Building B/WIP/Architecture.", prompt);
    }

    [Fact]
    public async Task ProcessAsync_RunsInScopeWithDelegateIdentity()
    {
        var h = await NewHarnessAsync();
        var task = await AddDelegatedTaskAsync(h, "Identity");

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        var context = Assert.Single(h.ToolCatalog.Contexts);
        Assert.Equal("ssa-architect", context.SsaId);
        Assert.Equal("agent-architect", context.AgentId);
        Assert.Equal(task.TaskId, context.DelegatedTaskId);
        Assert.Equal("b.hub", context.HubId);
        Assert.Equal(ProjectId, context.ProjectId);
        Assert.Equal(h.ConversationId, context.ConversationId);
    }

    [Fact]
    public async Task ProcessAsync_RemovesDelegationToolsFromDelegate()
    {
        var h = await NewHarnessAsync();
        var task = await AddDelegatedTaskAsync(h, "Tools");

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        Assert.Equal(["create-folder-structure", "list-folders"], Assert.Single(h.ToolCatalog.RequestedToolIds));
        Assert.Equal(["create-folder-structure", "list-folders"], h.ChatClient.ToolNames.Order().ToList());
    }

    [Fact]
    public async Task ProcessAsync_ChatClientThrows_FailsTaskAndPostsWarning()
    {
        var h = await NewHarnessAsync();
        h.ChatClient.Exception = new InvalidOperationException("The model is offline.");
        var task = await AddDelegatedTaskAsync(h, "Broken");

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        var stored = (await h.TaskService.GetTaskAsync(task.TaskId))!;
        Assert.Equal(AgentTaskStatus.Failed, stored.Status);
        Assert.Contains("The model is offline.", stored.ErrorDetail);
        Assert.Null(stored.Result);

        var message = Assert.Single(await MessagesAsync(h));
        Assert.StartsWith("⚠️ **Broken** — ", message.Content);
        Assert.Equal("agent-architect", message.AgentId);
        Assert.Equal(message.Id, stored.RelatedMessageId);
    }

    [Fact]
    public async Task ProcessAsync_DependencyFailed_FailsWithoutRunning()
    {
        var h = await NewHarnessAsync();
        var dependency = await AddDelegatedTaskAsync(h, "First", status: AgentTaskStatus.Failed);
        var task = await AddDelegatedTaskAsync(h, "Second", dependsOn: dependency.TaskId);

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        Assert.Empty(h.ChatClient.Requests);
        var stored = (await h.TaskService.GetTaskAsync(task.TaskId))!;
        Assert.Equal(AgentTaskStatus.Failed, stored.Status);
        Assert.Equal("Dependency 'First' did not complete.", stored.ErrorDetail);
        Assert.Equal("⚠️ **Second** — Dependency 'First' did not complete.", Assert.Single(await MessagesAsync(h)).Content);
    }

    [Fact]
    public async Task ProcessAsync_MissingDependency_FailsWithoutRunning()
    {
        var h = await NewHarnessAsync();
        var task = await AddDelegatedTaskAsync(h, "Orphan", dependsOn: "no-such-task");

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        Assert.Empty(h.ChatClient.Requests);
        Assert.Equal(AgentTaskStatus.Failed, (await h.TaskService.GetTaskAsync(task.TaskId))!.Status);
    }

    [Fact]
    public async Task ProcessAsync_WaitsForDependencyThenRuns()
    {
        var h = await NewHarnessAsync();
        h.Processor.DependencyPollInterval = TimeSpan.FromMilliseconds(10);
        var dependency = await AddDelegatedTaskAsync(h, "First", status: AgentTaskStatus.Running);
        var task = await AddDelegatedTaskAsync(h, "Second", dependsOn: dependency.TaskId);

        var processing = h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);
        await Task.Delay(50);
        Assert.Empty(h.ChatClient.Requests);
        Assert.Equal(AgentTaskStatus.Pending, (await h.TaskService.GetTaskAsync(task.TaskId))!.Status);

        dependency.Status = AgentTaskStatus.Completed;
        await h.TaskService.UpdateTaskAsync(dependency);
        await processing.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(h.ChatClient.Requests);
        Assert.Equal(AgentTaskStatus.Completed, (await h.TaskService.GetTaskAsync(task.TaskId))!.Status);
    }

    [Fact]
    public async Task ProcessAsync_MissingDelegate_FailsTask()
    {
        var h = await NewHarnessAsync();
        var task = await AddDelegatedTaskAsync(h, "Nobody", agentId: "agent-deleted");

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);

        Assert.Empty(h.ChatClient.Requests);
        var stored = (await h.TaskService.GetTaskAsync(task.TaskId))!;
        Assert.Equal(AgentTaskStatus.Failed, stored.Status);
        Assert.Contains("no longer exists", stored.ErrorDetail);
        Assert.StartsWith("⚠️ **Nobody** — ", Assert.Single(await MessagesAsync(h)).Content);
    }

    [Fact]
    public async Task ProcessAsync_TaskNotPending_DoesNothing()
    {
        var h = await NewHarnessAsync();
        var task = await AddDelegatedTaskAsync(h, "Already done", status: AgentTaskStatus.Completed);

        await h.Processor.ProcessAsync(task.TaskId, CancellationToken.None);
        await h.Processor.ProcessAsync("no-such-task", CancellationToken.None);

        Assert.Empty(h.ChatClient.Requests);
        Assert.Empty(await MessagesAsync(h));
    }

    [Fact]
    public async Task RecoverInterruptedTasksAsync_FailsRunningAndRequeuesPendingDelegatedTasks()
    {
        var h = await NewHarnessAsync();
        var running = await AddDelegatedTaskAsync(h, "Was running", status: AgentTaskStatus.Running);
        var pending = await AddDelegatedTaskAsync(h, "Was pending");
        var ownRunning = await h.TaskService.CreateTaskAsync(h.ConversationId, ProjectId, "Publish model");
        ownRunning.Status = AgentTaskStatus.Running;
        await h.TaskService.UpdateTaskAsync(ownRunning);
        var ownPending = await h.TaskService.CreateTaskAsync(h.ConversationId, ProjectId, "Not delegated");
        Drain(h.Queue);

        await h.Processor.RecoverInterruptedTasksAsync();

        var failed = (await h.TaskService.GetTaskAsync(running.TaskId))!;
        Assert.Equal(AgentTaskStatus.Failed, failed.Status);
        Assert.Equal("Interrupted by an app restart.", failed.ErrorDetail);
        Assert.Equal("⚠️ **Was running** — Interrupted by an app restart.", Assert.Single(await MessagesAsync(h)).Content);

        var requeued = Drain(h.Queue);
        Assert.Contains(pending.TaskId, requeued);
        Assert.DoesNotContain(ownPending.TaskId, requeued);
        Assert.Equal(AgentTaskStatus.Pending, (await h.TaskService.GetTaskAsync(pending.TaskId))!.Status);

        Assert.Equal(AgentTaskStatus.Running, (await h.TaskService.GetTaskAsync(ownRunning.TaskId))!.Status);
        Assert.Equal(AgentTaskStatus.Pending, (await h.TaskService.GetTaskAsync(ownPending.TaskId))!.Status);
    }

    // Returns a canned reply (or throws), and records each request's messages and tool names.
    private sealed class FakeChatClient : IChatClient
    {
        public string Reply { get; set; } = "Done the work.";
        public Exception? Exception { get; set; }
        public List<List<ChatMessage>> Requests { get; } = [];
        public List<string> ToolNames { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            ToolNames.AddRange(options?.Tools?.Select(t => t.Name) ?? []);
            if (Exception != null)
            {
                throw Exception;
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey == null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    // Returns a no-op function per requested ID, and records the IDs and the scope's tool context.
    private sealed class RecordingToolCatalogService : IToolCatalogService
    {
        private static readonly List<ToolDescriptor> Tools =
        [
            new() { ToolId = "list-folders", Module = ToolModules.Folders },
            new() { ToolId = "create-folder-structure", Module = ToolModules.Folders },
            new() { ToolId = "delegate-task", Module = ToolModules.Delegation },
            new() { ToolId = "get-delegated-tasks", Module = ToolModules.Delegation },
        ];

        public List<List<string>> RequestedToolIds { get; } = [];
        public List<AgentToolContext> Contexts { get; } = [];

        public IReadOnlyList<ToolDescriptor> GetAllTools() => Tools;

        public IReadOnlyList<AIFunction> GetAIFunctions(IEnumerable<string> toolIds, IServiceProvider services)
        {
            var ids = toolIds.ToList();
            RequestedToolIds.Add(ids);
            Contexts.Add(services.GetRequiredService<AgentToolContext>());
            return ids.Select(id => AIFunctionFactory.Create(() => "ok", name: id)).ToList();
        }
    }

    private sealed class FakeAgentRegistryService(params AgentDefinition[] agents) : IAgentRegistryService
    {
        public Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent) => Task.FromResult(agent);
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsByProjectAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<AgentDefinition>>(agents.Where(a => a.ProjectId == projectId).ToList());
        public Task<AgentDefinition?> GetAgentAsync(string agentId) =>
            Task.FromResult(agents.FirstOrDefault(a => a.AgentId == agentId));
        public Task UpdateAgentAsync(AgentDefinition agent) => Task.CompletedTask;
        public Task DeleteAgentAsync(string agentId) => Task.CompletedTask;
        public int GetAgentCount() => agents.Length;
    }
}
