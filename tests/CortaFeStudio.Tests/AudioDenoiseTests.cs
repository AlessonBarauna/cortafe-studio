using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class AudioDenoiseTests
{
    [Fact]
    public void Filter_SemModelo_NaoAlteraACadeia() => Assert.Equal("", AudioDenoise.Filter(null, "pregacao"));

    [Fact]
    public void Filter_Louvor_NuncaAplicaReducaoDeRuido() => Assert.Equal("", AudioDenoise.Filter("/app/tools/models/rnnoise.rnnn", "louvor"));

    [Fact]
    public void Filter_ComModelo_EscapaOCaminhoParaOFfmpeg()
    {
        var filter = AudioDenoise.Filter(@"C:\app\rnnoise.rnnn", "pregacao");
        Assert.StartsWith("arnndn=m='C\\:/app/rnnoise.rnnn'", filter);
        Assert.EndsWith(",", filter);
    }
}
