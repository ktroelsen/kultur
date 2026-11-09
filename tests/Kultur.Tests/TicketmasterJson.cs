namespace Kultur.Tests;

// Discovery API responses, trimmed to the fields the app reads.
public static class TicketmasterJson
{
    public static string Page(params string[] events) => $$"""
        {
          "_embedded": { "events": [ {{string.Join(",", events)}} ] },
          "page": { "size": 200, "totalElements": {{events.Length}}, "totalPages": 1, "number": 0 }
        }
        """;

    public static string Event(string id, string name, string date = "2099-03-14", string city = "København K",
        string country = "DK", string segment = "Music", string genre = "Rock", string venue = "Vega") => $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "url": "https://www.ticketmaster.dk/event/{{id}}",
          "images": [
            { "ratio": "16_9", "url": "https://img/small.jpg", "width": 205 },
            { "ratio": "16_9", "url": "https://img/card.jpg", "width": 640 },
            { "ratio": "4_3", "url": "https://img/other.jpg", "width": 640 }
          ],
          "sales": { "public": { "startDateTime": "2098-11-01T09:00:00Z" } },
          "dates": { "start": { "localDate": "{{date}}", "dateTime": "{{date}}T19:00:00Z" } },
          "classifications": [ { "segment": { "name": "{{segment}}" }, "genre": { "name": "{{genre}}" } } ],
          "priceRanges": [ { "type": "standard", "currency": "DKK", "min": 345.0, "max": 495.0 } ],
          "_embedded": { "venues": [ { "name": "{{venue}}", "city": { "name": "{{city}}" }, "country": { "countryCode": "{{country}}" } } ] }
        }
        """;
}
