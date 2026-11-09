# Kultur – koncept

En personlig app, der **finder**, **forstår** og **holder øje med** oplevelser: koncerter, teater,
stand-up, foredrag, film-særvisninger, udendørs/sommerspil, sport og løb. Over tid lærer den, hvad jeg er
interesseret i. Kører på https://kultur.kaspert.dk i samme stil og teknologi som Læs højt
(`ktroelsen/read`).

## Brugeren

- Som udgangspunkt én bruger (mig), med fast base i **København**.
- Data lægges pr. bruger (`App_Data/users/{id}/`), så flere brugere senere kan komme til uden omskrivning.
- Besked sker **i appen** (ingen mail/push i første omgang).

## Tre kerneidéer

1. **Finder** – henter løbende arrangementer fra faste kilder og normaliserer dem til ét katalog.
2. **Forstår** – en smagsprofil, der bliver skarpere jo mere appen bruges.
3. **Holder øje** – overvågninger ("sig til når …"), især annonceringer og billetsalgsstart.

## Afstandszoner

Interessen skal være større, jo længere væk noget er:

| Zone | Område | Vises når |
|---|---|---|
| 1 | Storkøbenhavn | alt, der matcher rimeligt |
| 2 | Resten af Danmark (+ evt. Malmø/Lund) | stærkt match |
| 3 | Udlandet | kun meget stærkt match (fx yndlingskunstner der ikke kommer til DK) – plus en rejsesøgning: "Jeg er i Berlin 12.–15. marts" |

## Smagsprofil

To dele, fordi smag i kultur og sport opfører sig forskelligt:

- **Kultur:** kunstnere, genrer, spillesteder, stemning. Startes fra **Spotify** (top-kunstnere
  kort/mellem/lang sigt + fulgte kunstnere), opdateres af signaler i appen.
- **Sport:** hold, sportsgrene og begivenhedstype – skrives eksplicit. Udgangspunkt:
  - **Fodbold:** Vejle Boldklub, landsholdet (herrer/damer).
  - **Håndbold:** landsholdene (herrer/damer).
  - **Badminton:** landsholdet (Thomas/Uber/Sudirman Cup), Denmark Open, VM/EM.
  - Slutrunder og finaler med uanset hold.
  - Regel: Vejles **udekampe i København** er zone 1 og vises altid; hjemmekampe i Vejle (zone 2)
    kun når de er særlige (mod FCK/Brøndby, pokal, afgørende kampe).

Signaler: **Gem**, **Måske**, **Nej tak**; senere **Jeg var der** og **Det var godt**.

**Smag** (bygget): en side, hvor emner kommer ét ad gangen – kategori, genre, kunstner/hold,
spillested – og vurderes fra *aldrig* til *meget*. Man kigger ind en gang imellem og tager et par
stykker. Først de brede kategorier; derefter skiftes mellem genrer, kunstnere (dobbelt så ofte) og
steder, de største først. Har man sagt *aldrig* til sport, spørges der ikke om fodboldklubber.
Vurderingerne giver point til arrangementerne, skjuler fravalgte og forklarer forslagene.

Profilen gemmes som JSON + en kort tekst i naturligt sprog, som Claude vedligeholder. Siden
**"Det tror jeg om dig"** viser profilen, og den kan rettes direkte. Hvert forslag har en
begrundelse ("fordi du gemte Agnes Obel og har været på Vega to gange").

## Rangering

1. Billigt grovfilter: dato, zone, pris (vises, intet loft – appen kan lære, hvis dyre billetter
   ofte fravælges).
2. Claude scorer de 50–100 bedste kandidater mod profilen (profilen i cachet prompt) og skriver
   begrundelsen. Dagligt udgiftsloft (`Ai:DailyTokenLimit` el.lign.), som i Læs højt.

## Datamodel (tre ting, der holdes adskilt)

1. **Arrangement** – dato, sted, kategori, pris, billetlink, kilde.
2. **Annoncering** – "X kommer til DK i 2027 / billetsalg fredag kl. 10". Findes *før*
   arrangementet er i billetsystemerne. Kobles til arrangementet, når det dukker op.
3. **Omtale** – stjerner + kilde + link (fx "★★★★★ GAFFA"). Vi gengiver ikke anmeldelsernes tekst.

## Kilder

Kilder er en fast liste i koden; klienten vælger en nøgle, aldrig en URL (som DR-feeds i Læs højt).
Kun sider, hvis brugervilkår tillader det.

