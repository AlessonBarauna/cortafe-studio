using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CortaFeStudio.Api.Services;

public sealed class WorshipFmService(ToolService tools)
{
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".ogg", ".wav", ".aac", ".m4a", ".wma", ".m4r", ".flac"
    };

    private static readonly HashSet<string> MatchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "official", "oficial", "video", "vídeo", "clipe", "clip", "lyrics", "lyric", "letra",
        "live", "vivo", "audio", "áudio", "version", "versao", "versão", "music", "musica", "música",
        "feat", "ft", "ao", "at", "home", "culto"
    };

    public string DefaultDownloads => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public string DefaultSourceFolder => Path.Combine(DefaultDownloads, "Worship_FM_Musicas");
    public string DefaultGtaRoot => Path.Combine(DefaultDownloads, "GTA San Andreas MOD");

    public static bool IsYouTubePlaylistUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")) return false;
        return uri.Query.Contains("list=", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WorshipPlaylistResult> ReadPlaylistAsync(WorshipPlaylistRequest request, CancellationToken ct)
    {
        if (!IsYouTubePlaylistUrl(request.Url))
            throw new ArgumentException("Informe uma playlist pública válida do YouTube.");

        var args = tools.YouTubeArguments();
        args.AddRange(["--flat-playlist", "--skip-download", "--dump-single-json", "--no-warnings"]);
        args = YouTubeAcquisition.WithBrowserSession(args, request.Browser);
        args.Add(request.Url.Trim());

        var raw = await tools.CaptureAsync(tools.Find("yt-dlp"), args, ct: ct);
        try
        {
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            var playlistTitle = String(root, "title") ?? "Playlist do YouTube";
            var tracks = new List<WorshipPlaylistTrack>();

            if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                var fallbackIndex = 1;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    var id = String(entry, "id") ?? "";
                    var title = String(entry, "title") ?? $"Faixa {fallbackIndex:00}";
                    var uploader = String(entry, "channel") ?? String(entry, "uploader") ?? "";
                    var index = Integer(entry, "playlist_index") ?? fallbackIndex;
                    var directUrl = String(entry, "webpage_url") ?? String(entry, "url");
                    if (string.IsNullOrWhiteSpace(directUrl) || !Uri.IsWellFormedUriString(directUrl, UriKind.Absolute))
                        directUrl = string.IsNullOrWhiteSpace(id) ? "" : $"https://www.youtube.com/watch?v={id}";

                    tracks.Add(new WorshipPlaylistTrack(index, title, uploader, id, directUrl));
                    fallbackIndex++;
                }
            }

            if (tracks.Count == 0)
                throw new InvalidOperationException("A playlist não retornou nenhuma faixa. Confirme se ela está pública e tente novamente.");

            tracks = tracks.OrderBy(track => track.Index).ToList();
            return new WorshipPlaylistResult(playlistTitle, request.Url.Trim(), tracks.Count, tracks);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("O yt-dlp não retornou metadados válidos da playlist.", ex);
        }
    }

    public WorshipFmStatus Status(string? gtaRoot, string? sourceFolder, int channel)
    {
        channel = ValidateChannel(channel);
        gtaRoot = FullDirectoryPath(gtaRoot, DefaultGtaRoot);
        sourceFolder = FullDirectoryPath(sourceFolder, DefaultSourceFolder);

        var atpRoot = Path.Combine(gtaRoot, "atp");
        var channelFolder = Path.Combine(atpRoot, channel.ToString(CultureInfo.InvariantCulture));
        var localFiles = Directory.Exists(sourceFolder) ? EnumerateAudio(sourceFolder).ToList() : [];
        var installedFiles = Directory.Exists(channelFolder) ? EnumerateAudio(channelFolder).ToList() : [];
        var infoPath = Path.Combine(channelFolder, "info.ini");
        var stationName = File.Exists(infoPath) ? ReadStationName(File.ReadAllText(infoPath)) : null;

        return new WorshipFmStatus
        {
            GtaRoot = gtaRoot,
            SourceFolder = sourceFolder,
            Channel = channel,
            AtpReady = File.Exists(Path.Combine(gtaRoot, "gta_sa.exe")) && Directory.Exists(atpRoot) && File.Exists(Path.Combine(atpRoot, "setting.ini")),
            ChannelReady = Directory.Exists(channelFolder) && File.Exists(infoPath),
            StationName = stationName,
            LocalAudioCount = localFiles.Count,
            InstalledAudioCount = installedFiles.Count,
            ChannelFolder = channelFolder
        };
    }

    public WorshipLocalScanResult ScanLocal(WorshipLocalScanRequest request)
    {
        var folder = FullDirectoryPath(request.Folder, DefaultSourceFolder);
        Directory.CreateDirectory(folder);
        var files = EnumerateAudio(folder)
            .Select(path => new WorshipLocalFile(Path.GetFileName(path), path, new FileInfo(path).Length))
            .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<WorshipTrackMatch>();
        foreach (var track in request.Tracks.OrderBy(track => track.Index))
        {
            var candidates = files
                .Select(file => new WorshipMatchCandidate(file.Name, file.FullPath, MatchScore(track.Title, file.Name)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(5)
                .ToList();

            var best = candidates.FirstOrDefault(candidate => candidate.Score >= 0.42 && !used.Contains(candidate.FullPath));
            if (best is not null) used.Add(best.FullPath);
            matches.Add(new WorshipTrackMatch(track.Index, track.Title, track.Uploader, best?.FullPath, best?.Score ?? 0, best is null ? "missing" : "matched", candidates));
        }

        return new WorshipLocalScanResult(folder, files.Count, matches.Count(match => match.Status == "matched"), files, matches);
    }

    public async Task<WorshipSyncResult> SyncAsync(WorshipFmSyncRequest request, CancellationToken ct)
    {
        var channel = ValidateChannel(request.Channel);
        var gtaRoot = FullDirectoryPath(request.GtaRoot, DefaultGtaRoot);
        var sourceFolder = FullDirectoryPath(request.SourceFolder, DefaultSourceFolder);
        if (!File.Exists(Path.Combine(gtaRoot, "gta_sa.exe"))) throw new DirectoryNotFoundException("A pasta informada não contém gta_sa.exe.");
        var atpRoot = Path.Combine(gtaRoot, "atp");
        if (!Directory.Exists(atpRoot) || !File.Exists(Path.Combine(atpRoot, "setting.ini")))
            throw new DirectoryNotFoundException("ATP não foi encontrado na raiz do GTA.");
        if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException("A pasta de músicas locais não existe.");
        if (request.Tracks.Count == 0) throw new ArgumentException("Nenhuma faixa local foi selecionada para sincronização.");

        var selected = request.Tracks.OrderBy(track => track.Index).ToList();
        foreach (var track in selected)
        {
            if (string.IsNullOrWhiteSpace(track.LocalPath)) throw new ArgumentException($"A faixa {track.Index:00} ainda não possui arquivo local.");
            var full = Path.GetFullPath(track.LocalPath);
            EnsureInside(sourceFolder, full);
            if (!File.Exists(full)) throw new FileNotFoundException($"Arquivo local não encontrado para a faixa {track.Index:00}.", full);
            if (!SupportedAudioExtensions.Contains(Path.GetExtension(full))) throw new ArgumentException($"Formato não suportado: {Path.GetExtension(full)}");
        }

        var channelFolder = Path.Combine(atpRoot, channel.ToString(CultureInfo.InvariantCulture));
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupRoot = Path.Combine(DefaultDownloads, "GTA_SA_BACKUPS", $"WORSHIP_FM_SYNC_{stamp}");
        Directory.CreateDirectory(backupRoot);
        if (Directory.Exists(channelFolder)) CopyDirectory(channelFolder, Path.Combine(backupRoot, channel.ToString(CultureInfo.InvariantCulture)));
        var setting = Path.Combine(atpRoot, "setting.ini");
        if (File.Exists(setting)) File.Copy(setting, Path.Combine(backupRoot, "setting.ini"), overwrite: true);

        var stationName = CleanStationName(request.StationName);
        var stageFolder = Path.Combine(atpRoot, $".__worship_sync_{Guid.NewGuid():N}");
        var oldFolder = channelFolder + $".__old_{Guid.NewGuid():N}";
        Directory.CreateDirectory(stageFolder);
        var previousInfo = Path.Combine(channelFolder, "info.ini");
        var stageInfo = Path.Combine(stageFolder, "info.ini");
        File.WriteAllText(stageInfo, File.Exists(previousInfo)
            ? SetStationName(File.ReadAllText(previousInfo), stationName)
            : BuildChannelInfo(stationName), Encoding.ASCII);

        try
        {
            var sequence = 1;
            foreach (var track in selected)
            {
                ct.ThrowIfCancellationRequested();
                var source = Path.GetFullPath(track.LocalPath);
                var safeTitle = SafeFileName(string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileNameWithoutExtension(source) : track.Title);
                if (request.Normalize)
                {
                    var target = Path.Combine(stageFolder, $"{sequence:000} - {safeTitle}.mp3");
                    await tools.RunAsync(tools.Find("ffmpeg"),
                        ["-hide_banner", "-loglevel", "error", "-y", "-i", source, "-vn", "-af", "loudnorm=I=-16:LRA=11:TP=-1.5", "-codec:a", "libmp3lame", "-b:a", "192k", target], ct: ct);
                }
                else
                {
                    var target = Path.Combine(stageFolder, $"{sequence:000} - {safeTitle}{Path.GetExtension(source).ToLowerInvariant()}");
                    File.Copy(source, target, overwrite: true);
                }
                sequence++;
            }

            var trac = Path.Combine(stageFolder, "trac.dat");
            if (File.Exists(trac)) File.Delete(trac);

            if (Directory.Exists(channelFolder)) Directory.Move(channelFolder, oldFolder);
            Directory.Move(stageFolder, channelFolder);
            if (Directory.Exists(oldFolder)) Directory.Delete(oldFolder, recursive: true);
        }
        catch
        {
            if (Directory.Exists(stageFolder)) Directory.Delete(stageFolder, recursive: true);
            if (!Directory.Exists(channelFolder) && Directory.Exists(oldFolder)) Directory.Move(oldFolder, channelFolder);
            throw;
        }

        return new WorshipSyncResult(channel, stationName, selected.Count, request.Normalize, channelFolder, backupRoot);
    }

    public static string NormalizeForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        var normalized = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\[[^\]]*\]|\([^\)]*\)", " ");
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    public static double MatchScore(string? title, string? fileName)
    {
        var left = NormalizeForMatching(title);
        var right = NormalizeForMatching(Path.GetFileNameWithoutExtension(fileName ?? ""));
        if (left.Length == 0 || right.Length == 0) return 0;
        if (right.Contains(left, StringComparison.Ordinal) || left.Contains(right, StringComparison.Ordinal)) return 0.98;

        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0;
        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (union == 0) return 0;
        var jaccard = (double)intersection / union;
        var coverage = (double)intersection / leftTokens.Count;
        return Math.Round((jaccard * 0.45) + (coverage * 0.55), 3);
    }

    public static string BuildChannelInfo(string stationName) => $"""[PROPERTIES]
Channel name={CleanStationName(stationName)}

Track scan protection=0
Track variants=0
Play mode=0
Atmospheric tracks=0
Play area X=0
Play area Y=0
Play area Radius=0
Channel volume=100
Logo TXD ID=NONE
""";

    private static List<string> Tokens(string normalized) => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length > 1 && !MatchStopWords.Contains(token)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<string> EnumerateAudio(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
        .Where(path => SupportedAudioExtensions.Contains(Path.GetExtension(path)));

    private static int ValidateChannel(int channel)
    {
        if (channel is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(channel), "O canal ATP deve estar entre 1 e 64.");
        return channel;
    }

    private static string FullDirectoryPath(string? value, string fallback) => Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : Environment.ExpandEnvironmentVariables(value.Trim()));

    private static void EnsureInside(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new UnauthorizedAccessException("O arquivo selecionado precisa estar dentro da pasta de músicas configurada.");
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? ' ' : ch).ToArray());
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().Trim('.');
        if (cleaned.Length == 0) cleaned = "Faixa";
        return cleaned.Length <= 120 ? cleaned : cleaned[..120].Trim();
    }

    private static string CleanStationName(string? value)
    {
        var clean = Regex.Replace(value?.Trim() ?? "WORSHIP FM", @"[\r\n=]+", " ").Trim();
        return string.IsNullOrWhiteSpace(clean) ? "WORSHIP FM" : clean.Length <= 48 ? clean : clean[..48].Trim();
    }

    private static string SetStationName(string info, string stationName)
    {
        var clean = CleanStationName(stationName);
        if (Regex.IsMatch(info, @"(?im)^\s*Channel name\s*=.*$"))
            return Regex.Replace(info, @"(?im)^\s*Channel name\s*=.*$", $"Channel name={clean}");
        if (Regex.IsMatch(info, @"(?im)^\s*\[PROPERTIES\]\s*$"))
            return Regex.Replace(info, @"(?im)^\s*\[PROPERTIES\]\s*$", $"[PROPERTIES]{Environment.NewLine}Channel name={clean}", 1);
        return BuildChannelInfo(clean);
    }

    private static string? ReadStationName(string info)
    {
        var match = Regex.Match(info, @"(?im)^\s*Channel name\s*=\s*(.+?)\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static int? Integer(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;
}

public sealed class WorshipPlaylistRequest
{
    public string Url { get; set; } = "";
    public string? Browser { get; set; }
}

public sealed record WorshipPlaylistTrack(int Index, string Title, string Uploader, string VideoId, string Url);
public sealed record WorshipPlaylistResult(string Title, string Url, int Count, List<WorshipPlaylistTrack> Tracks);

public sealed class WorshipLocalScanRequest
{
    public string? Folder { get; set; }
    public List<WorshipTrackReference> Tracks { get; set; } = [];
}

public sealed record WorshipTrackReference(int Index, string Title, string Uploader, string Url);
public sealed record WorshipLocalFile(string Name, string FullPath, long SizeBytes);
public sealed record WorshipMatchCandidate(string Name, string FullPath, double Score);
public sealed record WorshipTrackMatch(int Index, string Title, string Uploader, string? MatchedPath, double Score, string Status, List<WorshipMatchCandidate> Candidates);
public sealed record WorshipLocalScanResult(string Folder, int FileCount, int MatchedCount, List<WorshipLocalFile> Files, List<WorshipTrackMatch> TrackMatches);

public sealed class WorshipFmSyncRequest
{
    public string? GtaRoot { get; set; }
    public string? SourceFolder { get; set; }
    public int Channel { get; set; } = 13;
    public string StationName { get; set; } = "WORSHIP FM";
    public bool Normalize { get; set; } = true;
    public List<WorshipFmSyncTrack> Tracks { get; set; } = [];
}

public sealed record WorshipFmSyncTrack(int Index, string Title, string LocalPath);
public sealed record WorshipSyncResult(int Channel, string StationName, int TrackCount, bool Normalized, string ChannelFolder, string BackupFolder);

public sealed class WorshipFmStatus
{
    public string GtaRoot { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public int Channel { get; set; }
    public bool AtpReady { get; set; }
    public bool ChannelReady { get; set; }
    public string? StationName { get; set; }
    public int LocalAudioCount { get; set; }
    public int InstalledAudioCount { get; set; }
    public string ChannelFolder { get; set; } = "";
}
