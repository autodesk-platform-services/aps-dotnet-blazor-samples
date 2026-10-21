using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

// ACC Issues API v1 over plain HTTP, like CompanyService. The Issues SDK isn't used: its payload
// sends an unset status as null on PATCH. Here request bodies only hold the supplied fields.
public class IssuesService(IHttpClientFactory httpClientFactory) : IIssuesService
{
    private const string BaseUrl = "https://developer.api.autodesk.com/construction/issues/v1/projects/";
    private const int MaxSearchLimit = 100;
    private const int PageLimit = 100;

    // Statuses an issue can have, in the API's form.
    public static readonly IReadOnlyList<string> Statuses =
        ["draft", "open", "pending", "in_progress", "completed", "in_review", "not_approved", "in_dispute", "closed"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<IssuesPage<IssueDto>> SearchIssuesAsync(string accountProjectId, IssueSearchCriteria criteria, string accessToken) =>
        SendAsync<IssuesPage<IssueDto>>(HttpMethod.Get, accountProjectId, $"issues?{BuildSearchQuery(criteria)}", accessToken);

    public Task<IssueDto> GetIssueAsync(string accountProjectId, string issueId, string accessToken) =>
        SendAsync<IssueDto>(HttpMethod.Get, accountProjectId, $"issues/{Uri.EscapeDataString(issueId)}", accessToken);

    public Task<IssueDto> CreateIssueAsync(string accountProjectId, IssueFields fields, string accessToken) =>
        SendAsync<IssueDto>(HttpMethod.Post, accountProjectId, "issues", accessToken, BuildIssueBody(fields));

    public Task<IssueDto> UpdateIssueAsync(string accountProjectId, string issueId, IssueFields fields, string accessToken) =>
        SendAsync<IssueDto>(HttpMethod.Patch, accountProjectId, $"issues/{Uri.EscapeDataString(issueId)}", accessToken, BuildIssueBody(fields));

    public Task<IReadOnlyList<IssueCommentDto>> GetCommentsAsync(string accountProjectId, string issueId, string accessToken) =>
        GetAllPagesAsync<IssueCommentDto>(accountProjectId, $"issues/{Uri.EscapeDataString(issueId)}/comments", accessToken);

    public Task<IssueCommentDto> AddCommentAsync(string accountProjectId, string issueId, string body, string accessToken) =>
        SendAsync<IssueCommentDto>(HttpMethod.Post, accountProjectId, $"issues/{Uri.EscapeDataString(issueId)}/comments", accessToken,
            new JsonObject { ["body"] = body });

    public Task<IReadOnlyList<IssueTypeDto>> GetIssueTypesAsync(string accountProjectId, string accessToken) =>
        GetAllPagesAsync<IssueTypeDto>(accountProjectId, "issue-types?include=subtypes", accessToken);

    // Query string for GET issues (without the leading "?"). Empty filters are left out, values are
    // URL-encoded and the limit is clamped to what the API accepts (1-100).
    public static string BuildSearchQuery(IssueSearchCriteria criteria)
    {
        var parts = new List<string>();

        void AddFilter(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"filter[{name}]={Uri.EscapeDataString(value.Trim())}");
            }
        }

        AddFilter("status", criteria.Status);
        AddFilter("assignedTo", criteria.AssignedTo);
        AddFilter("issueTypeId", criteria.IssueTypeId);
        AddFilter("issueSubtypeId", criteria.IssueSubtypeId);
        AddFilter("search", criteria.Search);

        parts.Add($"limit={Math.Clamp(criteria.Limit, 1, MaxSearchLimit)}");
        if (criteria.Offset > 0)
        {
            parts.Add($"offset={criteria.Offset}");
        }

        return string.Join("&", parts);
    }

    // Request body for POST and PATCH issues, holding only the supplied fields (never a null value).
    public static JsonObject BuildIssueBody(IssueFields fields)
    {
        var body = new JsonObject();

        void Add(string name, string? value)
        {
            if (value is not null)
            {
                body[name] = value;
            }
        }

        Add("title", fields.Title);
        Add("description", fields.Description);
        Add("status", fields.Status);
        Add("issueSubtypeId", fields.IssueSubtypeId);
        Add("assignedTo", fields.AssignedTo);
        Add("assignedToType", fields.AssignedToType);
        Add("dueDate", fields.DueDate);

        return body;
    }

    // Matches a status ignoring case, and accepts spaces or hyphens for underscores ("In progress").
    public static bool TryNormalizeStatus(string? value, out string status)
    {
        var candidate = value?.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        status = Statuses.FirstOrDefault(s => s == candidate) ?? string.Empty;
        return status.Length > 0;
    }

    private async Task<IReadOnlyList<T>> GetAllPagesAsync<T>(string accountProjectId, string path, string accessToken)
    {
        var items = new List<T>();
        var separator = path.Contains('?') ? '&' : '?';

        while (true)
        {
            var page = await SendAsync<IssuesPage<T>>(
                HttpMethod.Get, accountProjectId, $"{path}{separator}limit={PageLimit}&offset={items.Count}", accessToken);

            var results = page.Results ?? [];
            items.AddRange(results);

            var totalResults = page.Pagination?.TotalResults ?? items.Count;
            if (results.Count == 0 || results.Count < PageLimit || items.Count >= totalResults) break;
        }

        return items;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string accountProjectId, string path, string accessToken, JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, $"{BaseUrl}{Uri.EscapeDataString(accountProjectId)}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        var httpClient = httpClientFactory.CreateClient();
        using var response = await httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new IssuesApiException(response.StatusCode, content);
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new IssuesApiException(response.StatusCode, "The Issues API returned an empty response.");
    }
}

public class IssuesPage<T>
{
    [JsonPropertyName("pagination")]
    public IssuesPagination? Pagination { get; set; }

    [JsonPropertyName("results")]
    public List<T>? Results { get; set; }
}

public class IssuesPagination
{
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("totalResults")]
    public int? TotalResults { get; set; }
}

public class IssueDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayId")]
    public int? DisplayId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("assignedToType")]
    public string? AssignedToType { get; set; }

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("issueTypeId")]
    public string? IssueTypeId { get; set; }

    [JsonPropertyName("issueSubtypeId")]
    public string? IssueSubtypeId { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

public class IssueCommentDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
}

public class IssueTypeDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; set; }

    [JsonPropertyName("subtypes")]
    public List<IssueSubtypeDto>? Subtypes { get; set; }
}

public class IssueSubtypeDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("isActive")]
    public bool? IsActive { get; set; }
}
