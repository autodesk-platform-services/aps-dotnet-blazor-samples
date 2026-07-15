namespace ApsSamples.Services;

public interface IProjectRoleService
{
    Task<IReadOnlyList<string>> GetProjectRolesAsync(string projectId, string accessToken);
    Task<bool> IsBimManagerAsync(string projectId, string accessToken);
}
