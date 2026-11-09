using System.Net;
using System.Net.Http.Json;
using Kultur.Api.Events;

namespace Kultur.Tests;

public class LearningTests
{
    static async Task<(KulturFactory Factory, HttpClient Client)> WithEventsAsync()
    {
        var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("a1", "Oplev Rane Willerslev", city: "Silkeborg", venue: "Jysk Musikteater", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]),
            TicketmasterJson.Event("a2", "Oplev Rane Willerslev", city: "København K", venue: "Bremen Teater", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]),
            TicketmasterJson.Event("b1", "Agnes Obel", venue: "Vega", performers: ["Agnes Obel"]),
            TicketmasterJson.Event("b2", "Agnes Obel – ekstra", venue: "DR Koncerthuset", performers: ["Agnes Obel"]),
            TicketmasterJson.Event("c1", "Andet band", venue: "Vega", performers: ["Andet band"]));
        await factory.ScanAsync();
        return (factory, await factory.LoggedInClientAsync());
    }

    static Task<HttpResponseMessage> React(HttpClient client, string id, string? reaction, string? reason = null) =>
        client.PutAsJsonAsync($"/api/events/ticketmaster:{id}/reaction", new { reaction, reason });

    static async Task<List<EventItem>> ForYou(HttpClient client) =>
        (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;

    [Fact]
    public async Task No_thanks_to_the_event_hides_every_date_also_ones_found_later()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        Assert.Equal(HttpStatusCode.NoContent, (await React(client, "a1", "NotInterested", "Event")).StatusCode);
        Assert.DoesNotContain(await ForYou(client), i => i.Event.Title == "Oplev Rane Willerslev");
        Assert.DoesNotContain((await client.GetAsync<EventList>("/api/events?view=new"))!.Events, i => i.Event.Title.Contains("Rane"));

        // A new date of the same show turns up later: hidden too.
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("a3", "Oplev Rane Willerslev", city: "Odense", venue: "Odeon", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]));
        await factory.ScanAsync();
        Assert.DoesNotContain(await ForYou(client), i => i.Event.Title == "Oplev Rane Willerslev");

        // Taking any date back brings the whole show back.
        await React(client, "a3", null);
        Assert.Equal(3, (await ForYou(client)).Count(i => i.Event.Title == "Oplev Rane Willerslev"));
    }

    [Fact]
    public async Task No_thanks_to_the_place_hides_only_that_date_and_counts_against_the_venue()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        await React(client, "a1", "NotInterested", "Place");
        var forYou = await ForYou(client);
        Assert.DoesNotContain(forYou, i => i.Event.Id == "ticketmaster:a1");
        Assert.Contains(forYou, i => i.Event.Id == "ticketmaster:a2");

        await React(client, "b1", "NotInterested", "Place"); // Vega
        Assert.True((await ForYou(client)).Single(i => i.Event.Id == "ticketmaster:c1").Score < 0); // also at Vega
    }

    [Fact]
    public async Task No_thanks_to_the_date_hides_only_that_date_and_teaches_nothing()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        await React(client, "b1", "NotInterested", "Date"); // Agnes Obel at Vega, busy that day
        var forYou = await ForYou(client);
        Assert.DoesNotContain(forYou, i => i.Event.Id == "ticketmaster:b1");
        Assert.Equal(0, forYou.Single(i => i.Event.Id == "ticketmaster:b2").Score); // her other date: untouched
        Assert.Equal(0, forYou.Single(i => i.Event.Id == "ticketmaster:c1").Score); // Vega: untouched
    }

    [Fact]
    public async Task Saving_lifts_the_performers_other_dates_and_taking_it_back_undoes_it()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        await React(client, "b1", "Saved");
        var forYou = await ForYou(client);
        var other = forYou.Single(i => i.Event.Id == "ticketmaster:b2");
        Assert.True(other.Score > 0);
        Assert.Contains(forYou[0].Event.Id, new[] { "ticketmaster:b1", "ticketmaster:b2" }); // Agnes Obel first

        await React(client, "b1", null);
        Assert.Equal(0, (await ForYou(client)).Single(i => i.Event.Id == "ticketmaster:b2").Score);
    }

    [Fact]
    public async Task A_reason_only_goes_with_no_thanks()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        Assert.Equal(HttpStatusCode.BadRequest, (await React(client, "b1", "Saved", "Place")).StatusCode);
    }

    [Fact]
    public void Series_falls_back_to_the_title_without_punctuation()
    {
        static CultureEvent E(string title, string? series = null) =>
            new("x:1", "x", title, Category.Talk, null, null, null, null, null, "DK", null, null, "https://x", null, null,
                DateTimeOffset.UnixEpoch, Series: series);
        Assert.Equal(Series.KeyOf(E("Oplev Rane Willerslev!")), Series.KeyOf(E("oplev  rane willerslev")));
        Assert.Equal("originaltalks:1091", Series.KeyOf(E("Glenn Bech", "originaltalks:1091")));
    }

    [Fact]
    public void Lessons_add_up_but_stay_within_a_rating()
    {
        var learning = new Learning();
        for (var i = 0; i < 5; i++) learning.Applied[$"e{i}"] = new() { ["Performer:x"] = 1 };
        Assert.Equal(2, learning.Signals()["Performer:x"]);
    }
}
