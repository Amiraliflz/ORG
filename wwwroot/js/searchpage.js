// Enhanced search functionality with normalization, dynamic origin/destination lists
// Keeps existing trip card rendering logic (Trips.js) unchanged.
// Origin/Destination UX: advanced matching, Persian guesses, supported city intersection.

let directions = [];
let originKeys = []; // normalized city keys (after intersecting with supported)
let supportedKeys = new Set(); // normalized keys supported by server (DirectionsRepository)
let displayNameByKey = new Map(); // normalized -> display name (prefer Persian)
let _destinations = []; // destination display names
let trips = []; // will be reused by Trips.js

// Decode literal \uXXXX sequences into real characters (handles double-escaped payloads)
function decodeUnicodeEscapes(str) {
  if (typeof str !== 'string') return str;
  if (!/\\u[0-9a-fA-F]{4}/.test(str)) return str;
  try {
    return str.replace(/\\u([0-9a-fA-F]{4})/g, (_, g1) => String.fromCharCode(parseInt(g1, 16)));
  } catch { return str; }
}

// Map common latin spellings to Persian city names
function toPersianGuess(str) {
  const s = (str || '').trim().toLowerCase();
  switch (s) {
    case 'tehran':
    case 'teh':
      return 'تهران';
    case 'isfahan':
    case 'esfahan':
      return 'اصفهان';
    case 'rasht':
      return 'رشت';
    case 'chalus':
    case 'chaloos':
    case 'chalous':
      return 'چالوس';
    case 'kermanshah':
      return 'کرمانشاه';
    case 'noushahr':
    case 'nowshahr':
    case 'noshahr':
      return 'نوشهر';
    case 'tabriz':
      return 'تبریز';
    case 'qom':
    case 'ghom':
      return 'قم';
    case 'hamedan':
    case 'hamadan':
      return 'همدان';
    case 'sari':
      return 'ساری';
    case 'shiraz':
      return 'شیراز';
    case 'mashhad':
    case 'mashad':
      return 'مشهد';
    case 'karaj':
      return 'کرج';
    case 'qazvin':
    case 'ghazvin':
      return 'قزوین';
    case 'kerman':
      return 'کرمان';
    case 'yazd':
      return 'یزد';
    case 'gorgan':
      return 'گرگان';
    case 'zanjan':
      return 'زنجان';
    case 'kashan':
      return 'کاشان';
    case 'sanandaj':
      return 'سنندج';
    case 'shahrekord':
    case 'shahr-e-kord':
      return 'شهرکرد';
    case 'lahijan':
      return 'لاهیجان';
    case 'ramsar':
      return 'رامسر';
    default:
      return str;
  }
}

function isAscii(str) { return /^[\x00-\x7F]*$/.test(str || ''); }

