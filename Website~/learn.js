(function () {
  'use strict';
  const find = selector => document.querySelector(selector);
  const t = key => window.ATS.t(key);
  const cache = new Map();
  const skills = find('#sk-root');
  const glossary = find('#gl-list');
  const routes = find('#workshop-routes');
  const search = find('#sk-search') || find('#gl-search');
  const topic = find('#gl-topic');
  let model, chapter = 1, order = 'az', generation = 0;

  function element(tag, cls, text) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  }
  function link(text, href, cls) {
    const node = element('a', cls, text);
    node.href = href;
    return node;
  }
  function localPage(page, hash) {
    const url = new URL(location.href);
    if (page) url.pathname = url.pathname.replace(/[^/]*$/, page);
    url.hash = hash || '';
    return url.pathname + url.search + url.hash;
  }
  async function load(lang) {
    if (cache.has(lang)) return cache.get(lang);
    const response = await fetch('data/glossary.' + encodeURIComponent(lang) + '.json?v=030-workshop-1');
    if (!response.ok) throw new Error('catalog unavailable');
    const data = await response.json();
    const byId = new Map(data.entries.map(e => [e.id, e]));
    const tiers = new Map(data.tiers.map(tier => [tier.n, tier]));
    const haystacks = new Map(data.entries.map(e => [e.id,
      [e.id.replace(/-/g, ' '), e.title, e.tip, e.definition, ...(e.aliases || []), ...(e.alsoCalled || [])]
        .join(' ').normalize('NFKC').toLocaleLowerCase(lang)]));
    const catalog = { entries: data.entries, tiers, byId, haystacks, lang };
    cache.set(lang, catalog);
    return catalog;
  }
  function matches(entry, query) {
    return query.every(word => model.haystacks.get(entry.id).includes(word));
  }
  function words() {
    return search.value.normalize('NFKC').trim().toLocaleLowerCase(model.lang).split(/\s+/).filter(Boolean);
  }
  function field(parent, key, value) {
    if (!value) return;
    parent.append(element('dt', '', t(key)), element('dd', '', value));
  }
  function related(parent, key, ids, isSkill) {
    const group = element('div', 'lesson-related');
    for (const id of ids || []) {
      const other = model.byId.get(id);
      if (other) group.append(link(other.title, localPage('', (isSkill ? 'sk-' : '') + id)));
    }
    if (group.childNodes.length) {
      group.prepend(element('strong', '', t(key)));
      parent.append(group);
    }
  }
  function manual(parent, entry) {
    if (!entry.docsUrl) return;
    try {
      const url = new URL(entry.docsUrl);
      if (url.protocol === 'https:' && !url.username && !url.password) {
        parent.append(link(t('lr.manual'), url.href, 'lesson-manual'));
      }
    } catch {  }
  }
  function entryCard(entry, isSkill, expanded) {
    const card = element('details', 'term lesson-card');
    card.id = (isSkill ? 'sk-' : '') + entry.id;
    const summary = element('summary');
    const label = element('span', 't-main');
    label.append(element('span', 't-name', entry.title), element('span', 't-tip', entry.tip));
    const tag = isSkill ? t(entry.bonus ? 'sk.bonus' : 'sk.lv.' + entry.level) : model.tiers.get(entry.tier).name;
    summary.append(label, element('span', 't-tier', tag));
    card.append(summary);

    function addBody() {
      if (card.children.length > 1) return;
      const body = element('div', 't-body');
      if (entry.definition) body.append(element('p', 't-def', entry.definition));
      const fields = element('dl', 't-dl');
      if (isSkill) {
        field(fields, 'gl.f.example', entry.example);
        field(fields, 'gl.f.try', entry.tryIt);
        field(fields, 'gl.f.where', entry.whereInUnity);
        field(fields, 'gl.f.watch', entry.watchOut);
      } else {
        for (const [name, key] of [['what','what'], ['when','when'], ['example','example'], ['tryIt','try'], ['watchOut','watch'], ['whereInUnity','where']]) {
          field(fields, 'gl.f.' + key, entry[name]);
        }
        if (entry.alsoCalled?.length) field(fields, 'gl.also', entry.alsoCalled.join(', '));
      }
      body.append(fields);
      related(body, isSkill ? 'sk.after' : 'gl.related', isSkill ? entry.requires : entry.related, isSkill);
      const actions = element('div', 'lesson-actions');
      actions.append(link(t(isSkill ? 'sk.more' : 'lr.practice'), localPage(isSkill ? 'glossary.html' : 'skills.html', (isSkill ? '' : 'sk-') + entry.id)));
      manual(actions, entry);
      body.append(actions);
      card.append(body);
    }
    card.addEventListener('toggle', () => { if (card.open) addBody(); });
    if (expanded) { addBody(); card.open = true; }
    return card;
  }

  function renderSkills(openId) {
    const q = words();
    const current = model.tiers.get(chapter) || model.tiers.values().next().value;
    const found = model.entries.filter(e => q.length ? matches(e, q) : e.tier === current.n);
    for (const anchor of find('#sk-nav').children) {
      if (!q.length && Number(anchor.dataset.chapter) === current.n) anchor.setAttribute('aria-current', 'true');
      else anchor.removeAttribute('aria-current');
    }
    const header = element('header', 'lesson-chapter');
    const h2 = element('h2', 'h-display', q.length ? t('sk.results') : current.name);
    if (!q.length) h2.id = 'tier-' + current.n;
    header.append(h2, element('p', '', q.length ? t('lr.count').replace('{n}', found.length) : current.blurb));
    const cards = found.map(entry => entryCard(entry, true, entry.id === openId));
    if (!cards.length) cards.push(element('p', 'pg-empty', t('gl.empty')));
    skills.replaceChildren(header, ...cards);
    if (!q.length && current.achievement) {
      const footer = element('div', 'lesson-milestone');
      footer.append(element('span', 'sk-medal', current.achievement));
      const next = model.tiers.get(current.n + 1);
      if (next) footer.append(link(next.name + ' →', localPage('', 'tier-' + next.n)));
      skills.append(footer);
    }
    find('#sk-status').textContent = t('lr.count').replace('{n}', found.length);
  }
  function renderGlossary(openId) {
    const q = words();
    const found = model.entries.filter(e => (!topic.value || e.tier === Number(topic.value)) && matches(e, q));
    const collate = new Intl.Collator(model.lang);
    found.sort((a, b) => (order === 'tier' ? a.tier - b.tier : 0) || collate.compare(a.title,b.title));
    const groups = new Map();
    for (const entry of found) {
      const title = order === 'tier' ? model.tiers.get(entry.tier).name : Array.from(entry.title)[0].toLocaleUpperCase(model.lang);
      if (!groups.has(title)) {
        const group = element('section', 'gl-group');
        group.append(element('h2', 'gl-head', title));
        groups.set(title, group);
      }
      groups.get(title).append(entryCard(entry, false, entry.id === openId));
    }
    glossary.replaceChildren(...groups.values());
    find('#gl-count').textContent = window.ATS.fmt(t('gl.count'), { n: found.length, total: model.entries.length });
    find('#gl-empty').hidden = !!found.length;
  }
  function navigate(scroll) {
    if (!model || !search) return;
    const hash = location.hash.slice(1);
    const entry = model.byId.get(skills ? hash.replace(/^sk-/, '') : hash);
    const tier = skills && /^tier-(\d+)$/.exec(hash);
    if (entry || tier) {
      search.value = '';
      if (topic) topic.value = '';
      chapter = entry ? entry.tier : Number(tier[1]);
    } else if (skills && (!hash || hash === 'lessons')) {
      chapter = 1;
    }
    if (skills) renderSkills(entry?.id);
    else renderGlossary(entry?.id);
    if (scroll && (entry || tier)) document.getElementById(hash)?.scrollIntoView({ block: 'start' });
  }

  const pathways = [
    ['paint','media/v4/studio/surface-print-texture.webp',['island','selection-preview','pen-pressure','scene-gradient','edit-draft']],
    ['finish','media/v4/studio/normal-detail-after.webp',['blend-stack','normal-map','material-copy','poiyomi','liltoon']],
    ['upload','media/v4/hero-crimson-saved-16x9-1280.webp',['creator-companion','avatar-descriptor','vrcfury-toggle','build-and-test','vrchat-upload']]
  ];
  function renderRoutes() {
    const cards = pathways.map(([key, src, ids]) => {
      const card = element('article', 'workshop-route route-' + key);
      const img = element('img');
      img.src = src; img.alt = ''; img.loading = 'lazy';
      card.append(img, element('h3', 'h-display', t('lr.route.' + key)));
      const lessons = element('ol');
      for (const id of ids) {
        const entry = model.byId.get(id);
        if (!entry) continue;
        const item = element('li');
        item.append(link(entry.title, localPage('skills.html', 'sk-' + id)));
        lessons.append(item);
      }
      card.append(lessons);
      return card;
    });
    routes.replaceChildren(...cards);
  }
  function renderChapters() {
    const chapters = [...model.tiers.values()];
    if (skills) {
      find('#sk-nav').replaceChildren(...chapters.map(item => {
        const anchor = link(item.name, localPage('', 'tier-' + item.n));
        anchor.dataset.chapter = item.n;
        return anchor;
      }));
    }
    if (topic) {
      const selected = topic.value;
      topic.replaceChildren(new Option(t('gl.any'), ''), ...chapters.map(item => new Option(item.name, item.n)));
      topic.value = selected;
    }
  }
  async function refresh() {
    const seq = ++generation;
    const lang = window.ATS.lang();
    try {
      const catalog = await load(lang).catch(() => load('en'));
      if (seq !== generation) return;
      model = catalog;
      const note = find('#gl-note');
      if (note) note.hidden = lang === model.lang;
      if (routes) renderRoutes();
      else { renderChapters(); navigate(true); }
    } catch {
      if (seq === generation) (routes || skills || glossary).replaceChildren(element('p', 'pg-note', t('lr.load.error')));
    }
  }
  search?.addEventListener('input', () => { if (model) (skills ? renderSkills : renderGlossary)(); });
  topic?.addEventListener('change', () => { if (model) renderGlossary(); });
  for (const mode of ['az', 'tier']) {
    find('#gl-' + mode)?.addEventListener('click', () => {
      order = mode;
      for (const id of ['az', 'tier']) find('#gl-' + id).setAttribute('aria-pressed', String(mode === id));
      if (model) renderGlossary();
    });
  }
  document.addEventListener('click', event => {
    const anchor = event.target.closest('a');
    if (!anchor || !anchor.hash || anchor.pathname !== location.pathname || anchor.hash !== location.hash || !anchor.closest('.chapter-rail, .lesson-related')) return;
    if (!event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey) navigate(true);
  });
  window.addEventListener('hashchange', () => navigate(true));

  const swatches = { original: 'base', texture: 'print', metal: 'metal' };
  document.querySelectorAll('[data-study]').forEach(button => button.addEventListener('click', () => {
    const phase = button.dataset.study;
    find('#study-model').src = 'media/v4/studio/opening/' + phase + '.webp';
    find('#study-map').src = 'media/v4/studio/opening/swatch-' + swatches[phase] + '.webp';
    document.querySelectorAll('[data-study]').forEach(b => b.setAttribute('aria-pressed', String(b === button)));
    const info = find('#study-info');
    info.dataset.i18n = 'lr.study.' + phase;
    info.textContent = t(info.dataset.i18n);
  }));

  window.ATS.ready(() => {
    refresh();
    document.addEventListener('ats:lang', refresh);
  });
})();
