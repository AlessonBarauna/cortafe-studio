using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class StorageCapacityService(IWebHostEnvironment environment, ProjectStore projects, ILogger<StorageCapacityService> logger)
{
    public const long DefaultSafetyReserve = 3L * 1024 * 1024 * 1024;
    private readonly string _root = environment.ContentRootPath;
    private readonly long _safetyReserve = environment.IsEnvironment("Test") || environment.IsEnvironment("Testing") ? 0 : DefaultSafetyReserve;

    public StorageCapacityReport Check(StorageOperation operation, double durationSeconds, int itemCount = 1)
    {
        var drive = new DriveInfo(Path.GetPathRoot(_root) ?? _root);
        var estimate = Estimate(operation, durationSeconds, itemCount);
        return Evaluate(operation, drive.AvailableFreeSpace, estimate, _safetyReserve);
    }

    public void Ensure(StorageOperation operation, double durationSeconds, int itemCount = 1)
    {
        var report = Check(operation, durationSeconds, itemCount);
        logger.LogInformation("[Storage] operation={Operation} estimatedBytes={Estimated} availableBytes={Available} allowed={Allowed}", operation, report.EstimatedBytes, report.AvailableBytes, report.Allowed);
        if (!report.Allowed) throw new InvalidOperationException(report.Message);
    }

    public StorageCapacityReport CheckNewProject(int itemCount = 1, long uploadBytes = 0)
    {
        var drive = new DriveInfo(Path.GetPathRoot(_root) ?? _root);
        var count = Math.Clamp(itemCount, 1, 20);
        var estimate = uploadBytes > 0
            ? Math.Max(512L * 1024 * 1024, uploadBytes * 2)
            : 512L * 1024 * 1024 * count;
        return Evaluate(StorageOperation.Acquisition, drive.AvailableFreeSpace, estimate, _safetyReserve);
    }

    public void EnsureNewProject(int itemCount = 1, long uploadBytes = 0)
    {
        var report = CheckNewProject(itemCount, uploadBytes);
        if (!report.Allowed) throw new InvalidOperationException(report.Message);
    }

    public static long Estimate(StorageOperation operation, double durationSeconds, int itemCount = 1)
    {
        var duration = Math.Max(1, durationSeconds); var count = Math.Clamp(itemCount, 1, 100);
        return operation switch
        {
            StorageOperation.Acquisition => ((long)(duration * 350_000) + 512L * 1024 * 1024) * count,
            StorageOperation.Transcription => (long)(duration * 32_000) + 1024L * 1024 * 1024,
            StorageOperation.BatchRender => (long)(duration * count * 300_000) + 768L * 1024 * 1024,
            _ => 1024L * 1024 * 1024
        };
    }

    public static StorageCapacityReport Evaluate(StorageOperation operation, long available, long estimated, long reserve)
    {
        var allowed = available - estimated >= reserve;
        return new StorageCapacityReport { Operation = operation, AvailableBytes = available, EstimatedBytes = estimated, SafetyReserveBytes = reserve, Allowed = allowed, Message = allowed ? "Espaco suficiente para a operacao." : $"Operacao bloqueada para proteger seus dados: sao necessarios aproximadamente {ToGb(estimated + reserve):0.0} GB e ha {ToGb(available):0.0} GB livres." };
    }

    public async Task<long> CleanupTemporaryAsync()
    {
        long removed = 0;
        foreach (var project in projects.ListAll())
        {
            var directory = Path.GetFullPath(projects.ProjectDirectory(project.Id)); var storageRoot = Path.GetFullPath(Path.Combine(_root, "storage", "projects")) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory)) continue;
            foreach (var pattern in new[] { "*.part", "*.tmp", "*.ytdl" })
                foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)) { try { var length = new FileInfo(file).Length; File.Delete(file); removed += length; } catch (IOException) { } }
        }
        await Task.CompletedTask; return removed;
    }

    private static double ToGb(long value) => value / 1024d / 1024 / 1024;
}
