using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Extensions.AI;

namespace ApsSamples.Services;

public interface IToolCatalogService
{
    IReadOnlyList<ToolDescriptor> GetAllTools();
    IReadOnlyList<AIFunction> GetAIFunctions(IEnumerable<string> toolIds, BimManagerAssistantTools tools);
}
