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

  var forced = reduced;
  var viewQuery = /[?&]view=(scroll|overview)(?:&|$)/.exec(location.search);
  var preferOverview = viewQuery ? viewQuery[1] === 'overview' : (function () { try { return localStorage.getItem('ats-fview') === 'overview'; } catch (e) { return false; } })();
  var overview = forced || preferOverview;
  var views = [];
  function flash(node) {
    if (!node) return;
    node.classList.remove('is-here'); void node.offsetWidth; node.classList.add('is-here');
    setTimeout(function () { node.classList.remove('is-here'); }, 1800);
  }
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
    preferOverview = ov;
    try { localStorage.setItem('ats-fview', ov ? 'overview' : 'scroll'); } catch (e) {}
    applyView(ov);
  }
  function applyView(ov) {
    var at = null;
    views.some(function (v) { var i = v.current(); if (i != null) { at = { v: v, i: i }; return true; } return false; });
    overview = ov;
    document.documentElement.classList.toggle('fv-overview', ov);
    views.forEach(function (v) { v.mode(ov); });
    renderSwitch();
    if (at) requestAnimationFrame(function () { at.v.show(at.i); });
  }
  function renderSwitch() {
    $$('.fview button').forEach(function (b) {
      var on = (b.getAttribute('data-fv') === 'overview') === overview;
      b.setAttribute('aria-pressed', String(on));
      b.disabled = forced && b.getAttribute('data-fv') === 'scroll';
      b.title = b.disabled ? (T('fv.forced') || 'Overview is active.') : '';
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

  function smooth(x) { x = Math.max(0, Math.min(1, x)); return x * x * (3 - 2 * x); }
  function clamp01(x) { return Math.max(0, Math.min(1, x)); }

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
    }).catch(function () {  });
  }

  function initUndo() {
    var root = $('#undo');
    if (!root) return;
    whenNear(root, function () {
      loadJson('history').then(function (d) {
        if (!d.steps || d.steps.length < 2) throw new Error('no steps');
        buildUndo(root, d);
      }).catch(function () {  });
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
    var play = $('#undo-play'), auto = !noAuto, timer = 0, inView = false;
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

  function initFiles() {
    var root = $('#files'), btn = $('#files-replay');
    if (!root || !('IntersectionObserver' in window) || reduced) return;
    root.classList.add('is-armed');
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
      }).catch(function () {  });
    });
  }

  function initLight() {
    var root = $('#lw');
    if (!root || reduced || !('IntersectionObserver' in window)) return;
    var track = $('#lw-track'), shots = Array.prototype.slice.call(root.querySelectorAll('.lw-shot')), dots = Array.prototype.slice.call(root.querySelectorAll('.lw-dots li'));
    var n = shots.length, pin = pinMetrics(), copy = $('.lw-copy', root), lastW = 0, lastH = 0;
    var fadeDistance, lastLight = null;
    if (n < 2) return;
    var pinned = !overview;
    if (pinned) root.setAttribute('data-pin', '');
    function size() {
      if (!pinned) return;
      pin = pinMetrics();
      root.style.setProperty('--pin-top', pin.top + 'px');
      root.style.setProperty('--stage-h', pin.h + 'px');
      fadeDistance = (n - 1) * window.innerHeight * .7;
      track.style.height = Math.round(pin.h + fadeDistance + window.innerHeight * .65) + 'px';
      if (copy) root.style.setProperty('--copy-b', Math.round(copy.offsetTop + copy.offsetHeight + 8) + 'px');
      lastW = window.innerWidth; lastH = window.innerHeight;
    }
    function update(q) {
      if (!pinned) return;
      var x = q * (n - 1), i = Math.min(n - 2, Math.floor(x)), t = smooth(clamp01((x - i - 0.3) / 0.4)), p = i + t;
      if (p === lastLight) return;
      lastLight = p;
      root.style.setProperty('--flash', (Math.sin(Math.PI * t) * 0.6).toFixed(3));
      shots.forEach(function (s, k) {
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
      update(clamp01((pin.top - r.top) / Math.max(1, fadeDistance)));
    });
    function again() { size(); sched(); }
    window.addEventListener('resize', function () {
      if (window.innerWidth === lastW && Math.abs(window.innerHeight - lastH) < 120) return;
      again();
    });
    document.addEventListener('ats:lang', again);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(again);
    views.push({
      mode: function (ov) {
        pinned = !ov;
        lastLight = null;
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
    initView(); initSays(); initLight(); initCloseups(); initMakeover(); initUndo(); initFiles(); initReport();
  }
  if (window.ATS) window.ATS.ready(boot); else document.addEventListener('DOMContentLoaded', boot);
})();
