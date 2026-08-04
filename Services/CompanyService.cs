using ApsSamples.Models;
using Autodesk.Construction.AccountAdmin;

namespace ApsSamples.Services;

public class CompanyService(AdminClient adminClient, ILogger<CompanyService> logger) : ICompanyService
{
    public async Task<IReadOnlyList<CompanyInfo>> GetProjectCompaniesAsync(string projectId, string accessToken)
    {
        try
        {
            // Account Admin project IDs drop the "b." hub prefix used by the Data Management API.
            var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectId[2..]
                : projectId;

            var project = await adminClient.GetProjectAsync(
                projectId: accountProjectId,
                accessToken: accessToken);

            var accountId = project?.AccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                logger.LogWarning("Project {ProjectId} has no AccountId; cannot fetch companies.", projectId);
                return Array.Empty<CompanyInfo>();
            }

            var companies = await adminClient.GetProjectCompaniesAsync(
                accountId: accountId,
                projectId: accountProjectId,
                accessToken: accessToken);

            return companies?
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new CompanyInfo
                {
                    CompanyId = c.Id ?? string.Empty,
                    CompanyName = c.Name ?? string.Empty,
                })
                .ToList() ?? new List<CompanyInfo>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching project companies for project {ProjectId}", projectId);
            return Array.Empty<CompanyInfo>();
        }
    }
}
