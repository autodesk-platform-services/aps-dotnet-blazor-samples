using System.ComponentModel;
using System.Net;
using ApsSamples.Services;
using Autodesk.Construction.AccountAdmin.Model;

namespace ApsSamples.Tools;

// Project user tools for any agent: search the hub's users, check who is in the project and with
// what access, and add or update project users. Every call runs under the agent's service account,
// except hub user search, which needs an app-level 2-legged token.
public class ProjectUserTools(
    IProjectUserService projectUserService,
    IProjectRoleService projectRoleService,
    ICompanyService companyService,
    AgentToolContext context)
{
    [Description("Searches the users of the hub (ACC account) by name and/or email, matching partial text. Returns each user's name, email, status and company. The result only covers account members: it does not mean the user is in the current project (use FindProjectUsers for that).")]
    public async Task<string> SearchHubUsersAsync(
        [Description("Part of the user's name to search for. Leave empty to search by email only.")] string? name = null,
        [Description("Part of the user's email to search for. Leave empty to search by name only.")] string? email = null)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
        {
            return "Give a name or an email to search for.";
        }

        try
        {
            var accountId = context.AccountId;
            var accessToken = await context.GetAccountTwoLeggedTokenAsync();

            var users = await projectUserService.SearchHubUsersAsync(accountId, name, email, accessToken);
            if (users.Count == 0)
            {
                return "No matching user in this hub.";
            }

            return ToolResults.Json(new
            {
                Count = users.Count,
                Users = users.Select(u => new
                {
                    u.Name,
                    u.Email,
                    u.Status,
                    CompanyName = u.CompanyName ?? u.Company
                })
            });
        }
        catch (Exception ex)
        {
            var error = ToolResults.Error("search the hub users", ex);
            return ToolResults.StatusCodeOf(ex) is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? error + " Hub user search needs this app to be added as a custom integration in the ACC account."
                : error;
        }
    }

    [Description("Finds users in the current project by name and/or email, matching partial text. Returns, per user, the user ID, name, email, status, company, role names, access levels (account admin, project admin, executive) and the products they have access to. Use it to check whether a user is in the project and what permissions and access they have. Leave both filters empty to list the first 50 project users.")]
    public async Task<string> FindProjectUsersAsync(
        [Description("Part of the user's name to look for. Leave empty to match any name.")] string? name = null,
        [Description("Part of the user's email to look for. Leave empty to match any email.")] string? email = null)
    {
        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var users = await projectUserService.FindProjectUsersAsync(accountProjectId, name, email, accessToken);
            if (users.Count == 0)
            {
                return "No matching user in this project.";
            }

            return ToolResults.Json(new
            {
                Count = users.Count,
                Users = users.Select(u => ProjectUserInfo.From(u.Id, u.Name, u.Email, u.Status, u.CompanyName, u.Roles, u.AccessLevels, u.Products))
            });
        }
        catch (Exception ex)
        {
            return Failure("find the project users", ex);
        }
    }

    [Description("Lists the roles and companies that can be assigned to users in the current project, with their names and IDs. Use it to find valid role and company names for AddUserToProject and UpdateProjectUser.")]
    public async Task<string> ListProjectRolesAndCompaniesAsync()
    {
        try
        {
            var projectId = context.RequireProjectId();
            var accessToken = await context.GetAccessTokenAsync();

            var roles = await projectRoleService.GetAllProjectRolesAsync(projectId, accessToken);
            var companies = await companyService.GetProjectCompaniesAsync(projectId, accessToken);

            return ToolResults.Json(new
            {
                Roles = roles.Select(r => new { Id = r.RoleId, Name = r.RoleName }),
                Companies = companies.Select(c => new { Id = c.CompanyId, Name = c.CompanyName })
            });
        }
        catch (Exception ex)
        {
            return Failure("list the project roles and companies", ex);
        }
    }

    [Description("Adds a user to the current project by email, with a role and optionally a company and products. Role and company are given by name (see ListProjectRolesAndCompanies). Without products the user gets Docs as a member. Giving the projectAdministration product makes the user a project administrator. Does nothing if the email is already a project user.")]
    public async Task<string> AddUserToProjectAsync(
        [Description("Email of the user to add.")] string email,
        [Description("Name of the project role to assign, as listed by ListProjectRolesAndCompanies.")] string roleName,
        [Description("Name of the user's company, as listed by ListProjectRolesAndCompanies. Leave empty to not set a company.")] string? companyName = null,
        [Description("Product keys to give access to, for example [\"docs\", \"build\", \"modelCoordination\"]. Leave empty for Docs only. \"projectAdministration\" makes the user a project administrator.")] List<string>? productKeys = null,
        [Description("Access to grant for the products other than projectAdministration: \"member\" (default) or \"administrator\".")] string? productAccess = null)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return "Give the email of the user to add.";
        }

        if (string.IsNullOrWhiteSpace(roleName))
        {
            return "Give the name of the role to assign (see ListProjectRolesAndCompanies).";
        }

        if (!TryParseProductAccess(productAccess, out var access))
        {
            return $"Unknown product access '{productAccess}'. Use \"member\" or \"administrator\".";
        }

        var keys = productKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList() ?? [];
        if (keys.Count == 0)
        {
            keys.Add("docs");
        }

        var unknownKeys = keys.Where(k => !ProjectUserService.TryParseProductKey(k, out _)).ToList();
        if (unknownKeys.Count > 0)
        {
            return $"Unknown product key(s): {string.Join(", ", unknownKeys)}. Valid keys: {string.Join(", ", ProjectUserService.ProductKeyNames)}.";
        }

        email = email.Trim();
        try
        {
            var projectId = context.RequireProjectId();
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            if (await FindByEmailAsync(accountProjectId, email, accessToken) is { } existing)
            {
                return $"{email} is already a user in this project (user ID {existing.Id}). Use UpdateProjectUser to change their role or company.";
            }

            var role = await ResolveRoleAsync(projectId, roleName, accessToken);
            if (role.Error is not null)
            {
                return role.Error;
            }

            string? companyId = null;
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                var company = await ResolveCompanyAsync(projectId, companyName, accessToken);
                if (company.Error is not null)
                {
                    return company.Error;
                }

                companyId = company.Id;
            }

            var added = await projectUserService.AddProjectUserAsync(accountProjectId, email, companyId, [role.Id!], keys, access, accessToken);

            return ToolResults.Json(new
            {
                Added = true,
                User = ProjectUserInfo.From(added.Id, added.Name, added.Email, added.Status, added.CompanyName, added.Roles, added.AccessLevels, added.Products)
            });
        }
        catch (Exception ex)
        {
            return Failure($"add {email} to the project", ex);
        }
    }

    [Description("Updates the role and/or company of a user in the current project, found by exact email. Role and company are given by name (see ListProjectRolesAndCompanies). A new role replaces the user's current roles. Only the supplied fields change.")]
    public async Task<string> UpdateProjectUserAsync(
        [Description("Email of the project user to update.")] string email,
        [Description("Name of the new project role. Leave empty to keep the current roles.")] string? roleName = null,
        [Description("Name of the new company. Leave empty to keep the current company.")] string? companyName = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "Give the email of the project user to update.";
        }

        if (string.IsNullOrWhiteSpace(roleName) && string.IsNullOrWhiteSpace(companyName))
        {
            return "No change requested. Give a new role name and/or company name.";
        }

        email = email.Trim();
        try
        {
            var projectId = context.RequireProjectId();
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var user = await FindByEmailAsync(accountProjectId, email, accessToken);
            if (user is null || string.IsNullOrEmpty(user.Id))
            {
                return $"{email} is not a user in this project. Use AddUserToProject to add them.";
            }

            List<string>? roleIds = null;
            if (!string.IsNullOrWhiteSpace(roleName))
            {
                var role = await ResolveRoleAsync(projectId, roleName, accessToken);
                if (role.Error is not null)
                {
                    return role.Error;
                }

                roleIds = [role.Id!];
            }

            string? companyId = null;
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                var company = await ResolveCompanyAsync(projectId, companyName, accessToken);
                if (company.Error is not null)
                {
                    return company.Error;
                }

                companyId = company.Id;
            }

            var updated = await projectUserService.UpdateProjectUserAsync(accountProjectId, user.Id, companyId, roleIds, accessToken);

            return ToolResults.Json(new
            {
                Updated = true,
                User = ProjectUserInfo.From(updated.Id ?? user.Id, updated.Name, updated.Email ?? email, updated.Status, updated.CompanyName, updated.Roles, updated.AccessLevels, updated.Products)
            });
        }
        catch (Exception ex)
        {
            return Failure($"update the project user {email}", ex);
        }
    }

    // The email filter matches partial text, so pick the exact (case-insensitive) match.
    private async Task<ProjectUser?> FindByEmailAsync(string accountProjectId, string email, string accessToken)
    {
        var users = await projectUserService.FindProjectUsersAsync(accountProjectId, null, email, accessToken);
        return users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(string? Id, string? Error)> ResolveRoleAsync(string projectId, string roleName, string accessToken)
    {
        var roles = await projectRoleService.GetAllProjectRolesAsync(projectId, accessToken);
        if (roles.Count == 0)
        {
            return (null, "Could not load the project roles, so the role can't be assigned.");
        }

        var role = roles.FirstOrDefault(r => string.Equals(r.RoleName, roleName.Trim(), StringComparison.OrdinalIgnoreCase));
        return role is not null
            ? (role.RoleId, null)
            : (null, $"No role named '{roleName}' in this project. Valid roles: {string.Join(", ", roles.Select(r => r.RoleName))}.");
    }

    private async Task<(string? Id, string? Error)> ResolveCompanyAsync(string projectId, string companyName, string accessToken)
    {
        var companies = await companyService.GetProjectCompaniesAsync(projectId, accessToken);
        if (companies.Count == 0)
        {
            return (null, "Could not load the companies, so the company can't be assigned.");
        }

        var company = companies.FirstOrDefault(c => string.Equals(c.CompanyName, companyName.Trim(), StringComparison.OrdinalIgnoreCase));
        return company is not null
            ? (company.CompanyId, null)
            : (null, $"No company named '{companyName}'. Valid companies: {string.Join(", ", companies.Select(c => c.CompanyName))}.");
    }

    private static bool TryParseProductAccess(string? value, out ProductAccess access)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "member":
                access = ProductAccess.Member;
                return true;
            case "administrator" or "admin":
                access = ProductAccess.Administrator;
                return true;
            default:
                access = default;
                return false;
        }
    }

    private static string Failure(string action, Exception ex) => ToolResults.StatusCodeOf(ex) switch
    {
        HttpStatusCode.Forbidden => ToolResults.Error(action, ex) + " The agent's service account needs project admin access in this project to manage its users.",
        _ => ToolResults.Error(action, ex),
    };
}

// A project user as shown to the model: who they are, their roles, access levels and the products
// they can use (products with no access are left out).
public class ProjectUserInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Status { get; set; }
    public string? CompanyName { get; set; }
    public List<string> Roles { get; set; } = [];
    public bool AccountAdmin { get; set; }
    public bool ProjectAdmin { get; set; }
    public bool Executive { get; set; }
    public List<string> Products { get; set; } = [];

    public static ProjectUserInfo From(
        string? id, string? name, string? email, string? status, string? companyName,
        List<ProjectUserRoles>? roles, ProjectUserAccessLevels? accessLevels, List<ProjectUserProducts>? products) => new()
    {
        Id = id,
        Name = name,
        Email = email,
        Status = status,
        CompanyName = companyName,
        Roles = roles?.Select(r => r.Name).OfType<string>().Where(n => n.Length > 0).ToList() ?? [],
        AccountAdmin = accessLevels?.AccountAdmin == true,
        ProjectAdmin = accessLevels?.ProjectAdmin == true,
        Executive = accessLevels?.Executive == true,
        Products = products?
            .Where(p => !string.IsNullOrEmpty(p.Key) && !string.Equals(p.Access, "none", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Key} ({p.Access})")
            .ToList() ?? []
    };
}
