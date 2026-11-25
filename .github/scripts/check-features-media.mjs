import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const site = path.resolve(args.find(arg => arg !== '--list') || 'Website~');
const page = fs.readFileSync(path.join(site, 'features.html'), 'utf8');
const media = path.join(site, 'media/v4');
const listed = JSON.parse(fs.readFileSync(path.join(media, 'ready.json'), 'utf8')).ready;
if (!Array.isArray(listed)) throw new Error('ready.json needs a ready array');
const ready = new Set(listed);
const captures = new Map();
const entries = new Set();
const locales = ['', 'ja/', 'ko/', 'zh-CN/'];
const localizedImages = new Set([
  'studio/select-uv.webp', 'studio/select-part.webp', 'studio/select-topology.webp',
  'studio/select-colour.webp', 'studio/select-paint.webp', 'studio/selection-tools-panel.webp',
  'studio/paint-handoff-panel.webp', 'studio/recolour-scope-panel.webp', 'studio/recolour-tools-panel.webp',
  'studio/paint-poster.webp', 'studio/save-panel.webp', 'studio/history-panel.webp', 'studio/audit-panel.webp',
  'studio/bake-ao-panel.webp', 'studio/bake-normal-panel.webp', 'studio/bake-apply-panel.webp',
  'studio/quick-material-panel.webp', 'studio/advanced-copy-panel.webp', 'studio/mask-picker-panel.webp',
  'studio/brush-library-panel.webp', 'studio/brush-shapes-panel.webp', 'studio/pen-controls-panel.webp',
  'studio/draft-browser.webp', 'studio/template-panel.webp', 'studio/style-pack-panel.webp',
  'studio/body-headband.webp', 'studio/body-cushion.webp',
  'ui-skill-tree-030.webp', 'ui-ai-helper-030.webp',
]);

function relative(file) {
  if (typeof file !== 'string' || !file || file.includes('\\') || path.posix.isAbsolute(file) ||
      path.posix.normalize(file) !== file || file.split('/').some(part => part === '..' || part === '.')) {
    throw new Error('Invalid relative media path');
  }
  return file;
}

function attributes(tag) {
  return Object.fromEntries([...tag.matchAll(/([\w:-]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g)]
    .map(([, key, double, single]) => [key, double ?? single]));
}

function mediaFile(value) {
  if (!value) return null;
  const file = value.split(/[?#]/)[0];
  if (file.startsWith('media/v4/')) return relative(file.slice('media/v4/'.length));
  if (file.startsWith('studio/')) return relative(file);
  return null;
}

function webpSize(file) {
  const bytes = fs.readFileSync(path.join(media, file));
  if (bytes.length < 30 || bytes.toString('ascii', 0, 4) !== 'RIFF' || bytes.toString('ascii', 8, 12) !== 'WEBP') {
    throw new Error(file + ' is not a WebP capture');
  }
  switch (bytes.toString('ascii', 12, 16)) {
    case 'VP8X': return [1 + bytes.readUIntLE(24, 3), 1 + bytes.readUIntLE(27, 3)];
    case 'VP8L': {
      const packed = bytes.readUInt32LE(21);
      return [1 + (packed & 0x3fff), 1 + ((packed >>> 14) & 0x3fff)];
    }
    case 'VP8 ': return [bytes.readUInt16LE(26) & 0x3fff, bytes.readUInt16LE(28) & 0x3fff];
    default: throw new Error(file + ' has an unsupported WebP header');
  }
}

function register(file, width, height) {
  relative(file);
  if (!ready.has(file) || !fs.existsSync(path.join(media, file)) || !fs.statSync(path.join(media, file)).isFile() ||
      fs.statSync(path.join(media, file)).size === 0) throw new Error(file + ' is missing from the media list or empty');
  entries.add(file);
  const previous = captures.get(file);
  if (width && height) {
    if (previous?.[0] && (previous[0] !== width || previous[1] !== height)) throw new Error(file + ' has conflicting sizes');
    captures.set(file, [width, height]);
  } else if (file.endsWith('.webp') && !previous) captures.set(file, [null, null]);
}

function registerImage(file, width, height) {
  for (const locale of localizedImages.has(file) ? locales : ['']) register(locale + file, width, height);
}

for (const [tag] of page.matchAll(/<img\b[^>]*>/g)) {
  const attrs = attributes(tag);
  const width = Number(attrs.width), height = Number(attrs.height);
  const source = attrs.src || attrs['data-opening-image'] || 'An image';
  if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height) || width <= 0 || height <= 0) {
    throw new Error(source + ' needs positive intrinsic dimensions');
  }
  for (const value of [attrs.src, attrs['data-opening-image']]) {
    const file = mediaFile(value);
    if (!file) continue;
    registerImage(file, width, height);
  }
}

for (const [, value] of page.matchAll(/data-image="([^"]+)"/g)) {
  const file = mediaFile(value);
  if (!file) continue;
  const wide = /^studio\/surface-/.test(file);
  registerImage(file, wide ? 1600 : 1280, wide ? 900 : 720);
}

