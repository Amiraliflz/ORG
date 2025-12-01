/**
 * Main - Front Pages
 */
'use strict';

let isRtl = window.Helpers.isRtl(),
  isDarkStyle = window.Helpers.isDarkStyle();

(function () {
  const menu = document.getElementById('navbarSupportedContent'),
    nav = document.querySelector('.layout-navbar'),
    navItemLink = document.querySelectorAll('.navbar-nav .nav-link');

  // Initialised custom options if checked
  setTimeout(function () {
    window.Helpers.initCustomOptionCheck();
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

  // If layout is RTL add .dropdown-menu-end class to .dropdown-menu
  if (isRtl) {
    Helpers._addClass('dropdown-menu-end', document.querySelectorAll('#layout-navbar .dropdown-menu'));
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

  document.addEventListener('click', function (event) {
    if (menu && !menu.contains(event.target)) {
      closeMenu();
    }
  });
  navItemLink.forEach(link => {
    link.addEventListener('click', event => {
      if (!link.classList.contains('dropdown-toggle')) {
        closeMenu();
      } else {
        event.preventDefault();
      }
    });
  });

  // If layout is RTL add .dropdown-menu-end class to .dropdown-menu
  if (isRtl) {
    Helpers._addClass('dropdown-menu-end', document.querySelectorAll('.dropdown-menu'));
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
