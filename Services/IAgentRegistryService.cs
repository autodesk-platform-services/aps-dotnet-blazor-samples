using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentRegistryService
{
    Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent);
    Task<IReadOnlyList<AgentDefinition>> GetAgentsByProjectAsync(string projectId);
    Task<AgentDefinition?> GetAgentAsync(string agentId);
    Task UpdateAgentAsync(AgentDefinition agent);
    Task DeleteAgentAsync(string agentId);
    int GetAgentCount();
}
