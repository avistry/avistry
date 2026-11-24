(function () {
  var reduced = (!!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) || document.documentElement.classList.contains('perf'));
  var CORAL = [255, 111, 145], VIOLET = [107, 78, 255], CYAN = [67, 220, 255];
  var FINISH = 7.6, SPEED = 2.5;
  var PAGE = document.documentElement.getAttribute('data-page') || '';
  var LOOKS = [[[236, 120, 190], [140, 100, 230]], [[230, 40, 60], [255, 170, 60]], [[150, 210, 255], [235, 245, 255]], [[90, 220, 110], [120, 60, 200]]];
  var visitSeed = Math.floor(Math.random() * 0x100000000);
  var editions = {
    home: ['blossom', 'contour', 'ribbon'], features: ['blossom', 'contour', 'ribbon'],
    gallery: ['ribbon', 'blossom', 'tools'], learn: ['tree', 'blossom', 'ribbon'],
    about: ['blossom', 'contour', 'ribbon'], download: ['ribbon', 'contour', 'tools']
  };
  var options = editions[PAGE] || editions.home;
  var cover = options[visitSeed % options.length];
  var signatures = ['blossom', 'contour', 'ribbon'].filter(function (name) { return name !== cover; });
  var signature = signatures[Math.floor(visitSeed / 13) % signatures.length];
  var widePaper = matchMedia('(min-width: 901px) and (min-height: 500px) and (hover: hover) and (pointer: fine)');
  var usedPrints = new Set();

  function pickPrint(library, seed, isSignature) {
    var random = rng(seed), chance = random();
    var tier = chance < 1 / 512 ? 'veryRare' : chance < 1 / 512 + 1 / 12 ? 'special' : 'normal';
    var choices = library[tier];
    if (tier === 'normal') choices = choices.concat(isSignature ? signatures : options);
    var remaining = choices.filter(function (name) { return !usedPrints.has(name); });
    if (remaining.length) choices = remaining;
    var name = choices[Math.floor(random() * choices.length)];
    usedPrints.add(name);
    return { name: name, tier: tier, newPrint: library[tier].indexOf(name) >= 0 };
  }

  function rgba(c, a) { return 'rgba(' + c[0] + ',' + c[1] + ',' + c[2] + ',' + a + ')'; }
  function mix(a, b, k) { return [a[0] + (b[0] - a[0]) * k | 0, a[1] + (b[1] - a[1]) * k | 0, a[2] + (b[2] - a[2]) * k | 0]; }
  function clamp(v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
  function span(t, a, b) { var k = clamp((t - a) / (b - a)); return k * k * (3 - 2 * k); }
  function rng(seed) {
    return function () {
      seed |= 0; seed = seed + 0x6D2B79F5 | 0;
      var x = Math.imul(seed ^ seed >>> 15, 1 | seed);
      x = x + Math.imul(x ^ x >>> 7, 61 | x) ^ x;
      return ((x ^ x >>> 14) >>> 0) / 4294967296;
    };
  }

  function scene(seed, W, H) {
    var R = rng(seed);
    var fanStep = Math.min(18, W * .025, H * .035);
    var ax = W * .04, aw = W * .92 - 5 * fanStep;
    var ay = 12 + 3 * fanStep, ah = Math.max(80, H - 24) - 3 * fanStep;
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
    var tree = [], nr = Math.max(12, Math.min(ah * 0.13, 24));
    for (var t = 0; t < 6; t++) tree.push([ax + aw * (0.07 + t * 0.172) + (R() - 0.5) * 10, ay + ah * (0.84 - t * 0.08 - (t % 2) * 0.18) + (R() - 0.5) * 8]);
    var links = [];
    for (var l = 0; l < 5; l++) {
      var a0 = tree[l], b0 = tree[l + 1], pts = [];
      for (var q = 0; q <= 24; q++) { var u = q / 24; pts.push([a0[0] + (b0[0] - a0[0]) * u, a0[1] + (b0[1] - a0[1]) * u - Math.sin(u * Math.PI) * ah * 0.08 + (R() - 0.5) * 1.5]); }
      links.push(pts);
    }
    return { isl: isl, brush: brush, pencil: pencil, spray: spray, mx: mx, ax: ax, aw: aw, ay: ay, ah: ah, sw: sw, tree: tree, links: links, nr: nr };
  }

  function part(ctx, pts, f) {
    var n = Math.max(1, Math.floor((pts.length - 1) * f));
    ctx.beginPath();
    ctx.moveTo(pts[0][0], pts[0][1]);
    for (var i = 1; i <= n; i++) ctx.lineTo(pts[i][0], pts[i][1]);
    return pts[n];
  }
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

  function print(kind, seed) {
    var random = rng(seed), marks = [];
    function mark(path, ink, weight, start, length, x, y, angle, scale, alpha) {
      var item = { path: new Path2D(path), ink: ink, weight: weight, start: start, length: length,
        x: x || 0, y: y || 0, angle: angle || 0, scale: scale || 1, alpha: alpha == null ? 1 : alpha };
      marks.push(item);
      return item;
    }
    function sparkle(x, y, r, ink, time) {
      mark('M0 -11 Q1 -1 9 0 Q1 1 0 11 Q-1 1 -9 0 Q-1 -1 0 -11Z', ink, 0, time, 0, x, y, .16, r / 11);
    }
    var phase = random() * Math.PI * 2;
    if (kind === 'blossom') {
      mark('M15 145C92 147 147 88 217 99S330 79 414 39Q448 23 476 29', 0, 4, 0, 524);
      mark('M94 131Q119 103 138 76M158 107Q158 69 188 47M206 98Q231 121 255 130M272 96Q296 64 307 39M354 67Q376 77 407 69', 0, 2, .12, 110);
      var leaf = 'M0 0Q7 -13 30 -9Q20 1 0 0Z';
      [[73,137,-.25],[117,105,-.88],[153,107,2.34],[176,60,-.92],[242,121,.7],[296,65,-1.1],[328,83,2.65],[375,72,.65],[431,33,-.5]].forEach(function (p, i) {
        mark(leaf, i % 2 ? 1 : 0, 0, .19 + i * .025, 0, p[0], p[1], p[2], .65 + random() * .4);
      });
      var petal = 'M0 1C-15 -3 -22 -15 -17 -23Q-14 -28 -9 -22Q-7 -30 -3 -28C8 -24 9 -9 0 1Z';
      [[139,77,.66],[189,46,.95],[218,98,1.1],[256,130,.68],[308,40,.62],[357,67,.88],[405,68,.49],[449,29,.62]].forEach(function (flower, i) {
        var angle = random() * 6.28, start = .27 + i * .043;
        for (var n = 0; n < 5; n++) {
          var turn = angle + n * Math.PI * .4, size = flower[2] * (.8 + random() * .31);
          mark(petal, n % 3 ? 1 : 2, 0, start + .012 * n, 0, flower[0], flower[1], turn, size);
          mark('M0 0Q-6 -8 -9 -19', 3, 1, start + .07, 23, flower[0], flower[1], turn, size, .75).fine = true;
        }
        sparkle(flower[0], flower[1], 3.8 * flower[2], 3, start + .12);
      });
      for (var j = 0; j < 13; j++) {
        mark(petal, j % 2 ? 1 : 2, 0, .48 + random() * .27, 0, 90 + random() * 346, 110 + random() * 44, random() * 6, .12 + random() * .14, .7);
      }
    } else if (kind === 'contour') {
      var ridges = [];
      for (var i = 0; i <= 16; i++) {
        var path = '', size = 1 - i * .053;
        for (var n = 0; n <= 150; n++) {
          var a = n * Math.PI / 75, wave = 1 + .14 * Math.sin(a * 3 + phase) + .055 * Math.sin(a * 6 - phase);
          var x = 238 + i * 2.8 + Math.cos(a) * 196 * wave * size;
          var y = 88 - i * .8 + (Math.sin(a) * 60 * wave - Math.cos(a) * 14) * size;
          path += (n ? 'L' : 'M') + x.toFixed(1) + ' ' + y.toFixed(1);
        }
        ridges.push(path + 'Z');
      }
      mark(ridges[0], 0, 0, 0, 0, 0, 0, 0, 1, .21);
      ridges.forEach(function (p, i) {
        mark(p, i % 6 === 0 ? 2 : i % 5 === 0 ? 3 : 0, i % 4 === 0 ? 2.6 : 1.25, .03 + i * .03, 1050 * (1 - .048 * i)).fine = i % 2 === 1;
      });
      [[369,39,9],[430,73,6],[133,151,7],[462,35,4]].forEach(function (p, i) { sparkle(p[0],p[1],p[2],i % 2 ? 1 : 2,.58 + i * .04); });
    } else {
      for (var strand = 0; strand < 24; strand++) {
        var r = strand / 23 - .5, p = '';
        for (var k = 0; k <= 100; k++) {
          var u = k / 100, envelope = Math.pow(Math.sin(Math.PI * u), .62);
          var cx = 18 + u * 458, cy = 88 + Math.sin(u * 6 + phase) * 29 + Math.sin(u * 11 - phase) * 13;
          var width = 70 * envelope * Math.cos(u * 10 + phase);
          var xx = cx + 23 * r * envelope * Math.sin(u * 10 + phase);
          p += (k && !(strand % 5 === 0 && (k + strand) % 18 === 0) ? 'L' : 'M') + xx.toFixed(1) + ' ' + (cy + width * r).toFixed(1);
        }
        mark(p, strand < 9 ? 0 : strand < 16 ? 1 : 2, strand % 8 === 0 ? 3.4 : 1.45, strand * .017, 650).fine = strand % 3 !== 0 && strand !== 23;
      }
      [[85,39,10],[373,134,12],[407,121,5],[307,32,4]].forEach(function (p, i) { sparkle(p[0], p[1], p[2], i % 2 ? 0 : 2, .56 + .038 * i); });
    }
    return marks;
  }

  var watchPrint;
  function readingArea() {
    var header = document.getElementById('hdr'), bar = document.getElementById('cta-bar');
    var panels = new Map(), observer, sizes, changes, margin, pending = 0;

    function refresh() {
      pending = 0;
      if (document.hidden || !panels.size) return;
      var top = header ? Math.max(0, Math.ceil(header.getBoundingClientRect().bottom)) : 0;
      var bottom = 16;
      if (bar && bar.offsetHeight) {
        var covered = bar.classList.contains('is-on') ? bar.offsetHeight : innerHeight - bar.getBoundingClientRect().top;
        bottom += Math.max(0, Math.ceil(covered));
      }
      var nextMargin = -top + 'px 0px ' + -bottom + 'px';
      if (nextMargin === margin) return;
      margin = nextMargin;
      if (observer) observer.disconnect();
      observer = new IntersectionObserver(function (entries, source) {
        if (source !== observer) return;
        entries.forEach(function (entry) {
          var panel = entry.target;
          var notify = panels.get(panel);
          if (notify) notify(panel.isConnected && entry.isIntersecting && entry.intersectionRatio >= .65 && entry.boundingClientRect.height > 0);
        });
      }, {rootMargin: margin, threshold: .65});
      panels.forEach(function (_, panel) { observer.observe(panel); });
    }
    function queue() {
      if (!pending && !document.hidden && panels.size) pending = requestAnimationFrame(refresh);
    }
    if ('ResizeObserver' in window) {
      sizes = new ResizeObserver(queue);
      if (header) sizes.observe(header);
      if (bar) sizes.observe(bar);
    }
    if (bar) {
      if ('MutationObserver' in window) {
        changes = new MutationObserver(queue);
        changes.observe(bar, {attributes: true, attributeFilter: ['class', 'hidden']});
      }
      bar.addEventListener('transitionend', queue);
    }
    window.addEventListener('resize', queue, {passive: true});
    document.addEventListener('visibilitychange', queue);
    return function (panel, onVisible) {
      panels.set(panel, onVisible);
      if (observer) observer.observe(panel); else refresh();
      return function () {
        panels.delete(panel);
        if (observer) observer.unobserve(panel);
        if (!panels.size) {
          if (sizes) sizes.disconnect();
          if (changes) changes.disconnect();
        }
      };
    };
  }

  function setup(head, kind) {
    if (head.classList.contains('has-paint') || document.documentElement.classList.contains('perf')) return;
    head.classList.add('has-paint');
    var isSignature = head.classList.contains('ink-signature');
    var seed = isSignature ? visitSeed ^ 0x51ac5e : visitSeed;
    var c, ctx, readingPrint = false, libraryUnavailable = false;
    var W = 0, H = 0, S, marks, raf = 0, seen = !('IntersectionObserver' in window), last = 0;
    var finished = false, dirty = true, measure = true;
    var elapsed = 0, previousFrame = 0;
    var motion = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)');
    var still = reduced;
    var ink, colours;
    function readPalette() {
      var styles = getComputedStyle(head);
      ink = styles.color || '#F3F1F6';
      colours = ['--link', '--violet-text', '--coral'].map(function (key) {
        return styles.getPropertyValue(key).trim() || ink;
      });
      colours.push(ink);
    }
    function prepare() {
      var library = widePaper.matches && window.AvistryLargePrints;
      if (widePaper.matches && !library && !libraryUnavailable) {
        if (!readingPrint) {
          readingPrint = true;
          import(new URL('large-prints.js?v=030-paper-3', document.baseURI).href).then(function () {
            libraryUnavailable = !window.AvistryLargePrints;
          }, function () { libraryUnavailable = true; }).then(function () { readingPrint = false; schedule(); });
        }
        return false;
      }
      c = document.createElement('canvas');
      ctx = c.getContext('2d');
      if (!ctx) return false;
      c.className = 'paint-fx';
      c.setAttribute('aria-hidden', 'true');
      head.appendChild(c);
      if (library) {
        var choice = pickPrint(library, seed, isSignature);
        kind = choice.name;
        head.dataset.inkTier = choice.tier;
        if (choice.newPrint) marks = library.build(kind, seed);
      }
      if (!marks && kind !== 'tools' && kind !== 'tree') marks = print(kind, seed);
      head.dataset.ink = kind;
      readPalette();
      return true;
    }

    function size() {
      var w = head.clientWidth, h = head.clientHeight, dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      var pixelsW = Math.max(1, Math.round(w * dpr)), pixelsH = Math.max(1, Math.round(h * dpr));
      measure = false;
      if (W === w && H === h && c.width === pixelsW && c.height === pixelsH) return;
      W = w; H = h;
      c.width = pixelsW; c.height = pixelsH;
      if (!w || !h) return;
      ctx.setTransform(pixelsW / W, 0, 0, pixelsH / H, 0, 0);
      if (!marks) S = scene(seed, W, H);
      dirty = true;
    }

    function draw(t) {
      ctx.clearRect(0, 0, W, H);
      if (!W || !H) return;
      var fade = 1;
      ctx.globalAlpha = fade;
      ctx.lineJoin = ctx.lineCap = 'round';
      function layer(fn) {
        ctx.save();
        fn();
        ctx.restore();
      }
      if (marks) {
        var scale = Math.min(W / 503, H / 180);
        ctx.save();
        ctx.translate(W / 2, H / 2);
        ctx.scale(scale, scale);
        ctx.translate(-250, -90);
        marks.forEach(function (m) {
          if (scale < .48 && m.fine) return;
          var k = span(t / FINISH, m.start, Math.min(1, m.start + (m.weight ? .46 : .21)));
          if (!k) return;
          ctx.save();
          ctx.translate(m.x, m.y);
          ctx.rotate(m.angle);
          ctx.scale(m.scale, m.scale);
          ctx.globalAlpha = m.alpha * (m.weight ? 1 : k);
          if (m.weight) {
            ctx.strokeStyle = colours[m.ink];
            ctx.lineWidth = Math.max(m.weight, .72 / (scale * m.scale));
            if (k < 1) ctx.setLineDash([Math.max(.01, m.length * k), m.length + 4]);
            ctx.stroke(m.path);
          } else {
            ctx.fillStyle = colours[m.ink];
            ctx.fill(m.path);
          }
          ctx.restore();
        });
        ctx.restore();
        return;
      }
      var matched = span(t, 6.1, 7.2);
      var li = ['original', 'crimson', 'frost', 'venom'].indexOf(document.documentElement.getAttribute('data-tint'));
      var look = li > 0 ? LOOKS[li] : null, A = look ? look[0] : CORAL, B = look ? look[1] : VIOLET;
      var CO = look ? A : CORAL, VI = look ? B : VIOLET, CY = look ? mix(A, [255, 255, 255], 0.45) : CYAN;
      var isl = S.isl, tree = kind === 'tree';
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
      layer(function () {
        var k = span(t, 1.5, 2.8);
        if (!k || tree) return;
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
      layer(function () {
        var k = span(t, 3.7, 5);
        if (!k || tree) return;
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
      layer(function () {
        var k = span(t, 4.5, 5.9);
        if (!k) return;
        ctx.fillStyle = rgba(CY, 0.45);
        for (var i = 0; i < S.spray.length && S.spray[i][3] <= k; i++) {
          var p = S.spray[i];
          ctx.fillRect(p[0], p[1], p[2], p[2]);
        }
      });
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
      layer(function () {
        var k = span(t, 0, 1.1);
        ctx.save();
        ctx.setLineDash([5, 4]);
        ctx.lineDashOffset = -t * 18;
        ctx.lineWidth = 1.5;
        if (!tree) isl.forEach(function (p, i) {
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

    function frame(now) {
      raf = 0;
      if (!head.isConnected || !seen || document.hidden || (!isSignature && !widePaper.matches) || document.documentElement.classList.contains('perf')) return;
      if (!c && !prepare()) return;
      if (!ctx) return;
      if (measure) size();
      if (!finished && previousFrame) elapsed += Math.min(80, now - previousFrame);
      previousFrame = now;
      var t = still || finished ? FINISH : Math.min(FINISH, .9 + elapsed / 1000 * SPEED);
      if (finished && !dirty) return;
      if (!dirty && t < FINISH && now - last < 1000 / 30) { schedule(); return; }
      last = now;
      draw(t);
      dirty = false;
      finished = t === FINISH;
      if (!finished) schedule();
    }
    function schedule() {
      if (!raf && seen && head.isConnected && !document.hidden && (isSignature || widePaper.matches) && !document.documentElement.classList.contains('perf')) raf = requestAnimationFrame(frame);
    }

    function repaint() {
      dirty = true;
      if (ctx) readPalette();
      schedule();
    }
    var unwatch, paperSize, paperTheme;
    if ('IntersectionObserver' in window) {
      if (!watchPrint) watchPrint = readingArea();
      unwatch = watchPrint(head, function (visible) {
        if (seen === visible) return;
        seen = visible;
        previousFrame = 0;
        schedule();
      });
    }
    if ('MutationObserver' in window) {
      paperTheme = new MutationObserver(function (records) {
        if (!head.isConnected) {
          seen = false;
          cancelAnimationFrame(raf);
          if (unwatch) unwatch();
          if (paperSize) paperSize.disconnect();
          paperTheme.disconnect();
        } else if (records.some(function (record) { return record.type === 'attributes'; })) repaint();
      });
      paperTheme.observe(document.documentElement, {attributes: true, attributeFilter: ['data-theme']});
      paperTheme.observe(document.body, {childList: true});
    }
    document.addEventListener('visibilitychange', function () { previousFrame = 0; schedule(); });
    window.addEventListener('ats-tint', repaint);
    if (motion && motion.addEventListener) motion.addEventListener('change', function () {
      still = motion.matches || document.documentElement.classList.contains('perf');
      repaint();
    });
    function resized() {
      measure = true;
      if (!isSignature && !widePaper.matches && c) {
        W = H = 0;
        c.width = c.height = 1;
        previousFrame = 0;
      }
      schedule();
    }
    if ('ResizeObserver' in window) {
      paperSize = new ResizeObserver(resized);
      paperSize.observe(head);
    }
    else window.addEventListener('resize', resized);
    schedule();
  }

  function mountPrint(host, className, kind) {
    if (!host || document.documentElement.classList.contains('perf')) return;
    var panel = document.createElement('div');
    panel.className = 'ink-panel ' + className;
    panel.setAttribute('aria-hidden', 'true');
    host.appendChild(panel);
    setup(panel, kind);
  }

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
    document.querySelectorAll('main h2, .studio-map-step > header > h3, .page-head h1, .pg-hero h1, #studio-title').forEach(function (h) {
      if (h.id === 'install-title' || h.closest('.tdock, .community, .inst-card') || h.classList.contains('sr-only') || / bm-\d/.test(' ' + h.className)) return;
      h.classList.add('bm', 'bm-' + (n++ % 6 + 1));
      if (io && PAGE !== 'features') { h.classList.add('bm-io'); io.observe(h); }
    });
  }

  var DOODLES = [
    '<path pathLength="1" d="M30 110C10 60 60 10 110 30S170 120 110 140 30 130 40 90 100 50 130 80"/>',
    '<path pathLength="1" d="M85 85m0 0c8-6 20 0 18 12s-22 22-36 12-12-40 10-48 50 6 52 34-24 54-58 50-52-38-44-70 44-54 78-44"/>',
    '<path pathLength="1" d="M85 20L98 68 148 72 108 100 122 148 85 120 48 148 62 100 22 72 72 68Z"/><circle cx="150" cy="30" r="5"/><circle cx="25" cy="140" r="4"/>',
    '<path pathLength="1" d="M10 60L40 30 70 60 100 30 130 60 160 30M10 110L40 80 70 110 100 80 130 110 160 80"/>',
    '<path pathLength="1" d="M5 90C30 40 55 140 85 90S140 40 165 90"/><circle cx="40" cy="140" r="5"/><circle cx="62" cy="140" r="5"/><circle cx="84" cy="140" r="5"/>'
  ];
  var INKS = [['var(--cyan)', 'var(--coral)'], ['var(--coral)', 'var(--violet)'], ['var(--violet)', 'var(--cyan)']];
  function doodleNode(markup, ink, className) {
    var d = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    d.setAttribute('class', className);
    d.setAttribute('viewBox', '0 0 170 170');
    d.setAttribute('aria-hidden', 'true');
    d.setAttribute('focusable', 'false');
    d.innerHTML = markup;
    d.querySelectorAll('path').forEach(function (p) { p.style.stroke = ink[0]; p.setAttribute('stroke-width', '3'); });
    d.querySelectorAll('circle').forEach(function (c) { c.setAttribute('fill', ink[1]); c.setAttribute('style', 'fill:' + ink[1]); });
    if (io && PAGE !== 'features') { d.classList.add('dd-io'); io.observe(d); }
    return d;
  }

  function doodles() {
    var k = 0;
    document.querySelectorAll('main > .sec, main > section.sec').forEach(function (sec) {
      if (sec.querySelector(':scope > .doodle') || sec.classList.contains('stage-sec') || sec.offsetHeight < 320) return;
      var d = doodleNode(DOODLES[k % DOODLES.length], INKS[k % 3], 'doodle ' + (k % 2 ? 'is-l' : 'is-r'));
      sec.classList.add('has-doodle');
      sec.insertBefore(d, sec.firstChild);
      k++;
    });
  }

  function studioArt() {
    var backdrop = document.querySelector('[data-studio-paint]');
    if (!backdrop || document.documentElement.classList.contains('perf')) return;
    setup(backdrop, cover);
    var shapes = { loop: DOODLES[0], star: DOODLES[2], zigzag: DOODLES[3], wave: DOODLES[4] };
    document.querySelectorAll('[data-studio-doodle]').forEach(function (head, index) {
      var mark = shapes[head.dataset.studioDoodle];
      if (!mark) return;
      var drawing = doodleNode(mark, INKS[index % INKS.length], 'doodle studio-margin-art');
      if (head.dataset.studioDoodle === 'wave') drawing.setAttribute('viewBox', '-4 58 178 96');
      if (head.dataset.studioDoodle === 'zigzag') drawing.setAttribute('viewBox', '0 16 170 108');
      head.appendChild(drawing);
    });
    if (reduced || !('IntersectionObserver' in window)) return;

    var pending = new Set(document.querySelectorAll('.feature-studio .bm, .studio-margin-art, .studio-sticker'));
    var observer, previousMargin;
    var header = document.querySelector('.hdr'), nav = document.querySelector('.studio-nav');
    pending.forEach(function (node) {
      node.classList.add(node.classList.contains('bm') ? 'bm-io' : node.classList.contains('doodle') ? 'dd-io' : 'studio-sticker-wait');
    });
    function observeArt() {
      var top = header.offsetHeight + nav.offsetHeight;
      var margin = -top + 'px 0px -' + Math.round(window.innerHeight * .23) + 'px 0px';
      if (margin === previousMargin) return;
      previousMargin = margin;
      if (observer) observer.disconnect();
      observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (!entry.isIntersecting || entry.intersectionRatio < .45) return;
          var node = entry.target;
          node.classList.add(node.classList.contains('bm') ? 'bm-in' : node.classList.contains('doodle') ? 'dd-in' : 'is-in');
          pending.delete(node);
          observer.unobserve(node);
        });
      }, { rootMargin: margin, threshold: .45 });
      pending.forEach(function (node) { observer.observe(node); });
    }
    observeArt();
    window.addEventListener('resize', observeArt, { passive: true });
    document.addEventListener('ats:lang', observeArt);
  }

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
    var word = inner.querySelector('.foot-mark');
    if (!document.documentElement.classList.contains('perf')) {
      var signing = document.createElement('div');
      signing.className = 'foot-signature';
      inner.insertBefore(signing, word);
      signing.appendChild(word);
      mountPrint(signing, 'ink-signature', signature);
    }
  }

  function siteAccents() {
    var effects = window.AvistryInkEffects;
    var header = document.querySelector('.hdr');
    var motion = matchMedia('(prefers-reduced-motion: reduce)');
    if (!effects || !header || document.documentElement.classList.contains('perf')) return;
    var selector = '.hdr a, .hdr button, .sheet-nav a, .jump a, .btn, .more-card, .next-card, .foot-links a, .foot .tools button, .studio-switch button, .stack-materials button, .looks button';
    var canvas, context, host, frame, hoverTimer, pending, playingFor, lastTime = -Infinity;
    var regular = [], rare = [], palette;
    var entrancePending = true;

    function headerSlot(target) {
      var width = header.clientWidth, height = header.clientHeight;
      var r = target.getBoundingClientRect(), center = r.left + r.width / 2;
      var occupied = Array.from(header.querySelectorAll('.brand, .is-big .hdr-word, .nav a, .hdr-looks, .hdr-lang, .hdr-icon, .menu-btn'))
        .map(function (node) { return node.getBoundingClientRect(); })
        .filter(function (box) { return box.width && box.height; })
        .sort(function (a, b) { return a.left - b.left; });
      var left = 8, best, bestScore = -Infinity;
      occupied.forEach(function (box, index) {
        var space = box.left - 8 - left;
        if (index && space >= 80) {
          var w = Math.min(210, space), x = Math.max(left, Math.min(center - w / 2, box.left - 8 - w));
          var score = w - Math.abs(center - x - w / 2) * .05;
          if (score > bestScore) { best = { x: x, y: (height - w * .18) / 2, w: w, h: w * .18, center: true }; bestScore = score; }
        }
        left = Math.max(left, box.right + 8);
      });
      if (best) return best;
      var w = Math.min(154, width - 16);
      return { x: Math.max(8, Math.min(width - w - 8, center - w / 2)), y: height - 18, w: w, h: 17 };
    }

    function slotFor(target) {
      if (host === header) return headerSlot(target);
      var width = host.clientWidth, height = host.clientHeight;
      if (host.classList.contains('bm')) {
        var words = document.createRange();
        words.selectNodeContents(host);
        var room = Math.floor(host.getBoundingClientRect().bottom - words.getBoundingClientRect().bottom);
        if (room < 8) return;
        var stroke = getComputedStyle(host, '::after');
        var start = parseFloat(stroke.width) + 6;
        var x = width - start >= 85 ? start : 0;
        var w = Math.min(170, width - x, room * 9);
        return { x: x, y: height - Math.min(room, 27), w: w, h: Math.min(room, 27) };
      }
      var w = Math.min(150, width - 4);
      var h = Math.min(19, w * .18, height / 3);
      return { x: (width - w) / 2, y: height - h - 1, w: w, h: h };
    }

    function stop() {
      cancelAnimationFrame(frame);
      if (canvas) canvas.remove();
      if (host) host.classList.remove('ink-event-host', 'ink-event-static');
      host = playingFor = null;
    }

    function updatePalette() {
      if (!host) return;
      var styles = getComputedStyle(host);
      palette = ['--link', '--violet-text', '--coral'].map(function (key) {
        return styles.getPropertyValue(key).trim() || styles.color;
      });
    }

    function play(target, passive) {
      if (!target || !target.isConnected || document.hidden || motion.matches || document.documentElement.classList.contains('perf')) return;
      var now = performance.now();
      if (now - lastTime < (passive ? 1700 : 480) || (host && (passive || playingFor === target))) return;
      stop();
      host = target.closest('.hdr') || target;
      var box = slotFor(target);
      if (!box || box.w < 24 || box.h < 5) { host = null; return; }
      if (!canvas) {
        canvas = document.createElement('canvas');
        canvas.className = 'ink-event';
        canvas.setAttribute('aria-hidden', 'true');
        context = canvas.getContext('2d');
      }
      if (!context) { host = null; return; }
      updatePalette();
      var special = Math.random() < 1 / 64;
      var bag = special ? rare : regular;
      if (!bag.length) bag.push.apply(bag, special ? effects.rare : effects.regular);
      var draw = bag.splice(Math.floor(Math.random() * bag.length), 1)[0];
      var duration = special ? 1750 : 1150, dpr = Math.min(devicePixelRatio || 1, 1.5);
      var strength = host === header || host.classList.contains('bm') ? .95 : .62;
      if (getComputedStyle(host).position === 'static') host.classList.add('ink-event-static');
      host.classList.add('ink-event-host');
      canvas.dataset.effect = draw.name;
      canvas.width = Math.ceil(box.w * dpr);
      canvas.height = Math.ceil(box.h * dpr);
      Object.assign(canvas.style, { left: box.x + 'px', top: box.center ? '50%' : box.y + 'px', transform: box.center ? 'translateY(-50%)' : '', width: box.w + 'px', height: box.h + 'px', opacity: '0' });
      host.appendChild(canvas);
      playingFor = target;
      lastTime = now;
      var lastDraw = -Infinity;
      function render(time) {
        var t = (time - now) / duration;
        if (t >= 1 || !canvas.isConnected || motion.matches || document.hidden || document.documentElement.classList.contains('perf')) { stop(); return; }
        if (time - lastDraw >= 30) {
          lastDraw = time;
          context.setTransform(canvas.width / 200, 0, 0, canvas.height / 36, 0, 0);
          context.clearRect(0, 0, 200, 36);
          context.save();
          draw(context, Math.max(0, t), palette);
          context.restore();
          canvas.style.opacity = String(strength * Math.min(1, t * 10, (1 - t) * 6));
        }
        frame = requestAnimationFrame(render);
      }
      frame = requestAnimationFrame(render);
    }

    document.addEventListener('pointerover', function (event) {
      if (event.pointerType === 'touch') return;
      var target = event.target.closest(selector);
      if (!target || target.contains(event.relatedTarget)) return;
      clearTimeout(hoverTimer);
      pending = target;
      hoverTimer = setTimeout(function () { if (target.matches(':hover')) play(target, false); }, 170);
    });
    document.addEventListener('pointerout', function (event) {
      if (pending && pending.contains(event.target) && !pending.contains(event.relatedTarget)) {
        clearTimeout(hoverTimer);
        pending = null;
      }
    });
    document.addEventListener('focusin', function (event) {
      if (event.target.matches(':focus-visible')) play(event.target.closest(selector), false);
    });
    document.addEventListener('click', function (event) { play(event.target.closest(selector), false); });
    document.addEventListener('visibilitychange', function () { if (document.hidden) stop(); });
    window.addEventListener('ats-tint', updatePalette);
    window.addEventListener('resize', stop, { passive: true });
    if ('IntersectionObserver' in window) {
      var sections = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting && entry.intersectionRatio >= .75) {
            sections.unobserve(entry.target);
            if (!entrancePending) play(entry.target, true);
          }
        });
      }, { rootMargin: '-85px 0px -48px', threshold: .75 });
      document.querySelectorAll('main h2.bm').forEach(function (title) { if (!title.closest('.studio-opening-copy')) sections.observe(title); });
    }
    var brand = header.querySelector('.brand');
    setTimeout(function () {
      entrancePending = false;
      if (lastTime === -Infinity) play(brand, true);
    }, header.classList.contains('is-intro') ? 950 : 260);
  }

  function start() {
    document.querySelectorAll('.page-head > .wrap, .pg-hero > .wrap').forEach(function (wrap) {
      mountPrint(wrap, 'ink-cover', cover);
    });
    marks();
    doodles();
    studioArt();
    footer();
    siteAccents();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
