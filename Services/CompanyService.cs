using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApsSamples.Models;
using Autodesk.Construction.AccountAdmin;

namespace ApsSamples.Services;

public class CompanyService(
    AdminClient adminClient,
    IHttpClientFactory httpClientFactory,
    ILogger<CompanyService> logger) : ICompanyService
{
    private const int PageLimit = 200;

    public async Task<IReadOnlyList<CompanyInfo>> GetProjectCompaniesAsync(string projectId, string accessToken)
    {
        try
        {
            // Account Admin project IDs drop the "b." hub prefix used by the Data Management API.
            var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectId[2..]
                : projectId;

            var project = await adminClient.GetProjectAsync(
                projectId: accountProjectId,
                accessToken: accessToken);

            var accountId = project?.AccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                logger.LogWarning("Project {ProjectId} has no AccountId; cannot fetch companies.", projectId);
                return Array.Empty<CompanyInfo>();
            }

            // The legacy hq/v1 "project companies" endpoint only works with 2-legged tokens.
            // Use the Account Admin API v1 "account companies" endpoint instead, which supports 3-legged auth.
            return await FetchAccountCompaniesAsync(accountId, accessToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching project companies for project {ProjectId}", projectId);
            return Array.Empty<CompanyInfo>();
        }
    }

    private async Task<IReadOnlyList<CompanyInfo>> FetchAccountCompaniesAsync(string accountId, string accessToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var companies = new List<CompanyInfo>();
        var offset = 0;

        while (true)
        {
            var url = $"https://developer.api.autodesk.com/construction/admin/v1/accounts/{accountId}/companies?limit={PageLimit}&offset={offset}";
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Account companies API error for account {AccountId}: {StatusCode} - {Content}", accountId, response.StatusCode, errorContent);
                break;
            }

            var content = await response.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<CompaniesPageDto>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var results = page?.Results ?? new List<CompanyDto>();
            if (results.Count == 0) break;

            companies.AddRange(results
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new CompanyInfo
                {
                    CompanyId = c.Id ?? string.Empty,
                    CompanyName = c.Name ?? string.Empty,
                }));

            offset += results.Count;
            var totalResults = page?.Pagination?.TotalResults ?? companies.Count;
            if (offset >= totalResults || results.Count < PageLimit) break;
        }

        return companies;
    }

    private class CompaniesPageDto
    {
        [JsonPropertyName("pagination")]
        public PaginationDto? Pagination { get; set; }

        [JsonPropertyName("results")]
        public List<CompanyDto>? Results { get; set; }
    }

    private class PaginationDto
    {
        [JsonPropertyName("totalResults")]
        public int? TotalResults { get; set; }
    }

    private class CompanyDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
