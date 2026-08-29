/**
 * JalaliDatepicker — multi-month scrollable Jalali calendar popup.
 * Requires window.JDate to be loaded (but NOT set as window.Date).
 *
 * Usage:
 *   new JalaliDatepicker(inputEl, { minDate: 'today', onSelect: fn })
 */
;(function (root) {
  'use strict';

  var MONTHS = [
    'فروردین','اردیبهشت','خرداد','تیر','مرداد','شهریور',
    'مهر','آبان','آذر','دی','بهمن','اسفند'
  ];
  var WEEKDAYS = ['ش','ی','د','س','چ','پ','ج'];
  var DAYS_IN_MONTH = [31,31,31,31,31,31,30,30,30,30,30,29];
  var MONTHS_VISIBLE = 2;
  var MAX_MONTHS_AHEAD = 12;

  var JD = root.JDate || root.Date;

  function addMonths(y, m, offset) {
    var total = y * 12 + m + offset;
    var ny = Math.floor(total / 12);
    var nm = total % 12;
    if (nm < 0) { nm += 12; ny -= 1; }
    return { y: ny, m: nm };
  }

  function toJalali(d) {
    var j = new JD(d instanceof JD ? +d : d);
    return { y: j.getFullYear(), m: j.getMonth(), d: j.getDate() };
  }

  function jalaliDayOfWeek(y, m, d) {
    var j = new JD(y, m, d);
    return j.getDay(); // 0=Sun
  }

  function isLeap(y) {
    var a = [1,5,9,13,17,22,26,30];
    var rem = y % 33; if (rem < 0) rem += 33;
    return a.indexOf(rem) !== -1;
  }

  function daysInMonth(y, m) {
    if (m === 11) return isLeap(y) ? 30 : 29;
    return DAYS_IN_MONTH[m];
  }

  function sameDay(a, b) {
    return a.y === b.y && a.m === b.m && a.d === b.d;
  }

  function beforeDay(a, b) {
    if (a.y !== b.y) return a.y < b.y;
    if (a.m !== b.m) return a.m < b.m;
    return a.d < b.d;
  }

  function formatJalali(y, m, d) {
    var mm = m + 1;
    return y + '/' + (mm < 10 ? '0' + mm : mm) + '/' + (d < 10 ? '0' + d : d);
  }

  function el(tag, cls, txt) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (txt != null) e.textContent = txt;
    return e;
  }

  function JalaliDatepicker(inputEl, opts) {
    opts = opts || {};
    this.input = typeof inputEl === 'string' ? document.querySelector(inputEl) : inputEl;
    this.onSelect = opts.onSelect || null;
    this.tooltipText = opts.tooltipText || '';
    this.selected = null;

    var today = toJalali(new JD());
    if (opts.minDate === 'today') {
      this.minDate = today;
    } else if (opts.minDate) {
      this.minDate = opts.minDate;
    } else {
      this.minDate = today;
    }
    this.today = today;
    this.viewOffset = 0;

    this._buildDOM();
    this._bindEvents();
  }

  JalaliDatepicker.prototype._buildDOM = function () {
    this.overlay = el('div', 'jdp-overlay');
    this.container = el('div', 'jdp-container');

    // top bar
    var topbar = el('div', 'jdp-topbar');
    this.closeBtn = el('button', 'jdp-close-btn', '✕');
    this.closeBtn.type = 'button';
    this.closeBtn.setAttribute('aria-label', 'بستن');

    var nav = el('div', 'jdp-topbar-nav');
    this.prevBtn = el('button', 'jdp-nav-btn jdp-nav-prev');
    this.prevBtn.type = 'button';
    this.prevBtn.setAttribute('aria-label', 'ماه‌های قبل');
    this.prevBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="m10 6-1.41 1.41L13.17 12l-4.58 4.59L10 18l6-6z"/></svg>';

    this.nextBtn = el('button', 'jdp-nav-btn jdp-nav-next');
    this.nextBtn.type = 'button';
    this.nextBtn.setAttribute('aria-label', 'ماه‌های بعد');
    this.nextBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z"/></svg>';

    this.rangeLabel = el('span', 'jdp-topbar-title', 'انتخاب تاریخ سفر');

    nav.appendChild(this.prevBtn);
    nav.appendChild(this.rangeLabel);
    nav.appendChild(this.nextBtn);

    topbar.appendChild(nav);
    topbar.appendChild(this.closeBtn);
    this.container.appendChild(topbar);

    // body
    this.body = el('div', 'jdp-body');
    this._renderMonths();
    this.container.appendChild(this.body);

    // footer
    var footer = el('div', 'jdp-footer');
    this.confirmBtn = el('button', 'jdp-confirm-btn', 'تأیید');
    this.confirmBtn.type = 'button';
    this.confirmBtn.disabled = true;
    footer.appendChild(this.confirmBtn);
    this.container.appendChild(footer);

    document.body.appendChild(this.overlay);
    document.body.appendChild(this.container);
  };

  JalaliDatepicker.prototype._monthRangeLabel = function () {
    var first = addMonths(this.today.y, this.today.m, this.viewOffset);
    var last = addMonths(first.y, first.m, MONTHS_VISIBLE - 1);
    if (first.y === last.y && first.m === last.m) {
      return MONTHS[first.m] + ' ' + first.y;
    }
    if (first.y === last.y) {
      return MONTHS[first.m] + ' – ' + MONTHS[last.m] + ' ' + first.y;
    }
    return MONTHS[first.m] + ' ' + first.y + ' – ' + MONTHS[last.m] + ' ' + last.y;
  };

  JalaliDatepicker.prototype._updateNav = function () {
    this.prevBtn.disabled = this.viewOffset <= 0;
    this.nextBtn.disabled = this.viewOffset >= MAX_MONTHS_AHEAD - (MONTHS_VISIBLE - 1);
    this.rangeLabel.textContent = this._monthRangeLabel();
  };

  JalaliDatepicker.prototype._renderMonths = function () {
    this.body.innerHTML = '';

    for (var i = 0; i < MONTHS_VISIBLE; i++) {
      var slot = addMonths(this.today.y, this.today.m, this.viewOffset + i);
      this._renderMonth(slot.y, slot.m);
    }

    this._updateNav();
  };

  JalaliDatepicker.prototype._shiftView = function (delta) {
    var next = this.viewOffset + delta;
    var maxOffset = MAX_MONTHS_AHEAD - (MONTHS_VISIBLE - 1);
    if (next < 0 || next > maxOffset) return;
    this.viewOffset = next;
    this._renderMonths();
  };

  JalaliDatepicker.prototype._renderMonth = function (y, m) {
    var monthEl = el('div', 'jdp-month');

    var header = el('div', 'jdp-month-header', MONTHS[m] + ' ' + y);
    monthEl.appendChild(header);

    var weekRow = el('div', 'jdp-weekdays');
    for (var w = 0; w < 7; w++) {
      weekRow.appendChild(el('div', 'jdp-weekday', WEEKDAYS[w]));
    }
    monthEl.appendChild(weekRow);

    var grid = el('div', 'jdp-days');
    var dim = daysInMonth(y, m);

    // first day of month — which column? Weekdays header is ش ی د س چ پ ج
    // Saturday=col0, Sunday=col1, ..., Friday=col6
    var dow = jalaliDayOfWeek(y, m, 1); // 0=Sun..6=Sat
    var col = (dow + 1) % 7; // Sat→0, Sun→1, Mon→2...Fri→6

    for (var e = 0; e < col; e++) {
      grid.appendChild(el('div', 'jdp-day-empty'));
    }

    for (var d = 1; d <= dim; d++) {
      var dayInfo = { y: y, m: m, d: d };
      var dayDow = (dow + d - 1) % 7; // native getDay for this day (approx)
      var realDow = jalaliDayOfWeek(y, m, d);
      var isFriday = realDow === 5;
      var isDisabled = beforeDay(dayInfo, this.minDate);
      var isToday = sameDay(dayInfo, this.today);
      var isSel = this.selected && sameDay(dayInfo, this.selected);

      var btn = el('button', 'jdp-day');
      btn.type = 'button';
      if (isFriday) btn.classList.add('jdp-friday');
      if (isDisabled) btn.classList.add('jdp-disabled');
      if (isToday) btn.classList.add('jdp-today');
      if (isSel) btn.classList.add('jdp-selected');
      btn.dataset.y = y;
      btn.dataset.m = m;
      btn.dataset.d = d;

      var inner = el('div', 'jdp-day-inner', String(d));
      btn.appendChild(inner);


      grid.appendChild(btn);
    }

    monthEl.appendChild(grid);
    this.body.appendChild(monthEl);
  };

  JalaliDatepicker.prototype._bindEvents = function () {
    var self = this;

    this.input.addEventListener('click', function (e) {
      e.preventDefault();
      self.open();
    });
    this.input.addEventListener('focus', function (e) {
      e.preventDefault();
      self.input.blur();
      self.open();
    });

    this.overlay.addEventListener('click', function () { self.close(); });
    this.closeBtn.addEventListener('click', function () { self.close(); });
    this.prevBtn.addEventListener('click', function () { self._shiftView(-1); });
    this.nextBtn.addEventListener('click', function () { self._shiftView(1); });

    this.body.addEventListener('click', function (e) {
      var btn = e.target.closest('.jdp-day');
      if (!btn || btn.classList.contains('jdp-disabled')) return;
      self.selected = {
        y: +btn.dataset.y,
        m: +btn.dataset.m,
        d: +btn.dataset.d
      };
      self._refreshSelection();
      self.confirmBtn.disabled = false;
    });

    this.confirmBtn.addEventListener('click', function () {
      if (!self.selected) return;
      var formatted = formatJalali(self.selected.y, self.selected.m, self.selected.d);
      self.input.value = formatted;
      if (self.onSelect) self.onSelect(self.selected, formatted);
      self.close();
    });
  };

  JalaliDatepicker.prototype._refreshSelection = function () {
    var all = this.body.querySelectorAll('.jdp-day');
    for (var i = 0; i < all.length; i++) {
      var b = all[i];
      var day = { y: +b.dataset.y, m: +b.dataset.m, d: +b.dataset.d };
      if (this.selected && sameDay(day, this.selected)) {
        b.classList.add('jdp-selected');
      } else {
        b.classList.remove('jdp-selected');
      }
    }
  };

  JalaliDatepicker.prototype.open = function () {
    this.viewOffset = 0;
    this._renderMonths();
    this.overlay.classList.add('jdp-open');
    this.container.classList.add('jdp-open');
    document.body.style.overflow = 'hidden';
    this.body.scrollTop = 0;
  };

  JalaliDatepicker.prototype.close = function () {
    this.overlay.classList.remove('jdp-open');
    this.container.classList.remove('jdp-open');
    document.body.style.overflow = '';
  };

  root.JalaliDatepicker = JalaliDatepicker;
})(window);
