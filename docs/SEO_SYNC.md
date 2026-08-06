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
5. **GSC / Bing**: after deploy, submit sitemap index  
   `https://sale.shoofer.taxi/sitemap.xml`  
   (includes `sitemap-routes.xml` and `sitemap-cities.xml`).

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
