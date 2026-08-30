#!/usr/bin/env python3
"""Generate MapBook cities.json + city-borders.json from ORS SEO catalog + Nominatim."""
import json
import math
import time
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SEO_CITIES = ROOT / "wwwroot/json/Seo/cities.generated.json"
OUT_CITIES = ROOT / "wwwroot/data/iran/cities.json"
OUT_BORDERS = ROOT / "wwwroot/data/iran/city-borders.json"
CACHE = ROOT / "tools/.mapbook-cities-cache.json"

# Province hints for cities where Nominatim state is vague
PROVINCE_OVERRIDES = {
    "تهران": "تهران",
    "اصفهان": "اصفهان",
    "مشهد": "خراسان رضوی",
    "شیراز": "فارس",
    "تبریز": "آذربایجان شرقی",
    "کرج": "البرز",
    "قم": "قم",
    "اهواز": "خوزستان",
    "کرمانشاه": "کرمانشاه",
    "رشت": "گیلان",
    "یزد": "یزد",
    "کرمان": "کرمان",
    "همدان": "همدان",
    "ارومیه": "آذربایجان غربی",
    "اردبیل": "اردبیل",
    "زنجان": "زنجان",
    "ساری": "مازندران",
    "گرگان": "گلستان",
    "سنندج": "کردستان",
    "ایلام": "ایلام",
    "بوشهر": "بوشهر",
    "بندرعباس": "هرمزگان",
    "زاهدان": "سیستان و بلوچستان",
    "شهرکرد": "چهارمحال و بختیاری",
    "یاسوج": "کهگیلویه و بویراحمد",
    "فرودگاه امام خمینی": "تهران",
    "فرودگاه وان": "آذربایجان غربی",
    "وان": "آذربایجان غربی",
    "اسلامشهر": "تهران",
    "شهر ری": "تهران",
    "شهرقدس": "تهران",
    "بهارستان": "تهران",
    "فشم": "تهران",
    "دماوند": "تهران",
    "سپاهان شهر": "اصفهان",
    "فولادشهر": "اصفهان",
    "خمینی شهر": "اصفهان",
    "نجف آباد": "اصفهان",
    "مبارکه": "اصفهان",
}

MAJOR = {
    "tehran", "isfahan", "mashhad", "shiraz", "tabriz", "karaj", "ahvaz",
    "qom", "kermanshah", "rasht", "yazd", "kerman", "hamedan", "urmia",
    "ardabil", "bandarabbas", "zahedan", "gorgan", "sari", "kashan",
    "qazvin", "sanandaj", "chalus", "nowshahr", "ramsar", "lahijan",
}


def nominatim(query: str):
    params = urllib.parse.urlencode({
        "q": query,
        "countrycodes": "ir",
        "format": "json",
        "limit": 1,
        "accept-language": "fa",
        "polygon_geojson": 1,
        "dedupe": 1,
    })
    url = f"https://nominatim.openstreetmap.org/search?{params}"
    req = urllib.request.Request(url, headers={"User-Agent": "MrShooferORG-MapBookGen/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode())


def radius_km(slug: str, name: str) -> float:
    if slug in {"tehran", "mashhad", "isfahan", "shiraz", "tabriz", "karaj", "ahvaz"}:
        return 22.0
    if slug in MAJOR:
        return 18.0
    if "فرودگاه" in name:
        return 8.0
    return 14.0


def province_from_hit(name: str, hit: dict) -> str:
    if name in PROVINCE_OVERRIDES:
        return PROVINCE_OVERRIDES[name]
    addr = hit.get("address") or {}
    for key in ("state", "province", "county", "city"):
        val = addr.get(key)
        if val and isinstance(val, str):
            return val.replace("استان ", "").strip()
    disp = hit.get("display_name") or ""
    parts = [p.strip() for p in disp.split(",")]
    for p in parts:
        if "استان" in p:
            return p.replace("استان", "").strip()
    return parts[-2] if len(parts) >= 2 else "ایران"


def simplify_polygon(coords, max_points=800):
    if not coords or len(coords) <= max_points:
        return coords
    step = max(1, len(coords) // max_points)
    out = coords[::step]
    if out[-1] != coords[-1]:
        out.append(coords[-1])
    return out


def main():
    seo = json.loads(SEO_CITIES.read_text(encoding="utf-8"))
    cache = {}
    if CACHE.exists():
        cache = json.loads(CACHE.read_text(encoding="utf-8"))

    cities_out = []
    features = []

    for i, c in enumerate(seo["cities"]):
        name = c["nameFa"]
        slug = c["slug"]
        key = slug
        print(f"[{i+1}/{len(seo['cities'])}] {name} ({slug})")

        hit = cache.get(key)
        if not hit:
            query = name if "فرودگاه" in name else f"{name}, ایران"
            try:
                results = nominatim(query)
                hit = results[0] if results else None
            except Exception as e:
                print(f"  WARN geocode failed: {e}")
                hit = None
            cache[key] = hit
            CACHE.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")
            time.sleep(1.1)

        if not hit:
            print("  SKIP — no geocode hit")
            continue

        lat = float(hit["lat"])
        lng = float(hit["lon"])
        province = province_from_hit(name, hit)

        cities_out.append({
            "id": slug,
            "name": name,
            "province": province,
            "lat": round(lat, 6),
            "lng": round(lng, 6),
            "radiusKm": radius_km(slug, name),
            "cityId": c.get("cityId"),
        })

        geo = hit.get("geojson")
        if geo and geo.get("type") in ("Polygon", "MultiPolygon"):
            geom = geo
            if geom["type"] == "Polygon":
                coords = geom["coordinates"]
                if coords and coords[0]:
                    coords[0] = simplify_polygon(coords[0])
                geom = {"type": "Polygon", "coordinates": coords}
            elif geom["type"] == "MultiPolygon":
                polys = []
                for poly in geom.get("coordinates") or []:
                    if poly and poly[0]:
                        polys.append([simplify_polygon(poly[0])] + poly[1:])
                geom = {"type": "MultiPolygon", "coordinates": polys}

            features.append({
                "type": "Feature",
                "properties": {
                    "id": slug,
                    "nameFa": name,
                    "osmType": hit.get("osm_type"),
                    "osmId": hit.get("osm_id"),
                    "displayName": hit.get("display_name"),
                },
                "geometry": geom,
            })
            print(f"  border OK ({geom['type']})")
        else:
            print("  no polygon — circle fallback")

    cities_out.sort(key=lambda x: x["name"])
    OUT_CITIES.write_text(
        json.dumps({"cities": cities_out}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    OUT_BORDERS.write_text(
        json.dumps({"type": "FeatureCollection", "features": features}, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"\nWrote {len(cities_out)} cities, {len(features)} borders")


if __name__ == "__main__":
    main()
