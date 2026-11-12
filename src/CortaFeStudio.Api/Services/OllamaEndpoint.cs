namespace CortaFeStudio.Api.Services;

/// <summary>Endereço do Ollama. Em servidor/Docker, defina OLLAMA_URL (ex.: http://ollama:11434).</summary>
public static class OllamaEndpoint
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("OLLAMA_URL") is { Length: > 0 } url ? url : "http://localhost:11434").TrimEnd('/');
    public static string Generate => BaseUrl + "/api/generate";
    public static string Tags => BaseUrl + "/api/tags";
}
