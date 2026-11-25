import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { readZipEntry } from './lib.mjs';

const args = Object.fromEntries(process.argv.slice(2).reduce((a, v, i, all) => (v.startsWith('--') ? [...a, [v.slice(2), all[i + 1]]] : a), []));
const sourcePath = args.source ?? '.github/vpm/source.json';
const outPath = args.out ?? '_site/index.json';

const [owner, repoName] = (process.env.GITHUB_REPOSITORY ?? '').split('/');
const resolve = (value) => {
  if (typeof value === 'string') {
    if (!owner) return value;
    return value
      .replaceAll('OWNER/REPO', `${owner}/${repoName}`)
      .replaceAll('OWNER.github.io/REPO', `${owner.toLowerCase()}.github.io/${repoName}`)
      .replaceAll('OWNER', owner);
  }
  if (Array.isArray(value)) return value.map(resolve);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, resolve(v)]));
  return value;
};

const source = resolve(JSON.parse(fs.readFileSync(sourcePath, 'utf8')));
const headers = { Accept: 'application/vnd.github+json', 'User-Agent': 'vpm-listing-builder' };
if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

async function listReleases(repo) {
  const all = [];
  for (let page = 1; ; page++) {
    const res = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=100&page=${page}`, { headers });
    if (!res.ok) throw new Error(`GitHub API ${res.status} for ${repo}: ${await res.text()}`);
    const batch = await res.json();
    all.push(...batch);
    if (batch.length < 100) return all;
  }
}

const packages = {};
for (const repo of source.githubRepos ?? []) {
  if (repo.includes('OWNER')) {
    console.warn(`Skipping placeholder repo "${repo}". Set GITHUB_REPOSITORY or edit ${sourcePath}.`);
    continue;
  }
  for (const release of await listReleases(repo)) {
    if (release.draft) continue;
    for (const asset of release.assets) {
      if (!asset.name.endsWith('.zip')) continue;
      const res = await fetch(asset.browser_download_url);
      if (!res.ok) throw new Error(`Download failed (${res.status}): ${asset.browser_download_url}`);
      const zip = Buffer.from(await res.arrayBuffer());
      const manifestBytes = readZipEntry(zip, 'package.json');
      if (!manifestBytes) {
        console.warn(`Skipping ${asset.name}: no package.json at zip root`);
        continue;
      }
      const manifest = resolve(JSON.parse(manifestBytes.toString('utf8')));
      const entry = {
        ...manifest,
        url: asset.browser_download_url,
        zipSHA256: crypto.createHash('sha256').update(zip).digest('hex'),
      };
      const pkg = (packages[manifest.name] ??= { versions: {} });
      pkg.versions[manifest.version] ??= entry;
      console.log(`Listed ${manifest.name} ${manifest.version} (${release.tag_name})`);
    }
  }
}

const listing = {
  name: source.name,
  id: source.id,
  author: source.author?.name ?? '',
  url: source.url,
  packages,
};
fs.mkdirSync(path.dirname(path.resolve(outPath)), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(listing, null, 2) + '\n');
console.log(`${outPath}: ${Object.keys(packages).length} package(s)`);
