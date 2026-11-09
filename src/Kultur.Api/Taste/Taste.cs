using Kultur.Api.Events;

namespace Kultur.Api.Taste;

/// <summary>From "aldrig" to "meget": how interesting a topic is.</summary>
public enum Interest { Never = 0, Little = 1, Maybe = 2, Gladly = 3, Very = 4 }

public enum TopicKind { Category, Genre, Performer, Venue }

/// <summary>
/// Something to have a taste about: a category (Sport), a genre (Jazz), a performer (an artist,
/// band or team) or a venue. Key is "{kind}:{name in lower case}", the same for every event.
/// </summary>
public record Topic(TopicKind Kind, string Name)
{
    public string Key => KeyOf(Kind, Name);

    public static string KeyOf(TopicKind kind, string name) => $"{kind}:{name.Trim().ToLowerInvariant()}";
}

/// <summary>How an event fits the ratings: hidden (turned down), a score, and the best reason for it.</summary>
public record Fit(bool Hidden, int Score, Topic? Because, Interest? BecauseRating);

// Turns ratings of topics into a judgement of each event. A performer weighs most (you go to see
// them), then the genre, then the venue and the broad category.
public static class TasteModel
{
    static int Weight(TopicKind kind) => kind switch
    {
        TopicKind.Performer => 3,
        TopicKind.Genre => 2,
        _ => 1,
    };

    public static IEnumerable<Topic> TopicsOf(CultureEvent e)
    {
        yield return new Topic(TopicKind.Category, e.Category.ToString());
        if (!string.IsNullOrWhiteSpace(e.Genre)) yield return new Topic(TopicKind.Genre, e.Genre.Trim());
        foreach (var p in e.Performers ?? []) yield return new Topic(TopicKind.Performer, p);
        if (!string.IsNullOrWhiteSpace(e.Venue)) yield return new Topic(TopicKind.Venue, e.Venue.Trim());
    }

    public static Fit FitOf(CultureEvent e, IReadOnlyDictionary<string, Interest> ratings)
    {
        var rated = TopicsOf(e)
            .Select(t => (Topic: t, Rating: ratings.TryGetValue(t.Key, out var r) ? r : (Interest?)null))
            .Where(x => x.Rating is not null)
            .Select(x => (x.Topic, Rating: x.Rating!.Value))
            .ToList();

        // "Aldrig" about a performer always hides the event. "Aldrig" about a genre, venue or category
        // hides it too – unless a performer you like is on: you'd go to see them anyway.
        var lovedPerformer = rated.Any(x => x.Topic.Kind == TopicKind.Performer && x.Rating >= Interest.Gladly);
        var hidden = rated.Any(x => x.Rating == Interest.Never && (x.Topic.Kind == TopicKind.Performer || !lovedPerformer));

        var score = rated.Sum(x => Weight(x.Topic.Kind) * ((int)x.Rating - (int)Interest.Maybe));
        var because = rated
            .Where(x => x.Rating >= Interest.Gladly)
            .OrderByDescending(x => Weight(x.Topic.Kind) * (int)x.Rating)
            .Select(x => ((Topic, Interest)?)(x.Topic, x.Rating))
            .FirstOrDefault();
        return new Fit(hidden, score, because?.Item1, because?.Item2);
    }
}
