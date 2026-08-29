/**
 * Main - Front Pages
 */
'use strict';

let isRtl = window.Helpers && typeof window.Helpers.isRtl === 'function' ? window.Helpers.isRtl() : true,
  isDarkStyle = window.Helpers && typeof window.Helpers.isDarkStyle === 'function' ? window.Helpers.isDarkStyle() : false;

(function () {
  const menu = document.getElementById('navbarSupportedContent'),
    nav = document.querySelector('.layout-navbar'),
    navItemLink = document.querySelectorAll('.navbar-nav .nav-link');

  // Initialised custom options if checked
  setTimeout(function () {
    if (window.Helpers && typeof window.Helpers.initCustomOptionCheck === 'function') {
      window.Helpers.initCustomOptionCheck();
    }
  }, 1000);

  if (typeof Waves !== 'undefined') {
    Waves.init();
    Waves.attach(".btn[class*='btn-']:not([class*='btn-outline-']):not([class*='btn-label-'])", ['waves-light']);
    Waves.attach("[class*='btn-outline-']");
    Waves.attach("[class*='btn-label-']");
    Waves.attach('.pagination .page-item .page-link');
  }

  // Init BS Tooltip
  const tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
  tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl);
  });

  function addClass(cls, nodes) {
    if (window.Helpers && typeof window.Helpers._addClass === 'function') {
      window.Helpers._addClass(cls, nodes);
      return;
    }
    (nodes || []).forEach(function (el) { if (el) el.classList.add(cls); });
  }

  // If layout is RTL add .dropdown-menu-end class to .dropdown-menu
  if (isRtl) {
    addClass('dropdown-menu-end', document.querySelectorAll('#layout-navbar .dropdown-menu'));
  }

  // Navbar (guard for pages without .layout-navbar)
  if (nav) {
    window.addEventListener('scroll', () => {
      if (window.scrollY > 10) {
        nav.classList.add('navbar-active');
      } else {
        nav.classList.remove('navbar-active');
      }
    });
    window.addEventListener('load', () => {
      if (window.scrollY > 10) {
        nav.classList.add('navbar-active');
      } else {
        nav.classList.remove('navbar-active');
      }
    });
  }

  // Function to close the mobile menu (guard menu)
  function closeMenu() { if (menu) menu.classList.remove('show'); }

  function hideDropdownToggle(toggle) {
    if (!toggle || typeof bootstrap === 'undefined') return;
    const instance = bootstrap.Dropdown.getInstance(toggle);
    if (instance) {
      instance.hide();
      return;
    }
    if (toggle.classList.contains('show')) {
      toggle.classList.remove('show');
      toggle.setAttribute('aria-expanded', 'false');
      const menu = toggle.closest('.dropdown')?.querySelector('.dropdown-menu');
      menu?.classList.remove('show');
    }
  }

  function closeNavbarDropdowns(exceptToggle) {
    document.querySelectorAll('.landing-navbar [data-bs-toggle="dropdown"]').forEach(toggle => {
      if (toggle !== exceptToggle) hideDropdownToggle(toggle);
    });
  }

  function resetNavbarDropdowns() {
    document.querySelectorAll('.landing-navbar .dropdown-menu.show').forEach(menu => {
      menu.classList.remove('show');
    });
    document.querySelectorAll('.landing-navbar [data-bs-toggle="dropdown"]').forEach(toggle => {
      toggle.classList.remove('show');
      toggle.setAttribute('aria-expanded', 'false');
      if (typeof bootstrap !== 'undefined') {
        const instance = bootstrap.Dropdown.getInstance(toggle);
        if (instance) instance.hide();
      }
    });
  }

  // Close stray open menus after hard refresh / bfcache restore
  resetNavbarDropdowns();
  document.addEventListener('DOMContentLoaded', resetNavbarDropdowns);
  window.addEventListener('pageshow', function (event) {
    if (event.persisted) resetNavbarDropdowns();
  });

  function closeProfileDropdown() {
    hideDropdownToggle(document.querySelector('.dropdown-user > .dropdown-toggle'));
  }

  function syncNavDrawerBodyLock() {
    document.body.classList.toggle('nav-drawer-open', !!(menu && menu.classList.contains('show')));
  }

  document.addEventListener('show.bs.dropdown', function (event) {
    const toggle = event.target;
    if (!toggle.closest('.landing-navbar')) return;
    closeNavbarDropdowns(toggle);
  });

  if (menu) {
    menu.addEventListener('show.bs.collapse', () => {
      closeNavbarDropdowns();
      syncNavDrawerBodyLock();
    });
    menu.addEventListener('hidden.bs.collapse', syncNavDrawerBodyLock);
  }

  document.addEventListener('click', function (event) {
    if (menu && menu.classList.contains('show')) {
      const toggle = document.querySelector('[data-bs-target="#navbarSupportedContent"][data-bs-toggle="collapse"]');
      if (!menu.contains(event.target) && !(toggle && toggle.contains(event.target))) {
        closeMenu();
        syncNavDrawerBodyLock();
      }
    }
  });

  navItemLink.forEach(link => {
    link.addEventListener('click', event => {
      // Only hijack dropdown toggles inside the mobile drawer — not the profile avatar
      if (link.classList.contains('dropdown-toggle') && menu && menu.contains(link)) {
        event.preventDefault();
        return;
      }
      if (!link.classList.contains('dropdown-toggle')) {
        closeMenu();
        syncNavDrawerBodyLock();
      }
    });
  });

  document.querySelectorAll('.landing-nav-menu .navbar-nav-btn:not(.dropdown-toggle)').forEach(link => {
    link.addEventListener('click', () => {
      closeMenu();
      syncNavDrawerBodyLock();
    });
  });

  document.querySelectorAll('.landing-nav-menu .landing-nav-dropdown-menu .navbar-nav-btn').forEach(link => {
    link.addEventListener('click', () => {
      closeMenu();
      closeNavbarDropdowns();
      syncNavDrawerBodyLock();
    });
  });

  document.querySelectorAll('.dropdown-user .landing-nav-dropdown-menu .navbar-nav-btn').forEach(link => {
    link.addEventListener('click', () => closeNavbarDropdowns());
  });

  // If layout is RTL add .dropdown-menu-end class to .dropdown-menu
  if (isRtl) {
    addClass('dropdown-menu-end', document.querySelectorAll('.dropdown-menu'));
  }

  // Mega dropdown
  const megaDropdown = document.querySelectorAll('.nav-link.mega-dropdown');
  if (megaDropdown) {
    megaDropdown.forEach(e => { new MegaDropdown(e); });
  }

  //Style Switcher (Light/Dark/System Mode)
  let styleSwitcher = document.querySelector('.dropdown-style-switcher');

  let storedStyle =
    localStorage.getItem('templateCustomizer-' + templateName + '--Style') ||
    (window.templateCustomizer?.settings?.defaultStyle ?? 'light');

  if (window.templateCustomizer && styleSwitcher) {
    let styleSwitcherItems = [].slice.call(styleSwitcher.children[1].querySelectorAll('.dropdown-item'));
    styleSwitcherItems.forEach(function (item) {
      item.addEventListener('click', function () {
        let currentStyle = this.getAttribute('data-theme');
        if (currentStyle === 'light') window.templateCustomizer.setStyle('light');
        else if (currentStyle === 'dark') window.templateCustomizer.setStyle('dark');
        else window.templateCustomizer.setStyle('system');
      });
    });

    const styleSwitcherIcon = styleSwitcher.querySelector('i');
    if (styleSwitcherIcon) {
      if (storedStyle === 'light') {
        styleSwitcherIcon.classList.add('ti-sun');
        new bootstrap.Tooltip(styleSwitcherIcon, { title: 'حالت روز', fallbackPlacements: ['bottom'] });
      } else if (storedStyle === 'dark') {
        styleSwitcherIcon.classList.add('ti-moon');
        new bootstrap.Tooltip(styleSwitcherIcon, { title: 'حالت شب', fallbackPlacements: ['bottom'] });
      } else {
        styleSwitcherIcon.classList.add('ti-device-desktop');
        new bootstrap.Tooltip(styleSwitcherIcon, { title: 'حالت سیستم', fallbackPlacements: ['bottom'] });
      }
    }
  }

  switchImage(storedStyle);

  function switchImage(style) {
    if (style === 'system') {
      style = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    const switchImagesList = [].slice.call(document.querySelectorAll('[data-app-' + style + '-img]'));
    switchImagesList.map(function (imageEl) {
      const setImage = imageEl.getAttribute('data-app-' + style + '-img');
      if (setImage) imageEl.src = assetsPath + 'img/' + setImage;
    });
  }
})();
