import fs from 'node:fs';
import path from 'node:path';

export function releaseInfo(version) {
  if (!/^(?:Unreleased|\d+\.\d+\.\d+(?:-[\w.-]+)?)$/.test(version)) throw new Error('Invalid release version.');
  const file = path.join('.github', 'release', version, 'release.json');
  const { title, subtitle } = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : {};
  return { name: title ? `Avistry ${version} — ${title}` : `Avistry ${version}`, subtitle };
}
