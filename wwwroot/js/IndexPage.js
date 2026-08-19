/**
 * Homepage trip search: form validation + Jalali datepicker on #starttime.
 */
'use strict';

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

  var dateInput = document.getElementById('starttime');
  if (dateInput && window.JalaliDatepicker) {
    new JalaliDatepicker(dateInput, { minDate: 'today' });
  }
});
