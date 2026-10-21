using ApsSamples.Models;
using Microsoft.Extensions.AI;

namespace ApsSamples.Services;

public interface IToolCatalogService
{
    IReadOnlyList<ToolDescriptor> GetAllTools();

    // Builds the AI functions for the enabled tool IDs, resolving tool classes from the caller's
    // scoped service provider. Unknown or not-yet-wired IDs are skipped.
    IReadOnlyList<AIFunction> GetAIFunctions(IEnumerable<string> toolIds, IServiceProvider services);
}
