using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CortaFeStudio.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory) { _factory = factory; _client = factory.CreateClient(); }

    [Fact]
    public async Task Health_RespondeComSucesso() => Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/health")).StatusCode);

    [Fact]
    public async Task ProjetoUrl_RejeitaDominioNaoPermitido()
    {
        var response = await _client.PostAsJsonAsync("/api/projects/url", new { url = "https://example.com/video" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Favicon_PossuiRotaDeCompatibilidade()
    {
        using var noRedirect = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await noRedirect.GetAsync("/favicon.ico");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/favicon.svg", response.Headers.Location?.ToString());
    }
}
