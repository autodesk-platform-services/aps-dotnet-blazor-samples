using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ApsSamples.Services;

/// <summary>
/// Minimal AEC Data Model GraphQL client (raw HttpClient, like <see cref="AccLinkedFilesService"/>).
/// GraphQL <c>errors</c> and non-2xx responses are surfaced as <see cref="InvalidOperationException"/>.
/// </summary>
public class AecDataModelService(IHttpClientFactory httpClientFactory, ILogger<AecDataModelService> logger) : IAecDataModelService
{
    public const string GraphQlEndpoint = "https://developer.api.autodesk.com/aec/graphql";

    private const int PageLimit = 50;
    // Safety net against a server that keeps returning the same cursor.
    private const int MaxPages = 1000;

    private const string LinkCategoryFilter = "property.name.category=='RVT Links'";
    private const string CategoryProperty = "Revit Category Type Id";
    private const string FamilyNameProperty = "Family Name";
    private const string LinkCategory = "RVT Links";
    private const string LinkFamilyName = "Linked Revit Model";

    // The hubs → projects → elementGroupsByProject queries from issue #35, plus pagination.
    private const string HubsQuery = """
        query GetHubs($pagination: PaginationInput) {
          hubs(pagination: $pagination) {
            pagination {
              cursor
            }
            results {
              name
              id
            }
          }
        }
        """;

    private const string ProjectsQuery = """
        query GetProjects($hubId: ID!, $pagination: PaginationInput) {
          projects(hubId: $hubId, pagination: $pagination) {
            pagination {
              cursor
            }
            results {
              id
              name
              alternativeIdentifiers {
                dataManagementAPIProjectId
              }
            }
          }
        }
        """;

    private const string ElementGroupsQuery = """
        query GetElementGroupsByProject($projectId: ID!, $filter: ElementGroupFilterInput, $pagination: PaginationInput) {
          elementGroupsByProject(projectId: $projectId, filter: $filter, pagination: $pagination) {
            pagination {
              cursor
            }
            results {
              name
              id
              alternativeIdentifiers {
                fileUrn
                fileVersionUrn
              }
              version {
                versionNumber
              }
            }
          }
        }
        """;

    // Fallback when the fileUrn filter is rejected: page through every element group and match client-side.
    private const string AllElementGroupsQuery = """
        query GetElementGroupsByProject($projectId: ID!, $pagination: PaginationInput) {
          elementGroupsByProject(projectId: $projectId, pagination: $pagination) {
            pagination {
              cursor
            }
            results {
              name
              id
              alternativeIdentifiers {
                fileUrn
                fileVersionUrn
              }
              version {
                versionNumber
              }
            }
          }
        }
        """;

    private const string LinkElementsQuery = """
        query elementsByElementGroup($elementGroupId: ID!, $filter: ElementFilterInput, $pagination: PaginationInput) {
          elementsByElementGroup(elementGroupId: $elementGroupId, filter: $filter, pagination: $pagination) {
            pagination {
              cursor
              pageSize
            }
            results {
              id
            }
          }
        }
        """;

    // Fallback when the RSQL filter is rejected: page through every element and filter client-side.
    private const string AllElementsQuery = """
        query elementsByElementGroup($elementGroupId: ID!, $pagination: PaginationInput) {
          elementsByElementGroup(elementGroupId: $elementGroupId, pagination: $pagination) {
            pagination {
              cursor
              pageSize
            }
            results {
              id
              properties(filter: { names: ["Revit Category Type Id", "Family Name"] }) {
                results {
                  name
                  value
                }
              }
            }
          }
        }
        """;

    // The query from issue #25, used verbatim.
    private const string DiffQuery = """
        query diffElementGroupByVersionWithLatest($elementGroupId: ID!, $startVersion: Int, $changeFilter: [DifferenceType], $pagination: PaginationInput) {
          diffElementGroupByVersionWithLatest(
            elementGroupId: $elementGroupId
            startVersion: $startVersion
            changeFilter: $changeFilter
            pagination: $pagination
          ) {
            pagination {
              pageSize
              cursor
            }
            result {
              type
              element {
                id
                name
                properties {
                  results {
                    name
                    value
                    definition {
                      units {
                        name
                      }
                    }
                  }
                }
              }
              differences {
                pagination {
                  cursor
                  pageSize
                }
                results {
                  type
                  oldItem {
                    name
                    value
                  }
                  item {
                    name
                    value
                  }
                }
              }
            }
          }
        }
        """;

