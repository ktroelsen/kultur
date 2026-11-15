using System.Text.Json;
using Kultur.Api.Events;
using Kultur.Api.Scan;

namespace Kultur.Tests;

public class VenueTests
{
    static string Programme(params object[] events) => "```json\n" + JsonSerializer.Serialize(new { events }) + "\n```";

    static object Aida(string date, string time = "21:00", string? url = "https://www.arena.it/en/aida/") => new
    {
        title = "Aida", series = "Aida", date, time, hall = (string?)null, category = "Music", genre = "Opera",
        performers = new[] { "Anna Netrebko" }, priceFrom = 30.0, currency = "EUR", url,
    };

    [Fact]
    public async Task Venues_are_read_with_web_fetch_on_their_own_site_and_become_events()
    {
        var (factory, app, claude) = RunsTests.WithClaude();
        using var _ = factory;
        using var __ = app;
        claude.AnswerPrompt = prompt =>
            prompt.Contains("Arena di Verona") ? Programme(Aida("2099-06-20"), Aida("2099-07-04", url: "https://evil.example/x"), Aida("2000-01-01"))
            : prompt.Contains("Planetariet") ? Programme(new
            {
                title = "Foredrag: Sorte huller", series = (string?)null, date = "2099-02-03", time = (string?)null, hall = "Kuplen",
                category = "Talk", genre = "Foredrag", performers = Array.Empty<string>(), priceFrom = (double?)null, currency = (string?)null,
                url = "https://www.planetariet.dk/sorte-huller",
            })
            : """{"editions": []}""";

        var client = await RunsTests.LoginAsync(app);
        var result = await (await client.PostAsync("/api/scan", null)).Content.ReadAsync<ScanResult>();
        var venues = result!.Sources.Single(s => s.Source == "steder");
        Assert.Null(venues.Error);
        Assert.Equal(3, venues.Fetched); // the past Aida is left out

        var request = claude.Requests.Single(r => r["messages"]![0]!["content"]!.ToString().Contains("Arena di Verona"));
        var tool = request["tools"]![0]!;
        Assert.Equal("web_fetch_20260209", tool["type"]!.GetValue<string>());
        Assert.Equal(["www.arena.it"], tool["allowed_domains"]!.AsArray().Select(d => d!.GetValue<string>()));

        var events = (await client.GetAsync<EventList>("/api/events"))!.Events;
        var aida = events.Single(i => i.Event.Id == "venue:arena-verona:aida-20990620-2100");
        Assert.Equal(Zone.Abroad, aida.Zone);
        Assert.Equal(Category.Music, aida.Event.Category);
        Assert.Equal("Opera", aida.Event.Genre);
        Assert.Equal(new DateTimeOffset(2099, 6, 20, 21, 0, 0, TimeSpan.FromHours(2)), aida.Event.Start); // CEST
        Assert.Equal(30m, aida.Event.PriceFrom);
        Assert.Equal("EUR", aida.Event.Currency);
        Assert.Equal("https://www.arena.it/en/aida/", aida.Event.Url);
        Assert.Equal("venue:arena-verona:aida", aida.Event.Series);
        Assert.Equal("https://www.arena.it/", events.Single(i => i.Event.Id == "venue:arena-verona:aida-20990704-2100").Event.Url); // not the venue's site

        var talk = events.Single(i => i.Event.Source == "planetariet");
        Assert.Equal(Category.Talk, talk.Event.Category);
        Assert.Equal("Planetariet – Kuplen", talk.Event.Venue);
        Assert.Equal(Zone.Copenhagen, talk.Zone);
        Assert.Null(talk.Event.Start);
    }

    [Fact]
    public async Task A_new_reading_replaces_the_venues_programme_but_only_once_a_week()
    {
        var (factory, app, claude) = RunsTests.WithClaude();
        using var _ = factory;
        using var __ = app;
        var programme = Programme(Aida("2099-06-20"), Aida("2099-06-27"));
        claude.AnswerPrompt = prompt => prompt.Contains("Arena di Verona") ? programme : """{"events": []}""";
        var client = await RunsTests.LoginAsync(app);
        await client.PostAsync("/api/scan", null);
        Assert.Equal(2, (await client.GetAsync<EventList>("/api/events"))!.Events.Count(i => i.Event.Source == "arena-verona"));

        // Within the week: not read again.
        var calls = claude.Requests.Count(r => r["messages"]![0]!["content"]!.ToString().Contains("Arena di Verona"));
        await client.PostAsync("/api/scan", null);
        Assert.Equal(calls, claude.Requests.Count(r => r["messages"]![0]!["content"]!.ToString().Contains("Arena di Verona")));

        // A week later, with one date cancelled: it's gone.
        File.Delete(Path.Combine(factory.DataDir, "venues.json"));
        programme = Programme(Aida("2099-06-20"));
        await client.PostAsync("/api/scan", null);
        Assert.Equal(["venue:arena-verona:aida-20990620-2100"],
            (await client.GetAsync<EventList>("/api/events"))!.Events.Where(i => i.Event.Source == "arena-verona").Select(i => i.Event.Id));
    }

    [Fact]
    public void Events_need_a_title_and_a_date()
    {
        var venue = new VenuesOptions().Venues[0];
        var now = DateTimeOffset.UnixEpoch;
        Assert.Null(VenueSource.ToEvent(venue, new VenueEvent(null, null, "2099-01-01", null, null, null, null, null, null, null, null), now));
        Assert.Null(VenueSource.ToEvent(venue, new VenueEvent("X", null, "snart", null, null, null, null, null, null, null, null), now));
        Assert.Equal(Category.Other, VenueSource.ToEvent(venue, new VenueEvent("X", null, "2099-01-01", null, null, "Opera?", null, null, null, null, null), now)!.Category);
    }

    [Theory]
    [InlineData("Ikke noget program her.")]
    [InlineData("{\"x\": 1}")]
    public void A_bad_answer_is_an_error(string text) =>
        Assert.Throws<HttpRequestException>(() => VenueSource.ParseEvents(text));
}
