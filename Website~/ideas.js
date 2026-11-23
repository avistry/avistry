/* The demos that are backed by real tool output. Every module reads a JSON file under media/v4/ideas/ and switches
   itself on only when the file is real data ("placeholder" is not true). Until then the section shows its still.
   No libraries, no sample numbers: if a number is not in the data, it is not printed.

   media/v4/ideas/pieces.json    { "placeholder": false, "default": "sleeve",
                                   "pieces": [ { "id": "sleeve", "count": 3,
                                                 "tex": [ [[x,y],...] ], "model": [ [[x,y],...] ],     (pixel space of the two pictures)
                                                 "tex_hl": "ideas/piece-links/tex-01.webp", "model_hl": "ideas/piece-links/model-01.webp" } ] }
   media/v4/ideas/layers.json    { "placeholder": false, "texture": "ideas/layers/base.webp",
                                   "layers": [ { "id": "sleeve_gradient", "img": "ideas/layers/01.webp", "blend": "multiply", "opacity": 0.85 } ] }
   media/v4/ideas/history.json   { "placeholder": false, "steps": [ { "id": "start", "img": "ideas/history/00.webp", "img_m": "ideas/history/00-m.webp" } ] }
   media/v4/ideas/audit.json     { "placeholder": false, "materials": 0, "textures": 0,
                                   "rows": [ { "id": "crunch", "status": "ok" | "fixed" | "warn", "count": 0 } ] }
   media/v4/ideas/makeover.json  { "placeholder": false, "duration": 84, "speed": 4, "steps": [ { "t": 0, "id": "pick" } ] }
   Labels never live in the data: they come from i18n keys built from the ids (sel.piece.<id>, stack.layer.<id>,
   undo.step.<id>, mk.step.<id>). A missing key falls back to English, then the label is left out. */