    public async Task<AecProject?> FindProjectAsync(string accessToken, string dataManagementProjectId, string? region, CancellationToken cancellationToken = default)
    {
        var wanted = StripProjectPrefix(dataManagementProjectId);

        await foreach (var hub in PageAsync(accessToken, HubsQuery, "hubs", "results",
            cursor => new { pagination = Pagination(cursor) }, region, cancellationToken))
        {
            if (GetString(hub, "id") is not { Length: > 0 } hubId)
                continue;

            await foreach (var project in PageAsync(accessToken, ProjectsQuery, "projects", "results",
                cursor => new { hubId, pagination = Pagination(cursor) }, region, cancellationToken))
            {
                var dmProjectId = TryGetObject(project, "alternativeIdentifiers", out var identifiers)
                    ? GetString(identifiers, "dataManagementAPIProjectId")
                    : null;
                if (dmProjectId is not null
                    && string.Equals(StripProjectPrefix(dmProjectId), wanted, StringComparison.OrdinalIgnoreCase)
                    && GetString(project, "id") is { Length: > 0 } id)
                    return new AecProject(id, GetString(project, "name") ?? "", hubId);
            }
        }

        return null;
    }

    public async Task<AecElementGroup?> FindElementGroupAsync(string accessToken, string aecProjectId, string lineageUrn, string? region, CancellationToken cancellationToken = default)
    {
        var wanted = StripVersionSuffix(lineageUrn);

        try
        {
            await foreach (var group in PageAsync(accessToken, ElementGroupsQuery, "elementGroupsByProject", "results",
                cursor => new
                {
                    projectId = aecProjectId,
                    filter = new { fileUrn = new[] { lineageUrn } },
                    pagination = Pagination(cursor)
                }, region, cancellationToken))
            {
                if (MatchElementGroup(group, wanted) is { } match)
                    return match;
            }
            return null;
        }
        catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AEC DM rejected the fileUrn filter for project {ProjectId}; matching element groups client-side", aecProjectId);
        }

