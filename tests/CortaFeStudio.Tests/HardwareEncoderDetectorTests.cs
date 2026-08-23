using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class HardwareEncoderDetectorTests
{
    [Fact]
    public void Profiles_PriorizaNvencQuickSyncEAmf()
    {
        Assert.Equal(["h264_nvenc", "h264_qsv", "h264_amf"], HardwareEncoderDetector.Profiles.Select(item => item.Codec));
        Assert.All(HardwareEncoderDetector.Profiles, item => Assert.True(item.HardwareAccelerated));
    }

    [Fact]
    public void Cpu_UsaLibx264ComoFallbackSeguro()
    {
        var profile = HardwareEncoderDetector.Cpu;
        Assert.Equal("libx264", profile.Codec);
        Assert.False(profile.HardwareAccelerated);
        Assert.Contains("veryfast", profile.Arguments);
    }

    [Theory]
    [InlineData("h264_nvenc", "p4")]
    [InlineData("h264_qsv", "faster")]
    [InlineData("h264_amf", "balanced")]
    public void Profiles_UsamPresetCompativel(string codec, string expected)
    {
        Assert.Contains(expected, HardwareEncoderDetector.Profiles.Single(item => item.Codec == codec).Arguments);
    }
}
