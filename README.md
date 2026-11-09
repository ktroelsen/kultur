# Kultur

Personlig app, der finder, forstår og holder øje med oplevelser – koncerter, teater, stand-up,
film-særvisninger og sport – og over tid lærer, hvad jeg er interesseret i.
Kører på https://kultur.kaspert.dk. Konceptet står i [`docs/koncept.md`](docs/koncept.md).

## Det kan den nu

- **Siden sidst** (forside): arrangementer, der er dukket op siden sidste besøg.
- **Til dig**: alle kommende arrangementer, jeg ikke har sagt nej tak til, delt i afstandszoner
  (København, resten af Danmark, udlandet) og med filter på kategori.
- **Gem / Måske / Nej tak** på hvert arrangement; **Gemt** viser de gemte. Billetsalg, der ikke er
  startet endnu, fremhæves.
- **Dig** ("Det tror jeg om dig"): smagsprofilen med egne ord, for kultur og for sport. Og knappen
  "Led efter nyt nu".
- **Natlig scanning** (GitHub Actions → `POST /api/scan`) af kilderne. Første kilde er
  Ticketmaster Discovery API (Danmark).

POC: én bruger med ét kodeord, ingen database (JSON-filer i `App_Data`, personlige data under
`App_Data/users/{id}/`).

## Arkitektur

| Del | Teknologi |
|---|---|
| Backend | ASP.NET Core minimal API, .NET 10 (`src/Kultur.Api`) |
| Frontend | React 19 + Vite + Tailwind 4, installerbar PWA (`web/`) |
| Kilder | `IEventSource` i `src/Kultur.Api/Scan/` – i dag `TicketmasterSource` |
| Data | `EventCatalog` (`App_Data/events.json`), `UserData` (reaktioner, sidste besøg), profil |
| Hosting | Simply (IIS, self-contained win-x86) – se [`docs/simply-deploy.md`](docs/simply-deploy.md) |

## Udvikling

```
dotnet build -c Release -warnaserror && dotnet test
npm --prefix web ci && npm --prefix web run lint && npm --prefix web run build
dotnet run --project src/Kultur.Api      # http://localhost:5280, kodeord "dev"
npm --prefix web run dev                  # http://localhost:5175
```
