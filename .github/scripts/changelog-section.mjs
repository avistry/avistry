import fs from 'node:fs';

const [version, file = 'CHANGELOG.md'] = process.argv.slice(2);
if (!version) {
  console.error('Usage: node changelog-section.mjs <version> [CHANGELOG.md]');
  process.exit(2);
}

const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
const heading = new RegExp(`^##\\s+\\[?v?${version.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\]?(\\s|$)`);
const start = lines.findIndex((l) => heading.test(l));
if (start < 0) {
  console.error(`No "## [${version}]" section in ${file}`);
  process.exit(1);
}

let end = lines.findIndex((l, i) => i > start && /^##\s/.test(l) && !/^###/.test(l));
if (end < 0) end = lines.length;

const body = lines
  .slice(start + 1, end)
  .filter((l) => !/^\[[^\]]+\]:\s+\S+/.test(l))
  .join('\n')
  .trim();

if (!body) {
  console.error(`Section [${version}] in ${file} is empty`);
  process.exit(1);
}
process.stdout.write(body + '\n');
