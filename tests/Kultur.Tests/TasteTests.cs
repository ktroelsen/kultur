using System.Net;
using System.Net.Http.Json;
using Kultur.Api.Events;
using Kultur.Api.Taste;

namespace Kultur.Tests;

public class TasteTests
{
    static async Task<(KulturFactory Factory, HttpClient Client)> WithEventsAsync()
    {
        var factory = new KulturFactory();
        factory.Ticketmaster.Body = TicketmasterJson.Page(
            TicketmasterJson.Event("m1", "Agnes Obel", genre: "Pop", venue: "Vega"),
            TicketmasterJson.Event("m2", "Agnes Obel – ekstra", genre: "Pop", venue: "Vega", performers: ["Agnes Obel"]),
            TicketmasterJson.Event("m3", "Jazz i kælderen", genre: "Jazz", venue: "Jazzhouse"),
            TicketmasterJson.Event("s1", "Danmark – Norge", segment: "Sports", genre: "Handball", venue: "Royal Arena", performers: ["Danmark", "Norge"]),
            TicketmasterJson.Event("s2", "FCK – Vejle", segment: "Sports", genre: "Football", venue: "Parken", performers: ["FC København", "Vejle Boldklub"]),
            TicketmasterJson.Event("s3", "Vejle – Brøndby", city: "Vejle", segment: "Sports", genre: "Football", venue: "Vejle Stadion", performers: ["Vejle Boldklub", "Brøndby IF"]));
        await factory.ScanAsync();
        return (factory, await factory.LoggedInClientAsync());
    }

    static Task<HttpResponseMessage> Rate(HttpClient client, TopicKind kind, string name, Interest? rating) =>
        client.PutAsJsonAsync("/api/taste", new { kind = kind.ToString(), name, rating = rating?.ToString() });

    [Fact]
    public async Task Asks_about_the_broad_categories_first_biggest_first()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;

        var first = await client.GetAsync<NextTopic>("/api/taste/next");
        Assert.Equal(new Topic(TopicKind.Category, "Music"), first!.Topic); // 3 music, 3 sport → alphabetical
        Assert.Equal(3, first.Upcoming);
        Assert.Equal(TasteEndpoints.ExampleCount, first.Examples.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await Rate(client, TopicKind.Category, "Music", Interest.Gladly)).StatusCode);
        var second = await client.GetAsync<NextTopic>("/api/taste/next");
        Assert.Equal(new Topic(TopicKind.Category, "Sport"), second!.Topic);
        Assert.Equal(1, second.Rated);
    }

    [Fact]
    public async Task Never_about_sport_stops_questions_about_teams_but_a_loved_team_stays()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        await Rate(client, TopicKind.Category, "Music", Interest.Gladly);
        await Rate(client, TopicKind.Category, "Sport", Interest.Never);

        var asked = new List<Topic>();
        for (var i = 0; i < 20; i++)
        {
            var res = await client.GetAsync("/api/taste/next");
            if (res.StatusCode == HttpStatusCode.NoContent) break;
            var next = (await res.Content.ReadAsync<NextTopic>())!;
            asked.Add(next.Topic);
            await Rate(client, next.Topic.Kind, next.Topic.Name, Interest.Maybe);
        }
        Assert.Contains(new Topic(TopicKind.Performer, "Agnes Obel"), asked);
        Assert.Contains(new Topic(TopicKind.Genre, "Jazz"), asked);
        Assert.DoesNotContain(asked, t => t.Name is "Vejle Boldklub" or "Football" or "Parken");

        // Sport is hidden in "Til dig" – until a team is rated "meget".
        var forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.DoesNotContain(forYou, i => i.Event.Category == Category.Sport);

        await Rate(client, TopicKind.Performer, "Vejle Boldklub", Interest.Very);
        forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.Equal(["FCK – Vejle", "Vejle – Brøndby"], forYou.Where(i => i.Event.Category == Category.Sport).Select(i => i.Event.Title).Order());
        var vejle = forYou.Single(i => i.Event.Title == "FCK – Vejle");
        Assert.Equal(new Topic(TopicKind.Performer, "Vejle Boldklub"), vejle.Because);
        Assert.Equal(Interest.Very, vejle.BecauseRating);
    }

    [Fact]
    public async Task Kinds_take_turns_after_the_categories()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        await Rate(client, TopicKind.Category, "Music", Interest.Maybe);
        await Rate(client, TopicKind.Category, "Sport", Interest.Maybe);

        var kinds = new List<TopicKind>();
        for (var i = 0; i < 4; i++)
        {
            var next = (await client.GetAsync<NextTopic>("/api/taste/next"))!;
            kinds.Add(next.Topic.Kind);
            await Rate(client, next.Topic.Kind, next.Topic.Name, null); // spring over
        }
        // Performers have twice the share of genres and venues.
        Assert.Equal(2, kinds.Count(k => k == TopicKind.Performer));
        Assert.Contains(TopicKind.Genre, kinds);
        Assert.Contains(TopicKind.Venue, kinds);
    }

    [Fact]
    public async Task Skipped_topics_are_not_ratings_and_ratings_can_be_listed_and_removed()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        await Rate(client, TopicKind.Category, "Music", null);
        await Rate(client, TopicKind.Genre, "Jazz", Interest.Very);

        var rated = (await client.GetAsync<List<RatedTopicItem>>("/api/taste"))!;
        var jazz = Assert.Single(rated);
        Assert.Equal("Genre:jazz", jazz.Key);
        Assert.Equal(Interest.Very, jazz.Rating);

        // Skipped music isn't asked again right away.
        Assert.Equal(new Topic(TopicKind.Category, "Sport"), (await client.GetAsync<NextTopic>("/api/taste/next"))!.Topic);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/taste/Genre:jazz")).StatusCode);
        Assert.Empty((await client.GetAsync<List<RatedTopicItem>>("/api/taste"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/taste/Genre:jazz")).StatusCode);
    }

    [Fact]
    public async Task For_you_puts_the_best_match_first_and_needs_more_far_away_once_you_have_rated_enough()
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        await Rate(client, TopicKind.Genre, "Jazz", Interest.Very);

        var forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.Equal("Jazz i kælderen", forYou[0].Event.Title);
        Assert.Contains(forYou, i => i.Event.Title == "Vejle – Brøndby"); // few ratings: no zone threshold yet

        for (var i = 0; i < EventEndpoints.RatingsBeforeZoneThresholds; i++)
            await Rate(client, TopicKind.Venue, $"Sted {i}", Interest.Maybe);
        forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.DoesNotContain(forYou, i => i.Event.Title == "Vejle – Brøndby");

        await Rate(client, TopicKind.Performer, "Vejle Boldklub", Interest.Very);
        forYou = (await client.GetAsync<EventList>("/api/events?view=forYou"))!.Events;
        Assert.Contains(forYou, i => i.Event.Title == "Vejle – Brøndby");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rating_needs_a_name(string name)
    {
        var (factory, client) = await WithEventsAsync();
        using var _ = factory;
        Assert.Equal(HttpStatusCode.BadRequest, (await Rate(client, TopicKind.Genre, name, Interest.Very)).StatusCode);
    }
}
