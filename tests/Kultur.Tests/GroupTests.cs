using System.Net;
using System.Net.Http.Json;
using Kultur.Api.Events;
using Kultur.Api.Groups;

namespace Kultur.Tests;

public class GroupTests
{
    const string Rane = "title:oplev rane willerslev";

    static async Task<(KulturFactory Factory, HttpClient Client)> WithEventsAsync()
    {
        var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("r1", "Oplev Rane Willerslev", date: "2099-03-02", city: "Silkeborg", venue: "Jysk Musikteater", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]),
            TicketmasterJson.Event("r2", "Oplev Rane Willerslev", date: "2099-01-15", city: "København K", venue: "Bremen Teater", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]),
            TicketmasterJson.Event("r3", "Oplev Rane Willerslev", date: "2099-02-10", city: "Odense", venue: "Odeon", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]),
            TicketmasterJson.Event("a1", "Agnes Obel", date: "2099-04-01", venue: "Vega", genre: "Pop", performers: ["Agnes Obel"]));
        await factory.ScanAsync();
        return (factory, await factory.LoggedInClientAsync());
    }

    static Task<GroupList?> Groups(HttpClient client, string view) => client.GetAsync<GroupList>($"/api/groups?view={view}");

    static Task<HttpResponseMessage> Choose(HttpClient client, string key, string? choice) =>
        client.PutAsJsonAsync("/api/groups/choice", new { key, choice });

    [Fact]
    public async Task Dates_of_one_event_are_one_group_from_first_to_last()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        var inbox = (await Groups(client, "inbox"))!.Groups;
        Assert.Equal(2, inbox.Count);
        var rane = inbox.Single(g => g.Key == Rane);
        Assert.Equal(3, rane.Dates);
        Assert.Equal(new DateOnly(2099, 1, 15), rane.From);
        Assert.Equal(new DateOnly(2099, 3, 2), rane.To);
        Assert.Equal(["København K", "Odense", "Silkeborg"], rane.Cities);
        Assert.Equal(Category.Talk, rane.Category);
        Assert.Equal(Zone.Copenhagen, rane.Zone);
        Assert.True(rane.IsNew);
        Assert.False(rane.Chosen);
    }

    [Fact]
    public async Task Choosing_moves_a_group_to_for_you_and_back()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        Assert.Equal(HttpStatusCode.NoContent, (await Choose(client, Rane, "Chosen")).StatusCode);
        Assert.DoesNotContain((await Groups(client, "inbox"))!.Groups, g => g.Key == Rane);
        var chosen = Assert.Single((await Groups(client, "chosen"))!.Groups);
        Assert.Equal(Rane, chosen.Key);
        Assert.True(chosen.Chosen);

        await Choose(client, Rane, null);
        Assert.Empty((await Groups(client, "chosen"))!.Groups);
        Assert.Contains((await Groups(client, "inbox"))!.Groups, g => g.Key == Rane);
    }

    [Fact]
    public async Task Removing_dates_of_a_chosen_group_narrows_it_and_the_detail_still_lists_them()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        await Choose(client, Rane, "Chosen");

        await client.PutAsJsonAsync("/api/events/ticketmaster:r2/reaction", new { reaction = "NotInterested" }); // 15 Jan
        var group = Assert.Single((await Groups(client, "chosen"))!.Groups);
        Assert.Equal(2, group.Dates);
        Assert.Equal(new DateOnly(2099, 2, 10), group.From);

        var detail = (await client.GetAsync<GroupDetail>($"/api/groups/events?key={Uri.EscapeDataString(Rane)}"))!;
        Assert.True(detail.Chosen);
        Assert.Equal(["ticketmaster:r2", "ticketmaster:r3", "ticketmaster:r1"], detail.Events.Select(i => i.Event.Id)); // by date
        Assert.Equal(Reaction.NotInterested, detail.Events[0].Reaction);
        Assert.Equal(2, detail.Group!.Dates);
    }

    [Fact]
    public async Task No_thanks_to_a_group_hides_all_its_dates_and_undo_brings_them_back()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        await Choose(client, Rane, "Dismissed");
        Assert.DoesNotContain((await Groups(client, "inbox"))!.Groups, g => g.Key == Rane);
        var detail = (await client.GetAsync<GroupDetail>($"/api/groups/events?key={Uri.EscapeDataString(Rane)}"))!;
        Assert.True(detail.Dismissed);
        Assert.Null(detail.Group);

        await Choose(client, Rane, null);
        Assert.Equal(3, (await Groups(client, "inbox"))!.Groups.Single(g => g.Key == Rane).Dates);
    }

    [Fact]
    public async Task Choosing_teaches_like_saving()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("r9", "Rane Willerslev – nyt foredrag", date: "2099-06-01", segment: "Miscellaneous", genre: "Lecture/Seminar", performers: ["Rane Willerslev"]));
        await factory.ScanAsync();

        await Choose(client, Rane, "Chosen");
        Assert.True((await Groups(client, "inbox"))!.Groups.Single(g => g.Title == "Rane Willerslev – nyt foredrag").Score > 0);
    }

    [Fact]
    public async Task Unknown_group_is_404()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        Assert.Equal(HttpStatusCode.NotFound, (await Choose(client, "title:findes ikke", "Chosen")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/events?key=title%3Afindes%20ikke")).StatusCode);
    }
}
