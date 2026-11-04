using System.Text.Json.Serialization;

namespace ApsSamples.Models;

// A server-to-server APS app that owns the SSAs behind a project's assistants. One app holds at
// most 10 SSAs, so a project adds more apps to create more assistants.
public class SsaAppCredential
{
    public string ProjectId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Safe to render: the first and last 4 characters only.
    [JsonIgnore]
    public string MaskedClientId => ClientId.Length <= 8
        ? ClientId
        : $"{ClientId[..4]}…{ClientId[^4..]}";
}
