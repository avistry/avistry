/* glossary.html and skills.html: both render from data/glossary.<lang>.json, a copy of
   Editor/Glossary, one file per language the tool has. */
(function () {
  'use strict';

  var $ = function (s, r) { return (r || document).querySelector(s); };
  function T(key, fb) { var v = window.ATS && window.ATS.t(key); return v != null ? v : (fb != null ? fb : key); }
  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; }

  /* languages that have a data file; the others show English without a failing request */
  var HAS_DATA = ['en', 'ja', 'ko', 'zh-CN'];
  var cache = {};
  var model = null;          /* { entries, tiers, byId } for the shown language */
  var order = 'az';

  function load(lang) {
    if (cache[lang]) return Promise.resolve(cache[lang]);
    if (HAS_DATA.indexOf(lang) < 0) return Promise.reject(new Error(lang));
    return fetch('data/glossary.' + lang + '.json').then(function (r) {
      if (!r.ok) throw new Error(lang);
      return r.json();
    }).then(function (d) { cache[lang] = d; return d; });
  }

  function get() {
    var lang = window.ATS.lang();
    return load(lang).catch(function () { return load('en'); }).then(function (d) {
      var m = { entries: d.entries, tiers: d.tiers, byId: {}, lang: d.language };
      d.entries.forEach(function (e) { m.byId[e.id] = e; });
      var note = $('#gl-note');
      if (note) note.hidden = d.language === lang;
      return m;
    });
  }

  function tierOf(m, n) { return m.tiers.filter(function (t) { return t.n === n; })[0]; }

  /* ---------- glossary ---------- */
  var FIELDS = [['what', 'gl.f.what'], ['when', 'gl.f.when'], ['example', 'gl.f.example'], ['tryIt', 'gl.f.try'], ['watchOut', 'gl.f.watch'], ['whereInUnity', 'gl.f.where']];

  function termNode(e, m) {
    var d = el('details', 'term'); d.id = e.id; d.setAttribute('data-id', e.id);
    d._blob = [e.title, e.tip, e.definition, e.alsoCalled.join(' '), e.aliases.join(' ')].join(' ').toLowerCase();
    var s = el('summary');
    var left = el('span', 't-main');
    left.appendChild(el('span', 't-name', e.title));
    left.appendChild(el('span', 't-tip', e.tip));
    s.appendChild(left);
    var tier = tierOf(m, e.tier);
    s.appendChild(el('span', 't-tier', T('gl.tier') + ' ' + e.tier + (tier ? ': ' + tier.name : '')));
    d.appendChild(s);
    var b = el('div', 't-body');
    b.appendChild(el('p', 't-def', e.definition));
    var dl = el('dl', 't-dl');
    FIELDS.forEach(function (f) {
      if (!e[f[0]]) return;
      dl.appendChild(el('dt', null, T(f[1])));
      dl.appendChild(el('dd', null, e[f[0]]));
    });
    b.appendChild(dl);
    if (e.alsoCalled.length) b.appendChild(el('p', 't-also', T('gl.also') + ' ' + e.alsoCalled.join(', ')));
    if (e.related.length) {
      var p = el('p', 't-rel', T('gl.related') + ' ');
      e.related.forEach(function (id, i) {
        var r = m.byId[id]; if (!r) return;
        if (i) p.appendChild(document.createTextNode(', '));
        var a = el('a', null, r.title); a.href = '#' + id;
        a.addEventListener('click', function () { setTimeout(openHash, 0); });
        p.appendChild(a);
      });
      b.appendChild(p);
    }
    d.appendChild(b);
    return d;
  }

  function renderGlossary(m) {
    var list = $('#gl-list');
    var keep = $('noscript', list);
    list.textContent = '';
    if (keep) list.appendChild(keep);
    var q = ($('#gl-search').value || '').trim().toLowerCase();
    var entries = m.entries.slice();
    var groups = [];
    if (order === 'az') {
      entries.sort(function (a, b) { return a.title.localeCompare(b.title, m.lang); });
      var cur = null;
      entries.forEach(function (e) {
        var L = e.title.charAt(0).toUpperCase();
        if (!cur || cur.key !== L) { cur = { key: L, label: L, items: [] }; groups.push(cur); }
        cur.items.push(e);
      });
    } else {
      m.tiers.forEach(function (t) {
        groups.push({ key: 't' + t.n, label: t.n + '. ' + t.name, sub: t.blurb, items: entries.filter(function (e) { return e.tier === t.n; }) });
      });
    }
    var shown = 0;
    groups.forEach(function (g) {
      var sec = el('section', 'gl-group');
      var h = el('h2', 'gl-head', g.label); sec.appendChild(h);
      if (g.sub) sec.appendChild(el('p', 'gl-sub', g.sub));
      var any = 0;
      g.items.forEach(function (e) {
        var n = termNode(e, m);
        var hit = !q || n._blob.indexOf(q) !== -1;
        n.hidden = !hit;
        if (hit) { any++; shown++; }
        sec.appendChild(n);
      });
      sec.hidden = !any;
      list.appendChild(sec);
    });
    $('#gl-count').textContent = window.ATS.fmt(T('gl.count'), { n: shown, total: m.entries.length });
    $('#gl-empty').hidden = shown > 0;
    openHash();
  }

  function openHash() {
    var id = decodeURIComponent((location.hash || '').slice(1));
    if (!id) return;
    var t = document.getElementById(id);
    if (t && t.classList.contains('term')) {
      if (t.hidden) { $('#gl-search').value = ''; if (model) renderGlossary(model); t = document.getElementById(id); }
      t.open = true;
      t.scrollIntoView({ block: 'start' });
    }
  }

  function initGlossary() {
    var input = $('#gl-search');
    function again() { get().then(function (m) { model = m; renderGlossary(m); }); }
    input.addEventListener('input', function () { if (model) renderGlossary(model); });
    $('#gl-az').addEventListener('click', function () { order = 'az'; $('#gl-az').setAttribute('aria-pressed', 'true'); $('#gl-tier').setAttribute('aria-pressed', 'false'); if (model) renderGlossary(model); });
    $('#gl-tier').addEventListener('click', function () { order = 'tier'; $('#gl-az').setAttribute('aria-pressed', 'false'); $('#gl-tier').setAttribute('aria-pressed', 'true'); if (model) renderGlossary(model); });
    window.addEventListener('hashchange', openHash);
    document.addEventListener('ats:lang', again);
  }

  /* ---------- skill tree ---------- */
  function renderSkills(m) {
    var root = $('#sk-root');
    var keep = $('noscript', root);
    root.textContent = '';
    if (keep) root.appendChild(keep);
    m.tiers.forEach(function (t) {
      var sec = el('section', 'sk-tier'); sec.setAttribute('aria-labelledby', 'tier-' + t.n);
      var head = el('div', 'sk-head');
      head.appendChild(el('span', 'sk-num', String(t.n)));
      var txt = el('div', 'sk-headtext');
      var h = el('h2', 'sk-name', t.name); h.id = 'tier-' + t.n;
      txt.appendChild(h);
      txt.appendChild(el('p', 'sk-blurb', t.blurb));
      txt.appendChild(el('p', 'sk-medal', t.achievement));
      head.appendChild(txt);
      sec.appendChild(head);
      var ul = el('ul', 'sk-cards'); ul.setAttribute('role', 'list');
      m.entries.filter(function (e) { return e.tier === t.n; }).forEach(function (e) {
        var li = el('li', 'sk-card'); li.id = 'sk-' + e.id;
        var top = el('div', 'sk-top');
        top.appendChild(el('h3', 'h3', e.title));
        top.appendChild(el('span', 'sk-lv', T('sk.lv.' + e.level)));
        if (e.bonus) top.appendChild(el('span', 'sk-bonus', T('sk.bonus')));
        li.appendChild(top);
        li.appendChild(el('p', 'sk-tip', e.tip));
        if (e.requires.length) {
          var after = el('p', 'sk-after', T('sk.after') + ' ');
          e.requires.forEach(function (id, i) {
            var r = m.byId[id]; if (!r) return;
            if (i) after.appendChild(document.createTextNode(', '));
            var a = el('a', null, r.title); a.href = '#sk-' + id; after.appendChild(a);
          });
          li.appendChild(after);
        }
        if (e.tryIt) { var tp = el('p', 'sk-try'); tp.appendChild(el('strong', null, T('gl.f.try') + ': ')); tp.appendChild(document.createTextNode(e.tryIt)); li.appendChild(tp); }
        var more = el('a', 'sk-more', T('sk.more')); more.href = 'glossary.html#' + e.id;
        li.appendChild(more);
        ul.appendChild(li);
      });
      sec.appendChild(ul);
      root.appendChild(sec);
    });
    var grad = el('section', 'sk-tier sk-grad');
    grad.appendChild(el('p', 'sk-medal sk-medal--big', T('sk.graduate')));
    grad.appendChild(el('p', 'sk-blurb', T('sk.graduate.body')));
    root.appendChild(grad);
    /* the tiers only exist now, so a link like skills.html#tier-4 has to be followed here */
    if (!scrolled) { scrolled = true; goTier(); }
  }
  var scrolled = false;
  function goTier() {
    var t = location.hash && document.getElementById(decodeURIComponent(location.hash.slice(1)));
    if (t && $('#sk-root').contains(t)) t.scrollIntoView({ block: 'start' });
  }

  /* ---------- learn page: the six tiers as one path ---------- */
  function renderPath(m) {
    var root = $('#lr-path');
    root.textContent = '';
    m.tiers.forEach(function (t) {
      var count = m.entries.filter(function (e) { return e.tier === t.n; }).length;
      var li = el('li', 'lr-stop');
      var a = el('a', 'lr-link'); a.href = 'skills.html#tier-' + t.n;
      a.appendChild(el('span', 'sk-num lr-num', String(t.n)));
      var txt = el('span', 'lr-text');
      txt.appendChild(el('span', 'lr-name', t.name));
      txt.appendChild(el('span', 'lr-blurb', t.blurb));
      var meta = el('span', 'lr-meta');
      meta.appendChild(el('span', 'lr-count', T('lr.count', '{n} skills').replace('{n}', count)));
      meta.appendChild(el('span', 'sk-medal lr-medal', t.achievement));
      txt.appendChild(meta);
      a.appendChild(txt);
      li.appendChild(a);
      root.appendChild(li);
    });
  }

  function initSkills() {
    function again() { get().then(renderSkills); }
    document.addEventListener('ats:lang', again);
    window.addEventListener('hashchange', goTier);
  }

  function boot() {
    if (!window.ATS) return;
    if ($('#gl-list')) initGlossary();
    if ($('#sk-root')) initSkills();
    if ($('#lr-path')) document.addEventListener('ats:lang', function () { get().then(renderPath); });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
