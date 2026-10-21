namespace ApsSamples.Tools;

// Validates folder names and splits folder paths for the folder tools, following the ACC Docs
// naming rules, so bad input is rejected before any API call is made.
public static class FolderPathValidator
{
    public const int MaxNameLength = 255;
    public const int MaxPathsPerCall = 200;
    public const int MaxDepth = 25;

    private static readonly char[] ReservedCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*', '\'', '¢', '™', '$', '®'];

    // Returns why the name is invalid, or null if it is valid. The name is checked trimmed.
    public static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "The folder name is empty.";
        }

        if (trimmed.Length > MaxNameLength)
        {
            return $"The folder name '{trimmed}' is longer than {MaxNameLength} characters.";
        }

        if (trimmed.IndexOfAny(ReservedCharacters) >= 0 || trimmed.Any(char.IsControl))
        {
            return $"The folder name '{trimmed}' contains a character that isn't allowed " +
                "(< > : \" / \\ | ? * ' ¢ ™ $ ® or a control character).";
        }

        return null;
    }

    // Returns why the number of paths in one call is invalid, or null if it is valid.
    public static string? ValidatePathCount(int count) => count switch
    {
        0 => "No folder paths were given.",
        > MaxPathsPerCall => $"Too many folder paths ({count}). At most {MaxPathsPerCall} can be created per call.",
        _ => null,
    };

    // Splits a "/"-separated path into trimmed segments, ignoring empty ones from leading,
    // trailing or double slashes.
    public static List<string> SplitPath(string? path) =>
        (path ?? string.Empty)
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    // Splits and validates a path. Returns false with the reason if the path is empty, too deep
    // or has an invalid segment.
    public static bool TryParsePath(string? path, out List<string> segments, out string? error)
    {
        segments = SplitPath(path);

        if (segments.Count == 0)
        {
            error = "The folder path is empty.";
            return false;
        }

        if (segments.Count > MaxDepth)
        {
            error = $"The folder path '{path}' is deeper than {MaxDepth} levels.";
            return false;
        }

        error = segments.Select(ValidateName).FirstOrDefault(e => e is not null);
        return error is null;
    }
}
