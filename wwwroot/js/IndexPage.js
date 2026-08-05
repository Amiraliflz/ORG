/**
 * Form Picker
 */

'use strict';

/* important for jalali bootstrap date */
window.Date = window.JDate;


$(document).ready(function () {
  $('#tripForm').on('submit', function (e) {
    let isValid = true;

    // Check each required field
    $(this).find('input').each(function () {
      if ($(this).val().trim() === '') {
        isValid = false;

        $(this).focus(); // Focus on the empty input field
        return false; // Exit the loop
      }
    });

    // If not valid, prevent the form from submitting
    if (!isValid) {
      e.preventDefault();
    }
  });
});



// Flat Picker
$(function () {

  // Flat Picker
  // --------------------------------------------------------------------
  const flatpickrDate = document.querySelector('#starttime'),
    flatpickrDateTime = document.querySelector('#flatpickr-datetime'),
    flatpickrMulti = document.querySelector('#flatpickr-multi'),
    flatpickrRange = document.querySelector('#flatpickr-range'),
    flatpickrInline = document.querySelector('#flatpickr-inline'),
    flatpickrFriendly = document.querySelector('#flatpickr-human-friendly'),
    flatpickrDisabledRange = document.querySelector('#flatpickr-disabled-range');

  // Date
  if (flatpickrDate) {
    flatpickrDate.flatpickr({
      disableMobile: true,
      monthSelectorType: 'static',
      locale: Object.assign({}, (window.flatpickr && flatpickr.l10ns && flatpickr.l10ns.fa) || {}, {
        weekdays: {
          shorthand: ["شنبه", "یکشنبه", "د", "س", "چ", "پ", "ج"],
          longhand: [
            "شنبه",
            "یک‌شنبه",
            "دوشنبه",
            "سه‌شنبه",
            "چهارشنبه",
            "پنج‌شنبه",
            "جمعه"
          ]
        },
        // Jalali month lengths: Farvardin–Shahrivar=31, Mehr–Bahman=30, Esfand=29/30
        daysInMonth: [31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29],
        firstDayOfWeek: 6
      }),
      altFormat: 'Y/m/d',
      minDate: "today"
    });
  }

  // Time
  if (flatpickrTime) {
    flatpickrTime.flatpickr({
      disableMobile: "flase",
      enableTime: true,
      noCalendar: true,
      locale: 'fa',
      altInput: true,
      altFormat: 'H:i',
    });
  }

  // Datetime
  if (flatpickrDateTime) {
    flatpickrDateTime.flatpickr({
      disableMobile: "flase",
      enableTime: true,
      dateFormat: 'Y/m/d H:i',
      locale: 'fa',
    });
  }

  // Multi Date Select
  if (flatpickrMulti) {
    flatpickrMulti.flatpickr({
      disableMobile: "flase",
      weekNumbers: true,
      enableTime: true,
      locale: 'fa',
      mode: 'multiple',
      dateFormat: 'Y/m/d H:i',
      minDate: 'today'
    });
  }

  // Range
  if (typeof flatpickrRange != undefined) {
    flatpickrRange.flatpickr({
      disableMobile: "flase",
      mode: 'range',
      locale: 'fa',
      dateFormat: 'Y/m/d',
    });
  }

  // Inline
  if (flatpickrInline) {
    flatpickrInline.flatpickr({
      disableMobile: "flase",
      inline: true,
      allowInput: false,
      dateFormat: 'Y/m/d',
      monthSelectorType: 'static',
      locale: 'fa',
    });
  }

  // Human Friendly
  if (flatpickrFriendly) {
    flatpickrFriendly.flatpickr({
      disableMobile: "flase",
      altInput: true,
      altFormat: 'j F Y',
      dateFormat: 'Y/m/d',
      locale: 'fa',
    });
  }

  // Disabled Date Range
  if (flatpickrDisabledRange) {
    const fromDate = new JDate(Date.now() - 3600 * 1000 * 48);
    const toDate = new JDate(Date.now() + 3600 * 1000 * 48);

    flatpickrDisabledRange.flatpickr({
      disableMobile: "flase",
      dateFormat: 'Y/m/d',
      locale: 'fa',
      disable: [
        {
          from: fromDate.toISOString().split('T')[0],
          to: toDate.toISOString().split('T')[0]
        }
      ]
    });
  }
})();

