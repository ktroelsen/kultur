using System.Security.Claims;

namespace Kultur.Api.Auth;

public static class Users
{
    /// <summary>The one user the password logs in as; their data is under App_Data/users/owner/.</summary>
    public const string Owner = "owner";

    /// <summary>Every user id a login can give. Integrations may only answer for one of these.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { Owner };

    // One user for now ("owner"), but all personal data is stored per user id so more can follow.
    public static string Id(ClaimsPrincipal user) => user.Identity?.Name ?? throw new InvalidOperationException("Not signed in.");
}
