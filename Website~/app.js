(function () {
  'use strict';

  var LANGS = ['en', 'ja', 'ko', 'zh-CN'];
  var FONT_LINKS = {
    ja: 'https://fonts.googleapis.com/css2?family=M+PLUS+Rounded+1c:wght@400;700;800&display=swap',
    ko: 'https://fonts.googleapis.com/css2?family=Noto+Sans+KR:wght@400;700&display=swap',
    'zh-CN': 'https://fonts.googleapis.com/css2?family=Noto+Sans+SC:wght@400;700&display=swap'
  };
  var MEDIA = 'media/v4/';
  var PAGE = document.documentElement.getAttribute('data-page') || 'home';
  var dicts = {};
  var state = { lang: 'en', release: null, loadFailed: false, look: 'crimson', layerCount: null };
  var reduced = !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
  var saveData = !!(navigator.connection && navigator.connection.saveData);
  var noAuto = reduced || saveData;

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }
  function mq(q) { return window.matchMedia ? window.matchMedia(q) : { matches: false, addEventListener: function () {} }; }
  function onChange(m, fn) { if (m.addEventListener) m.addEventListener('change', fn); }

  function store(key, value) {
    try {
      if (value === undefined) return localStorage.getItem(key);
      localStorage.setItem(key, value);
    } catch (e) {}
    return null;
  }

  /* one look for the whole site: ?look= in a link wins, then what this browser remembered, then crimson */
  var LOOKS = ['original', 'crimson', 'frost', 'venom'];
  var HERO_LOOKS = ['crimson', 'frost', 'venom'];
  function validLook(l) { return LOOKS.indexOf(l) !== -1; }
  state.look = (function () {
    var q = /[?&]look=(\w+)/.exec(location.search);
    /* the gallery opens on the next edited look each visit; a shared link from outside the site still wins */
    if (document.getElementById('gal-looks')) {
      var inside = false;
      try { inside = !!document.referrer && new URL(document.referrer).host === location.host; } catch (e) {}
      if (!(q && validLook(q[1]) && !inside)) {
        var last = store('ats-gal-last'), next = HERO_LOOKS[(HERO_LOOKS.indexOf(last) + 1) % HERO_LOOKS.length];
        store('ats-gal-last', next);
        try { var u = new URL(location.href); u.searchParams.set('look', next); history.replaceState(history.state, '', u.pathname + u.search + u.hash); } catch (e) {}
        return next;
      }
    }
    if (q && validLook(q[1])) return q[1];
    var saved = store('ats-look');
    return validLook(saved) ? saved : 'crimson';
  })();
  /* the inline script in index.html may already have pointed the hero at the remembered look */
  state.heroShown = (function () {
    var bg = document.getElementById('hero-bg');
    var l = bg && bg.getAttribute('data-look');
    return HERO_LOOKS.indexOf(l) !== -1 ? l : 'crimson';
  })();

  function t(key) {
    var d = dicts[state.lang];
    if (d && d[key] != null) return d[key];
    var en = dicts.en;
    return en && en[key] != null ? en[key] : null;
  }

  function fmt(template, values) {
    return template.replace(/\{(\w+)\}/g, function (m, k) { return values[k] != null ? values[k] : m; });
  }

  function safeUrl(u) {
    try {
      var url = new URL(u);
      return url.protocol === 'https:' ? url.href : null;
    } catch (e) { return null; }
  }

  function setText(sel, text) { var el = $(sel); if (el) el.textContent = text; }

  /* ---------- old anchors: the long page is now four pages ---------- */
  var OLD_HASH = {
    see: 'gallery.html#closeups', closeups: 'gallery.html#closeups', spin: 'gallery.html#spin', light: 'gallery.html#light',
    makeover: 'gallery.html#makeover', count: 'gallery.html#makeover', gallery: 'gallery.html', story: 'index.html#how',
    tools: 'features.html', select: 'features.html#select', link: 'features.html#select', paint: 'features.html#paint',
    layers: 'features.html#layers', stack: 'features.html#layers', colour: 'features.html#colour', color: 'features.html#colour',
    material: 'features.html#material', maps: 'features.html#maps', undo: 'features.html#safety', report: 'features.html#safety',
    learn: 'features.html#learn', trust: 'about.html#checks', checks: 'about.html#checks', faq: 'about.html#faq',
    made: 'about.html#made', help: 'about.html#made', glossary: 'glossary.html', skills: 'skills.html'
  };
  (function redirectOldHash() {
    var h = decodeURIComponent((location.hash || '').slice(1));
    if (!h || !OLD_HASH[h]) return;
    /* only when this page has no such section of its own; the DOM is not ready yet, so ask the target instead */
    var here = location.pathname.split('/').pop() || 'index.html';
    var target = OLD_HASH[h];
    if (target.split('#')[0] === here) return;
    var own = { home: ['how', 'wings', 'safe', 'install', 'top'], features: ['select', 'paint', 'layers', 'colour', 'material', 'maps', 'safety', 'learn', 'install'], gallery: ['spin', 'closeups', 'wings', 'light', 'makeover', 'honest', 'install'], about: ['what', 'checks', 'faq', 'made', 'install'], learn: [] }[PAGE] || [];
    if (own.indexOf(h) !== -1) return;
    location.replace(target);
  })();

  /* repository placeholders: on OWNER.github.io/REPO the links fix themselves */
  var repo = (function () {
    var m = /^([a-z0-9-]+)\.github\.io$/i.exec(location.hostname);
    var name = location.pathname.split('/')[1];
    return m && name ? { owner: m[1], name: name } : null;
  })();

  function fillRepo() {
    if (!repo) return;
    function sub(s) { return s.replace(/OWNER/g, repo.owner).replace(/REPO/g, repo.name); }
    $$('a[href]').forEach(function (a) {
      var href = a.getAttribute('href');
      if (/OWNER|REPO/.test(href)) a.setAttribute('href', sub(href));
    });
    $$('[data-sub]').forEach(function (el) {
      if (el.tagName === 'INPUT') el.value = sub(el.value);
      else el.textContent = sub(el.textContent);
    });
    var og = $('meta[property="og:image"]');
    if (og) og.setAttribute('content', sub(og.getAttribute('content')));
  }

  /* ---------- language: common strings plus this page's strings, English as the fallback ---------- */
  function detectLang() {
    var q = /[?&]lang=([\w-]+)/.exec(location.search);
    if (q && LANGS.indexOf(q[1]) !== -1) return q[1];
    var saved = store('ats-lang');
    if (saved && LANGS.indexOf(saved) !== -1) return saved;
    var prefs = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || 'en'];
    for (var i = 0; i < prefs.length; i++) {
      var p = String(prefs[i]).toLowerCase();
      if (p.indexOf('ja') === 0) return 'ja';
      if (p.indexOf('ko') === 0) return 'ko';
      if (p.indexOf('zh') === 0) return 'zh-CN';
      if (p.indexOf('en') === 0) return 'en';
    }
    return 'en';
  }

  function loadDict(lang) {
    if (dicts[lang]) return Promise.resolve();
    function one(file) {
      return fetch('i18n/' + lang + '/' + file + '.json')
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
        .catch(function () { return {}; });
    }
    return Promise.all([one('common'), one(PAGE)]).then(function (parts) {
      var d = {};
      parts.forEach(function (p) { for (var k in p) d[k] = p[k]; });
      dicts[lang] = d;
    });
  }

  /* Japanese, Korean and Chinese web fonts load only when that language is picked */
  function loadCjkFont(lang) {
    var href = FONT_LINKS[lang];
    if (!href || document.querySelector('link[data-font="' + lang + '"]')) return;
    var link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    link.setAttribute('data-font', lang);
    document.head.appendChild(link);
  }

  /* the coral full stop on display headings (the logo's dot) */
  function decorateHeadings() {
    $$('.h-display').forEach(function (h) {
      if (h.querySelector('.accent-dot')) return;
      var m = /^([\s\S]*?)([.。])\s*$/.exec(h.textContent);
      if (!m) return;
      h.textContent = '';
      h.appendChild(document.createTextNode(m[1]));
      var dot = document.createElement('span');
      dot.className = 'accent-dot';
      dot.textContent = m[2];
      h.appendChild(dot);
    });
  }

  function applyLang() {
    var lang = state.lang;
    document.documentElement.lang = lang;

    $$('[data-i18n]').forEach(function (el) {
      var v = t(el.getAttribute('data-i18n'));
      if (v != null) el.textContent = v;
    });
    $$('[data-i18n-html]').forEach(function (el) {
      var v = t(el.getAttribute('data-i18n-html'));
      if (v != null) el.innerHTML = v;
    });
    $$('[data-i18n-attr]').forEach(function (el) {
      el.getAttribute('data-i18n-attr').split(',').forEach(function (pair) {
        var i = pair.indexOf(':');
        var v = t(pair.slice(i + 1));
        if (v != null) el.setAttribute(pair.slice(0, i), v);
      });
    });
    applyAlts();
    var title = t(document.documentElement.getAttribute('data-title-key') || 'meta.title');
    if (title) document.title = title;

    $$('.langs button[data-lang]').forEach(function (b) {
      b.setAttribute('aria-pressed', String(b.getAttribute('data-lang') === lang));
    });

    decorateHeadings();
    fillRepo();
    renderTheme();
    renderRelease();
    updateCompareText();
    renderStepButtons();
    renderCount();
    renderLook();
    paintAllButtons();
    applyLangMedia();
    state.applied = true;
    document.dispatchEvent(new CustomEvent('ats:lang', { detail: { lang: lang } }));
  }

  /* small public surface for the other scripts on the site (ideas.js, learn.js) */
  window.ATS = {
    t: function (key) { return t(key); },
    fmt: fmt,
    lang: function () { return state.lang; },
    reduced: reduced,
    noAuto: noAuto,
    ready: function (fn) {
      if (state.applied) { fn(); return; }
      document.addEventListener('ats:lang', function once() { document.removeEventListener('ats:lang', once); fn(); });
    },
    setLayerCount: function (n) { state.layerCount = n; },
    setLook: function (look) { return setLook(look); },
    look: function () { return state.look; }
  };

  function setLang(lang, persist) {
    if (LANGS.indexOf(lang) === -1) lang = 'en';
    loadCjkFont(lang);
    loadDict('en').then(function () { return loadDict(lang); }).then(function () {
      state.lang = lang;
      if (persist) store('ats-lang', lang);
      applyLang();
    });
  }

  /* ---------- release (./latest.json, written by the release workflow) ---------- */
  function normalizeRelease(d) {
    if (!d || typeof d !== 'object') return null;
    var nested = d.sha256 && typeof d.sha256 === 'object';
    if (!nested && !d.releaseUrl && !d.unitypackage && !d.virustotal) return d;
    var sha = nested ? d.sha256 : {};
    var vt = d.virustotal || {};
    return {
      version: d.version,
      released: d.date || d.released,
      release_url: d.releaseUrl || d.release_url,
      unitypackage_url: d.unitypackage || d.unitypackage_url,
      zip_url: d.zip || d.zip_url,
      sha256: nested ? sha.unitypackage : d.sha256,
      vt_url: vt.unitypackage || d.vt_url,
      vt_scanned: vt.scanned != null ? vt.scanned : d.vt_scanned
    };
  }

  function loadRelease() {
    fetch('latest.json', { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (d) {
        d = normalizeRelease(d);
        if (d && typeof d.version === 'string' && /^\d+\.\d+\.\d+/.test(d.version)) {
          state.release = d;
          renderRelease();
        } else {
          throw new Error('bad latest.json');
        }
      })
      .catch(function () {
        state.loadFailed = true;
        renderRelease();
      });
  }

  function formatDate(value) {
    var d = value ? new Date(value) : null;
    if (!d || isNaN(d)) return '';
    return d.toLocaleDateString(state.lang, { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
  }

  function hide(sel, yes) { var el = $(sel); if (el) el.hidden = yes; }

  /* every page has the install strip; only the About page has the checks table, so each lookup is optional */
  function renderRelease() {
    var r = state.release;
    var fallback = $('#release-fallback');
    if (!r) {
      if (state.loadFailed && fallback) { fallback.removeAttribute('data-wait'); fallback.hidden = false; }
      return;
    }
    if (fallback) fallback.hidden = true;

    hide('#row-version', false);
    setText('#rel-version', r.version);
    var releasedText = formatDate(r.released);
    setText('#rel-date', releasedText ? fmt(t('release.released') || 'Released {date}', { date: releasedText }) : '');
    var versionText = fmt(t('release.version') || 'Version {version}', { version: r.version });
    var sub = $('#btn-dl-version');
    if (sub) { sub.textContent = versionText; sub.hidden = false; }
    var meta = $('#inst-version');
    if (meta) { meta.textContent = versionText + ' · '; meta.hidden = false; }

    var sha = typeof r.sha256 === 'string' && /^[0-9a-f]{64}$/i.test(r.sha256) ? r.sha256.toLowerCase() : null;
    hide('#row-sha', !sha);
    hide('#audit-sha-copy', !sha);
    var shaIcon = $('#audit-sha-icon');
    if (shaIcon) {
      shaIcon.setAttribute('class', 'icon ' + (sha ? 'is-ok' : 'is-neutral'));
      shaIcon.firstElementChild.setAttribute('href', sha ? '#i-check' : '#i-dot');
    }
    if (sha) {
      var short = sha.slice(0, 4) + '…' + sha.slice(-4);
      var shaBox = $('#rel-sha'), auditSha = $('#audit-sha');
      if (shaBox) { shaBox.textContent = short; shaBox.title = sha; }
      var c1 = $('#rel-sha-copy'); if (c1) c1.setAttribute('data-copy', sha);
      if (auditSha) { auditSha.textContent = short; auditSha.className = 'mono'; auditSha.title = sha; }
      var c2 = $('#audit-sha-copy'); if (c2) c2.setAttribute('data-copy', sha);
    }

    var release = safeUrl(r.release_url);
    var pkg = safeUrl(r.unitypackage_url);
    var zip = safeUrl(r.zip_url);
    var main = pkg || zip || release;
    if (main) $$('.js-download').forEach(function (a) { a.href = main; });
    var zipLink = $('#rel-zip');
    if (zipLink) { zipLink.hidden = !(pkg && zip); if (zip) zipLink.href = zip; }
    var notes = $('#rel-notes');
    if (notes && release) notes.href = release;
    hide('#row-files', !(release || (pkg && zip)));

    var vt = safeUrl(r.vt_url) || (sha ? 'https://www.virustotal.com/gui/file/' + sha : null);
    if (vt) $$('.js-vt').forEach(function (a) { a.href = vt; a.target = '_blank'; });
    var scanned = r.vt_scanned;
    var scannedDate = typeof scanned === 'string' ? (formatDate(scanned) || scanned) : (scanned === true ? releasedText : '');
    var isScanned = scanned === true || (typeof scanned === 'string' && scanned.length > 0);
    hide('#row-vt', !vt);
    setText('#rel-vt-text', isScanned
      ? (scannedDate ? fmt(t('release.vtScanned') || 'Scanned {date}', { date: scannedDate }) : '')
      : (t('release.vtPending') || 'Scan pending'));
    setText('#audit-vt-data', isScanned && scannedDate
      ? fmt(t('trust.r1.data') || 'Version {version}, scanned {date}', { version: r.version, date: scannedDate })
      : fmt(t('trust.r1.pending') || 'Version {version}, scan pending', { version: r.version }));
    var vtIcon = $('#audit-vt-icon');
    if (vtIcon) {
      vtIcon.setAttribute('class', 'icon ' + (isScanned ? 'is-ok' : 'is-warn'));
      vtIcon.firstElementChild.setAttribute('href', isScanned ? '#i-check' : '#i-warn');
    }

    var file = pkg ? decodeURIComponent(pkg.split('/').pop().split('?')[0]) : null;
    var win = $('#cmd-win'), mac = $('#cmd-mac');
    if (file && /^[\w.\- ]+$/.test(file) && file.indexOf(' ') === -1 && win && mac) {
      win.value = 'certutil -hashfile ' + file + ' SHA256';
      mac.value = 'shasum -a 256 ' + file;
    }
    var more = $('#release-more');
    if (more) more.hidden = $('#row-sha').hidden && $('#row-files').hidden;
  }

  /* ---------- install: copy, send to PC ---------- */
  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) {}
      document.body.removeChild(ta);
      ok ? resolve() : reject(new Error('copy failed'));
    });
  }

  function initCopy() {
    var status = $('#copy-status');
    $$('.copy').forEach(function (btn) {
      var timer = null;
      btn.addEventListener('click', function () {
        var target = btn.getAttribute('data-copy-target');
        var el = target && document.getElementById(target);
        var text = btn.getAttribute('data-copy') || (el && (el.value || el.textContent).trim());
        if (!text) return;
        copyText(text).then(function () {
          btn.textContent = t('ui.copied') || 'Copied';
          if (status) status.textContent = t('ui.copiedStatus') || 'Copied to the clipboard';
          clearTimeout(timer);
          timer = setTimeout(function () {
            btn.textContent = t('ui.copy') || 'Copy';
            if (status) status.textContent = '';
          }, 1500);
        }).catch(function () {});
      });
    });
    /* the VCC link opens another app; if this page still has focus a moment later, offer the copy route */
    $$('a[href^="vcc://"]').forEach(function (a) {
      a.addEventListener('click', function () {
        setTimeout(function () {
          var help = $('#vcc-help');
          if (help && document.hasFocus() && !document.hidden) help.hidden = false;
        }, 1500);
      });
    });
  }

  /* on a phone, Avistry cannot run: offer to send the link to the PC */
  function initSharePc() {
    var box = $('#share-pc-box'), btn = $('#share-pc');
    if (!box || !btn) return;
    var phone = mq('(max-width: 767px)');
    function sync() { box.hidden = !phone.matches; box.classList.toggle('is-on', phone.matches); }
    sync();
    onChange(phone, sync);
    btn.addEventListener('click', function () {
      var url = btn.getAttribute('data-share-url') || location.href;
      if (navigator.share) { navigator.share({ url: url, title: document.title }).catch(function () {}); return; }
      copyText(url).then(function () {
        var label = btn.textContent;
        btn.textContent = t('ui.copied') || 'Copied';
        setTimeout(function () { btn.textContent = label; }, 1500);
      }).catch(function () {});
    });
  }

  /* ---------- theme ---------- */
  function currentTheme() {
    var attr = document.documentElement.getAttribute('data-theme');
    return attr === 'light' ? 'light' : 'dark';
  }

  function renderTheme() {
    var text = t(currentTheme() === 'dark' ? 'skin.toLight' : 'skin.toDark');
    $$('.theme-toggle').forEach(function (btn) {
      if (!text) return;
      btn.setAttribute('aria-label', text);
      var span = $('.theme-text', btn);
      if (span) span.textContent = text;
    });
    var ground = currentTheme() === 'dark' ? '#141218' : '#F3F1F6';
    $$('meta[name="theme-color"]').forEach(function (m) { m.setAttribute('content', ground); });
  }

  function initTheme() {
    $$('.theme-toggle').forEach(function (toggle) {
      toggle.addEventListener('click', function () {
        var next = currentTheme() === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        store('ats-theme', next);
        renderTheme();
      });
    });
  }

  /* ---------- header, phone menu, sticky bar ---------- */
  function initHeader() {
    var hdr = $('#hdr');
    if (!hdr) return;
    var btn = $('#menu-btn'), sheet = $('#sheet');
    var hero = $('#top.hero');
    var clearOk = hdr.classList.contains('is-clear') && !!hero;
    var ticking = false;

    function paintHeader() {
      ticking = false;
      if (!clearOk) return;
      var open = sheet && !sheet.hidden;
      var past = hero.getBoundingClientRect().bottom < 120 || window.scrollY > hero.offsetHeight - 120;
      hdr.classList.toggle('is-clear', !past && !open);
    }
    function onScroll() { if (!ticking) { ticking = true; requestAnimationFrame(paintHeader); } }
    if (clearOk) { window.addEventListener('scroll', onScroll, { passive: true }); paintHeader(); }

    if (btn && sheet) {
      var wide = mq('(min-width: 768px)');
      var open = function (yes, focusBack) {
        sheet.hidden = !yes;
        btn.setAttribute('aria-expanded', String(yes));
        document.documentElement.classList.toggle('sheet-open', yes);
        paintHeader();
        if (yes) { var first = $('a', sheet); if (first) first.focus(); }
        else if (focusBack) btn.focus();
      };
      btn.addEventListener('click', function () { open(sheet.hidden, true); });
      $$('a', sheet).forEach(function (a) { a.addEventListener('click', function () { open(false, false); }); });
      document.addEventListener('keydown', function (e) {
        if (sheet.hidden) return;
        if (e.key === 'Escape') { open(false, true); return; }
        if (e.key !== 'Tab') return;
        var items = [btn].concat($$('a[href], button', sheet).filter(function (x) { return !x.disabled; }));
        var i = items.indexOf(document.activeElement);
        var next = items[(i + (e.shiftKey ? items.length - 1 : 1)) % items.length];
        if (i === -1) next = items[1];
        e.preventDefault();
        next.focus();
      });
      onChange(wide, function () { if (wide.matches && !sheet.hidden) open(false, false); });
    }

    /* the phone bar offers the install until the install strip, or the hero button, is on screen */
    var bar = $('#cta-bar');
    if (bar && 'IntersectionObserver' in window) {
      var seen = { install: false, hero: false };
      var paintBar = function () { bar.classList.toggle('is-on', !seen.install && !seen.hero); };
      var watch = function (el, key) {
        if (!el) return;
        new IntersectionObserver(function (en) { seen[key] = en[0].isIntersecting; paintBar(); }).observe(el);
      };
      watch($('#install'), 'install');
      watch($('#hero-vcc'), 'hero');
      paintBar();
    }
  }

  /* ---------- jump chips: the section on screen is the active chip ---------- */
  function initJump() {
    var links = $$('.jump a[href^="#"]');
    if (!links.length || !('IntersectionObserver' in window)) return;
    var current = null;
    function mark(id) {
      if (id === current) return;
      current = id;
      links.forEach(function (a) {
        var on = a.getAttribute('href') === '#' + id;
        if (on) { a.setAttribute('aria-current', 'true'); try { a.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) {} }
        else a.removeAttribute('aria-current');
      });
    }
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) { if (e.isIntersecting) mark(e.target.id); });
    }, { rootMargin: '-30% 0px -60% 0px', threshold: 0 });
    /* sections without a chip (install, last) also report, so the chip of the section above is not left lit */
    $$('main > section[id]').forEach(function (s) { io.observe(s); });
  }

  /* a link to a closed question opens it */
  function openHashTarget() {
    var id = decodeURIComponent((location.hash || '').slice(1));
    var el = id && document.getElementById(id);
    if (!el) return;
    var d = el.tagName === 'DETAILS' ? el : el.closest('details');
    if (d && !d.open) d.open = true;
    if (el.tagName === 'DETAILS') { try { el.scrollIntoView(); } catch (e) {} }
  }

  /* ---------- compare sliders ---------- */
  var compares = [];
  var hint = { raf: 0, timer: 0 };
  var renderStepButtons = function () {};
  var hero = null;

  /* the resting seam sits just left of the face so the first look is the saved face: phones 25%, desktop at a fixed spot in the picture (about 55% of the art, 60% on 21:9) */
  function restSeam() {
    if (!mq('(min-width: 1024px)').matches) return mq('(min-width: 640px)').matches ? 50 : 25;
    var art = $('#hero-saved .art'), drag = $('#hero-drag');
    if (hero && art && drag) {
      var at = mq('(min-aspect-ratio: 2/1)').matches ? 0.6 : 0.55;
      var a = art.getBoundingClientRect(), d = drag.getBoundingClientRect();
      if (a.width > 0 && d.width > 0) return Math.max(8, Math.min(60, Math.round(((a.left + a.width * at - d.left) / d.width) * 100)));
    }
    return 20;
  }

  function cancelHint() {
    if (hint.raf) cancelAnimationFrame(hint.raf);
    clearTimeout(hint.timer);
    hint.raf = 0;
  }

  function updateCompareText() { compares.forEach(function (c) { c.write(c.value); }); }

  /* one slider: the native range input is the source of truth, the pointer writes the same value.
     cfg: set (element carrying --seam), drag (pointer area), range (input), onwrite */
  function initCompare(cfg, initial) {
    var set = cfg.set, range = cfg.range, drag = cfg.drag;
    var c = { set: set, value: initial, label: null, anim: 0 };
    var down = null, dragging = false;

    c.write = function (v) {
      v = Math.max(0, Math.min(100, v));
      c.value = v;
      set.style.setProperty('--seam', String(Math.round(v * 10) / 10));
      range.value = String(Math.round(v));
      range.setAttribute('aria-valuetext', fmt(t('compare.value') || '{n}% original', { n: Math.round(v) }));
      if (c.label) range.setAttribute('aria-label', c.label());
      if (cfg.onwrite) cfg.onwrite(v);
    };
    c.glide = function (to, ms) {
      cancelHint();
      cancelAnimationFrame(c.anim);
      var from = c.value, start = performance.now();
      if (reduced) { c.write(to); return; }
      (function frame(now) {
        var p = Math.min(1, (now - start) / (ms || 400));
        var e = 1 - Math.pow(1 - p, 3);
        c.write(from + (to - from) * e);
        if (p < 1) c.anim = requestAnimationFrame(frame);
      })(start);
    };
    function fromPointer(e) {
      var r = drag.getBoundingClientRect();
      var zone = cfg.zone ? cfg.zone() : 0;
      c.write(((e.clientX - r.left - zone) / Math.max(1, r.width - zone)) * 100);
    }
    function begin(e) {
      dragging = true;
      c.touched = true;
      cancelAnimationFrame(c.anim);
      try { drag.setPointerCapture(e.pointerId); } catch (err) {}
      fromPointer(e);
    }
    function off() { return set.classList.contains('is-single'); }

    /* mouse and pen grab at once; a finger waits to see which way it goes: sideways moves the seam, up and down scrolls the page, a tap jumps the seam there */
    drag.addEventListener('pointerdown', function (e) {
      if (off() || (e.pointerType === 'mouse' && e.button !== 0)) return;
      cancelHint();
      if (e.pointerType === 'touch') down = { x: e.clientX, y: e.clientY, id: e.pointerId };
      else begin(e);
    });
    drag.addEventListener('pointermove', function (e) {
      if (dragging) { if (e.cancelable && e.pointerType === 'touch') e.preventDefault(); fromPointer(e); return; }
      if (!down || e.pointerId !== down.id) return;
      var dx = Math.abs(e.clientX - down.x), dy = Math.abs(e.clientY - down.y);
      if (dx > 3 && dx >= dy) { down = null; begin(e); }
    });
    function end(e) {
      if (down && e && e.type === 'pointerup') fromPointer(e);
      down = null; dragging = false;
    }
    drag.addEventListener('pointerup', end);
    drag.addEventListener('pointercancel', function () { down = null; dragging = false; });
    /* a finger is captured by the image under it first; that hand-over fires lostpointercapture on the image and must not end the drag */
    drag.addEventListener('lostpointercapture', function (e) { if (e.target === drag) dragging = false; });

    range.addEventListener('input', function () { cancelHint(); c.touched = true; cancelAnimationFrame(c.anim); c.write(Number(range.value)); });
    range.addEventListener('focus', cancelHint);
    range.addEventListener('keydown', function (e) {
      cancelHint();
      c.touched = true;
      cancelAnimationFrame(c.anim);
      if (e.key === 'PageUp' || e.key === 'PageDown') {
        e.preventDefault();
        c.write(c.value + (e.key === 'PageUp' ? 10 : -10));
      }
    });
    c.write(initial);
    compares.push(c);
    return c;
  }

  /* gallery close-ups: each row wipes in when it reaches the screen, then the seam sweeps once to show both sides */
  function initCuReveal() {
    var rail = $('.cu-rail');
    if (!rail || reduced || !('IntersectionObserver' in window)) return;
    rail.classList.add('cu-anim');
    var io = new IntersectionObserver(function (en) {
      en.forEach(function (e) {
        if (!e.isIntersecting) return;
        io.unobserve(e.target);
        var row = e.target, cmp = $('.cmp', row);
        row.classList.add('is-in');
        var c = compares.filter(function (x) { return x.set === cmp; })[0];
        if (!c || cmp.classList.contains('is-single')) return;
        var steps = [[34, 650]], k = 0;
        c.touched = false;
        function next() {
          /* the visitor wins: a drag, a key or a switch to a single picture ends the sweep */
          if (c.touched || k === steps.length || cmp.classList.contains('is-single')) { row.classList.remove('is-sweeping'); return; }
          var s = steps[k++];
          c.glide(s[0], s[1]);
          setTimeout(next, s[1] + 60);
        }
        setTimeout(function () { row.classList.add('is-sweeping'); next(); }, 250);
      });
    }, { threshold: 0.2 });
    rail.querySelectorAll('.cu-row').forEach(function (r) { io.observe(r); });
  }

  /* every .cmp on the page (wings, close-ups) */
  function initCmps() {
    $$('.cmp[data-cmp]').forEach(function (cmp) {
      var view = $('.cmp-view', cmp), range = $('.cmp-range', cmp);
      if (!view || !range) return;
      var ar = cmp.getAttribute('data-ar'), arm = cmp.getAttribute('data-ar-m');
      if (ar) view.style.setProperty('--ar-d', ar);
      if (arm) view.style.setProperty('--ar-m', arm);
      initCompare({ set: cmp, range: range, drag: view }, Number(cmp.getAttribute('data-cmp')) || 50);
    });
  }

  /* ---------- hero: hint, turn, looks ---------- */
  var heroCmp = null;

  /* one small hint that the handle moves, once, only if nobody has touched it */
  function scheduleHint(c) {
    if (reduced || !hero) return;
    var imgs = $$('#hero-bg img');
    var decoded = Promise.all(imgs.map(function (img) { return (img.decode ? img.decode() : Promise.resolve()).catch(function () {}); }));
    var fonts = document.fonts && document.fonts.ready ? document.fonts.ready.catch(function () {}) : Promise.resolve();
    Promise.all([decoded, fonts]).then(function () {
      hint.timer = setTimeout(function () {
        var base = c.value, start = performance.now(), dur = 1500;
        function stop() { cancelHint(); }
        hero.addEventListener('pointerdown', stop, { once: true });
        (function frame(now) {
          var p = Math.min(1, (now - start) / dur);
          c.write(base + 16 * Math.sin(p * Math.PI));
          if (p < 1) hint.raf = requestAnimationFrame(frame);
          else { hint.raf = 0; c.write(base); }
        })(start);
      }, 1200);
    });
  }

  /* the hero turns a little towards the pointer (or the phone's tilt). Frames were rendered in Unity around the still's
     camera, one per degree from -12 to +12: turn-{look}-{saved|orig}-{ratio}-{00-24}.webp. Both layers always draw the same
     frame, so the seam still compares one view. The still stays underneath and is all you see with reduced motion, on
     Save-Data, or until the frames have loaded. */
  var turn = null;
  var TURN_N = 25, TURN_MID = 12;

  function initTurn() {
    if (reduced || saveData || !hero || turn) return;
    var fine = mq('(pointer: fine)').matches;
    var tilt = !fine && 'DeviceOrientationEvent' in window;
    if (!fine && !tilt) return;
    var wideMq = mq('(min-width: 1024px) and (min-aspect-ratio: 2/1)'), tabMq = mq('(min-width: 640px)');
    var drag = $('#hero-drag');
    var layers = [['saved', $('#hero-saved')], ['orig', $('#hero-orig')]].map(function (l) {
      var cv = document.createElement('canvas');
      cv.className = 'turn';
      cv.setAttribute('aria-hidden', 'true');
      l[1].appendChild(cv);
      return { kind: l[0], cv: cv, ctx: null };
    });
    var set = null, gen = 0;
    var target = TURN_MID, shown = TURN_MID, raf = 0, last = 0, inView = true, px = null;
    /* every second frame first (2 degree steps), then the ones between */
    var order = [];
    [0, 1].forEach(function (odd) {
      for (var d = 0; d <= TURN_MID; d++) [TURN_MID - d, TURN_MID + d].forEach(function (i) { if (i % 2 === odd && order.indexOf(i) === -1) order.push(i); });
    });

    function ratio() { return wideMq.matches ? '21x9' : tabMq.matches ? '16x9' : '4x5'; }
    function name(look, kind, r, i) { return 'turn-' + look + '-' + kind + '-' + r + '-' + (i < 10 ? '0' : '') + i + '.webp'; }

    /* one set = one look at one ratio, four downloads at a time */
    function load() {
      var look = state.heroShown, r = ratio(), g = ++gen;
      layers.forEach(function (l) { l.cv.classList.remove('is-on'); });
      hero.classList.remove('is-turn');
      set = { look: look, f: { saved: [], orig: [] }, on: false };
      if (!isReady(name(look, 'saved', r, TURN_MID)) || !isReady(name(look, 'orig', r, TURN_MID))) return;
      var queue = [], running = 0;
      order.forEach(function (i) { layers.forEach(function (l) { queue.push([l.kind, i]); }); });
      function got(kind, i, img) { return function () { if (g === gen) set.f[kind][i] = img; }; }
      function next() {
        while (running < 4 && queue.length) {
          var q = queue.shift(), img = new Image();
          running++;
          img.decoding = 'async';
          img.src = MEDIA + name(look, q[0], r, q[1]);
          (img.decode ? img.decode() : new Promise(function (ok, no) { img.onload = ok; img.onerror = no; }))
            .then(got(q[0], q[1], img), function () {})
            .then(function () { running--; if (g !== gen) return; ready(); next(); });
        }
      }
      next();
    }

    function ready() {
      if (set.on || !set.f.saved[TURN_MID] || !set.f.orig[TURN_MID]) return;
      var w = set.f.saved[TURN_MID].naturalWidth, h = set.f.saved[TURN_MID].naturalHeight;
      layers.forEach(function (l) {
        if (l.cv.width !== w || l.cv.height !== h) { l.cv.width = w; l.cv.height = h; l.ctx = null; }
        l.ctx = l.ctx || l.cv.getContext('2d', { alpha: false });
      });
      set.on = true;
      paint();
      layers.forEach(function (l) { l.cv.classList.add('is-on'); });
      hero.classList.add('is-turn');
      kick();
    }

    /* the nearest loaded frame (frames still on their way are skipped) */
    function frame(kind, i) {
      var a = set.f[kind];
      for (var d = 0; d < TURN_N; d++) {
        if (a[i - d]) return a[i - d];
        if (a[i + d]) return a[i + d];
      }
      return null;
    }

    /* the two frames around the shown angle, the second laid over the first */
    function paint() {
      var i0 = Math.floor(shown), u = shown - i0, i1 = Math.min(TURN_N - 1, i0 + 1);
      var single = hero.classList.contains('is-single');
      layers.forEach(function (l) {
        if (single && l.kind === 'saved') return;
        var c = l.ctx, w = l.cv.width, h = l.cv.height, f0 = frame(l.kind, i0), f1 = frame(l.kind, i1);
        c.globalAlpha = 1;
        c.drawImage(f0, 0, 0, w, h);
        if (u > 0.004 && f1 !== f0) { c.globalAlpha = u; c.drawImage(f1, 0, 0, w, h); }
      });
    }

    /* the pointer across the slider's area picks the frame: far left turns her fully left, far right fully right */
    function aim() {
      if (px === null) { target = TURN_MID; return; }
      var r = drag.getBoundingClientRect();
      var n = Math.max(0, Math.min(1, (px - r.left) / Math.max(1, r.width)));
      target = Math.round(n * (TURN_N - 1));
    }

    function tick(now) {
      raf = 0;
      if (!set || !set.on || !inView) { last = 0; return; }
      if (fine) aim();
      var dt = last ? Math.min(64, now - last) : 16;
      last = now;
      shown += (target - shown) * (1 - Math.exp(-dt / 120));
      var done = Math.abs(target - shown) < 0.004;
      if (done) { shown = target; last = 0; }
      paint();
      if (!done) raf = requestAnimationFrame(tick);
    }
    function kick() { if (!raf && set && set.on && inView) raf = requestAnimationFrame(tick); }

    if (fine) {
      hero.addEventListener('pointermove', function (e) { if (e.pointerType === 'touch') return; px = e.clientX; kick(); });
      hero.addEventListener('pointerleave', function () { px = null; kick(); });
    } else {
      /* Android reports tilt straight away; iOS only after a tap on "Tilt to turn" grants it. The first reading counts as
         straight and that rest point follows slowly, so a phone held at any angle settles back. With no readings the
         button goes away and the seam works by touch as before. */
      var g0 = null, gs = 0, btn = null, ask = DeviceOrientationEvent.requestPermission;
      window.addEventListener('deviceorientation', function (e) {
        var a = (screen.orientation && screen.orientation.angle) || window.orientation || 0;
        var g = a === 90 ? e.beta : a === 270 || a === -90 ? -e.beta : a === 180 ? -e.gamma : e.gamma;
        if (g == null) return;
        if (g0 === null) { g0 = gs = g; if (btn) btn.hidden = true; if (!set) load(); }
        gs += (g - gs) * 0.3;
        g0 += (gs - g0) * 0.01;
        target = Math.round(TURN_MID + TURN_MID * Math.max(-1, Math.min(1, (gs - g0) / 18)));
        kick();
      });
      if (typeof ask === 'function') setTimeout(function () {
        if (g0 !== null) return;
        btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'hero-tilt';
        btn.setAttribute('data-i18n', 'hero.tilt');
        btn.textContent = t('hero.tilt') || 'Tilt to turn';
        btn.addEventListener('click', function () {
          btn.disabled = true;
          ask.call(DeviceOrientationEvent).then(function (s) {
            if (s !== 'granted') btn.hidden = true;
            else setTimeout(function () { if (g0 === null) btn.hidden = true; }, 1500);
          }, function () { btn.hidden = true; });
        });
        $('#hero-pic').appendChild(btn);
      }, 1000);
    }
    if ('IntersectionObserver' in window) new IntersectionObserver(function (en) { inView = en[0].isIntersecting; kick(); }).observe(hero);
    [wideMq, tabMq].forEach(function (m) { onChange(m, function () { if (set) load(); }); });
    turn = {
      look: function () { if (set && set.look !== state.heroShown) load(); },
      paint: function () { if (set && set.on) paint(); }
    };
    /* on desktop the frames load once the page has settled; on phones only when the tilt really reports */
    if (fine) {
      var idle = window.requestIdleCallback || function (fn) { return setTimeout(fn, 600); };
      var go = function () { idle(function () { if (!set) load(); }); };
      if (document.readyState === 'complete') go(); else window.addEventListener('load', go, { once: true });
    }
  }

  /* look chips: every look has its own original shot with the same camera, so the slider works on all of them */
  var BG_SIZES = { '21x9': [1920], '16x9': [1280, 1920], '4x5': [720, 1080] };
  var POP_SIZES = { '21x9': [1920], '16x9': [1280, 1920], '4x5': [720, 1080] };
  var lookCache = {};

  function heroPicture(prefix, look, kind, sizes) {
    var pic = document.createElement('picture');
    pic.className = 'art';
    function url(r, w) { return MEDIA + prefix + '-' + look + (kind ? '-' + kind : '') + '-' + r + '-' + w + '.webp'; }
    function set(r) { return sizes[r].map(function (w) { return url(r, w) + ' ' + w + 'w'; }).join(', '); }
    [['(min-width: 1024px) and (min-aspect-ratio: 2/1)', '21x9'], ['(min-width: 640px)', '16x9']].forEach(function (p) {
      var s = document.createElement('source');
      s.media = p[0]; s.srcset = set(p[1]); s.sizes = '100vw';
      pic.appendChild(s);
    });
    var img = document.createElement('img');
    img.decoding = 'async';
    img.alt = '';
    img.setAttribute('width', '1080'); img.setAttribute('height', '1350');
    img.sizes = '100vw';
    img.srcset = set('4x5');
    img.src = url('4x5', sizes['4x5'][0]);
    pic.appendChild(img);
    return pic;
  }

  function lookEntry(look) {
    if (!lookCache[look]) {
      lookCache[look] = {
        bg: heroPicture('hero', look, 'saved', BG_SIZES),
        pop: heroPicture('pop', look, '', POP_SIZES),
        obg: heroPicture('hero', look, 'orig', BG_SIZES)
      };
      lookCache[look].bg.querySelector('img').alt = t('look.' + look + '.alt') || '';
      lookCache[look].obg.querySelector('img').alt = t('look.' + look + '.orig.alt') || t('look.orig.alt') || '';
    }
    return lookCache[look];
  }

  /* ---------- one look for the whole site ---------- */
  var mediaReady = false;

  /* internal links carry ?look= so the next page opens on the same look even without storage */
  function syncLinks() {
    $$('a[href]').forEach(function (a) {
      if (a._h0 === undefined) a._h0 = a.getAttribute('href');
      var h = a._h0;
      if (!/^(?:index|features|gallery|about|glossary|skills|learn)\.html(?:[?#]|$)/.test(h)) return;
      var hash = '', q = '', i = h.indexOf('#');
      if (i !== -1) { hash = h.slice(i); h = h.slice(0, i); }
      i = h.indexOf('?');
      if (i !== -1) { q = h.slice(i + 1); h = h.slice(0, i); }
      var p = new URLSearchParams(q);
      p.set('look', state.look);
      a.setAttribute('href', h + '?' + p.toString() + hash);
    });
  }

  function rememberLook() {
    store('ats-look', state.look);
    try {
      var u = new URL(location.href);
      u.searchParams.set('look', state.look);
      history.replaceState(history.state, '', u.pathname + u.search + u.hash);
    } catch (e) {}
  }

  function uniq(a) { return a.filter(function (x, i) { return a.indexOf(x) === i; }); }
  function firstReady(chain, suffix) {
    for (var i = 0; i < chain.length; i++) if (isReady(chain[i] + suffix)) return chain[i];
    return null;
  }

  /* file names in one place (the media contract): see-{tab}-{look}, see-{tab}-original-{look} (the before side, lit like the look),
     thumb-{tab}-{look}, light-{scene}-{look}, wings-orbit-{look}, spin-{look}. Each list ends with the crimson file */
  var names = {
    see: function (tab, look) { return look === 'original' ? ['see-' + tab + '-original'] : uniq(['see-' + tab + '-' + look, 'see-' + tab + '-crimson']); },
    before: function (tab, look) { return look === 'original' ? ['see-' + tab + '-original'] : uniq(['see-' + tab + '-original-' + look, 'see-' + tab + '-original-crimson', 'see-' + tab + '-original']); },
    light: function (scene, look) { return uniq(['light-' + scene + '-' + look, 'light-' + scene + '-crimson']); },
    thumb: function (tab, look) {
      return ['thumb-' + tab + '-' + look, 'thumb-' + tab + '-crimson'];
    },
    loop: function (stem, look) { return uniq([stem + '-' + look, stem + '-crimson']); }
  };

  /* point a <picture> at the first file of the chain that exists; the phone file is chosen on its own */
  function setPic(pic, chain) {
    if (!pic) return false;
    var d = firstReady(chain, '.webp'), m = firstReady(chain, '-m.webp');
    if (!d && !m) return false;
    var img = pic.querySelector('img'), src = pic.querySelector('source');
    if (!src && m) { src = document.createElement('source'); src.media = '(max-width: 639px)'; pic.insertBefore(src, img); }
    var du = MEDIA + (d || m) + (d ? '.webp' : '-m.webp');
    if (src) { var mu = m ? MEDIA + m + '-m.webp' : du; if (src.getAttribute('srcset') !== mu) src.srcset = mu; }
    if (img.getAttribute('src') !== du) img.src = du;
    pic.hidden = false;
    return true;
  }

  function setTag(el, original) {
    if (!el) return;
    var key = original ? 'look.original' : 'tag.saved';
    el.setAttribute('data-i18n', key);
    var v = t(key); if (v) el.textContent = v;
  }

  /* captions that name the crimson colours switch to the picked look's line (or a neutral one) */
  function setLegend(el) {
    if (!el) return;
    if (!el._k0) el._k0 = el.getAttribute('data-i18n');
    var look = state.look;
    var key = look === 'crimson' ? el._k0 : (look !== 'original' && el.hasAttribute('data-legend-neutral') ? el.getAttribute('data-legend-neutral') : 'look.' + look + '.legend');
    el.setAttribute('data-i18n', key);
    var v = t(key); if (v) el.textContent = v;
  }

  function applyPictures() {
    if (!mediaReady) return;
    applyAlts();
    var look = state.look, single = look === 'original';
    $$('.cmp[data-see]').forEach(function (cmp) {
      var tab = cmp.getAttribute('data-see');
      var fig = cmp.closest('figure');
      var okO = setPic($('.cmp-orig', cmp), names.before(tab, look));
      var okS = single ? okO : setPic($('.cmp-saved', cmp), names.see(tab, look));
      cmp.classList.toggle('is-single', single);
      if (fig) {
        if (fig.hasAttribute('data-pending-if-missing')) fig.classList.toggle('is-pending', !(okO && okS));
        setTag($('[data-look-tag]', fig), single);
        setLegend($('[data-look-legend]', fig));
      }
    });
    $$('[data-thumb]').forEach(function (a) {
      var img = $('img', a), n = firstReady(names.thumb(a.getAttribute('data-thumb'), look), '.webp');
      if (img && n && img.getAttribute('src') !== MEDIA + n + '.webp') img.src = MEDIA + n + '.webp';
    });
    $$('[data-light]').forEach(function (fig) {
      setPic($('picture', fig), names.light(fig.getAttribute('data-light'), look));
    });
  }

  /* alt texts follow the look and the phone or desktop picture: alt.* keys in common.json, one set per language */
  function altKey(base, look) {
    var phone = phoneMq.matches ? t(base + '.' + look + '.m') : null;
    return phone || t(base + '.' + look);
  }
  function applyAlts() {
    if (!mediaReady) return;
    var look = state.look;
    $$('.cmp[data-see]').forEach(function (cmp) {
      var tab = cmp.getAttribute('data-see');
      var si = $('.cmp-saved img', cmp), oi = $('.cmp-orig img', cmp);
      var sa = look === 'original' ? null : altKey('alt.see.' + tab, look);
      if (si && sa) si.alt = sa;
      var oa = altKey('alt.see.' + tab, 'original');
      if (oa && look !== 'original') oa += ' ' + (t('alt.lit.' + look) || '');
      if (oi && oa) oi.alt = oa.trim();
    });
    $$('[data-thumb]').forEach(function (a) {
      var img = $('img', a), tab = a.getAttribute('data-thumb');
      var al = look === 'original' ? t('alt.see.' + tab + '.original.m') : t('alt.thumb.' + tab + '.' + look);
      if (img && al) img.alt = al;
    });
    $$('[data-light]').forEach(function (fig) {
      var img = $('img', fig), al = t('alt.light.' + fig.getAttribute('data-light') + '.' + look);
      if (img && al) img.alt = al;
    });
    $$('video[data-look-loop]').forEach(function (v) {
      var al = t('alt.orbit.' + look);
      if (al) v.setAttribute('aria-label', al);
    });
  }

  /* loops that follow the look (turntable, wings orbit): the first name that exists wins, the poster goes with it */
  function applyVideos(bind) {
    if (!mediaReady) return;
    $$('video[data-look-loop]').forEach(function (video) {
      var chain = names.loop(video.getAttribute('data-look-loop'), state.look);
      var name = null;
      for (var i = 0; i < chain.length && !name; i++) {
        if (isReady(chain[i] + '.mp4') || isReady(chain[i] + '.webm') || isReady(chain[i] + '-m.mp4') || isReady(chain[i] + '-m.webm')) name = chain[i];
      }
      if (!name) return;
      video.setAttribute('data-loop', name);
      var pm = phoneMq.matches && isReady(name + '-m-poster.webp') ? '-m' : '';
      if (isReady(name + pm + '-poster.webp')) video.setAttribute('poster', MEDIA + name + pm + '-poster.webp');
      var vf = video.closest('figure');
      setTag($('[data-look-tag]', vf), state.look === 'original' && name === chain[0]);
      setLegend($('[data-look-legend]', vf));
      if (bind) bindLoop(video);
    });
  }

  function renderLook() {
    var img = $('#hero-saved img');
    if (img) { var a = t('look.' + state.heroShown + '.alt'); if (a) img.alt = a; }
    var oimg = $('#hero-orig img');
    if (oimg) { var oa = t('look.' + state.heroShown + '.orig.alt') || t('look.orig.alt'); if (oa) oimg.alt = oa; }
    $$('.looks').forEach(function (group) {
      $$('button[data-look]', group).forEach(function (b) {
        var on = b.getAttribute('data-look') === state.look;
        b.setAttribute('aria-checked', String(on));
        b.tabIndex = on ? 0 : -1;
      });
    });
    if (hero) hero.classList.toggle('is-single', state.look === 'original');
    if (turn) turn.paint();
    var range = $('#hero-range');
    if (range) range.disabled = state.look === 'original';
  }

  function swapHero(look) {
    if (!$('#hero-saved') || look === state.heroShown) return Promise.resolve();
    var chip = $('#looks button[data-look="' + state.look + '"]');
    var e = lookEntry(look);
    var pairs = [['#hero-saved', e.bg], ['#pop-saved', e.pop], ['#hero-orig', e.obg]].map(function (p) {
      var host = $(p[0]);
      return { host: host, pic: p[1], old: $$('.art', host) };
    });
    var loadingTimer = setTimeout(function () { if (chip) chip.classList.add('is-loading'); }, 100);
    state.heroShown = look;
    if (turn) turn.look();
    pairs.forEach(function (p) {
      p.pic.style.opacity = '0';
      p.pic.style.transition = reduced ? 'none' : 'opacity 280ms ease-out';
      p.host.insertBefore(p.pic, $('.turn', p.host));
    });
    function dec(p) { var im = p.querySelector('img'); return (im.decode ? im.decode() : Promise.resolve()).catch(function () {}); }
    var timeout = new Promise(function (r) { setTimeout(r, 4000); });
    return Promise.race([Promise.all(pairs.map(function (p) { return dec(p.pic); })), timeout]).then(function () {
      clearTimeout(loadingTimer);
      if (chip) chip.classList.remove('is-loading');
      if (state.heroShown !== look) return;
      requestAnimationFrame(function () {
        pairs.forEach(function (p) { p.pic.style.opacity = '1'; });
        setTimeout(function () {
          pairs.forEach(function (p) { p.old.forEach(function (n) { if (n.parentNode && n !== p.pic) n.parentNode.removeChild(n); }); });
        }, reduced ? 0 : 320);
      });
    });
  }

  /* the single place where the look changes: chips, hero, every picture and loop, links, memory */
  function setLook(look, silent) {
    if (!validLook(look)) return Promise.resolve();
    state.look = look;
    if (!silent) rememberLook();
    syncLinks();
    applyPictures();
    applyVideos(true);
    var p = look === 'original' ? Promise.resolve() : swapHero(look);
    renderLook();
    return p;
  }

  function radioKeys(chips, pick) {
    chips.forEach(function (b, i) {
      b.addEventListener('keydown', function (e) {
        var n = null;
        if (e.key === 'ArrowRight' || e.key === 'ArrowDown') n = chips[(i + 1) % chips.length];
        else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') n = chips[(i - 1 + chips.length) % chips.length];
        if (n) { e.preventDefault(); n.focus(); pick(n); }
      });
    });
  }

  /* on phones the .unitypackage steps start folded so the install section stays short */
  function foldSteps() {
    var d = $('#unity-steps');
    if (d && !mq('(min-width: 768px)').matches) d.open = false;
  }

  function initHero() {
    hero = $('#top.hero');
    if (!hero) return;
    heroCmp = initCompare({
      set: hero, range: $('#hero-range'), drag: $('#hero-drag'),
      zone: function () { return 0; }
    }, restSeam());
    heroCmp.label = function () { return t('compare.label') || 'Compare original and saved'; };
    scheduleHint(heroCmp);
    onChange(mq('(min-width: 1024px)'), function () { heroCmp.write(restSeam()); });
    /* the other two looks are fetched when the browser is idle */
    if ($('#looks')) {
      var idle = window.requestIdleCallback || function (fn) { return setTimeout(fn, 1500); };
      if (!saveData) setTimeout(function () { idle(function () { HERO_LOOKS.forEach(lookEntry); }); }, 4000);
    }
  }

  /* every look picker on the page (home hero, gallery bar): same state, same keys */
  function initLookChips() {
    $$('.looks').forEach(function (group) {
      var chips = $$('button[data-look]', group);
      function pick(b) {
        var look = b.getAttribute('data-look');
        if (look === state.look) return;
        var r = hero && heroCmp ? restSeam() : 0;
        setLook(look);
        if (heroCmp && look !== 'original') heroCmp.glide(r, 400);
      }
      chips.forEach(function (b) {
        b.addEventListener('click', function () { pick(b); });
        if (group.id === 'looks') ['pointerenter', 'focus'].forEach(function (ev) { b.addEventListener(ev, function () { var l = b.getAttribute('data-look'); if (l !== 'original') lookEntry(l); }); });
      });
      radioKeys(chips, pick);
    });
  }

  /* ---------- media: which v4 files exist yet ---------- */
  var readyList = {};
  function isReady(name) { return !!readyList[name]; }

  function loadReady() {
    return fetch(MEDIA + 'ready.json', { cache: 'no-cache' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (d) { (d && d.ready || []).forEach(function (n) { readyList[n] = 1; }); })
      .catch(function () {});
  }

  /* pictures of the tool, and pictures with words in them, can have a copy per language in media/v4/<lang>/ under the
     same name; English and any copy not listed in ready.json use the plain file */
  var LANG_MEDIA = /^(ui-|skill-tree-|ai-helper|tour|safe-files|map-)[^/\s]*$/;
  function langDir(name) { var l = state.lang; return l && l !== 'en' && isReady(l + '/' + name) ? l + '/' : ''; }
  var relangHow = function () {};

  function applyLangMedia() {
    if (!mediaReady) return;
    var sel = ['src', 'srcset', 'poster', 'href'].map(function (a) { return '[' + a + '^="' + MEDIA + '"]'; }).join(',');
    $$(sel).forEach(function (el) {
      /* the tour player (tour.js) picks its own language */
      if (el.closest('[data-own-lang]')) return;
      ['src', 'srcset', 'poster', 'href'].forEach(function (a) {
        var v = el.getAttribute(a), key = '_en_' + a;
        if (!v || v.indexOf(MEDIA) !== 0) return;
        if (el[key] == null) { var f = v.slice(MEDIA.length); el[key] = LANG_MEDIA.test(f) ? f : ''; }
        if (!el[key]) return;
        var nv = MEDIA + langDir(el[key]) + el[key];
        if (nv === v) return;
        el.setAttribute(a, nv);
        if (el.tagName === 'SOURCE' && el.parentNode && el.parentNode.tagName === 'VIDEO') el.parentNode.load();
      });
    });
    $$('video[data-loop]').forEach(function (v) { if ('_lm' in v && v._lm !== loopFile(v)) bindLoop(v); });
    relangHow();
  }

  /* a still without a file yet stays out of the page; its plate shows a quiet note instead */
  function initStills() {
    $$('[data-v4]').forEach(function (el) {
      var name = el.getAttribute('data-v4');
      if (isReady(name)) {
        if (el.tagName === 'SOURCE') el.srcset = MEDIA + name;
        else el.src = MEDIA + name;
        var pic = el.closest('picture');
        if (pic && pic.hidden) pic.hidden = false;
        var plate = el.closest('.safe-plate');
        if (plate) plate.classList.add('has-still');
      } else {
        var fig = el.closest('[data-pending-if-missing]');
        if (fig) fig.classList.add('is-pending');
      }
    });
  }

  /* tiny logo marks behind the page, drifting slower than the scroll */
  function initMarks() {
    if ($('.bg-marks')) return;
    var m = document.createElement('div');
    m.className = 'bg-marks';
    m.setAttribute('aria-hidden', 'true');
    document.body.insertBefore(m, document.body.firstChild);
    document.body.classList.add('has-marks');
    if (reduced) return;
    var raf = 0;
    function move() { raf = 0; m.style.transform = 'translate3d(0,' + (-((window.scrollY * 0.18) % 640)).toFixed(1) + 'px,0)'; }
    window.addEventListener('scroll', function () { if (!raf) raf = requestAnimationFrame(move); }, { passive: true });
    move();
  }

  /* ---------- video: one frame, one rule ---------- */
  var regs = [];
  var external = null;
  var vio = null;
  var phoneMq = mq('(max-width: 639px)');
  onChange(phoneMq, applyAlts);

  function paintButton(r) {
    if (!r.btn) return;
    var paused = r.el.paused;
    r.btn.classList.toggle('is-paused', paused);
    var label = t(paused ? 'ui.play' : 'ui.pause') || (paused ? 'Play video' : 'Pause video');
    r.btn.setAttribute('aria-label', label);
  }
  function paintAllButtons() { regs.forEach(paintButton); }

  /* at most one video plays automatically: the one most in view, past 60% */
  function arbitrate() {
    var best = null, top = 0;
    /* while a video with sound plays, loops start only when pressed */
    var ext = external && !external.paused;
    regs.forEach(function (r) {
      var score = 0;
      if (r.ready && r.ratio > 0) {
        if (r.want === 'on') score = 2 + r.ratio;
        else if (!ext && r.want === 'auto' && !noAuto && r.ratio >= 0.6) score = 1 + r.ratio;
      }
      if (score > top) { top = score; best = r; }
    });
    regs.forEach(function (r) {
      if (r === best) {
        if (r.el.paused) {
          var p = r.el.play();
          if (p && p.catch) p.catch(function () { r.want = 'off'; paintButton(r); });
        }
      } else if (!r.el.paused) r.el.pause();
      paintButton(r);
    });
  }

  /* every loop can open larger in a popup; its buttons sit top right, away from the window's own menus */
  var ICON_ZOOM = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/></svg>';
  var ICON_CLOSE = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M6 6l12 12M18 6 6 18"/></svg>';
  var box = null;
  function addZoom(r) {
    var host = r.btn.parentNode;
    if (!host || $('.vz', host)) return;
    var b = document.createElement('button');
    b.type = 'button'; b.className = 'vz'; b.innerHTML = ICON_ZOOM;
    b.setAttribute('data-i18n-attr', 'aria-label:ui.zoom');
    b.setAttribute('aria-label', t('ui.zoom') || 'Open larger');
    b.addEventListener('click', function () { openBox(r, b); });
    host.insertBefore(b, r.btn);
  }
  function openBox(r, opener) {
    var src = r.el.currentSrc;
    if (!src) return;
    if (!box) {
      box = document.createElement('dialog');
      box.className = 'vbox';
      box.innerHTML = '<div class="vbox-in"><video muted playsinline loop></video><button type="button" class="pp"></button><button type="button" class="vz">' + ICON_CLOSE + '</button><p class="vbox-cap"></p></div>';
      document.body.appendChild(box);
      var bv = $('video', box), bp = $('.pp', box), bc = $('.vz', box);
      var paint = function () {
        bp.classList.toggle('is-paused', bv.paused);
        bp.setAttribute('aria-label', t(bv.paused ? 'ui.play' : 'ui.pause') || (bv.paused ? 'Play video' : 'Pause video'));
      };
      bv.addEventListener('play', paint); bv.addEventListener('pause', paint);
      bp.addEventListener('click', function () { if (bv.paused) bv.play().catch(function () {}); else bv.pause(); });
      bc.addEventListener('click', function () { box.close(); });
      box.addEventListener('click', function (e) { if (e.target === box) box.close(); });
      box.addEventListener('close', function () {
        bv.pause(); bv.removeAttribute('src'); bv.load();
        if (box._opener) box._opener.focus();
      });
      box._paint = paint;
    }
    var v = $('video', box), cap = $('.vbox-cap', box), close = $('.vz', box);
    close.setAttribute('aria-label', t('ui.close') || 'Close');
    var fig = r.el.closest('figure');
    var txt = fig && $('.plate-cap span:not(.tag)', fig) ? $('.plate-cap span:not(.tag)', fig).textContent : '';
    cap.textContent = txt; cap.hidden = !txt;
    v.setAttribute('aria-label', r.el.getAttribute('aria-label') || '');
    v.poster = r.el.poster || '';
    v.src = src;
    box._opener = opener;
    box.showModal();
    v.currentTime = r.el.currentTime || 0;
    v.play().catch(function () {});
    box._paint();
  }

  function register(video, btn) {
    if (video._r) return video._r;
    var r = { el: video, btn: btn || (video.parentNode && $('.pp', video.parentNode)), ratio: 0, want: 'auto', ready: false };
    video._r = r;
    regs.push(r);
    if (!vio && 'IntersectionObserver' in window) {
      vio = new IntersectionObserver(function (entries) {
        entries.forEach(function (en) { if (en.target._r) en.target._r.ratio = en.intersectionRatio; });
        arbitrate();
      }, { threshold: [0, 0.1, 0.3, 0.6, 0.8, 1] });
    }
    if (vio) vio.observe(video);
    video.addEventListener('play', function () { paintButton(r); });
    video.addEventListener('pause', function () { paintButton(r); });
    if (r.btn) {
      r.btn.addEventListener('click', function () {
        r.want = r.el.paused ? 'on' : 'off';
        if (r.el.ended) r.el.currentTime = 0;
        arbitrate();
      });
      addZoom(r);
    }
    return r;
  }

  /* a video with native controls (the makeover) wins over the loops while it plays */
  function initOneAtATime() {
    /* the tour may already be playing in the mini player when this page was swapped in */
    var tour = $('video[data-own-lang]');
    if (tour && !tour.paused) external = tour;
    document.addEventListener('play', function (e) {
      if (e.target._r) return;
      external = e.target;
      arbitrate();
    }, true);
    document.addEventListener('pause', function (e) { if (e.target === external) { external = null; arbitrate(); } }, true);
    document.addEventListener('ended', function (e) { if (e.target === external) { external = null; arbitrate(); } }, true);
  }

  function addSources(video, base) {
    video.textContent = '';
    ['webm:video/webm', 'mp4:video/mp4'].forEach(function (x) {
      var s = document.createElement('source');
      s.src = base + '.' + x.split(':')[0]; s.type = x.split(':')[1];
      video.appendChild(s);
    });
    video.load();
  }

  /* switching a look-following loop (spin, wings orbit) keeps the angle: the current frame is held on a canvas
     while the new file loads, then the new file jumps to the same point in the turn */
  function holdFrame(video) {
    if (!video.duration || video.readyState < 2 || !video.videoWidth) return null;
    var old = video.parentNode && $('.v-hold', video.parentNode);
    if (old) old.remove();
    var cv = document.createElement('canvas');
    cv.width = video.videoWidth; cv.height = video.videoHeight;
    try { cv.getContext('2d').drawImage(video, 0, 0); } catch (e) { cv = null; }
    if (cv) {
      cv.className = 'v-hold';
      cv.setAttribute('aria-hidden', 'true');
      cv.style.cssText = 'position:absolute;pointer-events:none;left:' + video.offsetLeft + 'px;top:' + video.offsetTop + 'px;width:' + video.offsetWidth + 'px;height:' + video.offsetHeight + 'px;object-fit:' + getComputedStyle(video).objectFit;
      video.parentNode.insertBefore(cv, video.nextSibling);
    }
    return { frac: (video.currentTime % video.duration) / video.duration, cv: cv };
  }

  function resumeAt(video, hold) {
    var done = false;
    function drop() {
      if (done) return;
      done = true;
      video.removeEventListener('seeked', onSeeked);
      video.removeEventListener('error', onSeeked, true);
      if (hold.cv) requestAnimationFrame(function () { hold.cv.remove(); });
    }
    function onSeeked() { drop(); }
    video.preload = 'auto';
    video.addEventListener('loadedmetadata', function once() {
      video.removeEventListener('loadedmetadata', once);
      try { video.currentTime = hold.frac * video.duration; } catch (e) { drop(); }
    });
    video.addEventListener('seeked', onSeeked);
    video.addEventListener('error', onSeeked, true);
    /* on a slow line the held frame stays until the new file can show the same angle; this is only a last resort */
    setTimeout(drop, 20000);
  }

  /* a loop: files {name}.mp4 and .webm in media/v4 (and {name}-m for phones) once they are listed in ready.json */
  function loopFile(video) {
    var name = video.getAttribute('data-loop');
    var m = phoneMq.matches && (isReady(name + '-m.mp4') || isReady(name + '-m.webm')) ? '-m' : '';
    if (!(isReady(name + m + '.mp4') || isReady(name + m + '.webm'))) return null;
    return langDir(name + m + '.mp4') + name + m;
  }

  function bindLoop(video) {
    var r = register(video);
    var file = loopFile(video);
    var ok = file !== null;
    video._lm = file;
    var plate = video.closest('.loop-plate');
    if (plate) plate.classList.toggle('is-pending', !ok);
    var host = video.parentNode;
    if (host && host.id === 'files-loop') {
      host.hidden = !ok;
      var mock = host.parentNode && $('.files-mock', host.parentNode);
      if (mock) mock.hidden = ok;
    }
    if (ok) {
      r.ready = true;
      var hold = holdFrame(video);
      addSources(video, MEDIA + file);
      if (hold) resumeAt(video, hold);
    } else {
      r.ready = false;
      video.pause();
      video.textContent = '';
    }
    arbitrate();
  }

  function initLoops() {
    var videos = $$('video[data-loop]');
    videos.forEach(bindLoop);
    onChange(phoneMq, function () { videos.forEach(bindLoop); });
    /* chips that swap the loop in one plate (the six selection tools) */
    $$('[data-loops-for]').forEach(function (group) {
      var video = $(group.getAttribute('data-loops-for'));
      if (!video) return;
      var chips = $$('button[data-loop]', group);
      function pick(b) {
        chips.forEach(function (x) { x.setAttribute('aria-pressed', String(x === b)); });
        video.setAttribute('data-loop', b.getAttribute('data-loop'));
        var cap = group.getAttribute('data-cap-id') ? document.getElementById(group.getAttribute('data-cap-id')) : $('#sel-cap');
        var key = b.getAttribute('data-cap');
        if (cap && key) { cap.setAttribute('data-i18n', key); var v = t(key); if (v) cap.textContent = v; }
        if (video._r) video._r.want = 'auto';
        bindLoop(video);
      }
      chips.forEach(function (b) { b.addEventListener('click', function () { pick(b); }); });
    });
  }

  /* ---------- how it works: one video, four cuts ---------- */
  function initHow() {
    var list = $('#how-steps');
    var video = $('#how-video');
    if (!list || !video) return;
    var steps = $$('.how-step', list);
    var r = register(video, $('.pp', video.parentNode));
    r.ready = true;
    var cap = $('#how-cap');
    var cur = 0;

    function base(i) { var f = 'how-' + (i + 1) + (phoneMq.matches ? 'm' : ''); return MEDIA + langDir(f + '.mp4') + f; }
    function load(i) {
      video._lm = base(i);
      video.poster = video._lm + '-poster.webp';
      addSources(video, video._lm);
    }
    relangHow = function () { if (video._lm !== base(cur)) setStep(cur, false); };
    function setStep(i, user) {
      cur = i;
      steps.forEach(function (s, j) {
        s.classList.toggle('is-on', j === i);
        s.classList.toggle('is-done', j < i);
        s.querySelector('button').setAttribute('aria-pressed', String(j === i));
      });
      video.setAttribute('aria-label', t('how.' + (i + 1) + '.alt') || '');
      if (cap) cap.textContent = steps[i].querySelector('.sb').textContent;
      list.style.setProperty('--p', String(Math.min(1, i / (steps.length - 1))));
      load(i);
      if (user) r.want = 'on';
      arbitrate();
    }
    steps.forEach(function (s, i) { s.querySelector('button').addEventListener('click', function () { setStep(i, true); }); });
    video.addEventListener('timeupdate', function () {
      if (!video.duration) return;
      list.style.setProperty('--p', String(Math.min(1, (cur + video.currentTime / video.duration) / (steps.length - 1))));
    });
    video.addEventListener('ended', function () { if (r.want !== 'off') setStep((cur + 1) % steps.length, false); });
    onChange(phoneMq, function () { setStep(cur, false); });
    renderStepButtons = function () {
      if (cap) cap.textContent = steps[cur].querySelector('.sb').textContent;
      video.setAttribute('aria-label', t('how.' + (cur + 1) + '.alt') || '');
      paintButton(r);
    };
    setStep(0, false);
  }

  /* ---------- the honest step count ---------- */
  var foldMq = mq('(max-width: 639px)');
  function renderCount() {
    $$('[data-n-for]').forEach(function (el) {
      var id = el.getAttribute('data-n-for');
      var ol = $('#count-' + id);
      var tpl = t(el.getAttribute('data-i18n'));
      if (ol && tpl) el.textContent = fmt(tpl, { n: ol.children.length });
    });
    var ol = $('#count-ps'), more = $('.count-more');
    if (!ol || !more) return;
    var items = Array.prototype.slice.call(ol.children);
    var limit = Number(ol.getAttribute('data-fold')) || 4;
    var folded = ol._folded !== false && foldMq.matches && items.length > limit;
    items.forEach(function (li, i) { li.hidden = folded && i >= limit; });
    more.hidden = !(foldMq.matches && items.length > limit);
    more.textContent = folded ? fmt(t('count.more') || 'Show all {n} steps', { n: items.length }) : (t('count.less') || 'Show fewer');
    more.setAttribute('aria-expanded', String(!folded));
  }
  function initCount() {
    var more = $('.count-more');
    if (!more) return;
    more.addEventListener('click', function () { var ol = $('#count-ps'); ol._folded = ol._folded === false; renderCount(); });
    onChange(foldMq, renderCount);
  }

  /* ---------- faq: one open at a time ---------- */
  function initFaq() {
    $$('.faq').forEach(function (group) {
      var all = $$('details', group);
      all.forEach(function (d) {
        d.addEventListener('toggle', function () {
          if (d.open) $$('details', document).forEach(function (o) { if (o !== d && o.open && o.closest('.faq')) o.open = false; });
        });
      });
    });
  }

  /* ---------- start (tour.js sets ATS_SOFT when it swaps a page in and runs this file again) ---------- */
  function start() {
    initCopy();
    initSharePc();
    initTheme();
    initHeader();
    initJump();
    initHero(); foldSteps();
    initCmps();
    initCuReveal();
    initCount();
    initFaq();
    initOneAtATime();
    $$('.langs button[data-lang]').forEach(function (b) {
      b.addEventListener('click', function () { setLang(b.getAttribute('data-lang'), true); });
    });
    fillRepo();
    renderTheme();
    initMarks();
    loadRelease();
    initLookChips();
    if (/[?&]look=/.test(location.search)) store('ats-look', state.look);
    if (state.look !== 'crimson') setLook(state.look, true); else syncLinks();
    loadReady().then(function () {
      mediaReady = Object.keys(readyList).length > 0;
      initStills();
      applyPictures();
      applyVideos(false);
      initLoops();
      initHow();
      initTurn();
      applyLangMedia();
    });
    openHashTarget();
    window.addEventListener('hashchange', openHashTarget);
    setLang(detectLang(), false);
  }
  if (window.ATS_SOFT) start(); else document.addEventListener('DOMContentLoaded', start);
})();
