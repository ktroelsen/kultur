using Kultur.Api.Events;

namespace Kultur.Tests;

public class ZoneTests
{
    [Theory]
    [InlineData("København K", "DK", Zone.Copenhagen)]
    [InlineData("Copenhagen", "DK", Zone.Copenhagen)]
    [InlineData("Brøndby", "DK", Zone.Copenhagen)]
    [InlineData("Kgs. Lyngby", "DK", Zone.Copenhagen)]
    [InlineData("Valby 2500", "DK", Zone.Copenhagen)]
    [InlineData("Vejle", "DK", Zone.Denmark)]
    [InlineData("Aarhus C", "DK", Zone.Denmark)]
    [InlineData(null, "DK", Zone.Denmark)]
    [InlineData("Berlin", "DE", Zone.Abroad)]
    [InlineData("Malmö", "SE", Zone.Abroad)]
    public void Zone_follows_city_and_country(string? city, string country, Zone expected) =>
        Assert.Equal(expected, Zones.Of(city, country));
}
