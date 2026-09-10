using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class WorshipFmServiceTests
{
    [Fact]
    public void Matching_IgnoraAcentosEPalavrasComuns()
    {
        var score = WorshipFmService.MatchScore(
            "Não Há Outro Igual a Ti (Ao Vivo)",
            "Alessandro Vilas Boas - Nao Ha Outro Igual a Ti - Official Video.mp3");

        Assert.True(score >= 0.6);
    }

    [Fact]
    public void Matching_DiferenciaMusicasSemelhantes()
    {
        var certo = WorshipFmService.MatchScore("Maranata", "Maranata - Alessandro Vilas Boas.mp3");
        var errado = WorshipFmService.MatchScore("Maranata", "Cálice de Amor - David Cardoso.mp3");

        Assert.True(certo > errado);
    }

    [Fact]
    public void ChannelInfo_CriaWorshipFmSequencial()
    {
        var info = WorshipFmService.BuildChannelInfo("WORSHIP FM");

        Assert.Contains("Channel name=WORSHIP FM", info);
        Assert.Contains("Play mode=0", info);
        Assert.Contains("Track scan protection=0", info);
    }

    [Theory]
    [InlineData("https://youtube.com/playlist?list=PL123", true)]
    [InlineData("https://www.youtube.com/watch?v=abc", false)]
    [InlineData("https://example.com/playlist?list=PL123", false)]
    public void PlaylistUrl_ValidaSomentePlaylistDoYouTube(string url, bool expected)
    {
        Assert.Equal(expected, WorshipFmService.IsYouTubePlaylistUrl(url));
    }
}
