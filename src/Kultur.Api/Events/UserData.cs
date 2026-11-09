namespace Kultur.Api.Events;

public record UserState(DateTimeOffset? LastVisitUtc);

// What one user thinks of the events (Gem / Måske / Nej tak) and when they last looked,
// under App_Data/users/{id}/. These signals are what the taste profile learns from.
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

    public async Task SetReactionAsync(string userId, string eventId, Reaction? reaction, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var reactions = await LoadReactionsAsync(userId, ct);
            if (reaction is { } r) reactions[eventId] = r;
            else reactions.Remove(eventId);
            await JsonFile.WriteAsync(FileOf(userId, "reactions.json"), reactions, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<UserState> StateAsync(string userId, CancellationToken ct = default) =>
        await JsonFile.ReadAsync<UserState>(FileOf(userId, "state.json"), ct) ?? new UserState(null);

    /// <summary>Marks everything up to now as seen ("Siden sidst" starts from here next time).</summary>
    public Task MarkSeenAsync(string userId, CancellationToken ct = default) =>
        JsonFile.WriteAsync(FileOf(userId, "state.json"), new UserState(clock.GetUtcNow()), ct);

    async Task<Dictionary<string, Reaction>> LoadReactionsAsync(string userId, CancellationToken ct) =>
        await JsonFile.ReadAsync<Dictionary<string, Reaction>>(FileOf(userId, "reactions.json"), ct) ?? [];
}
