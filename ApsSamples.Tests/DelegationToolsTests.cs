using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using ApsSamples.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class DelegationToolsTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private const string ProjectId = "b.project";

    public DelegationToolsTests(TempWorkingDirectoryFixture _) { }

    private static readonly AgentDefinition BimManager = Agent("bim", "BIM Manager Assistant", "BIM Manager", ["delegate-task"]);
    private static readonly AgentDefinition Architect = Agent("agent-architect", "Architect Assistant", "Architect", ["create-folder-structure", "list-folders", "list-revit-models"]);
    private static readonly AgentDefinition MepA = Agent("mep-a", "MEP Assistant A", "MEP Engineer", []);
    private static readonly AgentDefinition MepB = Agent("mep-b", "MEP Assistant B", "MEP Engineer", []);
    private static readonly AgentDefinition NoServiceAccount = Agent("structural", "Structural Assistant", "Structural Engineer", [], ssaId: "");
    private static readonly AgentDefinition OtherProject = Agent("other", "Other Project Assistant", "Architect", [], projectId: "b.other");

    private static AgentDefinition Agent(string id, string name, string role, List<string> toolIds, string ssaId = "ssa", string projectId = ProjectId) =>
        new()
        {
            AgentId = id,
            AgentName = name,
            RoleName = role,
            ProjectId = projectId,
            SsaId = ssaId == "ssa" ? $"ssa-{id}" : ssaId,
            EnabledToolIds = toolIds
        };

    private sealed record Harness(
        DelegationTools Tools,
        AgentToolContext Context,
        AgentTaskService TaskService,
        DelegatedTaskQueue Queue,
        string ConversationId);

    private static async Task<Harness> NewHarnessAsync(params AgentDefinition[] agents)
    {
        var conversationService = new AgentConversationService(NullLogger<AgentConversationService>.Instance);
        var conversation = await conversationService.CreateConversationAsync(ProjectId, "user-1", BimManager.AgentId);
        var taskService = new AgentTaskService(conversationService, null!, NullLogger<AgentTaskService>.Instance);
        var queue = new DelegatedTaskQueue();
        var context = new AgentToolContext(null!, null!)
        {
            HubId = "b.hub",
            ProjectId = ProjectId,
            ConversationId = conversation.ConversationId,
            AgentId = BimManager.AgentId,
            SsaId = BimManager.SsaId
        };

        var registry = new FakeAgentRegistryService(agents.Length > 0
            ? agents
            : [BimManager, Architect, MepA, MepB, NoServiceAccount, OtherProject]);

        var tools = new DelegationTools(context, registry, taskService, new StubToolCatalogService(), queue,
            new StubUserSessionService { UserId = "user-1" });

        return new Harness(tools, context, taskService, queue, conversation.ConversationId);
    }

    private static List<DelegatedTaskWorkItem> Drain(DelegatedTaskQueue queue)
    {
        var items = new List<DelegatedTaskWorkItem>();
        while (queue.Reader.TryRead(out var item))
        {
            items.Add(item);
        }
        return items;
    }

    // DelegateTask's reply ends with "(task id: <id>). ...".
    private static string TaskIdFrom(string reply)
    {
        const string marker = "(task id: ";
        var start = reply.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, reply);
        start += marker.Length;
        return reply[start..reply.IndexOf(')', start)];
    }

    [Fact]
    public async Task ListProjectAssistantsAsync_ExcludesSelfAndAssistantsWithoutServiceAccount()
    {
        var h = await NewHarnessAsync();

        var result = await h.Tools.ListProjectAssistantsAsync();

        using var doc = JsonDocument.Parse(result);
        var ids = doc.RootElement.EnumerateArray().Select(a => a.GetProperty("agentId").GetString()).ToList();
        Assert.Equal(["agent-architect", "mep-a", "mep-b"], ids);

        var architect = doc.RootElement.EnumerateArray().Single(a => a.GetProperty("agentId").GetString() == "agent-architect");
        Assert.Equal("Architect Assistant", architect.GetProperty("agentName").GetString());
        Assert.Equal("Architect", architect.GetProperty("roleName").GetString());
        Assert.Equal([ToolModules.Folders, ToolModules.RevitModels],
            architect.GetProperty("toolModules").EnumerateArray().Select(m => m.GetString()).ToList());
    }

    [Fact]
    public async Task ListProjectAssistantsAsync_NoOtherAssistants_ReturnsSentence()
    {
        var h = await NewHarnessAsync(BimManager, NoServiceAccount);

        var result = await h.Tools.ListProjectAssistantsAsync();

        Assert.StartsWith("There are no other assistants", result);
    }

    [Theory]
    [InlineData("agent-architect")]
    [InlineData("architect assistant")]
    [InlineData("ARCHITECT")]
    public async Task DelegateTaskAsync_ResolvesAssistantByIdNameOrRole_CreatesPendingTaskAndEnqueuesIt(string assistant)
    {
        var h = await NewHarnessAsync();

        var reply = await h.Tools.DelegateTaskAsync(assistant, " Create Building B folders ", " Create Building B/WIP/Architecture. ");

        var taskId = TaskIdFrom(reply);
        Assert.StartsWith("Delegated 'Create Building B folders' to Architect Assistant", reply);

        var task = await h.TaskService.GetTaskAsync(taskId);
        Assert.NotNull(task);
        Assert.Equal(AgentTaskStatus.Pending, task.Status);
        Assert.Equal(h.ConversationId, task.ConversationId);
        Assert.Equal(ProjectId, task.ProjectId);
        Assert.Equal("b.hub", task.HubId);
        Assert.Equal("Create Building B folders", task.Name);
        Assert.Equal("Create Building B/WIP/Architecture.", task.Instructions);
        Assert.Equal("agent-architect", task.AgentId);
        Assert.Equal("Architect Assistant", task.AgentName);
        Assert.Equal("bim", task.DelegatedByAgentId);
        Assert.Equal("BIM Manager Assistant", task.DelegatedByAgentName);
        Assert.Equal("user-1", task.RequestedByUserId);
        Assert.Equal("Queued for Architect Assistant.", task.ProgressDetail);
        Assert.Empty(task.DependsOnTaskIds);

        var item = Assert.Single(Drain(h.Queue));
        Assert.Equal(taskId, item.TaskId);
    }

    [Theory]
    [InlineData("Plumber")]
    [InlineData("bim")]
    [InlineData("structural")]
    [InlineData("other")]
    [InlineData("")]
    public async Task DelegateTaskAsync_UnknownAssistant_ReturnsValidNamesAndCreatesNothing(string assistant)
    {
        var h = await NewHarnessAsync();

        var reply = await h.Tools.DelegateTaskAsync(assistant, "Title", "Do it.");

        Assert.StartsWith("No assistant matches", reply);
        Assert.Contains("Architect Assistant (Architect)", reply);
        Assert.Empty(await h.TaskService.GetTasksByConversationAsync(h.ConversationId));
        Assert.Empty(Drain(h.Queue));
    }

    [Fact]
    public async Task DelegateTaskAsync_AmbiguousRole_ReturnsMessageAndCreatesNothing()
    {
        var h = await NewHarnessAsync();

        var reply = await h.Tools.DelegateTaskAsync("MEP Engineer", "Title", "Do it.");

        Assert.Contains("matches several assistants", reply);
        Assert.Empty(await h.TaskService.GetTasksByConversationAsync(h.ConversationId));
        Assert.Empty(Drain(h.Queue));
    }

    [Fact]
    public async Task DelegateTaskAsync_WithDependencyFromSameConversation_StoresDependency()
    {
        var h = await NewHarnessAsync();
        var firstId = TaskIdFrom(await h.Tools.DelegateTaskAsync("agent-architect", "First", "Do the first part."));

        var reply = await h.Tools.DelegateTaskAsync("mep-a", "Second", "Do the second part.", [firstId]);

        var second = await h.TaskService.GetTaskAsync(TaskIdFrom(reply));
        Assert.NotNull(second);
        Assert.Equal([firstId], second.DependsOnTaskIds);
        Assert.Equal(2, Drain(h.Queue).Count);
    }

    [Fact]
    public async Task DelegateTaskAsync_UnknownDependency_IsRejected()
    {
        var h = await NewHarnessAsync();

        var reply = await h.Tools.DelegateTaskAsync("agent-architect", "Title", "Do it.", ["no-such-task"]);

        Assert.Contains("'no-such-task'", reply);
        Assert.Empty(await h.TaskService.GetTasksByConversationAsync(h.ConversationId));
        Assert.Empty(Drain(h.Queue));
    }

    [Fact]
    public async Task DelegateTaskAsync_DependencyFromOtherConversation_IsRejected()
    {
        var h = await NewHarnessAsync();
        var otherTask = await h.TaskService.CreateTaskAsync("other-conversation", ProjectId, "Elsewhere");

        var reply = await h.Tools.DelegateTaskAsync("agent-architect", "Title", "Do it.", [otherTask.TaskId]);

        Assert.Contains($"'{otherTask.TaskId}'", reply);
        Assert.Empty(await h.TaskService.GetTasksByConversationAsync(h.ConversationId));
        Assert.Empty(Drain(h.Queue));
    }

    [Fact]
    public async Task DelegateTaskAsync_InDelegatedRun_Refuses()
    {
        var h = await NewHarnessAsync();
        h.Context.DelegatedTaskId = "running-task";

        var reply = await h.Tools.DelegateTaskAsync("agent-architect", "Title", "Do it.");

        Assert.Equal("Delegated assistants can't delegate further.", reply);
        Assert.Empty(await h.TaskService.GetTasksByConversationAsync(h.ConversationId));
        Assert.Empty(Drain(h.Queue));
    }

    [Theory]
    [InlineData(" ", "Do it.")]
    [InlineData("Title", "")]
    public async Task DelegateTaskAsync_MissingTitleOrInstructions_CreatesNothing(string title, string instructions)
    {
        var h = await NewHarnessAsync();

        var reply = await h.Tools.DelegateTaskAsync("agent-architect", title, instructions);

        Assert.StartsWith("Give the task", reply);
        Assert.Empty(await h.TaskService.GetTasksByConversationAsync(h.ConversationId));
    }

    [Fact]
    public async Task GetDelegatedTasksAsync_ReturnsOnlyDelegatedTasksOfCurrentConversation()
    {
        var h = await NewHarnessAsync();
        var delegatedId = TaskIdFrom(await h.Tools.DelegateTaskAsync("agent-architect", "Delegated", "Do it."));
        await h.TaskService.CreateTaskAsync(h.ConversationId, ProjectId, "Publish model");
        var elsewhere = await h.TaskService.CreateTaskAsync("other-conversation", ProjectId, "Elsewhere");
        elsewhere.DelegatedByAgentId = "bim";
        await h.TaskService.UpdateTaskAsync(elsewhere);

        var result = await h.Tools.GetDelegatedTasksAsync();

        using var doc = JsonDocument.Parse(result);
        var task = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(delegatedId, task.GetProperty("taskId").GetString());
        Assert.Equal("Delegated", task.GetProperty("title").GetString());
        Assert.Equal("Architect Assistant", task.GetProperty("assignedTo").GetString());
        Assert.Equal("Pending", task.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetDelegatedTasksAsync_NoneDelegated_ReturnsSentence()
    {
        var h = await NewHarnessAsync();

        var result = await h.Tools.GetDelegatedTasksAsync();

        Assert.Equal("No tasks have been delegated in this conversation.", result);
    }

    private sealed class FakeAgentRegistryService(IEnumerable<AgentDefinition> agents) : IAgentRegistryService
    {
        private readonly List<AgentDefinition> _agents = agents.ToList();

        public Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent) => Task.FromResult(agent);
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsByProjectAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<AgentDefinition>>(_agents.Where(a => a.ProjectId == projectId).ToList());
        public Task<AgentDefinition?> GetAgentAsync(string agentId) =>
            Task.FromResult(_agents.FirstOrDefault(a => a.AgentId == agentId));
        public Task UpdateAgentAsync(AgentDefinition agent) => Task.CompletedTask;
        public Task DeleteAgentAsync(string agentId) => Task.CompletedTask;
        public int GetAgentCount() => _agents.Count;
    }

    private sealed class StubToolCatalogService : IToolCatalogService
    {
        private static readonly List<ToolDescriptor> Tools =
        [
            new() { ToolId = "list-revit-models", Module = ToolModules.RevitModels },
            new() { ToolId = "list-folders", Module = ToolModules.Folders },
            new() { ToolId = "create-folder-structure", Module = ToolModules.Folders },
            new() { ToolId = "delegate-task", Module = ToolModules.Delegation },
        ];

        public IReadOnlyList<ToolDescriptor> GetAllTools() => Tools;
        public IReadOnlyList<AIFunction> GetAIFunctions(IEnumerable<string> toolIds, IServiceProvider services) => [];
    }

    private sealed class StubUserSessionService : IUserSessionService
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string? UserId { get; set; }
        public bool IsAuthenticated => UserId != null;
        public void ClearSession() => UserId = null;
    }
}
