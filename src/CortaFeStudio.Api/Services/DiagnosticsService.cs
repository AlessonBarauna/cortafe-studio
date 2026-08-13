namespace CortaFeStudio.Api.Services;

public sealed class DiagnosticsService(ProjectStore projects, ProjectQueue queue, ToolService tools, SocialService social, IWebHostEnvironment environment)
{
    public async Task<object> SnapshotAsync()
    {
        var toolStatus = await tools.CheckAsync();
        var root = Path.GetPathRoot(environment.ContentRootPath) ?? environment.ContentRootPath;
        var drive = new DriveInfo(root);
        var storage = Path.Combine(environment.ContentRootPath, "storage");
        var bytes = Directory.Exists(storage) ? Directory.EnumerateFiles(storage, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;
        var all = projects.List();
        var warnings = new List<string>();
        foreach (var required in new[] { "ffmpeg", "ffprobe", "ytDlp", "python" })
            if (toolStatus[required].GetType().GetProperty("available")?.GetValue(toolStatus[required]) is not true) warnings.Add($"Ferramenta obrigatória indisponível: {required}");
        if (drive.AvailableFreeSpace < 5L * 1024 * 1024 * 1024) warnings.Add("Menos de 5 GB livres para processar vídeos.");
        if (all.Any(p => p.Status == Models.ProjectStatus.Failed)) warnings.Add("Existem projetos com falha que precisam de revisão.");
        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            version = typeof(DiagnosticsService).Assembly.GetName().Version?.ToString(),
            runtime = new { framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processors = Environment.ProcessorCount, memoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 },
            disk = new { freeGb = Math.Round(drive.AvailableFreeSpace / 1024d / 1024 / 1024, 1), storageGb = Math.Round(bytes / 1024d / 1024 / 1024, 2) },
            projects = new { total = all.Count, ready = all.Count(p => p.Status == Models.ProjectStatus.Ready), processing = all.Count(p => p.Status is not Models.ProjectStatus.Ready and not Models.ProjectStatus.Failed), failed = all.Count(p => p.Status == Models.ProjectStatus.Failed) },
            queue = queue.Status(), tools = toolStatus, social = social.Status(), warnings
        };
    }
}
