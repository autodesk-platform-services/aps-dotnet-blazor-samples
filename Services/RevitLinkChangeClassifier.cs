using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

/// <summary>
/// Turns the <c>result</c> array of the AEC Data Model <c>diffElementGroupByVersionWithLatest</c>
/// query into Revit-link changes. Pure (no I/O) so it can be unit tested against captured responses.
/// </summary>
public static class RevitLinkChangeClassifier
{
    private const string CategoryProperty = "Revit Category Type Id";
    private const string FamilyNameProperty = "Family Name";
    private const string ElementContextProperty = "Element Context";
    private const string ElementNameProperty = "Element Name";
    private const string LinkCategory = "RVT Links";
    private const string LinkFamilyName = "Linked Revit Model";
    private const string InstanceNameSeparator = " : ";

    public static IReadOnlyList<RevitLinkChange> Classify(JsonElement diffResults, IReadOnlySet<string> knownLinkElementIds)
    {
        if (diffResults.ValueKind != JsonValueKind.Array)
            return [];

        // Keyed by (event type, file name) so a link's Type and Instance elements collapse into one change.
        var changes = new Dictionary<(string EventType, string FileName), RevitLinkChange>(new ChangeKeyComparer());
        var order = new List<RevitLinkChange>();

        foreach (var item in diffResults.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var eventType = GetString(item, "type") switch
            {
                "ADDITION" => RevitWebhookEventTypes.LinkAdded,
                "REMOVAL" => RevitWebhookEventTypes.LinkRemoved,
                _ => null // MODIFICATION (revit.link.modified) is out of scope
            };
            if (eventType is null)
                continue;

            if (!item.TryGetProperty("element", out var element) || element.ValueKind != JsonValueKind.Object)
                continue;

            var elementId = GetString(element, "id");
            var elementName = GetString(element, "name");
            var properties = ReadProperties(element);

            var isLink = IsLinkElement(properties)
                // Removed elements can come back without their properties, so fall back to the
                // link ids snapshotted from earlier versions.
                || (eventType == RevitWebhookEventTypes.LinkRemoved
                    && elementId is not null
                    && knownLinkElementIds.Contains(elementId));
            if (!isLink)
                continue;

            var name = !string.IsNullOrWhiteSpace(elementName) ? elementName : properties.GetValueOrDefault(ElementNameProperty);
            var isInstance = IsInstance(properties, name);
            var fileName = GetFileName(name, isInstance);

            var key = (eventType, fileName);
            if (!changes.TryGetValue(key, out var change))
            {
                change = new RevitLinkChange { EventType = eventType, FileName = fileName };
                changes[key] = change;
                order.Add(change);
            }

            if (isInstance)
                change.InstanceCount++;
            else
                change.TypeChanged = true;

            if (!string.IsNullOrEmpty(elementId))
                change.ElementIds.Add(elementId);
        }

        return order;
    }

    private static bool IsLinkElement(IReadOnlyDictionary<string, string?> properties) =>
        string.Equals(properties.GetValueOrDefault(CategoryProperty), LinkCategory, StringComparison.OrdinalIgnoreCase)
        || string.Equals(properties.GetValueOrDefault(FamilyNameProperty), LinkFamilyName, StringComparison.OrdinalIgnoreCase);

    private static bool IsInstance(IReadOnlyDictionary<string, string?> properties, string? name)
    {
        var context = properties.GetValueOrDefault(ElementContextProperty);
        if (!string.IsNullOrEmpty(context))
            return string.Equals(context, "Instance", StringComparison.OrdinalIgnoreCase);

        // Without properties, instance names look like "MEP.rvt : 1 : location <Not Shared>".
        return name?.Contains(InstanceNameSeparator, StringComparison.Ordinal) == true;
    }

    private static string GetFileName(string? name, bool isInstance)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "(unknown)";

        if (isInstance)
        {
            var separatorIndex = name.IndexOf(InstanceNameSeparator, StringComparison.Ordinal);
            if (separatorIndex > 0)
                return name[..separatorIndex].Trim();
        }

        return name.Trim();
    }

    private static Dictionary<string, string?> ReadProperties(JsonElement element)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!element.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var property in results.EnumerateArray())
        {
            var name = GetString(property, "name");
            if (string.IsNullOrEmpty(name) || result.ContainsKey(name))
                continue;

            result[name] = property.TryGetProperty("value", out var value) ? ValueToString(value) : null;
        }

        return result;
    }

    private static string? GetString(JsonElement obj, string propertyName) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propertyName, out var value)
            ? ValueToString(value)
            : null;

    // AEC DM property values are mixed JSON types (string, number, bool, null).
    private static string? ValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private sealed class ChangeKeyComparer : IEqualityComparer<(string EventType, string FileName)>
    {
        public bool Equals((string EventType, string FileName) x, (string EventType, string FileName) y) =>
            x.EventType == y.EventType && string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string EventType, string FileName) obj) =>
            HashCode.Combine(obj.EventType, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FileName));
    }
}
