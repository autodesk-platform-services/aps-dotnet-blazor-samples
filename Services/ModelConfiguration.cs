namespace ApsSamples.Services;

/// <summary>
/// Fluent builder for model configuration with enforced ordering
/// </summary>
public class ModelConfiguration
{
    public string Region { get; private set; } = string.Empty;
    public string ProjectGuid { get; private set; } = string.Empty;
    public string ModelGuid { get; private set; } = string.Empty;
    public string ToolName { get; private set; } = string.Empty;
    public bool SaveAfter { get; private set; } = true;

    private ModelConfiguration() { }

    public static IWithRegion Create() => new Builder();

    private class Builder : IWithRegion, IWithProject, IWithModel, IWithTool, IWithSave, IConfigured
    {
        private readonly ModelConfiguration _config = new();

        public IWithProject WithRegion(string region)
        {
            _config.Region = region;
            return this;
        }

        public IWithModel WithProject(string projectGuid)
        {
            _config.ProjectGuid = projectGuid;
            return this;
        }

        public IWithTool WithModelGuid(string modelGuid)
        {
            _config.ModelGuid = modelGuid;
            return this;
        }

        public IWithSave WithToolName(string toolName)
        {
            _config.ToolName = toolName;
            return this;
        }

        public IConfigured Save(bool save = true)
        {
            _config.SaveAfter = save;
            return this;
        }

        public ModelConfiguration Configure()
        {
            return _config;
        }
    }
}

// Fluent interfaces to enforce ordering
public interface IWithRegion
{
    IWithProject WithRegion(string region);
}

public interface IWithProject
{
    IWithModel WithProject(string projectGuid);
}

public interface IWithModel
{
    IWithTool WithModelGuid(string modelGuid);
}

public interface IWithTool
{
    IWithSave WithToolName(string toolName);
}

public interface IWithSave
{
    IConfigured Save(bool save = true);
}

public interface IConfigured
{
    ModelConfiguration Configure();
}
