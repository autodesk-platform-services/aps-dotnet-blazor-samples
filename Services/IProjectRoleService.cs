namespace ApsSamples.Services;

public interface IProjectRoleService
{
    Task<IReadOnlyList<string>> GetProjectRolesAsync(string projectId, string userId, string accessToken);
    Task<bool> IsBimManagerAsync(string projectId, string userId, string accessToken);
}