for (const [, value] of page.matchAll(/data-sample="([^"]+)"/g)) {
  const file = mediaFile(value);
  if (!file) continue;
  const texture = /^studio\/surface-/.test(file);
  registerImage(file, texture ? 477 : 320, texture ? 364 : 320);
}

const studies = {
  select: { files: ['select-0', 'select-1'], local: true, views: ['model', 'uv'] },
  paint: { files: ['paint-0', 'paint-1'], local: true, views: ['model'] },
  material: { files: [] },
};
const activeStudies = new Set([...page.matchAll(/data-motion-study="([^"]+)"/g)].map(([, name]) => name));
if (activeStudies.has('select')) {
  for (let group = 0; group < 4; group++) {
    for (const locale of locales) {
      register(locale + 'studio/motion/select-detail-' + group + '.webp', 3840, 1440);
    }
  }
}
if (activeStudies.has('material')) {
  const file = 'studio/material-study.json';
  const study = readJson(file);
  if (study.schema !== 1 || study.framesPerEffect !== 12 || !Array.isArray(study.effects) || study.effects.length === 0) {
    throw new Error(file + ' needs schema 1 and captured twelve-frame effects');
  }
  const ids = new Set(), sheets = new Set(), images = new Set();
  for (const effect of study.effects) {
    if (!effect || typeof effect.id !== 'string' || !/^[a-z][a-z0-9-]*$/.test(effect.id) || ids.has(effect.id) ||
        effect.file !== 'material-' + effect.id || sheets.has(effect.file) ||
        effect.image !== 'studio/' + effect.file + '.webp' || images.has(effect.image) ||
        typeof effect.title !== 'string' || !effect.title || typeof effect.caption !== 'string' || !effect.caption) {
      throw new Error(file + ' has an invalid or duplicate material effect');
    }
    ids.add(effect.id); sheets.add(effect.file); images.add(effect.image);
    studies.material.files.push(effect.file);
    register(effect.image, 1280, 720);
  }
  if (!images.has(study.finish)) throw new Error(file + ' finish must name a declared effect image');
}
for (const name of activeStudies) {
  const study = studies[name];
  if (!study) throw new Error('Unknown Features motion study: ' + name);
  for (const locale of study.local ? locales : ['']) {
    for (const sheet of study.files) register(locale + 'studio/motion/' + sheet + '.webp', 3200, 1350);
  }
}

for (const [, value] of page.matchAll(/data-recording="([^"]+)"/g)) {
  const clip = mediaFile(value);
  if (!clip?.endsWith('.mp4')) throw new Error('A Features recording needs a local MP4');
  for (const locale of locales) register(locale + clip);
}

