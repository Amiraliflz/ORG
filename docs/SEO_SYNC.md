# SEO route catalog sync

Keep programmatic SEO pages (`/routes/{slug}`, `/cities/{slug}`, sitemaps) aligned with **bookable** OD pairs from ORS.

## Source of truth

`GET {MrShoofer:ApiBaseUrl}/Directions/getAvailableDirections`

Do **not** invent city×city pages. Only ODs the product can sell are indexed.

## Sync command

From the repo root:

```bash
dotnet run --project tools/SeoSync
```

Options:

| Flag | Meaning |
|------|---------|
| *(none)* | Fetch ORS AvailableDirections (default). On failure, falls back to `Directions.json`. |
| `--from-directions` | Build only from `wwwroot/json/Directions/Directions.json` (offline). |
| `--api https://ors.shoofer.taxi` | Override API base URL. |

Writes (commit these):

- `wwwroot/json/Seo/routes.generated.json`
- `wwwroot/json/Seo/cities.generated.json`
- `wwwroot/json/Seo/catalog.generated.json` (debug snapshot)

Unresolved transliterations are printed and listed under `unresolvedSlugs` in the routes file — add them to `SeoSlugHelper` known map when you want stable Latin slugs.

## Workflow: sync → commit → deploy → Search Console

1. **When network/route set changes** (new city or OD in ORS): run `dotnet run --project tools/SeoSync`.
2. **Review**: skim `unresolvedSlugs`, spot-check a non-Tehran route (e.g. `/routes/isfahan-bandarabbas` if present).
3. **Commit** the three generated JSON files (and any slug-map edits).
4. **Deploy** (`./deploy.sh --full` or your usual pipeline). Catalog is file-based — no ORS call at request time.
5. **GSC / Bing**: after deploy, submit the sitemap index (see below).

## Google Search Console checklist

Property: `sale.shoofer.taxi` (must be verified).

### Submit sitemap

1. Open [Search Console → Sitemaps](https://search.google.com/search-console).
2. Submit **only** the index:
   - `https://sale.shoofer.taxi/sitemap.xml`
3. Do **not** also submit `sitemap-pages.xml` / `sitemap-routes.xml` / `sitemap-cities.xml` — they are already linked from the index.
4. Wait until status is **Success**. “Couldn’t fetch” usually means CDN/cache or a transient 5xx — retest after deploy settles.

`robots.txt` already declares the same Sitemap URL.

### URL Inspection (priority URLs)

Request indexing for a few money pages so Google doesn’t wait only on discovery:

- `https://sale.shoofer.taxi/`
- One primary route, e.g. `https://sale.shoofer.taxi/routes/tehran-isfahan`
- One city hub, e.g. `https://sale.shoofer.taxi/cities/tehran`

Do not mass-request every route URL (quota-limited). Let the sitemap cover the long tail.

### What to expect in Coverage

| Signal | Expected |
|--------|----------|
| `/routes/*`, `/cities/*` | Indexable SSR HTML (`index, follow`) |
| `/TaxiTrips` | Excluded via `robots.txt` Disallow — intentional |
| Trip cards on route landings | Loaded via JS — SEO body/FAQ still in HTML |

### HEAD probes

Sitemap and SEO pages accept **GET and HEAD**. If HEAD previously returned 500, that was a method-constraint + missing error view issue — fixed in app; redeploy for production.

## Runtime behavior

- `RouteCatalog` / `CityCatalog` load generated JSON from `wwwroot`.
- Hand blurbs in `CityCatalog` overlay matching cities; others get safe stubs (`CityStubFactory`).
- Hand route copy in `wwwroot/json/Seo/routes.overlays.json` (money routes) merges over generated `RouteContent` — see `docs/superpowers/specs/2026-08-06-route-copy-overlays-design.md`.
- Normal `/TaxiTrips` search attaches the SEO footer when the OD is in the catalog (`AttachRouteSeoIfCatalogMatch`).
- Tehran hubs keep richer hand copy; new cities degrade gracefully to stubs.

## Hand route overlays

Edit `wwwroot/json/Seo/routes.overlays.json` keyed by slug (e.g. `tehran-isfahan`). Any non-empty field replaces the generated template for that page. Omit fields you want to leave auto-generated. After edit, restart the app (lazy-loaded once per process).

## Optional CI check

Fail the build if:

- `routes.generated.json` is missing or `routes` is empty, or
- duplicate `slug` values appear in the routes array.
