using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Kultur.Api.Auth;

// Single-user login: one password from config (Auth:Password), exchanged for a cookie.
// Only one user, so there is no user store: the cookie just says "owner".
public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/login", async (LoginRequest body, HttpContext http, IConfiguration config) =>
        {
            var expected = config["Auth:Password"];
            if (string.IsNullOrEmpty(expected))
                return Results.Problem("Login is not configured (Auth:Password is missing).", statusCode: 503);

            if (!PasswordMatches(body.Password ?? "", expected))
                return Results.Unauthorized();

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new MeResponse("owner"));
        }).RequireRateLimiting(LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return Results.NoContent();
        });

        auth.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new MeResponse(user.Identity!.Name!)))
            .RequireAuthorization();
    }

    // Hash both sides so the comparison is constant-time regardless of length.
    static bool PasswordMatches(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}

public record LoginRequest(string? Password);
public record MeResponse(string Name);
