using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Json;
using System.Net;
using Kultur.Api.Events;
using Kultur.Api.Scan;

namespace Kultur.Tests;

public class ScanTests
{
    [Fact]
    public async Task Scan_needs_the_key_or_a_login()
    {
        using var factory = new KulturFactory();
        var anonymous = await factory.CreateClient().PostAsync("/api/scan", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/scan");
        req.Headers.Add(ScanEndpoints.KeyHeader, "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(req)).StatusCode);

        var client = await factory.LoggedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/scan", null)).StatusCode);
    }

    [Fact]
    public async Task Scan_stores_ticketmaster_events_normalised()
    {
        using var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("a1", "Agnes Obel"),
            TicketmasterJson.Event("b2", "Danmark – Norge", city: "Herning", segment: "Sports", genre: "Handball", venue: "Jyske Bank Boxen"),
            TicketmasterJson.Event("c3", "Stand-up aften", segment: "Arts & Theatre", genre: "Comedy"));
        await factory.ScanAsync();

        var url = Assert.Single(factory.Ticketmaster.Requested);
        Assert.StartsWith(TicketmasterSource.Endpoint, url);
        Assert.Contains("countryCode=DK", url);

        var client = await factory.LoggedInClientAsync();
        var list = await client.GetAsync<EventList>("/api/events");
        Assert.Equal(3, list!.Events.Count);

        var agnes = list.Events.Single(i => i.Event.Title == "Agnes Obel");
        Assert.Equal("ticketmaster:a1", agnes.Event.Id);
        Assert.Equal(Category.Music, agnes.Event.Category);
        Assert.Equal(new DateOnly(2099, 3, 14), agnes.Event.Date);
        Assert.Equal("Vega", agnes.Event.Venue);
        Assert.Equal(345m, agnes.Event.PriceFrom);
        Assert.Equal("https://img/card.jpg", agnes.Event.ImageUrl);
        Assert.Equal(Zone.Copenhagen, agnes.Zone);

        var handball = list.Events.Single(i => i.Event.Title.StartsWith("Danmark"));
        Assert.Equal(Category.Sport, handball.Event.Category);
        Assert.Equal(Zone.Denmark, handball.Zone);
        Assert.Equal(Category.Comedy, list.Events.Single(i => i.Event.Title == "Stand-up aften").Event.Category);

        // For you: nearest zone first.
        Assert.Equal(Zone.Denmark, list.Events[^1].Zone);
    }

    [Fact]
    public async Task Rescan_keeps_first_seen_and_drops_past_events()
    {
        using var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("a1", "Agnes Obel"),
            TicketmasterJson.Event("old", "Sidste år", date: "2000-01-01"));
        await factory.ScanAsync();
        var client = await factory.LoggedInClientAsync();
        var first = (await client.GetAsync<EventList>("/api/events"))!.Events.Single();
        Assert.Equal("Agnes Obel", first.Event.Title);

        factory.Ticketmaster.Body = TicketmasterJson.Page(TicketmasterJson.Event("a1", "Agnes Obel – ekstrakoncert"));
        await factory.ScanAsync();
        var again = (await client.GetAsync<EventList>("/api/events"))!.Events.Single();
        Assert.Equal("Agnes Obel – ekstrakoncert", again.Event.Title);
        Assert.Equal(first.Event.FirstSeenUtc, again.Event.FirstSeenUtc);
    }

    [Fact]
    public async Task A_failing_source_is_reported_not_thrown()
    {
        using var factory = new KulturFactory();
        factory.Ticketmaster.Status = HttpStatusCode.Unauthorized;
        var client = await factory.LoggedInClientAsync();
        var res = await client.PostAsync("/api/scan", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = await res.Content.ReadAsync<ScanResult>();
        var tm = Assert.Single(result!.Sources);
        Assert.NotNull(tm.Error);
        Assert.DoesNotContain("tm-key", tm.Error); // the key is in the URL; it must not leak
    }

    [Fact]
    public async Task Ticketmasters_own_reason_is_shown()
    {
        using var factory = new KulturFactory();
        factory.Ticketmaster.Status = HttpStatusCode.Unauthorized;
        factory.Ticketmaster.Body = """{"fault":{"faultstring":"Invalid ApiKey","detail":{"errorcode":"oauth.v2.InvalidApiKey"}}}""";
        var client = await factory.LoggedInClientAsync();
        var result = await (await client.PostAsync("/api/scan", null)).Content.ReadAsync<ScanResult>();
        Assert.Equal("Ticketmaster svarede 401: Invalid ApiKey", Assert.Single(result!.Sources).Error);
    }

    [Theory]
    [InlineData("""{"errors":[{"code":"DIS1004","detail":"Resource not found"}]}""", "Resource not found")]
    [InlineData("<html>Bad gateway</html>", null)]
    [InlineData("", null)]
    public void Error_reason_is_read_from_either_format(string body, string? expected) =>
        Assert.Equal(expected, TicketmasterSource.Reason(body));

    [Fact]
    public async Task Key_is_trimmed()
    {
        using var factory = new KulturFactory();
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Ticketmaster:ApiKey", "  tm-key\n"));
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { password = KulturFactory.Password })).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/scan", null)).EnsureSuccessStatusCode();
        Assert.Contains("apikey=tm-key&", Assert.Single(factory.Ticketmaster.Requested));
    }
}
