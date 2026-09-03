using System.IO.Compression;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ClipExportService(ProjectStore store)
{
    public async Task<string> CreateZipAsync(VideoProject project, CancellationToken ct = default)
    {
        var directory = store.ProjectDirectory(project.Id);
        var rendered = project.Clips.Where(clip => !string.IsNullOrWhiteSpace(clip.VideoPath))
            .Select(clip => (Clip: clip, Path: Path.Combine(directory, clip.VideoPath!))).Where(item => File.Exists(item.Path)).ToList();
        if (rendered.Count == 0) throw new InvalidOperationException("Renderize pelo menos um corte antes de baixar o pacote.");
        var target = Path.Combine(directory, "cortes-prontos.zip");
        var temporary = target + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        await using (var stream = File.Create(temporary))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (var index = 0; index < rendered.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var safeTitle = string.Concat(rendered[index].Clip.Title.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
                var entry = archive.CreateEntry($"{index + 1:00}-{safeTitle[..Math.Min(safeTitle.Length, 70)]}.mp4", CompressionLevel.NoCompression);
                await using var input = File.OpenRead(rendered[index].Path); await using var output = entry.Open(); await input.CopyToAsync(output, ct);
            }
        }
        File.Move(temporary, target, true);
        return target;
    }

    public async Task<string> CreateTikTokStudioPackageAsync(VideoProject project, CancellationToken ct = default)
    {
        var directory = store.ProjectDirectory(project.Id);
        var rendered = project.Clips.Where(clip => clip.Approved && clip.TikTokWorkflowStatus != "published" && !string.IsNullOrWhiteSpace(clip.VideoPath))
            .Select(clip => (Clip: clip, Path: Path.Combine(directory, clip.VideoPath!)))
            .Where(item => File.Exists(item.Path)).OrderByDescending(item => item.Clip.Score).ToList();
        if (rendered.Count == 0) throw new InvalidOperationException("Renderize pelo menos um corte aprovado antes de criar o pacote TikTok Studio.");
        var target = Path.Combine(directory, "pacote-tiktok-studio.zip"); var temporary = target + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        await using (var stream = File.Create(temporary))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var csv = new StringBuilder("ordem;arquivo;capa;titulo;descricao;hashtags;tema;status;data_sugerida;duracao_segundos\r\n");
            var editorialManifest = new List<object>();
            var start = DateTimeOffset.Now.Date.AddDays(1); var times = new[] { new TimeSpan(10, 0, 0), new TimeSpan(19, 0, 0) };
            for (var index = 0; index < rendered.Count; index++)
            {
                ct.ThrowIfCancellationRequested(); var clip = rendered[index].Clip;
                var safeTitle = SafeFileName(clip.Title); var fileName = $"{index + 1:00}-{safeTitle}.mp4";
                var videoEntry = archive.CreateEntry($"videos/{fileName}", CompressionLevel.NoCompression);
                await using (var input = File.OpenRead(rendered[index].Path)) await using (var output = videoEntry.Open()) await input.CopyToAsync(output, ct);
                var coverFileName = "";
                if (!string.IsNullOrWhiteSpace(clip.CoverPath))
                {
                    var coverPath = Path.Combine(directory, clip.CoverPath);
                    if (File.Exists(coverPath))
                    {
                        coverFileName = $"{index + 1:00}-{safeTitle}{Path.GetExtension(coverPath)}";
                        var coverEntry = archive.CreateEntry($"capas/{coverFileName}", CompressionLevel.Optimal);
                        await using var coverInput = File.OpenRead(coverPath); await using var coverOutput = coverEntry.Open(); await coverInput.CopyToAsync(coverOutput, ct);
                    }
                }
                var hashtags = string.Join(' ', clip.Hashtags); var postText = $"{clip.Title}\n\n{clip.Caption}\n\n{hashtags}".Trim();
                var textEntry = archive.CreateEntry($"legendas/{index + 1:00}-{safeTitle}.txt", CompressionLevel.Optimal);
                await using (var writer = new StreamWriter(textEntry.Open(), new UTF8Encoding(false))) await writer.WriteAsync(postText.AsMemory(), ct);
                var suggested = new DateTimeOffset(start.AddDays(index / 2).Add(times[index % 2]), TimeZoneInfo.Local.GetUtcOffset(start));
                var finalDuration = Math.Round((clip.End - clip.Start) / RenderFilterFactory.NormalizePlaybackSpeed(clip.PlaybackSpeed), 1);
                csv.AppendLine(string.Join(';', index + 1, Csv(fileName), Csv(coverFileName), Csv(clip.Title), Csv(clip.Caption), Csv(hashtags), Csv(clip.DiversityTopic), Csv(clip.TikTokWorkflowStatus), Csv(suggested.ToString("yyyy-MM-dd HH:mm")), finalDuration));
                editorialManifest.Add(new { order = index + 1, video = $"videos/{fileName}", cover = string.IsNullOrWhiteSpace(coverFileName) ? null : $"capas/{coverFileName}", clip.Title, clip.Caption, clip.Hashtags, scheduledAt = suggested, durationSeconds = finalDuration, clip.Score, hookScore = clip.SocialScore.Hook, subtitleReviewRequired = clip.SubtitleTrack?.RequiresReview ?? false });
            }
            var manifest = archive.CreateEntry("programacao-tiktok-studio.csv", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(true))) await writer.WriteAsync(csv.ToString().AsMemory(), ct);
            var jsonManifest = archive.CreateEntry("manifesto-editorial.json", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(jsonManifest.Open(), new UTF8Encoding(false)))
                await writer.WriteAsync(System.Text.Json.JsonSerializer.Serialize(editorialManifest, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }).AsMemory(), ct);
            var guide = archive.CreateEntry("LEIA-ME.txt", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(guide.Open(), new UTF8Encoding(false)))
                await writer.WriteAsync("PACOTE TIKTOK STUDIO\n\n1. Abra a pasta videos.\n2. Escolha a imagem correspondente na pasta capas.\n3. Envie os MP4s ao TikTok Studio na ordem numérica.\n4. Consulte programacao-tiktok-studio.csv para título, descrição, hashtags e agenda.\n5. Cada texto completo também está na pasta legendas.\n6. Consulte manifesto-editorial.json para pontuação e revisão de legendas.\n7. Revise direitos autorais antes de programar.\n".AsMemory(), ct);
        }
        File.Move(temporary, target, true); return target;
    }

    private static string SafeFileName(string value)
    {
        var safe = string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "corte" : safe[..Math.Min(safe.Length, 64)];
    }
    private static string Csv(object? value)
    {
        var text = Convert.ToString(value)?.Replace("\r", " ").Replace("\n", " ") ?? "";
        if (text.StartsWith('=') || text.StartsWith('+') || text.StartsWith('-') || text.StartsWith('@')) text = "'" + text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
