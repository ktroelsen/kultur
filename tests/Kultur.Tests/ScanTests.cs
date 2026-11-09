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
    public async Task Searches_month_by_month_a_year_ahead()
    {
        using var factory = new KulturFactory();
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Ticketmaster:Months", "12"));
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { password = KulturFactory.Password })).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/scan", null)).EnsureSuccessStatusCode();

        var windows = factory.Ticketmaster.Requested.Select(Window).ToList();
        Assert.Equal(12, windows.Count);
        // Back to back: each month starts where the previous one ended, from the 1st of a month.
        for (var i = 1; i < windows.Count; i++)
        {
            Assert.Equal(windows[i - 1].End, windows[i].Start);
            Assert.Equal(1, windows[i].Start.Day);
        }
        Assert.True(windows[^1].End - windows[0].Start > TimeSpan.FromDays(330));
    }

    [Fact]
    public async Task A_month_with_more_than_1000_events_is_split_and_events_are_not_doubled()
    {
        using var factory = new KulturFactory();
        // Over 1000 events while the window is longer than a week; otherwise one page of two events,
        // one of which shows up in every window.
        factory.Ticketmaster.Respond = uri =>
        {
            var (start, end) = Window(uri.ToString());
            return end - start > TimeSpan.FromDays(7)
                ? TicketmasterJson.PageOf(1500)
                : TicketmasterJson.Page(
                    TicketmasterJson.Event($"w{start:MMddHHmmss}", $"Koncert {start:dd/MM HH:mm}"),
                    TicketmasterJson.Event("same", "Går igen"));
        };
        await factory.ScanAsync();

        var windows = factory.Ticketmaster.Requested.Select(Window).ToList();
        var fetched = windows.Where(w => w.End - w.Start <= TimeSpan.FromDays(7)).ToList();
        Assert.True(fetched.Count >= 2, "the month should have been split into smaller windows");
        Assert.Equal(fetched.Count, fetched.Select(w => w.Start).Distinct().Count());

        var client = await factory.LoggedInClientAsync();
        var events = (await client.GetAsync<EventList>("/api/events"))!.Events;
        Assert.Single(events, e => e.Event.Id == "ticketmaster:same");
        Assert.Equal(fetched.Count + 1, events.Count);
    }

    static (DateTimeOffset Start, DateTimeOffset End) Window(string url)
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        return (DateTimeOffset.Parse(query["startDateTime"]!, System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(query["endDateTime"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Music", "Rock", Category.Music)]
    [InlineData("Arts & Theatre", "Comedy", Category.Comedy)]
    [InlineData("Arts & Theatre", "Theatre", Category.Theatre)]
    [InlineData("Miscellaneous", "Lecture/Seminar", Category.Talk)]
    [InlineData("Arts & Theatre", "Spoken Word", Category.Talk)]
    [InlineData("Sports", "Running", Category.Running)]
    [InlineData("Sports", "Marathon", Category.Running)]
    [InlineData("Sports", "Football", Category.Sport)]
    [InlineData("Miscellaneous", null, Category.Other)]
    public void Ticketmaster_segment_and_genre_give_the_category(string segment, string? genre, Category expected) =>
        Assert.Equal(expected, TicketmasterSource.CategoryOf(segment, genre));

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
        var tm = result!.Sources.Single(s => s.Source == "ticketmaster");
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
        Assert.Equal("Ticketmaster svarede 401: Invalid ApiKey", result!.Sources.Single(s => s.Source == "ticketmaster").Error);
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
