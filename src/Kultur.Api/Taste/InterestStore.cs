namespace Kultur.Api.Taste;

/// <summary>A rated topic; Rating null means "spring over" (ask again later).</summary>
public record RatedTopic(TopicKind Kind, string Name, Interest? Rating, DateTimeOffset AtUtc);

// The user's ratings of topics, in App_Data/users/{id}/interests.json, keyed by Topic.Key.
public class InterestStore(string usersDir, TimeProvider clock)
{
    readonly SemaphoreSlim _lock = new(1, 1);

    string FileOf(string userId) => Path.Combine(usersDir, userId, "interests.json");

    public async Task<Dictionary<string, RatedTopic>> AllAsync(string userId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await LoadAsync(userId, ct); }
        finally { _lock.Release(); }
    }

    /// <summary>Only the real ratings (not the skipped ones), for scoring events.</summary>
    public async Task<Dictionary<string, Interest>> RatingsAsync(string userId, CancellationToken ct = default) =>
        (await AllAsync(userId, ct))
            .Where(kv => kv.Value.Rating is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Rating!.Value);

    public async Task SetAsync(string userId, Topic topic, Interest? rating, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var all = await LoadAsync(userId, ct);
            all[topic.Key] = new RatedTopic(topic.Kind, topic.Name, rating, clock.GetUtcNow());
            await JsonFile.WriteAsync(FileOf(userId), all, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> RemoveAsync(string userId, string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var all = await LoadAsync(userId, ct);
            if (!all.Remove(key)) return false;
            await JsonFile.WriteAsync(FileOf(userId), all, ct);
            return true;
        }
        finally { _lock.Release(); }
    }

    async Task<Dictionary<string, RatedTopic>> LoadAsync(string userId, CancellationToken ct) =>
        await JsonFile.ReadAsync<Dictionary<string, RatedTopic>>(FileOf(userId), ct) ?? [];
}
