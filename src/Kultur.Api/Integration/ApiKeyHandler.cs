using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Kultur.Api.Integration;

/// <summary>Which config key the scheme checks the X-Api-Key header against, and the name it gives the caller.</summary>
public class ApiKeyOptions : AuthenticationSchemeOptions
{
    public string ConfigKey { get; set; } = "";
    public string Caller { get; set; } = "";
}

// kaspert.dk calls Kultur from its server with a key in X-Api-Key (Integrations:Kaspert:ApiKey,
// scheme "SummaryKey", policy "Summary"). The policy only guards the read-only
// GET /api/integration/summary: the key is shared by all our apps, so it must never open anything
// else. It isn't the default scheme, so every other endpoint still needs the login cookie (or, for
// /api/scan, X-Scan-Key), and the cookie doesn't open the summary.
public class ApiKeyHandler(IOptionsMonitor<ApiKeyOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config)
    : AuthenticationHandler<ApiKeyOptions>(options, logger, encoder)
{
    public const string SummaryScheme = "SummaryKey";
    public const string SummaryPolicy = "Summary";
    public const string Header = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = config[Options.ConfigKey];
        if (string.IsNullOrEmpty(expected) || !Request.Headers.TryGetValue(Header, out var given))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Hash both sides so the comparison is constant-time regardless of length.
        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(given.ToString())),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Task.FromResult(AuthenticateResult.Fail("Wrong API key"));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, Options.Caller)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
