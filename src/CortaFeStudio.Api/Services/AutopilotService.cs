using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class AutopilotService : BackgroundService
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm", ".mp3", ".wav", ".m4a" };
    private readonly ProjectStore _store;
    private readonly ProjectQueue _queue;
    private readonly ToolService _tools;
    private readonly ILogger<AutopilotService> _logger;
    private readonly string _configPath;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private AutopilotConfiguration _configuration;

    public AutopilotService(ProjectStore store, ProjectQueue queue, ToolService tools, ILogger<AutopilotService> logger)
    {
        _store = store;
        _queue = queue;
        _tools = tools;
        _logger = logger;
        var storage = Path.Combine(tools.Root, "storage");
        Directory.CreateDirectory(storage);
        _configPath = Path.Combine(storage, "autopilot.json");
        _configuration = Load();
    }

    public AutopilotConfiguration Snapshot()
    {
        var json = JsonSerializer.Serialize(_configuration, _json);
        return JsonSerializer.Deserialize<AutopilotConfiguration>(json, _json) ?? new();
    }

    public async Task<AutopilotConfiguration> UpdateAsync(AutopilotConfigurationUpdate request, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            foreach (var source in request.Sources)
            {
                source.Name = string.IsNullOrWhiteSpace(source.Name) ? "Canal" : source.Name.Trim();
                source.Url = source.Url?.Trim() ?? "";
                if (!IsSupportedWatchUrl(source.Url))
                    throw new ArgumentException($"Fonte inválida em “{source.Name}”. O Autopilot monitora canais do YouTube.");
                source.ContentType = NormalizeContentType(source.ContentType);
                source.WhisperModel = NormalizeWhisperModel(source.WhisperModel);
                source.ClipCount = Math.Clamp(source.ClipCount, 1, 20);
                if (string.IsNullOrWhiteSpace(source.Id)) source.Id = Guid.NewGuid().ToString("N")[..10];
                var previous = _configuration.Sources.FirstOrDefault(item => item.Id == source.Id);
                if (previous is not null)
                {
                    source.LastSeenMediaId ??= previous.LastSeenMediaId;
                    source.LastQueuedMediaId ??= previous.LastQueuedMediaId;
                    source.LastSeenAt ??= previous.LastSeenAt;
                }
            }

            foreach (var folder in request.WatchedFolders)
            {
                folder.Name = string.IsNullOrWhiteSpace(folder.Name) ? "Pasta de vídeos" : folder.Name.Trim();
                folder.Path = folder.Path?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(folder.Path)) throw new ArgumentException($"Informe o caminho da pasta em “{folder.Name}”.");
                var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder.Path));
                if (!Directory.Exists(fullPath)) throw new ArgumentException($"A pasta “{fullPath}” não existe ou não está acessível.");
                folder.Path = fullPath;
                folder.ContentType = NormalizeContentType(folder.ContentType);
                folder.WhisperModel = NormalizeWhisperModel(folder.WhisperModel);
                folder.ClipCount = Math.Clamp(folder.ClipCount, 1, 20);
                if (string.IsNullOrWhiteSpace(folder.Id)) folder.Id = Guid.NewGuid().ToString("N")[..10];
                var previous = _configuration.WatchedFolders.FirstOrDefault(item => item.Id == folder.Id);
                if (previous is not null)
                {
                    folder.ImportedFileKeys = previous.ImportedFileKeys.ToList();
                    folder.LastScanAt ??= previous.LastScanAt;
                    folder.LastImportedFile ??= previous.LastImportedFile;
                    folder.LastError ??= previous.LastError;
                }
            }

            _configuration.Enabled = request.Enabled;
            _configuration.PollMinutes = Math.Clamp(request.PollMinutes, 5, 180);
            _configuration.Sources = request.Sources.GroupBy(item => item.Id).Select(group => group.First()).Take(30).ToList();
            _configuration.WatchedFolders = request.WatchedFolders.GroupBy(item => item.Id).Select(group => group.First()).Take(20).ToList();
            await SaveUnsafeAsync(ct);
            return Snapshot();
        }
        finally { _stateLock.Release(); }
    }

    public async Task<AutopilotCheckResult> CheckNowAsync(bool queueCurrentOnFirstCheck = false, CancellationToken ct = default)
    {
        await _checkLock.WaitAsync(ct);
        try
        {
            var result = new AutopilotCheckResult();
            var snapshot = Snapshot();
            foreach (var source in snapshot.Sources.Where(source => source.Enabled))
                await CheckRemoteSourceAsync(source, result, queueCurrentOnFirstCheck, ct);
            foreach (var folder in snapshot.WatchedFolders.Where(folder => folder.Enabled))
                await CheckFolderAsync(folder, result, queueCurrentOnFirstCheck, ct);

            await _stateLock.WaitAsync(ct);
            try
            {
                _configuration.LastCheckAt = result.CheckedAt;
                _configuration.LastMessage = result.Messages.LastOrDefault();
                await SaveUnsafeAsync(ct);
            }
            finally { _stateLock.Release(); }
            return result;
        }
        finally { _checkLock.Release(); }
    }

    private async Task CheckRemoteSourceAsync(AutopilotSource source, AutopilotCheckResult result, bool queueCurrentOnFirstCheck, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        result.SourcesChecked++;
        try
        {
            var media = await DiscoverLatestCompletedAsync(source, ct);
            if (media is null)
            {
                source.LastError = "Nenhum culto/vídeo concluído foi encontrado.";
                result.Messages.Add($"{source.Name}: nenhum conteúdo concluído encontrado.");
                await MergeSourceStateAsync(source, ct);
                return;
            }

            var firstCheck = string.IsNullOrWhiteSpace(source.LastSeenMediaId);
            var changed = !string.Equals(source.LastSeenMediaId, media.Id, StringComparison.Ordinal);
            source.LastSeenMediaId = media.Id;
            source.LastSeenAt = DateTime.UtcNow;
            source.LastError = null;

            if (firstCheck && !queueCurrentOnFirstCheck)
            {
                result.Messages.Add($"{source.Name}: monitoramento iniciado em “{media.Title}”. O próximo culto novo será processado.");
                await MergeSourceStateAsync(source, ct);
                return;
            }
            if (!changed)
            {
                result.Messages.Add($"{source.Name}: sem conteúdo novo.");
                await MergeSourceStateAsync(source, ct);
                return;
            }
            if (string.Equals(source.LastQueuedMediaId, media.Id, StringComparison.Ordinal) ||
                _store.ListAll().Any(project => string.Equals(project.Source, media.Url, StringComparison.OrdinalIgnoreCase)))
            {
                source.LastQueuedMediaId = media.Id;
                result.Messages.Add($"{source.Name}: “{media.Title}” já existe na biblioteca.");
                await MergeSourceStateAsync(source, ct);
                return;
            }

            var project = await _store.CreateAsync(media.Title, SourceKind.YouTube, media.Url, BuildOptions(source.ContentType, source.ClipCount, source.WhisperModel, source.Topic));
            await _queue.EnqueueAsync(project.Id);
            source.LastQueuedMediaId = media.Id;
            result.ProjectsQueued++;
            result.Messages.Add($"{source.Name}: “{media.Title}” entrou automaticamente na fila.");
            await MergeSourceStateAsync(source, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            source.LastError = ex.Message;
            result.Messages.Add($"{source.Name}: {ex.Message}");
            _logger.LogWarning(ex, "[Autopilot] Falha ao consultar {Source}", source.Name);
            await MergeSourceStateAsync(source, ct);
        }
    }

    private async Task CheckFolderAsync(WatchedFolderSource folder, AutopilotCheckResult result, bool queueCurrentOnFirstCheck, CancellationToken ct)
    {
        result.FoldersChecked++;
        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder.Path));
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Pasta não encontrada: {fullPath}");
            var search = folder.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(fullPath, "*", search)
                .Where(path => MediaExtensions.Contains(Path.GetExtension(path)))
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists && file.Length > 1024 && DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromSeconds(30))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();

            var firstCheck = folder.ImportedFileKeys.Count == 0 && folder.LastScanAt is null;
            if (firstCheck && !queueCurrentOnFirstCheck)
            {
                folder.ImportedFileKeys = files.Select(FileKey).TakeLast(500).ToList();
                folder.LastScanAt = DateTime.UtcNow;
                folder.LastError = null;
                result.Messages.Add($"{folder.Name}: pasta preparada com {files.Count} arquivo(s) já existente(s); somente novos arquivos serão importados.");
                await MergeFolderStateAsync(folder, ct);
                return;
            }

            var imported = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var key = FileKey(file);
                if (folder.ImportedFileKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
                if (!CanReadCompletedFile(file.FullName)) continue;
                var project = await _store.CreateFromLocalPathAsync(file.FullName, BuildOptions(folder.ContentType, folder.ClipCount, folder.WhisperModel, folder.Topic), ct);
                await _queue.EnqueueAsync(project.Id);
                folder.ImportedFileKeys.Add(key);
                folder.LastImportedFile = file.FullName;
                imported++;
                result.ProjectsQueued++;
            }
            if (folder.ImportedFileKeys.Count > 500) folder.ImportedFileKeys = folder.ImportedFileKeys.TakeLast(500).ToList();
            folder.LastScanAt = DateTime.UtcNow;
            folder.LastError = null;
            result.Messages.Add(imported > 0 ? $"{folder.Name}: {imported} novo(s) arquivo(s) entrou(aram) na fila." : $"{folder.Name}: nenhum arquivo novo.");
            await MergeFolderStateAsync(folder, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            folder.LastError = ex.Message;
            result.Messages.Add($"{folder.Name}: {ex.Message}");
            _logger.LogWarning(ex, "[Autopilot] Falha ao vigiar pasta {Folder}", folder.Name);
            await MergeFolderStateAsync(folder, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var config = Snapshot();
            if (config.Enabled)
            {
                try { await CheckNowAsync(false, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "[Autopilot] Ciclo automático falhou"); }
            }
            var delay = config.Enabled ? TimeSpan.FromMinutes(Math.Clamp(config.PollMinutes, 5, 180)) : TimeSpan.FromMinutes(1);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<RemoteMedia?> DiscoverLatestCompletedAsync(AutopilotSource source, CancellationToken ct)
    {
        var url = NormalizeChannelUrl(source.Url);
        var args = _tools.YouTubeArguments();
        args.AddRange(["--flat-playlist", "--playlist-end", "8", "--print", "%(id)s\t%(title)s\t%(url)s\t%(live_status)s", url]);
        var output = await _tools.CaptureAsync(_tools.Find("yt-dlp"), args, _tools.Root, ct);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var id = parts[0].Trim();
            var title = parts[1].Trim();
            var itemUrl = parts.Length > 2 ? parts[2].Trim() : "";
            var liveStatus = parts.Length > 3 ? parts[3].Trim().ToLowerInvariant() : "";
            if (liveStatus is "is_live" or "is_upcoming") continue;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!Uri.TryCreate(itemUrl, UriKind.Absolute, out _)) itemUrl = $"https://www.youtube.com/watch?v={id}";
            return new RemoteMedia(id, string.IsNullOrWhiteSpace(title) ? $"Culto {id}" : title, itemUrl);
        }
        return null;
    }

    private async Task MergeSourceStateAsync(AutopilotSource source, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            var target = _configuration.Sources.FirstOrDefault(item => item.Id == source.Id);
            if (target is null) return;
            target.LastSeenMediaId = source.LastSeenMediaId;
            target.LastQueuedMediaId = source.LastQueuedMediaId;
            target.LastSeenAt = source.LastSeenAt;
            target.LastError = source.LastError;
            await SaveUnsafeAsync(ct);
        }
        finally { _stateLock.Release(); }
    }

    private async Task MergeFolderStateAsync(WatchedFolderSource folder, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            var target = _configuration.WatchedFolders.FirstOrDefault(item => item.Id == folder.Id);
            if (target is null) return;
            target.ImportedFileKeys = folder.ImportedFileKeys.ToList();
            target.LastScanAt = folder.LastScanAt;
            target.LastImportedFile = folder.LastImportedFile;
            target.LastError = folder.LastError;
            await SaveUnsafeAsync(ct);
        }
        finally { _stateLock.Release(); }
    }

    private AutopilotConfiguration Load()
    {
        try
        {
            if (!File.Exists(_configPath)) return new();
            return JsonSerializer.Deserialize<AutopilotConfiguration>(File.ReadAllText(_configPath), _json) ?? new();
        }
        catch { return new(); }
    }

    private async Task SaveUnsafeAsync(CancellationToken ct)
    {
        var temporary = _configPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_configuration, _json), ct);
        File.Move(temporary, _configPath, true);
    }

    private static ProjectOptions BuildOptions(string contentType, int clipCount, string whisperModel, string? topic)
    {
        var options = new ProjectOptions { ContentType = contentType, ClipCount = clipCount, WhisperModel = whisperModel, Topic = topic };
        options.ApplyAutomaticDuration();
        return options;
    }

    private static string FileKey(FileInfo file) => $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
    private static bool CanReadCompletedFile(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return stream.Length > 1024; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IsSupportedWatchUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be" }.Contains(uri.Host.ToLowerInvariant());

    private static string NormalizeChannelUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.Contains("/watch", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("playlist?", StringComparison.OrdinalIgnoreCase)) return trimmed;
        if (trimmed.EndsWith("/streams", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("/videos", StringComparison.OrdinalIgnoreCase)) return trimmed;
        return trimmed + "/streams";
    }

    private static string NormalizeContentType(string value) => value is "louvor" or "podcast" or "aula" or "motivacao" or "negocios" or "tecnologia" ? value : "pregacao";
    private static string NormalizeWhisperModel(string value) => value is "small" or "medium" or "large-v3" ? value : "base";
    private sealed record RemoteMedia(string Id, string Title, string Url);
}
