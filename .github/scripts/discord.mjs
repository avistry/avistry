// Usage:
//   node discord.mjs release <version> [--dry]   post the release to Discord (#announcements)
//   node discord.mjs commits [--dry]             post the pushed commits (GITHUB_EVENT_PATH) to Discord (#dev-commits)
// The webhook address comes from DISCORD_WEBHOOK and is never printed. Without it, or with --dry, the message is only
// printed. Runs in GitHub Actions; the tool itself makes no network calls.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const [mode, arg] = process.argv.slice(2);
const dry = process.argv.includes('--dry');
const repo = process.env.GITHUB_REPOSITORY || 'avistry/avistry';
const site = 'https://avistry.github.io/avistry/';
const identity = { username: 'Avistry', avatar_url: `${site}media/icon-512.png` };
const cut = (s, n) => (s.length > n ? s.slice(0, n - 1).trimEnd() + '…' : s);

async function send(payload) {
  const hook = process.env.DISCORD_WEBHOOK;
  if (dry || !hook) {
    console.log(JSON.stringify(payload, null, 2));
    if (!hook && !dry) console.log('::notice::DISCORD_WEBHOOK is not set, so nothing was posted.');
    return;
  }
  const res = await fetch(`${hook}?wait=true`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...identity, allowed_mentions: { parse: [] }, ...payload }),
  });
  if (!res.ok) { console.error(`Discord answered ${res.status}: ${cut(await res.text(), 300)}`); process.exit(1); }
  console.log('Posted to Discord.');
}

async function release(version) {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const section = execFileSync(process.execPath, [path.join(here, 'changelog-section.mjs'), version, 'CHANGELOG.md'], { encoding: 'utf8' });
  const lines = section.split('\n').filter((l) => !/^- Website:/.test(l));

  // The lines before the first heading, then the Highlights bullets (wrapped lines joined), then the Added groups.
  const lead = [];
  for (const l of lines) { if (/^###\s/.test(l)) break; if (l.trim()) lead.push(l.trim()); }
  const bullets = [];
  let heading = '';
  const groups = [];
  for (const l of lines) {
    if (/^###\s/.test(l)) { heading = l.replace(/^###\s+/, '').trim(); continue; }
    if (heading === 'Highlights') {
      if (l.startsWith('- ')) bullets.push(l.slice(2).trim());
      else if (l.trim() && bullets.length) bullets[bullets.length - 1] += ' ' + l.trim();
    }
    const g = heading === 'Added' && /^\*\*(.+)\*\*\s*$/.exec(l);
    if (g) groups.push({ title: g[1], n: 0 });
    else if (heading === 'Added' && l.startsWith('- ') && groups.length) groups[groups.length - 1].n++;
  }
  const counts = (h) => lines.reduce((a, l) => { if (/^###\s/.test(l)) a.on = l.includes(h); else if (a.on && l.startsWith('- ')) a.n++; return a; }, { on: false, n: 0 }).n;

  const tag = `v${version}`;
  const url = `https://github.com/${repo}/releases/tag/${tag}`;
  const parts = [];
  if (lead.length) parts.push(lead.join(' '));
  if (bullets.length) parts.push(bullets.map((b) => `• ${b}`).join('\n'));
  if (groups.length) parts.push('**What’s inside:** ' + groups.map((g) => `${g.title} (${g.n})`).join(' · '));
  const changed = counts('Changed'), fixed = counts('Fixed');
  if (changed || fixed) parts.push([changed && `${changed} changed`, fixed && `${fixed} fixed`].filter(Boolean).join(' · '));
  parts.push(`[**Release notes**](${url}) · [**Add to VCC**](${site}download.html) · [**Every feature**](${site}features.html)`);

  let description = parts.join('\n\n');
  if (description.length > 4000) description = cut(description, 4000);

  const embed = { title: `Avistry ${version}`, url, description, color: 0xff6f91, timestamp: new Date().toISOString() };
  const banner = `https://raw.githubusercontent.com/${repo}/${tag}/Website~/media/release/${version}/banner.jpg`;
  try { if ((await fetch(banner, { method: 'HEAD' })).ok) embed.image = { url: banner }; } catch { /* no banner: text only */ }
  await send({ content: `**Avistry ${version} is out.** Update in VCC, or download it from the release page.`, embeds: [embed] });
}

async function commits() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const list = (event.commits || []).filter((c) => c.distinct !== false);
  if (event.deleted || !list.length) { console.log('No new commits to post.'); return; }
  const branch = (event.ref || '').replace(/^refs\/heads\//, '');
  // Only the first line of each message, and no author names: people's names stay out of the channel.
  const rows = list.slice(-15).map((c) => `[\`${c.id.slice(0, 7)}\`](${c.url}) ${cut(c.message.split('\n')[0], 90)}`);
  if (list.length > 15) rows.unshift(`…and ${list.length - 15} earlier`);
  const n = list.length;
  await send({
    embeds: [{
      title: `${n} new commit${n === 1 ? '' : 's'} on ${branch}`,
      url: event.compare || `https://github.com/${repo}/commits/${branch}`,
      description: cut(rows.join('\n'), 4000),
      color: 0x43dcff,
      timestamp: new Date().toISOString(),
    }],
  });
}

if (mode === 'release' && arg && !arg.startsWith('--')) await release(arg);
else if (mode === 'commits') await commits();
else { console.error('Usage: node discord.mjs release <version> [--dry] | commits [--dry]'); process.exit(2); }
