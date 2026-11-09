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
            TicketmasterJson.Event("c1", "Andet band", venue: "Vega", genre: "Jazz", performers: ["Andet band"]));
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
    public async Task The_place_is_not_a_reason_and_teaches_nothing()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        Assert.Equal(HttpStatusCode.BadRequest, (await React(client, "a1", "NotInterested", "Place")).StatusCode);

        await React(client, "b1", "Saved"); // Agnes Obel at Vega
        Assert.Equal(0, (await ForYou(client)).Single(i => i.Event.Id == "ticketmaster:c1").Score); // another band at Vega
    }

    [Fact]
    public async Task No_thanks_to_the_date_hides_everything_that_day_and_teaches_nothing()
    {
        using var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("d1", "Agnes Obel", date: "2099-05-01", venue: "Vega", performers: ["Agnes Obel"]),
            TicketmasterJson.Event("d2", "Jazz i kælderen", date: "2099-05-01", venue: "Jazzhouse"),
            TicketmasterJson.Event("d3", "Teater", date: "2099-05-01", venue: "Betty Nansen", segment: "Arts & Theatre", genre: "Theatre"),
            TicketmasterJson.Event("d4", "Agnes Obel – ekstra", date: "2099-05-02", venue: "Vega", performers: ["Agnes Obel"]));
        await factory.ScanAsync();
        var client = await factory.LoggedInClientAsync();

        await React(client, "d3", "Saved"); // already decided to go to this one
        await React(client, "d1", "NotInterested", "Date"); // busy 1 May
        var forYou = await ForYou(client);
        Assert.DoesNotContain(forYou, i => i.Event.Id is "ticketmaster:d1" or "ticketmaster:d2");
        Assert.Contains(forYou, i => i.Event.Id == "ticketmaster:d3"); // saved: stays
        Assert.Equal(0, forYou.Single(i => i.Event.Id == "ticketmaster:d4").Score); // nothing learned

        // An event found later on that day is hidden too…
        factory.Ticketmaster.Body = TicketmasterJson.Page(TicketmasterJson.Event("d5", "Ny koncert", date: "2099-05-01"));
        await factory.ScanAsync();
        Assert.DoesNotContain(await ForYou(client), i => i.Event.Id == "ticketmaster:d5");

        // …and taking back any of them frees the day.
        await React(client, "d2", null);
        Assert.Equal(3, (await ForYou(client)).Count(i => i.Event.Date == new DateOnly(2099, 5, 1) && i.Event.Id != "ticketmaster:d3"));
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
