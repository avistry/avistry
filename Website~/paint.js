/* Avistry site: the drawing banner behind the title of every page head. A canvas paints the way the tool does: pieces
   picked with marching ants, a gradient fill, a dot pattern, brush strokes mirrored across a seam, a pencil, an
   airbrush, and a colour picked from a swatch and matched onto a piece. Every tool draws on its own layer, and the
   layers fan out like our plates as the page scrolls. Decoration only; one still frame with reduced motion. */
(function () {
  var reduced = (!!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) || document.documentElement.classList.contains('perf'));
  var CORAL = [255, 111, 145], VIOLET = [107, 78, 255], CYAN = [67, 220, 255];
  /* the timeline in scene seconds; it plays SPEED times faster than the clock */
  var CYCLE = 14.5, HOLD = 12.5, SPEED = 2.5;
  /* each page paints its own picture: the tools (features), the four looks (gallery), the skill tree (learn pages),
     a quieter one (about) */
  var PAGE = document.documentElement.getAttribute('data-page') || '';
  var LOOKS = [[[236, 120, 190], [140, 100, 230]], [[230, 40, 60], [255, 170, 60]], [[150, 210, 255], [235, 245, 255]], [[90, 220, 110], [120, 60, 200]]];

  function rgba(c, a) { return 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + a + ')'; }
  function mix(a, b, k) { return [a[0] + (b[0] - a[0]) * k | 0, a[1] + (b[1] - a[1]) * k | 0, a[2] + (b[2] - a[2]) * k | 0]; }
  function clamp(v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
  /* 0..1 progress of a step that runs from a to b seconds, eased */
  function span(t, a, b) { var k = clamp((t - a) / (b - a)); return k * k * (3 - 2 * k); }
  function rng(seed) {
    return function () {
      seed |= 0; seed = seed + 0x6D2B79F5 | 0;
      var x = Math.imul(seed ^ seed >>> 15, 1 | seed);
      x = x + Math.imul(x ^ x >>> 7, 61 | x) ^ x;
      return ((x ^ x >>> 14) >>> 0) / 4294967296;
    };
  }

  /* one composition: the pieces (UV islands), the strokes and the sprays, from a seed and the canvas size */
  function scene(seed, W, H) {
    var R = rng(seed), wide = W >= 900;
    var ax = wide ? W * 0.44 : W * 0.04, aw = (wide ? W * 0.54 : W * 0.92), ay = 12, ah = Math.max(80, H - 24);
    function island(cx, cy, r) {
      var n = 9, pts = [];
      for (var i = 0; i < n; i++) {
        var a = i / n * Math.PI * 2, k = 0.66 + R() * 0.42;
        pts.push([cx + Math.cos(a) * r * k * 1.25, cy + Math.sin(a) * r * k]);
      }
      var p = new Path2D(), m = function (i) { var a = pts[i % n], b = pts[(i + 1) % n]; return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2]; };
      var s = m(0);
      p.moveTo(s[0], s[1]);
      for (var j = 1; j <= n; j++) { var q = pts[j % n], e = m(j); p.quadraticCurveTo(q[0], q[1], e[0], e[1]); }
      p.closePath();
      return { path: p, cx: cx, cy: cy, r: r };
    }
    var r = Math.min(aw * 0.16, ah * 0.4);
    var isl = [
      island(ax + aw * (0.16 + R() * 0.04), ay + ah * (0.42 + R() * 0.12), r),
      island(ax + aw * (0.5 + R() * 0.04), ay + ah * (0.6 + R() * 0.08), r * 0.9),
      island(ax + aw * (0.82 + R() * 0.03), ay + ah * (0.36 + R() * 0.12), r * 0.8)
    ];
    /* a hand's line: two waves that drift apart, a little wobble, and a slight lift at the end */
    function wave(x0, x1, y, amp, n) {
      var pts = [], ph = R() * 6, ph2 = R() * 6, f = 0.7 + R() * 0.9, f2 = 1.8 + R() * 1.6, wob = 0;
      for (var i = 0; i <= n; i++) {
        var k = i / n;
        wob += (R() - 0.5) * 0.5; wob *= 0.85;
        pts.push([x0 + (x1 - x0) * k + Math.sin(k * 9 + ph) * amp * 0.15, y + (Math.sin(ph + k * f * Math.PI * 2) * 0.75 + Math.sin(ph2 + k * f2 * Math.PI * 2) * 0.25) * amp + wob * amp * 0.12 - k * k * amp * 0.4]);
      }
      return pts;
    }
    var mx = ax + aw * 0.5;
    var brush = [
      wave(ax + aw * 0.06, mx - aw * 0.03, ay + ah * 0.22, ah * 0.09, 80),
      wave(ax + aw * 0.12, mx - aw * 0.06, ay + ah * 0.82, ah * 0.07, 80)
    ];
    /* the pencil hatches: short diagonal strokes, each a little different */
    var pencil = [], c3 = isl[2];
    for (var i = 0; i < 16; i++) {
      var hx = c3.cx - c3.r * 1.1 + i * c3.r * 0.15 + (R() - 0.5) * 3, hy = c3.cy - c3.r * (0.55 + R() * 0.2), len = c3.r * (0.9 + R() * 0.5);
      pencil.push([[hx, hy], [hx + len * 0.35 + (R() - 0.5) * 3, hy + len * 0.5], [hx + len * 0.55, hy + len * (0.95 + R() * 0.1)]]);
    }
    var spray = [], path = wave(ax + aw * 0.55, ax + aw * 0.98, ay + ah * 0.78, ah * 0.1, 30);
    for (var s = 0; s < 360; s++) {
      var k = s / 360, at = path[Math.min(30, k * 30 | 0)], d = Math.abs(R() + R() + R() - 1.5) * 9 * (R() < 0.5 ? -1 : 1), a = R() * Math.PI * 2;
      spray.push([at[0] + Math.cos(a) * d * 1.6, at[1] + Math.sin(a) * d, 0.6 + R() * 1.6, k]);
    }
    var sw = [ax + aw - 74, ay + 12];
    /* the skill tree: six tiers climbing across, joined by brush lines */
    var tree = [], nr = Math.max(12, Math.min(ah * 0.13, 24));
    for (var t = 0; t < 6; t++) tree.push([ax + aw * (0.07 + t * 0.172) + (R() - 0.5) * 10, ay + ah * (0.84 - t * 0.08 - (t % 2) * 0.18) + (R() - 0.5) * 8]);
    var links = [];
    for (var l = 0; l < 5; l++) {
      var a0 = tree[l], b0 = tree[l + 1], pts = [];
      for (var q = 0; q <= 24; q++) { var u = q / 24; pts.push([a0[0] + (b0[0] - a0[0]) * u, a0[1] + (b0[1] - a0[1]) * u - Math.sin(u * Math.PI) * ah * 0.08 + (R() - 0.5) * 1.5]); }
      links.push(pts);
    }
    return { isl: isl, brush: brush, pencil: pencil, spray: spray, mx: mx, ax: ax, aw: aw, ay: ay, ah: ah, sw: sw, wide: wide, tree: tree, links: links, nr: nr };
  }

  function part(ctx, pts, f) {
    var n = Math.max(1, Math.floor((pts.length - 1) * f));
    ctx.beginPath();
    ctx.moveTo(pts[0][0], pts[0][1]);
    for (var i = 1; i <= n; i++) ctx.lineTo(pts[i][0], pts[i][1]);
    return pts[n];
  }
  /* a loaded brush: full in the middle of the stroke, thin where it lands and lifts, with dry bristle edges */
  function taper(ctx, pts, f, w, col, a) {
    var last = pts.length - 1, n = Math.max(1, Math.floor(last * f));
    ctx.strokeStyle = rgba(col, a);
    for (var i = 1; i <= n; i++) {
      var k = i / last, pr = 0.22 + 0.78 * Math.pow(Math.sin(Math.PI * k), 0.6);
      ctx.lineWidth = w * pr;
      ctx.beginPath(); ctx.moveTo(pts[i - 1][0], pts[i - 1][1]); ctx.lineTo(pts[i][0], pts[i][1]); ctx.stroke();
    }
    ctx.lineWidth = 1;
    [-0.5, 0.5].forEach(function (o, j) {
      ctx.strokeStyle = rgba(col, a * 0.45);
      ctx.beginPath();
      for (var i = 0; i <= n; i++) {
        var k = i / last, pr = 0.22 + 0.78 * Math.pow(Math.sin(Math.PI * k), 0.6), y = pts[i][1] + o * w * pr * 1.05;
        if (i === 0 || (i + j * 3) % 11 === 0) ctx.moveTo(pts[i][0], y); else ctx.lineTo(pts[i][0], y);
      }
      ctx.stroke();
    });
    return pts[n];
  }
  function ring(ctx, x, y, r, c) {
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = c;
    ctx.stroke();
  }

  /* quiet: brush strokes, airbrush and swatches only (the footer) */
  function setup(head, quiet) {
    if (head.querySelector('.paint-fx')) return;
    var c = document.createElement('canvas');
    c.className = 'paint-fx';
    c.setAttribute('aria-hidden', 'true');
    head.classList.add('has-paint');
    head.insertBefore(c, head.firstChild);
    var ctx = c.getContext('2d');
    if (!ctx) return;
    var W = 0, H = 0, S = null, seed = 7 + (location.pathname.length * 131), t0 = performance.now(), raf = 0, seen = true, last = 0;
    var ink = getComputedStyle(head).color || '#F3F1F6';

    function size() {
      var r = head.getBoundingClientRect(), dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      W = Math.round(r.width); H = Math.round(r.height);
      c.width = Math.max(1, W * dpr); c.height = Math.max(1, H * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      S = scene(seed, W, H);
      ink = getComputedStyle(head).color || ink;
    }

    function draw(t, fan) {
      ctx.clearRect(0, 0, W, H);
      if (!S) return;
      var fade = t > HOLD ? 1 - span(t, HOLD, CYCLE - 0.3) : 1;
      if (fade <= 0) return;
      ctx.globalAlpha = fade;
      ctx.lineJoin = ctx.lineCap = 'round';
      var isl = S.isl, L = 0;
      /* each tool on its own layer; the layers part like plates as the page scrolls */
      function layer(fn) {
        var d = L++ * fan;
        ctx.save();
        ctx.translate(d, -d * 0.6);
        fn();
        ctx.restore();
      }
      var matched = span(t, 6.1, 7.2);
      /* it paints in the picked look (app.js sets data-tint); Original paints in ours */
      var li = ['original', 'crimson', 'frost', 'venom'].indexOf(document.documentElement.getAttribute('data-tint'));
      var look = li > 0 ? LOOKS[li] : null, A = look ? look[0] : CORAL, B = look ? look[1] : VIOLET;
      /* in a picked look its own colours stand in for ours */
      var CO = look ? A : CORAL, VI = look ? B : VIOLET, CY = look ? mix(A, [255, 255, 255], 0.45) : CYAN;
      var tree = !quiet && PAGE === 'learn', calm = quiet || PAGE === 'about';
      /* layer 1: the gradient fill, wiped across the first piece, then recoloured by the colour match */
      layer(function () {
        var k = span(t, 0.7, 1.9);
        if (!k || tree) return;
        var a = isl[0], g = ctx.createLinearGradient(a.cx - a.r * 1.3, 0, a.cx + a.r * 1.3, 0);
        g.addColorStop(0, rgba(mix(look ? B : VI, CY, matched), 0.85));
        g.addColorStop(1, rgba(mix(A, look ? A : VI, matched), 0.85));
        ctx.save();
        ctx.clip(a.path);
        ctx.fillStyle = g;
        ctx.fillRect(a.cx - a.r * 1.4, a.cy - a.r * 1.2, a.r * 2.8 * k, a.r * 2.4);
        ctx.restore();
        if (matched > 0 && matched < 1) {
          ctx.save();
          ctx.clip(a.path);
          ring(ctx, S.sw[0] + 48, S.sw[1], 6 + matched * a.r * 3, rgba(CY, 0.9 * (1 - matched)));
          ctx.restore();
        }
      });
      /* layer 2: a halftone dot pattern, row by row, in the middle piece */
      layer(function () {
        var k = span(t, 1.5, 2.8);
        if (!k || tree || calm) return;
        var b = isl[1], step = 7, top = b.cy - b.r * 1.1, rows = Math.ceil(b.r * 2.2 / step);
        ctx.save();
        ctx.clip(b.path);
        ctx.fillStyle = rgba(look ? B : CY, 0.75);
        for (var row = 0; row < rows * k; row++) {
          var y = top + row * step;
          for (var x = b.cx - b.r * 1.4 + (row % 2) * step / 2; x < b.cx + b.r * 1.4; x += step) {
            ctx.beginPath();
            ctx.arc(x, y, 1.1 + 1.4 * Math.abs(Math.sin((x + y) * 0.03)), 0, Math.PI * 2);
            ctx.fill();
          }
        }
        ctx.restore();
      });
      /* layer 3: brush strokes and their mirror across the seam (symmetry) */
      layer(function () {
        var k = span(t, 2.4, 4.3);
        if (!k) return;
        var seamA = span(t, 2.2, 2.6) * (1 - span(t, 5, 5.6));
        if (seamA) {
          ctx.save();
          ctx.setLineDash([4, 6]);
          ctx.lineWidth = 1;
          ctx.strokeStyle = rgba(CY, 0.6 * seamA);
          ctx.beginPath(); ctx.moveTo(S.mx, S.ay); ctx.lineTo(S.mx, S.ay + S.ah); ctx.stroke();
          ctx.restore();
        }
        S.brush.forEach(function (pts, i) {
          var f = clamp(k * 1.5 - i * 0.5);
          if (!f) return;
          [1, -1].forEach(function (side) {
            ctx.save();
            if (side < 0) { ctx.translate(S.mx * 2, 0); ctx.scale(-1, 1); }
            var head = taper(ctx, pts, f, i ? 9 : 15, i ? B : A, 0.9);
            if (f < 1 && side > 0) ring(ctx, head[0], head[1], i ? 8 : 11, rgba([255, 255, 255], 0.85));
            ctx.restore();
          });
        });
      });
      /* layer 4: a pencil hatch over the third piece */
      layer(function () {
        var k = span(t, 3.7, 5);
        if (!k || tree || calm) return;
        var all = S.pencil.length * k, head = null;
        ctx.lineWidth = 1.3;
        ctx.strokeStyle = ink;
        ctx.globalAlpha = fade * 0.7;
        for (var h = 0; h < all; h++) {
          var ln = S.pencil[h], f = Math.min(1, all - h);
          ctx.beginPath(); ctx.moveTo(ln[0][0], ln[0][1]);
          var mx2 = ln[0][0] + (ln[2][0] - ln[0][0]) * f, my2 = ln[0][1] + (ln[2][1] - ln[0][1]) * f;
          ctx.quadraticCurveTo(ln[1][0], ln[1][1], mx2, my2);
          ctx.stroke();
          head = [mx2, my2];
        }
        ctx.globalAlpha = fade;
        if (k < 1 && head) { ctx.fillStyle = ink; ctx.beginPath(); ctx.arc(head[0], head[1], 2.2, 0, Math.PI * 2); ctx.fill(); }
      });
      /* layer 5: airbrush */
      layer(function () {
        var k = span(t, 4.5, 5.9);
        if (!k) return;
        ctx.fillStyle = rgba(CY, 0.45);
        for (var i = 0; i < S.spray.length && S.spray[i][3] <= k; i++) {
          var p = S.spray[i];
          ctx.fillRect(p[0], p[1], p[2], p[2]);
        }
      });
      /* learn pages: the six tiers light up one after another along brush lines, the last one gets a medal */
      if (tree) layer(function () {
        S.links.forEach(function (pts, i) {
          var f = span(t, 0.5 + i * 0.75, 1.2 + i * 0.75);
          if (f) taper(ctx, pts, f, 7, i % 2 ? VI : CY, 0.85);
        });
        var cols = [CO, VI, CY];
        S.tree.forEach(function (n, i) {
          var k = span(t, 0.2 + i * 0.75, 0.6 + i * 0.75);
          if (!k) return;
          var rr = S.nr * (0.6 + 0.4 * k) * (1 + 0.15 * Math.sin(Math.PI * k));
          ctx.fillStyle = rgba(VI, 0.9);
          ctx.beginPath(); ctx.arc(n[0] + 3, n[1] + 3, rr, 0, Math.PI * 2); ctx.fill();
          ctx.fillStyle = rgba(cols[i % 3], 1);
          ctx.beginPath(); ctx.arc(n[0], n[1], rr, 0, Math.PI * 2); ctx.fill();
          ctx.fillStyle = '#1A1033';
          ctx.font = '600 ' + Math.round(rr * 1.1) + 'px Fredoka, system-ui, sans-serif';
          ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
          ctx.fillText(String(i + 1), n[0], n[1] + 1);
        });
        var m = span(t, 5, 5.6), end = S.tree[5];
        if (m) {
          ctx.save();
          ctx.translate(end[0], end[1] - S.nr * 2.1);
          ctx.rotate(Math.PI / 4 * m);
          ctx.fillStyle = rgba(CO, m);
          for (var sp = 0; sp < 4; sp++) { ctx.rotate(Math.PI / 2); ctx.beginPath(); ctx.moveTo(0, 0); ctx.quadraticCurveTo(2, -S.nr * 0.25, 0, -S.nr * 0.8 * m); ctx.quadraticCurveTo(-2, -S.nr * 0.25, 0, 0); ctx.fill(); }
          ctx.restore();
        }
      });
      /* on top: the pieces' outlines as marching ants, the swatches and the colour picker */
      layer(function () {
        var k = span(t, 0, 1.1);
        ctx.save();
        ctx.setLineDash([5, 4]);
        ctx.lineDashOffset = -t * 18;
        ctx.lineWidth = 1.5;
        if (!tree) isl.forEach(function (p, i) {
          if (calm && i) return;
          ctx.globalAlpha = fade * clamp(k * 3 - i) * 0.8;
          ctx.strokeStyle = i === 0 && matched > 0.5 ? rgba(CY, 1) : ink;
          ctx.stroke(p.path);
        });
        ctx.restore();
        var sk = span(t, 5.4, 5.9);
        if (!sk) return;
        (look ? [A, B, CY] : [CO, VI, CY]).forEach(function (col, i) {
          ctx.globalAlpha = fade * sk;
          ctx.fillStyle = rgba(col, 1);
          ctx.beginPath();
          ctx.arc(S.sw[0] + i * 24, S.sw[1], 7, 0, Math.PI * 2);
          ctx.fill();
        });
        /* the picker travels from the first piece to the cyan swatch, picks, and the match runs back */
        var go = span(t, 5.6, 6.2), a = isl[0];
        if (go > 0 && t < 7.6) {
          var x = a.cx + (S.sw[0] + 48 - a.cx) * go, y = a.cy + (S.sw[1] - a.cy) * go;
          ctx.globalAlpha = fade * (1 - span(t, 7.2, 7.6));
          ring(ctx, x, y, 12, 'rgba(255,255,255,0.95)');
          ctx.fillStyle = rgba(go >= 1 ? CY : [255, 255, 255], 0.9);
          ctx.beginPath(); ctx.arc(x, y, 4, 0, Math.PI * 2); ctx.fill();
        }
        ctx.globalAlpha = fade;
      });
    }

    function fanOf() {
      var r = head.getBoundingClientRect();
      return reduced ? 0 : Math.max(0, Math.min(1, -r.top / Math.max(1, r.height))) * 22;
    }

    function frame(now) {
      raf = 0;
      if (!c.isConnected) return;
      var t = (now - t0) / 1000 * SPEED;
      if (t >= CYCLE) { t0 = now; t = 0; seed++; S = scene(seed, W, H); }
      /* while it only holds, a few frames a second are enough for the ants */
      if (t > 7.6 && t < HOLD && now - last < 50) { schedule(); return; }
      last = now;
      draw(t, fanOf());
      schedule();
    }
    function schedule() { if (!raf && seen && !document.hidden && c.isConnected) raf = requestAnimationFrame(frame); }

    size();
    if (reduced) { draw(7.6, 0); }
    else {
      schedule();
      if ('IntersectionObserver' in window) {
        new IntersectionObserver(function (en) { seen = en[0].isIntersecting; schedule(); }).observe(head);
      }
      document.addEventListener('visibilitychange', schedule);
    }
    var rt = 0;
    function resized() { clearTimeout(rt); rt = setTimeout(function () { if (!c.isConnected) return; size(); if (reduced) draw(7.6, 0); }, 120); }
    if (reduced) window.addEventListener('ats-tint', function () { if (c.isConnected) draw(7.6, 0); });
    if ('ResizeObserver' in window) new ResizeObserver(resized).observe(head);
    else window.addEventListener('resize', resized);
  }

  /* a brush mark under each section title, the brushes taking turns down the page */
  var timeline = !!(window.CSS && CSS.supports && CSS.supports('animation-timeline: view()'));
  var io = !timeline && !reduced && 'IntersectionObserver' in window ? new IntersectionObserver(function (en) {
    en.forEach(function (e) {
      if (!e.isIntersecting) return;
      io.unobserve(e.target);
      e.target.classList.add(e.target.classList.contains('doodle') ? 'dd-in' : 'bm-in');
    });
  }, { threshold: 0.6 }) : null;
  function marks() {
    var n = 0;
    document.querySelectorAll('main h2, .page-head h1, .pg-hero h1').forEach(function (h) {
      if (h.id === 'install-title' || h.closest('.tdock, .community, .inst-card') || h.classList.contains('sr-only') || / bm-\d/.test(' ' + h.className)) return;
      h.classList.add('bm', 'bm-' + (n++ % 6 + 1));
      if (io) { h.classList.add('bm-io'); io.observe(h); }
    });
  }

  /* loose doodles in the wide margins: a loop, a spiral, a star, a zigzag, a wave with dots; left and right in turn */
  var DOODLES = [
    '<path pathLength="1" d="M30 110C10 60 60 10 110 30S170 120 110 140 30 130 40 90 100 50 130 80"/>',
    '<path pathLength="1" d="M85 85m0 0c8-6 20 0 18 12s-22 22-36 12-12-40 10-48 50 6 52 34-24 54-58 50-52-38-44-70 44-54 78-44"/>',
    '<path pathLength="1" d="M85 20L98 68 148 72 108 100 122 148 85 120 48 148 62 100 22 72 72 68Z"/><circle cx="150" cy="30" r="5"/><circle cx="25" cy="140" r="4"/>',
    '<path pathLength="1" d="M10 60L40 30 70 60 100 30 130 60 160 30M10 110L40 80 70 110 100 80 130 110 160 80"/>',
    '<path pathLength="1" d="M5 90C30 40 55 140 85 90S140 40 165 90"/><circle cx="40" cy="140" r="5"/><circle cx="62" cy="140" r="5"/><circle cx="84" cy="140" r="5"/>'
  ];
  var INKS = [['var(--cyan)', 'var(--coral)'], ['var(--coral)', 'var(--violet)'], ['var(--violet)', 'var(--cyan)']];
  function doodles() {
    var k = 0;
    document.querySelectorAll('main > .sec, main > section.sec').forEach(function (sec) {
      if (sec.querySelector(':scope > .doodle') || sec.classList.contains('stage-sec') || sec.offsetHeight < 320) return;
      var ink = INKS[k % 3], d = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      d.setAttribute('class', 'doodle ' + (k % 2 ? 'is-l' : 'is-r'));
      d.setAttribute('viewBox', '0 0 170 170');
      d.setAttribute('aria-hidden', 'true');
      d.setAttribute('focusable', 'false');
      d.innerHTML = DOODLES[k % DOODLES.length];
      d.querySelectorAll('path').forEach(function (p) { p.style.stroke = ink[0]; p.setAttribute('stroke-width', '3'); });
      d.querySelectorAll('circle').forEach(function (c) { c.setAttribute('fill', ink[1]); c.setAttribute('style', 'fill:' + ink[1]); });
      sec.classList.add('has-doodle');
      sec.insertBefore(d, sec.firstChild);
      if (io) { d.classList.add('dd-io'); io.observe(d); }
      k++;
    });
  }

  /* the footer: the drawing canvas behind it, our name painted large along the bottom, and a brush that follows the
     pointer and fades (mouse only) */
  function footer() {
    var foot = document.querySelector('.foot'), inner = foot && foot.querySelector('.foot-in');
    if (!foot || !inner) return;
    if (!inner.querySelector('.foot-mark')) {
      var mark = document.createElement('p');
      mark.className = 'foot-mark';
      mark.setAttribute('aria-hidden', 'true');
      mark.appendChild(document.createTextNode('Avistry'));
      var dot = document.createElement('span');
      dot.textContent = '.';
      mark.appendChild(dot);
      inner.appendChild(mark);
      if (!timeline && !reduced && 'IntersectionObserver' in window) {
        var mo = new IntersectionObserver(function (en) { if (en[0].isIntersecting) { mark.classList.add('is-in'); mo.disconnect(); } }, { threshold: 0.4 });
        mo.observe(mark);
      }
    }
    setup(foot, true);
  }

  function start() {
    document.querySelectorAll('.page-head, .pg-hero').forEach(function (h) { setup(h); });
    marks();
    doodles();
    footer();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
