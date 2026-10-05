namespace ApsSamples.Tests;

public sealed class TempWorkingDirectoryFixture : IDisposable
{
    private readonly string _previousCwd;

    public TempWorkingDirectoryFixture()
    {
        _previousCwd = Directory.GetCurrentDirectory();
        TempPath = Path.Combine(Path.GetTempPath(), "ApsSamples.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempPath);
        Directory.SetCurrentDirectory(TempPath);
    }

    public string TempPath { get; }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousCwd);
        try { Directory.Delete(TempPath, recursive: true); } catch { }
    }
}
