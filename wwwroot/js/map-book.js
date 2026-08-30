/**
 * MapBook — Snapp-style map booking UX.
 * Flow: city → pin in border → city → pin → route anim → car class → date/time → Reservetrip
 */
(function () {
  'use strict';

  var ORIGIN_COLOR = '#6289E5';
  var DEST_COLOR = '#4AADA4';
  var SUGGESTED_CITY_LIMIT = 8;
  /* Cool muted roads — sit with Neshan light basemap + Shoofer ink */
  var HWY_MOTORWAY = '#4a5568';
  var HWY_PRIMARY = '#718096';
  var HWY_SECONDARY = '#a0aec0';
  var REDUCE_MOTION = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var MAP_BOUNDS_IRAN = [[44.0, 24.8], [63.4, 39.9]];
  var MAP_BOUNDS_EXTENDED = [[42.4, 24.8], [63.4, 39.9]]; // Van (Turkey) cross-border routes

  var state = {
    step: 1,
    cities: [],
    provinces: [],
    provinceFilter: null,
    cityBorders: Object.create(null),
    originCity: null,
    destCity: null,
    originLatLng: null,
    destLatLng: null,
    originZones: null,
    destZones: null,
    carClass: null,
    classPrices: {},
    tripPlanCode: null,
    dateJalali: null,
    picking: null, // 'origin' | 'dest' | null
    routeCoords: null,
    routeAnim: null,
    routeSource: null,
    nativeRouteLayer: false,
    trafficOn: false,
    trafficUserTouched: false,
    zonesLoaded: false,
    sheetCollapsed: false,
    sheetPinMode: null, // null | 'map' | 'confirm'
    pendingDestName: null,
    originLabel: null,
    destLabel: null,
    buildingPlaqueItems: [],
    activeVenue: null,
    selectedVenueEntranceId: null,
    publicVenues: []
  };

  var els = {};
  var map, routeCanvas, originMarker, destMarker;
  var mapReady = false;
  var pendingAfterMapLoad = [];
  var nmp = null;

  document.addEventListener('DOMContentLoaded', init);

  async function init() {
    var root = document.getElementById('mapBookApp');
    if (!root) return;

    cacheEls(root);
    bindUi();
    syncStepsUi();
    syncSheetMode();

    // Cities + borders (borders load from cache/URL — not inlined in HTML)
    applyCities(readCitiesFast(root));
    applyCityBorders(readCityBordersFast(root));
    applyPublicVenues(readPublicVenuesFast(root));
    initMap();
    applyDeepLink(root);

    refreshCitiesCache(root).catch(function () { /* ignore */ });
    refreshCityBordersCache(root).catch(function () { /* ignore */ });
  }

  function findCityByName(name) {
    var q = normalizeFa(String(name || '').trim());
    if (!q) return null;
    var exact = null;
    var contains = null;
    for (var i = 0; i < state.cities.length; i++) {
      var c = state.cities[i];
      var n = normalizeFa(c.name);
      var p = normalizeFa(c.province || '');
      if (n === q) { exact = c; break; }
      if (!contains && (n.indexOf(q) !== -1 || q.indexOf(n) !== -1 || p.indexOf(q) !== -1)) contains = c;
    }
    return exact || contains;
  }

  function applyDeepLink(root) {
    var params = new URLSearchParams(window.location.search || '');
    var originName = (root.dataset.origin || params.get('origin') || params.get('originstring') || '').trim();
    var destName = (root.dataset.dest || params.get('dest') || params.get('destination') || params.get('destinationstring') || '').trim();
    if (destName) state.pendingDestName = destName;

    if (!originName || !state.cities.length) return;
    var city = findCityByName(originName);
    if (!city) return;

    // Skip city list → jump straight into origin pin for the deep-linked city
    setTimeout(function () {
      selectOriginCity(city);
    }, 80);
  }

  function cacheKey(kind, ver) {
    return 'mapbook:' + kind + ':' + (ver || '1');
  }

  function readLocalJson(key) {
    try {
      var raw = localStorage.getItem(key);
      if (!raw) return null;
      return JSON.parse(raw);
    } catch (e) {
      return null;
    }
  }

  function writeLocalJson(key, data) {
    try {
      localStorage.setItem(key, JSON.stringify(data));
    } catch (e) { /* quota / private mode */ }
  }

  /* Geo API cache — reverse, nearest road, route (session + memory) */
  var GEO_CACHE_TTL_MS = 15 * 60 * 1000;
  var geoMemCache = Object.create(null);
  var geoInflight = Object.create(null);

  function geoCoordKey(lat, lng, decimals) {
    decimals = decimals == null ? 3 : decimals;
    var f = Math.pow(10, decimals);
    return (Math.round(lat * f) / f) + '|' + (Math.round(lng * f) / f);
  }

  function cityNeedsExtendedBounds(city) {
    if (!city) return false;
    var id = String(city.id || '');
    return id === 'van' || id === 'van-airport';
  }

  function updateMapBounds() {
    if (!map) return;
    var extended = cityNeedsExtendedBounds(state.originCity) ||
      cityNeedsExtendedBounds(state.destCity) ||
      (state.originLatLng && state.originLatLng.lng < 43.8) ||
      (state.destLatLng && state.destLatLng.lng < 43.8);
    try {
      map.setMaxBounds(extended ? MAP_BOUNDS_EXTENDED : MAP_BOUNDS_IRAN);
    } catch (e) { /* ignore */ }
  }

  function routePayloadIsReal(data, o, d) {
    if (!data || !data.path || data.path.length < 2) return false;
    if (data.source === 'fallback') return false;
    if (!o || !d) return data.path.length >= 8;
    var straight = mapDistanceMeters(o, d);
    if (straight > 6000 && data.path.length < 10) return false;
    if (straight > 20000 && data.path.length < 24) return false;
    return true;
  }

  function purgeGeoCache(kind, key) {
    var storeKey = kind + ':' + key;
    delete geoMemCache[storeKey];
    try { sessionStorage.removeItem('mbGeo:' + storeKey); } catch (e) { /* ignore */ }
  }

  function geoRouteKey(o, d) {
    return geoCoordKey(o.lat, o.lng, 4) + '>' + geoCoordKey(d.lat, d.lng, 4);
  }

  function readGeoCache(kind, key) {
    var storeKey = kind + ':' + key;
    var entry = geoMemCache[storeKey];
    if (entry && Date.now() - entry.at < GEO_CACHE_TTL_MS) return entry.data;
    try {
      var raw = sessionStorage.getItem('mbGeo:' + storeKey);
      if (!raw) return undefined;
      var parsed = JSON.parse(raw);
      if (!parsed || Date.now() - parsed.at >= GEO_CACHE_TTL_MS) return undefined;
      geoMemCache[storeKey] = parsed;
      return parsed.data;
    } catch (e) {
      return undefined;
    }
  }

  function writeGeoCache(kind, key, data) {
    var storeKey = kind + ':' + key;
    var entry = { at: Date.now(), data: data };
    geoMemCache[storeKey] = entry;
    try {
      sessionStorage.setItem('mbGeo:' + storeKey, JSON.stringify(entry));
    } catch (e) { /* quota */ }
  }

  async function fetchGeoCached(kind, key, fetcher) {
    var cached = readGeoCache(kind, key);
    if (cached !== undefined) return cached;
    var inflightKey = kind + ':' + key;
    if (geoInflight[inflightKey]) return geoInflight[inflightKey];
    geoInflight[inflightKey] = (async function () {
      var data = await fetcher();
      if (data !== undefined && data !== null) writeGeoCache(kind, key, data);
      return data;
    })();
    try {
      return await geoInflight[inflightKey];
    } finally {
      delete geoInflight[inflightKey];
    }
  }

  function parseInlineJson(id) {
    var el = document.getElementById(id);
    if (!el) return null;
    var text = (el.textContent || '').trim();
    if (!text) return null;
    try {
      return JSON.parse(text);
    } catch (e) {
      return null;
    }
  }

  function readCitiesFast(root) {
    var ver = root.dataset.cacheVer || '1';
    var inline = parseInlineJson('mbCitiesData');
    if (inline && inline.cities && inline.cities.length) {
      writeLocalJson(cacheKey('cities', ver), inline);
      return inline;
    }
    var cached = readLocalJson(cacheKey('cities', ver));
    if (cached && cached.cities && cached.cities.length) return cached;
    return { cities: [] };
  }

  function applyCities(data) {
    state.cities = (data && data.cities) || [];
    state.provinces = unique(state.cities.map(function (c) { return c.province; })).sort(faSort);
    if (!els.cityList) return;
    if (!state.cities.length) {
      els.cityList.innerHTML = '<li><button type="button" disabled>در حال بارگذاری شهرها…</button></li>';
      return;
    }
    renderProvinces();
    renderCityList(els.cityList, els.citySearch.value, false);
  }

  function readCityBordersFast(root) {
    var ver = root.dataset.cacheVer || '1';
    var cached = readLocalJson(cacheKey('cityBorders', ver));
    if (cached && cached.features && cached.features.length) return cached;
    var inline = parseInlineJson('mbCityBordersData');
    if (inline && inline.features && inline.features.length) {
      writeLocalJson(cacheKey('cityBorders', ver), inline);
      return inline;
    }
    return { type: 'FeatureCollection', features: [] };
  }

  function applyCityBorders(data) {
    state.cityBorders = Object.create(null);
    var feats = (data && data.features) || [];
    feats.forEach(function (f) {
      var id = f && f.properties && f.properties.id;
      if (id && f.geometry) state.cityBorders[id] = f;
    });
    if (state.picking && mapReady) {
      var activeCity = state.picking === 'origin' ? state.originCity : state.destCity;
      if (activeCity) {
        updateBoundVisual();
        if (getCityBorderFeature(activeCity)) fitToCityBorder(activeCity);
      }
    }
  }

  function readPublicVenuesFast(root) {
    var ver = root.dataset.cacheVer || '1';
    var inline = parseInlineJson('mbPublicVenuesData');
    if (inline && inline.venues && inline.venues.length) {
      writeLocalJson(cacheKey('publicVenues', ver), inline);
      return inline;
    }
    var cached = readLocalJson(cacheKey('publicVenues', ver));
    if (cached && cached.venues && cached.venues.length) return cached;
    return { venues: [] };
  }

  function applyPublicVenues(data) {
    state.publicVenues = (data && data.venues) || [];
  }

  function findPublicVenueAt(lat, lng) {
    if (!state.publicVenues || !state.publicVenues.length) return null;
    for (var i = 0; i < state.publicVenues.length; i++) {
      var v = state.publicVenues[i];
      if (!v || !v.polygon || !v.polygon.length) continue;
      if (pointInRing(lng, lat, v.polygon)) return v;
    }
    return null;
  }

  async function refreshCityBordersCache(root) {
    var url = (root.dataset.cityBordersUrl || '').trim();
    if (!url) return;
    var ver = root.dataset.cacheVer || '1';
    var res = await fetch(url + (url.indexOf('?') >= 0 ? '&' : '?') + 'v=' + encodeURIComponent(ver), {
      credentials: 'same-origin',
      cache: 'force-cache'
    });
    if (!res.ok) return;
    var data = await res.json();
    if (!data || !data.features || !data.features.length) return;
    writeLocalJson(cacheKey('cityBorders', ver), data);
    applyCityBorders(data);
  }

  async function refreshCitiesCache(root) {
    var url = root.dataset.citiesUrl;
    if (!url) return;
    var ver = root.dataset.cacheVer || '1';
    var res = await fetch(url + (url.indexOf('?') >= 0 ? '&' : '?') + 'v=' + encodeURIComponent(ver), {
      credentials: 'same-origin',
      cache: 'no-store'
    });
    if (!res.ok) return;
    var data = await res.json();
    if (!data || !data.cities || !data.cities.length) return;
    writeLocalJson(cacheKey('cities', ver), data);
    var nextIds = data.cities.map(function (c) { return c.id; }).join(',');
    var curIds = state.cities.map(function (c) { return c.id; }).join(',');
    if (!state.cities.length || nextIds !== curIds) applyCities(data);
  }

  function readZonesFast(root) {
    var ver = root.dataset.cacheVer || '1';
    var inline = parseInlineJson('mbZonesData');
    if (inline && inline.features && inline.features.length) {
      writeLocalJson(cacheKey('zones', ver), inline);
      return Promise.resolve(inline);
    }
    var cached = readLocalJson(cacheKey('zones', ver));
    if (cached && cached.features && cached.features.length) {
      return Promise.resolve(cached);
    }
    var url = (root.dataset.zonesUrl || '').trim();
    if (!url) return Promise.resolve(null);
    return fetch(url, { credentials: 'same-origin', cache: 'force-cache' })
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (geo) {
        if (geo && geo.features) writeLocalJson(cacheKey('zones', ver), geo);
        return geo;
      })
      .catch(function () { return null; });
  }

  function cacheEls(root) {
    els = {
      root: root,
      sheet: document.getElementById('mbSheet'),
      sheetBack: document.getElementById('mbSheetBack'),
      sheetBackLabel: document.getElementById('mbSheetBackLabel'),
      sheetHandle: document.getElementById('mbSheetHandle'),
      sheetGrabHint: document.getElementById('mbSheetGrabHint'),
      citySearch: document.getElementById('mbCitySearch'),
      destSearch: document.getElementById('mbDestSearch'),
      provinceChips: document.getElementById('mbProvinceChips'),
      cityList: document.getElementById('mbCityList'),
      destList: document.getElementById('mbDestList'),
      originPinHint: document.getElementById('mbOriginPinHint'),
      destPinHint: document.getElementById('mbDestPinHint'),
      confirmOrigin: document.getElementById('mbConfirmOrigin'),
      confirmDest: document.getElementById('mbConfirmDest'),
      confirmCar: document.getElementById('mbConfirmCar'),
      carClasses: document.getElementById('mbCarClasses'),
      dateStrip: document.getElementById('mbDateStrip'),
      timeSlots: document.getElementById('mbTimeSlots'),
      continueBtn: document.getElementById('mbContinueReserve'),
      selectedTrip: document.getElementById('mbSelectedTrip'),
      selectedTime: document.getElementById('mbSelectedTime'),
      selectedMeta: document.getElementById('mbSelectedMeta'),
      selectedPrice: document.getElementById('mbSelectedPrice'),
      originBadge: document.getElementById('mbOriginBadge'),
      destBadge: document.getElementById('mbDestBadge'),
      originText: document.getElementById('mbOriginText'),
      destText: document.getElementById('mbDestText'),
      eta: document.getElementById('mbEta'),
      centerPin: document.getElementById('mbCenterPin'),
      steps: root.querySelectorAll('.mapbook__step, .mapbook__side-step'),
      originPlaceSearch: document.getElementById('mbOriginPlaceSearch'),
      destPlaceSearch: document.getElementById('mbDestPlaceSearch'),
      originPlaceList: document.getElementById('mbOriginPlaceList'),
      destPlaceList: document.getElementById('mbDestPlaceList'),
      originVenueList: document.getElementById('mbOriginVenueList'),
      destVenueList: document.getElementById('mbDestVenueList'),
      originAddress: document.getElementById('mbOriginAddress'),
      destAddress: document.getElementById('mbDestAddress'),
      plaqueLayer: document.getElementById('mbPlaqueLayer'),
      pinZones: document.getElementById('mbPinZones'),
      zoneTraffic: document.getElementById('mbZoneTraffic'),
      zoneOddEven: document.getElementById('mbZoneOddEven'),
      routeZones: null,
      trafficToggle: document.getElementById('mbTrafficToggle'),
      fitRouteBtn: null
    };
  }

  function bindUi() {
    els.citySearch.addEventListener('input', function () {
      if (els.citySearch.value.trim()) {
        state.provinceFilter = null;
        var allChip = els.provinceChips && els.provinceChips.firstElementChild;
        if (allChip) syncChips(allChip);
      }
      renderCityList(els.cityList, els.citySearch.value, false);
    });
    els.destSearch.addEventListener('input', function () {
      renderCityList(els.destList, els.destSearch.value, true);
    });

    if (els.confirmOrigin) {
      els.confirmOrigin.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        confirmOriginPin();
      });
    }
    if (els.confirmDest) {
      els.confirmDest.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        confirmDestPin();
      });
    }
    els.confirmCar.addEventListener('click', function () {
      if (!state.carClass) return;
      goStep(4);
      buildDateStrip();
    });

    bindPlaceSearch(els.originPlaceSearch, els.originPlaceList, 'origin');
    bindPlaceSearch(els.destPlaceSearch, els.destPlaceList, 'dest');

    els.carClasses.addEventListener('click', function (e) {
      var btn = e.target.closest('.mapbook__car');
      if (!btn || btn.disabled || btn.classList.contains('mapbook__car--soon')) return;
      state.carClass = btn.dataset.class;
      els.carClasses.querySelectorAll('.mapbook__car').forEach(function (c) {
        c.setAttribute('aria-selected', c === btn ? 'true' : 'false');
      });
      els.confirmCar.disabled = false;
      updateCarPriceHint(state.carClass);
    });

    els.continueBtn.addEventListener('click', goToReservation);
    if (els.trafficToggle) {
      els.trafficToggle.addEventListener('click', toggleLiveTraffic);
    }
    if (els.sheetBack) {
      els.sheetBack.addEventListener('click', goBack);
    }
    bindSheetDrawer();
    els.root.querySelectorAll('[data-edit]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var which = btn.getAttribute('data-edit');
        if (which === 'origin') {
          reselectOrigin();
        } else {
          reselectDestination();
        }
      });
    });

    els.steps.forEach(function (btn) {
      btn.addEventListener('click', function () {
        var s = Number(btn.dataset.step);
        if (btn.disabled) return;
        if (s >= state.step) return;
        if (s === 1) reselectOrigin();
        else if (s === 2) reselectDestination();
        else goStep(s);
      });
    });
  }

  function initMap() {
    nmp = window.nmp_mapboxgl;
    var neshanKey = (els.root.dataset.neshanWebKey || '').trim();
    if (!nmp || !neshanKey) {
      console.error('Neshan Mapbox GL / WebApiKey missing');
      return;
    }

    map = new nmp.Map({
      container: 'mapBookMap',
      mapKey: neshanKey,
      mapType: nmp.Map.mapTypes.neshanVector,
      center: [53.6, 32.4],
      zoom: 5.6,
      minZoom: 5,
      maxZoom: 20,
      pitch: 0,
      trackResize: true,
      // Faster first paint — enable traffic/POI after the basemap is idle
      poi: false,
      traffic: false,
      mapTypeControllerStatus: { show: false },
      poiControllerOptions: { show: false },
      trafficControllerOptions: { show: false }
    });

    try {
      updateMapBounds();
    } catch (e) { /* older SDK */ }

    state.trafficOn = false;
    syncTrafficToggleUi();

    map.on('movestart', onMapInteractStart);
    map.on('dragstart', onMapInteractStart);
    map.on('zoomstart', onMapInteractStart);
    map.on('moveend', function () {
      onMapInteractEnd();
      if (state.routeCoords && state.routeCoords.length >= 2 && !state.picking) {
        scheduleRouteRedraw();
      }
      scheduleBuildingPlaqueFetch();
      scheduleVenueDetect();
    });
    map.on('dragend', function () {
      if (!state.picking) return;
      scheduleConfirmPeek();
      // User finished dragging — always re-snap (moveend alone can miss after programmatic fly)
      scheduleSnapPickToRoute();
      scheduleVenueDetect();
    });
    map.on('click', function () {
      if (!state.picking) return;
      clearTimeout(scheduleConfirmPeek._t);
      setConfirmPinBusy(true);
      setSheetPinMode('map');
      scheduleConfirmPeek();
    });
    map.on('move', function () {
      scheduleOverlaySync(false);
      if (state.routeCoords && state.routeCoords.length >= 2 && !state.picking) {
        scheduleRouteRedraw();
      }
    });
    map.on('rotate', function () {
      if (state.routeCoords && state.routeCoords.length >= 2 && !state.picking) {
        scheduleRouteRedraw();
      }
    });
    map.on('zoom', function () {
      scheduleOverlaySync(false);
      syncBuildingPlaqueVisibility();
      syncSnappBuildingLayers();
    });
    map.on('zoomend', function () {
      if (state.picking) scheduleSnapPickToRoute();
      scheduleBuildingPlaqueFetch();
      scheduleVenueDetect();
      syncSnappBuildingLayers();
    });
    map.on('resize', function () {
      if (routeCanvas) routeCanvas.resize();
    });

    map.on('load', function () {
      mapReady = true;
      routeCanvas = createRouteGlowOverlay(document.getElementById('mapBookMap'));
      restyleRoadLayers();
      loadRestrictionZones();
      pendingAfterMapLoad.forEach(function (fn) { try { fn(); } catch (err) { /* ignore */ } });
      pendingAfterMapLoad = [];
      enableLiveTrafficWhenIdle();
      ensurePlaqueLayerDom();
      syncSnappBuildingLayers();
    });

    // Style can reload when map type / traffic sources settle
    map.on('styledata', function () {
      if (!mapReady) return;
      _roadLayersRestyled = false;
      _snappLayerBucket = -1;
      clearTimeout(restyleRoadLayers._t);
      restyleRoadLayers._t = setTimeout(function () {
        restyleRoadLayers();
        syncSnappBuildingLayers();
        if (state.trafficOn) scheduleTrafficStylePolish();
      }, 120);
    });

    window.addEventListener('resize', function () {
      clearTimeout(window.__mbArrangeResizeT);
      window.__mbArrangeResizeT = setTimeout(function () {
        if (state.routeCoords && state.routeCoords.length >= 2) arrangeMapToRoute(false);
      }, 180);
    });
  }

  function restyleRoadLayers() {
    if (!map || typeof map.getStyle !== 'function') return;
    if (_roadLayersRestyled) return;
    var style;
    try { style = map.getStyle(); } catch (e) { return; }
    if (!style || !style.layers) return;

    style.layers.forEach(function (layer) {
      if (!layer || layer.type !== 'line' || !layer.id) return;
      var id = String(layer.id).toLowerCase();
      // Skip our own overlays / traffic congestion / labels
      if (id.indexOf('mb-') === 0) return;
      if (id.indexOf('traffic') >= 0 && id.indexOf('road') < 0) return;
      if (id.indexOf('transit') >= 0 || id.indexOf('rail') >= 0) return;
      if (id.indexOf('boundary') >= 0 || id.indexOf('admin') >= 0) return;
      if (id.indexOf('water') >= 0 || id.indexOf('building') >= 0) return;

      var isMotorway = /motorway|trunk|freeway| بزرگراه|آزادراه|highway/.test(id)
        || /motorway|trunk/.test(String(layer['source-layer'] || '').toLowerCase());
      var isPrimary = /primary|major|شریانی|اصلی/.test(id)
        || /primary/.test(String(layer['source-layer'] || '').toLowerCase());
      var isRoad = isMotorway || isPrimary
        || /road|street|secondary|tertiary|residential|path|bridge|tunnel|خیابان|جاده/.test(id)
        || /road|street|transportation/.test(String(layer['source-layer'] || '').toLowerCase());

      if (!isRoad) return;

      var color = isMotorway ? HWY_MOTORWAY : isPrimary ? HWY_PRIMARY : HWY_SECONDARY;
      try {
        map.setPaintProperty(layer.id, 'line-color', color);
      } catch (e) { /* layer may not support line-color */ }
    });
    _roadLayersRestyled = true;
  }

  function enableLiveTrafficWhenIdle() {
    if (!map) return;
    var bootstrapped = false;
    function turnOn() {
      if (bootstrapped || !map) return;
      bootstrapped = true;
      if (state.trafficUserTouched) return;
      try {
        if (typeof map.togglePoiLayer === 'function') map.togglePoiLayer(true);
      } catch (e) { /* ignore */ }
      try {
        if (typeof map.toggleTrafficLayer === 'function') {
          map.toggleTrafficLayer(true);
          state.trafficOn = typeof map.trafficLayer === 'boolean' ? !!map.trafficLayer : true;
        } else {
          state.trafficOn = true;
        }
      } catch (e) {
        state.trafficOn = true;
      }
      syncTrafficToggleUi();
      scheduleTrafficStylePolish();
    }

    var idleHandler = function () {
      map.off('idle', idleHandler);
      requestAnimationFrame(function () {
        setTimeout(turnOn, 60);
      });
    };
    map.on('idle', idleHandler);
    setTimeout(turnOn, 2500);
  }

  /* Building plaque labels (پلاک) — real Neshan Geocoding Plus, HTML overlay for RTL */
  var buildingPlaqueTimer = null;
  var buildingPlaqueSeq = 0;
  var buildingPlaqueLastKey = '';
  var buildingPlaqueStreetCache = {};
  var buildingPlaqueActiveStreet = '';
  var BUILDING_PLAQUE_CACHE_MS = 24 * 60 * 60 * 1000;
  var _snapIdleWait = false;
  var _nativeBuildingLayersFound = false;
  var _snappLayerBucket = -1;
  var _snappLayerPicking = null;
  var _roadLayersRestyled = false;

  function loadPlaqueCacheFromSession() {
    try {
      var raw = sessionStorage.getItem('mbPlaqueStreets');
      if (!raw) return;
      var parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object') buildingPlaqueStreetCache = parsed;
    } catch (e) { /* ignore */ }
  }

  function persistPlaqueCache() {
    try {
      sessionStorage.setItem('mbPlaqueStreets', JSON.stringify(buildingPlaqueStreetCache));
    } catch (e) { /* quota */ }
  }

  loadPlaqueCacheFromSession();

  function buildingPlaqueStreetKey(city, street) {
    return String(city || '').trim().toLowerCase() + '|' + String(street || '').trim().toLowerCase();
  }

  function filterPlaquesInView(plaques, view, maxCount) {
    var pad = 0.006;
    var out = [];
    (plaques || []).forEach(function (p) {
      if (out.length >= maxCount) return;
      if (p.lat >= view.minLat - pad && p.lat <= view.maxLat + pad &&
          p.lng >= view.minLng - pad && p.lng <= view.maxLng + pad) {
        out.push(p);
      }
    });
    return out;
  }

  function mergePlaqueCache(existing, incoming) {
    var byNum = {};
    (existing || []).forEach(function (p) { byNum[p.number] = p; });
    (incoming || []).forEach(function (p) { byNum[p.number] = p; });
    return Object.keys(byNum).sort(function (a, b) { return Number(a) - Number(b); })
      .map(function (k) { return byNum[k]; });
  }

  function applyPlaquesFromCache(plaques, viewKey) {
    if (!plaques.length) {
      clearBuildingPlaques(false);
      return false;
    }
    buildingPlaqueLastKey = viewKey;
    state.buildingPlaqueItems = plaques;
    renderBuildingPlaqueDom(plaques);
    syncBuildingPlaqueVisibility();
    return true;
  }

  function tryPlaquesFromClientCache(view, maxPlaques) {
    var sk = buildingPlaqueActiveStreet;
    if (!sk) return false;
    var entry = buildingPlaqueStreetCache[sk];
    if (!entry || !entry.plaques || !entry.plaques.length) return false;
    if (Date.now() - entry.at > BUILDING_PLAQUE_CACHE_MS) return false;
    var filtered = filterPlaquesInView(entry.plaques, view, maxPlaques);
    if (!filtered.length) return false;
    var viewKey = [sk, view.minLat.toFixed(4), view.minLng.toFixed(4),
      view.maxLat.toFixed(4), view.maxLng.toFixed(4), maxPlaques].join('|');
    return applyPlaquesFromCache(filtered, viewKey);
  }

  function ensurePlaqueLayerDom() {
    if (!els.plaqueLayer) {
      els.plaqueLayer = document.getElementById('mbPlaqueLayer');
    }
    if (!els.plaqueLayer && els.root) {
      var layer = document.createElement('div');
      layer.id = 'mbPlaqueLayer';
      layer.className = 'mapbook__plaque-layer';
      layer.hidden = true;
      layer.setAttribute('aria-hidden', 'true');
      els.root.appendChild(layer);
      els.plaqueLayer = layer;
    }
  }

  function syncBuildingAddressLabelLayers() {
    syncSnappBuildingLayers();
  }

  /** Snapp-style: building footprints + house numbers from Neshan vector tiles */
  function syncSnappBuildingLayers() {
    if (!map || typeof map.getStyle !== 'function') return;
    var zoom = map.getZoom();
    var picking = !!state.picking;
    var bucket = Math.floor(zoom * 2) / 2;
    if (bucket === _snappLayerBucket && picking === _snappLayerPicking && _snappLayerBucket >= 0) return;
    _snappLayerBucket = bucket;
    _snappLayerPicking = picking;

    var style;
    try { style = map.getStyle(); } catch (e) { return; }
    if (!style || !style.layers) return;

    var showBuildings = picking && zoom >= 15.5;
    var showNumbers = showBuildings && zoom >= 16;
    var buildingRe = /building|footprint|structure|parcel|landuse/i;
    var labelRe = /plaque|plate|pelak|پلاک|housenumber|house.?num|address.?num|building.?num|building.?label|num.?label|addr|number/i;
    var nativeFound = false;

    style.layers.forEach(function (layer) {
      if (!layer || !layer.id) return;
      var id = String(layer.id);
      if (id.indexOf('mb-') === 0) return;
      var sl = String(layer['source-layer'] || '');
      var idL = id.toLowerCase();
      var slL = sl.toLowerCase();

      if (layer.type === 'fill' && (buildingRe.test(idL) || buildingRe.test(slL))) {
        nativeFound = true;
        try {
          map.setLayoutProperty(id, 'visibility', showBuildings ? 'visible' : 'none');
          if (showBuildings) {
            map.setPaintProperty(id, 'fill-color', '#e8e8e8');
            map.setPaintProperty(id, 'fill-opacity', 0.92);
            map.setPaintProperty(id, 'fill-outline-color', '#bdbdbd');
          }
        } catch (e) { /* ignore */ }
      }

      if (layer.type === 'line' && (buildingRe.test(idL) || buildingRe.test(slL))) {
        nativeFound = true;
        try {
          map.setLayoutProperty(id, 'visibility', showBuildings ? 'visible' : 'none');
          if (showBuildings) {
            map.setPaintProperty(id, 'line-color', '#c8c8c8');
            map.setPaintProperty(id, 'line-width', 0.75);
          }
        } catch (e) { /* ignore */ }
      }

      if (layer.type === 'symbol') {
        var layout = layer.layout || {};
        if (layout['text-field'] == null) return;
        if (!labelRe.test(idL) && !labelRe.test(slL)) return;
        nativeFound = true;
        try {
          map.setLayoutProperty(id, 'visibility', showNumbers ? 'visible' : 'none');
          if (showNumbers) {
            map.setLayoutProperty(id, 'text-size', 11);
            map.setPaintProperty(id, 'text-color', '#2d2d2d');
            map.setPaintProperty(id, 'text-halo-color', '#ffffff');
            map.setPaintProperty(id, 'text-halo-width', 1.5);
          }
        } catch (e) { /* ignore */ }
      }
    });

    _nativeBuildingLayersFound = nativeFound;

    if (showNumbers && els.plaqueLayer) {
      syncBuildingPlaqueVisibility();
    }

    if (showBuildings) {
      try {
        if (typeof map.togglePoiLayer === 'function') map.togglePoiLayer(true);
      } catch (e) { /* ignore */ }
    }
  }

  function syncBuildingPlaqueVisibility() {
    ensurePlaqueLayerDom();
    if (!els.plaqueLayer) return;
    var zoom = map ? map.getZoom() : 0;
    var useNative = _nativeBuildingLayersFound && zoom >= 16.5;
    var show = !!state.picking && zoom >= 15.5 && !useNative;
    els.plaqueLayer.hidden = !show;
    if (!show) {
      if (els.plaqueLayer && useNative) {
        els.plaqueLayer.innerHTML = '';
      } else if (!state.picking || zoom < 15.5) {
        els.plaqueLayer.innerHTML = '';
        state.buildingPlaqueItems = [];
      }
    } else {
      syncBuildingPlaqueDomPositions();
    }
  }

  function renderBuildingPlaqueDom(items) {
    ensurePlaqueLayerDom();
    if (!els.plaqueLayer) return;
    els.plaqueLayer.hidden = false;
    els.plaqueLayer.innerHTML = '';
    (items || []).forEach(function (p) {
      var el = document.createElement('span');
      el.className = 'mapbook__plaque-marker';
      el.textContent = p.label || ('پلاک ' + toFaDigits(p.number));
      el.dataset.lat = String(p.lat);
      el.dataset.lng = String(p.lng);
      els.plaqueLayer.appendChild(el);
    });
    syncBuildingPlaqueDomPositions();
  }

  function syncBuildingPlaqueDomPositions() {
    if (!map || !els.plaqueLayer || els.plaqueLayer.hidden) return;
    var mapEl = document.getElementById('mapBookMap');
    if (!mapEl) return;
    var mapRect = mapEl.getBoundingClientRect();
    var layerRect = els.plaqueLayer.getBoundingClientRect();
    var ox = mapRect.left - layerRect.left;
    var oy = mapRect.top - layerRect.top;
    els.plaqueLayer.querySelectorAll('.mapbook__plaque-marker').forEach(function (el) {
      var lat = Number(el.dataset.lat);
      var lng = Number(el.dataset.lng);
      if (!isFinite(lat) || !isFinite(lng)) return;
      var pt = map.project([lng, lat]);
      el.style.left = (pt.x + ox) + 'px';
      el.style.top = (pt.y + oy) + 'px';
    });
  }

  function getMapViewBounds() {
    if (!map) return null;
    var b = map.getBounds();
    var c = map.getCenter();
    return {
      minLat: b.getSouth(),
      minLng: b.getWest(),
      maxLat: b.getNorth(),
      maxLng: b.getEast(),
      centerLat: c.lat,
      centerLng: c.lng,
      zoom: map.getZoom()
    };
  }

  function clearBuildingPlaques(resetKey) {
    if (resetKey) {
      buildingPlaqueLastKey = '';
      buildingPlaqueActiveStreet = '';
    }
    state.buildingPlaqueItems = [];
    ensurePlaqueLayerDom();
    if (els.plaqueLayer) els.plaqueLayer.innerHTML = '';
  }

  function scheduleBuildingPlaqueFetch() {
    if (!state.picking || !map || !mapReady) return;
    if (map.getZoom() < 15.5) {
      syncBuildingPlaqueVisibility();
      return;
    }
    clearTimeout(buildingPlaqueTimer);
    buildingPlaqueTimer = setTimeout(fetchBuildingPlaques, 900);
  }

  async function fetchBuildingPlaques() {
    if (!state.picking || !map || map.getZoom() < 15.5) return;
    var view = getMapViewBounds();
    if (!view) return;

    var maxPlaques = view.zoom >= 18 ? 20 : view.zoom >= 17 ? 16 : 12;
    var key = [
      buildingPlaqueActiveStreet || '',
      view.minLat.toFixed(3),
      view.minLng.toFixed(3),
      view.maxLat.toFixed(3),
      view.maxLng.toFixed(3),
      maxPlaques
    ].join('|');
    if (key === buildingPlaqueLastKey) return;

    // Prefer client street cache — instant, no Neshan
    if (tryPlaquesFromClientCache(view, maxPlaques)) return;

    var seq = ++buildingPlaqueSeq;
    var url = '/Reserve/BuildingPlaques'
      + '?minLat=' + encodeURIComponent(view.minLat)
      + '&minLng=' + encodeURIComponent(view.minLng)
      + '&maxLat=' + encodeURIComponent(view.maxLat)
      + '&maxLng=' + encodeURIComponent(view.maxLng)
      + '&centerLat=' + encodeURIComponent(view.centerLat)
      + '&centerLng=' + encodeURIComponent(view.centerLng)
      + '&max=' + encodeURIComponent(maxPlaques);

    try {
      var res = await fetch(url, { credentials: 'same-origin' });
      if (!res.ok || !state.picking) return;
      var data = await res.json();
      if (!state.picking) return;

      var plaques = (data && data.plaques) || [];
      if (data && data.city && data.street) {
        var sk = buildingPlaqueStreetKey(data.city, data.street);
        buildingPlaqueActiveStreet = sk;
        var prev = buildingPlaqueStreetCache[sk];
        buildingPlaqueStreetCache[sk] = {
          at: Date.now(),
          city: data.city,
          street: data.street,
          plaques: mergePlaqueCache(prev && prev.plaques, plaques)
        };
        persistPlaqueCache();
      }

      // Stale response (user panned while waiting): still keep cache, re-filter to CURRENT view
      var currentView = getMapViewBounds() || view;
      if (buildingPlaqueActiveStreet && buildingPlaqueStreetCache[buildingPlaqueActiveStreet]) {
        plaques = filterPlaquesInView(
          buildingPlaqueStreetCache[buildingPlaqueActiveStreet].plaques,
          currentView,
          maxPlaques
        );
      }

      // Only skip rendering if a newer fetch already finished after us
      if (seq !== buildingPlaqueSeq && state.buildingPlaqueItems && state.buildingPlaqueItems.length) {
        return;
      }

      if (!plaques.length) {
        // Don't wipe existing markers on empty — street may still be loading
        if (!state.buildingPlaqueItems || !state.buildingPlaqueItems.length) {
          clearBuildingPlaques(false);
        }
        return;
      }

      applyPlaquesFromCache(plaques, key);
    } catch (e) {
      console.warn('fetchBuildingPlaques', e);
    }
  }

  /* Public venues — airports, hospitals, malls (Snapp-style blue border + entrances) */
  var venueDetectTimer = null;

  function scheduleVenueDetect() {
    if (!state.picking || !map) return;
    clearTimeout(venueDetectTimer);
    venueDetectTimer = setTimeout(detectPublicVenue, 280);
  }

  function detectPublicVenue() {
    if (!state.picking || !map) return;
    var ll = getPickLatLng();
    if (!ll) return;
    var venue = findPublicVenueAt(ll.lat, ll.lng);
    if (venue) setActiveVenue(venue);
    else clearActiveVenue();
  }

  function setActiveVenue(venue) {
    if (!venue) return;
    var changed = !state.activeVenue || state.activeVenue.id !== venue.id;
    state.activeVenue = venue;
    clearBoundVisual();
    drawVenueBorder(venue);
    if (changed) fitVenueInView(venue);
    if (changed && venue.entrances && venue.entrances.length) {
      state.selectedVenueEntranceId = venue.entrances[0].id;
      selectVenueEntrance(venue.entrances[0], false);
    } else {
      renderVenueEntrances(venue);
    }
  }

  function clearActiveVenue() {
    var hadVenue = !!state.activeVenue;
    state.activeVenue = null;
    state.selectedVenueEntranceId = null;
    clearVenueBorder();
    hideVenueEntrances();
    if (hadVenue && state.picking) {
      updateBoundVisual();
      reverseCurrentPin();
      scheduleSnapPickToRoute();
    }
  }

  function drawVenueBorder(venue) {
    if (!map || !mapReady || !venue || !venue.polygon) return;
    var geo = {
      type: 'Feature',
      properties: { id: venue.id },
      geometry: { type: 'Polygon', coordinates: [venue.polygon] }
    };
    if (map.getSource('mb-venue-bound')) {
      map.getSource('mb-venue-bound').setData(geo);
    } else {
      map.addSource('mb-venue-bound', { type: 'geojson', data: geo });
      map.addLayer({
        id: 'mb-venue-bound-fill',
        type: 'fill',
        source: 'mb-venue-bound',
        paint: { 'fill-color': '#6289E5', 'fill-opacity': 0.14 }
      });
      map.addLayer({
        id: 'mb-venue-bound-line',
        type: 'line',
        source: 'mb-venue-bound',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: {
          'line-color': '#6289E5',
          'line-width': 3,
          'line-opacity': 0.95
        }
      });
    }
  }

  function clearVenueBorder() {
    if (!map || !mapReady) return;
    try {
      if (map.getLayer('mb-venue-bound-fill')) map.removeLayer('mb-venue-bound-fill');
      if (map.getLayer('mb-venue-bound-line')) map.removeLayer('mb-venue-bound-line');
      if (map.getSource('mb-venue-bound')) map.removeSource('mb-venue-bound');
    } catch (e) { /* ignore */ }
  }

  function hideVenueEntrances() {
    [els.originVenueList, els.destVenueList].forEach(function (listEl) {
      if (!listEl) return;
      listEl.hidden = true;
      listEl.innerHTML = '';
    });
  }

  function renderVenueEntrances(venue) {
    var listEl = state.picking === 'origin' ? els.originVenueList : els.destVenueList;
    var otherEl = state.picking === 'origin' ? els.destVenueList : els.originVenueList;
    if (otherEl) {
      otherEl.hidden = true;
      otherEl.innerHTML = '';
    }
    if (!listEl || !venue || !venue.entrances || !venue.entrances.length) {
      hideVenueEntrances();
      return;
    }
    listEl.hidden = false;
    listEl.innerHTML = '';
    var header = document.createElement('li');
    header.className = 'mapbook__venue-list-header';
    header.textContent = 'داخل محدوده';
    listEl.appendChild(header);
    venue.entrances.forEach(function (ent) {
      var li = document.createElement('li');
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'mapbook__venue-entrance' +
        (ent.id === state.selectedVenueEntranceId ? ' is-selected' : '');
      btn.innerHTML = '<span class="mapbook__venue-check" aria-hidden="true"></span>' +
        '<span>' + escapeHtml(ent.label) + '</span>';
      btn.addEventListener('click', function () {
        selectVenueEntrance(ent, true);
      });
      li.appendChild(btn);
      listEl.appendChild(li);
    });
  }

  function selectVenueEntrance(ent, animate) {
    if (!ent || !map) return;
    state.selectedVenueEntranceId = ent.id;
    if (state.activeVenue) renderVenueEntrances(state.activeVenue);
    flyPickToLatLng(ent.lat, ent.lng, Math.max(map.getZoom(), 16), animate !== false && !REDUCE_MOTION);
    var input = state.picking === 'origin' ? els.originPlaceSearch : els.destPlaceSearch;
    var addrEl = state.picking === 'origin' ? els.originAddress : els.destAddress;
    var title = state.activeVenue
      ? (state.activeVenue.shortName + '، ' + ent.label)
      : ent.label;
    if (input) input.value = title;
    setPinAddress(addrEl, title, state.activeVenue ? state.activeVenue.city : '');
    scheduleSnapPickToRoute();
  }

  /* Traffic: solid congestion colors + true traveling wave (line-gradient crests) */
  var TRAFFIC_COLORS = {
    free: '#2dbe60',
    low: '#2dbe60',
    moderate: '#f5c400',
    heavy: '#ff7a00',
    severe: '#e10600'
  };
  var trafficWave = {
    raf: null,
    t0: null,
    bases: [],
    propKey: null,
    nativePaint: Object.create(null),
    geomTimer: 0,
    phase: 0
  };
  var TRAFFIC_WAVE_SRC = 'mb-traffic-wave-src';
  var TRAFFIC_WAVE_LAYER = 'mb-traffic-wave-flow';
  var TRAFFIC_WAVE_GLOW = 'mb-traffic-wave-glow';
  var TRAFFIC_WAVE_SPEED = 0.00022; // phase units per ms (soft continuous wave)
  var TRAFFIC_WAVE_CRESTS = 3;

  function scheduleTrafficStylePolish() {
    clearTimeout(scheduleTrafficStylePolish._t);
    scheduleTrafficStylePolish._t = setTimeout(restyleTrafficLayers, 220);
    setTimeout(restyleTrafficLayers, 1100);
    setTimeout(restyleTrafficLayers, 2200);
  }

  function collectTrafficLayers() {
    if (!map || typeof map.getStyle !== 'function') return [];
    var style;
    try { style = map.getStyle(); } catch (e) { return []; }
    if (!style || !style.layers) return [];
    var out = [];
    style.layers.forEach(function (layer) {
      if (!layer || layer.type !== 'line' || !layer.id) return;
      var id = String(layer.id);
      var idL = id.toLowerCase();
      if (idL.indexOf('mb-traffic-') === 0) return;
      var src = String(layer.source || '');
      var srcL = src.toLowerCase();
      if (src === 'neshanTrafficSource' || srcL.indexOf('traffic') >= 0 || idL.indexOf('traffic') >= 0) {
        out.push(id);
      }
    });
    return out;
  }

  function discoverTrafficPropKey(layerIds) {
    if (trafficWave.propKey) return trafficWave.propKey;
    var candidates = [
      'congestion', 'Congestion', 'traffic', 'Traffic', 'level', 'Level',
      'traffic_level', 'trafficLevel', 'status', 'Status', 'speed', 'Speed',
      'class', 'Class', 'category', 'Category', 'jam', 'Jam', 'color', 'Color'
    ];
    try {
      var feats = map.queryRenderedFeatures({ layers: layerIds });
      if (!feats || !feats.length) {
        feats = [];
        layerIds.forEach(function (id) {
          try {
            var lyr = map.getLayer(id);
            if (!lyr || !lyr.source) return;
            var sl = lyr['source-layer'];
            var opts = sl ? { sourceLayer: sl } : {};
            var more = map.querySourceFeatures(lyr.source, opts) || [];
            feats = feats.concat(more);
          } catch (e) { /* ignore */ }
        });
      }
      for (var i = 0; i < feats.length; i++) {
        var props = (feats[i] && feats[i].properties) || {};
        for (var c = 0; c < candidates.length; c++) {
          if (Object.prototype.hasOwnProperty.call(props, candidates[c]) && props[candidates[c]] != null && props[candidates[c]] !== '') {
            trafficWave.propKey = candidates[c];
            return trafficWave.propKey;
          }
        }
        var keys = Object.keys(props);
        for (var k = 0; k < keys.length; k++) {
          var v = props[keys[k]];
          if (typeof v === 'number' && v >= 0 && v <= 4) {
            trafficWave.propKey = keys[k];
            return trafficWave.propKey;
          }
        }
      }
    } catch (e) { /* ignore */ }
    return null;
  }

  function trafficColorExprForKey(key) {
    return [
      'case',
      ['==', ['to-string', ['get', key]], '0'], TRAFFIC_COLORS.free,
      ['==', ['to-string', ['get', key]], 'free'], TRAFFIC_COLORS.free,
      ['==', ['to-string', ['get', key]], 'low'], TRAFFIC_COLORS.free,
      ['==', ['to-string', ['get', key]], 'green'], TRAFFIC_COLORS.free,
      ['==', ['to-string', ['get', key]], '1'], TRAFFIC_COLORS.moderate,
      ['==', ['to-string', ['get', key]], 'moderate'], TRAFFIC_COLORS.moderate,
      ['==', ['to-string', ['get', key]], 'medium'], TRAFFIC_COLORS.moderate,
      ['==', ['to-string', ['get', key]], 'yellow'], TRAFFIC_COLORS.moderate,
      ['==', ['to-string', ['get', key]], '2'], TRAFFIC_COLORS.heavy,
      ['==', ['to-string', ['get', key]], 'heavy'], TRAFFIC_COLORS.heavy,
      ['==', ['to-string', ['get', key]], 'orange'], TRAFFIC_COLORS.heavy,
      ['==', ['to-string', ['get', key]], '3'], TRAFFIC_COLORS.severe,
      ['==', ['to-string', ['get', key]], '4'], TRAFFIC_COLORS.severe,
      ['==', ['to-string', ['get', key]], 'severe'], TRAFFIC_COLORS.severe,
      ['==', ['to-string', ['get', key]], 'red'], TRAFFIC_COLORS.severe,
      ['==', ['get', key], 0], TRAFFIC_COLORS.free,
      ['==', ['get', key], 1], TRAFFIC_COLORS.moderate,
      ['==', ['get', key], 2], TRAFFIC_COLORS.heavy,
      ['==', ['get', key], 3], TRAFFIC_COLORS.severe,
      ['==', ['get', key], 4], TRAFFIC_COLORS.severe,
      TRAFFIC_COLORS.moderate
    ];
  }

  function trafficWidthExpr(scale) {
    scale = scale || 1;
    return [
      'interpolate', ['linear'], ['zoom'],
      8, 2.4 * scale,
      12, 4.6 * scale,
      15, 7.0 * scale,
      17, 9.0 * scale
    ];
  }

  function removeLegacyTrafficWaveLayers() {
    if (!map) return;
    var style;
    try { style = map.getStyle(); } catch (e) { return; }
    (style.layers || []).slice().forEach(function (l) {
      if (!l || !l.id) return;
      var id = String(l.id);
      if (id.indexOf('mb-traffic-under-') === 0 || id.indexOf('mb-traffic-wave-') === 0) {
        if (id === TRAFFIC_WAVE_LAYER || id === TRAFFIC_WAVE_GLOW) return;
        try { map.removeLayer(id); } catch (e2) { /* ignore */ }
      }
    });
  }

  /** Soft traveling crests along line-progress (0–1) — real wave, not dashes */
  function buildWaveGradient(phase, soft) {
    var peaks = [];
    var i;
    for (i = 0; i < TRAFFIC_WAVE_CRESTS; i++) {
      peaks.push((phase + i / TRAFFIC_WAVE_CRESTS) % 1);
    }
    peaks.sort(function (a, b) { return a - b; });

    var half = soft ? 0.09 : 0.055;
    var peakA = soft ? 0.35 : 0.82;
    var stops = [{ t: 0, a: 0 }, { t: 1, a: 0 }];

    peaks.forEach(function (p) {
      var left = p - half;
      var right = p + half;
      if (left > 0) stops.push({ t: left, a: 0 });
      else stops.push({ t: 0, a: peakA * (1 - (0 - left) / half) });
      stops.push({ t: Math.max(0, Math.min(1, p)), a: peakA });
      if (right < 1) stops.push({ t: right, a: 0 });
      else stops.push({ t: 1, a: peakA * (1 - (right - 1) / half) });
    });

    stops.sort(function (a, b) { return a.t - b.t || a.a - b.a; });
    var expr = ['interpolate', ['linear'], ['line-progress']];
    var lastT = -1;
    for (i = 0; i < stops.length; i++) {
      var s = stops[i];
      var t = Math.round(s.t * 1000) / 1000;
      if (t === lastT) continue;
      lastT = t;
      expr.push(t);
      expr.push('rgba(255,255,255,' + (Math.round(s.a * 100) / 100) + ')');
    }
    return expr;
  }

  function ensureTrafficWaveSourceAndLayers() {
    if (!map) return false;
    if (!map.getSource(TRAFFIC_WAVE_SRC)) {
      try {
        map.addSource(TRAFFIC_WAVE_SRC, {
          type: 'geojson',
          data: { type: 'FeatureCollection', features: [] },
          lineMetrics: true
        });
      } catch (e) {
        return false;
      }
    }

    if (!map.getLayer(TRAFFIC_WAVE_GLOW)) {
      try {
        map.addLayer({
          id: TRAFFIC_WAVE_GLOW,
          type: 'line',
          source: TRAFFIC_WAVE_SRC,
          layout: {
            'line-cap': 'round',
            'line-join': 'round',
            visibility: state.trafficOn ? 'visible' : 'none'
          },
          paint: {
            'line-width': trafficWidthExpr(1.35),
            'line-blur': 1.1,
            'line-opacity': 1,
            'line-gradient': buildWaveGradient(0, true)
          }
        });
      } catch (eGlow) { /* gradient may fail without metrics */ }
    }

    if (!map.getLayer(TRAFFIC_WAVE_LAYER)) {
      try {
        map.addLayer({
          id: TRAFFIC_WAVE_LAYER,
          type: 'line',
          source: TRAFFIC_WAVE_SRC,
          layout: {
            'line-cap': 'round',
            'line-join': 'round',
            visibility: state.trafficOn ? 'visible' : 'none'
          },
          paint: {
            'line-width': trafficWidthExpr(0.78),
            'line-blur': 0.15,
            'line-opacity': 1,
            'line-gradient': buildWaveGradient(0, false)
          }
        });
      } catch (eWave) {
        return false;
      }
    }
    return !!(map.getLayer(TRAFFIC_WAVE_LAYER) || map.getLayer(TRAFFIC_WAVE_GLOW));
  }

  function geometryKey(coords) {
    if (!coords || !coords.length) return '';
    var a = coords[0];
    var b = coords[coords.length - 1];
    if (typeof a[0] === 'number') {
      return a[0].toFixed(4) + ',' + a[1].toFixed(4) + '>' + b[0].toFixed(4) + ',' + b[1].toFixed(4) + '#' + coords.length;
    }
    return String(coords.length);
  }

  function rebuildTrafficWaveGeometry() {
    if (!map || !state.trafficOn) return;
    if (!ensureTrafficWaveSourceAndLayers()) return;
    var layers = trafficWave.bases.length ? trafficWave.bases : collectTrafficLayers();
    if (!layers.length) return;

    var feats;
    try {
      feats = map.queryRenderedFeatures({ layers: layers }) || [];
    } catch (e) {
      feats = [];
    }
    if (!feats.length) return;

    var seen = Object.create(null);
    var out = [];
    for (var i = 0; i < feats.length; i++) {
      var f = feats[i];
      if (!f || !f.geometry) continue;
      var g = f.geometry;
      if (g.type === 'LineString') {
        var k = geometryKey(g.coordinates);
        if (seen[k]) continue;
        seen[k] = 1;
        out.push({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: g.coordinates } });
      } else if (g.type === 'MultiLineString') {
        for (var m = 0; m < g.coordinates.length; m++) {
          var line = g.coordinates[m];
          var km = geometryKey(line);
          if (seen[km]) continue;
          seen[km] = 1;
          out.push({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: line } });
        }
      }
      if (out.length > 900) break;
    }

    try {
      map.getSource(TRAFFIC_WAVE_SRC).setData({ type: 'FeatureCollection', features: out });
    } catch (eSet) { /* ignore */ }
  }

  function scheduleTrafficWaveGeom() {
    clearTimeout(trafficWave.geomTimer);
    trafficWave.geomTimer = setTimeout(rebuildTrafficWaveGeometry, 160);
  }

  function applyWaveGradientPaint(phase) {
    if (!map) return;
    try {
      if (map.getLayer(TRAFFIC_WAVE_LAYER)) {
        map.setPaintProperty(TRAFFIC_WAVE_LAYER, 'line-gradient', buildWaveGradient(phase, false));
      }
      if (map.getLayer(TRAFFIC_WAVE_GLOW)) {
        map.setPaintProperty(TRAFFIC_WAVE_GLOW, 'line-gradient', buildWaveGradient((phase + 0.5 / TRAFFIC_WAVE_CRESTS) % 1, true));
      }
    } catch (e) { /* ignore */ }
  }

  function restyleTrafficLayers() {
    if (!map) return;
    removeLegacyTrafficWaveLayers();
    var layers = collectTrafficLayers();
    trafficWave.bases = layers;
    if (!layers.length) return;

    var propKey = discoverTrafficPropKey(layers);

    layers.forEach(function (id) {
      if (!trafficWave.nativePaint[id]) {
        try { trafficWave.nativePaint[id] = map.getPaintProperty(id, 'line-color'); } catch (e) { /* ignore */ }
      }
      if (propKey) {
        try { map.setPaintProperty(id, 'line-color', trafficColorExprForKey(propKey)); } catch (e1) { /* ignore */ }
      } else if (trafficWave.nativePaint[id] != null) {
        try { map.setPaintProperty(id, 'line-color', trafficWave.nativePaint[id]); } catch (e2) { /* ignore */ }
      }
      try { map.setPaintProperty(id, 'line-opacity', 0.96); } catch (e3) { /* ignore */ }
      try { map.setPaintProperty(id, 'line-width', trafficWidthExpr(1)); } catch (e4) { /* ignore */ }
      try {
        map.setLayoutProperty(id, 'line-cap', 'round');
        map.setLayoutProperty(id, 'line-join', 'round');
      } catch (e5) { /* ignore */ }
      try { map.setPaintProperty(id, 'line-dasharray', [1, 0]); } catch (e7) { /* ignore */ }
    });

    stopTrafficWave(true);
  }

  function startTrafficWave() {
    /* Wave animation removed — static congestion colors only */
  }

  function stopTrafficWave(hideOverlays) {
    if (trafficWave.raf) cancelAnimationFrame(trafficWave.raf);
    trafficWave.raf = null;
    clearTimeout(trafficWave.geomTimer);
    if (!map || hideOverlays === false) return;
    try {
      if (map.getLayer(TRAFFIC_WAVE_LAYER)) map.setLayoutProperty(TRAFFIC_WAVE_LAYER, 'visibility', 'none');
      if (map.getLayer(TRAFFIC_WAVE_GLOW)) map.setLayoutProperty(TRAFFIC_WAVE_GLOW, 'visibility', 'none');
    } catch (e) { /* ignore */ }
  }

  function whenMapReady(fn) {
    if (mapReady) fn();
    else pendingAfterMapLoad.push(fn);
  }

  /* ---------- Tehran restriction zone borders (display-only) ---------- */

  async function loadRestrictionZones() {
    if (!map || !els.root) return;
    try {
      var geo = await readZonesFast(els.root);
      if (!geo || !geo.features) return;

      var trafficFeat = {
        type: 'FeatureCollection',
        features: geo.features.filter(function (f) {
          return f.properties && f.properties.zone === 'traffic';
        })
      };
      var oddFeat = {
        type: 'FeatureCollection',
        features: geo.features.filter(function (f) {
          return f.properties && f.properties.zone === 'oddeven';
        })
      };

      if (!map.getSource('mb-zone-oddeven')) {
        map.addSource('mb-zone-oddeven', { type: 'geojson', data: oddFeat });
        map.addLayer({
          id: 'mb-zone-oddeven-fill',
          type: 'fill',
          source: 'mb-zone-oddeven',
          paint: {
            'fill-color': '#3b82f6',
            'fill-opacity': 0.08
          }
        });
        map.addLayer({
          id: 'mb-zone-oddeven-line',
          type: 'line',
          source: 'mb-zone-oddeven',
          paint: {
            'line-color': '#3b82f6',
            'line-width': 2.5,
            'line-opacity': 0.9
          }
        });
      } else {
        map.getSource('mb-zone-oddeven').setData(oddFeat);
      }

      if (!map.getSource('mb-zone-traffic')) {
        map.addSource('mb-zone-traffic', { type: 'geojson', data: trafficFeat });
        map.addLayer({
          id: 'mb-zone-traffic-fill',
          type: 'fill',
          source: 'mb-zone-traffic',
          paint: {
            'fill-color': '#ef4444',
            'fill-opacity': 0.14
          }
        });
        map.addLayer({
          id: 'mb-zone-traffic-line',
          type: 'line',
          source: 'mb-zone-traffic',
          paint: {
            'line-color': '#ef4444',
            'line-width': 2.75,
            'line-opacity': 0.95
          }
        });
      } else {
        map.getSource('mb-zone-traffic').setData(trafficFeat);
      }

      state.zonesLoaded = true;
    } catch (e) {
      console.warn('restriction zones failed', e);
    }
  }

  /* ---------- Place search (district / alley / street) ---------- */

  var placeTimers = { origin: null, dest: null };
  var reverseTimer = null;
  var reverseSeq = 0;
  var _lastReverseKey = '';
  var _lastReverseAt = 0;

  function bindPlaceSearch(input, listEl, kind) {
    if (!input || !listEl) return;
    input.addEventListener('input', function () {
      clearTimeout(placeTimers[kind]);
      var q = input.value.trim();
      if (q.length < 2) {
        listEl.hidden = true;
        listEl.innerHTML = '';
        return;
      }
      placeTimers[kind] = setTimeout(function () {
        runPlaceSearch(q, listEl, kind);
      }, 500);
    });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') {
        listEl.hidden = true;
      }
    });
  }

  async function runPlaceSearch(q, listEl, kind) {
    var city = kind === 'origin' ? state.originCity : state.destCity;
    if (!city) return;
    listEl.hidden = false;
    listEl.innerHTML = '<li class="is-empty">در حال جستجو…</li>';

    try {
      var url = '/Reserve/PlaceSearch?q=' + encodeURIComponent(q) +
        '&city=' + encodeURIComponent(city.name) +
        '&lat=' + encodeURIComponent(city.lat) +
        '&lng=' + encodeURIComponent(city.lng);
      var res = await fetch(url, { credentials: 'same-origin' });
      var items = await res.json();
      if (!Array.isArray(items) || !items.length) {
        listEl.innerHTML = '<li class="is-empty">نتیجه‌ای پیدا نشد — عبارت دیگری امتحان کنید.</li>';
        return;
      }
      listEl.innerHTML = '';
      var shown = 0;
      items.forEach(function (item) {
        var ll = { lat: item.lat, lng: item.lng };
        if (!insideBorder(ll, city)) return;
        shown++;
        var li = document.createElement('li');
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.innerHTML = '<strong>' + escapeHtml(item.title || q) + '</strong>' +
          '<small>' + escapeHtml(item.subtitle || '') + '</small>';
        btn.addEventListener('click', function () {
          selectPlaceResult(item, kind, listEl);
        });
        li.appendChild(btn);
        listEl.appendChild(li);
      });
      if (!shown) {
        listEl.innerHTML = '<li class="is-empty">داخل محدوده شهر نتیجه‌ای نبود — عبارت دیگری امتحان کنید.</li>';
      }
    } catch (e) {
      console.warn(e);
      listEl.innerHTML = '<li class="is-empty">خطا در جستجو. دوباره تلاش کنید.</li>';
    }
  }

  function selectPlaceResult(item, kind, listEl) {
    var city = kind === 'origin' ? state.originCity : state.destCity;
    var ll = { lat: item.lat, lng: item.lng };
    if (city && !insideBorder(ll, city)) {
      pulseHint(kind === 'origin' ? els.originPinHint : els.destPinHint);
      listEl.hidden = true;
      return;
    }
    flyPickToLatLng(ll.lat, ll.lng, Math.max(map.getZoom(), 16), !REDUCE_MOTION);
    listEl.hidden = true;
    var input = kind === 'origin' ? els.originPlaceSearch : els.destPlaceSearch;
    if (input) input.value = item.title || '';
    setPinAddress(
      kind === 'origin' ? els.originAddress : els.destAddress,
      item.title,
      item.subtitle
    );
    if (item.source === 'venue' || item.venueId) {
      scheduleVenueDetect();
    } else {
      scheduleSnapPickToRoute();
    }
  }

  function reverseCurrentPin(force) {
    if (!state.picking) return;
    var ll = getPickLatLng();
    if (!ll) return;
    var key = geoCoordKey(ll.lat, ll.lng, 3);
    if (!force && key === _lastReverseKey && Date.now() - _lastReverseAt < 90000) return;
    clearTimeout(reverseTimer);
    reverseTimer = setTimeout(doReverse, 650);
  }

  async function doReverse() {
    if (!state.picking || !map) return;
    var ll = getPickLatLng();
    if (!ll) return;
    var key = geoCoordKey(ll.lat, ll.lng, 3);
    var seq = ++reverseSeq;
    var addrEl = state.picking === 'origin' ? els.originAddress : els.destAddress;
    var cached = readGeoCache('rev', key);
    if (cached && seq === reverseSeq) {
      _lastReverseKey = key;
      _lastReverseAt = Date.now();
      applyReverseResult(cached, addrEl);
      return;
    }
    if (addrEl) addrEl.innerHTML = '<strong>در حال یافتن آدرس…</strong>';
    try {
      var data = await fetchGeoCached('rev', key, async function () {
        var url = '/Reserve/ReverseGeocode?lat=' + encodeURIComponent(ll.lat) +
          '&lng=' + encodeURIComponent(ll.lng);
        var res = await fetch(url, { credentials: 'same-origin' });
        return res.json();
      });
      if (seq !== reverseSeq) return;
      _lastReverseKey = key;
      _lastReverseAt = Date.now();
      applyReverseResult(data, addrEl);
    } catch (e) {
      if (seq !== reverseSeq) return;
      setPinAddress(addrEl, '', '', null);
    }
  }

  function applyReverseResult(data, addrEl) {
    if (!data) {
      setPinAddress(addrEl, '', '', null);
      return;
    }
    var zones = {
      inTrafficZone: !!data.inTrafficZone,
      inOddEvenZone: !!data.inOddEvenZone
    };
    if (state.picking === 'origin') state.originZones = zones;
    else state.destZones = zones;
    setPinAddress(addrEl, data.title, data.subtitle, zones);
    var placeInput = state.picking === 'origin' ? els.originPlaceSearch : els.destPlaceSearch;
    if (placeInput && data.title && !placeInput.matches(':focus')) {
      placeInput.value = data.title;
    }
  }

  function pickCalloutLabel() {
    return state.picking === 'origin' ? 'آدرس مبدا' : 'مقصد';
  }

  function setPinAddress(el, summary, detail, zones) {
    var sum = summary || '';
    var det = detail || '';
    if (el) {
      el.innerHTML = '<strong>' + escapeHtml(sum || pickCalloutLabel()) + '</strong>' +
        (det ? '<br>' + escapeHtml(det) : '');
    }
    if (zones !== undefined) setZoneChips(zones);
  }

  function setZoneChips(zones) {
    if (!els.pinZones) return;
    if (!zones) {
      els.pinZones.hidden = true;
      return;
    }
    els.pinZones.hidden = false;
    paintZoneChip(els.zoneTraffic, !!zones.inTrafficZone, 'طرح ترافیک');
    paintZoneChip(els.zoneOddEven, !!zones.inOddEvenZone, 'طرح آلودگی هوا');
  }

  function paintZoneChip(el, inside, label) {
    if (!el) return;
    el.hidden = false;
    el.classList.toggle('is-inside', inside);
    el.classList.toggle('is-outside', !inside);
    el.textContent = (inside ? 'داخل ' : 'خارج از ') + label;
  }

  function renderRouteZoneSummary() {
    /* Zone status chips removed from map chrome */
  }

  function toggleLiveTraffic() {
    if (!map) return;
    state.trafficUserTouched = true;
    var next = !state.trafficOn;
    try {
      if (typeof map.toggleTrafficLayer === 'function') {
        map.toggleTrafficLayer(next);
        state.trafficOn = typeof map.trafficLayer === 'boolean' ? !!map.trafficLayer : next;
      } else {
        state.trafficOn = next;
      }
    } catch (e) {
      console.warn('traffic toggle', e);
      state.trafficOn = next;
    }
    syncTrafficToggleUi();
    if (state.trafficOn) scheduleTrafficStylePolish();
    else stopTrafficWave(true);
  }

  function syncTrafficToggleUi() {
    if (!els.trafficToggle) return;
    els.trafficToggle.classList.toggle('is-on', !!state.trafficOn);
    els.trafficToggle.setAttribute('aria-pressed', state.trafficOn ? 'true' : 'false');
    els.trafficToggle.title = state.trafficOn ? 'ترافیک زنده: روشن' : 'ترافیک زنده: خاموش';
  }

  function mapDistanceMeters(a, b) {
    var lat1 = a.lat != null ? a.lat : a[0];
    var lng1 = a.lng != null ? a.lng : a[1];
    var lat2 = b.lat != null ? b.lat : b[0];
    var lng2 = b.lng != null ? b.lng : b[1];
    var R = 6371000;
    var dLat = (lat2 - lat1) * Math.PI / 180;
    var dLon = (lng2 - lng1) * Math.PI / 180;
    var x = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
      Math.sin(dLon / 2) * Math.sin(dLon / 2);
    return R * 2 * Math.atan2(Math.sqrt(x), Math.sqrt(1 - x));
  }

  function flyToLatLng(lat, lng, zoom, animate) {
    if (!map) return;
    var opts = {
      center: [lng, lat],
      zoom: zoom
    };
    if (animate === false || REDUCE_MOTION) {
      map.jumpTo(opts);
    } else {
      map.easeTo(Object.assign({ duration: 520 }, opts));
    }
  }

  /** Screen position of the center-pin tip relative to the map canvas. */
  function getPickPinScreenPoint() {
    var mapEl = document.getElementById('mapBookMap');
    if (!map || !mapEl) return null;
    if (els.centerPin && !els.centerPin.hidden) {
      var pinRect = els.centerPin.getBoundingClientRect();
      if (pinRect.width > 0 && pinRect.height > 0) {
        var mapRect = mapEl.getBoundingClientRect();
        return {
          x: pinRect.left + pinRect.width / 2 - mapRect.left,
          y: pinRect.bottom - mapRect.top
        };
      }
    }
    var anchorY = 0.42;
    try {
      var pinStyle = els.centerPin && getComputedStyle(els.centerPin);
      var topVal = pinStyle && (pinStyle.top || pinStyle.getPropertyValue('--mb-pick-anchor-y'));
      if (topVal && topVal.indexOf('%') >= 0) anchorY = parseFloat(topVal) / 100;
    } catch (e) { /* use default */ }
    return {
      x: mapEl.clientWidth * 0.5,
      y: mapEl.clientHeight * anchorY
    };
  }

  /** Lat/lng under the visible pick pin tip (not map geometric center). */
  function getPickLatLng() {
    if (!map) return null;
    var pt = getPickPinScreenPoint();
    if (!pt) {
      var c = map.getCenter();
      return { lat: c.lat, lng: c.lng };
    }
    var ll = map.unproject([pt.x, pt.y]);
    return { lat: ll.lat, lng: ll.lng };
  }

  /** Move the map so the pick pin tip lands on (lat, lng). */
  function flyPickToLatLng(lat, lng, zoom, animate) {
    if (!map) return;
    var pinPt = getPickPinScreenPoint();
    if (!pinPt) {
      flyToLatLng(lat, lng, zoom, animate);
      return;
    }
    var targetPt = map.project([lng, lat]);
    var centerPt = map.project(map.getCenter());
    var newCenterPt = {
      x: centerPt.x + (targetPt.x - pinPt.x),
      y: centerPt.y + (targetPt.y - pinPt.y)
    };
    var nc = map.unproject([newCenterPt.x, newCenterPt.y]);
    var opts = {
      center: [nc.lng, nc.lat],
      zoom: zoom == null ? map.getZoom() : zoom
    };
    if (animate === false || REDUCE_MOTION) {
      map.jumpTo(opts);
    } else {
      map.easeTo(Object.assign({ duration: 480 }, opts));
    }
  }

  function isSameCity(a, b) {
    if (!a || !b) return false;
    if (a.id === b.id) return true;
    return normalizeFa(a.name || '') === normalizeFa(b.name || '');
  }

  function getPickMapPadding() {
    var base = getMapUiPadding();
    var mapEl = document.getElementById('mapBookMap');
    var mapW = (mapEl && mapEl.clientWidth) || window.innerWidth || 400;
    var side = Math.max(28, Math.round(mapW * 0.1));
    base.left = side;
    base.right = side;
    return base;
  }

  function getRoutePreviewPadding() {
    var base = getMapUiPadding();
    var mapEl = document.getElementById('mapBookMap');
    var mapW = (mapEl && mapEl.clientWidth) || window.innerWidth || 400;
    var mapH = (mapEl && mapEl.clientHeight) || window.innerHeight || 600;
    var side = Math.max(36, Math.round(mapW * 0.1));
    base.left = Math.max(base.left, side);
    base.right = Math.max(base.right, side);
    base.top = Math.max(base.top, Math.round(mapH * 0.14));
    return base;
  }

  function fitBboxInView(bbox, opts) {
    if (!map || !mapReady || !bbox) return;
    opts = opts || {};
    var latSpan = bbox[1][1] - bbox[0][1];
    var lngSpan = bbox[1][0] - bbox[0][0];
    var latPad = Math.max(latSpan * 0.14, 0.0015);
    var lngPad = Math.max(lngSpan * 0.14, 0.0015);
    var padded = [
      [bbox[0][0] - lngPad, bbox[0][1] - latPad],
      [bbox[1][0] + lngPad, bbox[1][1] + latPad]
    ];
    try {
      map.fitBounds(padded, {
        padding: opts.padding || getPickMapPadding(),
        maxZoom: opts.maxZoom || 14.5,
        duration: REDUCE_MOTION ? 0 : (opts.duration || 520),
        bearing: 0,
        pitch: 0,
        essential: true
      });
    } catch (e) { /* ignore */ }
  }

  function fitVenueInView(venue) {
    if (!venue || !venue.polygon || !venue.polygon.length) return;
    var minLng = Infinity, maxLng = -Infinity, minLat = Infinity, maxLat = -Infinity;
    venue.polygon.forEach(function (p) {
      minLng = Math.min(minLng, p[0]); maxLng = Math.max(maxLng, p[0]);
      minLat = Math.min(minLat, p[1]); maxLat = Math.max(maxLat, p[1]);
    });
    fitBboxInView([[minLng, minLat], [maxLng, maxLat]], { maxZoom: 14.2 });
  }

  function getMapUiPadding() {
    var desktop = window.matchMedia('(min-width: 1024px)').matches;
    var mapEl = document.getElementById('mapBookMap');
    var mapH = (mapEl && mapEl.clientHeight) || window.innerHeight || 600;
    var mapW = (mapEl && mapEl.clientWidth) || window.innerWidth || 800;
    var pad = { top: 80, bottom: 48, left: 28, right: 28 };

    if (desktop) {
      var panelW = 0;
      if (els.sheet) panelW = els.sheet.getBoundingClientRect().width;
      if (!panelW && els.root) {
        panelW = parseFloat(getComputedStyle(els.root).getPropertyValue('--mb-side-panel')) || 0;
      }
      if (!panelW) panelW = Math.min(424, mapW * 0.38);
      // Full-bleed map under the right panel — reserve that strip
      pad.right = Math.round(panelW + 32);
      pad.top = 56;
      pad.bottom = 40;
      pad.left = 40;
    } else if (els.sheet) {
      var sheetRect = els.sheet.getBoundingClientRect();
      var sheetH = sheetRect.height;
      var viewH = (window.visualViewport && window.visualViewport.height) || window.innerHeight || mapH;
      if (!sheetH) {
        sheetH = Math.round(mapH * (els.sheet.classList.contains('is-collapsed')
          ? 0.08
          : (els.sheet.classList.contains('is-roomy') ? 0.48 : 0.4)));
      }
      pad.top = Math.max(84, Math.round(viewH * 0.1));
      pad.left = 16;
      pad.right = 16;
      var routePreview = els.root && els.root.classList.contains('is-route-preview');
      var bottomReserve = routePreview ? Math.max(sheetH, viewH * 0.36) : sheetH;
      pad.bottom = Math.round(bottomReserve + 24);
    }

    // Keep a usable map viewport so fitBounds does not collapse
    var minViewH = 140;
    var minViewW = 160;
    if (pad.top + pad.bottom > mapH - minViewH) {
      var vRoom = Math.max(minViewH, mapH - minViewH);
      var vSum = pad.top + pad.bottom || 1;
      pad.top = Math.round(vRoom * (pad.top / vSum));
      pad.bottom = vRoom - pad.top;
    }
    if (pad.left + pad.right > mapW - minViewW) {
      var hRoom = Math.max(minViewW, mapW - minViewW);
      var hSum = pad.left + pad.right || 1;
      pad.left = Math.round(hRoom * (pad.left / hSum));
      pad.right = hRoom - pad.left;
    }

    return pad;
  }

  function routeBoundsLngLat() {
    if (!state.routeCoords || state.routeCoords.length < 2) return null;
    var minLat = Infinity, maxLat = -Infinity, minLng = Infinity, maxLng = -Infinity;
    var pts = state.routeCoords;
    var step = pts.length > 600 ? Math.ceil(pts.length / 500) : 1;
    for (var i = 0; i < pts.length; i += step) {
      var c = pts[i];
      var lat = c[0], lng = c[1];
      if (lat < minLat) minLat = lat;
      if (lat > maxLat) maxLat = lat;
      if (lng < minLng) minLng = lng;
      if (lng > maxLng) maxLng = lng;
    }
    var last = pts[pts.length - 1];
    minLat = Math.min(minLat, last[0]); maxLat = Math.max(maxLat, last[0]);
    minLng = Math.min(minLng, last[1]); maxLng = Math.max(maxLng, last[1]);

    // Also include pinned origin/dest if present
    if (state.originLatLng) {
      minLat = Math.min(minLat, state.originLatLng.lat);
      maxLat = Math.max(maxLat, state.originLatLng.lat);
      minLng = Math.min(minLng, state.originLatLng.lng);
      maxLng = Math.max(maxLng, state.originLatLng.lng);
    }
    if (state.destLatLng) {
      minLat = Math.min(minLat, state.destLatLng.lat);
      maxLat = Math.max(maxLat, state.destLatLng.lat);
      minLng = Math.min(minLng, state.destLatLng.lng);
      maxLng = Math.max(maxLng, state.destLatLng.lng);
    }

    // Pad bounds so the full road route fits comfortably in view
    var latPad = Math.max((maxLat - minLat) * 0.14, 0.018);
    var lngPad = Math.max((maxLng - minLng) * 0.14, 0.018);
    minLat -= latPad; maxLat += latPad;
    minLng -= lngPad; maxLng += lngPad;

    if (minLat === maxLat) { minLat -= 0.02; maxLat += 0.02; }
    if (minLng === maxLng) { minLng -= 0.02; maxLng += 0.02; }
    return [[minLng, minLat], [maxLng, maxLat]];
  }

  function bearingBetween(a, b) {
    var lat1 = (a.lat != null ? a.lat : a[0]) * Math.PI / 180;
    var lat2 = (b.lat != null ? b.lat : b[0]) * Math.PI / 180;
    var lng1 = (a.lng != null ? a.lng : a[1]) * Math.PI / 180;
    var lng2 = (b.lng != null ? b.lng : b[1]) * Math.PI / 180;
    var dLng = lng2 - lng1;
    var y = Math.sin(dLng) * Math.cos(lat2);
    var x = Math.cos(lat1) * Math.sin(lat2) - Math.sin(lat1) * Math.cos(lat2) * Math.cos(dLng);
    var brng = Math.atan2(y, x) * 180 / Math.PI;
    return (brng + 360) % 360;
  }

  /** Geographic bounds that contain the full route when the map is at bearingDeg. */
  function routeBoundsForRotatedView(bearingDeg) {
    if (!state.routeCoords || state.routeCoords.length < 2) return routeBoundsLngLat();

    var br = ((bearingDeg % 360) + 360) % 360 * Math.PI / 180;
    var cosB = Math.cos(br);
    var sinB = Math.sin(br);

    var points = [];
    var pts = state.routeCoords;
    var step = pts.length > 500 ? Math.ceil(pts.length / 500) : 1;
    for (var i = 0; i < pts.length; i += step) {
      points.push({ lat: pts[i][0], lng: pts[i][1] });
    }
    points.push({ lat: pts[pts.length - 1][0], lng: pts[pts.length - 1][1] });
    if (state.originLatLng) points.push(state.originLatLng);
    if (state.destLatLng) points.push(state.destLatLng);

    var sumLat = 0, sumLng = 0;
    for (var j = 0; j < points.length; j++) {
      sumLat += points[j].lat;
      sumLng += points[j].lng;
    }
    var centerLat = sumLat / points.length;
    var centerLng = sumLng / points.length;
    var cosLat = Math.max(0.2, Math.cos(centerLat * Math.PI / 180));

    var minRx = Infinity, maxRx = -Infinity, minRy = Infinity, maxRy = -Infinity;
    for (var k = 0; k < points.length; k++) {
      var p = points[k];
      var x = (p.lng - centerLng) * cosLat;
      var y = p.lat - centerLat;
      var rx = x * cosB + y * sinB;
      var ry = -x * sinB + y * cosB;
      if (rx < minRx) minRx = rx;
      if (rx > maxRx) maxRx = rx;
      if (ry < minRy) minRy = ry;
      if (ry > maxRy) maxRy = ry;
    }

    var spanRx = maxRx - minRx || 0.01;
    var spanRy = maxRy - minRy || 0.01;
    var padRx = spanRx * 0.18 + 0.006;
    var padRy = spanRy * 0.18 + 0.006;
    minRx -= padRx; maxRx += padRx;
    minRy -= padRy; maxRy += padRy;

    var corners = [
      [minRx, minRy], [maxRx, minRy], [maxRx, maxRy], [minRx, maxRy]
    ];
    var minLng = Infinity, maxLng = -Infinity, minLat = Infinity, maxLat = -Infinity;
    for (var c = 0; c < corners.length; c++) {
      var rx = corners[c][0], ry = corners[c][1];
      var gx = rx * cosB - ry * sinB;
      var gy = rx * sinB + ry * cosB;
      var lng = centerLng + gx / cosLat;
      var lat = centerLat + gy;
      if (lng < minLng) minLng = lng;
      if (lng > maxLng) maxLng = lng;
      if (lat < minLat) minLat = lat;
      if (lat > maxLat) maxLat = lat;
    }

    if (!isFinite(minLng)) return routeBoundsLngLat();
    return [[minLng, minLat], [maxLng, maxLat]];
  }

  function applyRouteCamera(bounds, bearing, padding, maxZoom, duration) {
    if (!map || !bounds) return;
    var mobile = !window.matchMedia('(min-width: 1024px)').matches;
    var rotated = bearing !== 0;

    try {
      if (typeof map.cameraForBounds === 'function') {
        var cam = map.cameraForBounds(bounds, {
          padding: padding,
          maxZoom: maxZoom,
          bearing: bearing,
          pitch: 0
        });
        if (cam) {
          var zoom = Math.min(cam.zoom, maxZoom);
          if (rotated && mobile) zoom = Math.max(4.5, zoom - 0.5);
          map.easeTo({
            center: cam.center,
            zoom: zoom,
            bearing: bearing,
            pitch: 0,
            duration: duration,
            essential: true
          });
          return;
        }
      }
    } catch (e) { /* fall through to fitBounds */ }

    try {
      map.fitBounds(bounds, {
        padding: padding,
        maxZoom: maxZoom,
        duration: duration,
        essential: true,
        bearing: bearing,
        pitch: 0
      });
    } catch (e2) {
      console.warn('fitBounds failed', e2);
    }
  }

  /** Rotate map so origin→dest runs horizontally in the visible rectangle (Uber-style overview). */
  function routeOverviewBearing() {
    if (!state.originLatLng || !state.destLatLng) return 0;
    var brng = bearingBetween(state.originLatLng, state.destLatLng);
    return brng - 90;
  }

  /** Closest point on polyline to a lat/lng (path = [[lat,lng], …]). */
  function projectPointOnSegment(px, py, ax, ay, bx, by) {
    var dx = bx - ax;
    var dy = by - ay;
    var len2 = dx * dx + dy * dy;
    if (len2 < 1e-14) return { lat: ax, lng: ay };
    var t = ((px - ax) * dx + (py - ay) * dy) / len2;
    t = Math.max(0, Math.min(1, t));
    return { lat: ax + t * dx, lng: ay + t * dy };
  }

  function nearestPointOnPath(lat, lng, path, opts) {
    if (!path || path.length < 2) return null;
    opts = opts || {};
    var minSeg = Math.max(0, opts.minIndex || 0);
    var maxSeg = opts.maxIndex != null
      ? Math.min(opts.maxIndex, path.length - 2)
      : path.length - 2;
    if (minSeg > maxSeg) return null;

    var best = null;
    var bestDist = Infinity;
    for (var i = minSeg; i <= maxSeg; i++) {
      var proj = projectPointOnSegment(
        lat, lng,
        path[i][0], path[i][1],
        path[i + 1][0], path[i + 1][1]
      );
      var d = mapDistanceMeters({ lat: lat, lng: lng }, proj);
      if (d < bestDist) {
        bestDist = d;
        best = proj;
      }
    }
    return best ? { lat: best.lat, lng: best.lng, distM: bestDist } : null;
  }

  var _routeApiCooldownUntil = 0;

  async function fetchRouteGeometry(o, d, attempt) {
    if (!o || !d) return null;
    attempt = attempt || 0;

    var cacheKey = geoRouteKey(o, d);
    var cached = readGeoCache('route', cacheKey);
    if (cached) {
      if (routePayloadIsReal(cached, o, d)) return cached;
      purgeGeoCache('route', cacheKey);
    }

    if (Date.now() < _routeApiCooldownUntil && attempt === 0) {
      await new Promise(function (r) { setTimeout(r, Math.max(0, _routeApiCooldownUntil - Date.now())); });
    }

    try {
      var url = '/Reserve/OsrmRoute?oLat=' + encodeURIComponent(o.lat) +
        '&oLng=' + encodeURIComponent(o.lng) +
        '&dLat=' + encodeURIComponent(d.lat) +
        '&dLng=' + encodeURIComponent(d.lng);

      var data = await fetchGeoCached('route', cacheKey, async function () {
        var res = await fetch(url, { credentials: 'same-origin' });
        if (!res.ok) throw new Error('route HTTP ' + res.status);
        var body = await res.json();
        var route = body.routes && body.routes[0];
        var geom = route && route.geometry;
        if (!geom || !geom.coordinates || geom.coordinates.length < 2) return null;
        var path = geom.coordinates.map(function (c) {
          return [Number(c[1]), Number(c[0])];
        }).filter(function (p) {
          return isFinite(p[0]) && isFinite(p[1]);
        });
        if (path.length < 2) return null;
        var payload = {
          source: body.source || 'osrm',
          path: path,
          duration: route.duration,
          distance: route.distance
        };
        if (!routePayloadIsReal(payload, o, d)) return null;
        return payload;
      });

      if (data && routePayloadIsReal(data, o, d)) {
        _routeApiCooldownUntil = 0;
        return data;
      }
      if (data) purgeGeoCache('route', cacheKey);
    } catch (e) {
      var msg = e && e.message ? String(e.message) : '';
      if (e && (e.name === 'TypeError' || msg.indexOf('fetch') >= 0 || msg.indexOf('network') >= 0)) {
        _routeApiCooldownUntil = Date.now() + 3500;
      }
      if (attempt === 0) console.warn('fetchRouteGeometry', e);
    }
    if (attempt < 3) {
      await new Promise(function (r) { setTimeout(r, attempt === 0 ? 400 : (attempt === 1 ? 900 : 1600)); });
      return fetchRouteGeometry(o, d, attempt + 1);
    }
    return null;
  }

  var snapRouteTimer = null;
  var snapRouteSeq = 0;
  var _roadSnapLock = false;
  var _borderSnapLock = false;
  var _snapPending = false;
  var _programmaticSnapMove = false;
  var _roadSnapSafetyTimer = null;
  var SNAP_MIN_M = 0.5;
  var SNAP_MAX_M = 2000;

  function waitForMapIdleThenSnap() {
    if (!map || _snapIdleWait) return;
    _snapIdleWait = true;
    var released = false;
    var done = function () {
      if (released) return;
      released = true;
      _snapIdleWait = false;
      scheduleSnapPickToRoute();
    };
    if (typeof map.once === 'function') {
      map.once('idle', done);
      setTimeout(done, 1200);
    } else {
      setTimeout(done, 400);
    }
  }

  /** While picking origin/dest, snap the center pin onto the nearest driving road. */
  function scheduleSnapPickToRoute() {
    if (!state.picking || !map) return;
    // Inside airport/hospital/mall — keep entrance pin, don't pull to outer road
    if (state.activeVenue) return;
    if (_roadSnapLock || _borderSnapLock || _programmaticSnapMove) {
      _snapPending = true;
      return;
    }
    clearTimeout(snapRouteTimer);
    snapRouteTimer = setTimeout(snapPickToRoute, 160);
  }

  function releaseRoadSnapLock() {
    _roadSnapLock = false;
    _programmaticSnapMove = false;
    if (_roadSnapSafetyTimer) {
      clearTimeout(_roadSnapSafetyTimer);
      _roadSnapSafetyTimer = null;
    }
    if (_snapPending && state.picking) {
      _snapPending = false;
      scheduleSnapPickToRoute();
    }
  }

  async function fetchNearestRoad(lat, lng) {
    var cacheKey = geoCoordKey(lat, lng, 3);
    var cached = readGeoCache('near', cacheKey);
    if (cached) return cached;

    // Coarser cache — instant snap while a precise fetch is in flight
    var coarseKey = geoCoordKey(lat, lng, 2);
    var coarse = readGeoCache('near', coarseKey);

    try {
      var hit = await fetchGeoCached('near', cacheKey, async function () {
        var url = '/Reserve/NearestRoad?lat=' + encodeURIComponent(lat) +
          '&lng=' + encodeURIComponent(lng);
        var res = await fetch(url, { credentials: 'same-origin' });
        if (!res.ok) return null;
        var data = await res.json();
        if (!data || !data.ok || data.lat == null || data.lng == null) return null;
        return {
          lat: Number(data.lat),
          lng: Number(data.lng),
          distM: Number(data.distance) || mapDistanceMeters(
            { lat: lat, lng: lng },
            { lat: Number(data.lat), lng: Number(data.lng) }
          ),
          source: data.source || 'api'
        };
      });
      if (hit) {
        writeGeoCache('near', coarseKey, hit);
        return hit;
      }
    } catch (e) {
      console.warn('fetchNearestRoad', e);
    }
    return coarse || null;
  }

  /** Prefer road-snapped O/D endpoint from a real driving route when available. */
  async function fetchRouteEndpointSnap(center) {
    if (state.picking === 'dest' && state.originLatLng) {
      var toDest = await fetchRouteGeometry(state.originLatLng, center);
      if (toDest && toDest.path && toDest.path.length >= 2) {
        var end = toDest.path[toDest.path.length - 1];
        return {
          lat: end[0],
          lng: end[1],
          distM: mapDistanceMeters(center, { lat: end[0], lng: end[1] })
        };
      }
    }
    if (state.picking === 'origin' && state.destLatLng) {
      var fromOrigin = await fetchRouteGeometry(center, state.destLatLng);
      if (fromOrigin && fromOrigin.path && fromOrigin.path.length >= 2) {
        var start = fromOrigin.path[0];
        return {
          lat: start[0],
          lng: start[1],
          distM: mapDistanceMeters(center, { lat: start[0], lng: start[1] })
        };
      }
    }
    return null;
  }

  async function snapPickToRoute() {
    if (!state.picking || !map || _borderSnapLock || _roadSnapLock) {
      if (state.picking && (_roadSnapLock || _borderSnapLock)) {
        _snapPending = true;
      }
      return;
    }
    if (state.activeVenue) return;
    if (typeof map.isMoving === 'function' && map.isMoving()) {
      waitForMapIdleThenSnap();
      return;
    }

    var center = getPickLatLng();
    if (!center) return;
    var seq = ++snapRouteSeq;

    // Apply coarse cached snap immediately so the pin moves without waiting on the network
    var coarseKey = geoCoordKey(center.lat, center.lng, 2);
    var coarseSnap = readGeoCache('near', coarseKey);
    if (coarseSnap && coarseSnap.distM >= SNAP_MIN_M && coarseSnap.distM <= SNAP_MAX_M &&
        mapDistanceMeters(center, coarseSnap) >= SNAP_MIN_M) {
      flyPickToLatLng(coarseSnap.lat, coarseSnap.lng, map.getZoom(), false);
    }

    var near = await fetchNearestRoad(center.lat, center.lng);
    if (seq !== snapRouteSeq || !state.picking) return;

    if (!near || near.distM < SNAP_MIN_M || near.distM > SNAP_MAX_M) {
      reverseCurrentPin();
      return;
    }

    // Already on road — nothing to do
    if (mapDistanceMeters(center, near) < SNAP_MIN_M) {
      reverseCurrentPin();
      return;
    }

    // Keep pin inside the selected city border
    var city = state.picking === 'origin' ? state.originCity : state.destCity;
    if (city && !insideBorder(near, city)) return;

    _roadSnapLock = true;
    _programmaticSnapMove = true;
    _snapPending = false;
    if (_roadSnapSafetyTimer) clearTimeout(_roadSnapSafetyTimer);
    _roadSnapSafetyTimer = setTimeout(function () {
      // Hard unlock so a missed moveend never blocks later snaps
      if (_roadSnapLock || _programmaticSnapMove) releaseRoadSnapLock();
    }, 1600);

    flyPickToLatLng(near.lat, near.lng, map.getZoom(), !REDUCE_MOTION);
    var released = false;
    function finishSnapMove() {
      if (released) return;
      released = true;
      releaseRoadSnapLock();
      if (!state.picking) return;
      reverseCurrentPin();
      scheduleConfirmPeek();
    }
    if (REDUCE_MOTION || typeof map.once !== 'function') {
      finishSnapMove();
    } else {
      map.once('moveend', finishSnapMove);
      setTimeout(finishSnapMove, 700);
    }
  }

  /** After route is drawn, pull O/D markers onto the polyline (highway snap). */
  function snapMarkersToRoute() {
    if (!state.routeCoords || state.routeCoords.length < 2) return;
    var path = state.routeCoords;
    var n = path.length;
    var originCap = Math.max(1, Math.floor(n * 0.4));
    var destStart = Math.max(0, Math.floor(n * 0.6));

    if (state.originLatLng) {
      var oSnap = nearestPointOnPath(
        state.originLatLng.lat, state.originLatLng.lng, path,
        { maxIndex: originCap }
      );
      if (oSnap && oSnap.distM >= SNAP_MIN_M) {
        state.originLatLng = { lat: oSnap.lat, lng: oSnap.lng };
        placeOriginMarker(state.originLatLng);
        positionOverlay(els.originBadge, state.originLatLng);
      }
    }
    if (state.destLatLng) {
      var dSnap = nearestPointOnPath(
        state.destLatLng.lat, state.destLatLng.lng, path,
        { minIndex: destStart }
      );
      if (dSnap && dSnap.distM >= SNAP_MIN_M) {
        state.destLatLng = { lat: dSnap.lat, lng: dSnap.lng };
        placeDestMarker(state.destLatLng);
        positionOverlay(els.destBadge, state.destLatLng);
      }
    }
  }

  function shouldRotateRouteOverview() {
    return !window.matchMedia('(min-width: 1024px)').matches;
  }

  function arrangeMapToRoute(userTriggered) {
    if (!map || !mapReady || !state.routeCoords || state.routeCoords.length < 2) return;

    var bearing = shouldRotateRouteOverview() ? routeOverviewBearing() : 0;
    var bounds = bearing !== 0 ? routeBoundsForRotatedView(bearing) : routeBoundsLngLat();
    if (!bounds) return;

    var distKm = mapDistanceMeters(state.routeCoords[0], state.routeCoords[state.routeCoords.length - 1]) / 1000;
    var mobile = !window.matchMedia('(min-width: 1024px)').matches;
    var maxZoom = distKm > 120 ? 6.5 : distKm > 80 ? 7.2 : distKm > 40 ? 8.0 : distKm > 25 ? 8.8 : distKm > 12 ? 9.6 : distKm > 5 ? 10.4 : 11.5;
    if (mobile && distKm > 6) maxZoom = Math.min(maxZoom, 9.2);
    if (bearing !== 0 && mobile && distKm > 20) maxZoom = Math.min(maxZoom, 8.6);
    var duration = userTriggered ? 480 : (REDUCE_MOTION ? 0 : 720);

    try { map.resize(); } catch (e) { /* ignore */ }

    var padding = getRoutePreviewPadding();
    applyRouteCamera(bounds, bearing, padding, maxZoom, duration);

    setTimeout(function () {
      positionRouteEta();
      if (routeCanvas) routeCanvas.redraw();
    }, duration + 40);
  }

  function autoArrangeRouteCamera() {
    clearTimeout(autoArrangeRouteCamera._t1);
    clearTimeout(autoArrangeRouteCamera._t2);
    clearTimeout(autoArrangeRouteCamera._t3);
    requestAnimationFrame(function () {
      arrangeMapToRoute(false);
      autoArrangeRouteCamera._t1 = setTimeout(function () {
        arrangeMapToRoute(false);
      }, 380);
      autoArrangeRouteCamera._t2 = setTimeout(function () {
        arrangeMapToRoute(false);
      }, 900);
      autoArrangeRouteCamera._t3 = setTimeout(function () {
        arrangeMapToRoute(false);
        positionRouteEta();
        if (routeCanvas) routeCanvas.redraw();
      }, 1400);
    });
  }

  /* ---------- City / province UI ---------- */

  function renderProvinces() {
    els.provinceChips.innerHTML = '';
    var all = document.createElement('button');
    all.type = 'button';
    all.className = 'mapbook__chip is-active';
    all.textContent = 'همه';
    all.addEventListener('click', function () {
      state.provinceFilter = null;
      syncChips(all);
      renderCityList(els.cityList, els.citySearch.value, false);
    });
    els.provinceChips.appendChild(all);

    state.provinces.forEach(function (p) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'mapbook__chip';
      b.textContent = p;
      b.addEventListener('click', function () {
        state.provinceFilter = p;
        syncChips(b);
        renderCityList(els.cityList, els.citySearch.value, false);
      });
      els.provinceChips.appendChild(b);
    });
  }

  function syncChips(active) {
    els.provinceChips.querySelectorAll('.mapbook__chip').forEach(function (c) {
      c.classList.toggle('is-active', c === active);
    });
  }

  function cityMatchesQuery(city, qNorm) {
    if (!qNorm) return true;
    var name = normalizeFa(city.name || '');
    var province = normalizeFa(city.province || '');
    var id = normalizeFa(city.id || '');
    if (name.indexOf(qNorm) !== -1 || province.indexOf(qNorm) !== -1) return true;
    if (id.indexOf(qNorm) !== -1) return true;
    if ((qNorm === 'وان' || qNorm === 'van' || qNorm === 'ون') &&
        (id === 'van' || id === 'van-airport')) return true;
    if ((qNorm === 'ترکیه' || qNorm === 'turkey') &&
        (id === 'van' || id === 'van-airport')) return true;
    // Allow partial word match (e.g. "خرم" → خرم‌آباد)
    var qParts = qNorm.split(/\s+/).filter(Boolean);
    if (!qParts.length) return true;
    return qParts.every(function (part) {
      return name.indexOf(part) !== -1 || province.indexOf(part) !== -1;
    });
  }

  function renderCityList(listEl, query, isDest) {
    var q = (query || '').trim();
    var qNorm = normalizeFa(q);
    var items = state.cities.filter(function (c) {
      if (isDest && state.originCity && isSameCity(c, state.originCity)) return false;
      if (!isDest && state.destCity && isSameCity(c, state.destCity)) return false;
      // When user is searching, scan all cities (ignore province chip filter)
      if (!qNorm && !isDest && state.provinceFilter && c.province !== state.provinceFilter) return false;
      return cityMatchesQuery(c, qNorm);
    });

    // Empty search = suggested hubs only
    if (!qNorm && !state.provinceFilter) {
      var suggestedIds = ['tehran', 'isfahan', 'mashhad', 'shiraz', 'rasht', 'karaj', 'qom', 'kermanshah', 'van'];
      var suggested = [];
      suggestedIds.forEach(function (id) {
        var hit = state.cities.find(function (c) { return c.id === id; });
        if (!hit) return;
        if (isDest && state.originCity && isSameCity(hit, state.originCity)) return;
        if (!isDest && state.destCity && isSameCity(hit, state.destCity)) return;
        suggested.push(hit);
      });
      if (suggested.length) items = suggested;
      else items = items.slice(0, SUGGESTED_CITY_LIMIT);
    } else if (qNorm) {
      items.sort(function (a, b) {
        var aName = normalizeFa(a.name);
        var bName = normalizeFa(b.name);
        var aStarts = aName.indexOf(qNorm) === 0 ? 0 : 1;
        var bStarts = bName.indexOf(qNorm) === 0 ? 0 : 1;
        if (aStarts !== bStarts) return aStarts - bStarts;
        return a.name.localeCompare(b.name, 'fa');
      });
      items = items.slice(0, 40);
    }

    listEl.innerHTML = '';
    if (!items.length) {
      listEl.innerHTML = '<li><button type="button" disabled>' +
        (qNorm ? 'شهری با این نام پیدا نشد — نام کامل‌تر بنویسید' : 'شهری پیدا نشد') +
        '</button></li>';
      return;
    }

    items.forEach(function (c) {
      var li = document.createElement('li');
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.innerHTML = '<span>' + escapeHtml(c.name) + '</span><small>' + escapeHtml(c.province) + '</small>';
      btn.addEventListener('click', function () {
        if (isDest) selectDestCity(c);
        else selectOriginCity(c);
      });
      li.appendChild(btn);
      listEl.appendChild(li);
    });
  }

  function selectOriginCity(city) {
    state.originCity = city;
    updateMapBounds();
    startPicking('origin', city);
    els.originPinHint.hidden = false;
    els.cityList.style.display = 'none';
    els.provinceChips.style.display = 'none';
    els.citySearch.parentElement.style.display = 'none';
  }

  function selectDestCity(city) {
    state.destCity = city;
    updateMapBounds();
    startPicking('dest', city);
    els.destPinHint.hidden = false;
    els.destList.style.display = 'none';
    els.destSearch.parentElement.style.display = 'none';
  }

  function startPicking(kind, city) {
    state.picking = kind;
    state.sheetPinMode = null;
    state.sheetCollapsed = false;
    _roadSnapLock = false;
    _programmaticSnapMove = false;
    _snapPending = false;
    _snapIdleWait = false;
    if (_roadSnapSafetyTimer) {
      clearTimeout(_roadSnapSafetyTimer);
      _roadSnapSafetyTimer = null;
    }
    els.root.classList.toggle('is-picking-origin', kind === 'origin');
    els.root.classList.toggle('is-picking-dest', kind === 'dest');
    els.centerPin.hidden = false;

    whenMapReady(function () {
      fitToCityBorder(city);
      updateBoundVisual();
      reverseCurrentPin();
      setTimeout(scheduleSnapPickToRoute, REDUCE_MOTION ? 80 : 280);
    });

    var placeInput = kind === 'origin' ? els.originPlaceSearch : els.destPlaceSearch;
    var placeList = kind === 'origin' ? els.originPlaceList : els.destPlaceList;
    if (placeInput) {
      placeInput.value = '';
      setTimeout(function () { placeInput.focus(); }, 200);
    }
    if (placeList) {
      placeList.hidden = true;
      placeList.innerHTML = '';
    }
    if (els.centerPin) els.centerPin.hidden = false;
    try {
      if (map && typeof map.togglePoiLayer === 'function') map.togglePoiLayer(true);
    } catch (e) { /* ignore */ }
    syncSnappBuildingLayers();
    syncBuildingPlaqueVisibility();
    scheduleBuildingPlaqueFetch();
    scheduleVenueDetect();
    setConfirmPinBusy(false);
    syncSheetBack();
  }

  function exitPicking() {
    state.picking = null;
    clearTimeout(scheduleConfirmPeek._t);
    clearTimeout(buildingPlaqueTimer);
    state.sheetPinMode = null;
    setConfirmPinBusy(false);
    els.root.classList.remove('is-picking-origin', 'is-picking-dest');
    els.centerPin.hidden = true;
    clearBoundVisual();
    setZoneChips(null);
    clearBuildingPlaques(true);
    clearActiveVenue();
    syncBuildingPlaqueVisibility();
  }

  function showOriginCityChooser() {
    els.originPinHint.hidden = true;
    els.cityList.style.display = '';
    els.provinceChips.style.display = '';
    els.citySearch.parentElement.style.display = '';
    renderProvinces();
    renderCityList(els.cityList, els.citySearch.value, false);
  }

  function showDestCityChooser() {
    els.destPinHint.hidden = true;
    els.destList.style.display = '';
    els.destSearch.parentElement.style.display = '';
    renderCityList(els.destList, els.destSearch.value, true);
  }

  function reselectOrigin() {
    var prevCity = state.originCity;
    exitPicking();
    resetFrom(1);
    showOriginCityChooser();
    whenMapReady(function () { resetMapToCityView(prevCity); });
    syncSheetBack();
  }

  function reselectDestination() {
    var oCity = state.originCity;
    var oLl = state.originLatLng;
    var oZones = state.originZones;
    var oLabel = (els.originText && els.originText.textContent) || (oCity && oCity.name) || 'مبدأ';
    var prevDestCity = state.destCity;
    exitPicking();
    resetFrom(2);
    state.originCity = oCity;
    state.originLatLng = oLl;
    state.originZones = oZones;
    if (oLl) {
      placeOriginMarker(oLl);
      showBadge(els.originBadge, els.originText, oLabel, oLl);
    }
    goStep(2);
    showDestCityChooser();
    whenMapReady(function () { resetMapToCityView(prevDestCity || oCity); });
    syncSheetBack();
  }

  function goBack() {
    if (state.picking === 'origin') {
      var originCity = state.originCity;
      exitPicking();
      state.originCity = null;
      state.originLatLng = null;
      if (originMarker) { originMarker.remove(); originMarker = null; }
      els.originBadge.hidden = true;
      goStep(1);
      showOriginCityChooser();
      whenMapReady(function () { resetMapToCityView(originCity); });
      syncSheetBack();
      return;
    }
    if (state.picking === 'dest') {
      var destCity = state.destCity;
      exitPicking();
      state.destCity = null;
      state.destLatLng = null;
      goStep(2);
      showDestCityChooser();
      whenMapReady(function () { resetMapToCityView(destCity || state.originCity); });
      syncSheetBack();
      return;
    }
    if (state.step === 4) {
      state.tripPlanCode = null;
      if (els.continueBtn) els.continueBtn.disabled = true;
      if (els.selectedTrip) els.selectedTrip.hidden = true;
      goStep(3);
      syncSheetBack();
      return;
    }
    if (state.step === 3) {
      reselectDestination();
      return;
    }
    if (state.step === 2) {
      reselectOrigin();
    }
  }

  function activeConfirmBtn() {
    if (state.picking === 'origin') return els.confirmOrigin;
    if (state.picking === 'dest') return els.confirmDest;
    return null;
  }

  function setConfirmPinBusy(busy) {
    var btn = activeConfirmBtn();
    if (!btn) return;
    btn.disabled = !!busy;
    btn.classList.toggle('is-map-busy', !!busy);
    btn.setAttribute('aria-disabled', busy ? 'true' : 'false');
  }

  function syncSheetMode() {
    if (!els.sheet) return;
    var desktop = window.matchMedia('(min-width: 1024px)').matches;
    if (desktop) {
      els.sheet.classList.remove('is-collapsed', 'is-roomy', 'is-map-focus');
      els.sheet.classList.toggle('is-car-step', state.step === 3);
      state.sheetCollapsed = false;
      state.sheetPinMode = null;
      setConfirmPinBusy(false);
      return;
    }
    var pinOpen = !!state.picking ||
      (els.originPinHint && !els.originPinHint.hidden) ||
      (els.destPinHint && !els.destPinHint.hidden);
    var roomy = (pinOpen || state.step >= 3) && !state.sheetCollapsed && !state.sheetPinMode;
    els.sheet.classList.toggle('is-roomy', roomy);
    els.sheet.classList.toggle('is-car-step', state.step === 3 && !state.sheetCollapsed);
    els.sheet.classList.toggle('is-collapsed', !!state.sheetCollapsed && !state.sheetPinMode);
    els.sheet.classList.toggle('is-map-focus', state.sheetPinMode === 'map');

    if (els.sheetHandle) {
      var expanded = !state.sheetCollapsed && state.sheetPinMode !== 'map';
      els.sheetHandle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }
    if (els.sheetGrabHint) {
      if (state.sheetPinMode === 'map') {
        els.sheetGrabHint.textContent = 'نقشه را رها کنید تا تأیید فعال شود';
      } else if (state.sheetCollapsed) {
        els.sheetGrabHint.textContent = 'برای باز کردن پنل بالا بکشید';
      } else {
        els.sheetGrabHint.textContent = 'برای دیدن نقشه پایین بکشید';
      }
    }
  }

  function setSheetPinMode(mode) {
    if (window.matchMedia('(min-width: 1024px)').matches) return;
    if (!state.picking) {
      state.sheetPinMode = null;
      syncSheetMode();
      return;
    }
    state.sheetCollapsed = false;
    state.sheetPinMode = mode || null;
    syncSheetMode();
  }

  function scheduleConfirmPeek() {
    clearTimeout(scheduleConfirmPeek._t);
    scheduleConfirmPeek._t = setTimeout(function () {
      if (!state.picking) return;
      setSheetPinMode(null);
      setConfirmPinBusy(false);
    }, 520);
  }

  function onMapInteractStart() {
    if (state.picking) els.centerPin.classList.add('is-lifting');
    if (state.picking) {
      clearTimeout(scheduleConfirmPeek._t);
      setConfirmPinBusy(true);
      setSheetPinMode('map');
    }
  }

  function onMapInteractEnd() {
    els.centerPin.classList.remove('is-lifting');
    if (!state.picking) return;
    // Ignore moveend from our own road-snap fly — otherwise lock/pending fights itself
    // and later user pans stop snapping.
    if (_programmaticSnapMove) return;
    if (_roadSnapLock || _borderSnapLock) {
      _snapPending = true;
      return;
    }
    if (enforcePickInsideBorder()) {
      setConfirmPinBusy(true);
      return;
    }
    updateBoundVisual();
    scheduleSnapPickToRoute();
    scheduleVenueDetect();
    scheduleConfirmPeek();
  }

  function setSheetCollapsed(collapsed) {
    if (window.matchMedia('(min-width: 1024px)').matches) return;
    state.sheetCollapsed = !!collapsed;
    syncSheetMode();
    // Recenter route / pin after drawer size change
    clearTimeout(setSheetCollapsed._t);
    setSheetCollapsed._t = setTimeout(function () {
      if (state.routeCoords && state.routeCoords.length >= 2) arrangeMapToRoute(false);
      else if (map) {
        try { map.resize(); } catch (e) { /* ignore */ }
      }
    }, 300);
  }

  function bindSheetDrawer() {
    if (!els.sheetHandle || !els.sheet) return;
    var startY = 0;
    var dragging = false;
    var moved = false;

    els.sheetHandle.addEventListener('click', function (e) {
      if (moved) return;
      setSheetCollapsed(!state.sheetCollapsed);
    });

    function onPointerDown(e) {
      if (window.matchMedia('(min-width: 1024px)').matches) return;
      dragging = true;
      moved = false;
      startY = e.clientY || (e.touches && e.touches[0] && e.touches[0].clientY) || 0;
      els.sheetHandle.setPointerCapture && e.pointerId != null && els.sheetHandle.setPointerCapture(e.pointerId);
    }
    function onPointerMove(e) {
      if (!dragging) return;
      var y = e.clientY || (e.touches && e.touches[0] && e.touches[0].clientY) || startY;
      if (Math.abs(y - startY) > 10) moved = true;
    }
    function onPointerUp(e) {
      if (!dragging) return;
      dragging = false;
      var y = e.clientY || (e.changedTouches && e.changedTouches[0] && e.changedTouches[0].clientY) || startY;
      var dy = y - startY;
      if (Math.abs(dy) < 36) return; // treat as click
      if (dy > 36) setSheetCollapsed(true);
      else if (dy < -36) setSheetCollapsed(false);
    }

    els.sheetHandle.addEventListener('pointerdown', onPointerDown);
    els.sheetHandle.addEventListener('pointermove', onPointerMove);
    els.sheetHandle.addEventListener('pointerup', onPointerUp);
    els.sheetHandle.addEventListener('pointercancel', function () { dragging = false; });
  }

  function syncSheetBack() {
    syncSheetMode();
    if (!els.sheetBack) return;
    if (state.picking === 'origin') {
      els.sheetBack.hidden = false;
      if (els.sheetBackLabel) els.sheetBackLabel.textContent = 'تغییر مبدأ';
      return;
    }
    if (state.picking === 'dest') {
      els.sheetBack.hidden = false;
      if (els.sheetBackLabel) els.sheetBackLabel.textContent = 'تغییر مقصد';
      return;
    }
    if (state.step === 2) {
      els.sheetBack.hidden = false;
      if (els.sheetBackLabel) els.sheetBackLabel.textContent = 'تغییر مبدأ';
      return;
    }
    if (state.step === 3) {
      els.sheetBack.hidden = false;
      if (els.sheetBackLabel) els.sheetBackLabel.textContent = 'تغییر مقصد';
      return;
    }
    if (state.step === 4) {
      els.sheetBack.hidden = false;
      if (els.sheetBackLabel) els.sheetBackLabel.textContent = 'بازگشت';
      return;
    }
    els.sheetBack.hidden = true;
  }

  function getCityBorderFeature(city) {
    if (!city) return null;
    return (state.cityBorders && state.cityBorders[city.id]) || null;
  }

  function featureBbox(feature) {
    if (!feature || !feature.geometry) return null;
    var minLng = Infinity, minLat = Infinity, maxLng = -Infinity, maxLat = -Infinity;
    function walk(coords, depth) {
      if (!coords || !coords.length) return;
      if (typeof coords[0] === 'number') {
        var lng = coords[0], lat = coords[1];
        if (lng < minLng) minLng = lng;
        if (lng > maxLng) maxLng = lng;
        if (lat < minLat) minLat = lat;
        if (lat > maxLat) maxLat = lat;
        return;
      }
      for (var i = 0; i < coords.length; i++) walk(coords[i], depth + 1);
    }
    walk(feature.geometry.coordinates, 0);
    if (!isFinite(minLng)) return null;
    return [[minLng, minLat], [maxLng, maxLat]];
  }

  function fitToCityBorder(city) {
    resetMapToCityView(city, { duration: REDUCE_MOTION ? 0 : 700 });
  }

  /** North-up view centered on a selected city/province (smooth fly). */
  function resetMapToCityView(city, opts) {
    if (!map) return;
    opts = opts || {};
    var duration = REDUCE_MOTION ? 0 : (opts.duration || 850);
    var bearing = 0;
    var pitch = 0;

    if (city) {
      var feat = getCityBorderFeature(city);
      var bbox = featureBbox(feat);
      if (!bbox) {
        bbox = featureBbox(circlePolygon(city.lat, city.lng, (city.radiusKm || 15) * 1000));
      }
      if (bbox) {
        try {
          map.fitBounds(bbox, {
            padding: state.picking ? getPickMapPadding() : getMapUiPadding(),
            maxZoom: opts.maxZoom || 12.5,
            duration: duration,
            bearing: bearing,
            pitch: pitch,
            essential: true
          });
          return;
        } catch (e) { /* fall through */ }
      }
      map.easeTo({
        center: [city.lng, city.lat],
        zoom: opts.zoom || 12,
        bearing: bearing,
        pitch: pitch,
        duration: duration,
        essential: true
      });
      return;
    }

    map.easeTo({
      bearing: bearing,
      pitch: pitch,
      duration: duration,
      essential: true
    });
  }

  function pointInRing(lng, lat, ring) {
    // Ray casting; ring is [[lng,lat], ...]
    var inside = false;
    for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      var xi = ring[i][0], yi = ring[i][1];
      var xj = ring[j][0], yj = ring[j][1];
      var intersect = ((yi > lat) !== (yj > lat)) &&
        (lng < (xj - xi) * (lat - yi) / ((yj - yi) || 1e-12) + xi);
      if (intersect) inside = !inside;
    }
    return inside;
  }

  function pointInPolygonCoords(lng, lat, polygonCoords) {
    if (!polygonCoords || !polygonCoords.length) return false;
    if (!pointInRing(lng, lat, polygonCoords[0])) return false;
    // Holes
    for (var h = 1; h < polygonCoords.length; h++) {
      if (pointInRing(lng, lat, polygonCoords[h])) return false;
    }
    return true;
  }

  function pointInFeature(lng, lat, feature) {
    if (!feature || !feature.geometry) return false;
    var g = feature.geometry;
    if (g.type === 'Polygon') return pointInPolygonCoords(lng, lat, g.coordinates);
    if (g.type === 'MultiPolygon') {
      for (var i = 0; i < g.coordinates.length; i++) {
        if (pointInPolygonCoords(lng, lat, g.coordinates[i])) return true;
      }
      return false;
    }
    return false;
  }

  function updateBoundVisual() {
    if (state.activeVenue) return;
    var city = state.picking === 'origin' ? state.originCity : state.destCity;
    if (!city || !map || !mapReady) return;
    var feat = getCityBorderFeature(city);
    var data = feat || circlePolygon(city.lat, city.lng, (city.radiusKm || 15) * 1000);
    var color = state.picking === 'dest' ? DEST_COLOR : ORIGIN_COLOR;

    if (map.getSource('mb-city-bound')) {
      map.getSource('mb-city-bound').setData(data);
      if (map.getLayer('mb-city-bound-fill')) {
        map.setPaintProperty('mb-city-bound-fill', 'fill-color', color);
      }
      if (map.getLayer('mb-city-bound-line')) {
        map.setPaintProperty('mb-city-bound-line', 'line-color', color);
      }
    } else {
      map.addSource('mb-city-bound', { type: 'geojson', data: data });
      map.addLayer({
        id: 'mb-city-bound-fill',
        type: 'fill',
        source: 'mb-city-bound',
        paint: { 'fill-color': color, 'fill-opacity': 0.08 }
      });
      map.addLayer({
        id: 'mb-city-bound-line',
        type: 'line',
        source: 'mb-city-bound',
        paint: {
          'line-color': color,
          'line-width': 2.5,
          'line-opacity': 0.9
        }
      });
    }
  }

  function clearBoundVisual() {
    if (!map || !mapReady) return;
    if (map.getLayer('mb-city-bound-fill')) map.removeLayer('mb-city-bound-fill');
    if (map.getLayer('mb-city-bound-line')) map.removeLayer('mb-city-bound-line');
    if (map.getSource('mb-city-bound')) map.removeSource('mb-city-bound');
  }

  function enforcePickInsideBorder() {
    if (!state.picking || !map || _borderSnapLock) return false;
    var city = state.picking === 'origin' ? state.originCity : state.destCity;
    if (!city) return false;
    var ll = getPickLatLng();
    if (!ll) return false;
    if (insideBorder(ll, city)) return false;
    _borderSnapLock = true;
    pulseHint(state.picking === 'origin' ? els.originPinHint : els.destPinHint);
    flyPickToLatLng(city.lat, city.lng, Math.min(map.getZoom(), 13), true);
    setTimeout(function () {
      _borderSnapLock = false;
      if (_snapPending && state.picking) {
        _snapPending = false;
        scheduleSnapPickToRoute();
      }
    }, 520);
    return true;
  }

  function clearSheetPinUi() {
    clearTimeout(scheduleConfirmPeek._t);
    state.sheetPinMode = null;
    state.sheetCollapsed = false;
    setConfirmPinBusy(false);
    if (els.sheet) {
      els.sheet.classList.remove('is-map-focus', 'is-collapsed');
    }
  }

  function confirmOriginPin() {
    if (!map || !state.originCity) return;
    var ll = getPickLatLng();
    if (!ll) return;
    if (!insideBorder(ll, state.originCity)) {
      pulseHint(els.originPinHint);
      flyPickToLatLng(state.originCity.lat, state.originCity.lng, map.getZoom(), true);
      return;
    }
    var label = (els.originPlaceSearch && els.originPlaceSearch.value.trim())
      || (els.originAddress && els.originAddress.querySelector('strong') && els.originAddress.querySelector('strong').textContent.trim())
      || state.originCity.name;

    state.originLatLng = ll;
    clearSheetPinUi();
    exitPicking();
    if (els.originPinHint) els.originPinHint.hidden = true;
    clearBoundVisual();

    placeOriginMarker(ll);
    showBadge(els.originBadge, els.originText, label, ll);
    state.originLabel = label;
    goStep(2);
    showDestCityChooser();

    // Deep-link: auto-open destination city after origin confirm
    if (state.pendingDestName) {
      var pending = state.pendingDestName;
      state.pendingDestName = null;
      var destCity = findCityByName(pending);
      if (destCity) {
        setTimeout(function () { selectDestCity(destCity); }, 120);
      }
    }

    // Force destination drawer content visible (mobile peek CSS can linger)
    if (els.destList) els.destList.style.display = '';
    if (els.destSearch && els.destSearch.parentElement) els.destSearch.parentElement.style.display = '';
    if (els.destPinHint) els.destPinHint.hidden = true;
    syncSheetBack();
    if (els.sheet) {
      try { els.sheet.scrollTop = 0; } catch (e) { /* ignore */ }
    }
  }

  function confirmDestPin() {
    if (!map || !state.destCity) return;
    var ll = getPickLatLng();
    if (!ll) return;
    if (!insideBorder(ll, state.destCity)) {
      pulseHint(els.destPinHint);
      flyPickToLatLng(state.destCity.lat, state.destCity.lng, map.getZoom(), true);
      return;
    }
    var label = (els.destPlaceSearch && els.destPlaceSearch.value.trim())
      || (els.destAddress && els.destAddress.querySelector('strong') && els.destAddress.querySelector('strong').textContent.trim())
      || state.destCity.name;

    state.destLatLng = ll;
    clearSheetPinUi();
    exitPicking();
    if (els.destPinHint) els.destPinHint.hidden = true;
    clearBoundVisual();

    placeDestMarker(ll);
    showBadge(els.destBadge, els.destText, label, ll);
    state.destLabel = label;
    drawRouteAndAnimate().then(function () {
      clearSheetPinUi();
      goStep(3);
      loadCarClassPrices();
      syncSheetBack();
    });
  }

  function insideBorder(ll, city) {
    if (!city || !ll) return false;
    var feat = getCityBorderFeature(city);
    if (feat) return pointInFeature(ll.lng, ll.lat, feat);
    // Fallback only if polygon missing
    var d = mapDistanceMeters(ll, { lat: city.lat, lng: city.lng });
    return d <= (city.radiusKm || 15) * 1000;
  }

  function pulseHint(el) {
    if (!el) return;
    el.style.outline = '2px solid #b42318';
    setTimeout(function () { el.style.outline = ''; }, 600);
  }

  function circlePolygon(lat, lng, radiusM, steps) {
    steps = steps || 64;
    var coords = [];
    var latRad = lat * Math.PI / 180;
    for (var i = 0; i <= steps; i++) {
      var bearing = (i / steps) * 2 * Math.PI;
      var dLat = (radiusM / 6371000) * Math.cos(bearing);
      var dLng = (radiusM / 6371000) * Math.sin(bearing) / Math.cos(latRad);
      coords.push([lng + dLng * 180 / Math.PI, lat + dLat * 180 / Math.PI]);
    }
    return {
      type: 'Feature',
      properties: {},
      geometry: { type: 'Polygon', coordinates: [coords] }
    };
  }

  /* ---------- Markers / badges ---------- */

  function makePinMarker(ll, kind) {
    var el = document.createElement('div');
    el.className = 'mapbook__pin-marker mapbook__pin-marker--' + kind;
    el.innerHTML = '<span class="mapbook__pin-marker-head" aria-hidden="true"></span>';
    return new nmp.Marker({ element: el, anchor: 'bottom' })
      .setLngLat([ll.lng, ll.lat])
      .addTo(map);
  }

  function placeOriginMarker(ll) {
    if (originMarker) originMarker.remove();
    originMarker = makePinMarker(ll, 'origin');
  }

  function placeDestMarker(ll) {
    if (destMarker) destMarker.remove();
    destMarker = makePinMarker(ll, 'dest');
  }

  function showBadge(badge, textEl, name, ll) {
    if (textEl) textEl.textContent = name;
    if (!badge || state.step >= 3) {
      if (badge) badge.hidden = true;
      return;
    }
    badge.hidden = false;
    positionOverlay(badge, ll);
  }

  function hideMapFloatBadges() {
    if (els.originBadge) els.originBadge.hidden = true;
    if (els.destBadge) els.destBadge.hidden = true;
  }

  function positionRouteEta() {
    if (!map || !els.eta || els.eta.hidden || !state.routeCoords || state.routeCoords.length < 2) return;
    var midIdx = Math.floor(state.routeCoords.length / 2);
    var c = state.routeCoords[midIdx];
    var pt = map.project([c[1], c[0]]);
    var mapEl = document.getElementById('mapBookMap');
    var w = (mapEl && mapEl.clientWidth) || window.innerWidth || 400;
    var h = (mapEl && mapEl.clientHeight) || window.innerHeight || 600;
    var pad = getRoutePreviewPadding();
    var x = Math.max(pad.left + 8, Math.min(w - pad.right - 8, pt.x));
    var y = Math.max(pad.top + 8, Math.min(h - pad.bottom - 8, pt.y));
    els.eta.style.left = x + 'px';
    els.eta.style.top = y + 'px';
  }

  function showRouteEta(durationSec) {
    if (!els.eta || durationSec == null || !isFinite(durationSec)) return;
    state.routeDurationSec = durationSec;
    els.eta.hidden = false;
    els.eta.textContent = formatDuration(durationSec);
    positionRouteEta();
  }

  function positionOverlay(el, ll) {
    if (!map || !ll) return;
    var pt = map.project([ll.lng, ll.lat]);
    el.style.left = pt.x + 'px';
    el.style.top = pt.y + 'px';
  }

  var _overlaySyncRaf = 0;
  var _routeRedrawRaf = 0;
  var _routeRedrawTimer = 0;
  var _lastRouteRedrawAt = 0;

  function scheduleOverlaySync(redrawRoute) {
    if (_overlaySyncRaf) return;
    _overlaySyncRaf = requestAnimationFrame(function () {
      _overlaySyncRaf = 0;
      syncOverlays();
      if (redrawRoute !== false && routeCanvas) routeCanvas.redraw();
    });
  }

  function scheduleRouteRedraw() {
    var now = Date.now();
    var wait = Math.max(0, 90 - (now - _lastRouteRedrawAt));
    clearTimeout(_routeRedrawTimer);
    _routeRedrawTimer = setTimeout(function () {
      _routeRedrawTimer = 0;
      _lastRouteRedrawAt = Date.now();
      positionRouteEta();
      if (routeCanvas) routeCanvas.redraw();
    }, wait);
  }

  function syncOverlays() {
    if (!map) return;
    if (state.step < 3 && state.originLatLng) positionOverlay(els.originBadge, state.originLatLng);
    if (state.step < 3 && state.destLatLng) positionOverlay(els.destBadge, state.destLatLng);
    if (!els.eta.hidden) positionRouteEta();
    syncBuildingPlaqueDomPositions();
  }

  /* ---------- Route + Snapp-style glow ---------- */

  function coordsToRouteGeoJson(coords) {
    return {
      type: 'Feature',
      properties: {},
      geometry: {
        type: 'LineString',
        coordinates: (coords || []).map(function (c) { return [c[1], c[0]]; })
      }
    };
  }

  function ensureMapRouteLayer() {
    if (!map || !mapReady) return false;
    if (map.getSource('mb-route')) {
      state.nativeRouteLayer = true;
      return true;
    }
    try {
      map.addSource('mb-route', {
        type: 'geojson',
        lineMetrics: true,
        data: { type: 'FeatureCollection', features: [] }
      });
      map.addLayer({
        id: 'mb-route-glow',
        type: 'line',
        source: 'mb-route',
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: {
          'line-color': ORIGIN_COLOR,
          'line-width': 14,
          'line-opacity': 0.22
        }
      });
      map.addLayer({
        id: 'mb-route-line',
        type: 'line',
        source: 'mb-route',
        layout: { 'line-cap': 'round', 'line-join': 'round' },
        paint: {
          'line-color': ORIGIN_COLOR,
          'line-width': 7,
          'line-opacity': 0.95
        }
      });
      state.nativeRouteLayer = true;
      return true;
    } catch (e) {
      console.warn('ensureMapRouteLayer', e);
      state.nativeRouteLayer = false;
      return false;
    }
  }

  function setMapRoutePath(coords) {
    ensureMapRouteLayer();
    if (!map || !map.getSource('mb-route')) return;
    try {
      map.getSource('mb-route').setData(coordsToRouteGeoJson(coords || []));
    } catch (e) {
      console.warn('setMapRoutePath', e);
    }
  }

  function clearMapRouteLayer() {
    state.nativeRouteLayer = false;
    if (!map || !mapReady) return;
    try {
      if (map.getLayer('mb-route-line')) map.removeLayer('mb-route-line');
      if (map.getLayer('mb-route-glow')) map.removeLayer('mb-route-glow');
      if (map.getSource('mb-route')) map.removeSource('mb-route');
    } catch (e) { /* ignore */ }
  }

  function whenMapSettled(cb) {
    if (!map || typeof cb !== 'function') return;
    var done = false;
    var finish = function () {
      if (done) return;
      done = true;
      cb();
    };
    if (typeof map.once === 'function') map.once('idle', finish);
    setTimeout(finish, REDUCE_MOTION ? 80 : 900);
  }

  async function drawRouteAndAnimate() {
    var o = state.originLatLng;
    var d = state.destLatLng;
    var coords = null;
    var durationSec = null;
    var source = 'fallback';

    _routeApiCooldownUntil = 0;
    updateMapBounds();

    var fetched = await fetchRouteGeometry(o, d);
    if (fetched && routePayloadIsReal(fetched, o, d)) {
      coords = fetched.path;
      source = fetched.source || 'osrm';
      if (fetched.duration != null) durationSec = Number(fetched.duration);
    }

    if (!coords || coords.length < 2) {
      console.warn('MapBook: road route unavailable, using fallback curve');
      coords = fallbackCurve(o, d);
      source = 'fallback';
      durationSec = mapDistanceMeters(o, d) / 22;
    }

    state.routeSource = source;
    if (source === 'neshan' || source === 'osrm') {
      state.routeCoords = densifyPath(coords, Math.min(420, Math.max(coords.length, 100)));
    } else {
      state.routeCoords = smoothLatLngPath(densifyPath(coords, 120), 1);
    }

    snapMarkersToRoute();
    hideMapFloatBadges();
    if (els.root) els.root.classList.add('is-route-preview');

    setMapRoutePath(state.routeCoords);
    routeCanvas.setPath(state.routeCoords);
    routeCanvas.stop();
    routeCanvas._reveal = 1;
    routeCanvas.redraw();

    renderRouteZoneSummary();
    showRouteEta(durationSec);
    autoArrangeRouteCamera();

    whenMapSettled(function () {
      if (!state.routeCoords || state.routeCoords.length < 2) return;
      setMapRoutePath(state.routeCoords);
      routeCanvas.setPath(state.routeCoords);
      routeCanvas.play();
      routeCanvas.redraw();
    });
  }

  function densifyPath(latlngs, targetCount) {
    if (!latlngs || latlngs.length < 2) return latlngs || [];
    var out = [];
    var segs = latlngs.length - 1;
    var per = Math.max(1, Math.ceil(targetCount / segs));
    for (var i = 0; i < segs; i++) {
      var a = latlngs[i];
      var b = latlngs[i + 1];
      for (var j = 0; j < per; j++) {
        var t = j / per;
        out.push([
          a[0] + (b[0] - a[0]) * t,
          a[1] + (b[1] - a[1]) * t
        ]);
      }
    }
    out.push(latlngs[latlngs.length - 1]);
    return out;
  }

  /** Chaikin corner-cutting — softens sharp polyline elbows */
  function smoothLatLngPath(latlngs, iterations) {
    if (!latlngs || latlngs.length < 3) return latlngs || [];
    var pts = latlngs.slice();
    var it = iterations || 2;
    for (var n = 0; n < it; n++) {
      var next = [pts[0]];
      for (var i = 0; i < pts.length - 1; i++) {
        var p0 = pts[i];
        var p1 = pts[i + 1];
        next.push([
          0.75 * p0[0] + 0.25 * p1[0],
          0.75 * p0[1] + 0.25 * p1[1]
        ]);
        next.push([
          0.25 * p0[0] + 0.75 * p1[0],
          0.25 * p0[1] + 0.75 * p1[1]
        ]);
      }
      next.push(pts[pts.length - 1]);
      pts = next;
    }
    return pts;
  }

  function fallbackCurve(o, d) {
    var midLat = (o.lat + d.lat) / 2;
    var midLng = (o.lng + d.lng) / 2;
    var dx = d.lng - o.lng;
    var dy = d.lat - o.lat;
    var cLat = midLat + dx * 0.12;
    var cLng = midLng - dy * 0.12;
    var pts = [];
    for (var i = 0; i <= 48; i++) {
      var t = i / 48;
      var u = 1 - t;
      pts.push([
        u * u * o.lat + 2 * u * t * cLat + t * t * d.lat,
        u * u * o.lng + 2 * u * t * cLng + t * t * d.lng
      ]);
    }
    return pts;
  }

  /** Canvas overlay: bold blue→green route (Mapbox-compatible) */
  function createRouteGlowOverlay(container) {
    var canvas = document.createElement('canvas');
    canvas.className = 'mapbook-route-canvas';
    canvas.style.position = 'absolute';
    canvas.style.inset = '0';
    canvas.style.width = '100%';
    canvas.style.height = '100%';
    canvas.style.pointerEvents = 'none';
    canvas.style.zIndex = '2';
    container.appendChild(canvas);

    var api = {
      _canvas: canvas,
      _ctx: canvas.getContext('2d', { alpha: true }),
      _path: [],
      _phase: 0,
      _reveal: 0,
      _raf: null,
      _mode: 'reveal',
      setPath: function (latlngs) {
        this._path = latlngs || [];
        this._phase = 0;
        this._reveal = 0;
        this._mode = 'reveal';
        this.redraw();
      },
      play: function () {
        this.stop();
        if (REDUCE_MOTION) {
          this._reveal = 1;
          this._phase = 0.5;
          this._mode = 'idle';
          this.redraw();
          return;
        }
        var self = this;
        var start = performance.now();
        var revealMs = 1100;
        function easeOutCubic(t) { return 1 - Math.pow(1 - t, 3); }
        function tick(now) {
          var rt = Math.min(1, (now - start) / revealMs);
          self._reveal = easeOutCubic(rt);
          self._phase = self._reveal;
          self.redraw();
          if (rt < 1) {
            self._raf = requestAnimationFrame(tick);
          } else {
            self._reveal = 1;
            self._mode = 'wave';
            self._waveStart = performance.now();
            self._raf = requestAnimationFrame(waveTick);
          }
        }
        function waveTick(now) {
          if (self._mode !== 'wave') return;
          self._phase = ((now - self._waveStart) / 5600) % 1;
          self.redraw();
          self._raf = requestAnimationFrame(waveTick);
        }
        this._raf = requestAnimationFrame(tick);
      },
      stop: function () {
        if (this._raf) cancelAnimationFrame(this._raf);
        this._raf = null;
        this._mode = 'idle';
      },
      resize: function () { this.redraw(); },
      redraw: function () {
        if (!map) return;
        var rect = container.getBoundingClientRect();
        var size = { x: rect.width, y: rect.height };
        if (size.x < 2 || size.y < 2) return;
        var dpr = Math.min(window.devicePixelRatio || 1, 1.5);
        var tw = (size.x * dpr) | 0;
        var th = (size.y * dpr) | 0;
        if (this._canvas.width !== tw || this._canvas.height !== th) {
          this._canvas.width = tw;
          this._canvas.height = th;
        }
        var ctx = this._ctx;
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, size.x, size.y);
        var pts = this._path;
        if (!pts || pts.length < 2) return;

        var screen = new Array(pts.length);
        for (var si = 0; si < pts.length; si++) {
          var p = map.project([pts[si][1], pts[si][0]]);
          screen[si] = { x: p.x, y: p.y };
        }

        var revealCount = Math.max(2, Math.floor(screen.length * Math.max(0.02, this._reveal)));
        var visible = screen.length === revealCount ? screen : screen.slice(0, revealCount);

        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';

        ctx.strokeStyle = hexAlpha(ORIGIN_COLOR, 0.22);
        ctx.lineWidth = 14;
        strokePoly(ctx, visible);

        var chunks = 12;
        var chunk = Math.max(3, Math.floor(visible.length / chunks));
        for (var i = 0; i < visible.length - 1; i += chunk) {
          var end = Math.min(visible.length, i + chunk + 1);
          var slice = visible.slice(i, end);
          if (slice.length < 2) continue;
          var prog = Math.min(1, Math.max(0, i / Math.max(1, visible.length - 1)));
          ctx.strokeStyle = lerpColor(ORIGIN_COLOR, DEST_COLOR, prog);
          ctx.globalAlpha = 0.92;
          ctx.lineWidth = 7;
          strokePoly(ctx, slice);
        }
        ctx.globalAlpha = 1;

        ctx.strokeStyle = 'rgba(255,255,255,0.45)';
        ctx.lineWidth = 2;
        strokePoly(ctx, visible);

        if (this._mode === 'wave' && visible.length >= 2) {
          drawRouteWave(ctx, visible, this._phase);
        }
      }
    };
    api.resize();
    return api;
  }

  function drawRouteWave(ctx, pts, phase) {
    var lengths = [0];
    var total = 0;
    for (var i = 1; i < pts.length; i++) {
      var dx = pts[i].x - pts[i - 1].x;
      var dy = pts[i].y - pts[i - 1].y;
      total += Math.sqrt(dx * dx + dy * dy);
      lengths.push(total);
    }
    if (total < 1) return;

    var waveLen = total * 0.12;
    var head = phase * (total + waveLen);
    var tail = head - waveLen;
    var pulse = 0.55 + 0.45 * Math.sin(phase * Math.PI * 2);

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    for (var j = 1; j < pts.length; j++) {
      var segStart = lengths[j - 1];
      var segEnd = lengths[j];
      var overlapStart = Math.max(segStart, tail);
      var overlapEnd = Math.min(segEnd, head);
      if (overlapEnd <= overlapStart) continue;

      var span = segEnd - segStart || 1;
      var t0 = (overlapStart - segStart) / span;
      var t1 = (overlapEnd - segStart) / span;
      var x0 = pts[j - 1].x + (pts[j].x - pts[j - 1].x) * t0;
      var y0 = pts[j - 1].y + (pts[j].y - pts[j - 1].y) * t0;
      var x1 = pts[j - 1].x + (pts[j].x - pts[j - 1].x) * t1;
      var y1 = pts[j - 1].y + (pts[j].y - pts[j - 1].y) * t1;
      var midProg = (overlapStart + overlapEnd) * 0.5 / total;
      var local = (overlapStart + overlapEnd) * 0.5;
      var alongWave = (local - tail) / (waveLen || 1);
      var soft = Math.sin(Math.max(0, Math.min(1, alongWave)) * Math.PI);

      ctx.strokeStyle = lerpColor(ORIGIN_COLOR, DEST_COLOR, midProg);
      ctx.globalAlpha = (0.12 + soft * 0.28) * pulse;
      ctx.lineWidth = 8;
      ctx.beginPath();
      ctx.moveTo(x0, y0);
      ctx.lineTo(x1, y1);
      ctx.stroke();

      ctx.strokeStyle = 'rgba(255,255,255,0.7)';
      ctx.globalAlpha = (0.1 + soft * 0.22) * pulse;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(x0, y0);
      ctx.lineTo(x1, y1);
      ctx.stroke();
    }
    ctx.restore();
  }

  function strokePoly(ctx, pts) {
    if (!pts.length) return;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (var i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
    ctx.stroke();
  }

  /** Midpoint quadratic chain — soft corners instead of sharp elbows */
  function strokeSmooth(ctx, pts) {
    if (!pts || pts.length < 2) return;
    if (pts.length === 2) {
      ctx.beginPath();
      ctx.moveTo(pts[0].x, pts[0].y);
      ctx.lineTo(pts[1].x, pts[1].y);
      ctx.stroke();
      return;
    }
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (var i = 1; i < pts.length - 1; i++) {
      var xc = (pts[i].x + pts[i + 1].x) / 2;
      var yc = (pts[i].y + pts[i + 1].y) / 2;
      ctx.quadraticCurveTo(pts[i].x, pts[i].y, xc, yc);
    }
    var last = pts[pts.length - 1];
    var prev = pts[pts.length - 2];
    ctx.quadraticCurveTo(prev.x, prev.y, last.x, last.y);
    ctx.stroke();
  }

  /* ---------- Car class prices (shown after class pick, not on trip times) ---------- */

  var TRIP_CACHE_TTL_MS = 10 * 60 * 1000;
  var tripMemCache = Object.create(null);
  var tripInflight = Object.create(null);

  function tripsCacheKey(originName, destName, jalaliDate) {
    return String(originName) + '|' + String(destName) + '|' + String(jalaliDate);
  }

  function readTripCache(key) {
    var now = Date.now();
    var mem = tripMemCache[key];
    if (mem && now - mem.at < TRIP_CACHE_TTL_MS && Array.isArray(mem.data)) {
      return mem.data;
    }
    try {
      var raw = sessionStorage.getItem('mbTrips:' + key);
      if (!raw) return null;
      var parsed = JSON.parse(raw);
      if (!parsed || !Array.isArray(parsed.data) || now - parsed.at >= TRIP_CACHE_TTL_MS) return null;
      tripMemCache[key] = { at: parsed.at, data: parsed.data };
      return parsed.data;
    } catch (e) {
      return null;
    }
  }

  function writeTripCache(key, data) {
    var entry = { at: Date.now(), data: data };
    tripMemCache[key] = entry;
    try {
      sessionStorage.setItem('mbTrips:' + key, JSON.stringify(entry));
    } catch (e) { /* quota */ }
  }

  function normalizeTripsPayload(trips) {
    if (Array.isArray(trips)) return trips;
    if (trips && Array.isArray(trips.trips)) return trips.trips;
    if (trips && Array.isArray(trips.items)) return trips.items;
    return [];
  }

  async function fetchTripsCached(originName, destName, jalaliDate) {
    var key = tripsCacheKey(originName, destName, jalaliDate);
    var cached = readTripCache(key);
    if (cached) return { trips: cached, fromCache: true, ok: true };

    if (tripInflight[key]) return tripInflight[key];

    tripInflight[key] = (async function () {
      var url = '/TaxiTrips/SearchJson?originstring=' + encodeURIComponent(originName) +
        '&destinationstring=' + encodeURIComponent(destName) +
        '&searchdate=' + encodeURIComponent(jalaliDate);
      var res = await fetch(url, { credentials: 'same-origin' });
      var body = await res.json();
      if (!res.ok) {
        var err = (body && body.error) ? body.error : 'برای این مسیر برنامه‌ای یافت نشد.';
        return { trips: [], fromCache: false, ok: false, error: err };
      }
      var trips = normalizeTripsPayload(body);
      writeTripCache(key, trips);
      return { trips: trips, fromCache: false, ok: true };
    })();

    try {
      return await tripInflight[key];
    } finally {
      delete tripInflight[key];
    }
  }

  function prefetchTripDates(originName, destName, startOffset, count) {
    var JD = window.JDate || null;
    for (var i = startOffset; i < startOffset + count; i++) {
      var day = jalaliDayOffset(i, JD);
      fetchTripsCached(originName, destName, day.key).catch(function () { /* ignore */ });
    }
  }

  async function loadCarClassPrices() {
    document.querySelectorAll('.mapbook__car-price').forEach(function (el) {
      el.hidden = true;
      el.textContent = '';
    });
    if (!state.originCity || !state.destCity) return;

    var todayKey = jalaliDayOffset(0, window.JDate || null).key;
    try {
      var result = await fetchTripsCached(state.originCity.name, state.destCity.name, todayKey);
      if (!result.ok || !result.trips.length) return;
      var trips = result.trips;

      var mins = { eco: null, vip: null, tashrifat: null };
      ['eco', 'vip', 'tashrifat'].forEach(function (cls) {
        var list = filterByCarClass(trips, cls);
        list.forEach(function (t) {
          var n = parsePrice(t.afterdiscount || t.originalPrice);
          if (n > 0 && (mins[cls] == null || n < mins[cls].n)) {
            mins[cls] = { n: n, label: t.afterdiscount || t.originalPrice };
          }
        });
      });
      state.classPrices = mins;

      Object.keys(mins).forEach(function (cls) {
        var el = document.querySelector('.mapbook__car-price[data-price-for="' + cls + '"]');
        if (!el) return;
        if (mins[cls]) {
          el.hidden = false;
          el.textContent = 'از ' + toFaDigits(mins[cls].label) + ' ت';
        } else {
          el.hidden = true;
        }
      });

      // Warm nearby dates while user picks a car class
      prefetchTripDates(state.originCity.name, state.destCity.name, 0, 5);
    } catch (e) {
      /* prices stay hidden */
    }
  }

  function updateCarPriceHint(cls) {
    /* price lives on the card bottom bar */
  }

  /* ---------- Date strip + trip plan scroller → reservation ---------- */

  var MONTHS_FA = [
    'فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور',
    'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'
  ];
  var WEEKDAYS_FA = ['یکشنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنجشنبه', 'جمعه', 'شنبه'];

  function buildDateStrip() {
    if (!els.dateStrip) return;
    els.dateStrip.innerHTML = '';
    state.tripPlanCode = null;
    els.continueBtn.disabled = true;
    hideSelectedTrip();

    var JD = window.JDate || null;
    var days = [];
    for (var i = 0; i < 14; i++) {
      days.push(jalaliDayOffset(i, JD));
    }

    days.forEach(function (day, idx) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'mapbook__date-chip' + (idx === 0 ? ' is-active' : '');
      btn.setAttribute('role', 'option');
      btn.setAttribute('aria-selected', idx === 0 ? 'true' : 'false');
      btn.dataset.date = day.key;
      btn.innerHTML =
        '<span class="mapbook__date-chip-wd">' + escapeHtml(day.weekday) + '</span>' +
        '<span class="mapbook__date-chip-d">' + toFaDigits(day.day) + '</span>' +
        '<span class="mapbook__date-chip-m">' + escapeHtml(day.monthName) + '</span>';
      btn.addEventListener('click', function () {
        els.dateStrip.querySelectorAll('.mapbook__date-chip').forEach(function (c) {
          c.classList.remove('is-active');
          c.setAttribute('aria-selected', 'false');
        });
        btn.classList.add('is-active');
        btn.setAttribute('aria-selected', 'true');
        state.dateJalali = day.key;
        state.tripPlanCode = null;
        els.continueBtn.disabled = true;
        hideSelectedTrip();
        loadTripSlots(day.key);
      });
      els.dateStrip.appendChild(btn);
    });

    state.dateJalali = days[0].key;
    loadTripSlots(days[0].key);
  }

  function jalaliDayOffset(offset, JD) {
    if (JD) {
      var j = new JD();
      // Advance by Gregorian days then re-read Jalali — more reliable across month ends
      var g = new Date();
      g.setHours(12, 0, 0, 0);
      g.setDate(g.getDate() + offset);
      j = new JD(g);
      var y = j.getFullYear();
      var m = j.getMonth(); // 0-based
      var d = j.getDate();
      var wd = WEEKDAYS_FA[g.getDay()];
      return {
        key: y + '/' + pad(m + 1) + '/' + pad(d),
        day: d,
        monthName: MONTHS_FA[m] || '',
        weekday: wd
      };
    }
    // Fallback: show gregorian label if JDate missing
    var g2 = new Date();
    g2.setDate(g2.getDate() + offset);
    return {
      key: g2.getFullYear() + '/' + pad(g2.getMonth() + 1) + '/' + pad(g2.getDate()),
      day: g2.getDate(),
      monthName: '',
      weekday: WEEKDAYS_FA[g2.getDay()]
    };
  }

  async function loadTripSlots(jalaliDate) {
    if (!state.originCity || !state.destCity) return;

    var cacheKey = tripsCacheKey(state.originCity.name, state.destCity.name, jalaliDate);
    var cached = readTripCache(cacheKey);
    if (!cached) {
      els.timeSlots.innerHTML = '<p class="mapbook__empty">در حال یافتن برنامه‌های سفر…</p>';
    }

    try {
      var result = await fetchTripsCached(state.originCity.name, state.destCity.name, jalaliDate);
      if (!result.ok) {
        els.timeSlots.innerHTML = '<p class="mapbook__empty is-error">' + escapeHtml(result.error || 'برای این مسیر برنامه‌ای یافت نشد.') + '</p>';
        return;
      }

      var trips = result.trips;
      var filtered = filterByCarClass(trips, state.carClass);
      // Keep class filter soft: if empty, show all plans for the OD/date
      if (!filtered.length) filtered = trips;

      // Sort by departure time — 00:00 sorts last (end of day)
      filtered.sort(function (a, b) {
        return timeSortKey(extractTime(a)) - timeSortKey(extractTime(b));
      });

      if (!filtered.length) {
        els.timeSlots.innerHTML = '<p class="mapbook__empty">برای این تاریخ برنامه‌ای فعال نیست. روز دیگری را امتحان کنید.</p>';
        return;
      }

      els.timeSlots.innerHTML = '';
      // Unique times only (one chip per departure clock) — keep cheapest code for that time+class
      var byTime = {};
      filtered.forEach(function (t) {
        var code = t.tripcode || t.tripPlanCode || t.TripPlanCode;
        if (!code) return;
        var time = extractTime(t);
        var price = t.afterdiscount || t.originalPrice || '';
        var priceNum = parsePrice(price);
        if (!byTime[time] || (priceNum > 0 && priceNum < byTime[time].priceNum)) {
          byTime[time] = {
            code: code,
            time: time,
            price: price,
            priceNum: priceNum || Number.MAX_SAFE_INTEGER,
            car: (t.carModelName || '').split('|')[0].trim()
          };
        }
      });

      Object.keys(byTime)
        .sort(function (a, b) { return timeSortKey(a) - timeSortKey(b); })
        .forEach(function (time) {
          var trip = byTime[time];
          var btn = document.createElement('button');
          btn.type = 'button';
          btn.className = 'mapbook__trip-time-chip';
          btn.setAttribute('role', 'option');
          btn.setAttribute('aria-selected', 'false');
          btn.dataset.tripcode = trip.code;
          btn.textContent = toFaDigits(trip.time);
          btn.addEventListener('click', function () {
            selectTripPlan(btn, trip);
          });
          els.timeSlots.appendChild(btn);
        });

      if (!els.timeSlots.children.length) {
        els.timeSlots.innerHTML = '<p class="mapbook__empty">برنامه‌ای با کد رزرو معتبر یافت نشد.</p>';
      }

      // Prefetch neighboring days for snappy date switching
      var JD = window.JDate || null;
      var days = [];
      for (var di = 0; di < 14; di++) days.push(jalaliDayOffset(di, JD).key);
      var idx = days.indexOf(jalaliDate);
      if (idx >= 0) {
        [idx + 1, idx + 2, idx - 1].forEach(function (ni) {
          if (ni >= 0 && ni < days.length) {
            fetchTripsCached(state.originCity.name, state.destCity.name, days[ni]).catch(function () {});
          }
        });
      }
    } catch (e) {
      console.warn(e);
      els.timeSlots.innerHTML =
        '<p class="mapbook__empty is-error">خطا در دریافت برنامه‌ها. اتصال را بررسی کنید و دوباره تلاش کنید.</p>';
    }
  }

  function parsePrice(s) {
    if (!s) return 0;
    var n = String(s).replace(/[^\d]/g, '');
    return n ? parseInt(n, 10) : 0;
  }

  function selectTripPlan(btn, trip) {
    els.timeSlots.querySelectorAll('.mapbook__trip-time-chip').forEach(function (c) {
      c.classList.remove('is-active');
      c.setAttribute('aria-selected', 'false');
    });
    btn.classList.add('is-active');
    btn.setAttribute('aria-selected', 'true');
    state.tripPlanCode = trip.code;
    els.continueBtn.disabled = !trip.code;

    if (els.selectedTrip) {
      els.selectedTrip.hidden = false;
      if (els.selectedTime) els.selectedTime.textContent = toFaDigits(trip.time);
      if (els.selectedMeta) {
        els.selectedMeta.textContent = carClassLabel(state.carClass);
      }
      if (els.selectedPrice) {
        els.selectedPrice.textContent = trip.price ? (toFaDigits(trip.price) + ' تومان') : '';
      }
    }

    btn.scrollIntoView({ inline: 'center', block: 'nearest', behavior: REDUCE_MOTION ? 'auto' : 'smooth' });
  }

  function carClassLabel(id) {
    return ({
      eco: 'اکونومی',
      vip: 'VIP',
      van: 'ون',
      tashrifat: 'تشریفات'
    })[id] || '';
  }

  function hideSelectedTrip() {
    if (els.selectedTrip) els.selectedTrip.hidden = true;
  }

  function goToReservation() {
    if (!state.tripPlanCode) return;
    els.continueBtn.disabled = true;
    els.continueBtn.textContent = 'در حال انتقال…';

    var oLabel = state.originLabel
      || (els.originText && els.originText.textContent)
      || (state.originCity && state.originCity.name)
      || '';
    var dLabel = state.destLabel
      || (els.destText && els.destText.textContent)
      || (state.destCity && state.destCity.name)
      || '';
    var handoff = {
      oLabel: oLabel,
      dLabel: dLabel,
      oLat: state.originLatLng && state.originLatLng.lat,
      oLng: state.originLatLng && state.originLatLng.lng,
      dLat: state.destLatLng && state.destLatLng.lat,
      dLng: state.destLatLng && state.destLatLng.lng,
      tripcode: state.tripPlanCode,
      at: Date.now()
    };
    try { sessionStorage.setItem('mb:handoff', JSON.stringify(handoff)); } catch (e) { /* ignore */ }

    var q = new URLSearchParams();
    q.set('tripcode', state.tripPlanCode);
    if (oLabel) q.set('olabel', oLabel);
    if (dLabel) q.set('dlabel', dLabel);
    if (handoff.oLat != null) q.set('olat', String(handoff.oLat));
    if (handoff.oLng != null) q.set('olng', String(handoff.oLng));
    if (handoff.dLat != null) q.set('dlat', String(handoff.dLat));
    if (handoff.dLng != null) q.set('dlng', String(handoff.dLng));
    window.location.href = '/Reserve/Reservetrip?' + q.toString();
  }

  function normalizeFa(s) {
    return String(s || '')
      .replace(/\u064A/g, '\u06CC')
      .replace(/\u0643/g, '\u06A9')
      .toLowerCase();
  }

  /**
   * ORS supervisor mapping (confirmed from live SearchJson):
   *  6 = اکو,  8 = VIP,  7 = تشریفات
   */
  function classifyTrip(t) {
    var id = Number(t.taxiSupervisorID);
    if (id === 6) return 'eco';
    if (id === 8) return 'vip';
    if (id === 7) return 'tashrifat';

    var hay = normalizeFa(
      (t.taxiSupervisorName || '') + ' ' + (t.carModelName || t.CarModelName || '')
    );

    // تشریفات first — name often contains «VIP» too (e.g. «تشريفات VIP»)
    if (/تشریف|کمری|سوناتا|سفران/.test(hay)) return 'tashrifat';
    if (/آریو|اکسنت|جیلی|accent|geely|ario/.test(hay)) return 'vip';
    if (/(^|[^\u0600-\u06ff])vip([^\u0600-\u06ff]|$)/.test(hay) ||
        /وی[\u200c\s]*آی[\u200c\s]*پی|ویایپی/.test(hay)) {
      if (!/تشریف/.test(hay)) return 'vip';
    }
    if (/اکو|eco|economy|سمند|سورن/.test(hay)) return 'eco';
    if (/ون|van|هایس|h350/.test(hay)) return 'van';
    return null;
  }

  function filterByCarClass(trips, carClass) {
    if (!carClass) return trips;
    return trips.filter(function (t) {
      return classifyTrip(t) === carClass;
    });
  }

  function extractTime(t) {
    var raw = t.startingDateTime || t.departureTime || t.DepartureTime || t.time || t.startTime || '';
    if (typeof raw === 'string' && raw.length >= 4) {
      var m = raw.match(/(\d{1,2}):(\d{2})/);
      if (m) return pad(parseInt(m[1], 10)) + ':' + m[2];
      return raw;
    }
    return '—:—';
  }

  /** Sort key in minutes; 00:00 is end-of-day (last slot), not first. */
  function timeSortKey(timeStr) {
    var m = String(timeStr || '').match(/(\d{1,2}):(\d{2})/);
    if (!m) return 99999;
    var h = parseInt(m[1], 10);
    var min = parseInt(m[2], 10);
    if (h === 0 && min === 0) return 24 * 60;
    return h * 60 + min;
  }

  /* ---------- Steps ---------- */

  function goStep(n) {
    state.step = n;
    if (n >= 2) clearSheetPinUi();
    els.root.querySelectorAll('.mapbook__panel').forEach(function (p) {
      p.classList.toggle('is-active', Number(p.dataset.panel) === n);
    });
    syncStepsUi();

    if (n === 2) {
      els.destList.style.display = '';
      els.destSearch.parentElement.style.display = '';
      els.destPinHint.hidden = true;
    }
    if (n === 4 && els.dateStrip && !els.dateStrip.children.length) {
      buildDateStrip();
    }
    if (n >= 3) {
      hideMapFloatBadges();
      if (els.root) els.root.classList.add('is-route-preview');
    } else if (els.root) {
      els.root.classList.remove('is-route-preview');
    }
    if (n >= 3 && state.routeCoords) {
      requestAnimationFrame(function () {
        if (map) {
          try { map.resize(); } catch (e) { /* ignore */ }
        }
        autoArrangeRouteCamera();
      });
    }
    syncSheetBack();
  }

  function syncStepsUi() {
    var current = state.step;
    var hints = {
      1: 'انتخاب شهر و نقطه سوار شدن',
      2: 'انتخاب شهر و نقطه پیاده شدن',
      3: 'انتخاب کلاس خودرو',
      4: 'انتخاب تاریخ و ساعت حرکت'
    };
    var carLabels = {
      eco: 'اکونومی',
      vip: 'VIP',
      van: 'ون',
      tashrifat: 'تشریفات'
    };

    els.steps.forEach(function (btn) {
      var s = Number(btn.dataset.step);
      var isActive = s === current;
      var isDone = s < current;
      var isLocked = s > current;

      btn.classList.toggle('is-active', isActive);
      btn.classList.toggle('is-done', isDone);
      btn.classList.toggle('is-skeleton', isDone);
      btn.disabled = isLocked;
      btn.setAttribute('aria-current', isActive ? 'step' : 'false');
      btn.setAttribute('aria-disabled', isLocked ? 'true' : 'false');
      btn.setAttribute('aria-expanded', isActive ? 'true' : 'false');

      var acc = btn.closest('.mapbook__acc');
      if (acc) {
        acc.classList.toggle('is-open', isActive);
        acc.classList.toggle('is-active', isActive);
        acc.classList.toggle('is-done', isDone);
        acc.classList.toggle('is-locked', isLocked);
        acc.classList.toggle('is-skeleton', isDone);

        var hint = acc.querySelector('[data-acc-hint]');
        if (hint) {
          if (isDone) {
            hint.textContent = stepSummary(s, carLabels) || hints[s];
          } else {
            hint.textContent = hints[s];
          }
        }
      }
    });
  }

  function stepSummary(s, carLabels) {
    if (s === 1) {
      return (els.originText && els.originText.textContent) ||
        (state.originCity && state.originCity.name) || '';
    }
    if (s === 2) {
      return (els.destText && els.destText.textContent) ||
        (state.destCity && state.destCity.name) || '';
    }
    if (s === 3) {
      return (state.carClass && carLabels[state.carClass]) || '';
    }
    if (s === 4) {
      return (els.selectedTime && els.selectedTime.textContent) || '';
    }
    return '';
  }

  function canAdvanceTo(s) {
    if (s === 2) return !!state.originLatLng;
    if (s === 3) return !!state.destLatLng;
    if (s === 4) return !!state.carClass;
    return false;
  }

  function resetFrom(step) {
    if (step <= 1) {
      state.originCity = null;
      state.originLatLng = null;
      state.originZones = null;
      els.originBadge.hidden = true;
      els.cityList.style.display = '';
      els.provinceChips.style.display = '';
      els.citySearch.parentElement.style.display = '';
      els.originPinHint.hidden = true;
      if (originMarker) { originMarker.remove(); originMarker = null; }
    }
    if (step <= 2) {
      state.destCity = null;
      state.destLatLng = null;
      state.destZones = null;
      state.routeCoords = null;
      state.routeSource = null;
      state.routeDurationSec = null;
      if (els.routeZones) els.routeZones.hidden = true;
      if (els.fitRouteBtn) els.fitRouteBtn.hidden = true;
      els.destBadge.hidden = true;
      els.eta.hidden = true;
      if (els.root) els.root.classList.remove('is-route-preview');
      routeCanvas && routeCanvas.setPath([]);
      routeCanvas && routeCanvas.stop();
      clearMapRouteLayer();
      clearBoundVisual();
      if (destMarker) { destMarker.remove(); destMarker = null; }
    }
    if (step <= 3) {
      state.carClass = null;
      els.confirmCar.disabled = true;
      els.carClasses.querySelectorAll('.mapbook__car').forEach(function (c) {
        c.setAttribute('aria-selected', 'false');
      });
    }
    state.tripPlanCode = null;
    els.continueBtn.disabled = true;
    goStep(step);
  }

  /* ---------- utils ---------- */

  function unique(arr) {
    return arr.filter(function (v, i, a) { return a.indexOf(v) === i; });
  }

  function faSort(a, b) {
    return String(a).localeCompare(String(b), 'fa');
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
    });
  }

  function midPoint(a, b) {
    return { lat: (a.lat + b.lat) / 2, lng: (a.lng + b.lng) / 2 };
  }

  function formatDuration(sec) {
    var m = Math.round(sec / 60);
    if (m < 60) return toFaDigits(m) + ' دقیقه';
    var h = Math.floor(m / 60);
    var rem = m % 60;
    return toFaDigits(h) + ' ساعت و ' + toFaDigits(rem) + ' دقیقه';
  }

  function toFaDigits(v) {
    return String(v).replace(/\d/g, function (d) {
      return '۰۱۲۳۴۵۶۷۸۹'[Number(d)];
    });
  }

  function pad(n) {
    return n < 10 ? '0' + n : String(n);
  }

  function lerpColor(a, b, t) {
    var ca = hexToRgb(a);
    var cb = hexToRgb(b);
    var r = Math.round(ca.r + (cb.r - ca.r) * t);
    var g = Math.round(ca.g + (cb.g - ca.g) * t);
    var bl = Math.round(ca.b + (cb.b - ca.b) * t);
    return rgbToHex(r, g, bl);
  }

  function hexToRgb(hex) {
    var h = hex.replace('#', '');
    return {
      r: parseInt(h.slice(0, 2), 16),
      g: parseInt(h.slice(2, 4), 16),
      b: parseInt(h.slice(4, 6), 16)
    };
  }

  function rgbToHex(r, g, b) {
    return '#' + [r, g, b].map(function (x) {
      var s = x.toString(16);
      return s.length === 1 ? '0' + s : s;
    }).join('');
  }

  function hexAlpha(hex, a) {
    var c = hexToRgb(hex);
    return 'rgba(' + c.r + ',' + c.g + ',' + c.b + ',' + a + ')';
  }
})();
