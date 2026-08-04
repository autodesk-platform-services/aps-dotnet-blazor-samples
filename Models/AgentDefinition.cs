namespace ApsSamples.Models;

public class AgentDefinition
{
    public string AgentId { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string InstructionsText { get; set; } = string.Empty;
    public List<string> EnabledToolIds { get; set; } = new();
    public List<string> EnabledProductKeys { get; set; } = new();
    public string CompanyId { get; set; } = string.Empty;
    public string SsaId { get; set; } = string.Empty;
    public string SsaKeyId { get; set; } = string.Empty;
    public string SsaEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
