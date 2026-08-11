using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IProjectRoleService
{
    Task<IReadOnlyList<string>> GetProjectRolesAsync(string projectId, string userId, string accessToken);
    Task<bool> IsBimManagerAsync(string projectId, string userId, string accessToken);
    Task<IReadOnlyList<ProjectRoleInfo>> GetAllProjectRolesAsync(string projectId, string accessToken);
    Task<bool> IsProjectAdminAsync(string projectId, string userId, string accessToken);
}
