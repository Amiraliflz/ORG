'use strict';

var CACHE = 'mrshoofer-ops-v1';
var SHELL = ['/css/ops-monochrome.css', '/ops/monitor.js'];

self.addEventListener('install', function (event) {
  event.waitUntil(
    caches.open(CACHE).then(function (cache) {
      return cache.addAll(SHELL);
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', function (event) {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', function (event) {
  var url = event.request.url;
  if (url.indexOf('/Admin/Ops/StatusJson') !== -1) {
    event.respondWith(
      fetch(event.request).catch(function () {
        return new Response(JSON.stringify({ isHealthy: false, components: [] }), {
          headers: { 'Content-Type': 'application/json' }
        });
      })
    );
    return;
  }
  if (SHELL.some(function (p) { return url.indexOf(p) !== -1; })) {
    event.respondWith(
      caches.match(event.request).then(function (cached) {
        return cached || fetch(event.request);
      })
    );
  }
});
