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
  var reduced = !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
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
      if (lead) {
        lead.removeAttribute('data-i18n');
        var setLead = function () { lead.textContent = F(T('stack.lead') || '', { n: n }); };
        setLead();
        onLang(setLead);
      }
      update(lastE, lastQ);
    }

    var lastE = 1, lastQ = 0.5, pinned = false, pin = { top: 76, h: 560 }, OPEN = 0.2;
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
      var focus = m ? Number(items[cur].getAttribute('data-i')) : -1;
      if (pinned) paintStrip(items, f);
      items.forEach(function (li, k) { li.classList.toggle('is-near', pinned && k === cur); });
      all.forEach(function (s, k) { s.classList.toggle('is-focus', (d.texture ? k : k + 1) === focus && focusing); });
      if (m) counter(count, cur + 1, m);
    }

    root.setAttribute('data-state', 'ready');
    track.hidden = false;
    pinned = !reduced;
    if (pinned) {
      root.setAttribute('data-pin', '');
      if (head) stage.insertBefore(head, stage.firstChild);
    }
    size();
    renderLabels();
    if (window.ATS) window.ATS.setLayerCount(n);
    if (!pinned) { update(1, 0.5); } else {
      var sched = scrollLoop(track, function (r, vh) {
        /* it arrives folded into one texture and pulls apart once it holds */
        var q = clamp01((pin.top - r.top) / Math.max(1, r.height - pin.h));
        var e = smooth(clamp01(q / OPEN));
        update(e, q);
      });
      window.addEventListener('resize', function () { size(); sched(); });
    }
    onLang(renderLabels);
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

    if (reduced) {
      /* no motion: every result as a still, in two groups */
      var renderGrid = function () {
        grid.textContent = '';
        ['b', 'a'].forEach(function (k) {
          var group = rows.filter(function (r) { return r.kind === k; });
          if (!group.length) return;
          grid.appendChild(el('h3', null, kindOf(group[0])));
          group.forEach(function (r) {
            var fig = el('figure'), im = document.createElement('img');
            im.src = BASE + r.img; im.alt = ''; im.loading = 'lazy'; im.decoding = 'async'; im.width = 360; im.height = 360;
            fig.appendChild(im); fig.appendChild(el('figcaption', null, nameOf(r)));
            grid.appendChild(fig);
          });
        });
      };
      grid.hidden = false;
      renderGrid();
      onLang(renderGrid);
      root.setAttribute('data-state', 'ready');
      return;
    }

    var imgs = rows.map(function (r, i) {
      var im = document.createElement('img');
      im.alt = ''; im.decoding = 'async'; im.width = 720; im.height = 720;
      if (i < 3) im.src = BASE + r.img; else im.setAttribute('data-src', BASE + r.img);
      view.insertBefore(im, nameEl);
      return im;
    });
    /* the rest load right after, while the section is still on its way */
    setTimeout(function () { imgs.forEach(function (im) { var s = im.getAttribute('data-src'); if (s) { im.src = s; im.removeAttribute('data-src'); } }); }, 300);

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
    root.setAttribute('data-pin', '');
    track.hidden = false;
    size();
    renderList();
    var sched = scrollLoop(track, function (r) { update(clamp01((pin.top - r.top) / Math.max(1, r.height - pin.h))); });
    window.addEventListener('resize', function () { size(); sched(); });
    onLang(renderList);
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
    range.addEventListener('input', function () { show(Number(range.value)); });
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
    var played = false;
    new IntersectionObserver(function (en, io) {
      if (en[0].isIntersecting && !played) { played = true; root.classList.add('is-in'); if (btn) btn.hidden = false; io.disconnect(); }
    }, { threshold: 0.5 }).observe(root);
    if (btn) btn.addEventListener('click', function () {
      root.classList.remove('is-in');
      void root.offsetWidth;
      root.classList.add('is-in');
    });
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
    root.setAttribute('data-pin', '');
    function size() {
      pin = pinMetrics();
      root.style.setProperty('--pin-top', pin.top + 'px');
      root.style.setProperty('--stage-h', pin.h + 'px');
      track.style.height = Math.round(pin.h + (n - 1) * window.innerHeight * 0.7) + 'px';
      if (copy) root.style.setProperty('--copy-b', Math.round(copy.offsetTop + copy.offsetHeight + 8) + 'px');
      lastW = window.innerWidth; lastH = window.innerHeight;
    }
    function update(q) {
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
  }

  function boot() {
    initStack(); initModes(); initSays(); initLight(); initMakeover(); initUndo(); initFiles(); initReport();
  }
  if (window.ATS) window.ATS.ready(boot); else document.addEventListener('DOMContentLoaded', boot);
})();
