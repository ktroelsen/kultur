using Kultur.Api.Events;
using Kultur.Api.Taste;

namespace Kultur.Tests;

public class TasteModelTests
{
    static CultureEvent Event(Category category, string? genre, string? venue, params string[] performers) =>
        new("t:1", "t", "Titel", category, genre, null, null, venue, "København", "DK", null, null, "https://x", null, null,
            DateTimeOffset.UnixEpoch, performers.Length > 0 ? performers : null);

    static Dictionary<string, Interest> Ratings(params (TopicKind Kind, string Name, Interest Rating)[] r) =>
        r.ToDictionary(x => Topic.KeyOf(x.Kind, x.Name), x => x.Rating);

    [Fact]
    public void Topics_are_category_genre_performers_and_venue()
    {
        var topics = TasteModel.TopicsOf(Event(Category.Sport, "Football", "Parken", "Danmark", "Norge")).ToList();
        Assert.Equal(
            ["Category:sport", "Genre:football", "Performer:danmark", "Performer:norge", "Venue:parken"],
            topics.Select(t => t.Key));
    }

    [Fact]
    public void Unrated_event_is_neutral()
    {
        var fit = TasteModel.FitOf(Event(Category.Music, "Jazz", "Jazzhouse"), Ratings());
        Assert.Equal(new Fit(false, 0, null, null), fit);
    }

    [Fact]
    public void Score_weighs_performer_over_genre_over_venue_and_names_the_best_reason()
    {
        var fit = TasteModel.FitOf(Event(Category.Music, "Pop", "Vega", "Agnes Obel"), Ratings(
            (TopicKind.Performer, "Agnes Obel", Interest.Very),
            (TopicKind.Genre, "pop", Interest.Gladly),
            (TopicKind.Venue, "Vega", Interest.Little)));
        Assert.False(fit.Hidden);
        Assert.Equal(3 * 2 + 2 * 1 + 1 * -1, fit.Score);
        Assert.Equal(new Topic(TopicKind.Performer, "Agnes Obel"), fit.Because);
        Assert.Equal(Interest.Very, fit.BecauseRating);
    }

    [Fact]
    public void Never_about_a_genre_hides_the_event()
    {
        var fit = TasteModel.FitOf(Event(Category.Music, "Metal", "Pumpehuset", "Band"), Ratings((TopicKind.Genre, "Metal", Interest.Never)));
        Assert.True(fit.Hidden);
    }

    [Fact]
    public void A_loved_performer_beats_never_about_the_category()
    {
        var ratings = Ratings(
            (TopicKind.Category, "Sport", Interest.Never),
            (TopicKind.Performer, "Vejle Boldklub", Interest.Very));
        Assert.False(TasteModel.FitOf(Event(Category.Sport, "Football", "Parken", "FC København", "Vejle Boldklub"), ratings).Hidden);
        Assert.True(TasteModel.FitOf(Event(Category.Sport, "Football", "Parken", "FC København", "Brøndby"), ratings).Hidden);
    }

    [Fact]
    public void Never_about_a_performer_always_hides()
    {
        var fit = TasteModel.FitOf(Event(Category.Music, "Pop", "Royal Arena", "Stjerne"), Ratings(
            (TopicKind.Performer, "Stjerne", Interest.Never),
            (TopicKind.Genre, "Pop", Interest.Very)));
        Assert.True(fit.Hidden);
    }
}
