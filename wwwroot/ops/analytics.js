(function () {
  'use strict';

  var chartColors = ['#111', '#666', '#999', '#ccc', '#333', '#888', '#bbb', '#444'];
  var monoGrid = '#e5e5e5';
  var monoFg = '#111';

  function statCard(label, value) {
    return (
      '<div class="col-6 col-md-3">' +
        '<div class="ops-stat-card">' +
          '<div class="ops-stat-value">' + value + '</div>' +
          '<div class="ops-stat-label">' + label + '</div>' +
        '</div>' +
      '</div>'
    );
  }

  function fmtNum(n) {
    try { return Number(n).toLocaleString('fa-IR'); } catch (e) { return n; }
  }

  fetch('/Admin/Ops/AnalyticsJson', { credentials: 'same-origin' })
    .then(function (r) { return r.json(); })
    .then(function (d) {
      var stats = document.getElementById('opsStats');
      if (stats) {
        stats.innerHTML =
          statCard('فروش امروز', fmtNum(d.todaySold)) +
          statCard('فروش ۷ روز', fmtNum(d.weekSold)) +
          statCard('درآمد ۷ روز', fmtNum(d.weekRevenue)) +
          statCard('خطا ۲۴س', fmtNum(d.errors24h)) +
          statCard('مشتری جدید', fmtNum(d.newCustomersWeek)) +
          statCard('لغو ۷ روز', fmtNum(d.cancellationsWeek)) +
          statCard('درخواست HTTP', fmtNum(d.requests24h)) +
          statCard('فروش ۳۰ روز', fmtNum(d.monthSold));
      }

      if (typeof ApexCharts === 'undefined') return;

      var salesEl = document.querySelector('#opsSalesChart');
      if (salesEl && d.dailySales) {
        new ApexCharts(salesEl, {
          chart: { type: 'bar', height: 280, toolbar: { show: false }, fontFamily: 'inherit' },
          series: [{ name: 'فروش', data: d.dailySales.map(function (x) { return x.count; }) }],
          xaxis: { categories: d.dailySales.map(function (x) { return x.date; }), labels: { style: { colors: monoFg } } },
          colors: ['#111'],
          plotOptions: { bar: { borderRadius: 4, columnWidth: '55%' } },
          grid: { borderColor: monoGrid },
          dataLabels: { enabled: false },
          yaxis: { labels: { style: { colors: monoFg } } }
        }).render();
      }

      var routesEl = document.querySelector('#opsRoutesChart');
      if (routesEl && d.topRoutes && d.topRoutes.length) {
        new ApexCharts(routesEl, {
          chart: { type: 'donut', height: 280, fontFamily: 'inherit' },
          series: d.topRoutes.map(function (x) { return x.count; }),
          labels: d.topRoutes.map(function (x) { return x.label; }),
          colors: chartColors,
          legend: { position: 'bottom', labels: { colors: monoFg } },
          dataLabels: { style: { colors: ['#fff'] } },
          stroke: { colors: ['#fff'] }
        }).render();
      }
    })
    .catch(function (err) { console.error(err); });

})();
