namespace ApsSamples.Models;

public class AgentOptions
{
    public string ModelName { get; set; } = "llama3";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    // Maximum tool-calling round trips the model can make while answering one request.
    public int MaxToolIterations { get; set; } = 10;
}
