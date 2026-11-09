using System.Security.Claims;
using Kultur.Api.Auth;

namespace Kultur.Api.Profile;

/// <summary>
/// The taste profile in plain words ("Det tror jeg om dig"): culture, and sport (teams, sports,
/// kinds of events). For now written by the user; later kept up to date by Claude from the
/// reactions and Spotify, and always readable and editable here.
/// </summary>
public record TasteProfile(string About, string Sport);

public static class ProfileEndpoints
{
    public const int MaxLength = 4000;

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app, string usersDir)
    {
        var profile = app.MapGroup("/api/profile").RequireAuthorization();
        string FileOf(ClaimsPrincipal user) => Path.Combine(usersDir, Users.Id(user), "profile.json");

        profile.MapGet("/", async (ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await JsonFile.ReadAsync<TasteProfile>(FileOf(user), ct) ?? new TasteProfile("", "")));

        profile.MapPut("/", async (TasteProfile body, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var about = body.About?.Trim() ?? "";
            var sport = body.Sport?.Trim() ?? "";
            if (about.Length > MaxLength || sport.Length > MaxLength)
                return Results.Problem($"Højst {MaxLength} tegn pr. felt.", statusCode: 400);
            var saved = new TasteProfile(about, sport);
            await JsonFile.WriteAsync(FileOf(user), saved, ct);
            return Results.Ok(saved);
        });
    }
}
