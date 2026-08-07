using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodesk.Construction.AccountAdmin;
using Autodesk.Construction.AccountAdmin.Model;
using Autodesk.SecureServiceAccount.Http;
using Autodesk.SecureServiceAccount.Model;
using Microsoft.IdentityModel.Tokens;

namespace ApsSamples.Services;

public class SsaService : ISsaService
{
    private const string TokenEndpoint = "https://developer.api.autodesk.com/authentication/v2/token";
    private const string JwtBearerGrant = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    private readonly IAPSAuthenticationService _auth;
    private readonly IConfiguration _configuration;
    private readonly AdminClient _adminClient;
    private readonly ILogger<SsaService> _logger;

    private readonly AccountManagementApi _accountApi;
    private readonly KeyManagementApi _keyApi;

    private readonly string _keysFilePath;
    private readonly SemaphoreSlim _keysFileLock = new(1, 1);
    private Dictionary<string, SsaKeyRecord> _keysCache = new();

    // Cache exchanged tokens per (ssaId, scopeSet) until 60s before expiry.
    private readonly Dictionary<string, (string Token, DateTime Expires)> _tokenCache = new();
    private readonly SemaphoreSlim _tokenCacheLock = new(1, 1);

    public SsaService(
        IAPSAuthenticationService auth,
        IConfiguration configuration,
        AdminClient adminClient,
        AccountManagementApi accountApi,
        KeyManagementApi keyApi,
        ILogger<SsaService> logger)
    {
        _auth = auth;
        _configuration = configuration;
        _adminClient = adminClient;
        _logger = logger;

        _accountApi = accountApi;
        _keyApi = keyApi;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _keysFilePath = Path.Combine(dataDirectory, "ssa-keys.json");

        LoadKeysFromFile();
    }

    public async Task<string> CreateSsaAsync(string name)
    {
        var token = await _auth.GetSsaAppTwoLeggedTokenAsync();
        var sanitizedName = SanitizeSsaName(name);
        var payload = new CreateServiceAccountPayload
        {
            Name = sanitizedName,
            FirstName = sanitizedName,
            LastName = sanitizedName,
        };

        var response = await _accountApi.CreateServiceAccountAsync(
            createServiceAccountPayload: payload,
            accessToken: token);

        var serviceAccount = response.Content
            ?? throw new InvalidOperationException("SSA create response returned no content");

        _logger.LogInformation("Created SSA {SsaId} ({Email})", serviceAccount.ServiceAccountId, serviceAccount.Email);
        return serviceAccount.ServiceAccountId!;
    }

    // The SSA API requires name/firstName/lastName to be 5-100 characters, containing only
    // alphanumeric characters and dashes, with at least one alphanumeric character.
    // See: https://aps.autodesk.com/en/docs/ssa/v1/developers_guide/best-practices/#naming-conventions
    private static string SanitizeSsaName(string name)
    {
        var slug = Regex.Replace(name ?? string.Empty, "[^A-Za-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "agent";
        }

        while (slug.Length < 5)
        {
            slug += "-agent";
        }

        if (slug.Length > 100)
        {
            slug = slug[..100].TrimEnd('-');
        }

        return slug;
    }

