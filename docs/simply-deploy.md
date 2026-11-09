# Deploy til Simply (kultur.kaspert.dk)

Samme opsætning som Læs højt (read.kaspert.dk) – se `ktroelsen/read/docs/simply-deploy.md` for
erfaringerne bag. Her står det, der er specielt for Kultur.

## Flow

```
push/merge til main ─► CI (ubuntu) ─► grøn? ─► Deploy to Simply (windows) ─► kultur.kaspert.dk
                       lint, build, test        workflow_run på CI (eller manuelt)

hver nat 02:17 UTC ─► Nightly scan ─► POST https://kultur.kaspert.dk/api/scan (X-Scan-Key)
```

- **`ci.yml`**: `npm ci`, `npm run lint`, `npm run build`, `dotnet build -c Release -warnaserror`, `dotnet test`.
- **`deploy.yml`**: bygger frontend ind i `wwwroot`, publisher self-contained win-x86, skriver
  `appsettings.Production.json` fra secrets og synkroniserer med `msdeploy -verb:sync -enableRule:AppOffline`.
  **Sync sletter alt på sitet, der ikke er i build'et** (undtagen `App_Data` og `logs`). Derfor
  stopper jobbet, hvis en af Web Deploy-secrets mangler, eller hvis `SIMPLY_WEBDEPLOY_SITE` ikke er
  præcis `kultur.kaspert.dk`.
- **`scan.yml`**: natlig scanning (IIS genbruger app-poolen, så der er ingen baggrundstjeneste).
  Kan også startes manuelt under Actions, eller med "Led efter nyt nu" i appen.
- **`claude.yml`**: svarer på `@claude` i issues og PR-kommentarer.

## Før første deploy

1. Opret sitet `kultur.kaspert.dk` hos Simply med Web Deploy slået til (som read.kaspert.dk).
2. Opret environment **`Production`** i repoets Settings → Environments, og læg disse secrets der.

| Secret | Værdi | Bruges til |
|---|---|---|
| `SIMPLY_WEBDEPLOY_URL` | serveren (som for read) | msdeploy (`https://<url>:8172/msdeploy.axd`), uden `https://` og port |
| `SIMPLY_WEBDEPLOY_SITE` | `kultur.kaspert.dk` | **IIS-sitet, der overskrives.** Andre værdier afvises af workflowet. |
| `SIMPLY_WEBDEPLOY_USERNAME` | (fra Simply, for kultur-sitet) | Web Deploy-login |
| `SIMPLY_WEBDEPLOY_PASSWORD` | (fra Simply) | Web Deploy-adgangskode (korrekt stavet – i read hedder den `PASSEORD`) |
| `KULTUR_PASSWORD` | valgfrit kodeord | Login i appen |
| `KULTUR_SCAN_KEY` | lang tilfældig streng | Den natlige scanning (`X-Scan-Key`) |
| `ANTHROPIC_API_KEY` | `sk-ant-…` | Claude: løb (ugentligt opslag med websøgning). Uden den springes løb over. |
| `TICKETMASTER_API_KEY` | "Consumer Key" fra developer.ticketmaster.com | Ticketmaster-kilden. Uden den springes kilden over. |
| `CLAUDE_CODE_0AUTH_TOKEN` | (som i read) | `@claude` i issues/PR'er |

Røgtest efter deploy (Simplys firewall afviser curls egen User-Agent med 455):

```
curl -A "Mozilla/5.0" https://kultur.kaspert.dk/api/health
```

`ticketmaster`, `scanKey` og `ai` skal være `true`.

## Lokalt

```
dotnet run --project src/Kultur.Api        # http://localhost:5280, kodeord "dev"
npm --prefix web run dev                    # http://localhost:5175 (proxy til API'et)
dotnet user-secrets --project src/Kultur.Api set "Ticketmaster:ApiKey" "<nøgle>"
```
