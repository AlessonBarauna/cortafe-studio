using System.Net;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class RemoteAccessPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_SempreEPermitido(string ip) => Assert.True(RemoteAccessPolicy.IsAllowed(IPAddress.Parse(ip), false));

    [Theory]
    [InlineData("172.18.0.1")]
    [InlineData("192.168.0.20")]
    [InlineData("10.8.0.2")]
    [InlineData("100.100.1.5")]
    [InlineData("::ffff:172.17.0.1")]
    public void RedePrivada_SoComOptIn(string ip)
    {
        var address = IPAddress.Parse(ip);
        Assert.False(RemoteAccessPolicy.IsAllowed(address, false));
        Assert.True(RemoteAccessPolicy.IsAllowed(address, true));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2001:4860:4860::8888")]
    public void IpPublico_NuncaEPermitido(string ip) => Assert.False(RemoteAccessPolicy.IsAllowed(IPAddress.Parse(ip), true));
}