    public async Task<List<string>> GetAllSsaIdsAsync()
    {
        var token = await _auth.GetSsaAppTwoLeggedTokenAsync();

        var response = await _accountApi.GetAllServiceAccountsAsync(accessToken: token);

        return response.Content?.ServiceAccountsList?
            .Select(a => a.ServiceAccountId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList() ?? new List<string>();
    }

    public async Task<string> StoreSsaKeyAsync(string ssaId)
    {
        var token = await _auth.GetSsaAppTwoLeggedTokenAsync();

        var response = await _keyApi.CreateServiceAccountKeyAsync(
            serviceAccountId: ssaId,
            accessToken: token);

        var key = response.Content
            ?? throw new InvalidOperationException("SSA key create response returned no content");
        var kid = key.Kid ?? throw new InvalidOperationException("SSA key response missing kid");
        var pem = key.PrivateKey ?? throw new InvalidOperationException("SSA key response missing private key");

        await _keysFileLock.WaitAsync();
        try
        {
            _keysCache[ssaId] = new SsaKeyRecord { KeyId = kid, PrivateKeyPem = pem };
            await SaveKeysToFileNoLockAsync();
        }
        finally
        {
            _keysFileLock.Release();
        }

        _logger.LogInformation("Stored key {KeyId} for SSA {SsaId}", kid, ssaId);
        return kid;
    }

    public async Task<string> GetSsaEmailAsync(string ssaId)
    {
        var token = await _auth.GetSsaAppTwoLeggedTokenAsync();

        var response = await _accountApi.GetServiceAccountAsync(
            serviceAccountId: ssaId,
            accessToken: token);

        var details = response.Content
            ?? throw new InvalidOperationException($"SSA {ssaId} not found");

        return details.Email ?? throw new InvalidOperationException($"SSA {ssaId} response missing email");
    }

    public async Task<string> GetSsaTokenAsync(string ssaId, List<string> scopes)
    {
        var normalizedScopes = scopes.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var cacheKey = ssaId + "|" + string.Join(" ", normalizedScopes);

        await _tokenCacheLock.WaitAsync();
        try
        {
            if (_tokenCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.Expires)
            {
                return cached.Token;
            }
        }
        finally
        {
            _tokenCacheLock.Release();
        }

        SsaKeyRecord keyRecord;
        await _keysFileLock.WaitAsync();
        try
        {
            if (!_keysCache.TryGetValue(ssaId, out var kr))
            {
                throw new InvalidOperationException($"No stored key for SSA {ssaId}. Call StoreSsaKeyAsync first.");
            }
            keyRecord = kr;
        }
        finally
        {
            _keysFileLock.Release();
        }

        var ssaAppClientId = _configuration["SsaApp:ClientId"]
            ?? throw new InvalidOperationException("SsaApp:ClientId is required");
        var ssaAppClientSecret = _configuration["SsaApp:ClientSecret"]
            ?? throw new InvalidOperationException("SsaApp:ClientSecret is required");

        var assertion = BuildJwtAssertion(
            keyId: keyRecord.KeyId,
            privateKeyPem: keyRecord.PrivateKeyPem,
            issuerClientId: ssaAppClientId,
            subjectSsaId: ssaId,
            scopes: normalizedScopes);

        using var http = new HttpClient();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ssaAppClientId}:{ssaAppClientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = JwtBearerGrant,
                ["assertion"] = assertion,
                ["scope"] = string.Join(" ", normalizedScopes),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"SSA JWT exchange failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("SSA JWT exchange response missing access_token");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        var expires = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        await _tokenCacheLock.WaitAsync();
        try
        {
            _tokenCache[cacheKey] = (accessToken, expires);
        }
        finally
        {
            _tokenCacheLock.Release();
        }

        return accessToken;
    }

    public async Task<string> AddSsaToProjectAsync(string projectId, string ssaEmail, string? companyId, List<string> roleIds, List<string> productKeys, string accessToken)
    {
        // Assigning a project user must be done as the requesting user (3-legged), not the SSA's own
        // app-level 2-legged token, since the SSA has no membership/permissions on the project yet.

        // Account Admin project IDs drop the "b." hub prefix used by the Data Management API.
        var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? projectId[2..]
            : projectId;

        var payload = new ProjectUserPayload
        {
            Email = ssaEmail,
            CompanyId = string.IsNullOrEmpty(companyId) ? null : companyId,
            RoleIds = roleIds,
            Products = productKeys
                .Select(k => new ProjectUserPayloadProducts
                {
                    Key = Enum.Parse<ProductKeys>(k, ignoreCase: true),
                    // Project Administration grants admin access to that module; every other
                    // selected product grants standard member access.
                    Access = string.Equals(k, "projectAdministration", StringComparison.OrdinalIgnoreCase)
                        ? ProductAccess.Administrator
                        : ProductAccess.Member,
                })
                .ToList(),
        };

        var projectUser = await _adminClient.AssignProjectUserAsync(
            projectId: accountProjectId,
            projectUserPayload: payload,
            accessToken: accessToken);

        _logger.LogInformation("Added SSA {Email} to project {ProjectId}", ssaEmail, accountProjectId);
        return projectUser?.Id ?? string.Empty;
    }

    public async Task RemoveSsaFromProjectAsync(string projectId, string projectUserId, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(projectUserId))
        {
            _logger.LogWarning("No project user ID for SSA removal from project {ProjectId}; skipping.", projectId);
            return;
        }

        // Removing a project user must be done as the requesting user (3-legged), matching how the
        // SSA was assigned to the project.
        var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? projectId[2..]
            : projectId;

        await _adminClient.RemoveProjectUserAsync(
            projectId: accountProjectId,
            userId: projectUserId,
            accessToken: accessToken);

        _logger.LogInformation("Removed project user {ProjectUserId} from project {ProjectId}", projectUserId, accountProjectId);
    }