$.fn.datepicker.dates['fa'] = {
  days: ["یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"],
  daysShort: ["یک", "دو", "سه", "چهار", "پنج", "جمعه", "شنبه"],
  daysMin: ["ی", "د", "س", "چ", "پ", "ج", "شنبه"],
  months: ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"],
  monthsShort: ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"],
  today: "امروز",
  clear: "پاک کردن",
  titleFormat: "MM yyyy", /* Leverages same syntax as 'format' */
  weekStart: 6
};

// * Pickers with jQuery dependency (jquery)
$(function () {
  // Bootstrap Datepicker
  // --------------------------------------------------------------------

/*  var bsDatepickerBasic = $('#starttime');*/

  // Basic
  if (bsDatepickerBasic.length) {
    bsDatepickerBasic.datepicker({
      language: 'fa',
      format: 'yyyy/mm/dd',
      todayHighlight: true,
      orientation: isRtl ? 'auto right' : 'auto left'
    });
  }

  // Format
  if (bsDatepickerFormat.length) {
    bsDatepickerFormat.datepicker({
      language: 'fa',
      todayHighlight: true,
      format: 'yyyy/mm/dd',
      orientation: isRtl ? 'auto right' : 'auto left'
    });
  }

  // Range
  if (bsDatepickerRange.length) {
    bsDatepickerRange.datepicker({
      language: 'fa',
      format: 'yyyy/mm/dd',
      todayHighlight: true,
      orientation: isRtl ? 'auto right' : 'auto left'
    });
  }

  // Disabled Days
  if (bsDatepickerDisabledDays.length) {
    bsDatepickerDisabledDays.datepicker({
      language: 'fa',
      format: 'yyyy/mm/dd',
      todayHighlight: true,
      daysOfWeekDisabled: [0, 6],
      orientation: isRtl ? 'auto right' : 'auto left'
    });
  }

  // Multiple
  if (bsDatepickerMultidate.length) {
    bsDatepickerMultidate.datepicker({
      language: 'fa',
      format: 'yyyy/mm/dd',
      multidate: true,
      todayHighlight: true,
      orientation: isRtl ? 'auto right' : 'auto left'
    });
  }

  // Options
  if (bsDatepickerOptions.length) {
    bsDatepickerOptions.datepicker({
      language: 'fa',
      format: 'yyyy/mm/dd',
      calendarWeeks: true,
      clearBtn: true,
      todayHighlight: true,
      orientation: isRtl ? 'auto left' : 'auto right'
    });
  }

  // Auto close
  if (bsDatepickerAutoclose.length) {
    bsDatepickerAutoclose.datepicker({
      format: 'yyyy/mm/dd',
      language: 'fa',
      todayHighlight: true,
      autoclose: true,
      orientation: isRtl ? 'auto right' : 'auto left'
    });
  }

  // Inline picker
  if (bsDatepickerInlinedate.length) {
    bsDatepickerInlinedate.datepicker({
      format: 'yyyy/mm/dd',
      language: 'fa',
      todayHighlight: true
    });
  }

  // Bootstrap Daterange Picker
  // --------------------------------------------------------------------


  var dateRangePickerFa = {
    "format": "YYYY/MM/DD",
    "separator": " - ",
    "applyLabel": "اعمال",
    "cancelLabel": "انصراف",
    "startLabel": 'تاریخ شروع',
    "endLabel": 'تاریخ پایان',
    "fromLabel": "از",
    "toLabel": "تا",
    "weekLabel": "هفته",
    "customRangeLabel": "انتخاب بازه",
    "daysOfWeek": ["ی", "د", "س", "چ", "پ", "ج", "ش"],
    "monthNames": ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"],
    "firstDay": 6
  };

  var dateRangePickerTimeFa = {
    "format": "YYYY/MM/DD h:mm A",
    "separator": " - ",
    "applyLabel": "اعمال",
    "cancelLabel": "انصراف",
    "startLabel": 'تاریخ شروع',
    "endLabel": 'تاریخ پایان',
    "fromLabel": "از",
    "toLabel": "تا",
    "weekLabel": "هفته",
    "customRangeLabel": "انتخاب بازه",
    "daysOfWeek": ["ی", "د", "س", "چ", "پ", "ج", "ش"],
    "monthNames": ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"],
    "firstDay": 6
  };

  var bsRangePickerBasic = $('#bs-rangepicker-basic'),
    bsRangePickerSingle = $('#bs-rangepicker-single'),
    bsRangePickerTime = $('#bs-rangepicker-time'),
    bsRangePickerRange = $('#bs-rangepicker-range'),
    bsRangePickerWeekNum = $('#bs-rangepicker-week-num'),
    bsRangePickerDropdown = $('#bs-rangepicker-dropdown'),
    bsRangePickerCancelBtn = document.getElementsByClassName('cancelBtn');

  // Basic
  if (bsRangePickerBasic.length) {
    bsRangePickerBasic.daterangepicker({
      locale: dateRangePickerFa,
      opens: isRtl ? 'left' : 'right'
    });
  }

  // Single
  if (bsRangePickerSingle.length) {
    bsRangePickerSingle.daterangepicker({
      locale: dateRangePickerFa,
      singleDatePicker: true,
      opens: isRtl ? 'left' : 'right'
    });
  }

  // Time & Date
  if (bsRangePickerTime.length) {
    bsRangePickerTime.daterangepicker({
      timePicker: true,
      timePickerIncrement: 30,
      locale: dateRangePickerTimeFa,
      opens: isRtl ? 'left' : 'right'
    });
  }

  if (bsRangePickerRange.length) {
    bsRangePickerRange.daterangepicker({
      locale: dateRangePickerFa,
      ranges: {
        'امروز': [moment(), moment()],
        'دیروز': [moment().subtract(1, 'days'), moment().subtract(1, 'days')],
        '7 روز قبل': [moment().subtract(6, 'days'), moment()],
        '۳۰ روز قبل': [moment().subtract(29, 'days'), moment()],
        'این ماه': [moment().startOf('month'), moment().endOf('month')],
        'ماه قبلی': [moment().subtract(1, 'month').startOf('month'), moment().subtract(1, 'month').endOf('month')]
      },
      opens: isRtl ? 'left' : 'right'
    });
  }

  // Week Numbers
  if (bsRangePickerWeekNum.length) {
    bsRangePickerWeekNum.daterangepicker({
      locale: dateRangePickerFa,
      showWeekNumbers: true,
      opens: isRtl ? 'left' : 'right'
    });
  }
  // Dropdown
  if (bsRangePickerDropdown.length) {
    bsRangePickerDropdown.daterangepicker({
      locale: dateRangePickerFa,
      showDropdowns: true,
      opens: isRtl ? 'left' : 'right'
    });
  }

  // Adding btn-label-secondary class in cancel btn
  for (var i = 0; i < bsRangePickerCancelBtn.length; i++) {
    bsRangePickerCancelBtn[i].classList.remove('btn-default');
    bsRangePickerCancelBtn[i].classList.add('btn-label-primary');
  }

  // jQuery Timepicker
  // --------------------------------------------------------------------

  var timePickerLocaleFa = {
    am: ' ق.ظ',
    pm: ' ب.ظ',
    AM: ' ق.ظ',
    PM: ' ب.ظ',
    decimal: '.',
    mins: 'دقیقه',
    hr: 'ساعت',
    hrs: 'ساعت'
  };

  var basicTimepicker = $('#timepicker-basic'),
    minMaxTimepicker = $('#timepicker-min-max'),
    disabledTimepicker = $('#timepicker-disabled-times'),
    formatTimepicker = $('#timepicker-format'),
    stepTimepicker = $('#timepicker-step'),
    altHourTimepicker = $('#timepicker-24hours');

  // Basic
  if (basicTimepicker.length) {
    basicTimepicker.timepicker({
      lang: timePickerLocaleFa,
      orientation: isRtl ? 'r' : 'l'
    });
  }

  // Min & Max
  if (minMaxTimepicker.length) {
    minMaxTimepicker.timepicker({
      lang: timePickerLocaleFa,
      minTime: '2:00pm',
      maxTime: '7:00pm',
      showDuration: true,
      orientation: isRtl ? 'r' : 'l'
    });
  }

  // Disabled Picker
  if (disabledTimepicker.length) {
    disabledTimepicker.timepicker({
      lang: timePickerLocaleFa,
      disableTimeRanges: [
        ['12am', '3am'],
        ['4am', '4:30am']
      ],
      orientation: isRtl ? 'r' : 'l'
    });
  }

  // Format Picker
  if (formatTimepicker.length) {
    formatTimepicker.timepicker({
      lang: timePickerLocaleFa,
      timeFormat: 'H:i:s',
      orientation: isRtl ? 'r' : 'l'
    });
  }

  // Steps Picker
  if (stepTimepicker.length) {
    stepTimepicker.timepicker({
      lang: timePickerLocaleFa,
      step: 15,
      orientation: isRtl ? 'r' : 'l'
    });
  }

  // 24 Hours Format
  if (altHourTimepicker.length) {
    altHourTimepicker.timepicker({
      lang: timePickerLocaleFa,
      show: '24:00',
      timeFormat: 'H:i:s',
      orientation: isRtl ? 'r' : 'l'
    });
  }
});
