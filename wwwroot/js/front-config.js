/**
 * Front-page config — no TemplateCustomizer (saves ~100KB on public pages).
 */
'use strict';

var assetsPath = document.documentElement.getAttribute('data-assets-path') || '/';
var templateName = document.documentElement.getAttribute('data-template');
var rtlSupport = true;
