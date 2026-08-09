/**
 * Homepage trip search: form validation + Jalali flatpickr on #starttime.
 */
'use strict';

/* flatpickr-jdate expects Jalali Date */
if (window.JDate) {
  window.Date = window.JDate;
}

$(function () {
  $('#tripForm').on('submit', function (e) {
    var isValid = true;
    $(this).find('input').each(function () {
      if (($(this).val() || '').trim() === '') {
        isValid = false;
        $(this).focus();
        return false;
      }
    });
    if (!isValid) {
      e.preventDefault();
    }
  });

  var flatpickrDate = document.querySelector('#starttime');
  if (flatpickrDate && typeof flatpickrDate.flatpickr === 'function') {
    flatpickrDate.flatpickr({
      disableMobile: true,
      monthSelectorType: 'static',
      locale: Object.assign({}, (window.flatpickr && flatpickr.l10ns && flatpickr.l10ns.fa) || {}, {
        weekdays: {
          shorthand: ['شنبه', 'یکشنبه', 'د', 'س', 'چ', 'پ', 'ج'],
          longhand: ['شنبه', 'یک‌شنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنج‌شنبه', 'جمعه']
        },
        daysInMonth: [31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29],
        firstDayOfWeek: 6
      }),
      altFormat: 'Y/m/d',
      minDate: 'today'
    });
  }
});