| Niveau | Kilder | Brug |
|---|---|---|
| **A. Åbne API'er** | Ticketmaster Discovery API, Bandsintown (pr. kunstner), Spotify (profil), Billetto (*skal verificeres*) | Struktureret, hentes natligt |
| **B. Claude-læste feeds/sider** | Annonceringer: GAFFA "Offentliggørelser", Soundvenue, Devilution, Live Nation. Teater: Det Kgl. Teater, Teaterbilletter.dk, Scenen.dk, ISCENE, Kulturkupeen, CPHCulture, Danske Sommerspil, Grønnegårds Teatret, SHAKES. Comedy: FBI.dk, Tajmer.dk. Film: Cinemateket, Ekko. Sport: DBU, DHF, Badminton Danmark, BWF-kalenderen, Vejle Boldklub. | RSS hvor det findes (*skal verificeres*), ellers HTML → Claude → struktureret JSON |
| **C. Kræver partneraftale** | Eventim/Billetlugen B2B-feeds, Kino.dk, Nordisk Film | Ikke integreret – kun links |

**Film** passer dårligt som "arrangement" (en film går i uger). Kun særvisninger, premierer,
festivaler og IMAX-events tages med i første omgang.

**Videresalg:** når et gemt arrangement er udsolgt, kan appen henvise til officielt videresalg
(fanSALE, Ticketmaster Resale) – kun links.

## Foredrag og løb

- **Foredrag** (fx Original Talks) er en kategori for sig; Ticketmasters "Lecture/Seminar" og
  "Spoken Word" lander her.
- **Løb** – motionsløb i Danmark og store løb i udlandet (fx Berlin Marathon) – er også en kategori.
  For løb er *tilmeldingsstart* (eller lodtrækning) det, der svarer til billetsalg, og de ligger ofte
  langt ude i fremtiden, så de skal hentes mere end 12 måneder frem. Kilder: løbenes egne sider og
  danske tilmeldingssystemer (*skal undersøges*).

## Overvågninger

- Spotify-kunstnere på turné (automatisk, opdateres natligt).
- Egne overvågninger: kunstner, spillested, hold, kategori.
- **Billetsalgsstart** er førsteklasses – især for sport (landskampe, slutrunder, Denmark Open),
  der bliver udsolgt hurtigt.

## Skærme

- **Siden sidst** (forside): nyt siden sidste besøg – fund fra overvågninger, billetsalg der starter, nye forslag.
- **Til dig**: forslag med begrundelse og Gem / Måske / Nej tak.
- **Gemt**: mine gemte arrangementer, med `.ics`-knap til kalenderen.
- **Overvågninger**: liste + opret.
- **Det tror jeg om dig**: profilen, læsbar og redigerbar.
- Senere: **Søg** i naturligt sprog ("noget jazz i weekenden under 300 kr").

## Teknik

Samme opskrift som Læs højt:

- ASP.NET Core minimal API (.NET 10), React + Vite + Tailwind, installerbar PWA.
- Ingen database – JSON-filer i `App_Data` (skippes ved deploy).
- Claude til rangering, profil-vedligehold og udtræk fra HTML. Uden `Ai:ApiKey` slås det fra.
- Simply (IIS, 32-bit, shared, self-contained win-x86) på kultur.kaspert.dk.
- **Natlig scanning:** IIS genbruger app-poolen, så baggrundstjenester er upålidelige. En GitHub
  Actions-cron kalder `POST /api/scan` med en hemmelig nøgle; scanningen henter kilder, kører
  overvågninger og opdaterer forslag.
- Hemmeligheder fra GitHub Secrets (environment `Production`) → `appsettings.Production.json`.

## Versioner

1. **v1:** Ticketmaster + Bandsintown + Spotify-login, "Til dig", "Siden sidst", profilside,
   natlig scanning.
2. **v2:** Claude-læste kilder (annonceringer, teater, comedy, sport), billetsalgsstart-overvågning,
   søgning i naturligt sprog, `.ics`.
3. **v3:** Omtaler/stjerner, videresalg, film-særvisninger, sommerspil, evt. Billetto, flere brugere,
   ugentlig kulturpodcast (Gemini-opskriften fra Læs højt).

## Åbne spørgsmål

- Har Billetto en åben søge-API for ikke-arrangører?
- Hvilke af B-kilderne har RSS, og hvad siger deres brugervilkår om automatisk hentning?
- Spotify-udviklerapp: begrænsninger i development mode (fint for én bruger).
