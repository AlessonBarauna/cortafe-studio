using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class FailureRecoveryPolicyTests
{
    [Theory]
    [InlineData("processing-error", "HTTP Error 403: temporariamente indisponível")]
    [InlineData("processing-error", "connection timed out")]
    public void IsTransient_ReconheceFalhasQuePodemSerRepetidas(string code, string message) => Assert.True(FailureRecoveryPolicy.IsTransient(code, message));

    [Theory]
    [InlineData("youtube-auth-required", "Sign in to confirm")]
    [InlineData("processing-error", "Operacao bloqueada por armazenamento")]
    [InlineData("processing-error", "A transcrição não produziu conteúdo")]
    public void IsTransient_NaoRepeteFalhasQueExigemAcao(string code, string message) => Assert.False(FailureRecoveryPolicy.IsTransient(code, message));

    [Fact]
    public void Delay_AumentaEntreTentativas() => Assert.True(FailureRecoveryPolicy.Delay(2) > FailureRecoveryPolicy.Delay(1));
}
