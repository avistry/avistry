// Gives every page a copy per language (ja/, ko/, zh-CN/) whose title, description and share picture are already in
// that language, so a shared link previews in the language it was copied in: link previews never run JavaScript.
// The copies use the same files as the English pages through <base href="../">; app.js does the rest of the
// translation as usual. Every page also gets hreflang links to its other languages.
//
//   node .github/scripts/build-langs.mjs --site _site
import fs from 'node:fs';
import path from 'node:path';

const i = process.argv.indexOf('--site');
const site = path.resolve(i > 0 ? process.argv[i + 1] : '_site');
const LANGS = ['ja', 'ko', 'zh-CN'];
const LOCALE = { en: 'en_US', ja: 'ja_JP', ko: 'ko_KR', 'zh-CN': 'zh_CN' };
const PAGES = fs.readdirSync(site).filter(f => f.endsWith('.html') && fs.readFileSync(path.join(site, f), 'utf8').includes('data-title-key'));

const dict = lang => Object.assign({}, ...fs.readdirSync(path.join(site, 'i18n', lang)).filter(f => f.endsWith('.json'))
  .map(f => JSON.parse(fs.readFileSync(path.join(site, 'i18n', lang, f), 'utf8'))));
const esc = s => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const strip = s => String(s).replace(/<[^>]+>/g, '');

function meta(html, attr, name, value) {
  const re = new RegExp('<meta ' + attr + '="' + name.replace(/[.:]/g, '\\$&') + '" content="[^"]*"');
  return re.test(html) ? html.replace(re, `<meta ${attr}="${name}" content="${esc(value)}"`) : html;
}

let made = 0;
for (const page of PAGES) {
  const file = path.join(site, page);
  let en = fs.readFileSync(file, 'utf8');
  const url = (/<meta property="og:url" content="([^"]+)"/.exec(en) || [])[1];
  if (!url) { console.warn('skipped ' + page + ' (no og:url)'); continue; }
  const root = url.slice(0, url.lastIndexOf('/') + 1);
  const name = page === 'index.html' ? '' : page;
  const href = lang => root + (lang === 'en' ? '' : lang + '/') + name;
  const alts = ['en', ...LANGS].map(l => `<link rel="alternate" hreflang="${l}" href="${href(l)}">`).join('\n')
    + `\n<link rel="alternate" hreflang="x-default" href="${href('en')}">\n<meta name="ats-lang-dirs" content="1">\n`;
  const titleKey = (/data-title-key="([^"]+)"/.exec(en) || [])[1];
  const descKey = (/content:([\w.-]+meta\.description)/.exec(en) || [])[1];
  const img = (/<meta property="og:image" content="([^"]+)"/.exec(en) || [])[1];

  for (const lang of LANGS) {
    const d = dict(lang);
    let h = en.replace('<html lang="en"', `<html lang="${lang}"`)
      .replace('<meta charset="utf-8">', '<meta charset="utf-8">\n<base href="../">');
    if (titleKey && d[titleKey]) {
      h = h.replace(/<title>[^<]*<\/title>/, `<title>${esc(strip(d[titleKey]))}</title>`);
      h = meta(h, 'property', 'og:title', strip(d[titleKey]));
    }
    if (descKey && d[descKey]) {
      h = meta(h, 'name', 'description', strip(d[descKey]));
      h = meta(h, 'property', 'og:description', strip(d[descKey]));
    }
    h = meta(h, 'property', 'og:url', href(lang)).replace(/<link rel="canonical" href="[^"]*">/, `<link rel="canonical" href="${href(lang)}">`);
    if (img) {
      const local = img.slice(root.length).replace(/\.jpg$/, '-' + lang + '.jpg');
      if (fs.existsSync(path.join(site, local))) {
        h = meta(h, 'property', 'og:image', root + local);
        h = meta(h, 'name', 'twitter:image', root + local);
        if (titleKey && d[titleKey]) h = meta(h, 'property', 'og:image:alt', strip(d[titleKey]));
      }
    }
    h = h.replace(/<meta property="og:type"[^>]*>\n/, m => m + `<meta property="og:locale" content="${LOCALE[lang]}">\n`);
    h = h.replace('</head>', alts + '</head>');
    fs.mkdirSync(path.join(site, lang), { recursive: true });
    fs.writeFileSync(path.join(site, lang, page), h);
    made++;
  }
  en = en.replace(/<meta property="og:type"[^>]*>\n/, m => m + `<meta property="og:locale" content="${LOCALE.en}">\n`)
    .replace('</head>', alts + '</head>');
  fs.writeFileSync(file, en);
}
console.log(`made ${made} language copies of ${PAGES.length} pages`);
