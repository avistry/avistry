// Writes releases.json for the website: every published release with its files, download counts and checksums,
// plus the total downloads across all versions. Node 20+, no dependencies.
// Usage: node build-releases.mjs --out _site/releases.json [--repo owner/name]
// Reads GITHUB_TOKEN (optional, raises the API rate limit) and GITHUB_REPOSITORY.
import fs from 'node:fs';

const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(name); return i === -1 ? null : args[i + 1]; };
const out = opt('--out');
const repo = opt('--repo') || process.env.GITHUB_REPOSITORY;
if (!out || !repo) { console.error('Usage: build-releases.mjs --out <file> [--repo owner/name]'); process.exit(2); }

const headers = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'avistry-pages' };
if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

async function api(url) {
  const r = await fetch(url, { headers });
  if (!r.ok) throw new Error(`${r.status} ${url}`);
  return r.json();
}

// the files people download: the VCC zip (VCC itself fetches this one) and the .unitypackage
function kind(name) {
  if (name.endsWith('.unitypackage')) return 'unitypackage';
  if (name.endsWith('.zip')) return 'zip';
  return null;
}

const all = [];
for (let page = 1; page <= 10; page++) {
  const batch = await api(`https://api.github.com/repos/${repo}/releases?per_page=100&page=${page}`);
  all.push(...batch);
  if (batch.length < 100) break;
}

const releases = [];
for (const r of all) {
  if (r.draft) continue;
  const files = r.assets.filter((a) => kind(a.name)).map((a) => ({
    kind: kind(a.name), name: a.name, url: a.browser_download_url, size: a.size, downloads: a.download_count
  }));
  // the release workflow attaches latest.json with the checksums and the VirusTotal links of that version
  let sums = null;
  const meta = r.assets.find((a) => a.name === 'latest.json');
  if (meta) {
    try { const m = await (await fetch(meta.browser_download_url)).json(); sums = { sha256: m.sha256 || null, virustotal: m.virustotal || null }; } catch (e) { /* the list still works without them */ }
  }
  releases.push({
    version: String(r.tag_name || '').replace(/^v/, ''),
    tag: r.tag_name,
    name: r.name || r.tag_name,
    date: (r.published_at || r.created_at || '').slice(0, 10),
    prerelease: !!r.prerelease,
    url: r.html_url,
    downloads: files.reduce((n, f) => n + f.downloads, 0),
    files,
    sha256: sums && sums.sha256,
    virustotal: sums && sums.virustotal
  });
}
releases.sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0));

const latest = releases.find((r) => !r.prerelease) || releases[0] || null;
const data = {
  generated: new Date().toISOString(),
  repo,
  latest: latest ? latest.version : null,
  total_downloads: releases.reduce((n, r) => n + r.downloads, 0),
  releases
};
fs.writeFileSync(out, JSON.stringify(data, null, 1) + '\n');
console.log(`releases.json: ${releases.length} releases, ${data.total_downloads} downloads, latest ${data.latest}`);
