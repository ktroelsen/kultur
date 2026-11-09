using System.Text.Json;
using Kultur.Api.Events;
using Kultur.Api.Scan;

namespace Kultur.Tests;

// Shaped like www.originaltalks.dk (Next.js): sitemap.xml, and show pages whose event objects sit
// as escaped JSON inside self.__next_f.push([1,"…"]) chunks.
public static class OriginalTalksPages
{
    public static string Sitemap(params string[] paths) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
        <url><loc>https://originaltalks.dk/</loc></url>
        <url><loc>https://originaltalks.dk/events</loc></url>
        {string.Concat(paths.Select(p => $"<url><loc>https://originaltalks.dk{p}</loc></url>\n"))}
        <url><loc>https://evil.example/events/x-1</loc></url>
        </urlset>
        """;

    public static string Event(long id, string title, string speaker, long unixFrom, string venue = "Dagmar Teatret",
        string city = "København V", int priceCents = 33900, string external = "", bool online = false,
        string groupSlug = "simon-hoegmark-vi-er-natur-1015", string slug = "koebenhavn-2026-11-11") => $$"""
        {"id":{{id}},"event_group_id":1015,"title":"{{title}}","sub_title":"En rejse","speaker":"{{speaker}}",
         "category":{"id":6,"name":"I godt selskab"},"description_html":"$74","unix_time_from":{{unixFrom}},
         "unix_time_to":{{unixFrom + 7200}},"lowest_price_cents":{{priceCents}},"currency":"DKK","sold_out":false,
         "is_online":{{(online ? "true" : "false")}},"is_hidden":false,"external_ticket_link":"{{external}}","url_slug":"{{slug}}",
         "images":{"640x360":"https://static.tikko.dk/x.png"},
         "venue":{"id":647,"name":"{{venue}}","address":"Jernbanegade 2","zip_code":"1608","city":"{{city}}","area_city":"København","country":"dk"},
         "event_group_summary":[{"event_id":{{id}},"unix_time_from":{{unixFrom}},"url_slug":"{{slug}}","venue_info":"x"}],
         "event_group_info":{"id":1015,"title":"{{title}}","speaker":"{{speaker}}","url_slug":"{{groupSlug}}"} }
        """;

    // The payload is split across several chunks, mid-object, as Next.js does.
    public static string ShowPage(params string[] events)
    {
        var payload = "8:[\"$\",\"div\",null,{\"events\":[" + string.Join(",", events.Select(e => e.Replace("\n", ""))) + "]}]\n";
        var half = payload.Length / 2;
        string Chunk(string s) => $"<script>self.__next_f.push([1,{JsonSerializer.Serialize(s)}])</script>";
        return $"<html><head><script>self.__next_f.push([0])</script></head><body><h1 class=\"font-medium\">x</h1>" +
               Chunk(payload[..half]) + Chunk(payload[half..]) + "</body></html>";
    }
}

public class OriginalTalksTests
{
    const long Nov11At1830 = 1794418200; // 2026-11-11 17:30 UTC = 18:30 in Copenhagen

    [Fact]
    public void Show_paths_come_from_the_sitemap_once_each_and_only_from_originaltalks()
    {
        var paths = OriginalTalksSource.ShowPaths(OriginalTalksPages.Sitemap(
            "/events/glenn-bech-mutti-live-1091",
            "/events/glenn-bech-mutti-live-1091/11539-horsens-2027-01-26",
            "/events/svend-brinkmann-meningen-med-det-hele-936/11526-middelfart-2026-11-19"));
        Assert.Equal(["/events/glenn-bech-mutti-live-1091", "/events/svend-brinkmann-meningen-med-det-hele-936"], paths);
    }

    [Fact]
    public void Events_are_read_from_the_next_payload()
    {
        var html = OriginalTalksPages.ShowPage(
            OriginalTalksPages.Event(11306, "Vi er natur", "Simon Høegmark", Nov11At1830),
            OriginalTalksPages.Event(11307, "Vi er natur", "Simon Høegmark", Nov11At1830 + 86400, venue: "Musikhuset", city: "Aarhus C",
                external: "https://billet.musikhuset.dk/x", slug: "aarhus-2026-11-12"));
        var events = OriginalTalksSource.Parse(html, DateTimeOffset.UnixEpoch).OrderBy(e => e.Id).ToList();

        Assert.Equal(2, events.Count);
        var cph = events[0];
        Assert.Equal("originaltalks:11306", cph.Id);
        Assert.Equal(Category.Talk, cph.Category);
        Assert.Equal("I godt selskab", cph.Genre);
        Assert.Equal(new DateOnly(2026, 11, 11), cph.Date);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Nov11At1830), cph.Start);
        Assert.Equal("Dagmar Teatret", cph.Venue);
        Assert.Equal("København V", cph.City);
        Assert.Equal("DK", cph.CountryCode);
        Assert.Equal(339m, cph.PriceFrom);
        Assert.Equal("https://www.originaltalks.dk/events/simon-hoegmark-vi-er-natur-1015/11306-koebenhavn-2026-11-11", cph.Url);
        Assert.Equal(["Simon Høegmark"], cph.Performers!);
        Assert.Null(cph.ImageUrl); // their pictures stay theirs

        Assert.Equal("https://billet.musikhuset.dk/x", events[1].Url); // sold by the venue
    }

    [Fact]
    public void Ticketmaster_sold_online_and_hidden_dates_are_left_out()
    {
        var html = OriginalTalksPages.ShowPage(
            OriginalTalksPages.Event(1, "Via Ticketmaster", "A", Nov11At1830, external: "https://www.ticketmaster.dk/event/945478811"),
            OriginalTalksPages.Event(2, "Online", "B", Nov11At1830, online: true),
            OriginalTalksPages.Event(3, "Med", "C", Nov11At1830));
        Assert.Equal(["originaltalks:3"], OriginalTalksSource.Parse(html, DateTimeOffset.UnixEpoch).Select(e => e.Id));
    }

    [Theory]
    [InlineData("Glenn Bech og Dorte Bech Møldrup", new[] { "Glenn Bech", "Dorte Bech Møldrup" })]
    [InlineData("Svend Brinkmann", new[] { "Svend Brinkmann" })]
    [InlineData("A, B & C", new[] { "A", "B", "C" })]
    public void Speakers_are_split(string speaker, string[] expected) =>
        Assert.Equal(expected, OriginalTalksSource.Speakers(speaker)!);

    [Fact]
    public async Task Scan_fetches_the_sitemap_then_each_show_once()
    {
        using var factory = new KulturFactory();
        var site = factory.OriginalTalks;
        site.Pages[OriginalTalksSource.SitemapUrl] = OriginalTalksPages.Sitemap(
            "/events/simon-hoegmark-vi-er-natur-1015",
            "/events/simon-hoegmark-vi-er-natur-1015/11306-koebenhavn-2026-11-11",
            "/events/glenn-bech-mutti-live-1091/11539-horsens-2027-01-26",
            "/events/findes-ikke-1/1-x-2027-01-01");
        site.Pages[OriginalTalksSource.Site + "/events/simon-hoegmark-vi-er-natur-1015"] =
            OriginalTalksPages.ShowPage(OriginalTalksPages.Event(11306, "Vi er natur", "Simon Høegmark", 4102444800)); // 2100
        site.Pages[OriginalTalksSource.Site + "/events/glenn-bech-mutti-live-1091"] =
            OriginalTalksPages.ShowPage(OriginalTalksPages.Event(11539, "Glenn Bech og Mutti LIVE", "Glenn Bech og Dorte Bech Møldrup", 4102531200,
                venue: "Horsens Ny Teater", city: "Horsens", groupSlug: "glenn-bech-mutti-live-1091", slug: "horsens-2100-01-02"));

        var client = await factory.LoggedInClientAsync();
        var result = await (await client.PostAsync("/api/scan", null)).Content.ReadAsync<ScanResult>();
        var ot = result!.Sources.Single(s => s.Source == "originaltalks");
        Assert.Null(ot.Error); // a missing show page is skipped, not fatal
        Assert.Equal(2, ot.Fetched);
        Assert.Equal(4, site.Requested.Count); // sitemap + 3 shows
        Assert.All(site.Requested, u => Assert.StartsWith("https://www.originaltalks.dk/", u));

        var events = (await client.GetAsync<EventList>("/api/events"))!.Events;
        Assert.Contains(events, i => i.Event.Title == "Glenn Bech og Mutti LIVE" && i.Zone == Zone.Denmark);
    }
}
