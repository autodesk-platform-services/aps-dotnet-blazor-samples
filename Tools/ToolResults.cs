using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApsSamples.Services;

namespace ApsSamples.Tools;

// Shapes what agent tools hand back to the model. When a tool throws, the model only sees a
// generic failure, so tools catch expected errors and return them as plain text instead; data is
// returned as compact JSON to keep the LLM context small.
public static class ToolResults
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    public static string Error(string action, Exception ex) => $"Could not {action}: {DescribeError(ex)}";

    // HTTP status of a failed API call, if the exception carries one. Lets tools add a hint for
    // e.g. a 403 (missing SSA permissions or subscription).
    public static HttpStatusCode? StatusCodeOf(Exception ex) => ex switch
    {
        IssuesApiException issues => issues.StatusCode,
        Autodesk.DataManagement.ServiceApiException dm => dm.HttpResponseMessage?.StatusCode,
        Autodesk.Construction.AccountAdmin.ServiceApiException admin => admin.HttpResponseMessage?.StatusCode,
        HttpRequestException http => http.StatusCode,
        _ => null,
    };

    private static string DescribeError(Exception ex)
    {
        var body = ex switch
        {
            IssuesApiException issues => issues.Body,
            Autodesk.DataManagement.ServiceApiException dm => ReadBody(dm.HttpResponseMessage),
            Autodesk.Construction.AccountAdmin.ServiceApiException admin => ReadBody(admin.HttpResponseMessage),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(body) ? ex.Message : body;
    }

    // SDK responses are buffered, so the body can be read synchronously. If it was already
    // disposed or consumed, fall back to the exception message.
    private static string? ReadBody(HttpResponseMessage? response)
    {
        if (response?.Content is null) return null;

        try
        {
            using var reader = new StreamReader(response.Content.ReadAsStream());
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
