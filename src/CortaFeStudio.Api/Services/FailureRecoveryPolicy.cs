namespace CortaFeStudio.Api.Services;

public static class FailureRecoveryPolicy
{
    public const int MaximumAttempts = 3;

    public static bool IsTransient(string code, string message)
    {
        if (code is "youtube-auth-required" or "youtube-cookie-access") return false;
        if (message.Contains("armazenamento", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("espaço", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("espaco", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("não produziu conteúdo", StringComparison.OrdinalIgnoreCase)) return false;
        return new[] { "403", "temporariamente", "timeout", "timed out", "connection", "conexão", "socket", "soquete", "network", "rede" }
            .Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public static TimeSpan Delay(int attempt) => TimeSpan.FromSeconds(attempt switch { 1 => 5, 2 => 15, _ => 30 });
}
