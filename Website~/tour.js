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

  var reduced = (!!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) || document.documentElement.classList.contains('perf'));
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
  function focusVideo() { var b = P.wrap && $('.tc-play', P.wrap); if (lastKey && b) try { b.focus({ preventScroll: true }); } catch (e) {} }

  /* ---------- the player ---------- */
  /* hold: until this time a click in the player does not send it to the mini player (only scrolling away does) */
  var P = { wrap: null, video: null, lang: null, picked: false, mode: 'off', active: false, plate: null, ph: null, io: null, ratio: 1, pin: 1, wantInline: false, hold: 0, centred: false };
  function held() { return performance.now() < P.hold; }
  /* the video leaves for the corner when it is scrolled away from while it plays: under 35% in view and lower than
     the best view since the last click in it (a click on a half-hidden player is not scrolling away). While a click
     holds it, the check runs again when the hold ends */
  var recheck = 0;
  function checkMini() {
    clearTimeout(recheck);
    if (P.mode !== 'inline' || !P.video || P.video.paused || P.ratio >= 0.35 || P.ratio > P.pin - 0.04) return;
    if (held()) { recheck = setTimeout(checkMini, P.hold - performance.now() + 30); return; }
    setMode('mini');
  }
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
      try { hold.getContext('2d').drawImage(v, 0, 0); v.parentNode.insertBefore(hold, v.nextSibling); } catch (e) { hold = null; }
    }
    var rate = rateOf(v);
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

  /* ---------- our own controls: a bar under the picture, so the burned-in subtitles stay clear ---------- */
  var SPEEDS = [0.75, 1, 1.25, 1.5, 2];
  var DUR = 180; /* all four files; shown until the video has loaded */
  /* iPhones keep the volume on the hardware buttons: there the slider is left out and the speaker only mutes */
  var canVolume = (function () { try { var t = document.createElement('video'); t.volume = 0.5; return t.volume === 0.5; } catch (e) { return false; } })();
  function clock(s) { s = Math.max(0, Math.floor(s || 0)); return Math.floor(s / 60) + ':' + ('0' + (s % 60)).slice(-2); }
  function icon(paths, cls) { return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false" class="' + cls + '">' + paths + '</svg>'; }
  var SPEAKER = '<path d="M4 9.5h3.5L12 6v12l-4.5-3.5H4z"/>';
  var I = {
    play: icon('<path d="M8 5.2v13.6L19 12z"/>', 'f i-play'),
    pause: icon('<path d="M7 5h3.5v14H7zM13.5 5H17v14h-3.5z"/>', 'f i-pause'),
    back: icon('<path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3"/><path d="M4.5 4.5v4h4"/><text x="12.6" y="15.2" text-anchor="middle">10</text>', 'i-skip'),
    fwd: icon('<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3"/><path d="M19.5 4.5v4h-4"/><text x="11.4" y="15.2" text-anchor="middle">10</text>', 'i-skip'),
    vol: icon(SPEAKER + '<path class="w1" d="M15.5 9.5a3.5 3.5 0 0 1 0 5"/><path class="w2" d="M18 7a7 7 0 0 1 0 10"/>', 'i-vol'),
    mute: icon(SPEAKER + '<path d="M16 9.5l5 5M21 9.5l-5 5"/>', 'i-mute'),
    fs: icon('<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>', 'i-fs') + icon('<path d="M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5"/>', 'i-fsx')
  };

  /* ---------- effects: a tape-stop when the tour pauses, a spin-up when it starts, a glitch between languages ---------- */
  function pitch(v, keep) { v.preservesPitch = keep; v.mozPreservesPitch = keep; v.webkitPreservesPitch = keep; }
  function rateOf(v) { return v.defaultPlaybackRate || 1; }
  /* the playback rate glides, and with the pitch let go the voice glides with it */
  var rampId = 0;
  function ramp(v, from, to, ms, done) {
    cancelAnimationFrame(rampId);
    var t0 = performance.now();
    pitch(v, false);
    (function step(now) {
      var k = Math.min(1, (now - t0) / ms), e = k * k * (3 - 2 * k);
      try { v.playbackRate = from + (to - from) * e; } catch (err) {}
      if (k < 1) rampId = requestAnimationFrame(step); else if (done) done();
    })(t0);
  }
  function stopRamp(v) { cancelAnimationFrame(rampId); try { v.playbackRate = rateOf(v); } catch (e) {} pitch(v, true); }

  /* a short synthesised glitch: crushed noise and square blips, then a quick tuning sweep; as loud as the video */
  var ac = null;
  function glitchSound(v) {
    var level = v.muted ? 0 : v.volume;
    if (level <= 0) return 0;
    try {
      ac = ac || new (window.AudioContext || window.webkitAudioContext)();
      if (ac.state === 'suspended') ac.resume();
    } catch (e) { return 0; }
    var c = ac, t = c.currentTime + 0.005, out = c.createGain();
    out.gain.value = 0.22 * level;
    out.connect(c.destination);
    var len = Math.floor(c.sampleRate * 0.25), buf = c.createBuffer(1, len, c.sampleRate), d = buf.getChannelData(0), hold = 0, s = 0;
    for (var i = 0; i < len; i++) { if (hold-- <= 0) { s = Math.random() * 2 - 1; hold = 3 + (Math.random() * 22 | 0); } d[i] = s; }
    var at = t, bursts = 5;
    for (var n = 0; n < bursts; n++) {
      var dur = 0.018 + Math.random() * 0.03, g = c.createGain(), src, peak = n % 2 ? 0.95 : 0.55;
      g.gain.setValueAtTime(0, at);
      g.gain.linearRampToValueAtTime(peak, at + 0.002);
      g.gain.setValueAtTime(peak, at + dur - 0.003);
      g.gain.linearRampToValueAtTime(0, at + dur);
      g.connect(out);
      if (n % 2) {
        src = c.createBufferSource();
        src.buffer = buf;
        var f = c.createBiquadFilter();
        f.type = 'bandpass'; f.frequency.value = 900 + Math.random() * 4500; f.Q.value = 1.6;
        src.connect(f); f.connect(g);
        src.start(at, Math.random() * 0.2, dur);
      } else {
        src = c.createOscillator();
        src.type = 'square';
        src.frequency.setValueAtTime(140 + Math.random() * 1600, at);
        src.frequency.setValueAtTime(140 + Math.random() * 1600, at + dur / 2);
        src.connect(g);
        src.start(at); src.stop(at + dur);
      }
      at += dur + Math.random() * 0.012;
    }
    var o = c.createOscillator(), og = c.createGain();
    o.type = 'sine';
    o.frequency.setValueAtTime(2600, at);
    o.frequency.exponentialRampToValueAtTime(380, at + 0.08);
    og.gain.setValueAtTime(0.4, at);
    og.gain.exponentialRampToValueAtTime(0.001, at + 0.1);
    o.connect(og); og.connect(out);
    o.start(at); o.stop(at + 0.11);
    return bursts;
  }

  /* the real voice stutters: the same few frames again and again, at changing speeds */
  function stutter(v, ms) {
    if (v.paused) return 0;
    var t = v.currentTime, n = 0, speeds = [1.4, 0.6, 1.9, 0.5];
    pitch(v, false);
    var id = setInterval(function () {
      try { v.playbackRate = speeds[n++ % 4] * rateOf(v); v.currentTime = t; } catch (e) {}
    }, 50);
    setTimeout(function () { clearInterval(id); stopRamp(v); }, ms);
    return Math.max(1, Math.floor(ms / 50));
  }

  /* the picture: torn strips, moved blocks, a rolling frame, a red/cyan split, a flash and scanlines over the last
     frame. Drawn at 384x216 and scaled up: cheap, and the big pixels are part of the look */
  function glitchFx(box, src) {
    var w = 384, h = 216, c = document.createElement('canvas'), off = document.createElement('canvas'), tint = document.createElement('canvas');
    c.className = 'tv-glitch';
    c.width = off.width = tint.width = w;
    c.height = off.height = tint.height = h;
    box.appendChild(c);
    box.classList.remove('is-shake');
    void box.offsetWidth;
    box.classList.add('is-shake');
    var x = c.getContext('2d'), o = off.getContext('2d'), t = tint.getContext('2d'), endAt = 0;
    var g = { src: src, power: 1, split: 0, end: function () { if (!endAt) endAt = performance.now(); } };
    var cols = ['67,220,255', '255,111,145', '107,78,255'];
    function size(s) {
      return s.videoWidth ? [s.videoWidth, s.videoHeight] : s.naturalWidth ? [s.naturalWidth, s.naturalHeight] : [s.width, s.height];
    }
    /* one colour channel of the torn frame, shifted sideways; red plus cyan adds up to the full picture */
    function channel(color, dx) {
      t.globalCompositeOperation = 'copy';
      t.drawImage(off, 0, 0);
      t.globalCompositeOperation = 'multiply';
      t.fillStyle = color;
      t.fillRect(0, 0, w, h);
      /* keep the empty edges empty (multiply fills them with the colour) */
      t.globalCompositeOperation = 'destination-in';
      t.drawImage(off, 0, 0);
      x.drawImage(tint, dx, 0);
    }
    (function frame(now) {
      if (!c.isConnected) return;
      var p = g.power;
      if (endAt) {
        var k = (now - endAt) / 150;
        if (k >= 1) { c.remove(); box.classList.remove('is-shake'); return; }
        p *= 1 - k;
      }
      var s = g.src, sz = s ? size(s) : [0, 0], has = !!(s && sz[0]);
      x.globalCompositeOperation = 'source-over';
      x.clearRect(0, 0, w, h);
      if (has) {
        try {
          o.clearRect(0, 0, w, h);
          var roll = Math.random() < 0.3 * p ? (Math.random() * h * 0.35) | 0 : 0;
          var jx = (Math.random() - 0.5) * 34 * p;
          o.drawImage(s, jx, -roll, w, h);
          if (roll) o.drawImage(s, jx, h - roll, w, h);
          for (var i = 0, n = 2 + (Math.random() * 7 * p | 0); i < n; i++) {
            var y = Math.random() * h | 0, sh = 2 + (Math.random() * 30 | 0);
            o.drawImage(s, 0, y * sz[1] / h, sz[0], sh * sz[1] / h, (Math.random() - 0.5) * 160 * p, y, w, sh);
          }
          for (var b = 0, nb = Math.random() * 5 * p | 0; b < nb; b++) {
            var bw = 16 + Math.random() * 100, bh = 6 + Math.random() * 44;
            o.drawImage(off, Math.random() * (w - bw), Math.random() * (h - bh), bw, bh, Math.random() * (w - bw), Math.random() * (h - bh), bw, bh);
          }
          g.split = Math.round((3 + Math.random() * 11) * p);
          x.globalCompositeOperation = 'lighter';
          channel('#ff0000', -g.split);
          channel('#00ffff', g.split);
          x.globalCompositeOperation = 'source-over';
        } catch (e) {}
      }
      for (var q = 0, m = 1 + (Math.random() * 4 * p | 0); q < m; q++) {
        x.fillStyle = 'rgba(' + cols[q % 3] + ',' + ((0.3 + Math.random() * 0.45) * p).toFixed(2) + ')';
        x.fillRect(Math.random() * w, Math.random() * h, 12 + Math.random() * 200, 1 + Math.random() * 10);
      }
      if (Math.random() < 0.14 * p) {
        x.globalCompositeOperation = 'difference';
        x.fillStyle = '#fff';
        x.fillRect(0, 0, w, h);
        x.globalCompositeOperation = 'source-over';
      }
      x.fillStyle = 'rgba(0,0,0,' + (0.3 * p).toFixed(2) + ')';
      for (var yy = 0; yy < h; yy += 3) x.fillRect(0, yy, w, 1);
      c.style.opacity = String(Math.min(1, p * 1.5));
      requestAnimationFrame(frame);
    })(performance.now());
    return g;
  }

  /* the console: a few lines of what the player does, typed in under the language switch and gone again */
  function stamp(t) { t = Math.max(0, t || 0); return clock(t) + '.' + ('0' + Math.floor((t % 1) * 100)).slice(-2); }
  function console_(media) {
    var el = null, timer = 0, queue = 0, pend = [];
    function ensure() {
      if (el && el.isConnected) return el;
      el = document.createElement('div');
      el.className = 'tv-term';
      el.setAttribute('aria-hidden', 'true');
      el.innerHTML = '<div class="tt-bar"><i></i><i></i><i></i><span>avistry ~ tour</span></div><div class="tt-body"></div>';
      media.appendChild(el);
      return el;
    }
    function put(text, cls) {
      var b = $('.tt-body', ensure()), d = document.createElement('div');
      d.className = 'tt-line' + (cls ? ' ' + cls : '');
      d.textContent = text;
      d.style.setProperty('--n', String(Math.max(4, Math.min(44, text.length))));
      b.appendChild(d);
      while (b.children.length > 6) b.removeChild(b.firstChild);
    }
    return {
      open: function () {
        var e = ensure();
        clearTimeout(timer);
        clearTimeout(queue);
        queue = 0;
        pend = [];
        $('.tt-body', e).textContent = '';
        e.classList.add('is-on');
      },
      /* lines typed one after another, a few frames apart */
      lines: function (list) {
        pend = pend.concat(list);
        (function pump() {
          if (queue || !pend.length) return;
          var l = pend.shift();
          put(l[0], l[1]);
          queue = setTimeout(function () { queue = 0; pump(); }, 55);
        })();
      },
      close: function (ms) {
        clearTimeout(timer);
        timer = setTimeout(function () { if (el) el.classList.remove('is-on'); }, ms);
      }
    };
  }
  function track(cls) {
    return '<div class="tc-track ' + cls + '" role="slider" tabindex="0" aria-valuemin="0"><span class="tc-rail"><span class="tc-buf"></span><span class="tc-fill"></span></span><span class="tc-knob"></span></div>';
  }
  function bar() {
    var b = document.createElement('div');
    b.className = 'tctl';
    b.innerHTML =
      '<div class="tc-seek"><span class="tc-time tc-cur">0:00</span>' + track('tc-strack') + '<span class="tc-time tc-dur">' + clock(DUR) + '</span></div>' +
      '<div class="tc-mid"><button type="button" class="tc-btn tc-back">' + I.back + '</button>' +
      '<button type="button" class="tc-btn tc-play">' + I.play + I.pause + '</button>' +
      '<button type="button" class="tc-btn tc-fwd">' + I.fwd + '</button></div>' +
      '<div class="tc-end"><div class="tc-vol"><button type="button" class="tc-btn tc-mute">' + I.vol + I.mute + '</button>' +
      (canVolume ? track('tc-vtrack') : '') + '</div>' +
      '<button type="button" class="tc-btn tc-speed"><span>1×</span></button>' +
      '<button type="button" class="tc-btn tc-fs">' + I.fs + '</button></div>';
    return b;
  }

  /* one slider for the time and the volume: mouse sets at once, touch on a tap or a sideways drag (an upward drag
     still scrolls the page), keys as listed */
  function slider(el, pick, keys) {
    var id = null, moved = false, touch = false;
    function frac(e) { var r = el.getBoundingClientRect(); return Math.max(0, Math.min(1, (e.clientX - r.left) / (r.width || 1))); }
    function end(e) {
      if (id === null || (e && e.pointerId != null && e.pointerId !== id)) return;
      if (e && e.type === 'pointerup' && touch && !moved) pick(frac(e));
      id = null;
      el.classList.remove('is-drag');
    }
    el.addEventListener('pointerdown', function (e) {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      if (e.pointerType === 'mouse') e.preventDefault();
      id = e.pointerId; moved = false; touch = e.pointerType !== 'mouse';
      try { el.setPointerCapture(id); } catch (err) {}
      el.classList.add('is-drag');
      if (!touch) pick(frac(e));
    });
    el.addEventListener('pointermove', function (e) {
      if (e.pointerId !== id) return;
      if (e.pointerType === 'mouse' && e.buttons === 0) { end(); return; }
      moved = true;
      pick(frac(e));
    });
    ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(function (t) { el.addEventListener(t, end); });
    el.addEventListener('keydown', function (e) {
      var f = keys[e.key];
      if (!f || e.altKey || e.ctrlKey || e.metaKey) return;
      e.preventDefault();
      e.stopPropagation();
      f();
    });
  }

  function hdrH() { return parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--hdr-h')) || 56; }

  /* the tour in focus: its title just under the header and the whole player below it, on one screen (the plate is
     sized for that from --tour-head, the room the title takes) */
  function tourHead() {
    var h = $('#tour-title'), plate = $('#tour .tour-plate');
    if (h && plate) document.documentElement.style.setProperty('--tour-head', Math.round(plate.getBoundingClientRect().top - h.getBoundingClientRect().top) + 'px');
  }
  function focusTour() {
    if (P.mode !== 'inline' || !P.wrap) return;
    var h = $('#tour-title') || P.wrap;
    var top = window.scrollY + h.getBoundingClientRect().top - hdrH() - 16;
    P.hold = performance.now() + 1500;
    if (Math.abs(top - window.scrollY) > 4) window.scrollTo({ top: Math.max(0, top), behavior: reduced ? 'auto' : 'smooth' });
  }

  /* the plate grows or shrinks when the video leaves for the corner or comes back; when its top is above the screen,
     the page below it must not jump, so the scroll moves by the same amount (the browser's own scroll anchoring is
     off for that moment, so it is not done twice) */
  function keepView(change) {
    if (!P.plate) { change(); return; }
    var root = document.documentElement, r0 = P.plate.getBoundingClientRect(), above = r0.top < hdrH();
    root.style.overflowAnchor = 'none';
    change();
    if (above) {
      var d = P.plate.getBoundingClientRect().bottom - r0.bottom;
      if (d) window.scrollTo({ top: window.scrollY + d, behavior: 'instant' });
    }
    setTimeout(function () { root.style.overflowAnchor = ''; }, 120);
  }

  /* while the tour plays in the page, a glow at the bottom says the page can still be scrolled: the video keeps
     playing in the corner. It shows for a while each time the tour starts, until the visitor has used the mini player */
  var hint = null, hintT = 0, hintDone = false;
  function showHint() {
    if (hintDone || P.mode !== 'inline' || !P.video || P.video.paused) return;
    if (!hint) {
      hint = document.createElement('button');
      hint.type = 'button';
      hint.className = 'tour-more';
      hint.hidden = true;
      hint.innerHTML = '<span class="tm-dots" aria-hidden="true"><i></i><i></i><i></i></span><span class="tm-txt"><b></b><small></small></span>' +
        '<span class="tm-go" aria-hidden="true">' + svg('M6 9l6 6 6-6') + '</span>';
      hint.addEventListener('click', function () {
        var s = P.plate && P.plate.closest('section'), n = s && s.nextElementSibling;
        hideHint();
        if (n) n.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'start' });
      });
    }
    hintText();
    if (!hint.isConnected) document.body.appendChild(hint);
    /* on phones the "Get it" bar sits at the bottom: the hint goes above it */
    var cta = $('#cta-bar.is-on');
    hint.style.setProperty('--lift', cta && cta.offsetHeight ? cta.offsetHeight + 'px' : '0px');
    hint.hidden = false;
    void hint.offsetWidth;
    hint.classList.add('is-on');
    clearTimeout(hintT);
    hintT = setTimeout(hideHint, 10000);
  }
  function hintText() {
    $('b', hint).textContent = T('player.exploreA', 'Scroll to explore');
    $('small', hint).textContent = T('player.exploreB', 'The tour keeps playing in the corner');
  }
  function hideHint() {
    clearTimeout(hintT);
    if (!hint || hint.hidden) return;
    hint.classList.remove('is-on');
    setTimeout(function () { if (!hint.classList.contains('is-on')) hint.hidden = true; }, reduced ? 0 : 300);
  }

  /* full screen: a computer gets this player (control bar under the picture, logo in the corner); a phone or tablet
     gets the system's own player, which on iPhone Safari only exists on the video itself */
  function fsEl() { return document.fullscreenElement || document.webkitFullscreenElement || null; }
  function fullscreen() {
    var v = P.video, d = document;
    if (!v) return;
    if (fsEl()) { (d.exitFullscreen || d.webkitExitFullscreen).call(d); return; }
    var touch = !!(window.matchMedia && matchMedia('(pointer: coarse)').matches);
    var el = touch ? v : P.wrap, req = el.requestFullscreen || el.webkitRequestFullscreen;
    if (req && (d.fullscreenEnabled || d.webkitFullscreenEnabled)) {
      var p = req.call(el);
      if (p && p.then) p.then(function () {
        if (touch && screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(function () {});
      }, function () {});
    } else if (v.webkitEnterFullscreen) {
      try { v.webkitEnterFullscreen(); } catch (e) {
        /* Safari wants the video loaded first; it starts playing here and the next tap goes full screen */
        v.play().catch(function () {});
      }
    }
  }
  /* the system's controls only while the video really is full screen (a request can be granted and still not happen) */
  function fsChange() {
    if (!P.video) return;
    var f = fsEl(), native = f === P.video;
    P.video.controls = native;
    if (!native) P.video.removeAttribute('controls');
    P.wrap.classList.toggle('is-fs', !!f && f === P.wrap);
    if (P.labels) P.labels();
    if (!f) try { screen.orientation.unlock(); } catch (e) {}
  }
  add.call(document, 'fullscreenchange', fsChange);
  add.call(document, 'webkitfullscreenchange', fsChange);

  function adopt(video) {
    var wrap = document.createElement('div');
    wrap.className = 'tvid';
    var media = document.createElement('div');
    media.className = 'tv-box';
    video.parentNode.insertBefore(wrap, video);
    wrap.appendChild(media);
    media.appendChild(video);
    video.setAttribute('data-own-lang', '');
    /* the page keeps the browser's controls for visitors without JS */
    video.controls = false;
    video.removeAttribute('controls');
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
      b.addEventListener('click', function () { switchLang(x[0]); });
      g.appendChild(b);
    });
    media.appendChild(g);

    /* the logo shown in full screen */
    var logo = document.createElement('img');
    logo.className = 'tv-logo';
    logo.src = 'media/lockup.svg';
    logo.width = 130; logo.height = 40; logo.alt = '';
    media.appendChild(logo);
    var con = console_(media);

    /* a language picked here glitches over: picture and sound break up for a moment, then the new voice comes in,
       and the console shows what happened */
    var switching = 0, fx = null;
    function switchLang(l) {
      if (l === P.lang && !switching) return;
      if (reduced) { setLang(l, true); return; }
      g.querySelectorAll('button').forEach(function (b) { b.setAttribute('aria-pressed', String(b.getAttribute('data-tl') === l)); });
      var src = video.readyState >= 2 && video.videoWidth ? video : null, at = video.currentTime || 0, t0 = performance.now();
      if (!src) { var im = new Image(); im.src = video.poster; if (im.complete) src = im; }
      if (fx) fx.end();
      fx = glitchFx(media, src);
      var mine = fx, bursts = glitchSound(video), loops = stutter(video, 170);
      con.open();
      con.lines([
        ['tour.lang = "' + l + '"', 'tt-cmd'],
        [at > 0 ? 'freeze frame @ ' + stamp(at) + ' -> canvas' : 'swap poster -> ' + l],
        ['glitch: rgb split, torn strips, blocks'],
        [loops || bursts ? 'audio: ' + (loops ? 'stutter x' + loops : 'silent') + (bursts ? ' + ' + bursts + ' synth bursts' : '') : 'audio: muted']
      ]);
      clearTimeout(switching);
      switching = setTimeout(function () {
        switching = 0;
        setLang(l, true);
        con.lines([['src -> media/v4/' + (l === 'en' ? '' : l + '/') + 'tour.mp4']]);
        var hold = $('.tv-hold', media);
        if (!hold) {
          setTimeout(mine.end, 120);
          con.lines([['ready: ' + l + ', press play', 'tt-ok']]);
          con.close(1300);
          return;
        }
        mine.src = hold;
        mine.power = 0.5;
        function settle() {
          video.removeEventListener('seeked', settle);
          clearTimeout(cap);
          mine.end();
          con.lines([['seek ' + stamp(video.currentTime) + ' ok, ' + Math.round(performance.now() - t0) + ' ms'], ['voice + subtitles matched', 'tt-ok']]);
          con.close(1400);
        }
        var cap = setTimeout(settle, 2000);
        video.addEventListener('seeked', settle);
      }, 170);
    }

    var big = document.createElement('button');
    big.type = 'button';
    big.className = 'tc-big';
    big.innerHTML = I.play;
    media.appendChild(big);
    var ctl = bar();
    wrap.appendChild(ctl);
    function q(s) { return $(s, wrap); }
    var seek = q('.tc-strack'), vtr = q('.tc-vtrack'), playB = q('.tc-play'), muteB = q('.tc-mute'), speedB = q('.tc-speed');

    /* time: a spot picked before the video has loaded is kept until it has */
    var pending = null;
    function dur() { return isFinite(video.duration) && video.duration > 0 ? video.duration : DUR; }
    function now() { return pending != null ? pending : video.currentTime || 0; }
    function paintTime() {
      var d = dur(), t = now(), b = 0, r = video.buffered;
      for (var i = 0; i < r.length; i++) if (r.start(i) <= t + 0.5 && r.end(i) > b) b = r.end(i);
      wrap.style.setProperty('--tp', String(Math.min(1, t / d)));
      wrap.style.setProperty('--tb', String(Math.min(1, b / d)));
      q('.tc-cur').textContent = clock(t);
      q('.tc-dur').textContent = clock(d);
      seek.setAttribute('aria-valuemax', String(Math.round(d)));
      seek.setAttribute('aria-valuenow', String(Math.round(t)));
      seek.setAttribute('aria-valuetext', T('player.of', '{a} of {b}').replace('{a}', clock(t)).replace('{b}', clock(d)));
      if (dock) dock.style.setProperty('--p', String(t / d));
      if (P.ph) P.ph.style.setProperty('--p', String(t / d));
    }
    function seekTo(t) {
      t = Math.max(0, Math.min(dur() - 0.05, t));
      if (video.readyState >= 1) { pending = null; video.currentTime = t; } else pending = t;
      paintTime();
    }
    function skip(by) { seekTo(now() + by); }
    video.addEventListener('loadedmetadata', function () {
      if (pending != null) { var t = pending; pending = null; try { video.currentTime = t; } catch (e) {} }
      paintTime();
    });
    ['timeupdate', 'progress', 'durationchange', 'seeked'].forEach(function (ev) { video.addEventListener(ev, paintTime); });
    slider(seek, function (f) { seekTo(f * dur()); }, {
      ArrowLeft: function () { skip(-5); }, ArrowDown: function () { skip(-5); }, ArrowRight: function () { skip(5); }, ArrowUp: function () { skip(5); },
      PageDown: function () { skip(-30); }, PageUp: function () { skip(30); }, Home: function () { seekTo(0); }, End: function () { seekTo(dur()); },
      ' ': function () { toggle(true); }
    });

    /* a pause winds down like a tape stopping (picture and voice slow and drop); a start spins up from half speed */
    var stopping = false, spin = 0;
    function playing() { return !video.paused && !video.ended && !stopping; }
    function settle() {
      stopping = false;
      stopRamp(video);
      paintPlay();
    }
    function toggle(user) {
      if (playing()) {
        if (!user || reduced) { video.pause(); return; }
        stopping = true;
        spin++;
        wrap.classList.add('is-held');
        paintPlay();
        con.open();
        con.lines([['tour.pause()', 'tt-cmd'], ['rate ' + rateOf(video).toFixed(2) + ' -> ' + (rateOf(video) * 0.3).toFixed(2) + ', pitch unlocked']]);
        ramp(video, video.playbackRate, rateOf(video) * 0.3, 380, function () {
          video.pause();
          settle();
          con.lines([['stopped @ ' + stamp(video.currentTime), 'tt-ok']]);
          con.close(900);
        });
        return;
      }
      if (stopping) settle();
      wrap.classList.remove('is-held');
      if (user && !reduced) {
        var my = ++spin;
        try { video.playbackRate = rateOf(video) * 0.5; } catch (e) {}
        pitch(video, false);
        con.open();
        con.lines([['tour.play()', 'tt-cmd']]);
        con.close(3000);
        var up = function () {
          video.removeEventListener('playing', up);
          if (my !== spin) return;
          con.lines([['spin-up ' + (rateOf(video) * 0.5).toFixed(2) + ' -> ' + rateOf(video).toFixed(2)]]);
          ramp(video, video.playbackRate, rateOf(video), 260, function () {
            pitch(video, true);
            con.lines([['rolling @ ' + stamp(video.currentTime), 'tt-ok']]);
            con.close(900);
          });
        };
        video.addEventListener('playing', up);
      }
      if (video.paused || video.ended) video.play().catch(function () {});
      /* the first start brings the player to the middle of the screen; later ones leave the page where it is */
      if (user) {
        P.hold = performance.now() + 1500;
        if (!P.centred) { P.centred = true; focusTour(); fadeIn(); }
      }
    }
    function paintPlay() {
      wrap.classList.toggle('is-playing', playing());
      playB.setAttribute('aria-label', playing() ? T('player.pause', 'Pause') : T('player.play', 'Play'));
    }
    video.addEventListener('pause', function () { if (!stopping) { spin++; stopRamp(video); } });
    video.addEventListener('play', function () { if (!stopping) wrap.classList.remove('is-held'); });
    ['play', 'pause', 'ended', 'emptied'].forEach(function (ev) { video.addEventListener(ev, paintPlay); });
    playB.addEventListener('click', function () { toggle(true); });
    big.addEventListener('click', function () { toggle(true); });
    /* in full screen the system's controls are on (video.controls) and take the clicks */
    video.addEventListener('click', function () { if (!video.controls) toggle(true); });
    video.addEventListener('dblclick', function () { if (!video.controls) fullscreen(); });
    q('.tc-back').addEventListener('click', function () { skip(-10); });
    q('.tc-fwd').addEventListener('click', function () { skip(10); });
    q('.tc-fs').addEventListener('click', fullscreen);

    /* volume: the level is remembered, mute is not (the tour is about the voice) */
    /* the tour starts at 40% (or the viewer's own level), and the first start fades the sound in, so nobody gets
       blasted; the volume slider rises with it */
    var fading = false;
    try {
      var sv = JSON.parse(store('ats-tour-vol') || 'null');
      if (canVolume) video.volume = sv >= 0.05 && sv <= 1 ? sv : 0.4;
    } catch (e) { if (canVolume) video.volume = 0.4; }
    function fadeIn() {
      if (!canVolume || reduced || video.muted) return;
      var to = video.volume, t0 = performance.now();
      fading = true;
      video.volume = 0;
      var id = setInterval(function () {
        var k = Math.min(1, (performance.now() - t0) / 1200);
        /* a click on the volume during the fade wins */
        if (!fading) { clearInterval(id); return; }
        video.volume = to * k * k;
        if (k >= 1) { clearInterval(id); fading = false; }
      }, 40);
    }
    function paintVol() {
      var off = video.muted || video.volume === 0, v = video.muted ? 0 : video.volume;
      wrap.classList.toggle('is-muted', off);
      muteB.setAttribute('aria-label', off ? T('player.unmute', 'Unmute') : T('player.mute', 'Mute'));
      wrap.style.setProperty('--tv', String(v));
      if (vtr) {
        vtr.setAttribute('aria-valuemax', '100');
        vtr.setAttribute('aria-valuenow', String(Math.round(v * 100)));
        vtr.setAttribute('aria-valuetext', Math.round(v * 100) + '%');
      }
    }
    function setVol(v) {
      fading = false;
      v = Math.max(0, Math.min(1, Math.round(v * 100) / 100));
      video.volume = v;
      video.muted = v === 0;
    }
    function mute() {
      fading = false;
      if (video.muted || video.volume === 0) { if (video.volume === 0) video.volume = 0.5; video.muted = false; }
      else video.muted = true;
    }
    video.addEventListener('volumechange', function () { paintVol(); if (video.volume > 0 && !fading) store('ats-tour-vol', String(video.volume)); });
    muteB.addEventListener('click', mute);
    if (vtr) slider(vtr, setVol, {
      ArrowLeft: function () { setVol(video.volume - 0.05); }, ArrowDown: function () { setVol(video.volume - 0.05); },
      ArrowRight: function () { setVol((video.muted ? 0 : video.volume) + 0.05); }, ArrowUp: function () { setVol((video.muted ? 0 : video.volume) + 0.05); },
      Home: function () { setVol(0); }, End: function () { setVol(1); }
    });

    /* speed: one button that steps through the speeds (Shift steps back); a new language file keeps it */
    /* the chosen speed lives in defaultPlaybackRate; playbackRate moves during the effects */
    function paintSpeed() {
      var r = rateOf(video);
      $('span', speedB).textContent = r + '×';
      speedB.setAttribute('aria-label', T('player.speed', 'Playback speed') + ': ' + r + '×');
    }
    function speed(by) {
      var i = SPEEDS.indexOf(rateOf(video));
      var r = SPEEDS[(Math.max(0, i) + by + SPEEDS.length) % SPEEDS.length];
      video.defaultPlaybackRate = r;
      video.playbackRate = r;
    }
    speedB.addEventListener('click', function (e) { speed(e.shiftKey ? -1 : 1); });
    video.addEventListener('ratechange', paintSpeed);

    /* after a mouse click the button lets go of the keyboard, so Space keeps scrolling the page */
    ctl.addEventListener('click', function (e) {
      var a = document.activeElement;
      if (e.detail > 0 && !lastKey && a && ctl.contains(a)) a.blur();
    });

    function labels() {
      big.setAttribute('aria-label', T('player.play', 'Play'));
      seek.setAttribute('aria-label', T('player.seek', 'Seek'));
      if (vtr) vtr.setAttribute('aria-label', T('player.volume', 'Volume'));
      q('.tc-back').setAttribute('aria-label', T('player.back10', 'Back 10 seconds'));
      q('.tc-fwd').setAttribute('aria-label', T('player.fwd10', 'Forward 10 seconds'));
      q('.tc-fs').setAttribute('aria-label', wrap.classList.contains('is-fs') ? T('player.exitfull', 'Leave full screen') : T('player.fullscreen', 'Full screen'));
      paintPlay(); paintVol(); paintSpeed(); paintTime();
    }
    labels();
    P.labels = labels;
    P.toggle = toggle;
    P.skip = skip;
    P.mute = mute;
    P.speed = speed;

    /* the switch fades while the video plays and the pointer rests, like the native controls */
    var idle = 0;
    function wake() {
      wrap.classList.remove('is-idle');
      clearTimeout(idle);
      idle = setTimeout(function () { if (!video.paused) wrap.classList.add('is-idle'); }, 2400);
    }
    ['pointermove', 'pointerdown', 'focusin'].forEach(function (ev) { wrap.addEventListener(ev, wake); });

    video.addEventListener('pause', function () { clearTimeout(idle); wrap.classList.remove('is-idle'); hideHint(); });
    video.addEventListener('playing', function () { setTimeout(showHint, reduced ? 0 : 1200); });
    video.addEventListener('play', function () {
      P.active = true;
      wake();
      /* only one voice at a time: another video with sound pauses */
      document.querySelectorAll('video').forEach(function (o) { if (o !== video && !o.paused && !o.muted) o.pause(); });
      checkMini();
    });
    /* using the player is not scrolling away from it */
    ['pointerdown', 'keydown'].forEach(function (ev) { wrap.addEventListener(ev, function () { P.hold = performance.now() + 1500; P.pin = P.ratio; }, true); });

    P.wrap = wrap;
    P.video = video;
    P.lang = null;
    P.centred = false;
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
    /* the title bar plus the control bar under the picture */
    var ctl = dock && P.wrap && dock.contains(P.wrap) ? $('.tctl', P.wrap) : null;
    var bar = (dock ? $('.tdock-bar', dock).offsetHeight || 36 : 36) + (ctl ? ctl.offsetHeight : 0);
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
    /* the control bar changes its rows with the width, so the height is measured after the width is set */
    dock.style.setProperty('--w', box.w + 'px');
    var h = dock.offsetHeight || height(box.w, b);
    box.x = Math.round(Math.max(b.l, Math.min(b.r - box.w, box.x)));
    box.y = Math.round(Math.max(b.t, Math.min(b.b - h, box.y)));
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
    var size = button('tdock-size', 'player.resize', 'Resize the player (arrow keys)', svg('M6.5 6.5l11 11M6.5 6.5h5.5M6.5 6.5V12M17.5 17.5H12M17.5 17.5V12'));
    size.title = T('player.resize', 'Resize the player (arrow keys)');
    grip.title = T('player.move', 'Move the player (arrow keys)');
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
    /* while the video plays in the corner, its room shows a stand-in: the poster, blurred and drifting, scanlines, a
       little equaliser and the progress, to invite a scroll back */
    var ph = document.createElement('div');
    ph.className = 'tour-ph';
    ph.innerHTML = '<div class="tour-ph-stage"><div class="tour-ph-bg" aria-hidden="true"></div><div class="tour-ph-in">' +
      '<span class="tour-ph-eq" aria-hidden="true"><i></i><i></i><i></i><i></i></span>' +
      '<p data-i18n="player.here"></p><button type="button" class="btn btn-primary btn-sm" data-i18n="player.back"></button></div></div>' +
      '<div class="tour-ph-foot" aria-hidden="true"><span class="tour-ph-prog"></span></div>';
    $('.tour-ph-bg', ph).style.backgroundImage = 'url("' + dir(P.lang) + 'tour-poster.webp")';
    $('p', ph).textContent = T('player.here', 'Playing in the mini player');
    var b = $('button', ph);
    b.textContent = T('player.back', 'Bring it back here');
    b.addEventListener('click', function () { setMode('inline'); focusTour(); focusVideo(); });
    P.ph = ph;
    return ph;
  }

  function setMode(mode) {
    if (!P.wrap || mode === P.mode) return;
    var had = P.wrap.contains(document.activeElement) ? document.activeElement : null;
    if (mode === 'mini') {
      hintDone = true;
      hideHint();
      makeDock();
      var wasHidden = dock.hidden;
      keepView(function () {
        if (P.plate) {
          var ph = placeholder();
          if (ph && ph.parentNode !== P.plate) {
            /* the stand-in takes exactly the player's room (picture plus control bar), so nothing below moves */
            var c = $('.tctl', P.wrap);
            ph.style.setProperty('--foot', (c ? c.offsetHeight : 53) + 'px');
            P.plate.appendChild(ph);
            if (!reduced) { ph.classList.remove('is-new'); void ph.offsetWidth; ph.classList.add('is-new'); }
          }
        }
        $('.tdock-media', dock).appendChild(P.wrap);
      });
      dock.hidden = false;
      if (wasHidden) {
        restore();
        if (!reduced) {
          dock.classList.remove('is-in', 'is-hint');
          void dock.offsetWidth;
          dock.classList.add('is-in');
          /* the first time, the resize handle pulses so people notice it */
          if (!store('ats-tour-knob')) { dock.classList.add('is-hint'); store('ats-tour-knob', '1'); }
        }
      }
    } else if (mode === 'inline') {
      if (!P.plate) return;
      keepView(function () {
        if (P.ph && P.ph.parentNode) P.ph.remove();
        P.plate.appendChild(P.wrap);
      });
      if (dock) dock.hidden = true;
      if (!reduced) {
        P.wrap.classList.remove('is-back');
        void P.wrap.offsetWidth;
        P.wrap.classList.add('is-back');
      }
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
      focusTour();
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
    P.pin = 1;
    var plate = $('#tour .plate-body');
    P.plate = plate;
    tourHead();
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
      P.wrap = null; P.video = null; P.active = false; P.labels = P.toggle = null;
      if (!fresh) return;
      adopt(fresh);
      P.mode = 'inline';
    }
    P.wantInline = false;
    if ('IntersectionObserver' in window) {
      P.io = new IntersectionObserver(function (en) {
        P.ratio = en[0].intersectionRatio;
        if (P.ratio > P.pin) P.pin = P.ratio;
        /* a gap between the two thresholds, so a small scroll at the edge does not flip it back and forth */
        if (P.mode === 'mini' && P.ratio >= 0.6) setMode('inline');
        else checkMini();
      }, { threshold: [0, 0.1, 0.2, 0.3, 0.35, 0.6, 0.8, 1] });
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
  /* player keys: Space and K play or pause, the arrows seek 5 s, J and L 10 s, M mutes, F full screen, < and >
     change the speed. They work while the focus is in the player, or after a click in it (focus on the page);
     Space then still scrolls the page */
  var armed = false;
  add.call(document, 'pointerdown', function (e) { armed = !!(P.wrap && P.wrap.contains(e.target)); }, true);
  add.call(document, 'keydown', function (e) {
    if (!P.wrap || !P.toggle || e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return;
    var fe = fsEl();
    if ((fe && fe !== P.wrap) || (!fe && P.mode === 'mini' && (!dock || dock.hidden))) return;
    var a = document.activeElement, inside = P.wrap.contains(a);
    if (!inside && fe !== P.wrap && !(armed && (!a || a === document.body))) return;
    var k = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (k === ' ') { if (!inside || /^(BUTTON|A)$/.test(a.tagName)) return; P.toggle(true); }
    else if (k === 'k') P.toggle(true);
    else if (k === 'ArrowLeft' || k === 'ArrowRight') { if (inside && a.closest('.tlang')) return; P.skip(k === 'ArrowLeft' ? -5 : 5); }
    else if (k === 'j' || k === 'l') P.skip(k === 'j' ? -10 : 10);
    else if (k === 'm') P.mute();
    else if (k === 'f') fullscreen();
    else if (k === '<' || k === '>') P.speed(k === '<' ? -1 : 1);
    else return;
    e.preventDefault();
  });

  add.call(document, 'ats:lang', function () {
    if (P.labels) P.labels();
    if (hint) hintText();
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

  function viewportWidth() { document.documentElement.style.setProperty('--vw', document.documentElement.clientWidth + 'px'); tourHead(); }
  add.call(document, 'ats:lang', function () { setTimeout(tourHead, 0); });
  add.call(window, 'resize', viewportWidth);
  function boot() { viewportWidth(); attach(); }
  if (document.readyState === 'loading') add.call(document, 'DOMContentLoaded', boot); else boot();
})();
