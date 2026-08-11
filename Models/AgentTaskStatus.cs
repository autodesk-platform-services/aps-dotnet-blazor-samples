using System.Text.Json.Serialization;

namespace ApsSamples.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
