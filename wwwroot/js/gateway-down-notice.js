/**
 * Gateway temporarily unavailable — show notice modal on ConfirmInfo.
 * Self-contained: injects markup into document.body (same pattern as reserve-trip-modals.js).
 */
(function () {
  'use strict';

  var SUPPORT_TEL = '02128422243';
  var SUPPORT_DISPLAY = '۰۲۱-۲۸۴۲۲۲۴۳';
  var MODAL_ID = 'gatewayDownModal';
  var STORAGE_KEY = 'gateway-down-notice-shown';
  var shownOnce = false;

  function alreadyShownThisSession() {
    try { return sessionStorage.getItem(STORAGE_KEY) === '1'; } catch (e) { return shownOnce; }
  }

  function markShown() {
    shownOnce = true;
    try { sessionStorage.setItem(STORAGE_KEY, '1'); } catch (e) { /* ignore */ }
  }

  function buildModalHtml() {
    return (
      '<div class="modal fade" id="' + MODAL_ID + '" tabindex="-1" aria-hidden="true" style="z-index:20000">' +
        '<div class="modal-dialog modal-dialog-centered" role="document">' +
          '<div class="modal-content" style="border-radius:1.25rem;overflow:hidden">' +
            '<div class="modal-header border-bottom-0 pb-0">' +
              '<h5 class="modal-title fw-bold">درگاه پرداخت موقتاً غیرفعال است</h5>' +
              '<button aria-label="بستن" class="btn-close" data-bs-dismiss="modal" type="button"></button>' +
            '</div>' +
            '<div class="modal-body pt-2 pb-3">' +
              '<div class="d-flex flex-column align-items-center text-center px-2 py-2">' +
                '<div class="d-flex align-items-center justify-content-center mb-3" style="width:4rem;height:4rem;border-radius:50%;background:#fff4e5">' +
                  '<i class="ti ti-building-bank" style="font-size:1.75rem;color:#e67e22"></i>' +
                '</div>' +
                '<p class="mb-3 text-muted" style="line-height:1.9;font-size:.95rem">' +
                  'به دلیل مشکلات بانکی، درگاه پرداخت تا اطلاع ثانوی از دسترس خارج می‌باشد. ' +
                  'برای هماهنگی سفرها با شماره پشتیبانی ما در تماس باشید.' +
                '</p>' +
                '<a href="tel:' + SUPPORT_TEL + '" class="d-inline-flex align-items-center gap-2 fw-bold text-decoration-none px-3 py-2" ' +
                  'style="background:#f3f2ff;border-radius:1rem;color:#696cff;font-size:1.15rem" dir="ltr">' +
                  '<i class="ti ti-phone-call" style="font-size:1.2rem"></i> ' + SUPPORT_DISPLAY +
                '</a>' +
              '</div>' +
            '</div>' +
            '<div class="modal-footer border-top-0 d-flex justify-content-between gap-2">' +
              '<button class="btn btn-label-secondary px-3" data-bs-dismiss="modal" type="button">بستن</button>' +
              '<a href="tel:' + SUPPORT_TEL + '" class="btn btn-primary px-3">' +
                '<i class="ti ti-phone me-1"></i> تماس با پشتیبانی' +
              '</a>' +
            '</div>' +
          '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function ensureModalElement() {
    var existing = document.getElementById(MODAL_ID);
    if (existing) {
      if (existing.parentElement !== document.body) {
        document.body.appendChild(existing);
      }
      existing.style.zIndex = '20000';
      return existing;
    }
    document.body.insertAdjacentHTML('beforeend', buildModalHtml());
    return document.getElementById(MODAL_ID);
  }

  function showGatewayDownModal() {
    if (typeof bootstrap === 'undefined' || !bootstrap.Modal) {
      return false;
    }
    var el = ensureModalElement();
    if (!el) return false;

    var instance = bootstrap.Modal.getOrCreateInstance
      ? bootstrap.Modal.getOrCreateInstance(el)
      : new bootstrap.Modal(el);

    // Raise backdrop above front-navbar (z-index up to 9999)
    el.addEventListener('shown.bs.modal', function onShown() {
      el.removeEventListener('shown.bs.modal', onShown);
      document.querySelectorAll('.modal-backdrop').forEach(function (b) {
        b.style.zIndex = '19999';
      });
    });

    instance.show();
    markShown();
    return true;
  }

  window.showGatewayDownModal = showGatewayDownModal;

  function autoOpen() {
    if (alreadyShownThisSession()) return;
    // ConfirmInfo sets this; default true so standalone script still opens
    if (window.gatewayPaymentEnabled === true) return;
    setTimeout(function () {
      if (alreadyShownThisSession()) return;
      if (!showGatewayDownModal()) {
        // bootstrap may not be ready yet — retry briefly
        var tries = 0;
        var t = setInterval(function () {
          tries++;
          if (showGatewayDownModal() || tries > 30) clearInterval(t);
        }, 150);
      }
    }, 400);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', autoOpen);
  } else {
    autoOpen();
  }
  window.addEventListener('load', function () {
    setTimeout(autoOpen, 200);
  });
})();
