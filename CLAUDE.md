# Kultur – noter til Claude

- Konceptet står i `docs/koncept.md`. Læs det før større ændringer, og hold det opdateret.
- Samme stil og teknologi som Læs højt (`ktroelsen/read`): ASP.NET Core minimal API (.NET 10) + React/Vite/Tailwind PWA i `web/`.
- Backend: `dotnet build -c Release -warnaserror` og `dotnet test` skal være grønne. I Git Bash: brug `-warnaserror`, ikke `/warnaserror`.
- Frontend: `npm --prefix web run lint` og `npm --prefix web run build`.
- UI-tekster er på dansk; kode og kommentarer på engelsk.
- Ingen database: data er JSON-filer i `App_Data`, pr. bruger under `App_Data/users/{id}/`. App_Data skippes ved deploy, så data overlever.
- Kilder er en fast liste i koden; klienten vælger en nøgle, aldrig en URL. Hent kun fra sider, hvis brugervilkår tillader det. Omtaler gengives som stjerner + kilde + link, aldrig anmeldelsens tekst.
- Claude (`Ai:ApiKey`) bruges til rangering, profil og udtræk fra HTML, med dagligt udgiftsloft. Uden nøgle er AI slået fra.
- Natlig scanning: GitHub Actions-cron kalder `POST /api/scan` med en hemmelig nøgle (IIS genbruger app-poolen, så ingen baggrundstjeneste).
- Hosting er Simply (IIS, 32-bit, shared, self-contained win-x86) på kultur.kaspert.dk – samme opsætning som read.kaspert.dk.
- Deploy synkroniserer hele sitet og sletter alt, der ikke er i build'et (undtagen `App_Data` og `logs`). `SIMPLY_WEBDEPLOY_SITE` afgør, hvilket site der overskrives – ret aldrig den eller `_URL` uden at brugeren bekræfter værdierne.
- Hemmeligheder kommer fra GitHub Secrets (environment `Production`) → `appsettings.Production.json` i deploy-workflowet. Aldrig i repoet. Navnene `SIMPLY_WEBDEPLOY_PASSEORD` og `CLAUDE_CODE_0AUTH_TOKEN` er stavet sådan med vilje (de matcher secrets i read-repoet) – "ret" dem ikke.
- Simplys firewall (ModSecurity) afviser curl med 455; brug `curl -A "Mozilla/5.0" …` ved røgtest.
- Arbejdsgang: branch → PR → CI grøn → merge → auto-deploy.
