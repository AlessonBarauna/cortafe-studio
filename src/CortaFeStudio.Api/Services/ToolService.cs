using System.Diagnostics;

namespace CortaFeStudio.Api.Services;

public sealed class ToolService(IWebHostEnvironment env)
{
    public string Root => env.ContentRootPath;
    public string Find(string name)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        if (name.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            var embeddedPython = Path.Combine(Root, "tools", "python", exe);
            if (File.Exists(embeddedPython)) return embeddedPython;
        }
        var local = Path.Combine(Root, "tools", exe);
        return File.Exists(local) ? local : exe;
    }

    public async Task<Dictionary<string, object>> CheckAsync()
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, command, args) in new[] { ("ffmpeg", Find("ffmpeg"), "-version"), ("ffprobe", Find("ffprobe"), "-version"), ("ytDlp", Find("yt-dlp"), "--version"), ("python", Find("python"), "--version"), ("ollama", "ollama", "--version") })
            result[key] = await VersionAsync(command, args);
        result["transcriber"] = File.Exists(Path.Combine(Root, "scripts", "transcribe.py"));
        return result;
    }

    private static async Task<object> VersionAsync(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(command, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!; var output = await p.StandardOutput.ReadLineAsync() ?? await p.StandardError.ReadLineAsync(); await p.WaitForExitAsync();
            return new { available = p.ExitCode == 0, version = output };
        }
        catch (Exception ex) { return new { available = false, error = ex.Message }; }
    }

    public async Task RunAsync(string command, IEnumerable<string> args, string? workDir = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir ?? Root };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {command}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct); var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct); var error = await stderr; await stdout;
        if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(command)} falhou: {error[^Math.Min(error.Length, 1800)..]}");
    }

    public async Task<string> CaptureAsync(string command, IEnumerable<string> args, string? workDir = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir ?? Root };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {command}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct); var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct); var output = await stdout; var error = await stderr;
        if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(command)} falhou: {error[^Math.Min(error.Length, 1800)..]}");
        return output.Trim();
    }
}
