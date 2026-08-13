using System.IO.Compression;
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
}
