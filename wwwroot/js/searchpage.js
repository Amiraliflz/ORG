// Enhanced search functionality with normalization, dynamic origin/destination lists
// Keeps existing trip card rendering logic (Trips.js) unchanged.
// Origin/Destination UX: advanced matching, Persian guesses, supported city intersection.

let directions = [];
let originKeys = []; // normalized city keys (after intersecting with supported)
let supportedKeys = new Set(); // normalized keys supported by server (DirectionsRepository)
let displayNameByKey = new Map(); // normalized -> display name (prefer Persian)
let _destinations = []; // destination display names
let trips = []; // will be reused by Trips.js
let activeOriginKey = ''; // exact typed/clicked origin currently driving destination choices

// Decode literal \uXXXX sequences into real characters (handles double-escaped payloads)
function decodeUnicodeEscapes(str) {
  if (typeof str !== 'string') return str;
  if (!/\\u[0-9a-fA-F]{4}/.test(str)) return str;
  try {
    return str.replace(/\\u([0-9a-fA-F]{4})/g, (_, g1) => String.fromCharCode(parseInt(g1, 16)));
  } catch { return str; }
}

// Latin spellings → Persian city names (prefix-matched while typing)
const LATIN_TO_CITY = {
  tehran: 'تهران', teh: 'تهران', teheran: 'تهران',
  isfahan: 'اصفهان', esfahan: 'اصفهان', esfehan: 'اصفهان',
  rasht: 'رشت',
  chalus: 'چالوس', chaloos: 'چالوس', chalous: 'چالوس',
  kermanshah: 'کرمانشاه',
  noushahr: 'نوشهر', nowshahr: 'نوشهر', noshahr: 'نوشهر',
  tabriz: 'تبریز',
  qom: 'قم', ghom: 'قم',
  hamedan: 'همدان', hamadan: 'همدان',
  sari: 'ساری',
  shiraz: 'شیراز',
  mashhad: 'مشهد', mashad: 'مشهد',
  karaj: 'کرج',
  qazvin: 'قزوین', ghazvin: 'قزوین',
  kerman: 'کرمان',
  yazd: 'یزد',
  gorgan: 'گرگان',
  zanjan: 'زنجان',
  kashan: 'کاشان',
  sanandaj: 'سنندج',
  shahrekord: 'شهرکرد', 'shahr-e-kord': 'شهرکرد',
  lahijan: 'لاهیجان',
  ramsar: 'رامسر'
};

// Windows Persian (ISIRI) layout: typing تهران with an English keyboard produces jivhk
const QWERTY_TO_FA = {
  q: 'ض', w: 'ص', e: 'ث', r: 'ق', t: 'ف', y: 'غ', u: 'ع', i: 'ه', o: 'خ', p: 'ح',
  '[': 'ج', ']': 'چ',
  a: 'ش', s: 'س', d: 'ی', f: 'ب', g: 'ل', h: 'ا', j: 'ت', k: 'ن', l: 'م',
  ';': 'ک', "'": 'گ',
  z: 'ظ', x: 'ط', c: 'ز', v: 'ر', b: 'ذ', n: 'د', m: 'پ',
  ',': 'و', '`': 'پ', '\\': 'پ'
};

