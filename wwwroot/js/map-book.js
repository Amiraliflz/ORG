/**
 * MapBook — Snapp-style map booking UX.
 * Flow: city → pin in border → city → pin → route anim → car class → date/time → Reservetrip
 */
(function () {
  'use strict';

  var ORIGIN_COLOR = '#2563eb';
  var DEST_COLOR = '#0f766e';
  var SUGGESTED_CITY_LIMIT = 3;
  /* Cool muted roads — sit with Neshan light basemap + Shoofer ink */
  var HWY_MOTORWAY = '#4a5568';
  var HWY_PRIMARY = '#718096';
  var HWY_SECONDARY = '#a0aec0';
  var REDUCE_MOTION = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

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
    trafficOn: false,
    trafficUserTouched: false,
    zonesLoaded: false,
    sheetCollapsed: false,
    sheetPinMode: null, // null | 'map' | 'confirm'
    pendingDestName: null,
    originLabel: null,
    destLabel: null
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

    // Cities first (inline / cache) so the picker paints immediately
    applyCities(readCitiesFast(root));
    applyCityBorders(readCityBordersFast(root));
    initMap();
    applyDeepLink(root);

    // Background refresh of cities JSON into localStorage (non-blocking)
    refreshCitiesCache(root).catch(function () { /* ignore */ });
    refreshCityBordersCache(root).catch(function () { /* ignore */ });
  }

  function findCityByName(name) {
    var q = normalizeFa(String(name || '').trim());
    if (!q) return null;
    var exact = null;
    var soft = null;
    for (var i = 0; i < state.cities.length; i++) {
      var c = state.cities[i];
      var n = normalizeFa(c.name);
      if (n === q) { exact = c; break; }
      if (!soft && (n.indexOf(q) === 0 || q.indexOf(n) === 0)) soft = c;
    }
    return exact || soft;
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
    var inline = parseInlineJson('mbCityBordersData');
    if (inline && inline.features && inline.features.length) {
      writeLocalJson(cacheKey('cityBorders', ver), inline);
      return inline;
    }
    var cached = readLocalJson(cacheKey('cityBorders', ver));
    if (cached && cached.features && cached.features.length) return cached;
    return { type: 'FeatureCollection', features: [] };
  }

  function applyCityBorders(data) {
    state.cityBorders = Object.create(null);
    var feats = (data && data.features) || [];
    feats.forEach(function (f) {
      var id = f && f.properties && f.properties.id;
      if (id && f.geometry) state.cityBorders[id] = f;
    });
  }

  async function refreshCityBordersCache(root) {
    var url = (root.dataset.cityBordersUrl || '').trim();
    if (!url) return;
    var ver = root.dataset.cacheVer || '1';
    var res = await fetch(url + (url.indexOf('?') >= 0 ? '&' : '?') + 'v=' + encodeURIComponent(ver), {
      credentials: 'same-origin',
      cache: 'no-store'
    });
    if (!res.ok) return;
    var data = await res.json();
    if (!data || !data.features || !data.features.length) return;
    writeLocalJson(cacheKey('cityBorders', ver), data);
    var next = data.features.length;
    var cur = Object.keys(state.cityBorders || {}).length;
    if (!cur || next !== cur) applyCityBorders(data);
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
      originAddress: document.getElementById('mbOriginAddress'),
      destAddress: document.getElementById('mbDestAddress'),
      pinCallout: document.getElementById('mbPinCallout'),
      pinCalloutTitle: document.getElementById('mbPinCalloutTitle'),
      pinCalloutSub: document.getElementById('mbPinCalloutSub'),
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
      map.setMaxBounds([[44.0, 24.8], [63.4, 39.9]]);
    } catch (e) { /* older SDK */ }

    state.trafficOn = false;
    syncTrafficToggleUi();

    map.on('movestart', onMapInteractStart);
    map.on('dragstart', onMapInteractStart);
    map.on('zoomstart', onMapInteractStart);
    map.on('moveend', onMapInteractEnd);
    map.on('dragend', function () {
      if (state.picking) scheduleConfirmPeek();
    });
    map.on('moveend', function () {
      if (state.trafficOn) scheduleTrafficWaveGeom();
    });
    map.on('zoomend', function () {
      if (state.trafficOn) scheduleTrafficWaveGeom();
    });
    map.on('click', function () {
      if (!state.picking) return;
      clearTimeout(scheduleConfirmPeek._t);
      setSheetPinMode('map');
      scheduleConfirmPeek();
    });
    map.on('move', function () {
      scheduleOverlaySync();
    });
    map.on('zoom', function () {
      scheduleOverlaySync();
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
    });

    // Style can reload when map type / traffic sources settle
    map.on('styledata', function () {
      if (!mapReady) return;
      clearTimeout(restyleRoadLayers._t);
      restyleRoadLayers._t = setTimeout(function () {
        restyleRoadLayers();
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
    scheduleTrafficStylePolish._t = setTimeout(function () {
      restyleTrafficLayers();
      startTrafficWave();
    }, 220);
    setTimeout(function () {
      restyleTrafficLayers();
      startTrafficWave();
    }, 1100);
    setTimeout(function () {
      restyleTrafficLayers();
      startTrafficWave();
    }, 2200);
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

    ensureTrafficWaveSourceAndLayers();
    rebuildTrafficWaveGeometry();
  }

  function startTrafficWave() {
    stopTrafficWave(false);
    if (!state.trafficOn) return;
    if (!trafficWave.bases.length) restyleTrafficLayers();
    if (!ensureTrafficWaveSourceAndLayers()) return;

    try {
      if (map.getLayer(TRAFFIC_WAVE_LAYER)) map.setLayoutProperty(TRAFFIC_WAVE_LAYER, 'visibility', 'visible');
      if (map.getLayer(TRAFFIC_WAVE_GLOW)) map.setLayoutProperty(TRAFFIC_WAVE_GLOW, 'visibility', 'visible');
    } catch (eVis) { /* ignore */ }

    rebuildTrafficWaveGeometry();
    scheduleTrafficWaveGeom();

    var speed = REDUCE_MOTION ? TRAFFIC_WAVE_SPEED * 0.4 : TRAFFIC_WAVE_SPEED;
    trafficWave.t0 = null;

    function tick(now) {
      if (!map || !state.trafficOn) {
        trafficWave.raf = null;
        return;
      }
      if (trafficWave.t0 == null) trafficWave.t0 = now;
      trafficWave.phase = ((now - trafficWave.t0) * speed) % 1;
      applyWaveGradientPaint(trafficWave.phase);
      trafficWave.raf = requestAnimationFrame(tick);
    }
    trafficWave.raf = requestAnimationFrame(tick);
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
      }, 380);
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
    flyToLatLng(ll.lat, ll.lng, Math.max(map.getZoom(), 16), !REDUCE_MOTION);
    listEl.hidden = true;
    var input = kind === 'origin' ? els.originPlaceSearch : els.destPlaceSearch;
    if (input) input.value = item.title || '';
    setPinAddress(
      kind === 'origin' ? els.originAddress : els.destAddress,
      item.title,
      item.subtitle
    );
  }

  function reverseCurrentPin() {
    if (!state.picking) return;
    clearTimeout(reverseTimer);
    reverseTimer = setTimeout(doReverse, 450);
  }

  async function doReverse() {
    if (!state.picking || !map) return;
    var ll = map.getCenter();
    var seq = ++reverseSeq;
    var addrEl = state.picking === 'origin' ? els.originAddress : els.destAddress;
    if (els.pinCallout) {
      els.pinCallout.hidden = false;
      if (els.pinCalloutTitle) els.pinCalloutTitle.textContent = 'در حال یافتن آدرس…';
      if (els.pinCalloutSub) els.pinCalloutSub.textContent = '';
      setZoneChips(null);
    }
    try {
      var url = '/Reserve/ReverseGeocode?lat=' + encodeURIComponent(ll.lat) +
        '&lng=' + encodeURIComponent(ll.lng);
      var res = await fetch(url, { credentials: 'same-origin' });
      var data = await res.json();
      if (seq !== reverseSeq) return;
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
    } catch (e) {
      if (seq !== reverseSeq) return;
      setPinAddress(addrEl, 'موقعیت روی نقشه', '', null);
    }
  }

  function setPinAddress(el, title, subtitle, zones) {
    var t = title || 'موقعیت روی نقشه';
    var s = subtitle || '';
    if (el) {
      el.innerHTML = '<strong>' + escapeHtml(t) + '</strong>' +
        (s ? '<br>' + escapeHtml(s) : '');
    }
    if (els.pinCallout) {
      els.pinCallout.hidden = false;
      if (els.pinCalloutTitle) els.pinCalloutTitle.textContent = t;
      if (els.pinCalloutSub) {
        els.pinCalloutSub.textContent = s;
        els.pinCalloutSub.hidden = !s;
      }
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
    else stopTrafficWave();
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
      map.easeTo(Object.assign({ duration: 700 }, opts));
    }
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
      if (!sheetH) {
        sheetH = Math.round(mapH * (els.sheet.classList.contains('is-collapsed')
          ? 0.08
          : (els.sheet.classList.contains('is-roomy') ? 0.48 : 0.4)));
      }
      // Top chrome (back + steps)
      pad.top = 92;
      pad.left = 18;
      pad.right = 18;
      // Route must live in the visible map ABOVE the bottom sheet (use real height)
      pad.bottom = Math.round(sheetH + 18);
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

    // Pad bounds ~6% so the line isn't glued to the edges of the visible area
    var latPad = Math.max((maxLat - minLat) * 0.06, 0.012);
    var lngPad = Math.max((maxLng - minLng) * 0.06, 0.012);
    minLat -= latPad; maxLat += latPad;
    minLng -= lngPad; maxLng += lngPad;

    if (minLat === maxLat) { minLat -= 0.02; maxLat += 0.02; }
    if (minLng === maxLng) { minLng -= 0.02; maxLng += 0.02; }
    return [[minLng, minLat], [maxLng, maxLat]];
  }

  function arrangeMapToRoute(userTriggered) {
    if (!map || !mapReady || !state.routeCoords || state.routeCoords.length < 2) return;
    var bounds = routeBoundsLngLat();
    if (!bounds) return;

    var distKm = mapDistanceMeters(state.routeCoords[0], state.routeCoords[state.routeCoords.length - 1]) / 1000;
    var maxZoom = distKm > 120 ? 7.2 : distKm > 80 ? 8.2 : distKm > 40 ? 9.2 : distKm > 25 ? 10.2 : distKm > 12 ? 11.2 : distKm > 5 ? 12.2 : 13.5;
    var duration = userTriggered ? 520 : (REDUCE_MOTION ? 0 : 850);

    try { map.resize(); } catch (e) { /* ignore */ }

    var padding = getMapUiPadding();
    var fitOpts = {
      padding: padding,
      maxZoom: maxZoom,
      duration: duration,
      essential: true,
      bearing: 0,
      pitch: 0
    };

    try {
      map.fitBounds(bounds, fitOpts);
    } catch (e) {
      console.warn('fitBounds failed', e);
      try {
        if (typeof map.cameraForBounds === 'function') {
          var cam = map.cameraForBounds(bounds, { padding: padding, maxZoom: maxZoom });
          if (cam) {
            map.easeTo({
              center: cam.center,
              zoom: Math.min(cam.zoom, maxZoom),
              bearing: 0,
              pitch: 0,
              duration: duration,
              essential: true
            });
          }
        }
      } catch (e2) {
        console.warn('cameraForBounds failed', e2);
      }
    }
  }

  /** Fit once now, again after layout/panel settles (PC side picker / mobile sheet). */
  function autoArrangeRouteCamera() {
    clearTimeout(autoArrangeRouteCamera._t1);
    clearTimeout(autoArrangeRouteCamera._t2);
    clearTimeout(autoArrangeRouteCamera._t3);
    requestAnimationFrame(function () {
      arrangeMapToRoute(false);
      autoArrangeRouteCamera._t1 = setTimeout(function () {
        arrangeMapToRoute(false);
      }, 320);
      autoArrangeRouteCamera._t2 = setTimeout(function () {
        arrangeMapToRoute(false);
      }, 750);
      autoArrangeRouteCamera._t3 = setTimeout(function () {
        arrangeMapToRoute(false);
      }, 1200);
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

  function renderCityList(listEl, query, isDest) {
    var q = (query || '').trim();
    var items = state.cities.filter(function (c) {
      if (!isDest && state.provinceFilter && c.province !== state.provinceFilter) return false;
      if (isDest && state.originCity && c.id === state.originCity.id) return false;
      if (!q) return true;
      return c.name.indexOf(q) !== -1 || c.province.indexOf(q) !== -1;
    });

    // Empty search = suggested list only (top money hubs)
    if (!q && !state.provinceFilter) {
      items = items.slice(0, SUGGESTED_CITY_LIMIT);
    }

    listEl.innerHTML = '';
    if (!items.length) {
      listEl.innerHTML = '<li><button type="button" disabled>شهری پیدا نشد</button></li>';
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
    startPicking('origin', city);
    els.originPinHint.hidden = false;
    els.cityList.style.display = 'none';
    els.provinceChips.style.display = 'none';
    els.citySearch.parentElement.style.display = 'none';
  }

  function selectDestCity(city) {
    state.destCity = city;
    startPicking('dest', city);
    els.destPinHint.hidden = false;
    els.destList.style.display = 'none';
    els.destSearch.parentElement.style.display = 'none';
  }

  function startPicking(kind, city) {
    state.picking = kind;
    state.sheetPinMode = null;
    state.sheetCollapsed = false;
    els.root.classList.toggle('is-picking-origin', kind === 'origin');
    els.root.classList.toggle('is-picking-dest', kind === 'dest');
    els.centerPin.hidden = false;

    whenMapReady(function () {
      fitToCityBorder(city);
      updateBoundVisual();
      reverseCurrentPin();
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
    if (els.pinCallout) {
      els.pinCallout.hidden = false;
      if (els.pinCalloutTitle) els.pinCalloutTitle.textContent = city.name;
      if (els.pinCalloutSub) {
        els.pinCalloutSub.textContent = 'فقط داخل محدوده شهر — محله را جستجو کنید یا نقشه را جابه‌جا کنید';
        els.pinCalloutSub.hidden = false;
      }
    }
    setZoneChips(null);
    syncSheetBack();
  }

  function exitPicking() {
    state.picking = null;
    clearTimeout(scheduleConfirmPeek._t);
    state.sheetPinMode = null;
    els.root.classList.remove('is-picking-origin', 'is-picking-dest');
    els.centerPin.hidden = true;
    if (els.pinCallout) els.pinCallout.hidden = true;
    clearBoundVisual();
    setZoneChips(null);
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
    exitPicking();
    resetFrom(1);
    showOriginCityChooser();
    syncSheetBack();
  }

  function reselectDestination() {
    var oCity = state.originCity;
    var oLl = state.originLatLng;
    var oZones = state.originZones;
    var oLabel = (els.originText && els.originText.textContent) || (oCity && oCity.name) || 'مبدأ';
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
    syncSheetBack();
  }

  function goBack() {
    if (state.picking === 'origin') {
      exitPicking();
      state.originCity = null;
      state.originLatLng = null;
      if (originMarker) { originMarker.remove(); originMarker = null; }
      els.originBadge.hidden = true;
      goStep(1);
      showOriginCityChooser();
      syncSheetBack();
      return;
    }
    if (state.picking === 'dest') {
      exitPicking();
      state.destCity = null;
      state.destLatLng = null;
      goStep(2);
      showDestCityChooser();
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

  function syncSheetMode() {
    if (!els.sheet) return;
    var desktop = window.matchMedia('(min-width: 1024px)').matches;
    if (desktop) {
      els.sheet.classList.remove('is-collapsed', 'is-roomy', 'is-map-focus', 'is-confirm-peek');
      state.sheetCollapsed = false;
      state.sheetPinMode = null;
      return;
    }
    var pinOpen = !!state.picking ||
      (els.originPinHint && !els.originPinHint.hidden) ||
      (els.destPinHint && !els.destPinHint.hidden);
    var roomy = (pinOpen || state.step >= 3) && !state.sheetCollapsed && !state.sheetPinMode;
    els.sheet.classList.toggle('is-roomy', roomy);
    els.sheet.classList.toggle('is-collapsed', !!state.sheetCollapsed && !state.sheetPinMode);
    els.sheet.classList.toggle('is-map-focus', state.sheetPinMode === 'map');
    els.sheet.classList.toggle('is-confirm-peek', state.sheetPinMode === 'confirm');

    if (els.sheetHandle) {
      var expanded = !state.sheetCollapsed && state.sheetPinMode !== 'map';
      els.sheetHandle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }
    if (els.sheetGrabHint) {
      if (state.sheetPinMode === 'map') {
        els.sheetGrabHint.textContent = 'برای تأیید مبدأ/مقصد کمی صبر کنید';
      } else if (state.sheetPinMode === 'confirm') {
        els.sheetGrabHint.textContent = 'تأیید را بزنید یا نقشه را جابه‌جا کنید';
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
      setSheetPinMode('confirm');
    }, 520);
  }

  function onMapInteractStart() {
    if (state.picking) els.centerPin.classList.add('is-lifting');
    if (state.picking) {
      clearTimeout(scheduleConfirmPeek._t);
      setSheetPinMode('map');
    }
  }

  function onMapInteractEnd() {
    els.centerPin.classList.remove('is-lifting');
    if (!state.picking) return;
    if (enforcePickInsideBorder()) return;
    updateBoundVisual();
    reverseCurrentPin();
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
    if (!map || !city) return;
    var feat = getCityBorderFeature(city);
    var bbox = featureBbox(feat);
    if (bbox) {
      try {
        map.fitBounds(bbox, {
          padding: getMapUiPadding(),
          maxZoom: 12.5,
          duration: REDUCE_MOTION ? 0 : 700,
          essential: true
        });
        return;
      } catch (e) { /* fall through */ }
    }
    flyToLatLng(city.lat, city.lng, 12, !REDUCE_MOTION);
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

  var _borderSnapLock = false;
  function enforcePickInsideBorder() {
    if (!state.picking || !map || _borderSnapLock) return false;
    var city = state.picking === 'origin' ? state.originCity : state.destCity;
    if (!city) return false;
    var c = map.getCenter();
    var ll = { lat: c.lat, lng: c.lng };
    if (insideBorder(ll, city)) return false;
    _borderSnapLock = true;
    pulseHint(state.picking === 'origin' ? els.originPinHint : els.destPinHint);
    flyToLatLng(city.lat, city.lng, Math.min(map.getZoom(), 13), true);
    setTimeout(function () { _borderSnapLock = false; }, 500);
    return true;
  }

  function clearSheetPinUi() {
    clearTimeout(scheduleConfirmPeek._t);
    state.sheetPinMode = null;
    state.sheetCollapsed = false;
    if (els.sheet) {
      els.sheet.classList.remove('is-confirm-peek', 'is-map-focus', 'is-collapsed');
    }
  }

  function confirmOriginPin() {
    if (!map || !state.originCity) return;
    var c = map.getCenter();
    var ll = { lat: c.lat, lng: c.lng };
    if (!insideBorder(ll, state.originCity)) {
      pulseHint(els.originPinHint);
      flyToLatLng(state.originCity.lat, state.originCity.lng, map.getZoom(), true);
      return;
    }
    var label = (els.originPlaceSearch && els.originPlaceSearch.value.trim())
      || (els.pinCalloutTitle && els.pinCalloutTitle.textContent)
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
    var c = map.getCenter();
    var ll = { lat: c.lat, lng: c.lng };
    if (!insideBorder(ll, state.destCity)) {
      pulseHint(els.destPinHint);
      flyToLatLng(state.destCity.lat, state.destCity.lng, map.getZoom(), true);
      return;
    }
    var label = (els.destPlaceSearch && els.destPlaceSearch.value.trim())
      || (els.pinCalloutTitle && els.pinCalloutTitle.textContent)
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

  function makeDotMarker(ll, color) {
    var el = document.createElement('div');
    el.className = 'mapbook__dot-marker';
    el.style.background = color;
    return new nmp.Marker({ element: el, anchor: 'center' })
      .setLngLat([ll.lng, ll.lat])
      .addTo(map);
  }

  function placeOriginMarker(ll) {
    if (originMarker) originMarker.remove();
    originMarker = makeDotMarker(ll, ORIGIN_COLOR);
  }

  function placeDestMarker(ll) {
    if (destMarker) destMarker.remove();
    destMarker = makeDotMarker(ll, DEST_COLOR);
  }

  function showBadge(badge, textEl, name, ll) {
    textEl.textContent = name;
    badge.hidden = false;
    positionOverlay(badge, ll);
  }

  function positionOverlay(el, ll) {
    if (!map || !ll) return;
    var pt = map.project([ll.lng, ll.lat]);
    el.style.left = pt.x + 'px';
    el.style.top = pt.y + 'px';
  }

  var _overlaySyncRaf = 0;
  function scheduleOverlaySync() {
    if (_overlaySyncRaf) return;
    _overlaySyncRaf = requestAnimationFrame(function () {
      _overlaySyncRaf = 0;
      syncOverlays();
      if (routeCanvas) routeCanvas.redraw();
    });
  }

  function syncOverlays() {
    if (!map) return;
    if (state.originLatLng) positionOverlay(els.originBadge, state.originLatLng);
    if (state.destLatLng) {
      positionOverlay(els.destBadge, state.destLatLng);
      if (!els.eta.hidden && state.originLatLng) {
        var mid = midPoint(state.originLatLng, state.destLatLng);
        var pt = map.project([mid.lng, mid.lat]);
        els.eta.style.left = pt.x + 'px';
        els.eta.style.top = pt.y + 'px';
      }
    }
  }

  /* ---------- Route + Snapp-style glow ---------- */

  async function drawRouteAndAnimate() {
    var o = state.originLatLng;
    var d = state.destLatLng;
    var coords = null;
    var durationSec = null;
    var source = 'fallback';

    try {
      var url = '/Reserve/OsrmRoute?oLat=' + o.lat + '&oLng=' + o.lng +
        '&dLat=' + d.lat + '&dLng=' + d.lng;
      var res = await fetch(url, { credentials: 'same-origin' });
      var data = await res.json();
      source = data.source || 'osrm';
      if (data.routes && data.routes[0] && data.routes[0].geometry) {
        coords = data.routes[0].geometry.coordinates.map(function (c) {
          return [c[1], c[0]];
        });
        durationSec = data.routes[0].duration;
      }
    } catch (e) {
      console.warn('route failed, using fallback', e);
    }

    if (!coords || coords.length < 2) {
      coords = fallbackCurve(o, d);
      source = 'fallback';
    }

    state.routeSource = source;
    // Real road geometry: light densify only. Chaikin would pull the line off the roads.
    if (source === 'neshan' || source === 'osrm') {
      var target = Math.min(420, Math.max(coords.length, Math.floor(coords.length * 1.15)));
      state.routeCoords = densifyPath(coords, target);
    } else {
      state.routeCoords = smoothLatLngPath(densifyPath(coords, 120), 1);
    }
    routeCanvas.setPath(state.routeCoords);
    routeCanvas.play();
    renderRouteZoneSummary();
    autoArrangeRouteCamera();

    if (els.eta) els.eta.hidden = true;
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
            self._mode = 'idle';
            self._raf = null;
            self.redraw();
          }
        }
        this._raf = requestAnimationFrame(tick);
      },
      stop: function () {
        if (this._raf) cancelAnimationFrame(this._raf);
        this._raf = null;
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

        ctx.strokeStyle = 'rgba(15, 23, 42, 0.28)';
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
          ctx.globalAlpha = 0.95;
          ctx.lineWidth = 7;
          strokePoly(ctx, slice);
        }
        ctx.globalAlpha = 1;

        ctx.strokeStyle = 'rgba(255,255,255,0.5)';
        ctx.lineWidth = 2;
        strokePoly(ctx, visible);
      }
    };
    api.resize();
    return api;
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
      if (els.routeZones) els.routeZones.hidden = true;
      if (els.fitRouteBtn) els.fitRouteBtn.hidden = true;
      els.destBadge.hidden = true;
      els.eta.hidden = true;
      routeCanvas && routeCanvas.setPath([]);
      routeCanvas && routeCanvas.stop();
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
