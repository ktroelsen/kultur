using System.Net;
using System.Net.Http.Json;

namespace Kultur.Tests;

public class ApiTests(KulturFactory factory) : IClassFixture<KulturFactory>
{
    [Fact]
    public async Task Health_is_public()
    {
        var res = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/events")]
    [InlineData("/api/profile")]
    [InlineData("/api/auth/me")]
    public async Task Api_needs_login(string path)
    {
        var res = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_gives_a_cookie_that_works()
    {
        var client = await factory.LoggedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_is_404()
    {
        var client = await factory.LoggedInClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/nope")).StatusCode);
    }
}
