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

    public List<string> YouTubeArguments()
    {
        var args = new List<string> { "--force-ipv4" };
        if (CommandAvailable(Find("node"))) args.AddRange(["--js-runtimes", "node"]);
        return args;
    }

    public async Task<Dictionary<string, object>> CheckAsync()
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, command, args) in new[] { ("ffmpeg", Find("ffmpeg"), "-version"), ("ffprobe", Find("ffprobe"), "-version"), ("ytDlp", Find("yt-dlp"), "--version"), ("python", Find("python"), "--version"), ("node", Find("node"), "--version"), ("ollama", "ollama", "--version") })
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

    private static bool CommandAvailable(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(command, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null || !process.WaitForExit(3000)) return false;
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task RunAsync(string command, IEnumerable<string> args, string? workDir = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir ?? Root };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {command}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct); var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct); var error = await stderr; await stdout;
        if (p.ExitCode != 0) throw new InvalidOperationException(FriendlyError(command, error));
    }

    public async Task<string> CaptureAsync(string command, IEnumerable<string> args, string? workDir = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir ?? Root };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {command}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct); var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct); var output = await stdout; var error = await stderr;
        if (p.ExitCode != 0) throw new InvalidOperationException(FriendlyError(command, error));
        return output.Trim();
    }

    public async Task<string> CaptureDiagnosticAsync(string command, IEnumerable<string> args, string? workDir = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir ?? Root };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Nao foi possivel iniciar {command}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct); var output = await stdout; var diagnostic = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(FriendlyError(command, diagnostic));
        return output + Environment.NewLine + diagnostic;
    }

    public static string ClassifyFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "processing-error";
        if (error.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("confirmação de acesso", StringComparison.OrdinalIgnoreCase)) return "youtube-auth-required";
        if (error.Contains("cookie database", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("failed to decrypt", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Não foi possível acessar a sessão do navegador", StringComparison.OrdinalIgnoreCase)) return "youtube-cookie-access";
        return "processing-error";
    }

    private static string FriendlyError(string command, string error)
    {
        if (Path.GetFileName(command).StartsWith("yt-dlp", StringComparison.OrdinalIgnoreCase))
        {
            if (error.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                return "O YouTube pediu uma confirmação de acesso. Escolha abaixo o navegador em que sua conta do YouTube está conectada e processe novamente com essa sessão.";
            if (error.Contains("cookie database", StringComparison.OrdinalIgnoreCase) || error.Contains("failed to decrypt", StringComparison.OrdinalIgnoreCase))
                return "Não foi possível acessar a sessão do navegador. Feche completamente o navegador escolhido e tente novamente.";
            if (error.Contains("No supported JavaScript runtime", StringComparison.OrdinalIgnoreCase))
                return "O YouTube exige um runtime JavaScript. Instale o Node.js 22 ou superior e reinicie o CortaFé.";
            if (error.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase))
                return "O YouTube recusou temporariamente o download (erro 403). Atualize o yt-dlp, desative VPN/proxy e tente novamente.";
        }
        return $"{Path.GetFileName(command)} falhou: {error[^Math.Min(error.Length, 1800)..]}";
    }
}
