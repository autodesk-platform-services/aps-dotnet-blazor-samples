using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class AgentTaskServiceTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private readonly TempWorkingDirectoryFixture _fixture;

    public AgentTaskServiceTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    // The scope factory is only used for webhook cleanup, which these tests never trigger.
    private static AgentTaskService NewService() =>
        new(new AgentConversationService(NullLogger<AgentConversationService>.Instance),
            null!,
            NullLogger<AgentTaskService>.Instance);

    private string TasksFilePath => Path.Combine(_fixture.TempPath, "Data", "agent-tasks.json");

    [Fact]
    public async Task GetTaskAsync_ReturnsTaskById_OrNullWhenUnknown()
    {
        var svc = NewService();
        var task = await svc.CreateTaskAsync("conv", "proj", "Task A");

        var found = await svc.GetTaskAsync(task.TaskId);
        var missing = await svc.GetTaskAsync("no-such-task");

        Assert.Same(task, found);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetTasksByStatusAsync_ReturnsOnlyMatchingTasks_OldestFirst()
    {
        var svc = NewService();
        var conversationId = Guid.NewGuid().ToString();
        var first = await svc.CreateTaskAsync(conversationId, "proj", "First");
        var second = await svc.CreateTaskAsync(conversationId, "proj", "Second");
        var running = await svc.CreateTaskAsync(conversationId, "proj", "Running");
        running.Status = AgentTaskStatus.Running;
        await svc.UpdateTaskAsync(running);

        var pending = (await svc.GetTasksByStatusAsync(AgentTaskStatus.Pending))
            .Where(t => t.ConversationId == conversationId)
            .Select(t => t.TaskId)
            .ToList();

        Assert.Equal(new[] { first.TaskId, second.TaskId }, pending);
    }

    [Fact]
    public async Task DelegationFields_RoundTripThroughFile()
    {
        var svc = NewService();
        var task = await svc.CreateTaskAsync("conv", "proj", "Delegated");
        task.AgentId = "architect";
        task.AgentName = "Architect Assistant";
        task.DelegatedByAgentId = "bim-manager";
        task.DelegatedByAgentName = "BIM Manager Assistant";
        task.HubId = "hub";
        task.Instructions = "Create the folders.";
        task.Result = "Folders created.";
        task.DependsOnTaskIds = new List<string> { "dep-1", "dep-2" };
        await svc.UpdateTaskAsync(task);

        var reloaded = await NewService().GetTaskAsync(task.TaskId);

        Assert.NotNull(reloaded);
        Assert.Equal("Architect Assistant", reloaded.AgentName);
        Assert.Equal("bim-manager", reloaded.DelegatedByAgentId);
        Assert.Equal("BIM Manager Assistant", reloaded.DelegatedByAgentName);
        Assert.Equal("hub", reloaded.HubId);
        Assert.Equal("Create the folders.", reloaded.Instructions);
        Assert.Equal("Folders created.", reloaded.Result);
        Assert.Equal(new[] { "dep-1", "dep-2" }, reloaded.DependsOnTaskIds);
    }

    [Fact]
    public async Task LegacyFileWithoutDelegationFields_StillLoads()
    {
        var taskId = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(TasksFilePath)!);
        await File.WriteAllTextAsync(TasksFilePath, $$"""
            [
              {
                "taskId": "{{taskId}}",
                "conversationId": "conv",
                "projectId": "proj",
                "agentId": "agent",
                "requestedByUserId": "user",
                "name": "Publish model",
                "status": "Running",
                "startedAt": "2026-01-01T00:00:00Z"
              }
            ]
            """);

        var task = await NewService().GetTaskAsync(taskId);

        Assert.NotNull(task);
        Assert.Equal("Publish model", task.Name);
        Assert.Null(task.DelegatedByAgentId);
        Assert.Null(task.Result);
        Assert.Empty(task.DependsOnTaskIds);
    }
}
