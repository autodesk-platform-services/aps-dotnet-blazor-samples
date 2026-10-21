using Autodesk.Construction.AccountAdmin;
using Autodesk.Construction.AccountAdmin.Model;

namespace ApsSamples.Services;

public class ProjectUserService(AdminClient adminClient) : IProjectUserService
{
    private const int Limit = 50;

    // Product keys a user can be given access to, in the API's camelCase form. The SDK's
    // AndcloudWorksharing member serializes to "and cloudWorksharing", which the API rejects.
    public static readonly IReadOnlyList<string> ProductKeyNames = Enum.GetValues<ProductKeys>()
        .Where(k => k != ProductKeys.AndcloudWorksharing)
        .Select(k => char.ToLowerInvariant(k.ToString()[0]) + k.ToString()[1..])
        .ToList();

    public async Task<IReadOnlyList<ProjectUser>> FindProjectUsersAsync(string accountProjectId, string? name, string? email, string accessToken)
    {
        var users = await adminClient.GetProjectUsersAsync(
            projectId: accountProjectId,
            filterName: string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            filterEmail: string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            filterTextMatch: FilterTextMatch.Contains,
            limit: Limit,
            accessToken: accessToken);

        return users?.Results ?? [];
    }

    public async Task<ProjectUserResponse> AddProjectUserAsync(
        string accountProjectId, string email, string? companyId, List<string> roleIds, List<string> productKeys, ProductAccess productAccess, string accessToken)
    {
        var payload = new ProjectUserPayload
        {
            Email = email,
            CompanyId = string.IsNullOrEmpty(companyId) ? null : companyId,
            RoleIds = roleIds,
            Products = BuildProductPayloads(productKeys, productAccess),
        };

        return await adminClient.AssignProjectUserAsync(
            projectId: accountProjectId,
            projectUserPayload: payload,
            accessToken: accessToken);
    }

    public async Task<ProjectUserResponse> UpdateProjectUserAsync(string accountProjectId, string userId, string? companyId, List<string>? roleIds, string accessToken)
    {
        // Unset fields are left out of the request body, so only the supplied ones change.
        var payload = new ProjectUsersUpdatePayload
        {
            CompanyId = string.IsNullOrEmpty(companyId) ? null : companyId,
            RoleIds = roleIds,
        };

        return await adminClient.UpdateProjectUserAsync(
            projectId: accountProjectId,
            userId: userId,
            projectUsersUpdatePayload: payload,
            accessToken: accessToken);
    }

    public async Task<IReadOnlyList<User>> SearchHubUsersAsync(string accountId, string? name, string? email, string accountTwoLeggedToken)
    {
        var users = await adminClient.SearchUsersAsync(
            accountId: accountId,
            region: Region.US,
            name: string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            email: string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            partial: true,
            limit: Limit,
            accessToken: accountTwoLeggedToken);

        return users ?? [];
    }

    // Project Administration grants admin access to that module; every other product gets the
    // given access (standard member access by default). Throws ArgumentException for an unknown key.
    public static List<ProjectUserPayloadProducts> BuildProductPayloads(IEnumerable<string> productKeys, ProductAccess access = ProductAccess.Member) =>
        productKeys
            .Select(k =>
            {
                if (!TryParseProductKey(k, out var key))
                {
                    throw new ArgumentException($"Unknown product key '{k}'.", nameof(productKeys));
                }

                return new ProjectUserPayloadProducts
                {
                    Key = key,
                    Access = key == ProductKeys.ProjectAdministration ? ProductAccess.Administrator : access,
                };
            })
            .ToList();

    // Matches a product key by name, ignoring case. Unlike a bare Enum.TryParse, numeric strings
    // and the unusable AndcloudWorksharing member are rejected.
    public static bool TryParseProductKey(string? value, out ProductKeys key)
    {
        key = default;
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && ProductKeyNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            && Enum.TryParse(trimmed, ignoreCase: true, out key);
    }
}
