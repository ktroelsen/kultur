# Kultur – noter til Claude

- Konceptet står i `docs/koncept.md`. Læs det før større ændringer, og hold det opdateret.
- Samme stil og teknologi som Læs højt (`ktroelsen/read`): ASP.NET Core minimal API (.NET 10) + React/Vite/Tailwind PWA i `web/`.
- Backend: `dotnet build -c Release -warnaserror` og `dotnet test` skal være grønne. I Git Bash: brug `-warnaserror`, ikke `/warnaserror`.
- Frontend: `npm --prefix web run lint` og `npm --prefix web run build`.
- UI-tekster er på dansk; kode og kommentarer på engelsk.
- Ingen database: data er JSON-filer i `App_Data`, pr. bruger under `App_Data/users/{id}/`. App_Data skippes ved deploy, så data overlever.
- Kilder er en fast liste i koden; klienten vælger en nøgle, aldrig en URL. Hent kun fra sider, hvis brugervilkår tillader det. Omtaler gengives som stjerner + kilde + link, aldrig anmeldelsens tekst.
- Kilder implementerer `IEventSource` (`src/Kultur.Api/Scan/`) og registreres i `Program.cs`; en kilde uden nøgle springes over, og en fejlende kilde stopper ikke de andre. Arrangementer normaliseres til `CultureEvent` med id `"{kilde}:{kildens id}"` og samles i `EventCatalog` (`App_Data/events.json`); fortidige fjernes ved hver scanning. Reaktioner (Gem/Måske/Nej tak) og sidste besøg ligger i `UserData`.
- Zoner (`Zones`): 1 Storkøbenhavn, 2 resten af Danmark, 3 udlandet.
- Claude (`Ai:ApiKey`, kommer senere) bruges til rangering, profil og udtræk fra HTML, med dagligt udgiftsloft. Uden nøgle er AI slået fra.
- Natlig scanning: `.github/workflows/scan.yml` kalder `POST /api/scan` med `X-Scan-Key` (`Scan:Key`, secret `KULTUR_SCAN_KEY`); en logget-ind bruger må også starte den. IIS genbruger app-poolen, så ingen baggrundstjeneste.
- Hosting er Simply (IIS, 32-bit, shared, self-contained win-x86) på kultur.kaspert.dk – samme opsætning som read.kaspert.dk.
- Se `docs/simply-deploy.md` for secrets og første deploy.
- Deploy synkroniserer hele sitet og sletter alt, der ikke er i build'et (undtagen `App_Data` og `logs`). `SIMPLY_WEBDEPLOY_SITE` afgør, hvilket site der overskrives – ret aldrig den eller `_URL` uden at brugeren bekræfter værdierne. `deploy.yml` afviser andre sites end `kultur.kaspert.dk` – fjern ikke det værn.
- Hemmeligheder kommer fra GitHub Secrets (environment `Production`) → `appsettings.Production.json` i deploy-workflowet. Aldrig i repoet. Navnene `SIMPLY_WEBDEPLOY_PASSEORD` og `CLAUDE_CODE_0AUTH_TOKEN` er stavet sådan med vilje (de matcher secrets i read-repoet) – "ret" dem ikke.
- Simplys firewall (ModSecurity) afviser curl med 455; brug `curl -A "Mozilla/5.0" …` ved røgtest.
- Arbejdsgang: branch → PR → CI grøn → merge → auto-deploy.