(function () {
  'use strict';

  var BASE = 'media/v4/';
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var reduced = (!!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) || document.documentElement.classList.contains('perf'));
  var noAuto = reduced || !!(navigator.connection && navigator.connection.saveData);
  var phone = window.matchMedia ? matchMedia('(max-width: 639px)') : { matches: false };

  function T(key) { return window.ATS ? window.ATS.t(key) : null; }
  function F(tpl, v) { return window.ATS ? window.ATS.fmt(tpl, v) : tpl; }
  function onLang(fn) { document.addEventListener('ats:lang', fn); }
  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }

  /* data files load when their section is near the screen; real data only (placeholder is treated as no data) */
  function loadJson(name) {
    return fetch(BASE + 'ideas/' + name + '.json').then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (d) { if (!d || d.placeholder === true) throw new Error('placeholder'); return d; });
  }
  function whenNear(node, fn, margin) {
    if (!node) return;
    if (!('IntersectionObserver' in window)) { fn(); return; }
    var io = new IntersectionObserver(function (en) { if (en[0].isIntersecting) { io.disconnect(); fn(); } }, { rootMargin: (margin || 1400) + 'px 0px' });
    io.observe(node);
  }
  function label(prefix, id) {
    var v = T(prefix + id);
    return v != null && v !== '' ? v : null;
  }

  /* ---------- the view: "Scroll" holds the layers and the blend modes full screen while you scroll, "Overview" lays
     them all out at once. Performance mode and reduced motion always get the overview. Switching keeps your place. ---------- */
  var forced = reduced;
  /* links from the Learn page ask for the overview (?view=overview): every tool at a glance is easier to follow */
  var overview = forced || /[?&]view=overview/.test(location.search) || (function () { try { return localStorage.getItem('ats-fview') === 'overview'; } catch (e) { return false; } })();
  var views = [];
  function flash(node) {
    if (!node) return;
    node.classList.remove('is-here'); void node.offsetWidth; node.classList.add('is-here');
    setTimeout(function () { node.classList.remove('is-here'); }, 1800);
  }
  /* the card nearest the middle of the screen */
  function nearest(nodes) {
    var mid = window.innerHeight / 2, best = null, bd = 1e9;
    nodes.forEach(function (n) {
      var r = n.getBoundingClientRect();
      if (r.bottom < 0 || r.top > window.innerHeight) return;
      var dd = Math.abs(r.top + r.height / 2 - mid);
      if (dd < bd) { bd = dd; best = n; }
    });
    return best;
  }
  function setView(ov) {
    if (forced || ov === overview) return;
    var at = null;
    views.some(function (v) { var i = v.current(); if (i != null) { at = { v: v, i: i }; return true; } return false; });
    overview = ov;
    try { localStorage.setItem('ats-fview', ov ? 'overview' : 'scroll'); } catch (e) {}
    document.documentElement.classList.toggle('fv-overview', ov);
    views.forEach(function (v) { v.mode(ov); });
    renderSwitch();
    if (at) requestAnimationFrame(function () { at.v.show(at.i); });
  }
  function renderSwitch() {
    $$('.fview button').forEach(function (b) {
      var on = (b.getAttribute('data-fv') === 'overview') === overview;
      b.setAttribute('aria-pressed', String(on));
      if (forced && b.getAttribute('data-fv') === 'scroll') { b.disabled = true; b.title = T('fv.forced') || 'Performance mode always shows the overview.'; }
    });
  }
  function initView() {
    document.documentElement.classList.toggle('fv-overview', overview);
    var sw = $('.fview');
    if (!sw) return;
    sw.hidden = false;
    $$('button', sw).forEach(function (b) { b.addEventListener('click', function () { setView(b.getAttribute('data-fv') === 'overview'); }); });
    renderSwitch();
    onLang(renderSwitch);
  }

  function initStack() {
    var root = $('#stack');
    if (!root) return;
    whenNear(root, function () {
      loadJson('layers').then(function (d) {
        if (!d.layers || !d.layers.length) throw new Error('no layers');
        buildStack(root, d);
      }).catch(function () { /* the layers panel still stays */ });
    }, 1800);
  }

  function smooth(x) { x = Math.max(0, Math.min(1, x)); return x * x * (3 - 2 * x); }
  function clamp01(x) { return Math.max(0, Math.min(1, x)); }

  /* a full-screen stage held under the header while its track scrolls by */
  function pinMetrics() {
    var hdr = $('.hdr'), jump = $('.jump'), bar = $('.look-bar');
    var top = (hdr ? hdr.offsetHeight : 0) + (jump ? jump.offsetHeight : 0) + (bar ? bar.offsetHeight : 0);
    return { top: top, h: Math.max(380, window.innerHeight - top) };
  }
  function scrollLoop(node, fn) {
    var raf = 0;
    function run() { raf = 0; fn(node.getBoundingClientRect(), window.innerHeight); }
    function sched() { if (!raf) raf = requestAnimationFrame(run); }
    if ('IntersectionObserver' in window) {
      var on = false;
      new IntersectionObserver(function (en) {
        var now = en[0].isIntersecting;
        if (now && !on) { window.addEventListener('scroll', sched, { passive: true }); sched(); }
        if (!now && on) window.removeEventListener('scroll', sched);
        on = now;
      }, { rootMargin: '300px 0px' }).observe(node);
    } else window.addEventListener('scroll', sched, { passive: true });
    return sched;
  }
  /* a strip of names: the current one in the middle and lit, its neighbours smaller and fading with distance */
  function paintStrip(items, f) {
    items.forEach(function (li, k) {
      var off = k - f, a = Math.abs(off);
      li.style.setProperty('--d', off.toFixed(3));
      li.style.setProperty('--s', (1 + 0.45 * Math.max(0, 1 - a) - Math.min(a, 3) * 0.05).toFixed(3));
      li.style.opacity = Math.max(0, 1 - a * 0.38).toFixed(3);
    });
  }
  function counter(node, i, n) {
    if (!node) return;
    node.textContent = '';
    node.appendChild(document.createTextNode(String(i).padStart(2, '0')));
    node.appendChild(el('small', null, '/ ' + n));
  }

  function buildStack(root, d) {
    var track = $('#stack-track'), stage = $('.stack-stage', track), scene = $('#stack-scene'), labels = $('#stack-labels'), lead = $('#stack-lead');
    var head = $('.stack-head', root), count = $('#stack-count');
    var n = d.layers.length;
    var pile = el('div', 'pile');
    var all = [];
    function layerEl(src, i, cls) {
      var s = el('div', 'sl' + (cls ? ' ' + cls : ''));
      s.style.setProperty('--i', String(i));
      var im = document.createElement('img');
      im.alt = ''; im.decoding = 'async'; im.src = BASE + src;
      s.appendChild(im);
      pile.appendChild(s);
      return s;
    }
    if (d.texture) all.push(layerEl(d.texture, 0, 'sl--base'));
    d.layers.forEach(function (L, i) {
      var s = layerEl(L.img, i + 1);
      /* "vo" is the opacity of the picture when it is already a composite of changed pixels */
      var vo = L.vo != null ? L.vo : L.opacity;
      if (vo != null) s.style.setProperty('--vo', String(vo));
      all.push(s);
    });
    scene.appendChild(pile);

    /* the overview: the texture, then every layer from the bottom up, each with its blend mode and opacity */
    var cards = el('ol', 'stack-cards wrap');
    cards.setAttribute('role', 'list');
    cards.hidden = true;
    root.insertBefore(cards, $('#stack-still'));
    function renderCards() {
      cards.textContent = '';
      var list = (d.texture ? [{ img: d.texture, i: 0 }] : []).concat(d.layers.map(function (L, i) { return { img: L.img, L: L, i: i + 1 }; }));
      list.forEach(function (c) {
        var li = el('li', 'stack-card' + (c.L ? '' : ' is-base'));
        li.setAttribute('data-i', c.i);
        var pic = el('div', 'stack-card-pic'), im = document.createElement('img');
        im.alt = ''; im.loading = 'lazy'; im.decoding = 'async'; im.width = 360; im.height = 360; im.src = BASE + c.img;
        pic.appendChild(im);
        pic.appendChild(el('span', 'stack-card-n tnum', String(c.i)));
        li.appendChild(pic);
        li.appendChild(el('b', 'stack-card-t', c.L ? (label('stack.layer.', c.L.id) || pretty(c.L.id.replace(/_/g, '-'))) : (T('stack.base') || 'The texture')));
        if (c.L) {
          var bk = c.L.blend === 'source-over' || !c.L.blend ? 'normal' : c.L.blend;
          var blend = T('mode.b.' + bk) || bk.replace(/-/g, ' ');
          li.appendChild(el('span', 'stack-card-m', blend + ' · ' + Math.round((c.L.opacity != null ? c.L.opacity : 1) * 100) + '%'));
        } else li.appendChild(el('span', 'stack-card-m', T('mode.kind.o') || 'The texture, no layers'));
        cards.appendChild(li);
      });
    }

    var items = [];
    function labelText(L) {
      var nm = label('stack.layer.', L.id);
      if (!nm) return null;
      var blend = T('blend.' + L.blend) || (L.blend ? L.blend.replace(/-/g, ' ') : '');
      return F(T('stack.fmt') || '{name}, {blend}, {opacity}%', { name: nm, blend: blend, opacity: Math.round((L.opacity != null ? L.opacity : 1) * 100) });
    }
    function renderLabels() {
      labels.textContent = '';
      items = [];
      var rows = d.layers.map(function (L, i) { return { L: L, i: i + 1, txt: labelText(L) }; }).filter(function (r) { return r.txt; });
      rows.reverse().forEach(function (r) {
        var li = el('li', null, r.txt);
        li.setAttribute('data-i', r.i);
        labels.appendChild(li);
        items.push(li);
      });
      setLead();
      update(lastE, lastQ);
    }
    function setLead() {
      if (!lead) return;
      lead.removeAttribute('data-i18n');
      lead.textContent = F(T(pinned ? 'stack.lead' : 'stack.lead.ov') || T('stack.lead') || '', { n: n });
    }

    var lastE = 1, lastQ = 0.5, lastCur = -1, pinned = false, pin = { top: 76, h: 560 }, OPEN = 0.2;
    function size() {
      var w = stage.clientWidth || window.innerWidth;
      if (pinned) {
        pin = pinMetrics();
        root.style.setProperty('--pin-top', pin.top + 'px');
        root.style.setProperty('--stage-h', pin.h + 'px');
        /* most of a screen to pull the pile apart, then about a third of a screen per layer */
        track.style.height = Math.round(pin.h + window.innerHeight * 0.8 + n * window.innerHeight * 0.3) + 'px';
      }
      var h = pinned ? pin.h : 480;
      var wide = w >= 900;
      var lw = wide ? Math.min(w * 0.3, h * 0.56) : Math.min(w * 0.62, h * 0.36);
      pile.style.setProperty('--lw', Math.round(lw) + 'px');
      /* the opened stack must fit the stage: about half its height for all the gaps together */
      pile.style.setProperty('--gap', Math.round(Math.max(14, Math.min(wide ? 46 : 30, h * (wide ? 0.5 : 0.36) / n))) + 'px');
    }
    function update(e, q) {
      lastE = e; lastQ = q;
      var m = items.length;
      /* held: first the pile pulls apart, then the names walk; the last bit shows the whole stack */
      var f = clamp01((q - OPEN - 0.03) / (0.92 - OPEN)) * Math.max(0, m - 1);
      var focusing = pinned && q > OPEN && q < 0.96;
      pile.style.setProperty('--e', e.toFixed(4));
      labels.style.setProperty('--lab', Math.min(1, e * 1.4).toFixed(3));
      root.classList.toggle('is-focusing', focusing);
      root.classList.toggle('is-deep', pinned && q > OPEN * 0.8);
      var cur = m ? Math.round(f) : -1;
      lastCur = q > OPEN ? cur : -1;
      var focus = m ? Number(items[cur].getAttribute('data-i')) : -1;
      if (pinned) paintStrip(items, f);
      items.forEach(function (li, k) { li.classList.toggle('is-near', pinned && k === cur); });
      all.forEach(function (s, k) { s.classList.toggle('is-focus', (d.texture ? k : k + 1) === focus && focusing); });
      if (m) counter(count, cur + 1, m);
    }

    root.setAttribute('data-state', 'ready');
    var sched = null;
    function mode(ov) {
      pinned = !ov;
      track.hidden = ov;
      cards.hidden = !ov;
      if (pinned) { root.setAttribute('data-pin', ''); if (head) stage.insertBefore(head, stage.firstChild); }
      else { root.removeAttribute('data-pin'); if (head) root.insertBefore(head, root.firstChild); }
      size();
      setLead();
      if (!pinned) { update(1, 0.5); return; }
      if (!sched) {
        sched = scrollLoop(track, function (r) {
          if (!pinned) return;
          /* it arrives folded into one texture and pulls apart once it holds */
          var q = clamp01((pin.top - r.top) / Math.max(1, r.height - pin.h));
          update(smooth(clamp01(q / OPEN)), q);
        });
        window.addEventListener('resize', function () { size(); if (pinned) sched(); });
      }
      sched();
    }
    renderCards();
    renderLabels();
    mode(overview);
    if (window.ATS) window.ATS.setLayerCount(n);
    onLang(function () { renderLabels(); renderCards(); });
    views.push({
      mode: mode,
      current: function () {
        var r = root.getBoundingClientRect();
        if (r.bottom < window.innerHeight * 0.4 || r.top > window.innerHeight * 0.6) return null;
        if (!pinned) { var c = nearest($$('.stack-card', cards)); return c ? Number(c.getAttribute('data-i')) : 0; }
        return lastCur >= 0 && items[lastCur] ? Number(items[lastCur].getAttribute('data-i')) : 0;
      },
      show: function (i) {
        if (!pinned) {
          var c = $('.stack-card[data-i="' + i + '"]', cards) || $('.stack-card', cards);
          if (c) { c.scrollIntoView({ block: 'center' }); flash(c); }
          return;
        }
        var m = items.length, k = -1;
        items.forEach(function (li, j) { if (Number(li.getAttribute('data-i')) === i) k = j; });
        var q = k < 0 ? OPEN * 0.5 : OPEN + 0.03 + (m > 1 ? k / (m - 1) : 0) * (0.92 - OPEN);
        var r = track.getBoundingClientRect();
        window.scrollTo(0, Math.round(window.scrollY + r.top - pin.top + q * (r.height - pin.h)) + 1);
      }
    });
  }

  /* ---------- 1b. every blend mode and adjustment layer, rendered by Avistry's own compositor ---------- */
  var GROUP = { multiply: 'darken', darken: 'darken', 'color-burn': 'darken', 'linear-burn': 'darken', 'darker-color': 'darken',
    screen: 'lighten', add: 'lighten', lighten: 'lighten', 'color-dodge': 'lighten', 'glow-dodge': 'lighten', 'add-glow': 'lighten', 'lighter-color': 'lighten',
    overlay: 'contrast', 'soft-light': 'contrast', 'hard-light': 'contrast', 'vivid-light': 'contrast', 'linear-light': 'contrast', 'pin-light': 'contrast', 'hard-mix': 'contrast',
    subtract: 'compare', difference: 'compare', exclusion: 'compare', divide: 'compare',
    color: 'colour', hue: 'colour', saturation: 'colour', luminosity: 'colour' };
  function pretty(id) { return id.replace(/-/g, ' ').replace(/^./, function (c) { return c.toUpperCase(); }); }

  function initModes() {
    var root = $('#modes-root');
    if (!root) return;
    whenNear(root, function () {
      loadJson('modes').then(function (d) {
        if (!d.blend || !d.blend.length) throw new Error('no modes');
        buildModes(root, d);
      }).catch(function () { /* the blend loop below stays */ });
    }, 2400);
  }

  function buildModes(root, d) {
    var track = $('#modes-track'), view = $('#modes-view'), list = $('#modes-list');
    var nameEl = $('#modes-name'), kindEl = $('#modes-kind'), count = $('#modes-count'), grid = $('#modes-grid');
    var rows = (d.base ? [{ kind: 'o', id: 'base', img: d.base }] : [])
      .concat(d.blend.map(function (x) { return { kind: 'b', id: x.id, img: x.img }; }))
      .concat((d.adjust || []).map(function (x) { return { kind: 'a', id: x.id, img: x.img }; }));
    var nb = d.blend.length, na = (d.adjust || []).length, off = d.base ? 1 : 0;
    function nameOf(r) { return r.kind === 'o' ? (T('mode.base') || 'Before') : (T('mode.' + r.kind + '.' + r.id) || pretty(r.id)); }
    function groupOf(r) { return r.kind === 'b' && GROUP[r.id] ? (T('mode.g.' + GROUP[r.id]) || '') : ''; }
    function kindOf(r) {
      if (r.kind === 'o') return T('mode.kind.o') || 'The texture, no layers';
      return r.kind === 'b' ? F(T('mode.kind.b') || '{n} blend modes', { n: nb }) : F(T('mode.kind.a') || '{n} adjustment layers', { n: na });
    }

    /* the overview: the blend modes in their families (the texture first, to compare), then the adjustment layers */
    var ORDER = ['', 'darken', 'lighten', 'contrast', 'compare', 'colour'];
    function card(r, i) {
      var fig = el('figure', 'modes-card'), im = document.createElement('img');
      fig.setAttribute('data-i', i);
      im.src = BASE + r.img; im.alt = ''; im.loading = 'lazy'; im.decoding = 'async'; im.width = 360; im.height = 360;
      fig.appendChild(im);
      fig.appendChild(el('figcaption', null, nameOf(r)));
      return fig;
    }
    function renderGrid() {
      grid.textContent = '';
      var idx = rows.map(function (r, i) { return { r: r, i: i }; });
      var blend = idx.filter(function (x) { return x.r.kind !== 'a'; }), adj = idx.filter(function (x) { return x.r.kind === 'a'; });
      var g = function (x) { return x.r.kind === 'b' ? (GROUP[x.r.id] || '') : ''; };
      blend.sort(function (a, b) { return (ORDER.indexOf(g(a)) - ORDER.indexOf(g(b))) || (a.i - b.i); });
      grid.appendChild(el('h3', 'modes-gh', F(T('mode.kind.b') || '{n} blend modes', { n: nb })));
      var last = null;
      blend.forEach(function (x) {
        var k = g(x);
        if (k && k !== last) grid.appendChild(el('p', 'modes-sub', T('mode.g.' + k) || pretty(k)));
        last = k;
        grid.appendChild(card(x.r, x.i));
      });
      if (adj.length) {
        grid.appendChild(el('h3', 'modes-gh', F(T('mode.kind.a') || '{n} adjustment layers', { n: na })));
        adj.forEach(function (x) { grid.appendChild(card(x.r, x.i)); });
      }
    }

    var imgs = [];
    function setupTrack() {
      if (imgs.length) return;
      imgs = rows.map(function (r, i) {
        var im = document.createElement('img');
        im.alt = ''; im.decoding = 'async'; im.width = 720; im.height = 720;
        if (i < 3) im.src = BASE + r.img; else im.setAttribute('data-src', BASE + r.img);
        view.insertBefore(im, nameEl);
        return im;
      });
      /* the rest load right after, while the section is still on its way */
      setTimeout(function () { imgs.forEach(function (im) { var s = im.getAttribute('data-src'); if (s) { im.src = s; im.removeAttribute('data-src'); } }); }, 300);
    }

    var items = [], cur = -1, lastQ = 0, pin = { top: 76, h: 560 };
    function renderList() {
      list.textContent = '';
      items = rows.map(function (r) {
        var li = el('li', null, nameOf(r));
        var g = groupOf(r);
        if (g && g !== nameOf(r)) li.appendChild(el('small', null, g));
        list.appendChild(li);
        return li;
      });
      cur = -1;
      update(lastQ);
    }
    function size() {
      pin = pinMetrics();
      root.style.setProperty('--pin-top', pin.top + 'px');
      root.style.setProperty('--stage-h', pin.h + 'px');
      track.style.height = Math.round(pin.h + rows.length * window.innerHeight * 0.11) + 'px';
    }
    function update(q) {
      if (!items.length) return;
      lastQ = q;
      var f = clamp01((q - 0.02) / 0.94) * (rows.length - 1);
      var i = Math.round(f);
      paintStrip(items, f);
      if (i === cur) return;
      cur = i;
      var r = rows[i];
      imgs.forEach(function (im, k) { im.classList.toggle('is-on', k === i); });
      items.forEach(function (li, k) { li.classList.toggle('is-near', k === i); });
      nameEl.textContent = nameOf(r);
      kindEl.textContent = kindOf(r);
      if (r.kind === 'o') { count.textContent = ''; return; }
      counter(count, r.kind === 'b' ? i - off + 1 : i - off - nb + 1, r.kind === 'b' ? nb : na);
    }
    root.setAttribute('data-state', 'ready');
    var sched = null, pinned = false;
    function mode(ov) {
      pinned = !ov;
      track.hidden = ov;
      grid.hidden = !ov;
      if (ov) { root.removeAttribute('data-pin'); return; }
      root.setAttribute('data-pin', '');
      setupTrack();
      size();
      cur = -1;
      if (!sched) {
        sched = scrollLoop(track, function (r) { if (pinned) update(clamp01((pin.top - r.top) / Math.max(1, r.height - pin.h))); });
        window.addEventListener('resize', function () { if (pinned) { size(); sched(); } });
      }
      sched();
    }
    renderGrid();
    renderList();
    mode(overview);
    onLang(function () { renderList(); renderGrid(); });
    views.push({
      mode: mode,
      current: function () {
        var r = root.getBoundingClientRect();
        if (r.bottom < window.innerHeight * 0.4 || r.top > window.innerHeight * 0.6) return null;
        if (!pinned) { var c = nearest($$('.modes-card', grid)); return c ? Number(c.getAttribute('data-i')) : 0; }
        return cur < 0 ? 0 : cur;
      },
      show: function (i) {
        if (!pinned) {
          var c = $('.modes-card[data-i="' + i + '"]', grid);
          if (c) { c.scrollIntoView({ block: 'center' }); flash(c); }
          return;
        }
        var q = 0.02 + (rows.length > 1 ? i / (rows.length - 1) : 0) * 0.94;
        var r = track.getBoundingClientRect();
        window.scrollTo(0, Math.round(window.scrollY + r.top - pin.top + q * (r.height - pin.h)) + 1);
      }
    });
  }

  /* ---------- statements between sections: they slide in from their side as they scroll into view ---------- */
  function initSays() {
    var rows = $$('.says');
    if (!rows.length || reduced) return;
    rows.forEach(function (row) {
      row.classList.add('is-live');
      var says = $$('.say', row);
      var sched = scrollLoop(row, function (r, vh) {
        var t = clamp01((vh - r.top) / (vh * 0.75));
        var vw = window.innerWidth;
        says.forEach(function (s, k) {
          var dir = s.classList.contains('say--r') ? 1 : -1;
          var tt = clamp01(t * 1.25 - k * 0.12);
          s.style.setProperty('--sx', (dir * (1 - smooth(tt)) * vw * 0.12).toFixed(1) + 'px');
          s.style.setProperty('--so', (0.08 + 0.92 * smooth(tt)).toFixed(3));
        });
      });
      sched();
    });
  }

  /* ---------- 2. one makeover ---------- */
  function initMakeover() {
    var sec = $('#makeover');
    if (!sec) return;
    loadJson('makeover').then(function (d) {
      if (!d.steps || !d.steps.length) throw new Error('no steps');
      var video = $('#mk-video'), list = $('#mk-chapters'), speed = $('#mk-speed');
      var steps = d.steps.slice().sort(function (a, b) { return a.t - b.t; });
      function render() {
        list.textContent = '';
        steps.forEach(function (s, i) {
          var nm = label('mk.step.', s.id);
          if (!nm) return;
          var li = el('li'), b = el('button', null, nm);
          b.type = 'button'; li.setAttribute('data-i', i);
          b.addEventListener('click', function () { video.currentTime = s.t; if (video.paused) video.play().catch(function () {}); });
          li.appendChild(b); list.appendChild(li);
        });
        speed.textContent = d.note === 'steps' ? F(T('mk.steps') || '{n} steps, one frame after each, in order.', { n: d.count || steps.length })
          : d.speed > 1 ? F(T('mk.speed') || 'Shown {x} times faster.', { x: d.speed }) : (T('mk.realtime') || 'Real time.');
        mark();
      }
      function mark() {
        var cur = 0;
        steps.forEach(function (s, i) { if (video.currentTime >= s.t - 0.05) cur = i; });
        $$('li', list).forEach(function (li) {
          if (Number(li.getAttribute('data-i')) === cur) li.setAttribute('aria-current', 'step'); else li.removeAttribute('aria-current');
        });
      }
      video.addEventListener('timeupdate', mark);
      video.addEventListener('seeked', mark);
      video.preload = 'none';
      if (!noAuto && 'IntersectionObserver' in window) {
        new IntersectionObserver(function (en) {
          if (en[0].isIntersecting) { if (video.paused && !video._userPaused) { var p = video.play(); if (p && p.catch) p.catch(function () {}); } } else video.pause();
        }, { threshold: 0.5 }).observe(video);
        video.addEventListener('pause', function () { if (!video.ended && document.visibilityState === 'visible' && video._seen) video._userPaused = true; });
      }
      sec.hidden = false;
      var proof = $('#hero-proof'); if (proof) proof.hidden = false;
      render();
      onLang(render);
    }).catch(function () { /* the section stays hidden until the video exists */ });
  }

  /* ---------- 6. undo scrubber ---------- */
  function initUndo() {
    var root = $('#undo');
    if (!root) return;
    whenNear(root, function () {
      loadJson('history').then(function (d) {
        if (!d.steps || d.steps.length < 2) throw new Error('no steps');
        buildUndo(root, d);
      }).catch(function () { /* the History picture and "Ctrl+Z undoes every edit." stay */ });
    });
  }

  function buildUndo(root, d) {
    var view = $('#undo-view'), bar = $('#undo-bar'), range = $('#undo-range'), out = $('#undo-out'), list = $('#hist-list');
    var steps = d.steps, n = steps.length, cur = n - 1, imgs = [];
    range.max = String(n - 1);
    function src(s) { return BASE + (phone.matches && s.img_m ? s.img_m : s.img); }
    function ensure(i) {
      if (i < 0 || i >= n || imgs[i]) return imgs[i];
      var im = document.createElement('img');
      im.alt = ''; im.decoding = 'async'; im.src = src(steps[i]);
      view.appendChild(im);
      imgs[i] = im;
      return im;
    }
    function name(i) { return label('undo.step.', steps[i].id) || ''; }
    function textFor(i) { return F(T('undo.value') || 'Step {i} of {n}: {name}', { i: i + 1, n: n, name: name(i) }).replace(/:\s*$/, ''); }
    function show(i, fromList) {
      cur = i;
      for (var k = i - 2; k <= i + 2; k++) ensure(k);
      imgs.forEach(function (im, k) { if (im) im.classList.toggle('is-on', k === i); });
      range.value = String(i);
      range.setAttribute('aria-valuetext', textFor(i));
      out.textContent = textFor(i);
      $$('li', list).forEach(function (li, k) { li.classList.toggle('is-cur', k === i); });
      var li = $$('li', list)[i];
      if (li && !fromList) { var top = li.offsetTop - 120; if (Math.abs(list.scrollTop - top) > 60) list.scrollTop = Math.max(0, top); }
    }
    function renderList() {
      list.textContent = '';
      steps.forEach(function (s, i) {
        var li = el('li'), b = el('button');
        b.type = 'button';
        b.appendChild(el('span', 'hn', String(i + 1)));
        b.appendChild(el('span', null, name(i)));
        b.addEventListener('click', function () { show(i, true); });
        li.appendChild(b); list.appendChild(li);
      });
      show(cur);
    }
    /* it plays through the history by itself while in view, then starts over; touching the slider, a step or the
       button stops it */
    var play = $('#undo-play'), auto = !reduced, timer = 0, inView = false;
    function tick() {
      clearTimeout(timer);
      if (!auto || !inView) return;
      timer = setTimeout(function () {
        var atEnd = cur >= n - 1;
        show(atEnd ? 0 : cur + 1);
        tick();
      }, cur >= n - 1 ? 2200 : 950);
    }
    function paintPlay() {
      if (!play) return;
      play.classList.toggle('is-paused', !auto);
      play.setAttribute('aria-label', T(auto ? 'ui.pause' : 'ui.play') || (auto ? 'Pause' : 'Play'));
    }
    function stop() { auto = false; clearTimeout(timer); paintPlay(); }
    if (play) {
      play.hidden = reduced;
      play.addEventListener('click', function () { auto = !auto; if (auto && cur >= n - 1) show(0); paintPlay(); tick(); });
    }
    if ('IntersectionObserver' in window) {
      new IntersectionObserver(function (en) { inView = en[0].isIntersecting; if (inView) tick(); else clearTimeout(timer); }, { threshold: 0.4 }).observe(view);
    }
    if (auto) cur = 0;
    paintPlay();
    onLang(paintPlay);
    range.addEventListener('input', function () { stop(); show(Number(range.value)); });
    list.addEventListener('click', stop);
    root.setAttribute('data-ready', '');
    bar.hidden = false; list.hidden = false;
    var h = $('#undo-h');
    range.setAttribute('aria-labelledby', h ? 'undo-h' : '');
    renderList();
    onLang(renderList);
  }

  /* ---------- 7. your files stay safe ---------- */
  function initFiles() {
    var root = $('#files'), btn = $('#files-replay');
    if (!root || !('IntersectionObserver' in window) || reduced) return;
    root.classList.add('is-armed');
    /* it plays when the window is well in view (the middle of the screen), and again each time it comes back */
    var plate = $('.files-plate', root) || root;
    function play() {
      root.classList.remove('is-in');
      void root.offsetWidth;
      root.classList.add('is-in');
      var v = $('#files-loop video');
      if (v && !$('#files-loop').hidden) { try { v.currentTime = 0; } catch (e) {} }
    }
    new IntersectionObserver(function (en) {
      if (en[0].isIntersecting) { play(); if (btn) btn.hidden = false; }
    }, { rootMargin: '-30% 0px -30% 0px' }).observe(plate);
    new IntersectionObserver(function (en) {
      if (!en[0].isIntersecting) root.classList.remove('is-in');
    }).observe(plate);
    if (btn) btn.addEventListener('click', play);
  }

  /* ---------- 8. report card ---------- */
  function initReport() {
    var root = $('#report');
    if (!root) return;
    whenNear(root, function () {
      loadJson('audit').then(function (d) {
        if (!d.rows || !d.rows.length) throw new Error('no rows');
        function paint() {
          var counts = $('#rcard-counts');
          if (d.materials != null && d.textures != null) {
            counts.textContent = F(T('card.counts') || '{m} materials and {t} textures checked.', { m: d.materials, t: d.textures });
            counts.hidden = false;
          }
          d.rows.forEach(function (r) {
            var li = $('#rcard-rows [data-row="' + r.id + '"]');
            if (!li) return;
            var st = r.status === 'ok' || r.status === 'fixed' || r.status === 'warn' ? r.status : 'ok';
            var icon = $('.icon', li);
            icon.setAttribute('class', 'icon ' + (st === 'warn' ? 'is-warn' : 'is-ok'));
            icon.firstElementChild.setAttribute('href', st === 'warn' ? '#i-warn' : (st === 'fixed' ? '#i-fix' : '#i-check'));
            var txt = T('card.' + st) || st;
            $('.rstat', li).textContent = r.count ? txt + ' (' + r.count + ')' : txt;
          });
        }
        root.setAttribute('data-ready', '');
        paint();
        onLang(paint);
      }).catch(function () { /* the eight checks stay as a plain list, with the audit window picture */ });
    });
  }

  /* gallery "in any world": the stage holds while day turns to sunset and sunset to club */
  function initLight() {
    var root = $('#lw');
    if (!root || reduced || !('IntersectionObserver' in window)) return;
    var track = $('#lw-track'), shots = Array.prototype.slice.call(root.querySelectorAll('.lw-shot')), dots = Array.prototype.slice.call(root.querySelectorAll('.lw-dots li'));
    var n = shots.length, pin = pinMetrics(), copy = $('.lw-copy', root), lastW = 0, lastH = 0;
    if (n < 2) return;
    var pinned = !overview;
    if (pinned) root.setAttribute('data-pin', '');
    function size() {
      if (!pinned) return;
      pin = pinMetrics();
      root.style.setProperty('--pin-top', pin.top + 'px');
      root.style.setProperty('--stage-h', pin.h + 'px');
      track.style.height = Math.round(pin.h + (n - 1) * window.innerHeight * 0.7) + 'px';
      if (copy) root.style.setProperty('--copy-b', Math.round(copy.offsetTop + copy.offsetHeight + 8) + 'px');
      lastW = window.innerWidth; lastH = window.innerHeight;
    }
    function update(q) {
      if (!pinned) return;
      /* each light holds for a while, then hands over to the next */
      var x = q * (n - 1), i = Math.min(n - 2, Math.floor(x)), t = smooth(clamp01((x - i - 0.3) / 0.4)), p = i + t;
      root.style.setProperty('--flash', (Math.sin(Math.PI * t) * 0.6).toFixed(3));
      shots.forEach(function (s, k) {
        /* the next light fades in over the last one, which stays solid, so there is no see-through moment;
           all shots share one zoom so the avatar never doubles */
        s.style.setProperty('--o', clamp01(p - k + 1).toFixed(3));
        s.style.setProperty('--c', clamp01(1 - Math.abs(p - k)).toFixed(3));
        s.style.setProperty('--z', (1.02 + 0.06 * p).toFixed(4));
      });
      var cur = Math.round(p);
      dots.forEach(function (d, k) { d.classList.toggle('is-on', k === cur); });
    }
    size();
    update(0);
    var sched = scrollLoop(track, function (r) {
      update(clamp01((pin.top - r.top) / Math.max(1, r.height - pin.h)));
    });
    function again() { size(); sched(); }
    window.addEventListener('resize', function () {
      /* a phone's address bar sliding in or out only nudges the height; re-sizing then would make the stage jump */
      if (window.innerWidth === lastW && Math.abs(window.innerHeight - lastH) < 120) return;
      again();
    });
    document.addEventListener('ats:lang', again);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(again);
    /* Overview: the three lights side by side, nothing held */
    views.push({
      mode: function (ov) {
        pinned = !ov;
        if (ov) {
          root.removeAttribute('data-pin');
          track.style.height = '';
          shots.forEach(function (s) { ['--o', '--c', '--z'].forEach(function (p) { s.style.removeProperty(p); }); });
        } else { root.setAttribute('data-pin', ''); again(); }
      },
      current: function () { var r = root.getBoundingClientRect(); return r.top < window.innerHeight * 0.5 && r.bottom > window.innerHeight * 0.5 ? 0 : null; },
      show: function () { root.scrollIntoView({ block: 'start' }); }
    });
  }

  /* gallery close-ups: one big row each while scrolling, a grid in the overview */
  function initCloseups() {
    var rows = $$('.cu-row');
    if (!rows.length) return;
    views.push({
      mode: function () {},
      current: function () { var n = nearest(rows); return n ? rows.indexOf(n) : null; },
      show: function (i) { if (rows[i]) { rows[i].scrollIntoView({ block: 'center' }); flash(rows[i]); } }
    });
  }

  function boot() {
    initView(); initStack(); initModes(); initSays(); initLight(); initCloseups(); initMakeover(); initUndo(); initFiles(); initReport();
  }
  if (window.ATS) window.ATS.ready(boot); else document.addEventListener('DOMContentLoaded', boot);
})();
