namespace Kultur.Api.Events;

// Home is Copenhagen. Zone 1 is Greater Copenhagen, zone 2 the rest of Denmark, zone 3 abroad.
public static class Zones
{
    // Towns in Greater Copenhagen as they appear in ticketing data (lower case).
    static readonly HashSet<string> GreaterCopenhagen =
    [
        "frederiksberg", "valby", "vanløse", "brønshøj", "nordhavn", "amager", "ørestad", "kastrup", "tårnby",
        "dragør", "hvidovre", "rødovre", "brøndby", "brøndby strand", "glostrup", "albertslund", "ishøj",
        "vallensbæk", "herlev", "ballerup", "måløv", "gladsaxe", "søborg", "bagsværd", "gentofte", "hellerup",
        "charlottenlund", "klampenborg", "lyngby", "kgs. lyngby", "kongens lyngby", "virum", "farum",
        "værløse", "skovlunde", "husum", "taastrup", "høje taastrup",
    ];

    public static Zone Of(string? city, string? countryCode)
    {
        if (countryCode is not null && !countryCode.Equals("DK", StringComparison.OrdinalIgnoreCase))
            return Zone.Abroad;
        return IsGreaterCopenhagen(city) ? Zone.Copenhagen : Zone.Denmark;
    }

    static bool IsGreaterCopenhagen(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return false;
        var c = city.Trim().ToLowerInvariant();
        // "København K", "Copenhagen", "Kobenhavn S" …
        if (c.StartsWith("københavn") || c.StartsWith("kobenhavn") || c.StartsWith("copenhagen")) return true;
        // "Brøndby", "Valby 2500" – strip a trailing postcode or district letter.
        var name = string.Join(' ', c.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.All(char.IsDigit) && part.Length > 1));
        return GreaterCopenhagen.Contains(name);
    }
}
