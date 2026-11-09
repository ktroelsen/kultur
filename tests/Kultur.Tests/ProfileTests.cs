using System.Net;
using System.Net.Http.Json;
using Kultur.Api.Profile;

namespace Kultur.Tests;

public class ProfileTests(KulturFactory factory) : IClassFixture<KulturFactory>
{
    [Fact]
    public async Task Profile_is_saved_and_read_back()
    {
        var client = await factory.LoggedInClientAsync();
        var res = await client.PutAsJsonAsync("/api/profile", new { about = "  Singer-songwriter og ny dansk dramatik ", sport = "Vejle Boldklub, landsholdene" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var profile = await client.GetAsync<TasteProfile>("/api/profile");
        Assert.Equal("Singer-songwriter og ny dansk dramatik", profile!.About);
        Assert.Equal("Vejle Boldklub, landsholdene", profile.Sport);
    }

    [Fact]
    public async Task Too_long_profile_is_rejected()
    {
        var client = await factory.LoggedInClientAsync();
        var res = await client.PutAsJsonAsync("/api/profile", new { about = new string('x', ProfileEndpoints.MaxLength + 1), sport = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
