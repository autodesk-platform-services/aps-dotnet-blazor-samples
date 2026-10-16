using Autodesk.Construction.AccountAdmin.Model;

namespace ApsSamples.Services;

// Account Admin API calls for project users and hub (account) users. Project and account IDs are
// in account form, without the Data Management "b." prefix.
public interface IProjectUserService
{
    Task<IReadOnlyList<ProjectUser>> FindProjectUsersAsync(string accountProjectId, string? name, string? email, string accessToken);

    Task<ProjectUserResponse> AddProjectUserAsync(string accountProjectId, string email, string? companyId, List<string> roleIds, List<string> productKeys, ProductAccess productAccess, string accessToken);

    Task<ProjectUserResponse> UpdateProjectUserAsync(string accountProjectId, string userId, string? companyId, List<string>? roleIds, string accessToken);

    Task<IReadOnlyList<User>> SearchHubUsersAsync(string accountId, string? name, string? email, string accountTwoLeggedToken);
}
