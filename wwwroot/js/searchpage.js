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
  if (!supportedKeys || supportedKeys.size === 0) return;
  directions = directions.filter(d => supportedKeys.has(d.Cityone) && supportedKeys.has(d.Citytwo));
  const cities = new Set();
  directions.forEach(d => { cities.add(d.Cityone); cities.add(d.Citytwo); });
  originKeys = Array.from(cities);
}

async function FetchTrips() {
  const origin = ($('#origin_input').val() || '').trim();
  const destination = ($('#destination_input').val() || '').trim();
  const searchdate = ($('#starttime').val() || '').trim();

  if (!origin || !destination || !searchdate) {
    trips = [];
    // If renderTrips exists (Trips.js), clear cards
    if (typeof renderTrips === 'function') renderTrips(trips);
    return;
  }

  const oKey = normalize(toPersianGuess(origin));
  const dKey = normalize(toPersianGuess(destination));
  const isOriginValid = originKeys.includes(oKey) && (!supportedKeys.size || supportedKeys.has(oKey));
  const isDestValid = originKeys.includes(dKey) && (!supportedKeys.size || supportedKeys.has(dKey));
  if (!isOriginValid || !isDestValid) {
    const msg = !isOriginValid ? `شهر مبدا نامعتبر است: ${origin}` : `شهر مقصد نامعتبر است: ${destination}`;
    // Show error in trips-container instead of dropdown list
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
    // Use existing rendering pipeline if available
    if (typeof renderTrips === 'function') {
      renderTrips(trips);
      if (typeof GetCarModels === 'function' && typeof GenerateCarModelsFilter === 'function') {
        $('#carmodelsfilter').find('.form-check').not(':first').remove(); // remove previous dynamic filters (keep "همه")
        const carModels = GetCarModels(trips);
        GenerateCarModelsFilter(carModels);
      }
    }
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
  $('#origin_input').val(city).attr("value", id);
  $('#destination_input').val('').attr('value', '');
  EnableDestination();
  SetDestinations(city);
}

function DestSelected(id, name) { $('#destination_input').val(name).attr("value", id); }

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

  // Submit behavior: if on TaxiTrips page, do AJAX search & render cards, else allow navigation
  $('#tripForm').on('submit', async function (e) {
    const inTaxiTripsPage = $('.trips-container').length > 0;
    if (!inTaxiTripsPage) return true;
    e.preventDefault();
    // show loading spinner if exists
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
