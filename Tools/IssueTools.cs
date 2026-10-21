using System.ComponentModel;
using System.Globalization;
using System.Net;
using ApsSamples.Services;

namespace ApsSamples.Tools;

// ACC issue tools for any agent: search, review (details and comments), create and update issues,
// and comment on them. Every call runs under the agent's service account. The Issues API has no
// delete, so issues are retired by closing them.
public class IssueTools(
    IIssuesService issuesService,
    IProjectUserService projectUserService,
    AgentToolContext context)
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 1000;
    private const string DateFormat = "yyyy-MM-dd";

    [Description("Searches the issues of the current project. All filters are optional. Returns a compact list with each issue's ID, display ID (the number shown in ACC), title, status, assignee and due date, plus the total number of matches. Use GetIssue for the full details and comments.")]
    public async Task<string> SearchIssuesAsync(
        [Description("Text to look for in the issue title or display ID. Leave empty to match any issue.")] string? text = null,
        [Description("Status to filter by: draft, open, pending, in_progress, completed, in_review, not_approved, in_dispute or closed. Several can be given separated by commas. Leave empty for any status.")] string? status = null,
        [Description("Email of the project user the issues are assigned to. Leave empty for any assignee.")] string? assigneeEmail = null,
        [Description("Maximum number of issues to return, from 1 to 100. Defaults to 25.")] int limit = 25)
    {
        string? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statuses = new List<string>();
            foreach (var value in status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!IssuesService.TryNormalizeStatus(value, out var normalized))
                {
                    return UnknownStatus(value);
                }

                statuses.Add(normalized);
            }

            statusFilter = statuses.Count > 0 ? string.Join(",", statuses.Distinct()) : null;
        }

        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            string? assignedTo = null;
            if (!string.IsNullOrWhiteSpace(assigneeEmail))
            {
                var assignee = await ResolveAssigneeAsync(accountProjectId, assigneeEmail, accessToken);
                if (assignee.Error is not null)
                {
                    return assignee.Error;
                }

                assignedTo = assignee.AutodeskId;
            }

            var page = await issuesService.SearchIssuesAsync(accountProjectId, new IssueSearchCriteria
            {
                Status = statusFilter,
                AssignedTo = assignedTo,
                Search = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                Limit = limit,
            }, accessToken);

            var issues = page.Results ?? [];
            if (issues.Count == 0)
            {
                return "No matching issue in this project.";
            }

            var people = await DescribePeopleAsync(accountProjectId, issues.Select(i => i.AssignedTo), accessToken);

            return ToolResults.Json(new
            {
                TotalResults = page.Pagination?.TotalResults ?? issues.Count,
                Count = issues.Count,
                Issues = issues.Select(i => new
                {
                    i.Id,
                    i.DisplayId,
                    i.Title,
                    i.Status,
                    Assignee = NameOf(i.AssignedTo, people),
                    i.DueDate
                })
            });
        }
        catch (Exception ex)
        {
            return Failure("search the issues", ex);
        }
    }

    [Description("Gets the full details of an issue in the current project, with its comments. Use it to review an issue. Call SearchIssues first to get the issue ID.")]
    public async Task<string> GetIssueAsync(
        [Description("ID of the issue, as returned by SearchIssues (not the display number).")] string issueId)
    {
        if (string.IsNullOrWhiteSpace(issueId))
        {
            return "Give the ID of the issue (see SearchIssues).";
        }

        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var issue = await issuesService.GetIssueAsync(accountProjectId, issueId.Trim(), accessToken);
            var comments = await issuesService.GetCommentsAsync(accountProjectId, issueId.Trim(), accessToken);

            var people = await DescribePeopleAsync(
                accountProjectId, comments.Select(c => c.CreatedBy).Append(issue.AssignedTo), accessToken);

            return ToolResults.Json(new
            {
                issue.Id,
                issue.DisplayId,
                issue.Title,
                issue.Description,
                issue.Status,
                Assignee = NameOf(issue.AssignedTo, people),
                issue.AssignedToType,
                issue.DueDate,
                issue.IssueTypeId,
                issue.IssueSubtypeId,
                issue.CreatedAt,
                issue.UpdatedAt,
                Comments = comments.Select(c => new
                {
                    c.Body,
                    CreatedBy = NameOf(c.CreatedBy, people),
                    c.CreatedAt
                })
            });
        }
        catch (Exception ex)
        {
            return Failure($"get the issue {issueId}", ex);
        }
    }

    [Description("Lists the active issue types of the current project and their subtypes, with names and IDs. Use it to find the issue subtype ID that CreateIssue needs.")]
    public async Task<string> ListIssueTypesAsync()
    {
        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var types = await issuesService.GetIssueTypesAsync(accountProjectId, accessToken);
            var active = types.Where(t => t.IsActive != false).ToList();
            if (active.Count == 0)
            {
                return "This project has no active issue types. Issue types are set up in the ACC project's Issues settings.";
            }

            return ToolResults.Json(active.Select(t => new
            {
                t.Id,
                t.Title,
                Subtypes = (t.Subtypes ?? []).Where(s => s.IsActive != false).Select(s => new { s.Id, s.Title })
            }));
        }
        catch (Exception ex)
        {
            return Failure("list the issue types", ex);
        }
    }

    [Description("Creates an issue in the current project. It needs a title and an issue subtype ID (see ListIssueTypes). The assignee is given by the email of a project user. Returns the new issue's ID, display ID, title and status.")]
    public async Task<string> CreateIssueAsync(
        [Description("Title of the issue, at most 100 characters.")] string title,
        [Description("ID of the issue subtype, as returned by ListIssueTypes.")] string issueSubtypeId,
        [Description("Description of the issue, at most 1000 characters. Leave empty for none.")] string? description = null,
        [Description("Email of the project user to assign the issue to. Leave empty to leave it unassigned.")] string? assigneeEmail = null,
        [Description("Due date in yyyy-MM-dd format, for example 2026-12-31. Leave empty for none.")] string? dueDate = null,
        [Description("Initial status: draft, open, pending, in_progress, completed, in_review, not_approved, in_dispute or closed. Defaults to open.")] string? status = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Give a title for the issue.";
        }

        if (string.IsNullOrWhiteSpace(issueSubtypeId))
        {
            return "Give the issue subtype ID (see ListIssueTypes).";
        }

        var error = ValidateFields(title, description, status, dueDate, out var normalizedStatus, out var normalizedDueDate);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var fields = new IssueFields
            {
                Title = title.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Status = normalizedStatus ?? "open",
                IssueSubtypeId = issueSubtypeId.Trim(),
                DueDate = normalizedDueDate,
            };

            if (!string.IsNullOrWhiteSpace(assigneeEmail))
            {
                var assignee = await ResolveAssigneeAsync(accountProjectId, assigneeEmail, accessToken);
                if (assignee.Error is not null)
                {
                    return assignee.Error;
                }

                fields = fields with { AssignedTo = assignee.AutodeskId, AssignedToType = "user" };
            }

            var issue = await issuesService.CreateIssueAsync(accountProjectId, fields, accessToken);

            return ToolResults.Json(new
            {
                Created = true,
                issue.Id,
                issue.DisplayId,
                issue.Title,
                issue.Status,
                Assignee = string.IsNullOrWhiteSpace(assigneeEmail) ? null : assigneeEmail.Trim(),
                issue.DueDate
            });
        }
        catch (Exception ex)
        {
            return Failure("create the issue", ex);
        }
    }

    [Description("Updates an issue in the current project. Only the supplied fields change. Use it to review an issue (for example set the status to in_review, not_approved or completed) or to reassign it. The ACC Issues API has no delete. To retire an issue, set its status to `closed` and tell the user that deleting it must be done in the ACC UI.")]
    public async Task<string> UpdateIssueAsync(
        [Description("ID of the issue, as returned by SearchIssues (not the display number).")] string issueId,
        [Description("New title, at most 100 characters. Leave empty to keep it.")] string? title = null,
        [Description("New description, at most 1000 characters. Leave empty to keep it.")] string? description = null,
        [Description("New status: draft, open, pending, in_progress, completed, in_review, not_approved, in_dispute or closed. Leave empty to keep it.")] string? status = null,
        [Description("Email of the project user to assign the issue to. Leave empty to keep the assignee.")] string? assigneeEmail = null,
        [Description("New due date in yyyy-MM-dd format. Leave empty to keep it.")] string? dueDate = null)
    {
        if (string.IsNullOrWhiteSpace(issueId))
        {
            return "Give the ID of the issue to update (see SearchIssues).";
        }

        if (new[] { title, description, status, assigneeEmail, dueDate }.All(string.IsNullOrWhiteSpace))
        {
            return "No change requested. Give a new title, description, status, assignee or due date.";
        }

        var error = ValidateFields(title, description, status, dueDate, out var normalizedStatus, out var normalizedDueDate);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var fields = new IssueFields
            {
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Status = normalizedStatus,
                DueDate = normalizedDueDate,
            };

            if (!string.IsNullOrWhiteSpace(assigneeEmail))
            {
                var assignee = await ResolveAssigneeAsync(accountProjectId, assigneeEmail, accessToken);
                if (assignee.Error is not null)
                {
                    return assignee.Error;
                }

                fields = fields with { AssignedTo = assignee.AutodeskId, AssignedToType = "user" };
            }

            var issue = await issuesService.UpdateIssueAsync(accountProjectId, issueId.Trim(), fields, accessToken);
            var people = await DescribePeopleAsync(accountProjectId, [issue.AssignedTo], accessToken);

            return ToolResults.Json(new
            {
                Updated = true,
                issue.Id,
                issue.DisplayId,
                issue.Title,
                issue.Status,
                Assignee = NameOf(issue.AssignedTo, people),
                issue.DueDate
            });
        }
        catch (Exception ex)
        {
            return Failure($"update the issue {issueId}", ex);
        }
    }

    [Description("Adds a comment to an issue in the current project. Call SearchIssues first to get the issue ID.")]
    public async Task<string> AddIssueCommentAsync(
        [Description("ID of the issue, as returned by SearchIssues (not the display number).")] string issueId,
        [Description("Text of the comment.")] string comment)
    {
        if (string.IsNullOrWhiteSpace(issueId))
        {
            return "Give the ID of the issue to comment on (see SearchIssues).";
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            return "Give the text of the comment.";
        }

        try
        {
            var accountProjectId = context.AccountProjectId;
            var accessToken = await context.GetAccessTokenAsync();

            var added = await issuesService.AddCommentAsync(accountProjectId, issueId.Trim(), comment.Trim(), accessToken);

            return ToolResults.Json(new
            {
                Added = true,
                IssueId = issueId.Trim(),
                CommentId = added.Id,
                added.Body,
                added.CreatedAt
            });
        }
        catch (Exception ex)
        {
            return Failure($"comment on the issue {issueId}", ex);
        }
    }

    // Checks the optional fields shared by create and update. Returns an error message, or null
    // with the status and due date in the API's form (null when not supplied).
    private static string? ValidateFields(
        string? title, string? description, string? status, string? dueDate,
        out string? normalizedStatus, out string? normalizedDueDate)
    {
        normalizedStatus = null;
        normalizedDueDate = null;

        if (title is not null && title.Trim().Length > MaxTitleLength)
        {
            return $"The title is too long ({title.Trim().Length} characters). Use at most {MaxTitleLength}.";
        }

        if (description is not null && description.Trim().Length > MaxDescriptionLength)
        {
            return $"The description is too long ({description.Trim().Length} characters). Use at most {MaxDescriptionLength}.";
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!IssuesService.TryNormalizeStatus(status, out var parsedStatus))
            {
                return UnknownStatus(status);
            }

            normalizedStatus = parsedStatus;
        }

        if (!string.IsNullOrWhiteSpace(dueDate))
        {
            if (!DateOnly.TryParseExact(dueDate.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return $"Invalid due date '{dueDate}'. Use the {DateFormat} format, for example 2026-12-31.";
            }

            normalizedDueDate = parsedDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        return null;
    }

    // Issues are assigned by Autodesk ID, which the Issues API doesn't list, so the user is looked
    // up among the project users by exact email.
    private async Task<(string? AutodeskId, string? Error)> ResolveAssigneeAsync(string accountProjectId, string email, string accessToken)
    {
        email = email.Trim();
        var users = await projectUserService.FindProjectUsersAsync(accountProjectId, null, email, accessToken);
        var user = users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return (null, $"{email} is not a user in this project. Issues can only be assigned to project users.");
        }

        return string.IsNullOrEmpty(user.AutodeskId)
            ? (null, $"{email} has no Autodesk ID yet (they may not have activated their account), so they can't be matched to issues.")
            : (user.AutodeskId, null);
    }

    // Best effort: maps the Autodesk IDs in issue results to "Name (email)" from the first page of
    // project users. IDs that aren't found (users past that page, companies or roles) stay as IDs.
    private async Task<Dictionary<string, string>> DescribePeopleAsync(string accountProjectId, IEnumerable<string?> autodeskIds, string accessToken)
    {
        var ids = autodeskIds.OfType<string>().Where(id => id.Length > 0).ToHashSet();
        if (ids.Count == 0)
        {
            return [];
        }

        try
        {
            var users = await projectUserService.FindProjectUsersAsync(accountProjectId, null, null, accessToken);
            return users
                .Where(u => !string.IsNullOrEmpty(u.AutodeskId) && ids.Contains(u.AutodeskId))
                .GroupBy(u => u.AutodeskId!)
                .ToDictionary(g => g.Key, g => $"{g.First().Name} ({g.First().Email})");
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? NameOf(string? autodeskId, Dictionary<string, string> people) =>
        autodeskId is not null && people.TryGetValue(autodeskId, out var name) ? name : autodeskId;

    private static string UnknownStatus(string status) =>
        $"Unknown status '{status}'. Valid statuses: {string.Join(", ", IssuesService.Statuses)}.";

    private static string Failure(string action, Exception ex) => ToolResults.StatusCodeOf(ex) switch
    {
        HttpStatusCode.Forbidden => ToolResults.Error(action, ex) + " Using Issues through the agent's service account needs a Forma Build subscription on the service account and access to Issues in this project.",
        _ => ToolResults.Error(action, ex),
    };
}
