// Usage: node changelog-release.mjs <version> [CHANGELOG.md]
// Turns "## [Unreleased]" into "## [<version>] - <today>", puts a fresh empty "## [Unreleased]" above it and
// updates the compare links at the bottom. Exits 1 (and changes nothing) if Unreleased is empty or the version exists.
import fs from 'node:fs';

const REPO = process.env.GITHUB_REPOSITORY ? `https://github.com/${process.env.GITHUB_REPOSITORY}` : 'https://github.com/avistry/avistry';
const [version, file = 'CHANGELOG.md'] = process.argv.slice(2);
if (!version) {
  console.error('Usage: node changelog-release.mjs <version> [CHANGELOG.md]');
  process.exit(2);
}
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version)) {
  console.error(`"${version}" is not a version like 0.3.0`);
  process.exit(2);
}

const text = fs.readFileSync(file, 'utf8');
const eol = text.includes('\r\n') ? '\r\n' : '\n';
const lines = text.split(/\r?\n/);
const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const isLink = (l) => /^\[[^\]]+\]:\s+\S+/.test(l);
const isUnreleased = (l) => /^##\s+\[Unreleased\]\s*$/i.test(l);

const start = lines.findIndex(isUnreleased);
if (start < 0) {
  console.error(`No "## [Unreleased]" section in ${file}`);
  process.exit(1);
}
if (lines.some((l) => new RegExp(`^##\\s+\\[?v?${esc(version)}\\]?(\\s|$)`).test(l))) {
  console.error(`${file} already has a section for ${version}. Pick a new version number.`);
  process.exit(1);
}

let end = lines.findIndex((l, i) => i > start && /^##\s/.test(l) && !/^###/.test(l));
if (end < 0) end = lines.length;

// Real content = any line that is not blank, a "###" heading or a link definition.
const hasContent = lines.slice(start + 1, end).some((l) => l.trim() && !/^###/.test(l) && !isLink(l));
if (!hasContent) {
  console.error(`"## [Unreleased]" in ${file} is empty. Add what changed for users before releasing.`);
  process.exit(1);
}

const prevMatch = lines.slice(end).map((l) => /^##\s+\[v?(\d[^\]\s]*)\]/.exec(l)).find(Boolean);
const today = new Date().toISOString().slice(0, 10);
const ownLink = new RegExp(`^\\[(unreleased|v?${esc(version)})\\]:`, 'i');
// Only the trailing block of link definitions is rewritten; a link definition used inside an entry stays put.
let tail = lines.length;
while (tail > 0 && (lines[tail - 1].trim() === '' || isLink(lines[tail - 1]))) tail--;
const oldLinks = lines.slice(tail).filter((l) => isLink(l) && !ownLink.test(l));
const body = lines.slice(0, tail);
const at = body.findIndex(isUnreleased);
const out = [...body.slice(0, at), '## [Unreleased]', '', `## [${version}] - ${today}`, ...body.slice(at + 1)];
while (out.length && out[out.length - 1].trim() === '') out.pop();
out.push(
  '',
  `[Unreleased]: ${REPO}/compare/v${version}...HEAD`,
  prevMatch ? `[${version}]: ${REPO}/compare/v${prevMatch[1]}...v${version}` : `[${version}]: ${REPO}/releases/tag/v${version}`,
  ...oldLinks,
);
fs.writeFileSync(file, out.join(eol) + eol);
console.log(`${file}: Unreleased is now [${version}] - ${today}. A new empty Unreleased section is at the top.`);
