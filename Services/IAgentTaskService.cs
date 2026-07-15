using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentTaskService
{
    Task<AgentTaskInfo> CreateTaskAsync(string conversationId, string projectId, string name);
    Task UpdateTaskAsync(AgentTaskInfo task);
    Task<IReadOnlyList<AgentTaskInfo>> GetTasksByConversationAsync(string conversationId);
}