    public async Task DeleteSsaAsync(string ssaId)
    {
        var token = await _auth.GetSsaAppTwoLeggedTokenAsync();

        await _accountApi.DeleteServiceAccountAsync(
            serviceAccountId: ssaId,
            accessToken: token);

        await _keysFileLock.WaitAsync();
        try
        {
            if (_keysCache.Remove(ssaId))
            {
                await SaveKeysToFileNoLockAsync();
            }
        }
        finally
        {
            _keysFileLock.Release();
        }

        _logger.LogInformation("Deleted SSA {SsaId}", ssaId);
    }

    private static string BuildJwtAssertion(
        string keyId,
        string privateKeyPem,
        string issuerClientId,
        string subjectSsaId,
        List<string> scopes)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var key = new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = keyId };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuerClientId,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", subjectSsaId),
                new System.Security.Claims.Claim("aud", TokenEndpoint),
                new System.Security.Claims.Claim("scope", string.Join(" ", scopes)),
            }),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(300),
            SigningCredentials = creds,
        };

        // The JwtPayload emitted from ClaimsIdentity would nest aud/sub differently; set explicit
        // claims by building the token directly to keep the payload flat.
        var header = new JwtHeader(creds);
        var payload = new JwtPayload
        {
            ["iss"] = issuerClientId,
            ["sub"] = subjectSsaId,
            ["aud"] = TokenEndpoint,
            ["exp"] = new DateTimeOffset(now.AddSeconds(300)).ToUnixTimeSeconds(),
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["scope"] = scopes,
        };
        var jwt = new JwtSecurityToken(header, payload);
        return handler.WriteToken(jwt);
    }

    private async Task SaveKeysToFileNoLockAsync()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            var json = JsonSerializer.Serialize(_keysCache, options);
            await File.WriteAllTextAsync(_keysFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving SSA keys to file");
        }
    }

    private void LoadKeysFromFile()
    {
        try
        {
            if (!File.Exists(_keysFilePath))
            {
                _logger.LogInformation("No existing SSA keys file found at {Path}", _keysFilePath);
                return;
            }

            var json = File.ReadAllText(_keysFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            var loaded = JsonSerializer.Deserialize<Dictionary<string, SsaKeyRecord>>(json, options);
            if (loaded != null)
            {
                _keysCache = loaded;
                _logger.LogInformation("Loaded {Count} SSA keys from {Path}", loaded.Count, _keysFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading SSA keys from file");
        }
    }

    private sealed class SsaKeyRecord
    {
        public string KeyId { get; set; } = string.Empty;
        public string PrivateKeyPem { get; set; } = string.Empty;
    }
}