        await foreach (var group in PageAsync(accessToken, AllElementGroupsQuery, "elementGroupsByProject", "results",
            cursor => new
            {
                projectId = aecProjectId,
                pagination = Pagination(cursor)
            }, region, cancellationToken))
        {
            if (MatchElementGroup(group, wanted) is { } match)
                return match;
        }
        return null;
    }

    public async Task<IReadOnlyList<string>> GetLinkElementIdsAsync(string accessToken, string elementGroupId, string? region, CancellationToken cancellationToken = default)
    {
        try
        {
            var ids = new List<string>();
            await foreach (var element in PageAsync(accessToken, LinkElementsQuery, "elementsByElementGroup", "results",
                cursor => new
                {
                    elementGroupId,
                    filter = new { query = LinkCategoryFilter },
                    pagination = Pagination(cursor)
                }, region, cancellationToken))
            {
                if (GetString(element, "id") is { Length: > 0 } id)
                    ids.Add(id);
            }
            return ids;
        }
        catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AEC DM rejected the RVT Links filter for element group {ElementGroupId}; filtering client-side", elementGroupId);
        }

        var fallbackIds = new List<string>();
        await foreach (var element in PageAsync(accessToken, AllElementsQuery, "elementsByElementGroup", "results",
            cursor => new
            {
                elementGroupId,
                pagination = Pagination(cursor)
            }, region, cancellationToken))
        {
            if (GetString(element, "id") is { Length: > 0 } id && IsLinkElement(element))
                fallbackIds.Add(id);
        }
        return fallbackIds;
    }

    public async Task<JsonElement> GetDiffAsync(string accessToken, string elementGroupId, int startVersion, string? region, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            await foreach (var item in PageAsync(accessToken, DiffQuery, "diffElementGroupByVersionWithLatest", "result",
                cursor => new
                {
                    elementGroupId,
                    startVersion,
                    changeFilter = new[] { "ADDITION", "REMOVAL" },
                    pagination = Pagination(cursor)
                }, region, cancellationToken))
            {
                item.WriteTo(writer);
            }
            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    // Follows pagination.cursor and yields every item of the connection's results array.
    private async IAsyncEnumerable<JsonElement> PageAsync(
        string accessToken,
        string query,
        string connectionField,
        string resultsField,
        Func<string?, object> variables,
        string? region,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 0; page < MaxPages; page++)
        {
            var data = await PostAsync(accessToken, query, variables(cursor), region, cancellationToken);
            if (!TryGetObject(data, connectionField, out var connection))
                yield break;

            if (connection.TryGetProperty(resultsField, out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                    yield return item;
            }

            cursor = TryGetObject(connection, "pagination", out var pagination) ? GetString(pagination, "cursor") : null;
            if (string.IsNullOrEmpty(cursor) || !seenCursors.Add(cursor))
                yield break;
        }

        logger.LogWarning("Stopped paging {Field} after {MaxPages} pages", connectionField, MaxPages);
    }

    // Posts a GraphQL request and returns a detached copy of the "data" element.
    private async Task<JsonElement> PostAsync(string accessToken, string query, object variables, string? region, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query, variables }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(region) && !string.Equals(region, "US", StringComparison.OrdinalIgnoreCase))
            request.Headers.Add("region", region);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("AEC DM GraphQL error: {StatusCode} - {Content}", response.StatusCode, content);
            throw new InvalidOperationException($"AEC Data Model request failed ({(int)response.StatusCode} {response.StatusCode}): {Truncate(content)}");
        }

        using var document = ParseOrThrow(content);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            var messages = errors.EnumerateArray()
                .Select(e => GetString(e, "message") ?? e.GetRawText())
                .ToList();
            logger.LogError("AEC DM GraphQL errors: {Errors}", string.Join(" | ", messages));
            throw new InvalidOperationException($"AEC Data Model returned errors: {string.Join("; ", messages)}");
        }

        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data.Clone()
            : default;
    }

    private static JsonDocument ParseOrThrow(string content)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"AEC Data Model returned invalid JSON: {Truncate(content)}", ex);
        }
    }

    private static object Pagination(string? cursor) =>
        cursor is null ? new { limit = PageLimit } : new { cursor, limit = PageLimit };

    // Data Management project ids may or may not carry the "b." prefix; compare them without it.
    private static string StripProjectPrefix(string projectId) =>
        projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? projectId[2..] : projectId;

    // "urn:...:dm.lineage:abc?version=3" → "urn:...:dm.lineage:abc".
    private static string StripVersionSuffix(string urn)
    {
        var query = urn.IndexOf('?');
        return query < 0 ? urn : urn[..query];
    }

    // Version number from the "version=N" query value of a file version URN.
    private static int? ParseVersionNumber(string? fileVersionUrn)
    {
        var query = fileVersionUrn?.IndexOf('?') ?? -1;
        if (query < 0)
            return null;

        foreach (var pair in fileVersionUrn![(query + 1)..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2
                && string.Equals(parts[0], "version", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[1], out var number))
                return number;
        }

        return null;
    }

    // The element group when its alternativeIdentifiers.fileUrn is the wanted lineage URN, else null.
    private static AecElementGroup? MatchElementGroup(JsonElement group, string lineageUrn)
    {
        if (!TryGetObject(group, "alternativeIdentifiers", out var identifiers)
            || GetString(identifiers, "fileUrn") is not { } fileUrn
            || !string.Equals(StripVersionSuffix(fileUrn), lineageUrn, StringComparison.OrdinalIgnoreCase)
            || GetString(group, "id") is not { Length: > 0 } id)
            return null;

        var fileVersionUrn = GetString(identifiers, "fileVersionUrn");
        int? versionNumber = TryGetObject(group, "version", out var version)
            && version.TryGetProperty("versionNumber", out var number)
            && number.ValueKind == JsonValueKind.Number
            && number.TryGetInt32(out var parsed)
                ? parsed
                : ParseVersionNumber(fileVersionUrn);

        return new AecElementGroup(id, GetString(group, "name"), fileUrn, fileVersionUrn, versionNumber);
    }

    private static bool IsLinkElement(JsonElement element)
    {
        if (!TryGetObject(element, "properties", out var properties)
            || !properties.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var property in results.EnumerateArray())
        {
            var name = GetString(property, "name");
            var value = GetString(property, "value");
            if ((string.Equals(name, CategoryProperty, StringComparison.OrdinalIgnoreCase) && string.Equals(value, LinkCategory, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(name, FamilyNameProperty, StringComparison.OrdinalIgnoreCase) && string.Equals(value, LinkFamilyName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static bool TryGetObject(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object)
            return true;

        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string propertyName) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "...";
}
