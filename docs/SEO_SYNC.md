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

Property: `mrshoofer.com` (must be verified). Keep `mrshoofer.ir` verified for Change of address.

### Submit sitemap

1. Open [Search Console → Sitemaps](https://search.google.com/search-console).
2. On the **`.com`** property submit:
   - `https://mrshoofer.com/sitemap.xml`
3. On the **`.ir`** property (keep verified for Change of address) also submit:
   - `https://mrshoofer.ir/sitemap.xml`
   - Nginx proxies sitemap/robots on `.ir` without redirect; `<loc>` URLs inside still use `https://mrshoofer.com/...`.
4. Do **not** also submit child sitemaps individually — they are linked from the index.
5. Wait until status is **Success**. “Couldn’t fetch” usually means CDN/cache or a transient 5xx — retest after deploy settles.

Child sitemaps (linked from index):

| File | Contents |
|------|----------|
| `sitemap-pages.xml` | Home, /routes, /cities, FAQ, Contact, … |
| `sitemap-routes.xml` | `/routes/{slug}` booking landings (412) |
| `sitemap-guides.xml` | `/routes/{slug}/guide` content-only guides (412) |
| `sitemap-cities.xml` | `/cities/{slug}` (76) |

`robots.txt` declares both `.com` and `.ir` sitemap URLs.

### URL Inspection (priority URLs)

Request indexing for a few money pages so Google doesn’t wait only on discovery:

- `https://mrshoofer.com/`
- One primary route, e.g. `https://mrshoofer.com/routes/tehran-isfahan`
- One city hub, e.g. `https://mrshoofer.com/cities/tehran`

Do not mass-request every route URL (quota-limited). Let the sitemap cover the long tail.

### What to expect in Coverage

| Signal | Expected |
|--------|----------|
| `/routes/*`, `/cities/*` | Indexable SSR HTML (`index, follow`) |
| `/TaxiTrips` | Excluded via `robots.txt` Disallow — intentional |
| Trip cards on route landings | Loaded via JS — SEO body/FAQ still in HTML |

### HEAD probes

Sitemap and SEO pages accept **GET and HEAD**. If HEAD previously returned 500, that was a method-constraint + missing error view issue — fixed in app; redeploy for production.

## Domain migration (.ir → .com)

Config: `appsettings.json` → `Seo:PreferredOrigin` = `https://mrshoofer.com`, `Seo:LegacyHosts` lists hosts that 301 to `.com`.

After deploy:

1. **ArvanCloud / nginx** — `mrshoofer.com` must return **200** (not 504). Legacy hosts 301 to `.com`. See `deploy/nginx/mrshoofer-public.conf`.
2. **Verify** — `./deploy/scripts/verify-domain-migration.sh` (all checks green).
3. **GSC** — Submit `https://mrshoofer.com/sitemap.xml` on the `.com` property.
4. **Primary migration path (2026-08):** rely on **301s + .com sitemap/canonicals**. Keep `.ir` redirects live for **12+ months** (ideally indefinitely).
5. **Change of address** — optional accelerator only. On **`.ir` Domain property** → Settings → Change of address → `.com`. If the wizard says “Couldn't fetch http://mrshoofer.ir/” while URL Inspection / Googlebot still get 301s, **do not redesign redirects** — continue with phase 4 above.

### Migration strategy (locked)

Do **not** change ASP.NET routing, `.com` canonicals, `.com` sitemap, or one-hop Nginx `.ir` → `.com` redirects.

| Phase | Action |
|-------|--------|
| Now | Keep equivalent-path 301s (e.g. `/routes/tehran-isfahan` → same path on `.com`) |
| GSC `.com` | Sitemap submitted; request indexing for key URLs as needed |
| Monitor | `.com` indexed pages / impressions; `.ir` indexed pages declining |
| Optional | One last CoA try via Arvan **edge** redirect; if still fails, abandon CoA |
| Never | Delete `.ir`, block `.ir` in robots, 302s, JS/meta redirects, sitemap with `.ir` locs, redirect all paths to `/` |

**Evidence (2026-08-23):** CoA validator did not reach origin (no Googlebot in `migration_debug.log`); InspectionTool/Googlebot UAs did reach Nginx and received clean 301. CoA is not required for the move.

### Change of address — why the wizard fails

The `.com` sitemap can be **Success** while Change of address still fails. Google’s CoA crawler may be unable to fetch `http://mrshoofer.ir/` at the CDN edge even when Inspection Tool succeeds.

**Verify anytime:** `./deploy/scripts/verify-domain-migration.sh` and (while diagnosing) `/var/log/nginx/migration_debug.log`.

**Temporary debug logging:** `deploy/nginx/conf.d/00-migration-debug-format.conf` + `access_log … migration_debug` on `.ir` vhosts — remove after diagnosis.

**Still failing CoA after redirects are green?** Usually Arvan edge vs CoA fetcher — not wrong Nginx 301s. Optional experiment: Arvan page-rule 301 for page paths only (keep `/robots.txt` + `/sitemap*.xml` as 200 on origin). If CoA still fails, stop spending time on the tool.

**GSC property setup (same Google account, Owner on both):**

| Step | Action |
|------|--------|
| 1 | Domain properties: `sc-domain:mrshoofer.ir` and `sc-domain:mrshoofer.com` |
| 2 | Live-test `http://mrshoofer.ir/` / robots on `.ir` when debugging CoA |
| 3 | Optional: Change of address on `.ir` → `.com` |
| 4 | If CoA fails: proceed with 301-only migration |

Per [Google’s docs](https://support.google.com/webmasters/answer/9370220): Change of address helps; **301s + recrawl** are what transfer the site.

### GSC “Page indexing” reports (`.ir` Domain property)

| Report | Example | Verdict | Action |
|--------|---------|---------|--------|
| **Redirect error** | `/routes/…/guide`, `/routes/tehran-mashhad` on `.ir` | Live: single 301 → matching `.com` **200** | Stale/crawl glitch. URL Inspection → Validate fix. Index the `.com` URL. |
| **Blocked by robots.txt** | `https://pay.mrshoofer.ir/` | Payment host under Domain property | Root may be crawled to follow 301; `/pg/` + `/Payment/` stay Disallow; all pay responses `noindex`. |
| **Page with redirect** | `http://www.mrshoofer.ir/` | Correct migration | Expected. Do not “fix”. |

Do **not** open payment paths to indexing, remove `.ir` 301s, or block `.ir` in robots to “clear” these reports.

## Runtime behavior

- `RouteCatalog` / `CityCatalog` load generated JSON from `wwwroot`.
- Hand blurbs in `CityCatalog` overlay matching cities; others get safe stubs (`CityStubFactory`).
- Hand route copy in `wwwroot/json/Seo/routes.overlays.json` (money routes) merges over generated `RouteContent` — see `docs/superpowers/specs/2026-08-06-route-copy-overlays-design.md`.
- Normal `/TaxiTrips` search attaches the SEO footer when the OD is in the catalog (`AttachRouteSeoIfCatalogMatch`).
- **Route guides** at `/routes/{slug}/guide` — same long-form copy as the trip-page footer, content-only for crawlers; listed in `sitemap-guides.xml`.
- Tehran hubs keep richer hand copy; new cities degrade gracefully to stubs.

## Free backlinks & outreach

See [BACKLINKS.md](./BACKLINKS.md) for directories, GSC steps, and safe link targets.

## Hand route overlays

Edit `wwwroot/json/Seo/routes.overlays.json` keyed by slug (e.g. `tehran-isfahan`). Any non-empty field replaces the generated template for that page. Omit fields you want to leave auto-generated. After edit, restart the app (lazy-loaded once per process).

## Optional CI check

Fail the build if:

- `routes.generated.json` is missing or `routes` is empty, or
- duplicate `slug` values appear in the routes array.
