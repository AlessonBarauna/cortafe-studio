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
        var rendered = project.Clips.Where(clip => clip.Approved && !string.IsNullOrWhiteSpace(clip.VideoPath))
            .Select(clip => (Clip: clip, Path: Path.Combine(directory, clip.VideoPath!)))
            .Where(item => File.Exists(item.Path)).OrderByDescending(item => item.Clip.Score).ToList();
        if (rendered.Count == 0) throw new InvalidOperationException("Renderize pelo menos um corte aprovado antes de criar o pacote TikTok Studio.");
        var target = Path.Combine(directory, "pacote-tiktok-studio.zip"); var temporary = target + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        await using (var stream = File.Create(temporary))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var csv = new StringBuilder("ordem;arquivo;titulo;descricao;hashtags;duracao_segundos\r\n");
            for (var index = 0; index < rendered.Count; index++)
            {
                ct.ThrowIfCancellationRequested(); var clip = rendered[index].Clip;
                var safeTitle = SafeFileName(clip.Title); var fileName = $"{index + 1:00}-{safeTitle}.mp4";
                var videoEntry = archive.CreateEntry($"videos/{fileName}", CompressionLevel.NoCompression);
                await using (var input = File.OpenRead(rendered[index].Path)) await using (var output = videoEntry.Open()) await input.CopyToAsync(output, ct);
                var hashtags = string.Join(' ', clip.Hashtags); var postText = $"{clip.Title}\n\n{clip.Caption}\n\n{hashtags}".Trim();
                var textEntry = archive.CreateEntry($"legendas/{index + 1:00}-{safeTitle}.txt", CompressionLevel.Optimal);
                await using (var writer = new StreamWriter(textEntry.Open(), new UTF8Encoding(false))) await writer.WriteAsync(postText.AsMemory(), ct);
                csv.AppendLine(string.Join(';', index + 1, Csv(fileName), Csv(clip.Title), Csv(clip.Caption), Csv(hashtags), Math.Round(clip.End - clip.Start, 1)));
            }
            var manifest = archive.CreateEntry("programacao-tiktok-studio.csv", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(true))) await writer.WriteAsync(csv.ToString().AsMemory(), ct);
            var guide = archive.CreateEntry("LEIA-ME.txt", CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(guide.Open(), new UTF8Encoding(false)))
                await writer.WriteAsync("PACOTE TIKTOK STUDIO\n\n1. Abra a pasta videos.\n2. Envie os MP4s ao TikTok Studio na ordem numérica.\n3. Consulte programacao-tiktok-studio.csv para título, descrição e hashtags.\n4. Cada texto completo também está na pasta legendas.\n5. Revise direitos autorais antes de programar.\n".AsMemory(), ct);
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
