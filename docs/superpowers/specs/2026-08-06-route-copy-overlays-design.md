# Design: Hand-polished money-route SEO copy overlays

## Goal

SEO uniqueness for top money route pages (`/routes/{slug}` and search SEO footer). Full-page hand copy where present; generated `RouteContent` elsewhere.

## Approach

JSON overlays at `wwwroot/json/Seo/routes.overlays.json`, keyed by route slug. `RouteContent.For()` builds the template bundle, then merges non-empty overlay fields.

## v1 scope

The 12 slugs in `SeoDefaults.HomepagePopularRouteSlugs`.

## Merge rules

- Overlay field present and non-empty → replace.
- Empty / omitted → keep generated text.
- Prefer catalog travel minutes when writing `travelInfo`; no invented prices or hard arrival guarantees.
- If `aboutBlocks` is set, recompute `aboutCorridor` as joined block texts.

## Copy rules

- Corridor-specific angle in intro (not interchangeable city names).
- H1 keeps `{origin} به {destination}` for query match.
- Meta ≈ ≤160 chars.
- FAQs unique per route (4–6).

## Non-goals (v1)

- City-hub polish
- Conversion-first tone
- Per-route OG images
