using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentTaskService
{
    Task<AgentTaskInfo> CreateTaskAsync(string conversationId, string projectId, string name);
    Task UpdateTaskAsync(AgentTaskInfo task);
    Task<IReadOnlyList<AgentTaskInfo>> GetTasksByConversationAsync(string conversationId);
    Task<IReadOnlyList<AgentTaskInfo>> GetTasksByProjectAsync(string projectId);
    Task<AgentTaskInfo?> GetTaskByWebhookHookIdAsync(string hookId);
}
