using System.Net;

namespace ApsSamples.Services;

// Thrown by the raw-HTTP Issues API client on a non-success response. Carries the response body,
// which holds the API's actual error detail.
public class IssuesApiException(HttpStatusCode statusCode, string body)
    : Exception($"Issues API request failed ({(int)statusCode}): {body}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}
