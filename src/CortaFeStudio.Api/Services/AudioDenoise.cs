namespace CortaFeStudio.Api.Services;

/// <summary>
/// Redução de ruído de voz com RNNoise (filtro arnndn do FFmpeg). Só entra se o modelo existir
/// (tools/models/rnnoise.rnnn ou CORTAFE_RNNOISE_MODEL) e nunca em louvor, onde afetaria o canto.
/// </summary>
public static class AudioDenoise
{
    public static string? ResolveModel(string root)
    {
        var configured = Environment.GetEnvironmentVariable("CORTAFE_RNNOISE_MODEL");
        var path = string.IsNullOrWhiteSpace(configured) ? Path.Combine(root, "tools", "models", "rnnoise.rnnn") : configured;
        return File.Exists(path) ? path : null;
    }

    /// <summary>Trecho de filtro (com vírgula no fim) ou texto vazio quando não se aplica.</summary>
    public static string Filter(string? modelPath, string contentType)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || contentType == "louvor") return "";
        var escaped = modelPath.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
        return $"arnndn=m='{escaped}':mix=0.85,";
    }
}
