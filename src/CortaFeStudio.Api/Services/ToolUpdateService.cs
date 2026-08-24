using System.Diagnostics;
using System.Text.Json;

namespace CortaFeStudio.Api.Services;

public sealed class ToolUpdateService(ToolService tools, IHttpClientFactory http)
{
    public async Task<object> CheckAsync(CancellationToken ct = default)
    {
        var installed = await tools.CaptureAsync(tools.Find("yt-dlp"), ["--version"], ct: ct);
        string? latest = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest"); request.Headers.UserAgent.ParseAdd("AmadoJesusStudio/1.0");
            using var response = await http.CreateClient().SendAsync(request, ct); response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); latest = document.RootElement.GetProperty("tag_name").GetString()?.TrimStart();
        } catch { }
        return new { installed, latest, updateAvailable = latest is not null && !installed.Contains(latest, StringComparison.OrdinalIgnoreCase), nodeRequired = "22+" };
    }

    public async Task<object> UpdateYtDlpAsync(CancellationToken ct = default)
    {
        var executable = tools.Find("yt-dlp"); if (!Path.IsPathRooted(executable)) throw new InvalidOperationException("O yt-dlp do sistema deve ser atualizado pelo gerenciador que o instalou.");
        var backup = executable + ".backup"; File.Copy(executable, backup, true);
        try
        {
            await RunAsync(executable, ["-U"], ct);
            var version = await tools.CaptureAsync(executable, ["--version"], ct: ct);
            File.Delete(backup); return new { updated = true, version };
        }
        catch
        {
            File.Copy(backup, executable, true); File.Delete(backup); throw new InvalidOperationException("A atualização falhou e a versão anterior do yt-dlp foi restaurada.");
        }
    }

    private static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o atualizador.");
        var error = process.StandardError.ReadToEndAsync(ct); await process.WaitForExitAsync(ct); if (process.ExitCode != 0) throw new InvalidOperationException(await error);
    }
}
