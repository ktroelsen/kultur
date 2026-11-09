using System.Security.Claims;

namespace Kultur.Api.Auth;

public static class Users
{
    // One user for now ("owner"), but all personal data is stored per user id so more can follow.
    public static string Id(ClaimsPrincipal user) => user.Identity?.Name ?? throw new InvalidOperationException("Not signed in.");
}
