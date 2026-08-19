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
  var MONTHS_AHEAD = 2;

  var JD = root.JDate || root.Date;

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
    var title = el('span', 'jdp-topbar-title', 'انتخاب تاریخ سفر');
    topbar.appendChild(title);
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

  JalaliDatepicker.prototype._renderMonths = function () {
    this.body.innerHTML = '';
    var startY = this.today.y;
    var startM = this.today.m;

    for (var i = 0; i < MONTHS_AHEAD; i++) {
      var m = startM + i;
      var y = startY + Math.floor(m / 12);
      m = m % 12;
      this._renderMonth(y, m);
    }
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
    this.overlay.classList.add('jdp-open');
    this.container.classList.add('jdp-open');
    document.body.style.overflow = 'hidden';

    // scroll to today's month
    var todayEl = this.body.querySelector('.jdp-today');
    if (todayEl) {
      var month = todayEl.closest('.jdp-month');
      if (month) month.scrollIntoView({ block: 'start' });
    }
  };

  JalaliDatepicker.prototype.close = function () {
    this.overlay.classList.remove('jdp-open');
    this.container.classList.remove('jdp-open');
    document.body.style.overflow = '';
  };

  root.JalaliDatepicker = JalaliDatepicker;
})(window);
