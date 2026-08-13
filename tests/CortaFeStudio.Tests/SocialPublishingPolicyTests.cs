using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class SocialPublishingPolicyTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"cortafe-social-{Guid.NewGuid():N}.mp4");

    public SocialPublishingPolicyTests() => File.WriteAllBytes(_file, [1, 2, 3]);

    [Fact]
    public void Validate_RejeitaTituloLongoNoYouTube() => Assert.Throws<InvalidOperationException>(() =>
        SocialPublishingPolicy.Validate(SocialPlatform.YouTube, _file, 60, new PublishRequest(SocialPlatform.YouTube, new string('a', 101), "Legenda")));

    [Fact]
    public void Validate_AceitaCorteVerticalDentroDosLimites() =>
        SocialPublishingPolicy.Validate(SocialPlatform.TikTok, _file, 45, new PublishRequest(SocialPlatform.TikTok, "Título", "Legenda", "private"));

    [Fact]
    public void Validate_InstagramNaoPrometePublicacaoPrivada() => Assert.Throws<InvalidOperationException>(() =>
        SocialPublishingPolicy.Validate(SocialPlatform.Instagram, _file, 45, new PublishRequest(SocialPlatform.Instagram, "Título", "Legenda", "private")));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 20)]
    public void RetryDelay_AumentaEsperaEntreTentativas(int attempt, int minutes) => Assert.Equal(TimeSpan.FromMinutes(minutes), SocialPublishingPolicy.RetryDelay(attempt));

    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }
}