function looksLatin(str) {
  const s = str || '';
  if (!s || /[\u0600-\u06FF]/.test(s)) return false;
  return /[A-Za-z;'[\]`,\\]/.test(s);
}

function latinKeyboardToPersian(str) {
  if (!looksLatin(str)) return str;
  let out = '';
  let converted = 0;
  for (const ch of str) {
    const mapped = QWERTY_TO_FA[ch.toLowerCase()];
    if (mapped) {
      out += mapped;
      converted++;
    } else {
      out += ch;
    }
  }
  return converted ? out : str;
}

function toPersianGuess(str) {
  const trimmed = (str || '').trim();
  if (!trimmed) return str;
  const fromAlias = LATIN_TO_CITY[trimmed.toLowerCase()];
  if (fromAlias) return fromAlias;
  const fromKeys = latinKeyboardToPersian(trimmed);
  if (fromKeys !== trimmed) return fromKeys;
  return str;
}

function queryNeedles(raw) {
  const needles = [];
  const add = (value) => {
    const n = normalize(value);
    if (n && !needles.includes(n)) needles.push(n);
  };
  add(raw);
  add(toPersianGuess(raw));
  add(latinKeyboardToPersian(raw));
  const lower = (raw || '').trim().toLowerCase();
  if (lower.length >= 2 && isAscii(lower)) {
    Object.keys(LATIN_TO_CITY).forEach((alias) => {
      if (alias.startsWith(lower)) add(LATIN_TO_CITY[alias]);
    });
  }
  return needles;
}

function keysMatchingNeedles(keys, needles) {
  if (!needles.length) return [];
  return keys.filter((key) => needles.some((n) => key.includes(n)));
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

function isMobileCityPicker() {
  return !!(document.getElementById('tripForm') && window.matchMedia('(max-width: 767.98px)').matches);
}

let desktopPickerModeActive = null;

function cityPickerDropdown(input) {
  if (!input || typeof bootstrap === 'undefined') return null;
  return bootstrap.Dropdown.getOrCreateInstance(input, { autoClose: 'outside', offset: [0, 8] });
}

function syncCityPickerDropdownMode() {
  const mobile = isMobileCityPicker();
  if (desktopPickerModeActive === !mobile) return;
  desktopPickerModeActive = !mobile;

  ['origin_input', 'destination_input'].forEach(function (id) {
    const el = document.getElementById(id);
    if (!el) return;
    const inst = bootstrap.Dropdown.getInstance(el);
    if (inst) inst.dispose();
    if (mobile) {
      el.setAttribute('data-bs-toggle', 'dropdown');
      el.setAttribute('data-bs-offset', '0,8');
    } else {
      el.removeAttribute('data-bs-toggle');
      cityPickerDropdown(el);
    }
  });
}

function openDesktopCityPicker(input) {
  if (!input || isMobileCityPicker()) return;

  const menu = input.parentElement && input.parentElement.querySelector('.dropdown-menu');
  if (menu && menu.classList.contains('show')) return;

  const otherInput = input.id === 'origin_input'
    ? document.getElementById('destination_input')
    : document.getElementById('origin_input');
  if (otherInput) {
    const otherMenu = otherInput.parentElement && otherInput.parentElement.querySelector('.dropdown-menu');
    if (otherMenu && otherMenu.classList.contains('show')) {
      const otherInst = bootstrap.Dropdown.getInstance(otherInput);
      if (otherInst) otherInst.hide();
    }
  }

  try {
    cityPickerDropdown(input).show();
  } catch { /* Bootstrap initializes on first interaction */ }
}

function closeCityPicker(input) {
  if (!input) return;
  try {
    const inst = typeof bootstrap !== 'undefined' ? bootstrap.Dropdown.getInstance(input) : null;
    if (inst) inst.hide();
    else $(input).dropdown('hide');
  } catch { /* already closed */ }
}

function openCityPicker(input, force) {
  if (!input) return;
  if (isMobileCityPicker()) {
    try {
      cityPickerDropdown(input).show();
    } catch {
      try { $(input).dropdown('show'); } catch { /* Bootstrap initializes on first interaction */ }
    }
    return;
  }
  if (!force) {
    openDesktopCityPicker(input);
    return;
  }
  const otherInput = input.id === 'origin_input'
    ? document.getElementById('destination_input')
    : document.getElementById('origin_input');
  if (otherInput) closeCityPicker(otherInput);
  try {
    cityPickerDropdown(input).show();
  } catch { /* Bootstrap initializes on first interaction */ }
}

function showFocusedDesktopPicker(input) {
  openDesktopCityPicker(input);
}

function cityPickerContext(target) {
  if (!target) return null;
  const isOrigin = target.id === 'origin_input' || target.id === 'origin_picker_q';
  const isDestination = target.id === 'destination_input' || target.id === 'dest_picker_q';
  if (!isOrigin && !isDestination) return null;

  const toggle = document.getElementById(isOrigin ? 'origin_input' : 'destination_input');
  if (!toggle) return null;
  const menuClass = isOrigin ? 'origin_location' : 'destination_location';
  const menu = (toggle.parentElement && toggle.parentElement.querySelector('.' + menuClass))
    || document.querySelector('body > .' + menuClass + '[data-city-picker-host="' + toggle.id + '"]');
  return { toggle, menu };
}

function clearCityPickerKeyboardSelection(toggle) {
  if (!toggle) return;
  toggle.removeAttribute('aria-activedescendant');
  const context = cityPickerContext(toggle);
  if (!context || !context.menu) return;
  context.menu.querySelectorAll('[role="option"]').forEach(function (option) {
    option.classList.remove('active');
    option.setAttribute('aria-selected', 'false');
  });
}

function moveCityPickerSelection(target, key) {
  const context = cityPickerContext(target);
  if (!context || !context.menu) return false;
  try { openDesktopCityPicker(context.toggle); } catch { /* already open */ }

  const options = Array.from(context.menu.querySelectorAll('[role="option"]'))
    .filter(option => !option.classList.contains('disabled') && !option.hasAttribute('disabled'));
  if (!options.length) return false;

  let currentIndex = options.findIndex(option => option.classList.contains('active'));
  let nextIndex;
  if (key === 'Home') nextIndex = 0;
  else if (key === 'End') nextIndex = options.length - 1;
  else if (key === 'ArrowDown') nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % options.length;
  else nextIndex = currentIndex < 0 ? options.length - 1 : (currentIndex - 1 + options.length) % options.length;

  options.forEach(function (option, index) {
    const selected = index === nextIndex;
    option.classList.toggle('active', selected);
    option.setAttribute('aria-selected', selected ? 'true' : 'false');
  });
  context.toggle.setAttribute('aria-activedescendant', options[nextIndex].id);
  options[nextIndex].scrollIntoView({ block: 'nearest' });
  return true;
}

function selectActiveCityPickerOption(target) {
  const context = cityPickerContext(target);
  if (!context || !context.menu) return false;
  const activeOption = context.menu.querySelector('[role="option"].active');
  if (!activeOption) return false;
  activeOption.click();
  clearCityPickerKeyboardSelection(context.toggle);
  return true;
}

function pickerChrome(title, inputId, placeholder, iconClass) {
  return `
      <div class="city-picker__head">
        <span class="city-picker__title">
          <i class="ti ${iconClass} city-picker__context-icon" aria-hidden="true"></i>
          <span>${title}</span>
        </span>
        <button type="button" class="city-picker__close" aria-label="بستن">
          <i class="ti ti-x" aria-hidden="true"></i>
        </button>
      </div>
      <div class="city-picker__search">
        <i class="ti ti-search" aria-hidden="true"></i>
        <input type="text" class="form-control city-picker__input" id="${inputId}" placeholder="${placeholder}" autocomplete="off" />
      </div>`;
}

function ensureOriginDropdown() {
  const spanElement = $('.origin_location');
  spanElement.attr({ id: 'origin_city_listbox', role: 'listbox' });
  $('#origin_input').attr('aria-controls', 'origin_city_listbox');
  if ($('#origincontainer').length === 0) {
    spanElement.html(`
      <div class="city-picker">
        ${pickerChrome('انتخاب مبدا', 'origin_picker_q', 'جستجوی شهر مبدا', 'ti-location')}
        <div class="staredlocations">
          <label class="staredlocation_title ms-2 mt-2 text-muted pb-1" id="origin_most_lable">
            <i class="ti ti-map-pin-star icon locationicon p-1 pe-0"></i>
            شهرهای پرتردد
          </label>
          <div class="px-1 terminals_container_orig" id="origincontainer"></div>
        </div>
      </div>`);
  }
}

function ensureDestinationDropdown() {
  const spanElement = $('.dropdown-menu.destination_location');
  spanElement.attr({ id: 'destination_city_listbox', role: 'listbox' });
  $('#destination_input').attr('aria-controls', 'destination_city_listbox');
  if ($('#desticontainer').length === 0) {
    spanElement.html(`
      <div class="city-picker">
        ${pickerChrome('انتخاب مقصد', 'dest_picker_q', 'جستجوی شهر مقصد', 'ti-map-pin')}
        <div class="staredlocations">
          <label class="staredlocation_title ms-2 mt-2 text-muted pb-1">
            <i class="ti ti-map-pin-star icon locationicon p-1 pe-0"></i>
            مقصد ها
          </label>
          <div class="px-1 terminals_container_desti" id="desticontainer"></div>
        </div>
      </div>`);
  }
}

let cityPickerPushed = false;

function pushCityPickerHistory() {
  if (cityPickerPushed) return;
  cityPickerPushed = true;
  try { history.pushState({ mrCityPicker: true }, ''); } catch { /* ignore */ }
}

function consumeCityPickerHistory() {
  if (!cityPickerPushed) return;
  cityPickerPushed = false;
  try {
    if (history.state && history.state.mrCityPicker) history.back();
  } catch { /* ignore */ }
}

function syncCityPickerLock() {
  const open = isMobileCityPicker() && (
    document.querySelector('.dropdown-menu.origin_location.show, .dropdown-menu.destination_location.show') ||
    document.querySelector('body > .dropdown-menu.origin_location, body > .dropdown-menu.destination_location')
  );
  document.body.classList.toggle('city-picker-open', !!open);
}

const DIRECTIONS_STORAGE_KEY = 'mrshoofer_available_directions_v2';
const DIRECTIONS_CACHE_TTL_MS = 60 * 60 * 1000; // 1 hour
let directionsReady = false;
let destinationUnlocked = false;

function applyDirectionsPayload(data) {
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
  // Direction pairs are ordered: Cityone is an origin and Citytwo is a destination.
  // A city that only appears as Citytwo must not be offered as an origin.
  originKeys = Array.from(new Set(directions.map(d => d.Cityone)));
  directionsReady = originKeys.length > 0;
}

function readCachedDirections() {
  try {
    const raw = sessionStorage.getItem(DIRECTIONS_STORAGE_KEY);
    if (!raw) return null;
    const cached = JSON.parse(raw);
    if (!cached || !Array.isArray(cached.data) || !cached.ts) return null;
    if (Date.now() - cached.ts > DIRECTIONS_CACHE_TTL_MS) return null;
    return cached.data;
  } catch {
    return null;
  }
}

function writeCachedDirections(data) {
  try {
    sessionStorage.setItem(DIRECTIONS_STORAGE_KEY, JSON.stringify({ ts: Date.now(), data }));
  } catch { /* quota / private mode */ }
}

function showPickerStatus(containerSelector, message) {
  const $c = $(containerSelector);
  if (!$c.length) return;
  $c.empty().append($('<a>', {
    class: 'dropdown-item text-center mt-2 text-muted',
    text: message,
    href: 'javascript:void(0)'
  }));
}

function FetchDirections() {
  return new Promise((resolve, reject) => {
    const cached = readCachedDirections();
    if (cached && cached.length) {
      applyDirectionsPayload(cached);
      resolve();
      // Soft refresh in background — do not block UI
      $.getJSON('/TaxiTrips/AvailableDirections')
        .done(function (data) {
          applyDirectionsPayload(data);
          writeCachedDirections(data);
          const typedOrigin = ($('#origin_input').val() || '').trim();
          const typedOriginKey = normalize(typedOrigin);
          if (typedOriginKey && originKeys.includes(typedOriginKey)) {
            // Refresh choices from the new server catalog without changing either input.
            activeOriginKey = typedOriginKey;
            SetDestinations(typedOriginKey);
            EnableDestination();
          } else if (!typedOrigin) {
            LoadMostUsedOrigins();
          }
        })
        .fail(function () { /* keep cache */ });
      return;
    }

    $.getJSON('/TaxiTrips/AvailableDirections', function (data) {
      applyDirectionsPayload(data);
      writeCachedDirections(data);
      resolve();
    }).fail(function (xhr) {
      console.error('Failed to fetch available directions.', xhr?.status, xhr?.responseText);
      reject('Error fetching available directions');
    });
  });
}

var most_used_origins = [];
const SEARCH_HINTS_STORAGE_KEY = 'mrshoofer_search_hints_v1';

function applySearchHints(data) {
  if (Array.isArray(data.supportedCities)) {
    supportedKeys = new Set(data.supportedCities.map(c => normalize(c)));
  }
  if (Array.isArray(data.popularOrigins) && data.popularOrigins.length) {
    most_used_origins = data.popularOrigins.slice();
  }
}

function readCachedSearchHints(version) {
  try {
    const raw = sessionStorage.getItem(SEARCH_HINTS_STORAGE_KEY);
    if (!raw) return null;
    const cached = JSON.parse(raw);
    if (!cached || cached.version !== version) return null;
    return cached;
  } catch {
    return null;
  }
}

function FetchSearchHints() {
  return new Promise((resolve) => {
    $.getJSON('/TaxiTrips/SearchHints', function (data) {
      const version = data?.version || '';
      const cached = readCachedSearchHints(version);
      if (cached) {
        applySearchHints(cached);
        resolve();
        return;
      }
      try {
        sessionStorage.setItem(SEARCH_HINTS_STORAGE_KEY, JSON.stringify(data));
      } catch { /* quota / private mode */ }
      applySearchHints(data);
      resolve();
    }).fail(function (xhr) {
      console.error('Failed to fetch search hints.', xhr?.status, xhr?.responseText);
      most_used_origins = ["تهران", "اصفهان", "رشت", "چالوس", "کرمانشاه", "نوشهر"];
      supportedKeys = new Set();
      resolve();
    });
  });
}

function FetchSupportedCities() {
  return FetchSearchHints();
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
  const isDirectionValid = directions.length === 0 ||
    directions.some(direction => direction.Cityone === oKey && direction.Citytwo === dKey);
  if (!isOriginValid || !isDirectionValid) {
    const msg = !isOriginValid
      ? `شهر مبدا نامعتبر است: ${origin}`
      : `مسیر ${origin} به ${destination} در حال حاضر فعال نیست`;
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

function keyToDisplay(key) { return displayNameByKey.get(key) || toPersianGuess(key) || key; }

function SetDestinations(originDisplay) {
  ensureDestinationDropdown();
  const selectedKey = normalize(originDisplay);
  const destinationsKeys = directions
    .filter(item => item.Cityone === selectedKey)
    .map(item => item.Citytwo);
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
  clearCityPickerKeyboardSelection(document.getElementById('origin_input'));
  terminals_container.empty();
  if (!keys || keys.length === 0) {
    terminals_container.append($('<div>', { class: 'dropdown-item text-center mt-2 text-muted', role: 'status', text: "نتیجه‌ای پیدا نشد" }));
  } else {
    keys.forEach((key, index) => {
      const display = keyToDisplay(key);
      var $aTag = $('<button>', {
        id: 'origin_city_option_' + index,
        type: 'button',
        class: 'dropdown-item',
        role: 'option',
        tabindex: '-1',
        'aria-selected': 'false',
        text: display
      });
      $aTag.on('mousedown', function (e) {
        e.preventDefault();
      });
      $aTag.on('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        OriginSelected(0, display);
      });
      terminals_container.append($aTag);
    });
  }
}

function AddResultLocations_destination(result_locations) {
  ensureDestinationDropdown();
  var terminals_container = $('#desticontainer');
  clearCityPickerKeyboardSelection(document.getElementById('destination_input'));
  terminals_container.empty();
  if (!result_locations || result_locations.length === 0) {
    terminals_container.append($('<div>', { class: 'dropdown-item text-center mt-2 text-muted', role: 'status', text: "ابتدا شهر مبدا را انتخاب کنید" }));
    return;
  }
  result_locations.forEach((location, index) => {
    var $aTag = $('<button>', {
      id: 'destination_city_option_' + index,
      type: 'button',
      class: 'dropdown-item',
      role: 'option',
      tabindex: '-1',
      'aria-selected': 'false',
      text: location
    });
    $aTag.on('mousedown', function (e) {
      e.preventDefault();
    });
    $aTag.on('click', function (e) {
      e.preventDefault();
      e.stopPropagation();
      DestSelected(0, location);
    });
    terminals_container.append($aTag);
  });
}

function focusDestinationPicker() {
  const destInput = document.getElementById('destination_input');
  if (!destInput || !destinationUnlocked) return;
  destInput.focus();
  openCityPicker(destInput, true);
}

function OriginSelected(id, name) {
  const city = (name || '').trim();
  // Always store the city name in both property and attribute (never the numeric id)
  $('#origin_input').val(city).attr('value', city);
  $('#origin_picker_q').val(city);
  $('#destination_input').val('').attr('value', '');
  $('#dest_picker_q').val('');
  activeOriginKey = normalize(city);
  EnableDestination();
  SetDestinations(city);

  const originInput = document.getElementById('origin_input');
  const originMenu = originInput && originInput.parentElement && originInput.parentElement.querySelector('.dropdown-menu');
  const originWasOpen = !!(originMenu && originMenu.classList.contains('show'));

  if (originWasOpen && originInput) {
    const onOriginHidden = function () {
      originInput.removeEventListener('hidden.bs.dropdown', onOriginHidden);
      focusDestinationPicker();
    };
    originInput.addEventListener('hidden.bs.dropdown', onOriginHidden);
    closeCityPicker(originInput);
    window.setTimeout(function () {
      originInput.removeEventListener('hidden.bs.dropdown', onOriginHidden);
      if (!originMenu.classList.contains('show')) focusDestinationPicker();
    }, isMobileCityPicker() ? 120 : 60);
    return;
  }

  window.setTimeout(focusDestinationPicker, isMobileCityPicker() ? 80 : 0);
}

function focusNextSearchField() {
  if (isMobileCityPicker()) return;
  const dateInput = document.getElementById('starttime');
  if (!dateInput || dateInput.disabled) return;
  window.setTimeout(function () {
    dateInput.focus();
  }, 0);
}

function DestSelected(id, name) {
  const city = (name || '').trim();
  $('#destination_input').val(city).attr('value', city);
  $('#dest_picker_q').val(city);

  const destInput = document.getElementById('destination_input');
  closeCityPicker(destInput);
  focusNextSearchField();
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
    activeOriginKey = '';
    LoadMostUsedOrigins();
    _destinations = [];
    AddResultLocations_destination([]);
    DisableDestination();
    return;
  }

  if (!originKeys.includes(originKey)) {
    activeOriginKey = '';
    _destinations = [];
    AddResultLocations_destination([]);
    DisableDestination();
    return;
  }

  const originDisplay = keyToDisplay(originKey);
  activeOriginKey = originKey;
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

function DisableDestination() {
  destinationUnlocked = false;
  // Keep dropdown openable so users never hit a "dead" field — list explains next step.
  $("#destination_input").prop("disabled", false);
  syncCityPickerDropdownMode();
  AddResultLocations_destination([]);
}
function EnableDestination() {
  destinationUnlocked = true;
  $("#destination_input").prop("disabled", false);
  syncCityPickerDropdownMode();
}

function applyOdUiAfterLoad() {
  var origin_value_raw = ($('#origin_input').val() || '').trim();
  var origin_value = normalize(origin_value_raw);

  if (origin_value && originKeys.includes(origin_value)) {
    activeOriginKey = origin_value;
    LoadMostUsedOrigins();
    SetDestinations(origin_value_raw);
    EnableDestination();
  } else if (originKeys.length) {
    activeOriginKey = '';
    LoadMostUsedOrigins();
    AddResultLocations_destination([]);
    DisableDestination();
  } else {
    showPickerStatus('#origincontainer', 'شهرها در دسترس نیستند — لطفا دوباره تلاش کنید');
    showPickerStatus('#desticontainer', 'ابتدا شهر مبدا را انتخاب کنید');
    DisableDestination();
  }
}

$(document).ready(async function () {
  // Build picker chrome immediately so Bootstrap can open menus before the API returns.
  ensureOriginDropdown();
  ensureDestinationDropdown();
  showPickerStatus('#origincontainer', 'در حال بارگذاری شهرها…');
  showPickerStatus('#desticontainer', 'ابتدا شهر مبدا را انتخاب کنید');
  DisableDestination();

  const loadCatalog = (async function () {
    try {
      await Promise.all([FetchDirections(), FetchSupportedCities()]);
      intersectDirectionsWithSupported();
      applyOdUiAfterLoad();
      if (destinationUnlocked && (($('#destination_input').val() || '').trim())) {
        await FetchTrips();
      }
    } catch (error) {
      console.error('An error occurred:', error);
      most_used_origins = most_used_origins.length
        ? most_used_origins
        : ["تهران", "اصفهان", "رشت", "چالوس", "کرمانشاه", "نوشهر"];
      if (!originKeys.length && most_used_origins.length) {
        most_used_origins.forEach((c) => {
          const key = normalize(c);
          displayNameByKey.set(key, c);
          originKeys.push(key);
        });
      }
      applyOdUiAfterLoad();
      if (!directionsReady) {
        showPickerStatus('#origincontainer', 'خطا در بارگذاری — دوباره لمس کنید');
      }
    }
  })();

  syncCityPickerDropdownMode();
  window.addEventListener('resize', function () {
    window.clearTimeout(syncCityPickerDropdownMode._t);
    syncCityPickerDropdownMode._t = window.setTimeout(syncCityPickerDropdownMode, 150);
  });

  document.addEventListener('focus', function (event) {
    if (event.target.matches('#origin_input, #destination_input')) {
      openDesktopCityPicker(event.target);
    }
  }, true);

  // Block Bootstrap's delegated click-toggle on desktop (data-bs-toggle is removed, but guard anyway).
  document.addEventListener('pointerdown', function (event) {
    if (isMobileCityPicker()) return;
    if (!event.target.matches('#origin_input, #destination_input')) return;
    event.stopImmediatePropagation();
    openDesktopCityPicker(event.target);
  }, true);

  document.addEventListener('keydown', function (event) {
    if (!event.target.matches('#origin_input, #destination_input, #origin_picker_q, #dest_picker_q')) return;

    if (event.key === 'Tab') {
      const nextToggle = !event.shiftKey && event.target.id === 'origin_input'
        ? document.getElementById('destination_input')
        : event.shiftKey && event.target.id === 'destination_input'
          ? document.getElementById('origin_input')
          : null;
      if (nextToggle) {
        event.preventDefault();
        event.stopPropagation();
        clearCityPickerKeyboardSelection(event.target);
        nextToggle.focus();
        showFocusedDesktopPicker(nextToggle);
      }
      return;
    }

    if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      if (moveCityPickerSelection(event.target, event.key)) {
        event.preventDefault();
        event.stopPropagation();
      }
      return;
    }

    if (event.key === 'Enter' && selectActiveCityPickerOption(event.target)) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }

    if (event.key === 'Escape') {
      const context = cityPickerContext(event.target);
      if (context && context.menu && context.menu.classList.contains('show')) {
        event.preventDefault();
        event.stopPropagation();
        clearCityPickerKeyboardSelection(context.toggle);
        try { $(context.toggle).dropdown('hide'); } catch { /* already closed */ }
      }
    }
  }, true);

  $('#origin_input').on('input', function () {
    ensureOriginDropdown();
    showFocusedDesktopPicker(this);
    if (!directionsReady && !originKeys.length) {
      showPickerStatus('#origincontainer', 'در حال بارگذاری شهرها…');
      return;
    }
    const raw = (($(this).val() || ''));
    const needles = queryNeedles(raw);
    if (!normalize(raw)) {
      activeOriginKey = '';
      $('#destination_input').val('');
      _destinations = [];
      AddResultLocations_destination([]);
      LoadMostUsedOrigins();
      DisableDestination();
      return;
    }
    $('#origin_most_lable').css('display', 'none');
    const matches = keysMatchingNeedles(originKeys, needles);
    const listToShow = raw.length < 2 ? originKeys : matches;
    AddResultLocations_origin(listToShow);
    const exactKey = originKeys.find((key) => key === normalize(raw));
    if (exactKey) {
      // Exact text unlocks its destinations, but never rewrites the user's input.
      // Only an explicit dropdown click commits/canonicalizes the displayed city.
      if (activeOriginKey !== exactKey) {
        activeOriginKey = exactKey;
        $('#destination_input').val('').attr('value', '');
        $('#dest_picker_q').val('');
      }
      SetDestinations(exactKey);
      EnableDestination();
    } else {
      activeOriginKey = '';
      _destinations = [];
      AddResultLocations_destination([]);
      DisableDestination();
    }
  });

  $('#destination_input').on('input', function () {
    ensureDestinationDropdown();
    showFocusedDesktopPicker(this);
    if (!destinationUnlocked) {
      AddResultLocations_destination([]);
      return;
    }
    const raw = $(this).val() || '';
    const needles = queryNeedles(raw);
    if (!normalize(raw)) {
      AddResultLocations_destination(_destinations);
    } else {
      const destKeys = _destinations.map((city) => normalize(city));
      const matchedKeys = new Set(keysMatchingNeedles(destKeys, needles));
      const filteredCities = _destinations.filter((city) => matchedKeys.has(normalize(city)));
      AddResultLocations_destination(filteredCities);
    }
  });

  $(document).on('input', '#origin_picker_q', function () {
    $('#origin_input').val($(this).val()).trigger('input');
  });
  $(document).on('input', '#dest_picker_q', function () {
    $('#destination_input').val($(this).val()).trigger('input');
  });
  $(document).on('click', '.city-picker__close', function (e) {
    e.preventDefault();
    e.stopPropagation();
    const $menu = $(this).closest('.dropdown-menu');
    const hostId = $menu.attr('data-city-picker-host');
    const $toggle = hostId
      ? $('#' + hostId)
      : $menu.closest('.input-container').find('input.dropdown-toggle');
    if ($toggle.length) $toggle.dropdown('hide');
  });

  function portalCityPickerMenu(toggleEl) {
    if (!isMobileCityPicker() || !toggleEl) return;
    const menu = toggleEl.parentElement && toggleEl.parentElement.querySelector('.dropdown-menu');
    if (!menu || menu.parentElement === document.body) return;
    menu.setAttribute('data-city-picker-host', toggleEl.id || '');
    menu._cityPickerHome = toggleEl.parentElement;
    document.body.appendChild(menu);
    menu.classList.add('show');
    menu.style.position = 'fixed';
    menu.style.inset = '0';
    menu.style.top = '0';
    menu.style.right = '0';
    menu.style.bottom = '0';
    menu.style.left = '0';
    menu.style.transform = 'none';
    menu.style.width = '100vw';
    menu.style.height = '100dvh';
    menu.style.maxHeight = '100dvh';
    menu.style.zIndex = '1080';
    menu.style.margin = '0';
  }

  function unportalCityPickerMenu(toggleEl) {
    const hostId = toggleEl && toggleEl.id;
    let menu = hostId
      ? document.querySelector('body > .dropdown-menu[data-city-picker-host="' + hostId + '"]')
      : null;
    if (!menu && toggleEl && toggleEl.parentElement) {
      menu = toggleEl.parentElement.querySelector('.dropdown-menu');
    }
    if (!menu) return;
    const home = menu._cityPickerHome;
    menu.style.cssText = '';
    menu.removeAttribute('data-city-picker-host');
    delete menu._cityPickerHome;
    if (home && menu.parentElement === document.body) {
      home.appendChild(menu);
    }
  }

  $('#origin_input, #destination_input').on('show.bs.dropdown', function (e) {
    if (this.id === 'destination_input' && !destinationUnlocked) {
      ensureDestinationDropdown();
      AddResultLocations_destination([]);
    }
    if (this.id === 'origin_input' && !directionsReady && !originKeys.length) {
      ensureOriginDropdown();
      showPickerStatus('#origincontainer', 'در حال بارگذاری شهرها…');
      loadCatalog.catch(function () { /* handled above */ });
    }
    if (!isMobileCityPicker()) return;
    this.setAttribute('data-bs-display', 'static');
  });
  $('#origin_input').on('shown.bs.dropdown', function () {
    if (!isMobileCityPicker()) return;
    portalCityPickerMenu(this);
    const $q = $('#origin_picker_q');
    $q.val($(this).val() || '');
    window.setTimeout(() => $q.trigger('focus'), 50);
    pushCityPickerHistory();
    syncCityPickerLock();
  });
  $('#destination_input').on('shown.bs.dropdown', function () {
    if (!isMobileCityPicker()) return;
    portalCityPickerMenu(this);
    const $q = $('#dest_picker_q');
    $q.val($(this).val() || '');
    window.setTimeout(() => $q.trigger('focus'), 50);
    pushCityPickerHistory();
    syncCityPickerLock();
  });
  $('#origin_input, #destination_input').on('hidden.bs.dropdown', function () {
    clearCityPickerKeyboardSelection(this);
    unportalCityPickerMenu(this);
    syncCityPickerLock();
    consumeCityPickerHistory();
  });
  window.addEventListener('popstate', function () {
    if (!document.body.classList.contains('city-picker-open')) return;
    cityPickerPushed = false;
    $('#origin_input, #destination_input').dropdown('hide');
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
    await loadCatalog;
    await FetchTrips();
    return false;
  });
});