// Normalize text: trim, unify Arabic/Persian chars, remove ZWNJ/diacritics, lowercase
function normalize(str) {
  try {
    return (str || '')
      .trim()
      .replace(/\(.*/, '')
      .replace(/[\u200C\u200F\u200E\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED]/g, '')
      .replace(/\u064A/g, '\u06CC')
      .replace(/\u0643/g, '\u06A9')
      .replace(/[\u0629]/g, '\u0647')
      .replace(/\s+/g, ' ')
      .toLocaleLowerCase();
  } catch { return (str || '').trim().toLocaleLowerCase(); }
}

function ensureOriginDropdown() {
  const spanElement = $('.origin_location');
  if ($('#origincontainer').length === 0) {
    spanElement.html(`
      <div class="staredlocations">
        <label class="staredlocation_title ms-2 mt-2 text-muted pb-1" id="origin_most_lable">
          <i class="ti ti-map-pin-star icon locationicon p-1 pe-0"></i>
          شهرهای پرتردد
        </label>
        <div class="px-1 .terminals_container_orig" id="origincontainer"></div>
      </div>`);
  }
}

function ensureDestinationDropdown() {
  const spanElement = $('.dropdown-menu.destination_location');
  if ($('#desticontainer').length === 0) {
    spanElement.html(`
      <div class="staredlocations">
        <label class="staredlocation_title ms-2 mt-2 text-muted pb-1">
          <i class="ti ti-map-pin-star icon locationicon p-1 pe-0"></i>
          مقصد ها
        </label>
        <div class="px-1 .terminals_container_desti" id="desticontainer"></div>
      </div>`);
  }
}

function FetchDirections() {
  return new Promise((resolve, reject) => {
    $.getJSON('/TaxiTrips/AvailableDirections', function (data) {
      directions = [];
      originKeys = [];
      displayNameByKey = new Map();

      const normalizedPairs = (data || [])
        .map(item => {
          let raw1 = item.Cityone || item.cityone || item.cityOne || item.city_one || item.city_name || '';
          let raw2 = item.Citytwo || item.citytwo || item.cityTwo || item.city_two || item.destination_city_name || '';
          raw1 = decodeUnicodeEscapes(raw1);
          raw2 = decodeUnicodeEscapes(raw2);
          const disp1 = isAscii(raw1) ? toPersianGuess(raw1) : raw1;
          const disp2 = isAscii(raw2) ? toPersianGuess(raw2) : raw2;
          const key1 = normalize(disp1 || raw1);
          const key2 = normalize(disp2 || raw2);
          if (key1) displayNameByKey.set(key1, disp1 || raw1 || '');
          if (key2) displayNameByKey.set(key2, disp2 || raw2 || '');
          return key1 && key2 ? { Cityone: key1, Citytwo: key2 } : null;
        })
        .filter(Boolean);

      directions = normalizedPairs;
      const cities = new Set();
      directions.forEach(d => { cities.add(d.Cityone); cities.add(d.Citytwo); });
      originKeys = Array.from(cities);
      resolve();
    }).fail(function (xhr) {
      console.error('Failed to fetch available directions.', xhr?.status, xhr?.responseText);
      reject('Error fetching available directions');
    });
  });
}

function FetchSupportedCities() {
  return new Promise((resolve) => {
    $.getJSON('/TaxiTrips/SupportedCities', function (data) {
      supportedKeys = new Set((data || []).map(c => normalize(c)));
      resolve();
    }).fail(function (xhr) {
      console.error('Failed to fetch supported cities.', xhr?.status, xhr?.responseText);
      supportedKeys = new Set();
      resolve();
    });
  });
}

function intersectDirectionsWithSupported() {
  // Skip filtering if supportedKeys is empty or smaller than API directions
  // The API (AvailableDirections) is the source of truth for available cities
  // We only use supportedKeys for validation fallback, not to restrict the city list
  if (!supportedKeys || supportedKeys.size === 0) return;
  
  // Don't filter - API directions should be the primary source
  // The supportedKeys from DirectionsRepository is just a static fallback list
  // Filtering would remove valid cities that the API supports but aren't in the hardcoded list
}

/** Page was server-rendered for this OD; AJAX only updates trip cards, not SEO. */
function pageInitialOd() {
  const form = document.getElementById('tripForm');
  return {
    origin: normalize(toPersianGuess((form?.dataset.initialOrigin || '').trim())),
    dest: normalize(toPersianGuess((form?.dataset.initialDest || '').trim()))
  };
}

function searchOdChangedFromPage() {
  if (!$('.trips-container').length) return false;
  const initial = pageInitialOd();
  if (!initial.origin || !initial.dest) return false;
  const origin = normalize(toPersianGuess(($('#origin_input').val() || '').trim()));
  const dest = normalize(toPersianGuess(($('#destination_input').val() || '').trim()));
  return origin !== initial.origin || dest !== initial.dest;
}

function readSearchCities() {
  // Prefer live .val(); fall back to attribute (DestSelected used to set value="0")
  const read = (sel) => {
    const $el = $(sel);
    let v = ($el.val() || '').toString().trim();
    if (!v || v === '0') {
      const attr = ($el.attr('value') || '').toString().trim();
      if (attr && attr !== '0') v = attr;
    }
    return v;
  };
  return {
    origin: read('#origin_input'),
    destination: read('#destination_input'),
    searchdate: ($('#starttime').val() || '').trim()
  };
}

/** Keep sticky bridge + sidebar labels in sync with the search inputs. */
function updateRouteChromeLabels(origin, destination) {
  const o = (origin || '').trim();
  const d = (destination || '').trim();
  if (!o || !d) return;
  $('.trips-sticky-bridge-route').text(o + ' ← ' + d);
  const $strongs = $('.direction-text strong');
  if ($strongs.length >= 2) {
    $strongs.eq(0).text(o);
    $strongs.eq(1).text(d);
  }
}

/**
 * Swap #route-seo (+ bridge) for the current OD after an AJAX search.
 */
async function refreshRouteSeoUi(origin, destination) {
  const o = (origin || '').trim();
  const d = (destination || '').trim();
  if (!o || !d || o === '0' || d === '0') return;

  updateRouteChromeLabels(o, d);

  const form = document.getElementById('tripForm');
  if (form) {
    form.dataset.initialOrigin = o;
    form.dataset.initialDest = d;
  }

  try {
    const html = await $.ajax({
      url: '/TaxiTrips/RouteSeoPartial',
      method: 'GET',
      dataType: 'html',
      data: { originstring: o, destinationstring: d }
    });

    let $bridge = $('.trips-sticky-bridge');
    if (!$bridge.length && $('.trips-results .page-safezone').length) {
      $('.trips-results .page-safezone').prepend(
        `<div class="trips-sticky-bridge">
          <a href="#route-seo" class="trips-sticky-bridge-inner">
            <span class="trips-sticky-bridge-text">
              <span class="trips-sticky-bridge-label">راهنمای مسیر</span>
              <span class="trips-sticky-bridge-cta">
                نکته‌ها و سوالات
                <i class="ti ti-chevrons-down" aria-hidden="true"></i>
              </span>
              <span class="trips-sticky-bridge-route"></span>
            </span>
          </a>
        </div>`
      );
      $bridge = $('.trips-sticky-bridge');
    }
    updateRouteChromeLabels(o, d);
    $bridge.show();

    const $existing = $('#route-seo');
    if ($existing.length) $existing.replaceWith(html);
    else $('.trips-results .page-safezone').append(html);

    if (!document.querySelector('link[href*="RoutePages.css"]')) {
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = '/css/RoutePages.css?v=12';
      document.head.appendChild(link);
    }
  } catch (err) {
    $('#route-seo').remove();
    $('.trips-sticky-bridge').remove();
  }
}

async function FetchTrips() {
  const { origin, destination, searchdate } = readSearchCities();

  if (!origin || !destination || !searchdate) {
    trips = [];
    if (typeof renderTrips === 'function') renderTrips(trips);
    return;
  }

  // Update labels immediately so the bridge never lags behind the trip cards
  updateRouteChromeLabels(origin, destination);

  const oKey = normalize(toPersianGuess(origin));
  const dKey = normalize(toPersianGuess(destination));
  const isOriginValid = originKeys.length === 0 || originKeys.includes(oKey);
  const isDestValid = originKeys.length === 0 || originKeys.includes(dKey);
  if (!isOriginValid || !isDestValid) {
    const msg = !isOriginValid ? `شهر مبدا نامعتبر است: ${origin}` : `شهر مقصد نامعتبر است: ${destination}`;
    const $container = $('.trips-container');
    if ($container.length) {
      $container.empty().append(`<div class="d-flex col-12 mt-3" style="flex-direction: column; align-items: center; justify-content: start;">
        <label class="fs-5 fw-bold mt-4 pt-3 text-danger">${msg}</label>
      </div>`);
    } else { alert(msg); }
    trips = [];
    return;
  }

  try {
    const url = `/TaxiTrips/SearchJson?originstring=${encodeURIComponent(origin)}&destinationstring=${encodeURIComponent(destination)}&searchdate=${encodeURIComponent(searchdate)}`;
    const data = await $.getJSON(url);
    trips = data || [];
    if (typeof renderTrips === 'function') {
      renderTrips(trips);
      if (typeof GetCarModels === 'function' && typeof GenerateCarModelsFilter === 'function') {
        $('#carmodelsfilter').find('.form-check').not(':first').remove();
        const carModels = GetCarModels(trips);
        GenerateCarModelsFilter(carModels);
      }
    }
    await refreshRouteSeoUi(origin, destination);
  } catch (e) {
    console.error('Failed to fetch trips', e);
    let msg = 'خطا در جستجوی سفر';
    if (e && e.responseJSON && e.responseJSON.error) {
      const sug = Array.isArray(e.responseJSON.suggestions) && e.responseJSON.suggestions.length
        ? `\nپیشنهاد: ${e.responseJSON.suggestions.join('، ')}`
        : '';
      msg = `${e.responseJSON.error}${sug}`;
    }
    const $container = $('.trips-container');
    if ($container.length) {
      $container.empty().append(`<div class="d-flex col-12 mt-3" style="flex-direction: column; align-items: center; justify-content: start;">
        <label class="fs-5 fw-bold mt-4 pt-3 text-danger">${msg}</label>
      </div>`);
    } else { alert(msg); }
    trips = [];
  }
}

var most_used_origins = ["تهران", "اصفهان", "رشت", "چالوس", "کرمانشاه", "نوشهر"];

function keyToDisplay(key) { return displayNameByKey.get(key) || toPersianGuess(key) || key; }

function SetDestinations(originDisplay) {
  ensureDestinationDropdown();
  const selectedKey = normalize(originDisplay);
  const destinationsKeys = [];
  directions.forEach(item => {
    if (item.Cityone === selectedKey) destinationsKeys.push(item.Citytwo);
    else if (item.Citytwo === selectedKey) destinationsKeys.push(item.Cityone);
  });
  const uniqueKeys = Array.from(new Set(destinationsKeys));
  _destinations = uniqueKeys.map(keyToDisplay);
  AddResultLocations_destination(_destinations);
}

function LoadMostUsedOrigins() {
  ensureOriginDropdown();
  $('#origin_most_lable').css('visibility', 'visible');
  const existings = most_used_origins.map(c => normalize(c)).filter(k => originKeys.includes(k));
  const list = existings.length ? existings : originKeys.slice(0, 10);
  AddResultLocations_origin(list);
}

function AddResultLocations_origin(keys) {
  ensureOriginDropdown();
  var terminals_container = $('#origincontainer');
  terminals_container.empty();
  if (!keys || keys.length === 0) {
    terminals_container.append($('<a>', { class: 'dropdown-item text-center mt-2 text-muted', text: "نتیجه‌ای پیدا نشد" }));
  } else {
    keys.forEach(key => {
      const display = keyToDisplay(key);
      var $aTag = $('<a>', { class: 'dropdown-item', text: display });
      $aTag.on('click', function () { OriginSelected(0, display); });
      terminals_container.append($aTag);
    });
  }
}

function AddResultLocations_destination(result_locations) {
  ensureDestinationDropdown();
  var terminals_container = $('#desticontainer');
  terminals_container.empty();
  if (!result_locations || result_locations.length === 0) {
    terminals_container.append($('<a>', { class: 'dropdown-item text-center mt-2 text-muted', text: "ابتدا شهر مبدا را انتخاب کنید" }));
    return;
  }
  result_locations.forEach(location => {
    var $aTag = $('<a>', { class: 'dropdown-item', text: location });
    $aTag.on('click', function () { DestSelected(0, location); FetchTrips(); });
    terminals_container.append($aTag);
  });
}

function OriginSelected(id, name) {
  const city = (name || '').trim();
  // Always store the city name in both property and attribute (never the numeric id)
  $('#origin_input').val(city).attr('value', city);
  $('#destination_input').val('').attr('value', '');
  EnableDestination();
  SetDestinations(city);
}

function DestSelected(id, name) {
  const city = (name || '').trim();
  $('#destination_input').val(city).attr('value', city);
}

/**
 * Swap origin ↔ destination without wiping dest via OriginSelected.
 * Rebuilds destination list for the new origin; keeps dest if still valid.
 */
async function SwapOriginDestination() {
  const $o = $('#origin_input');
  const $d = $('#destination_input');
  if (!$o.length || !$d.length) return;

  const prevOrigin = ($o.val() || '').trim();
  const prevDest = ($d.val() || '').trim();
  if (!prevOrigin && !prevDest) return;

  const newOriginRaw = prevDest;
  const newDestRaw = prevOrigin;

  $o.val(newOriginRaw).attr('value', newOriginRaw);
  $d.val(newDestRaw).attr('value', newDestRaw);

  const originKey = normalize(toPersianGuess(newOriginRaw));

  if (!originKey) {
    LoadMostUsedOrigins();
    _destinations = [];
    AddResultLocations_destination([]);
    DisableDestination();
    return;
  }

  if (!originKeys.includes(originKey)) {
    _destinations = [];
    AddResultLocations_destination([]);
    DisableDestination();
    return;
  }

  const originDisplay = keyToDisplay(originKey);
  $o.val(originDisplay).attr('value', originDisplay);
  SetDestinations(originDisplay);
  EnableDestination();

  const destKey = normalize(toPersianGuess(newDestRaw));
  const destMatch = _destinations.find(city => normalize(city) === destKey);
  if (destMatch) {
    $d.val(destMatch).attr('value', destMatch);
  } else if (newDestRaw) {
    // Keep typed value visible but destinations list already refreshed
    $d.val(newDestRaw).attr('value', newDestRaw);
  } else {
    $d.val('').attr('value', '');
  }

  updateRouteChromeLabels(($o.val() || '').trim(), ($d.val() || '').trim());

  const inTaxiTripsPage = $('.trips-container').length > 0;
  if (inTaxiTripsPage && ($o.val() || '').trim() && ($d.val() || '').trim() && ($('#starttime').val() || '').trim()) {
    await FetchTrips();
  }
}

function DisableDestination() { $("#destination_input").removeAttr("data-bs-toggle").prop("disabled", true); }
function EnableDestination() { $("#destination_input").attr("data-bs-toggle", "dropdown").prop("disabled", false); }

$(document).ready(async function () {
  try {
    await FetchDirections();
    await FetchSupportedCities();
    intersectDirectionsWithSupported();
    ensureOriginDropdown();
    ensureDestinationDropdown();

    var origin_value_raw = ($('#origin_input').val() || '').trim();
    var origin_value = normalize(origin_value_raw);

    if (origin_value && originKeys.includes(origin_value)) {
      SetDestinations(origin_value_raw);
      EnableDestination();
      if ((($('#destination_input').val() || '').trim())) await FetchTrips();
    } else {
      LoadMostUsedOrigins();
      AddResultLocations_destination([]);
      DisableDestination();
    }
  } catch (error) { console.error('An error occurred:', error); }

  $('#origin_input').on('input', function () {
    ensureOriginDropdown();
    const raw = (($(this).val() || ''));
    const maybePersian = toPersianGuess(raw);
    const inputKey = normalize(maybePersian);
    if (inputKey === "") {
      $('#destination_input').val('');
      _destinations = [];
      AddResultLocations_destination([]);
      LoadMostUsedOrigins();
      DisableDestination();
      return;
    }
    $('#origin_most_lable').css('display', 'none');
    const matches = originKeys.filter(key => key.includes(inputKey));
    const listToShow = raw.length < 2 ? originKeys : matches;
    AddResultLocations_origin(listToShow);
    if (originKeys.includes(inputKey)) {
      OriginSelected(0, keyToDisplay(inputKey));
    } else if (matches.length === 1) {
      OriginSelected(0, keyToDisplay(matches[0]));
    } else {
      _destinations = [];
      AddResultLocations_destination([]);
      DisableDestination();
    }
  });

  $('#origin_input').on('blur', function () {
    const maybePersian = toPersianGuess(($(this).val() || ''));
    const inputKey = normalize(maybePersian);
    if (inputKey && originKeys.includes(inputKey)) OriginSelected(0, keyToDisplay(inputKey));
  });

  $('#destination_input').on('input', function () {
    ensureDestinationDropdown();
    const maybePersian = toPersianGuess($(this).val());
    const inputText = normalize(maybePersian);
    if (inputText === "") {
      AddResultLocations_destination(_destinations);
    } else {
      const filteredCities = _destinations.filter(city => normalize(city).includes(inputText));
      AddResultLocations_destination(filteredCities);
    }
  });

  $(document).on('click', '#od-swap-btn', function (e) {
    e.preventDefault();
    e.stopPropagation();
    const $btn = $(this);
    $btn.addClass('is-swapping');
    window.setTimeout(() => $btn.removeClass('is-swapping'), 380);
    SwapOriginDestination();
  });

  // Submit: AJAX trips + refresh SEO block for the selected OD
  $('#tripForm').on('submit', async function (e) {
    const inTaxiTripsPage = $('.trips-container').length > 0;
    if (!inTaxiTripsPage) return true;
    e.preventDefault();

    const $c = $('.trips-container');
    if ($c.length) {
      $c.empty().append(`<div class="d-flex justify-content-center align-items-center mt-5 pt-3">
        <div class="sk-chase sk-primary">
          <div class="sk-chase-dot"></div><div class="sk-chase-dot"></div><div class="sk-chase-dot"></div><div class="sk-chase-dot"></div><div class="sk-chase-dot"></div><div class="sk-chase-dot"></div>
        </div>
        <label class="fw-bold fs-5 ms-3">در حال بارگزاری سفر ها</label>
      </div>`);
    }
    await FetchTrips();
    return false;
  });
});
