(function () {
  'use strict';
  if (typeof Path2D === 'undefined' || document.documentElement.classList.contains('perf')) return;
  var tau = 2 * Math.PI;
  function path(d) {
    return new Path2D(d);
  }
  function phase(progress, start, end) {
    var u = Math.max(0, Math.min(1, (progress - start) / (end - start)));
    return u * u * (3 - 2 * u);
  }
  function line(ctx, shape, colour, width) {
    ctx.strokeStyle = colour;
    ctx.lineWidth = width;
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.stroke(shape);
  }
  function trace(ctx, shape, length, progress, colour, width) {
    if (progress <= 0) return;
    ctx.save();
    ctx.setLineDash([length, length]);
    ctx.lineDashOffset = length * (1 - progress);
    line(ctx, shape, colour, width);
    ctx.restore();
  }
  function stamp(ctx, shape, colour, x, y, size, angle, width) {
    if (size <= 0) return;
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(angle);
    ctx.scale(size, size);
    if (width) line(ctx, shape, colour, width);
    else {
      ctx.fillStyle = colour;
      ctx.fill(shape);
    }
    ctx.restore();
  }
  function oval(ctx, colour, x, y, rx, ry, angle, width) {
    if (rx <= 0 || ry <= 0) return;
    ctx.beginPath();
    ctx.ellipse(x, y, rx, ry, angle, 0, tau);
    if (width) {
      ctx.lineWidth = width;
      ctx.strokeStyle = colour;
      ctx.stroke();
    } else {
      ctx.fillStyle = colour;
      ctx.fill();
    }
  }

  var star = path('M0 -8 Q1 -1 7 0 Q1 1 0 8 Q-1 1 -7 0 Q-1 -1 0 -8Z');
  var leaf = path('M0 0 Q3 -8 15 -7 Q10 1 0 0Z');
  var nib = path('M-8 1 L5 -2 Q8 -2 8 0 L7 1Z');

  var hairs = [
    'M5 22C43 28 53 3 91 11S150 31 191 10',
    'M8 24C43 30 54 6 91 13S149 33 187 14',
    'M10 26C47 30 57 9 91 15S147 34 179 19',
    'M12 19C44 25 57 2 91 8S148 27 193 8',
    'M23 16C49 20 63 1 93 6S148 24 180 11'
  ].map(path);
  function dryBristle(ctx, progress, palette) {
    hairs.forEach(function (hair, i) {
      trace(
        ctx,
        hair,
        201,
        phase(progress, i * 0.028, 0.86 + i * 0.03),
        palette[i === 3 ? 2 : i % 2],
        [3, 2, 0.9, 1.15, 0.85][i]
      );
    });
  }

  function saddleStitch(ctx, progress, palette) {
    var travel = phase(progress, 0, 0.9) * 16;
    for (var i = 0; i < 16; i++) {
      var u = Math.min(1, Math.max(0, travel - i)),
        x = 8 + i * 11.4,
        y = 19 + 5 * Math.sin(i * 0.44);
      if (!u) break;
      oval(ctx, palette[1], x, y, 1.4, 1.4, 0);
      ctx.beginPath();
      ctx.moveTo(x, y);
      ctx.quadraticCurveTo(x + 3.5 * u, y - 5 * u, x + 8.4 * u, y - 3 * u);
      ctx.lineCap = 'round';
      ctx.lineWidth = 2;
      ctx.strokeStyle = palette[0];
      ctx.stroke();
    }
    var pos = Math.min(191, 8 + travel * 11.4),
      lift = Math.sin(travel * Math.PI);
    stamp(
      ctx,
      nib,
      palette[2],
      pos,
      15 + 5 * Math.sin(travel * 0.44) - 4 * Math.abs(lift),
      0.95,
      -0.48 * lift
    );
  }

  var dust = [];
  for (var j = 0; j < 79; j++) {
    var x = 6 + 186 * ((j * 0.61803398875) % 1),
      scatter = ((j * 0.754877666) % 1) - 0.5;
    dust.push([
      x,
      18 + 6 * Math.sin(x * 0.043) + scatter * (8 + 9 * Math.sin(x * 0.018)),
      0.6 + (j % 6) * 0.47
    ]);
  }
  function chalkFan(ctx, progress, palette) {
    var front = phase(progress, 0, 0.93) * 203;
    dust.forEach(function (fleck, i) {
      var k = phase(front, fleck[0], fleck[0] + 8);
      if (k)
        oval(
          ctx,
          palette[i % 7 === 0 ? 2 : i % 2],
          fleck[0],
          fleck[1] + 3 * (1 - k),
          k * fleck[2],
          k * 0.9,
          -0.62
        );
    });
  }

  function halftoneRoll(ctx, progress, palette) {
    for (var row = 0; row < 5; row++) {
      for (var col = 0; col < 23; col++) {
        var k = phase(progress, (col + row * 0.65) / 32, (col + row * 0.65 + 4) / 32);
        var weight = (1 + Math.cos(col * 0.44 + row * 0.52)) * 0.92 + 0.45;
        var radius = weight * k * (1 - Math.abs(row - 2) * 0.17);
        oval(
          ctx,
          palette[row === 2 && col % 7 === 0 ? 2 : col < 12 ? 0 : 1],
          7 + col * 8.25 + (row % 2) * 3,
          6 + row * 6,
          radius,
          radius,
          0
        );
      }
    }
  }

  var twillUp = path('M-8 6L0 -7 9 -8 1 5Z'),
    twillDown = path('M-7 -7L0 4 8 7 1 -6Z');
  function wovenTape(ctx, progress, palette) {
    for (var i = 0; i < 11; i++) {
      var start = i * 0.059,
        x = 11 + i * 17.3,
        y = 18 + Math.sin(i * 0.55) * 3;
      stamp(ctx, twillDown, palette[1], x + 6, y, phase(progress, start, start + 0.23), -0.11);
      stamp(
        ctx,
        twillUp,
        palette[i === 7 ? 2 : 0],
        x,
        y + 1,
        phase(progress, start + 0.07, start + 0.31),
        -0.11
      );
    }
  }

  var curl = path(
    'M5 23C22 29 27 6 46 8C70 10 54 32 43 25S47 6 73 16S108 31 114 17S97 5 99 18S132 36 150 17Q163 3 193 11'
  );
  var curlEcho = path('M8 27Q21 31 30 21M66 18C90 28 102 32 111 26M138 27Q146 25 154 18');
  function ribbonCurl(ctx, progress, palette) {
    trace(ctx, curl, 311, phase(progress, 0, 0.9), palette[0], 2.6);
    trace(ctx, curlEcho, 53, phase(progress, 0.43, 0.95), palette[1], 1.25);
    stamp(ctx, star, palette[2], 192, 11, 0.59 * phase(progress, 0.78, 1), 0.13);
  }

  var splitLeft = path('M100 18C76 22 71 8 53 12S26 26 8 21');
  var splitRight = path('M100 18C120 13 129 28 149 19S176 7 192 11');
  var forks = path('M62 12Q49 5 39 7M31 20Q24 29 17 29M142 21Q153 30 162 27M165 13Q168 6 180 5');
  var blot = path(
    'M-8 -1Q-9 -5 -5 -6Q-2 -9 1 -5Q6 -8 6 -3Q12 -1 7 3Q8 8 3 6Q0 10 -3 6Q-9 8 -8 3Q-13 1 -8 -1Z'
  );
  function splitInk(ctx, progress, palette) {
    stamp(ctx, blot, palette[0], 100, 18, 1.3 * phase(progress, 0, 0.21), progress * 0.33);
    trace(ctx, splitLeft, 103, phase(progress, 0.09, 0.84), palette[0], 2.2);
    trace(ctx, splitRight, 104, phase(progress, 0.15, 0.9), palette[1], 2.7);
    trace(ctx, forks, 27, phase(progress, 0.47, 1), palette[2], 1.2);
  }

  var twig = path('M5 27C43 28 67 18 96 19S149 9 193 10');
  var sprigs = path('M40 24Q39 15 31 11M76 20Q81 10 89 8M129 16Q136 22 144 24M165 11Q170 6 176 5');
  var petal = path('M0 0C-5 -2 -6 -7 -2 -7L0 -5Q5 -11 5 -6Q4 -2 0 0Z');
  function blossomSprig(ctx, progress, palette) {
    trace(ctx, twig, 195, phase(progress, 0, 0.67), palette[0], 2);
    trace(ctx, sprigs, 18, phase(progress, 0.24, 0.65), palette[0], 1.15);
    [
      [46, 24, -0.58],
      [69, 21, 2.5],
      [113, 18, -0.3],
      [154, 12, 2.7]
    ].forEach(function (b, i) {
      stamp(ctx, leaf, palette[0], b[0], b[1], 0.76 * phase(progress, 0.22 + i * 0.1, 0.51 + i * 0.1), b[2]);
    });
    [
      [30, 10],
      [89, 9],
      [145, 25],
      [177, 8]
    ].forEach(function (b, i) {
      var k = phase(progress, 0.29 + i * 0.1, 0.66 + i * 0.1);
      for (var n = 0; n < 5; n++)
        stamp(ctx, petal, palette[n === 1 ? 2 : 1], b[0], b[1], k * 0.86, (n * tau) / 5 + k * 0.35);
      oval(ctx, palette[2], b[0], b[1], k * 1.7, k * 1.7, 0);
    });
  }

  function waterRipples(ctx, progress, palette) {
    [34, 101, 165].forEach(function (x, i) {
      var start = i * 0.16,
        drop = phase(progress, start, start + 0.22),
        y = 19 + (i % 2) * 4;
      if (drop > 0 && drop < 1) oval(ctx, palette[2], x, 4 + (y - 5) * drop, 1.6, 2.6, 0);
      for (var r = 0; r < 3; r++) {
        var k = phase(progress, start + 0.19 + r * 0.075, start + 0.49 + r * 0.075);
        oval(ctx, palette[r === 1 ? 1 : 0], x, y, k * (28 - r * 8), k * (7 - r * 2), -0.075, 1.15);
      }
    });
  }

  var striker = path('M5 28L42 23 35 27 88 15 79 22 141 11 129 17 161 12');
  function matchStrike(ctx, progress, palette) {
    trace(ctx, striker, 221, phase(progress, 0, 0.68), palette[0], 2);
    var k = phase(progress, 0.5, 0.92);
    for (var i = 0; i < 8; i++) {
      var angle = -2.72 + i * 0.66,
        len = (i % 2 ? 10 : 17) * k;
      var x = 162 + Math.cos(angle) * len,
        y = 15 + Math.sin(angle) * len * 0.61;
      stamp(ctx, nib, palette[i % 2 ? 1 : 2], x, y, k * 0.41, angle);
    }
    stamp(ctx, star, palette[2], 162, 15, k * 0.83, 0.16);
  }

  var pad = path('M-4 0Q-1 -5 1 -3L5 1Q6 4 2 4L-3 4Q-7 3 -4 0Z');
  function pawParade(ctx, progress, palette) {
    for (var i = 0; i < 8; i++) {
      var k = phase(progress, i * 0.097, i * 0.097 + 0.16);
      if (!k) continue;
      ctx.save();
      ctx.translate(12 + 24.5 * i, 18 + Math.sin(i * 0.91) * 5 + (i % 2 ? -3 : 3));
      ctx.rotate(1.42 + 0.38 * Math.cos(i * 0.91));
      ctx.scale(k, k);
      stamp(ctx, pad, palette[i % 2], 0, 0, 1, 0);
      for (var n = 0; n < 4; n++)
        oval(
          ctx,
          palette[i % 2],
          (n - 1.5) * 3.5,
          -4.2 - (n === 1 || n === 2 ? 2.5 : 0),
          1.55,
          2.05,
          (n - 1.5) * 0.27
        );
      ctx.restore();
    }
  }

  var fish = path('M12 -1C12 -7 1 -8 -7 -2L-15 -6Q-12 -1 -15 6L-7 2C1 8 10 6 12 -1Z');
  var fishPatch = path('M-6 -2Q-4 -5 0 -5Q3 -2 -1 -1Q-3 2 -6 -2ZM4 1Q6 -1 8 0Q8 4 4 3Z');
  function koiPond(ctx, progress, palette) {
    var k = phase(progress, 0, 0.95);
    [
      [14 + 91 * k, 11 + 4 * Math.sin(progress * tau), 0.16 * Math.cos(progress * tau)],
      [186 - 68 * k, 25 - 2 * Math.sin(progress * tau), Math.PI - 0.15 * Math.cos(progress * tau)]
    ].forEach(function (p, i) {
      ctx.save();
      ctx.translate(p[0], p[1]);
      ctx.rotate(p[2]);
      stamp(ctx, fish, palette[i ? 1 : 2], 0, 0, 1.1, 0);
      stamp(ctx, fishPatch, palette[0], 0, 0, 1.1, 0);
      oval(ctx, palette[i ? 2 : 1], 9, -1.8, 1.1, 1.1, 0);
      ctx.restore();
      var sign = i ? 1 : -1;
      for (var n = 0; n < 3; n++) {
        oval(
          ctx,
          palette[i],
          p[0] + sign * (22 + 8 * n),
          p[1] + Math.sin(progress * tau + n) * 2,
          2.1 - 0.35 * n,
          1.3,
          0,
          1
        );
      }
    });
  }

  var flightTrail = path('M5 27Q22 12 38 22T71 25Q85 18 102 15');
  var wing = path('M-14 1L-24 -11 7 -3 20 -10 7 1 3 12 -2 4Z');
  var fold = path('M-14 1L-2 4 7 -3ZM-2 4L3 12 4 1Z');
  function paperSwallow(ctx, progress, palette) {
    var k = phase(progress, 0, 0.94);
    ctx.save();
    ctx.setLineDash([3, 4]);
    ctx.beginPath();
    ctx.rect(0, 0, 106 * k, 36);
    ctx.clip();
    line(ctx, flightTrail, palette[1], 1.4);
    ctx.restore();
    ctx.save();
    ctx.translate(29 + 138 * k, 19 - 5 * Math.sin(k * Math.PI));
    ctx.rotate(-0.13 + 0.11 * k);
    ctx.scale(1, 0.8 + 0.2 * Math.cos(k * tau * 1.5));
    stamp(ctx, wing, palette[0], 0, 0, 1, 0);
    stamp(ctx, fold, palette[2], 0, 0, 1, 0);
    ctx.restore();
  }

  var flare = path('M-22 0Q-4 -2 2 -7Q12 -5 9 2Q4 9 -2 5Z');
  var cometTail = path('M5 30Q74 24 115 15T178 11'),
    cometEcho = path('M24 32Q68 29 95 21T151 15');
  function cometPass(ctx, progress, palette) {
    var k = phase(progress, 0, 0.94),
      x = 18 + k * 157,
      y = 26 - 15 * Math.sin(k * 1.62);
    for (var i = 0; i < 5; i++)
      stamp(
        ctx,
        star,
        palette[i % 2],
        14 + i * 37,
        8 + 20 * (i % 2),
        0.28 * phase(progress, i * 0.09, i * 0.09 + 0.25),
        i * 0.18
      );
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, x, 36);
    ctx.clip();
    line(ctx, cometTail, palette[1], 2);
    line(ctx, cometEcho, palette[0], 1);
    ctx.restore();
    stamp(ctx, flare, palette[0], x, y, 1.14, -0.16);
    stamp(ctx, star, palette[2], x + 4, y, 0.72, -0.12);
  }

  var ridge = path('M4 31L29 18 38 23 61 9 81 25 99 15 112 24 127 17 143 26 158 13 181 26 195 22');
  var snow = path('M48 18L61 9 70 16 63 14 60 17 57 14ZM148 21L158 13 168 19 161 17 158 19 156 17Z');
  var aurora = path('M3 12Q33 0 60 5T111 8Q141 -1 183 4Q159 5 136 11T78 12Q42 4 3 12Z');
  var crescent = path('M4 -5A6 6 0 1 0 6 3Q-2 4 4 -5Z');
  function mountainNight(ctx, progress, palette) {
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, 200 * phase(progress, 0.05, 0.78), 36);
    ctx.clip();
    stamp(ctx, aurora, palette[1], 0, 2 * Math.sin(progress * Math.PI), 1, 0);
    ctx.restore();
    trace(ctx, ridge, 230, phase(progress, 0, 0.81), palette[0], 1.9);
    for (var i = 0; i < 3; i++)
      stamp(
        ctx,
        star,
        palette[2],
        18 + i * 45,
        6 + (i % 2) * 3,
        0.27 * phase(progress, 0.44 + i * 0.06, 0.77 + i * 0.06),
        0
      );
    if (progress > 0.5) {
      ctx.save();
      ctx.globalAlpha *= phase(progress, 0.5, 0.85);
      stamp(ctx, snow, palette[2], 0, 0, 1, 0);
      ctx.restore();
    }
    stamp(ctx, crescent, palette[2], 185, 9, phase(progress, 0.57, 0.93) * 0.92, -0.25);
  }

  var stars = [
    [8, 25],
    [27, 21],
    [51, 26],
    [66, 17],
    [86, 17],
    [96, 7],
    [113, 16],
    [129, 16],
    [142, 7],
    [147, 22],
    [163, 26],
    [181, 20],
    [193, 22]
  ];
  function starChart(ctx, progress, palette) {
    for (var i = 0; i < stars.length; i++) {
      var p = stars[i],
        k = phase(progress, i * 0.06, i * 0.06 + 0.14);
      if (!k) break;
      if (i) {
        var prev = stars[i - 1];
        ctx.beginPath();
        ctx.moveTo(prev[0], prev[1]);
        ctx.lineTo(prev[0] + (p[0] - prev[0]) * k, prev[1] + (p[1] - prev[1]) * k);
        ctx.strokeStyle = palette[1];
        ctx.lineWidth = 1.2;
        ctx.stroke();
      }
      stamp(ctx, star, palette[i % 4 === 1 ? 2 : 0], p[0], p[1], k * (i % 4 === 1 ? 0.63 : 0.33), 0.12);
    }
  }

  var cassette = path(
    'M-16 -12H16Q20 -12 20 -8V8Q20 12 16 12H-16Q-20 12 -20 8V-8Q-20 -12 -16 -12ZM-13 10L-10 5H10L13 10'
  );
  var windowSlot = path('M-10 -7H10A5 5 0 0 1 10 3H-10A5 5 0 0 1 -10 -7Z');
  var spool = path('M0 -3V-1M2.6 1.5L1 .6M-2.6 1.5L-1 .6');
  var tape = path('M46 25C60 29 65 8 76 9C90 11 73 32 66 23S91 6 107 16S134 32 148 19S162 8 185 12');
  function cassetteSpool(ctx, progress, palette) {
    stamp(ctx, cassette, palette[0], 26, 18, 1, 0, 1.6);
    stamp(ctx, windowSlot, palette[1], 26, 18, 1, 0, 1.4);
    for (var i = 0; i < 2; i++) {
      oval(ctx, palette[2], 16 + i * 20, 16, 3.6, 3.6, 0, 1);
      stamp(ctx, spool, palette[2], 16 + i * 20, 16, 1, progress * tau * 1.4, 1.1);
    }
    trace(ctx, tape, 202, phase(progress, 0, 0.93), palette[1], 2.1);
    stamp(ctx, star, palette[2], 187, 12, 0.5 * phase(progress, 0.78, 1), 0.14);
  }

  window.AvistryInkEffects = {
    regular: [
      dryBristle,
      saddleStitch,
      chalkFan,
      halftoneRoll,
      wovenTape,
      ribbonCurl,
      splitInk,
      blossomSprig,
      waterRipples,
      matchStrike
    ],
    rare: [pawParade, koiPond, paperSwallow, cometPass, mountainNight, starChart, cassetteSpool]
  };
})();
