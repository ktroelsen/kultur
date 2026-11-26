using System.Net;
using System.Net.Http.Json;
using Kultur.Api.Groups;
using Kultur.Api.Integration;

namespace Kultur.Tests;

// kaspert.dk's "I dag": GET /api/integration/summary behind its own key, for one configured user.
public class SummaryTests
{
    const string Key = "test-summary-key";
    const string Path = "/api/integration/summary";
    static readonly DateTimeOffset Now = new(2099, 3, 1, 10, 0, 0, TimeSpan.Zero); // 11:00 in Copenhagen

    static KulturFactory Factory(string? user = "owner")
    {
        var factory = new KulturFactory { Clock = new FixedClock(Now) };
        factory.Settings["Integrations:Kaspert:ApiKey"] = Key;
        if (user is not null) factory.Settings["Integrations:Kaspert:User"] = user;
        return factory;
    }

    static HttpRequestMessage Get(string path, string? key = Key, HttpMethod? method = null)
    {
        var req = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (key is not null) req.Headers.Add(ApiKeyHandler.Header, key);
        return req;
    }

    static async Task<Summary> SummaryAsync(KulturFactory factory)
    {
        var res = await factory.CreateClient().SendAsync(Get(Path));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadAsync<Summary>())!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData("")]
    public async Task Without_the_right_key_it_is_401(string? key)
    {
        using var factory = Factory();
        var res = await factory.CreateClient().SendAsync(Get(Path, key));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Without_a_configured_key_nothing_opens_it()
    {
        using var factory = Factory();
        factory.Settings["Integrations:Kaspert:ApiKey"] = "";
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(Get(Path, ""))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(Get(Path, "anything"))).StatusCode);
    }

    [Fact]
    public async Task The_right_key_gives_an_empty_summary_without_events()
    {
        using var factory = Factory();
        var summary = await SummaryAsync(factory);
        Assert.Empty(summary.Items);
        Assert.Empty(summary.Notes);
        Assert.Null(summary.Badge);
    }

    [Theory]
    [InlineData("GET", "/api/events")]
    [InlineData("GET", "/api/groups?view=inbox")]
    [InlineData("GET", "/api/profile")]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/taste/next")]
    [InlineData("POST", "/api/scan")]
    [InlineData("POST", "/api/events/seen")]
    public async Task The_key_opens_nothing_else(string method, string path)
    {
        using var factory = Factory();
        var res = await factory.CreateClient().SendAsync(Get(path, Key, new HttpMethod(method)));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task The_login_cookie_does_not_open_it()
    {
        using var factory = Factory();
        var client = await factory.LoggedInClientAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Path)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kasper")]
    public async Task Without_a_known_user_it_is_503(string? user)
    {
        using var factory = Factory(user);
        var res = await factory.CreateClient().SendAsync(Get(Path));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Lists_dates_saved_or_chosen_from_today_through_a_week_and_ticket_sales()
    {
        using var factory = Factory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("today", "Aftenkoncert", date: "2099-03-01", venue: "Vega", city: "København V"),
            TicketmasterJson.Event("plus3", "Teater på torsdag", date: "2099-03-04", venue: "Betty Nansen", city: "Frederiksberg"),
            TicketmasterJson.Event("plus10", "Langt ude", date: "2099-03-11"),
            TicketmasterJson.Event("declined", "Nej tak-koncert", date: "2099-03-03"),
            TicketmasterJson.Event("r1", "Oplev Rane Willerslev", date: "2099-03-02", venue: "Bremen Teater", city: "København K"),
            TicketmasterJson.Event("r2", "Oplev Rane Willerslev", date: "2099-03-20", venue: "Odeon", city: "Odense"),
            TicketmasterJson.Event("agnes", "Agnes Obel", date: "2099-06-01", venue: "Royal Arena", city: "København S", saleStart: "2099-03-05T09:00:00Z"),
            TicketmasterJson.Event("other", "Uafklaret", date: "2099-03-02"));
        await factory.ScanAsync();
        var client = await factory.LoggedInClientAsync();
        foreach (var id in new[] { "today", "plus10", "agnes" })
            await client.PutAsJsonAsync($"/api/events/ticketmaster:{id}/reaction", new { reaction = "Saved" });
        await client.PutAsJsonAsync("/api/events/ticketmaster:plus3/reaction", new { reaction = "Maybe" });
        await client.PutAsJsonAsync("/api/events/ticketmaster:declined/reaction", new { reaction = "NotInterested" });
        await client.PutAsJsonAsync("/api/groups/choice", new { key = "title:oplev rane willerslev", choice = "Chosen" });

        var summary = await SummaryAsync(factory);

        Assert.Equal(
            [
                new SummaryItem("Aftenkoncert", "Vega, København V", "2099-03-01", "20:00", "https://kultur.kaspert.dk/group?key=title%3Aaftenkoncert"),
                new SummaryItem("Oplev Rane Willerslev", "Bremen Teater, København K", "2099-03-02", "20:00", "https://kultur.kaspert.dk/group?key=title%3Aoplev%20rane%20willerslev"),
                new SummaryItem("Teater på torsdag", "Måske · Betty Nansen, Frederiksberg", "2099-03-04", "20:00", "https://kultur.kaspert.dk/group?key=title%3Ateater%20p%C3%A5%20torsdag"),
                new SummaryItem("Billetsalget åbner: Agnes Obel", "Royal Arena, København S", "2099-03-05", "10:00", "https://kultur.kaspert.dk/group?key=title%3Aagnes%20obel"),
            ],
            summary.Items);
        Assert.Empty(summary.Notes);

        // The badge counts the new groups in "Siden sidst", as the app itself shows them.
        var inbox = (await client.GetAsync<GroupList>("/api/groups?view=inbox"))!.Groups;
        Assert.Contains(inbox, g => g.Key == "title:uafklaret");
        var fresh = inbox.Count(g => g.IsNew);
        Assert.True(fresh > 0);
        Assert.Equal($"{fresh} nye", summary.Badge);
    }

    [Fact]
    public async Task Turned_down_dates_and_dismissed_series_never_show()
    {
        using var factory = Factory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("r1", "Oplev Rane Willerslev", date: "2099-03-02"),
            TicketmasterJson.Event("r2", "Oplev Rane Willerslev", date: "2099-03-03"),
            TicketmasterJson.Event("gone", "Nej tak alle", date: "2099-03-02", saleStart: "2099-03-02T09:00:00Z"),
            TicketmasterJson.Event("gone2", "Nej tak alle", date: "2099-03-05"));
        await factory.ScanAsync();
        var client = await factory.LoggedInClientAsync();
        await client.PutAsJsonAsync("/api/groups/choice", new { key = "title:oplev rane willerslev", choice = "Chosen" });
        await client.PutAsJsonAsync("/api/events/ticketmaster:r1/reaction", new { reaction = "NotInterested" });
        await client.PutAsJsonAsync("/api/events/ticketmaster:gone2/reaction", new { reaction = "Saved" });
        await client.PutAsJsonAsync("/api/groups/choice", new { key = "title:nej tak alle", choice = "Dismissed" });

        var summary = await SummaryAsync(factory);
        var item = Assert.Single(summary.Items);
        Assert.Equal(("Oplev Rane Willerslev", "2099-03-03"), (item.Title, item.Date));
        Assert.Null(summary.Badge); // nothing left undecided
    }
}
