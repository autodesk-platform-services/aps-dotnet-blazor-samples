namespace ApsSamples.Models;

public class RevitLinkChange
{
    public string EventType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int InstanceCount { get; set; }
    // True when the link's Type element (the linked file itself) was added or removed,
    // false when only placed instances of an already-linked file changed.
    public bool TypeChanged { get; set; }
    public List<string> ElementIds { get; set; } = new();
}
