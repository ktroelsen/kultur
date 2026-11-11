namespace Kultur.Api.Events;

public record UserState(DateTimeOffset? LastVisitUtc);

// What one user thinks of the events (Gem / Måske / Nej tak) and when they last looked,
// under App_Data/users/{id}/ (reactions.json, learning.json, state.json). The buttons teach the
// taste model a little (see Lessons); the ratings under "Smag" still weigh most.
public class UserData(string usersDir, TimeProvider clock)
{
    readonly SemaphoreSlim _lock = new(1, 1);

    string FileOf(string userId, string name) => Path.Combine(usersDir, userId, name);

    public async Task<Dictionary<string, Reaction>> ReactionsAsync(string userId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await LoadReactionsAsync(userId, ct); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Sets Gem / Måske / Nej tak (null takes it back) and what the button teaches. "Nej tak" because of
    /// the event hides its whole series – every date, also ones found later; because of the date, every
    /// event that day. Taking any date of a hidden series back (or giving it another reaction) brings the
    /// series back; taking back any event on a busy day frees the day.
    /// </summary>
    public async Task SetReactionAsync(string userId, CultureEvent e, Reaction? reaction, DismissReason? reason, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var reactions = await LoadReactionsAsync(userId, ct);
            var learning = await LoadLearningAsync(userId, ct);
            var series = Series.KeyOf(e);
            var dismissesSeries = reaction == Reaction.NotInterested && reason == DismissReason.Event;
            var day = e.Date?.ToString("yyyy-MM-dd");
            var marksBusy = reaction == Reaction.NotInterested && reason == DismissReason.Date && day is not null;

            // Gem or Måske on a busy day keeps that one event; only taking a "Nej tak" back frees the day.
            if (day is not null && learning.BusyDays.TryGetValue(day, out var busyTrigger) && reaction is null)
            {
                learning.BusyDays.Remove(day);
                if (busyTrigger != e.Id) Forget(busyTrigger);
            }

            if (learning.DismissedSeries.TryGetValue(series, out var trigger) && !(dismissesSeries && trigger == e.Id))
            {
                learning.DismissedSeries.Remove(series);
                if (trigger != e.Id) Forget(trigger);
            }
            Forget(e.Id);

            if (reaction is { } r)
            {
                reactions[e.Id] = r;
                if (r == Reaction.NotInterested && reason is { } why) learning.Reasons[e.Id] = why;
                var deltas = Lessons.From(e, r, reason);
                if (deltas.Count > 0) learning.Applied[e.Id] = deltas;
                if (dismissesSeries) learning.DismissedSeries[series] = e.Id;
                if (marksBusy) learning.BusyDays[day!] = e.Id;
            }

            await JsonFile.WriteAsync(FileOf(userId, "reactions.json"), reactions, ct);
            await JsonFile.WriteAsync(FileOf(userId, "learning.json"), learning, ct);

            void Forget(string id)
            {
                reactions.Remove(id);
                learning.Reasons.Remove(id);
                learning.Applied.Remove(id);
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// "Vælg til" (Chosen: the series goes to "Til dig" and teaches like Gem), "Nej tak" (Declined: the
    /// dates listed now go, nothing learned – dates found later may turn up), "Nej tak alle" (Dismissed:
    /// the event whatever the date and place, also later dates; teaches like "Nej tak – begivenheden"),
    /// or null: back to undecided. Representative is a date of the series, for what it teaches;
    /// current are the series' dates listed now, for Declined.
    /// </summary>
    public async Task SetSeriesChoiceAsync(string userId, string series, CultureEvent representative, SeriesChoice? choice,
        IReadOnlyCollection<string>? current = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var reactions = await LoadReactionsAsync(userId, ct);
            var learning = await LoadLearningAsync(userId, ct);
            var chosenKey = "series:" + series;

            if (learning.ChosenSeries.Remove(series)) learning.Applied.Remove(chosenKey);
            if (learning.DismissedSeries.Remove(series, out var trigger))
            {
                reactions.Remove(trigger);
                learning.Reasons.Remove(trigger);
                learning.Applied.Remove(trigger);
            }
            if (learning.DeclinedSeries.Remove(series, out var declined))
            {
                foreach (var id in declined)
                    if (reactions.GetValueOrDefault(id) == Reaction.NotInterested) reactions.Remove(id);
            }

            switch (choice)
            {
                case SeriesChoice.Chosen:
                    learning.ChosenSeries[series] = representative.Id;
                    var lessons = Lessons.From(representative, Reaction.Saved, null);
                    if (lessons.Count > 0) learning.Applied[chosenKey] = lessons;
                    break;
                case SeriesChoice.Declined:
                    // Only dates without a reaction of their own: a saved date stays saved.
                    var ids = (current ?? [representative.Id]).Where(i => !reactions.ContainsKey(i)).ToList();
                    foreach (var i in ids) reactions[i] = Reaction.NotInterested;
                    learning.DeclinedSeries[series] = ids;
                    break;
                case SeriesChoice.Dismissed:
                    var id = representative.Id;
                    learning.DismissedSeries[series] = id;
                    reactions[id] = Reaction.NotInterested;
                    learning.Reasons[id] = DismissReason.Event;
                    learning.Applied[id] = Lessons.From(representative, Reaction.NotInterested, DismissReason.Event);
                    break;
            }

            await JsonFile.WriteAsync(FileOf(userId, "reactions.json"), reactions, ct);
            await JsonFile.WriteAsync(FileOf(userId, "learning.json"), learning, ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>What the buttons have taught, plus the hidden series.</summary>
    public async Task<Learning> LearningAsync(string userId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await LoadLearningAsync(userId, ct); }
        finally { _lock.Release(); }
    }

    async Task<Learning> LoadLearningAsync(string userId, CancellationToken ct) =>
        await JsonFile.ReadAsync<Learning>(FileOf(userId, "learning.json"), ct) ?? new Learning();

    public async Task<UserState> StateAsync(string userId, CancellationToken ct = default) =>
        await JsonFile.ReadAsync<UserState>(FileOf(userId, "state.json"), ct) ?? new UserState(null);

    /// <summary>Marks everything up to now as seen ("Siden sidst" starts from here next time).</summary>
    public Task MarkSeenAsync(string userId, CancellationToken ct = default) =>
        JsonFile.WriteAsync(FileOf(userId, "state.json"), new UserState(clock.GetUtcNow()), ct);

    async Task<Dictionary<string, Reaction>> LoadReactionsAsync(string userId, CancellationToken ct) =>
        await JsonFile.ReadAsync<Dictionary<string, Reaction>>(FileOf(userId, "reactions.json"), ct) ?? [];
}

/// <summary>
/// A choice about a whole series: Chosen ("Vælg til"), Declined ("Nej tak": the dates listed now),
/// Dismissed ("Nej tak alle": the event whatever the date and place, also later dates).
/// </summary>
public enum SeriesChoice { Chosen, Dismissed, Declined }

/// <summary>
/// What the buttons have taught. Applied keeps each event's own lessons, so taking a reaction back
/// takes back exactly what it taught – also after the event itself is over and gone.
/// </summary>
public class Learning
{
    public Dictionary<string, DismissReason> Reasons { get; set; } = [];

    /// <summary>Series hidden by "Nej tak – arrangementet", and the date it was pressed on.</summary>
    public Dictionary<string, string> DismissedSeries { get; set; } = [];

    /// <summary>Series declined with "Nej tak" on the group, and the dates it turned down then.</summary>
    public Dictionary<string, List<string>> DeclinedSeries { get; set; } = [];

    /// <summary>Series chosen with "Vælg til" (they're on "Til dig"), and the date it was pressed on.</summary>
    public Dictionary<string, string> ChosenSeries { get; set; } = [];

    /// <summary>Days marked busy ("Nej tak – datoen", yyyy-MM-dd), and the event it was pressed on.</summary>
    public Dictionary<string, string> BusyDays { get; set; } = [];

    /// <summary>Per event id: topic key → points it added (positive) or took away.</summary>
    public Dictionary<string, Dictionary<string, double>> Applied { get; set; } = [];

    /// <summary>The sum per topic, kept within the range of a rating (−2 "aldrig" … +2 "meget").</summary>
    public Dictionary<string, double> Signals() => Applied.Values
        .SelectMany(d => d)
        .GroupBy(kv => kv.Key)
        .ToDictionary(g => g.Key, g => Math.Clamp(g.Sum(kv => kv.Value), -2, 2));
}

/// <summary>What one press of a button teaches, in rating steps (a rating of "meget" is +2).</summary>
public static class Lessons
{
    public static Dictionary<string, double> From(CultureEvent e, Reaction reaction, DismissReason? reason)
    {
        var lessons = new Dictionary<string, double>();
        void Add(Taste.TopicKind kind, double points)
        {
            foreach (var t in Taste.TasteModel.TopicsOf(e).Where(t => t.Kind == kind))
                lessons[t.Key] = lessons.GetValueOrDefault(t.Key) + points;
        }

        switch (reaction, reason)
        {
            // The place isn't part of it: the buttons teach about performers and genres only.
            case (Reaction.Saved, _):
                Add(Taste.TopicKind.Performer, 1); Add(Taste.TopicKind.Genre, 0.5);
                break;
            case (Reaction.Maybe, _):
                Add(Taste.TopicKind.Performer, 0.5); Add(Taste.TopicKind.Genre, 0.25);
                break;
            case (Reaction.NotInterested, DismissReason.Event):
                Add(Taste.TopicKind.Performer, -1); Add(Taste.TopicKind.Genre, -0.5);
                break;
            // DismissReason.Date: busy that day – says nothing about taste.
        }
        return lessons;
    }
}
