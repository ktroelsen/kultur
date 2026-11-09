using System.Net;
using System.Net.Http.Json;
using Kultur.Api.Events;

namespace Kultur.Tests;

public class EventTests
{
    static async Task<(KulturFactory Factory, HttpClient Client)> WithEventsAsync()
    {
        var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("a1", "Agnes Obel"),
            TicketmasterJson.Event("b2", "Vejle – FCK", city: "Vejle", segment: "Sports", genre: "Football"),
            TicketmasterJson.Event("c3", "Musical"));
        await factory.ScanAsync();
        return (factory, await factory.LoggedInClientAsync());
    }

    [Fact]
    public async Task Reactions_sort_events_into_saved_and_away_from_for_you()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/events/ticketmaster:a1/reaction", new { reaction = "Saved" })).StatusCode);
        await client.PutAsJsonAsync("/api/events/ticketmaster:b2/reaction", new { reaction = "Maybe" });
        await client.PutAsJsonAsync("/api/events/ticketmaster:c3/reaction", new { reaction = "NotInterested" });

        var saved = (await client.GetAsync<EventList>("/api/events?view=saved"))!.Events;
        Assert.Equal(["Agnes Obel", "Vejle – FCK"], saved.Select(i => i.Event.Title).Order());
        Assert.Equal(Reaction.Saved, saved.Single(i => i.Event.Title == "Agnes Obel").Reaction);

        var forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.DoesNotContain(forYou, i => i.Event.Title == "Musical");

        // Taking a reaction back.
        await client.PutAsJsonAsync("/api/events/ticketmaster:c3/reaction", new { reaction = (string?)null });
        forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.Contains(forYou, i => i.Event.Title == "Musical");
    }

    [Fact]
    public async Task Reaction_to_unknown_event_is_404()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        var res = await client.PutAsJsonAsync("/api/events/ticketmaster:nope/reaction", new { reaction = "Saved" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Since_last_time_shows_only_what_arrived_after_the_last_visit()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        Assert.Equal(3, (await client.GetAsync<EventList>("/api/events?view=new"))!.Events.Count);
        await client.PostAsync("/api/events/seen", null);
        Assert.Empty((await client.GetAsync<EventList>("/api/events?view=new"))!.Events);

        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("a1", "Agnes Obel"),
            TicketmasterJson.Event("d4", "Danmark – Sverige", segment: "Sports", genre: "Badminton"));
        await factory.ScanAsync();

        var fresh = (await client.GetAsync<EventList>("/api/events?view=new"))!;
        Assert.NotNull(fresh.LastVisitUtc);
        Assert.Equal("Danmark – Sverige", Assert.Single(fresh.Events).Event.Title);
    }
}
