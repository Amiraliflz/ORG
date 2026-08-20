(function () {
  'use strict';

  var pollMs = 30000;
  var componentsEl = document.getElementById('opsComponents');
  var heroDot = document.getElementById('opsHeroDot');
  var heroText = document.getElementById('opsHeroText');
  var pill = document.getElementById('opsOverallPill');
  var uptimeEl = document.getElementById('opsUptime');
  var lastCheckEl = document.getElementById('opsLastCheck');
  var restartForm = document.getElementById('opsRestartForm');
  var refreshBtn = document.getElementById('opsRefreshBtn');

  function fmtTime(iso) {
    if (!iso) return '—';
    try {
      return new Date(iso).toLocaleString('fa-IR');
    } catch (e) {
      return iso;
    }
  }

  function renderStatus(data) {
    var up = data.isHealthy;
    heroDot.className = 'ops-hero-dot ' + (up ? 'up' : 'down');
    heroText.textContent = up ? 'UP' : 'DOWN';
    pill.textContent = up ? 'UP' : 'DOWN';
    pill.className = 'ops-status-pill ' + (up ? 'up' : 'down');
    uptimeEl.textContent = 'Uptime 24h: ' + (data.uptimePercent24h != null ? data.uptimePercent24h + '%' : '—');
    lastCheckEl.textContent = 'Last check: ' + fmtTime(data.checkedAt);

    if (restartForm) {
      restartForm.style.display = up ? 'none' : 'block';
    }

    if (!componentsEl) return;
    componentsEl.innerHTML = (data.components || []).map(function (c) {
      var detail = c.details || '';
      if (c.responseMs != null) detail = (detail ? detail + ' · ' : '') + c.responseMs + 'ms';
      return (
        '<div class="ops-component">' +
          '<div><div class="ops-component-name">' + (c.label || c.name) + '</div>' +
          (detail ? '<div class="ops-component-detail">' + detail + '</div>' : '') +
          '</div>' +
          '<div class="ops-dot ' + (c.isHealthy ? 'up' : 'down') + '"></div>' +
        '</div>'
      );
    }).join('');
  }

  function fetchStatus() {
    return fetch('/Admin/Ops/StatusJson', { credentials: 'same-origin' })
      .then(function (r) { return r.json(); })
      .then(renderStatus)
      .catch(function () {
        renderStatus({ isHealthy: false, uptimePercent24h: 0, checkedAt: new Date().toISOString(), components: [
          { name: 'app', label: 'Application', isHealthy: false, details: 'Fetch failed' }
        ]});
      });
  }

  if (refreshBtn) refreshBtn.addEventListener('click', fetchStatus);
  fetchStatus();
  setInterval(fetchStatus, pollMs);
})();
