namespace ApsSamples.Services;

// ACC Issues API v1 calls. Project IDs are in account form, without the Data Management "b."
// prefix. Non-success responses throw IssuesApiException. The API has no delete endpoint.
public interface IIssuesService
{
    Task<IssuesPage<IssueDto>> SearchIssuesAsync(string accountProjectId, IssueSearchCriteria criteria, string accessToken);

    Task<IssueDto> GetIssueAsync(string accountProjectId, string issueId, string accessToken);

    Task<IssueDto> CreateIssueAsync(string accountProjectId, IssueFields fields, string accessToken);

    Task<IssueDto> UpdateIssueAsync(string accountProjectId, string issueId, IssueFields fields, string accessToken);

    Task<IReadOnlyList<IssueCommentDto>> GetCommentsAsync(string accountProjectId, string issueId, string accessToken);

    Task<IssueCommentDto> AddCommentAsync(string accountProjectId, string issueId, string body, string accessToken);

    Task<IReadOnlyList<IssueTypeDto>> GetIssueTypesAsync(string accountProjectId, string accessToken);
}

// Filters for an issue search. Null or empty values are left out of the query. Status, AssignedTo,
// IssueTypeId and IssueSubtypeId accept a comma-separated list.
public record IssueSearchCriteria
{
    public string? Status { get; init; }
    public string? AssignedTo { get; init; }
    public string? IssueTypeId { get; init; }
    public string? IssueSubtypeId { get; init; }
    public string? Search { get; init; }
    public int Limit { get; init; } = 25;
    public int Offset { get; init; }
}

// Issue fields to send on create or update. Only the non-null ones go into the request body, so an
// update never clears a field by accident.
public record IssueFields
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }
    public string? IssueSubtypeId { get; init; }
    public string? AssignedTo { get; init; }
    public string? AssignedToType { get; init; }
    public string? DueDate { get; init; }
}
