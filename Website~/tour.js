/* The tour video: a language switch on the video, and a mini player that keeps playing while the visitor moves
   around the site. Moving to another page while the tour plays swaps the page in place (fetch, swap the body,
   pushState) instead of loading it, so the video element never leaves the document. Without JS, or when a fetch
   fails, the links are ordinary links. Loaded before app.js on every page. */
(function () {
  'use strict';

  var add = EventTarget.prototype.addEventListener, rem = EventTarget.prototype.removeEventListener;

  /* the page scripts (app.js, ideas.js, learn.js) listen on window, document and media queries; those listeners are
     noted here so a page swap can remove them before the next page's scripts run again */
  var tracked = [];
  function trackOn(obj, proto) {
    obj.addEventListener = function (type, fn, opts) {
      var self = this == null ? window : this;
      tracked.push([self, type, fn, opts]);
      return add.call(self, type, fn, opts);
    };
    if (proto) obj.addListener = function (fn) { this.addEventListener('change', fn); };
  }
  trackOn(window);
  trackOn(document);
  if (window.MediaQueryList) trackOn(MediaQueryList.prototype, true);
  function untrackAll() {
    tracked.forEach(function (l) { try { rem.call(l[0], l[1], l[2], l[3]); } catch (e) {} });
    tracked = [];
  }

  var reduced = !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
  var LANGS = [['en', 'EN', 'EN', 'English'], ['ja', '日本語', 'JA', '日本語'], ['ko', '한국어', 'KO', '한국어'], ['zh-CN', '中文', 'ZH', '中文']];
  function dir(l) { return 'media/v4/' + (l && l !== 'en' ? l + '/' : ''); }
  function T(key, fb) { var v = window.ATS && window.ATS.t(key); return v != null ? v : fb; }
  function $(sel, root) { return (root || document).querySelector(sel); }
  function store(key, value) {
    try {
      if (value === undefined) return localStorage.getItem(key);
      localStorage.setItem(key, value);
    } catch (e) {}
    return null;
  }
  function svg(d) { return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="' + d + '"/></svg>'; }
  function button(cls, key, fb, html) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = cls;
    b.setAttribute('aria-label', T(key, fb));
    b.setAttribute('data-i18n-attr', 'aria-label:' + key);
    b.innerHTML = html;
    return b;
  }

  /* focus moves to the video only for keyboard users: a video with focus takes Space and the arrow keys */
  var lastKey = false;
  add.call(document, 'keydown', function () { lastKey = true; }, true);
  add.call(document, 'pointerdown', function () { lastKey = false; }, true);
  function focusVideo() { if (lastKey && P.video) try { P.video.focus({ preventScroll: true }); } catch (e) {} }

  /* ---------- the player ---------- */
  var P = { wrap: null, video: null, lang: null, picked: false, mode: 'off', active: false, plate: null, ph: null, io: null, ratio: 1, wantInline: false };
  var dock = null;

  function pageLang() {
    if (window.ATS) return window.ATS.lang();
    var l = store('ats-lang');
    return LANGS.some(function (x) { return x[0] === l; }) ? l : 'en';
  }

  function paintLangs() {
    if (!P.wrap) return;
    P.wrap.querySelectorAll('.tlang button').forEach(function (b) { b.setAttribute('aria-pressed', String(b.getAttribute('data-tl') === P.lang)); });
  }

  /* the four files share one timeline, so the new file picks up at the same second; the current frame stays on a
     canvas until the new file has seeked there */
  function setLang(l, user) {
    if (user) P.picked = true;
    if (!P.video || l === P.lang) { P.lang = l; paintLangs(); return; }
    var v = P.video, t = v.currentTime || 0, playing = !v.paused && !v.ended, hold = null;
    P.lang = l;
    paintLangs();
    if (t > 0 && v.readyState >= 2 && v.videoWidth) {
      hold = document.createElement('canvas');
      hold.className = 'tv-hold';
      hold.width = v.videoWidth; hold.height = v.videoHeight;
      try { hold.getContext('2d').drawImage(v, 0, 0); P.wrap.insertBefore(hold, v.nextSibling); } catch (e) { hold = null; }
    }
    var rate = v.playbackRate;
    v.poster = dir(l) + 'tour-poster.webp';
    if (t > 0 || playing) v.preload = 'auto';
    v.src = dir(l) + 'tour.mp4';
    var done = false;
    function drop() {
      if (done) return;
      done = true;
      v.removeEventListener('seeked', drop);
      v.removeEventListener('error', drop);
      if (hold) { var h = hold; h.style.opacity = '0'; setTimeout(function () { h.remove(); }, reduced ? 0 : 180); }
    }
    if (t > 0) {
      v.addEventListener('loadedmetadata', function once() {
        v.removeEventListener('loadedmetadata', once);
        v.playbackRate = rate;
        try { v.currentTime = Math.min(t, (v.duration || t) - 0.05); } catch (e) { drop(); }
        if (playing) v.play().catch(function () {});
      });
      v.addEventListener('seeked', drop);
      v.addEventListener('error', drop);
      setTimeout(drop, 15000);
    } else if (playing) v.play().catch(function () {});
  }

  function adopt(video) {
    var wrap = document.createElement('div');
    wrap.className = 'tvid';
    video.parentNode.insertBefore(wrap, video);
    wrap.appendChild(video);
    video.setAttribute('data-own-lang', '');
    Array.prototype.slice.call(video.querySelectorAll('source')).forEach(function (s) { s.remove(); });
    video.preload = 'none';

    var g = document.createElement('div');
    g.className = 'tlang';
    g.setAttribute('role', 'group');
    g.setAttribute('aria-label', T('player.lang', 'Tour language'));
    g.setAttribute('data-i18n-attr', 'aria-label:player.lang');
    LANGS.forEach(function (x) {
      var b = document.createElement('button');
      b.type = 'button';
      b.setAttribute('data-tl', x[0]);
      b.setAttribute('lang', x[0]);
      b.setAttribute('aria-label', x[3]);
      b.innerHTML = '<span class="l-long" aria-hidden="true">' + x[1] + '</span><span class="l-short" aria-hidden="true">' + x[2] + '</span>';
      b.addEventListener('click', function () { setLang(x[0], true); });
      g.appendChild(b);
    });
    wrap.appendChild(g);

    /* the switch fades while the video plays and the pointer rests, like the native controls */
    var idle = 0;
    function wake() {
      wrap.classList.remove('is-idle');
      clearTimeout(idle);
      idle = setTimeout(function () { if (!video.paused) wrap.classList.add('is-idle'); }, 2400);
    }
    ['pointermove', 'pointerdown', 'focusin'].forEach(function (ev) { wrap.addEventListener(ev, wake); });

    /* a click on the video gives it focus, and the browser's player then keeps Space, PageDown and the arrows for
       itself; after a mouse click those keys scroll the page as usual (keyboard users keep the player's keys) */
    var SCROLL_KEYS = { ' ': 1, PageDown: 1, PageUp: 1, ArrowDown: 1, ArrowUp: 1, Home: 1, End: 1 };
    var byKey = false;
    video.addEventListener('focus', function () { byKey = lastKey; });
    wrap.addEventListener('keydown', function (e) {
      if (e.target !== video || !SCROLL_KEYS[e.key] || e.altKey || e.ctrlKey || e.metaKey) return;
      if (byKey) return;
      e.preventDefault();
      video.blur();
      var h = window.innerHeight * 0.85, by = { ' ': e.shiftKey ? -h : h, PageDown: h, PageUp: -h, ArrowDown: 40, ArrowUp: -40 }[e.key];
      var how = reduced || e.repeat ? 'auto' : 'smooth';
      if (e.key === 'Home') window.scrollTo({ top: 0, behavior: how });
      else if (e.key === 'End') window.scrollTo({ top: document.documentElement.scrollHeight, behavior: how });
      else window.scrollBy({ top: by, behavior: how });
    }, true);
    video.addEventListener('pause', function () { clearTimeout(idle); wrap.classList.remove('is-idle'); });
    video.addEventListener('play', function () {
      P.active = true;
      wake();
      /* only one voice at a time: another video with sound pauses */
      document.querySelectorAll('video').forEach(function (o) { if (o !== video && !o.paused && !o.muted) o.pause(); });
      if (P.mode === 'inline' && P.ratio < 0.35) setMode('mini');
    });
    video.addEventListener('timeupdate', function () {
      if (dock && video.duration) dock.style.setProperty('--p', String(video.currentTime / video.duration));
    });

    P.wrap = wrap;
    P.video = video;
    P.lang = null;
    setLang(P.picked && P.lang ? P.lang : pageLang());
    return wrap;
  }

  /* ---------- the mini player: position and size per viewer, never off screen ---------- */
  var GAP = 8;
  var box = { x: 0, y: 0, w: 0 };
  function narrow() { return document.documentElement.clientWidth < 640; }
  function insets() {
    var p = $('.tdock-probe');
    if (!p) {
      p = document.createElement('div');
      p.className = 'tdock-probe';
      p.setAttribute('aria-hidden', 'true');
      document.body.appendChild(p);
    }
    var s = getComputedStyle(p);
    return { t: parseFloat(s.paddingTop) || 0, r: parseFloat(s.paddingRight) || 0, b: parseFloat(s.paddingBottom) || 0, l: parseFloat(s.paddingLeft) || 0 };
  }
  /* the player may grow to the whole screen (minus the margins), so a big screen gets a big player */
  function bounds() {
    var i = insets(), vw = document.documentElement.clientWidth, vh = window.innerHeight;
    var bar = dock ? $('.tdock-bar', dock).offsetHeight || 36 : 36;
    var maxW = Math.max(160, Math.min(vw - i.l - i.r - 2 * GAP, ((vh - i.t - i.b - 2 * GAP - bar) * 16) / 9));
    return { vw: vw, l: i.l + GAP, t: i.t + GAP, r: vw - i.r - GAP, b: vh - i.b - GAP, maxW: maxW, minW: Math.min(narrow() ? 160 : 220, maxW), bar: bar };
  }
  function height(w, b) { return Math.round((w * 9) / 16) + b.bar; }
  function saved() {
    try { return (JSON.parse(store('ats-tour-dock') || '{}') || {})[narrow() ? 'm' : 'd'] || null; } catch (e) { return null; }
  }
  function place(save) {
    if (!dock) return;
    var b = bounds();
    box.w = Math.round(Math.max(b.minW, Math.min(b.maxW, box.w)));
    var h = height(box.w, b);
    box.x = Math.round(Math.max(b.l, Math.min(b.r - box.w, box.x)));
    box.y = Math.round(Math.max(b.t, Math.min(b.b - h, box.y)));
    dock.style.setProperty('--w', box.w + 'px');
    dock.style.setProperty('--x', box.x + 'px');
    dock.style.setProperty('--y', box.y + 'px');
    dock.classList.toggle('is-left', box.x + box.w / 2 > (b.l + b.r) / 2);
    if (save) {
      var spanX = Math.max(1, b.r - b.l - box.w), spanY = Math.max(1, b.b - b.t - h);
      var key = narrow() ? 'm' : 'd', all = {};
      try { all = JSON.parse(store('ats-tour-dock') || '{}') || {}; } catch (e) {}
      all[key] = { ax: (box.x - b.l) / spanX, ay: (box.y - b.t) / spanY, fw: box.w / b.vw };
      store('ats-tour-dock', JSON.stringify(all));
    }
  }
  /* size and spot are kept as shares of the screen (width) and of the free room (spot), so the player scales with the
     window and top right stays top right */
  function restore() {
    var b = bounds(), s = saved();
    var fw = s && s.fw > 0 ? s.fw : narrow() ? 0.56 : 0.3;
    box.w = Math.max(b.minW, Math.min(b.maxW, fw * b.vw));
    var h = height(box.w, b), hdr = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--hdr-h')) || 56;
    if (s && isFinite(s.ax) && isFinite(s.ay)) {
      box.x = b.l + s.ax * Math.max(0, b.r - b.l - box.w);
      box.y = b.t + s.ay * Math.max(0, b.b - b.t - h);
    } else {
      box.x = b.r - box.w;
      box.y = b.t + hdr + 4;
    }
    place(false);
  }

  function makeDock() {
    if (dock) return dock;
    dock = document.createElement('div');
    dock.className = 'tdock';
    dock.hidden = true;
    dock.setAttribute('role', 'region');
    dock.setAttribute('aria-label', T('player.region', 'Tour video'));
    dock.setAttribute('data-i18n-attr', 'aria-label:player.region');
    var bar = document.createElement('div');
    bar.className = 'tdock-bar';
    var grip = button('tdock-grip', 'player.move', 'Move the player (arrow keys)', '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><g class="dots"><circle cx="9" cy="6" r="1.6"/><circle cx="15" cy="6" r="1.6"/><circle cx="9" cy="12" r="1.6"/><circle cx="15" cy="12" r="1.6"/><circle cx="9" cy="18" r="1.6"/><circle cx="15" cy="18" r="1.6"/></g></svg>');
    var title = document.createElement('span');
    title.className = 'tdock-title';
    title.setAttribute('data-i18n', 'player.title');
    title.textContent = T('player.title', 'Tour');
    var full = button('tdock-btn', 'player.full', 'Back to the full-size video', svg('M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5'));
    var close = button('tdock-btn', 'player.close', 'Close the player', svg('M6 6l12 12M18 6 6 18'));
    var prog = document.createElement('span');
    prog.className = 'tdock-prog';
    prog.setAttribute('aria-hidden', 'true');
    bar.appendChild(grip); bar.appendChild(title); bar.appendChild(full); bar.appendChild(close); bar.appendChild(prog);
    var media = document.createElement('div');
    media.className = 'tdock-media';
    var size = button('tdock-size', 'player.resize', 'Resize the player (arrow keys)', svg('M8 16 16 8M12 17l5-5'));
    dock.appendChild(bar); dock.appendChild(media); dock.appendChild(size);
    document.body.appendChild(dock);

    full.addEventListener('click', backToFull);
    close.addEventListener('click', closeMini);

    /* drag by the bar (not its buttons); touch, pen and mouse alike */
    var drag = null;
    function start(e, kind) {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      e.preventDefault();
      drag = { kind: kind, id: e.pointerId, x0: e.clientX, y0: e.clientY, bx: box.x, by: box.y, bw: box.w, left: dock.classList.contains('is-left') };
      dock.classList.add('is-drag');
      try { e.currentTarget.setPointerCapture(e.pointerId); } catch (err) {}
    }
    function move(e) {
      if (!drag || e.pointerId !== drag.id) return;
      /* the button came up somewhere this page did not hear about: the drag is over */
      if (e.pointerType === 'mouse' && e.buttons === 0) { end(e); return; }
      var dx = e.clientX - drag.x0, dy = e.clientY - drag.y0;
      if (drag.kind === 'move') { box.x = drag.bx + dx; box.y = drag.by + dy; }
      else {
        /* the knob sits on the corner away from the screen edge; the other edges stay put */
        var byX = drag.left ? -dx : dx, byY = (dy * 16) / 9;
        var w = drag.bw + (Math.abs(byX) > Math.abs(byY) ? byX : byY);
        var b = bounds();
        w = Math.max(b.minW, Math.min(b.maxW, w));
        box.w = w;
        box.x = drag.left ? drag.bx + drag.bw - w : drag.bx;
        box.y = drag.by;
      }
      place(false);
    }
    function end(e) {
      if (!drag || (e && e.pointerId != null && e.pointerId !== drag.id)) return;
      drag = null;
      dock.classList.remove('is-drag');
      place(true);
    }
    bar.addEventListener('pointerdown', function (e) { if (!e.target.closest('button') || e.target.closest('.tdock-grip')) start(e, 'move'); });
    size.addEventListener('pointerdown', function (e) { start(e, 'size'); });
    [bar, size].forEach(function (el) {
      el.addEventListener('pointermove', move);
      el.addEventListener('pointerup', end);
      el.addEventListener('pointercancel', end);
      el.addEventListener('lostpointercapture', end);
    });
    add.call(window, 'blur', function () { end(); });
    grip.addEventListener('keydown', function (e) {
      var s = e.shiftKey ? 64 : 24, k = { ArrowLeft: [-s, 0], ArrowRight: [s, 0], ArrowUp: [0, -s], ArrowDown: [0, s] }[e.key];
      if (!k) return;
      e.preventDefault();
      box.x += k[0]; box.y += k[1];
      place(true);
    });
    size.addEventListener('keydown', function (e) {
      var s = e.shiftKey ? 64 : 24, k = { ArrowLeft: -s, ArrowUp: -s, ArrowRight: s, ArrowDown: s, '-': -s, '+': s, '=': s }[e.key];
      if (!k) return;
      e.preventDefault();
      var left = dock.classList.contains('is-left'), right = box.x + box.w;
      box.w += k;
      var b = bounds();
      box.w = Math.max(b.minW, Math.min(b.maxW, box.w));
      if (left) box.x = right - box.w;
      place(true);
    });
    add.call(window, 'resize', function () { if (!dock.hidden && !drag) restore(); });
    return dock;
  }

  /* ---------- modes: inline in the home page's tour plate, or mini in the corner ---------- */
  function placeholder() {
    if (!P.plate) return null;
    if (P.ph && P.ph.parentNode === P.plate) return P.ph;
    var ph = document.createElement('div');
    ph.className = 'tour-ph';
    ph.style.backgroundImage = 'url("' + dir(P.lang) + 'tour-poster.webp")';
    ph.innerHTML = '<div class="tour-ph-in"><p data-i18n="player.here"></p><button type="button" class="btn btn-primary btn-sm" data-i18n="player.back"></button></div>';
    $('p', ph).textContent = T('player.here', 'Playing in the mini player');
    var b = $('button', ph);
    b.textContent = T('player.back', 'Bring it back here');
    b.addEventListener('click', function () { setMode('inline'); focusVideo(); });
    P.ph = ph;
    return ph;
  }

  function setMode(mode) {
    if (!P.wrap || mode === P.mode) return;
    var had = P.wrap.contains(document.activeElement) ? document.activeElement : null;
    if (mode === 'mini') {
      makeDock();
      var wasHidden = dock.hidden;
      if (P.plate) { var ph = placeholder(); if (ph && ph.parentNode !== P.plate) P.plate.appendChild(ph); }
      $('.tdock-media', dock).appendChild(P.wrap);
      dock.hidden = false;
      if (wasHidden) {
        restore();
        if (!reduced) { dock.classList.remove('is-in'); void dock.offsetWidth; dock.classList.add('is-in'); }
      }
    } else if (mode === 'inline') {
      if (!P.plate) return;
      if (P.ph && P.ph.parentNode) P.ph.remove();
      P.plate.appendChild(P.wrap);
      if (dock) dock.hidden = true;
      if (!reduced) { P.wrap.classList.remove('is-back'); void P.wrap.offsetWidth; P.wrap.classList.add('is-back'); }
    }
    P.mode = mode;
    /* a keyboard user keeps their place; after a mouse click the moved video lets go of the keys */
    if (had && lastKey) try { had.focus({ preventScroll: true }); } catch (e) {}
    else if (had && document.activeElement === had) had.blur();
  }

  function closeMini() {
    var inDock = dock && dock.contains(document.activeElement);
    if (P.video) P.video.pause();
    P.active = false;
    if (P.plate) { setMode('inline'); if (inDock) focusVideo(); return; }
    if (dock) dock.hidden = true;
    P.mode = 'off';
    if (inDock && lastKey) { var m = $('#main'); if (m) { m.setAttribute('tabindex', '-1'); m.focus({ preventScroll: true }); } }
  }

  function backToFull() {
    if (P.plate) {
      setMode('inline');
      try { P.plate.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'center' }); } catch (e) { P.plate.scrollIntoView(); }
      focusVideo();
      return;
    }
    P.wantInline = true;
    go('index.html#tour', true);
  }

  /* called on the first load and after every swap: find this page's tour plate, if it has one */
  function attach() {
    if (P.io) { P.io.disconnect(); P.io = null; }
    P.ratio = 1;
    var plate = $('#tour .plate-body');
    P.plate = plate;
    P.ph = null;
    if (!plate) {
      if (P.active && P.wrap) setMode('mini');
      return;
    }
    var fresh = $('video', plate);
    if (P.active && P.wrap) {
      if (fresh) fresh.remove();
      if (P.wantInline) { P.mode = 'mini'; setMode('inline'); }
      else { P.mode = 'off'; setMode('mini'); }
    } else {
      if (P.wrap && P.wrap.parentNode) P.wrap.remove();
      if (dock) dock.hidden = true;
      P.wrap = null; P.video = null; P.active = false;
      if (!fresh) return;
      adopt(fresh);
      P.mode = 'inline';
    }
    P.wantInline = false;
    if ('IntersectionObserver' in window) {
      P.io = new IntersectionObserver(function (en) {
        P.ratio = en[0].intersectionRatio;
        if (P.mode === 'inline' && P.ratio < 0.35 && P.video && !P.video.paused) setMode('mini');
        /* a gap between the two thresholds, so a small scroll at the edge does not flip it back and forth */
        else if (P.mode === 'mini' && P.ratio >= 0.6) setMode('inline');
      }, { threshold: [0, 0.35, 0.6] });
      P.io.observe(plate);
    }
  }

  /* Esc closes the mini player unless a menu, a dialog or a text field has it */
  add.call(document, 'keydown', function (e) {
    if (e.key !== 'Escape' || e.defaultPrevented || !dock || dock.hidden || P.mode !== 'mini') return;
    if (document.documentElement.classList.contains('sheet-open') || $('dialog[open]')) return;
    var a = document.activeElement;
    if (a && !dock.contains(a) && /^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName)) return;
    closeMini();
  });
  add.call(document, 'ats:lang', function () {
    if (!P.picked && P.video) setLang(pageLang());
    if (P.ph) {
      $('p', P.ph).textContent = T('player.here', 'Playing in the mini player');
      $('button', P.ph).textContent = T('player.back', 'Bring it back here');
    }
    var t = dock && $('.tdock-title', dock);
    if (t) t.textContent = T('player.title', 'Tour');
  });
  /* a muted loop is fine; another voice pauses the tour */
  add.call(document, 'play', function (e) {
    if (P.video && e.target !== P.video && !e.target.muted && !P.video.paused) P.video.pause();
  }, true);

  /* ---------- soft navigation, only while the tour plays or sits in the corner ---------- */
  function softOk() { return !!(P.wrap && P.active && (P.mode === 'mini' || !P.video.paused)); }
  function pageOf(path) { return path.replace(/\/$/, '/index.html'); }
  var rendered = pageOf(location.pathname);
  var base = location.pathname.replace(/[^/]*$/, '');
  var seq = 0;

  function siteUrl(a) {
    if (!a || (a.target && a.target !== '_self') || a.hasAttribute('download')) return null;
    var u;
    try { u = new URL(a.getAttribute('href'), location.href); } catch (e) { return null; }
    if (u.origin !== location.origin || u.pathname.indexOf(base) !== 0) return null;
    var rest = u.pathname.slice(base.length);
    return rest === '' || /^[\w-]+\.html$/.test(rest) ? u : null;
  }

  add.call(document, 'click', function (e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || !softOk()) return;
    var a = e.target.closest && e.target.closest('a[href]');
    var u = siteUrl(a);
    if (!u) return;
    if (pageOf(u.pathname) === rendered) {
      if (u.hash) return;
      e.preventDefault();
      window.scrollTo({ top: 0, behavior: reduced ? 'auto' : 'smooth' });
      return;
    }
    e.preventDefault();
    go(u.href, true);
  });

  add.call(window, 'popstate', function (e) {
    if (pageOf(location.pathname) === rendered) return;
    go(location.href, false, e.state && typeof e.state.y === 'number' ? e.state.y : null);
  });

  function copyAttr(from, to, name) {
    if (from && from.hasAttribute(name)) to.setAttribute(name, from.getAttribute(name)); else to.removeAttribute(name);
  }

  function go(href, push, y) {
    var my = ++seq, root = document.documentElement;
    if (push) {
      try { history.replaceState(Object.assign({}, history.state, { y: window.scrollY }), ''); } catch (e) {}
    }
    if (P.mode === 'inline' && P.video && !P.video.paused && !P.wantInline) setMode('mini');
    root.classList.add('soft-fade', 'is-soft');
    fetch(href, { credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
      .then(function (html) {
        if (my !== seq) return;
        var d = new DOMParser().parseFromString(html, 'text/html');
        if (!d.body || !d.getElementById('main')) throw new Error('not a page');
        var srcs = Array.prototype.slice.call(d.body.querySelectorAll('script[src]')).map(function (s) { return s.getAttribute('src'); })
          .filter(function (src) { return !/(^|\/)tour\.js(\?|$)/.test(src); });
        return Promise.all(srcs.map(code)).then(function (codes) {
          if (my !== seq) return;
          if (push) history.pushState({ y: 0 }, '', href);
          if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
          swap(d, y, srcs, codes);
        });
      })
      .catch(function () {
        if (my !== seq) return;
        if (push) location.href = href; else location.reload();
      });
  }

  /* the page scripts are fetched (from the cache, mostly) before the swap and then run in one go, like deferred
     scripts: app.js announces the language with an event that learn.js and ideas.js must already be listening for */
  var codes = {};
  function code(src) {
    if (!codes[src]) codes[src] = fetch(src, { credentials: 'same-origin' }).then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); });
    codes[src].catch(function () { delete codes[src]; });
    return codes[src];
  }

  function swap(d, y, srcs, texts) {
    var root = document.documentElement, body = document.body, nr = d.documentElement;
    untrackAll();
    copyAttr(nr, root, 'data-page');
    copyAttr(nr, root, 'data-title-key');
    root.classList.remove('sheet-open');
    document.title = d.title;
    ['meta[name="description"]', 'link[rel="canonical"]', 'meta[property="og:title"]', 'meta[property="og:url"]', 'meta[property="og:description"]'].forEach(function (sel) {
      var o = document.head.querySelector(sel), n = d.head.querySelector(sel);
      if (o && n) { copyAttr(n, o, 'content'); copyAttr(n, o, 'href'); }
    });
    body.className = d.body.className;
    Array.prototype.slice.call(body.childNodes).forEach(function (n) { if (n !== dock) body.removeChild(n); });

    var frag = document.createDocumentFragment();
    Array.prototype.slice.call(d.body.childNodes).forEach(function (n) { frag.appendChild(document.importNode(n, true)); });
    Array.prototype.slice.call(frag.querySelectorAll('noscript')).forEach(function (n) { n.remove(); });
    Array.prototype.slice.call(frag.querySelectorAll('script')).forEach(function (s) {
      if (s.getAttribute('src')) { s.remove(); return; }
      var ns = document.createElement('script');
      ns.textContent = s.textContent;
      s.parentNode.replaceChild(ns, s);
    });
    body.insertBefore(frag, dock);
    rendered = pageOf(location.pathname);
    attach();

    var hash = decodeURIComponent((location.hash || '').slice(1));
    if (!hash && y == null) window.scrollTo(0, 0);

    /* app.js starts at once when ATS_SOFT is set */
    window.ATS_SOFT = true;
    var shown = false;
    function show() {
      if (shown) return;
      shown = true;
      rem.call(document, 'ats:lang', show);
      root.classList.remove('is-soft');
    }
    add.call(document, 'ats:lang', show);
    setTimeout(show, 1500);
    srcs.forEach(function (src, i) {
      var s = document.createElement('script');
      s.setAttribute('data-src', src);
      s.textContent = texts[i] + '\n//# sourceURL=' + new URL(src, location.href).href;
      body.insertBefore(s, dock);
    });
    var el = hash && document.getElementById(hash);
    if (el) el.scrollIntoView();
    else if (y != null) window.scrollTo(0, y);
    /* the words arrive a moment later and can move the target; follow it unless the visitor scrolled meanwhile */
    if (el || y != null) {
      var at = window.scrollY;
      var again = function () {
        rem.call(document, 'ats:lang', again);
        clearTimeout(stop);
        if (window.scrollY === at) { if (el) el.scrollIntoView(); else window.scrollTo(0, y); }
      };
      var stop = setTimeout(function () { rem.call(document, 'ats:lang', again); }, 2000);
      add.call(document, 'ats:lang', again);
    }
    var h = $('#main h1') || $('#main h2');
    if (P.mode === 'inline' && P.active && P.video && lastKey) focusVideo();
    else if (h) { h.setAttribute('tabindex', '-1'); h.classList.add('soft-focus'); try { h.focus({ preventScroll: true }); } catch (e) {} }
  }

  function viewportWidth() { document.documentElement.style.setProperty('--vw', document.documentElement.clientWidth + 'px'); }
  add.call(window, 'resize', viewportWidth);
  function boot() { viewportWidth(); attach(); }
  if (document.readyState === 'loading') add.call(document, 'DOMContentLoaded', boot); else boot();
})();
