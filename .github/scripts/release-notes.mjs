import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { releaseInfo } from './release-info.mjs';

const args = process.argv.slice(2);
const fullGallery = args.includes('--full-gallery');
const [version, file = 'CHANGELOG.md'] = args.filter((arg) => arg !== '--full-gallery' && arg !== '--title');
if (!version) { console.error('Usage: node release-notes.mjs <version> [CHANGELOG.md] [--full-gallery | --title]'); process.exit(2); }
const { name, subtitle } = releaseInfo(version);
if (args.includes('--title')) {
  console.log(name);
  process.exit(0);
}
const here = path.dirname(fileURLToPath(import.meta.url));
const section = execFileSync(process.execPath, [path.join(here, 'changelog-section.mjs'), version, file], { encoding: 'utf8' });

const lines = section.split('\n').filter((l) => !/^- Website:/.test(l));
const out = [];
let foldGroups = false, group = null, hideGallery = false;
const close = () => {
  if (!group) return;
  while (group.body.length && !group.body[group.body.length - 1].trim()) group.body.pop();
  const n = group.body.filter((l) => l.startsWith('- ')).length;
  out.push(`<details><summary><b>${group.title}</b> (${n})</summary>`, '', ...group.body, '', '</details>', '');
  group = null;
};
for (const l of lines) {
  if (/^###\s/.test(l)) {
    close();
    foldGroups = /^###\s+(Added|Feature drops)\s*$/.test(l);
    hideGallery = !fullGallery && /^###\s+Editor screenshots\s*$/.test(l);
    out.push(l);
    if (hideGallery) {
      const changelog = version === 'Unreleased' ? 'CHANGELOG.md' : `https://github.com/avistry/avistry/blob/v${version}/CHANGELOG.md`;
      out.push('', `[View the full screenshot gallery](${changelog}#editor-screenshots)`, '');
    }
    continue;
  }
  if (hideGallery) continue;
  const g = foldGroups && /^\*\*(.+)\*\*\s*$/.exec(l);
  if (g) { close(); group = { title: g[1], body: [] }; continue; }
  (group ? group.body : out).push(l);
}
close();

const intro = path.join('.github', 'release', version, 'intro.md');
let notes = out.join('\n').replace(/\n{3,}/g, '\n\n').trim() + '\n';
let template = fs.existsSync(intro) ? fs.readFileSync(intro, 'utf8').trim() : '';
const subtitleMarker = '<!-- release-subtitle -->';
if (template.includes(subtitleMarker)) {
  template = template.replace(subtitleMarker, () => subtitle || '');
} else if (subtitle) {
  notes = subtitle + '\n\n' + notes;
}
if (template) {
  const marker = '<!-- release-notes -->';
  notes = template.includes(marker)
    ? template.replace(marker, () => notes.trimEnd()) + '\n'
    : template + '\n\n' + notes;
}
if (version !== 'Unreleased') {
  const screenshots = '.github/release/screenshots/';
  const taggedScreenshots = `https://raw.githubusercontent.com/avistry/avistry/v${version}/${screenshots}`;
  notes = notes.replaceAll(`](${screenshots}`, `](${taggedScreenshots}`);
}

const bodyBudget = 120_000;
const bytes = Buffer.byteLength(notes, 'utf8');
if (!fullGallery && bytes > bodyBudget) {
  console.error(`Release notes are ${bytes} bytes. Keep the GitHub body under ${bodyBudget} bytes before adding verification details.`);
  process.exit(1);
}
process.stdout.write(notes);