for (const [tag] of page.matchAll(/<video\b[^>]*>/g)) {
  const attrs = attributes(tag);
  if (!attrs['data-loop']) continue;
  const clip = relative(attrs['data-loop']);
  const poster = mediaFile(attrs.poster);
  if (!poster || !(Number(attrs.width) > 0 && Number(attrs.height) > 0)) {
    throw new Error(clip + ' needs a local poster and positive intrinsic dimensions');
  }
  for (const locale of locales) {
    register(locale + clip + '.mp4');
    register(locale + poster, Number(attrs.width), Number(attrs.height));
  }
}

function readJson(file) {
  register(file);
  return JSON.parse(fs.readFileSync(path.join(media, file), 'utf8'));
}

if ([...activeStudies].some(name => studies[name].views)) {
  for (const locale of locales) {
    const file = locale + 'studio/media-layout.json';
    const layout = readJson(file);
    if (layout.schema !== 1 || !Array.isArray(layout.tile) || layout.tile.length !== 2 ||
        layout.tile[0] !== 800 || layout.tile[1] !== 450) throw new Error(file + ' needs schema 1 and an 800×450 tile');
    for (const name of activeStudies) {
      for (const view of studies[name].views || []) {
        const rect = layout[name]?.[view];
        if (!Array.isArray(rect) || rect.length !== 4 || !rect.every(Number.isFinite) ||
            rect[0] < 0 || rect[1] < 0 || rect[2] <= 0 || rect[3] <= 0 ||
            rect[0] + rect[2] > 800 || rect[1] + rect[3] > 450) {
          throw new Error(file + ' has an invalid ' + name + '.' + view + ' rectangle');
        }
      }
    }
  }
}

const stack = readJson('studio/layer-stack.json');
if (!Array.isArray(stack.layers) || stack.layers.length === 0) throw new Error('Missing captured editing stages');
for (const row of stack.layers) {
  register('studio/' + row.stage, 1600, 900);
  register('studio/' + row.swatch, 360, 360);
  register('studio/' + row.plane, 360, 360);
}
for (const [button] of page.matchAll(/<button\b[^>]*\bdata-stack-material=[^>]*>/g)) {
  const material = attributes(button)['data-stack-material'];
  if (material === 'earcup') continue;
  const layers = stack.materials?.[material];
  if (!Array.isArray(layers) || layers.length < 2 || layers[0].id !== '00-base') {
    throw new Error('Missing original texture or editable layers for ' + material);
  }
  for (const row of layers) {
    register('studio/' + row.plane, 480, 480);
    register('studio/' + row.swatch, 480, 480);
  }
}

const blends = readJson('studio/blends.json');
if (blends.placeholder || !Array.isArray(blends.blend) || !Array.isArray(blends.adjust) ||
    blends.blend.length === 0 || blends.adjust.length === 0) throw new Error('Missing captured blend and adjustment examples');
register(blends.base, 960, 960);
for (const row of [...blends.blend, ...blends.adjust]) register(row.img, 960, 960);

for (const [file, [width, height]] of captures) {
  const [actualWidth, actualHeight] = webpSize(file);
  if (width && height && (width !== actualWidth || height !== actualHeight)) {
    throw new Error(file + ' is ' + actualWidth + '×' + actualHeight + ', expected ' + width + '×' + height);
  }
}
for (const file of entries) {
  if (!file.endsWith('.mp4')) continue;
  const header = Buffer.alloc(12), handle = fs.openSync(path.join(media, file), 'r');
  try {
    if (fs.readSync(handle, header, 0, header.length, 0) !== header.length || header.toString('ascii', 4, 8) !== 'ftyp') {
      throw new Error(file + ' is not an MP4 container');
    }
  } finally { fs.closeSync(handle); }
}
const movies = [...entries].filter(file => file.endsWith('.mp4'));
if (args.includes('--list')) {
  console.log(JSON.stringify([...entries].sort(), null, 2));
} else {
  console.log('Verified ' + captures.size + ' Features images and motion sheets, ' + movies.length + ' native videos and their localized layouts.');
}
