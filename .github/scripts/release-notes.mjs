// Usage: node release-notes.mjs <version> [CHANGELOG.md]
// Prints the GitHub release notes for <version>: the version's CHANGELOG section, with .github/release/<version>/intro.md
// (banner and highlight pictures) on top when it exists, lines about the website left out (the site is not part of
// the package), and each "**Group**" list under "### Added" folded into a <details> block so the page stays short.
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const [version, file = 'CHANGELOG.md'] = process.argv.slice(2);
if (!version) { console.error('Usage: node release-notes.mjs <version> [CHANGELOG.md]'); process.exit(2); }
const here = path.dirname(fileURLToPath(import.meta.url));
const section = execFileSync(process.execPath, [path.join(here, 'changelog-section.mjs'), version, file], { encoding: 'utf8' });

const lines = section.split('\n').filter((l) => !/^- Website:/.test(l));
const out = [];
let inAdded = false, group = null;
const close = () => {
  if (!group) return;
  while (group.body.length && !group.body[group.body.length - 1].trim()) group.body.pop();
  const n = group.body.filter((l) => l.startsWith('- ')).length;
  out.push(`<details><summary><b>${group.title}</b> (${n})</summary>`, '', ...group.body, '', '</details>', '');
  group = null;
};
for (const l of lines) {
  if (/^###\s/.test(l)) { close(); inAdded = /^###\s+Added/.test(l); out.push(l); continue; }
  const g = inAdded && /^\*\*(.+)\*\*\s*$/.exec(l);
  if (g) { close(); group = { title: g[1], body: [] }; continue; }
  (group ? group.body : out).push(l);
}
close();

const intro = path.join('.github', 'release', version, 'intro.md');
let notes = out.join('\n').replace(/\n{3,}/g, '\n\n').trim() + '\n';
if (fs.existsSync(intro)) notes = fs.readFileSync(intro, 'utf8').trim() + '\n\n' + notes;
process.stdout.write(notes);
