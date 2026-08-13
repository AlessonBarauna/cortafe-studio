using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class SocialPublishingPolicy
{
    public static void Validate(SocialPlatform platform, string path, double duration, PublishRequest request)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidOperationException("O arquivo renderizado está vazio ou não existe.");
        if (duration is <= 0 or > 180) throw new InvalidOperationException("O corte precisa ter entre 1 segundo e 3 minutos para publicação automática.");
        if (string.IsNullOrWhiteSpace(request.Title)) throw new InvalidOperationException("Informe um título para publicar.");
        if (platform == SocialPlatform.YouTube && request.Title.Length > 100) throw new InvalidOperationException("O título do YouTube pode ter no máximo 100 caracteres.");
        var allowed = platform switch
        {
            SocialPlatform.YouTube => new[] { "private", "unlisted", "public" },
            SocialPlatform.TikTok => new[] { "private", "public" },
            _ => new[] { "public" }
        };
        if (!allowed.Contains(request.Privacy, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("A visibilidade escolhida não é aceita pela plataforma.");
    }

    public static TimeSpan RetryDelay(int attempt) => attempt switch { <= 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), _ => TimeSpan.FromMinutes(20) };
}
