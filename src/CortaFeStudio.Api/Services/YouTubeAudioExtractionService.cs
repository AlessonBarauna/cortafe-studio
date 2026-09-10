using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class YouTubeAudioExtractionService(ProjectStore store, ToolService tools)
{
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase) { "mp3", "m4a", "wav" };
    private static readonly HashSet<string> Qualities = new(StringComparer.OrdinalIgnoreCase) { "high", "medium", "compact" };

    public async Task ProcessAsync(VideoProject project, CancellationToken ct = default)
    {
        if (project.SourceKind != SourceKind.YouTube || !IsYouTubeUrl(project.Source))
            throw new InvalidOperationException("A extração direta de áudio aceita somente links do YouTube.");

        var format = NormalizeFormat(project.Options.AudioFormat);
        var quality = NormalizeQuality(project.Options.AudioQuality);
        var outputDirectory = ResolveOutputDirectory(project.Options.AudioOutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        project.Status = ProjectStatus.Acquiring;
        project.Progress = 8;
        project.Stage = "Preparando extração de áudio";
        await store.SaveAsync(project);

        var common = YouTubeAcquisition.WithBrowserSession(tools.YouTubeArguments(), project.YouTubeCookieBrowser);
        var args = BuildArguments(common, tools.Find("ffmpeg"), outputDirectory, project.Source, format, quality,
            project.Options.AudioDownloadPlaylist,
            project.Options.AudioOrganizeByChannel,
            project.Options.AudioOrganizeByPlaylist);

        project.Progress = 24;
        project.Stage = project.Options.AudioDownloadPlaylist ? "Baixando e convertendo playlist" : "Baixando e convertendo áudio";
        await store.SaveAsync(project);

        var output = await tools.CaptureAsync(tools.Find("yt-dlp"), args, outputDirectory, ct);
        var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            throw new InvalidOperationException("O áudio foi processado, mas o arquivo final não pôde ser localizado.");

        project.AudioOutputFiles = paths;
        project.LocalMedia = null;
        project.Clips = [];
        project.Transcript = [];
        project.TranscriptSource = null;
        project.CandidateAnalysis = null;
        project.CompletedStages = ["audio-export"];
        project.LastCheckpoint = "audio-export";
        project.Status = ProjectStatus.Ready;
        project.Progress = 100;
        project.CompletedAt = DateTime.UtcNow;
        project.Stage = paths.Count == 1 ? "Áudio salvo na pasta local" : $"{paths.Count} áudios salvos na pasta local";

        if (project.Name == "Vídeo do YouTube")
            project.Name = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : $"Áudios do YouTube · {paths.Count} arquivos";

        await store.SaveAsync(project);
    }

    public static List<string> BuildArguments(
        IEnumerable<string> common,
        string ffmpeg,
        string outputDirectory,
        string url,
        string format,
        string quality,
        bool playlist,
        bool organizeByChannel,
        bool organizeByPlaylist)
    {
        if (!IsYouTubeUrl(url)) throw new ArgumentException("Informe um link válido do YouTube.", nameof(url));
        format = NormalizeFormat(format);
        quality = NormalizeQuality(quality);

        var args = common.ToList();
        args.AddRange(["--retries", "15", "--fragment-retries", "15", "--extractor-retries", "5", "--retry-sleep", "linear=1::3"]);
        args.Add(playlist ? "--yes-playlist" : "--no-playlist");
        if (playlist) args.AddRange(["--playlist-end", "200"]);
        args.AddRange(["--ffmpeg-location", ffmpeg, "-x", "--audio-format", format]);

        if (format == "mp3")
            args.AddRange(["--audio-quality", quality switch { "medium" => "5", "compact" => "7", _ => "0" }]);
        else if (format == "m4a")
            args.AddRange(["--audio-quality", quality switch { "medium" => "160K", "compact" => "128K", _ => "256K" }]);

        args.AddRange(["--embed-metadata", "--no-write-playlist-metafiles"]);

        var parts = new List<string>();
        if (organizeByChannel) parts.Add("%(uploader,channel|Canal)s");
        if (playlist && organizeByPlaylist) parts.Add("%(playlist_title|Playlist)s");
        var filename = playlist ? "%(playlist_index|0)03d - %(title)s.%(ext)s" : "%(title)s.%(ext)s";
        var relativeTemplate = parts.Count == 0 ? filename : Path.Combine([.. parts, filename]);
        var template = Path.Combine(outputDirectory, relativeTemplate);

        args.AddRange(["-o", template, "--print", "after_move:filepath", url]);
        return args;
    }

    public static string ResolveOutputDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            return Path.GetFullPath(expanded);
        }

        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(music)) music = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(music, "AmadoJesus", "YouTube");
    }

    public static bool IsYouTubeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var host = uri.Host.TrimStart('.').ToLowerInvariant();
        return host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be" || host.EndsWith(".youtube.com", StringComparison.Ordinal);
    }

    public static string NormalizeFormat(string? value) => Formats.Contains(value ?? "") ? value!.ToLowerInvariant() : "mp3";
    public static string NormalizeQuality(string? value) => Qualities.Contains(value ?? "") ? value!.ToLowerInvariant() : "high";
}
