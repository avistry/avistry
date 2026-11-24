using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class OnlineGuide
    {
        internal const string Site = "https://avistry.github.io/avistry/";
        internal const string DiscordSupport = "https://discord.gg/sSeWgDJJjY";
        internal const string GitHubBugReport = "https://github.com/avistry/avistry/issues/new?template=bug_report.yml";

        internal static void OpenSupport(bool gitHub) => Application.OpenURL(gitHub ? GitHubBugReport : DiscordSupport);

        internal sealed class Page
        {
            public readonly string Key, Path;
            public readonly bool Video;
            public Page(string key, string path, bool video) { Key = key; Path = path; Video = video; }
        }

        static readonly Page[] s_pages =
        {
            new Page("home", "index.html", false),
            new Page("steps", "index.html#how", true),
            new Page("tour", "features.html", false),
            new Page("select", "features.html#select", false),
            new Page("paint", "features.html#paint", true),
            new Page("layers", "features.html#layers", false),
            new Page("modes", "features.html#modes", false),
            new Page("colour", "features.html#colour", false),
            new Page("material", "features.html#quick-material", false),
            new Page("maps", "features.html#maps", false),
            new Page("undo", "features.html#history-example", false),
            new Page("files", "features.html#safety", false),
            new Page("report", "features.html#audit-example", false),
            new Page("learn", "features.html#learn", false),
            new Page("gallery", "gallery.html", false),
            new Page("glossary", "glossary.html", false),
            new Page("skills", "skills.html", false),
            new Page("faq", "about.html#faq", false),
            new Page("upload", "about.html#q-upload", false),
            new Page("shaders", "about.html#q-shaders", false),
            new Page("ai", "about.html#q-ai", false),
        };

        static readonly Dictionary<string, string> s_conceptPage = new Dictionary<string, string>
        {
            { "hierarchy", "steps" }, { "scene-view", "select" }, { "project-window", "files" }, { "asset", "files" },
            { "undo", "undo" }, { "simple-advanced", "learn" }, { "mesh", "steps" }, { "material", "material" },
            { "texture", "files" }, { "hue", "colour" }, { "tint", "colour" }, { "bake", "files" }, { "png", "files" },
            { "uv-map", "select" }, { "island", "select" }, { "seam", "paint" }, { "layer", "layers" }, { "opacity", "layers" },
            { "blend-mode", "modes" }, { "mask", "layers" }, { "clipping", "layers" }, { "gradient-map", "modes" },
            { "channels", "layers" }, { "ambient-occlusion", "maps" }, { "shader", "shaders" }, { "shader-lock", "shaders" },
            { "poiyomi", "shaders" }, { "liltoon", "shaders" }, { "vrchat-upload", "upload" }, { "import-settings", "report" },
            { "normal-map", "maps" }, { "sdf-face-shadow", "maps" }, { "ai-helper", "ai" },
        };

        static readonly Dictionary<string, string> s_tabPage = new Dictionary<string, string>
        {
            { "Target", "steps" }, { "Colour", "colour" }, { "Select", "select" }, { "Paint", "paint" }, { "Layers", "layers" }, { "Save", "files" },
        };

        static readonly string[] s_menu = { "tour", "gallery", null, "skills", "glossary", null, "faq", "home" };

        static Dictionary<string, Page> s_byKey;

        internal static IReadOnlyList<Page> Pages => s_pages;
        internal static IEnumerable<KeyValuePair<string, string>> ConceptPages => s_conceptPage;
        internal static IEnumerable<KeyValuePair<string, string>> TabPages => s_tabPage;

        internal static Page Get(string key)
        {
            if (key == null) return null;
            if (s_byKey == null)
            {
                s_byKey = new Dictionary<string, Page>();
                foreach (var p in s_pages) s_byKey[p.Key] = p;
            }
            return s_byKey.TryGetValue(key, out var page) ? page : null;
        }

        internal static Page ForConcept(string conceptId) => conceptId != null && s_conceptPage.TryGetValue(conceptId, out var k) ? Get(k) : null;
        internal static Page ForTab(string tabName) => tabName != null && s_tabPage.TryGetValue(tabName, out var k) ? Get(k) : null;

        internal static string Url(Page page, string anchor = null)
        {
            if (page == null) return null;
            string path = page.Path;
            int hash = path.IndexOf('#');
            string file = hash < 0 ? path : path.Substring(0, hash);
            string own = hash < 0 ? null : path.Substring(hash + 1);
            string frag = string.IsNullOrEmpty(anchor) ? own : anchor;
            string lang = SiteLanguage();
            return Site + file + (lang != null ? "?lang=" + lang : "") + (string.IsNullOrEmpty(frag) ? "" : "#" + Uri.EscapeDataString(frag));
        }

        internal static string TermUrl(string conceptId) => Url(Get("glossary"), conceptId);
        internal static string SkillUrl(string conceptId) => Url(Get("skills"), string.IsNullOrEmpty(conceptId) ? null : "sk-" + conceptId);

        static string SiteLanguage() => Loc.English ? null : Loc.Current;

        internal static string Shown(string url) => url != null && url.StartsWith("https://", StringComparison.Ordinal) ? url.Substring(8) : url;

        internal static bool Open(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith(Site, StringComparison.Ordinal)) return false;
            Application.OpenURL(url);
            return true;
        }

        internal static string Label(string id, string fallback)
        {
            var c = Glossary.Control(id);
            return c != null && !string.IsNullOrEmpty(c.label) ? c.label : fallback;
        }

        internal static string Tip(string id)
        {
            var c = Glossary.Control(id);
            return c != null ? c.tip : null;
        }

        internal static string PageLabel(Page p) => Label("web." + p.Key, p.Key);

        internal static string Hover(Page p, string url)
        {
            string tip = p == null ? null : Tip("web." + p.Key);
            return string.IsNullOrEmpty(tip) ? url : tip + "\n" + url;
        }

        internal static void AddMenu(GenericMenu menu, string tabName)
        {
            string root = Label("web.menu", "Online guide") + "/";
            var tab = ForTab(tabName);
            if (tab != null)
            {
                string url = Url(tab);
                menu.AddItem(new GUIContent(root + Label("web.thisTab", "This tab, explained") + ": " + PageLabel(tab)), false, () => Open(url));
                menu.AddSeparator(root);
            }
            foreach (var key in s_menu)
            {
                if (key == null) { menu.AddSeparator(root); continue; }
                var p = Get(key);
                string url = Url(p);
                menu.AddItem(new GUIContent(root + PageLabel(p)), false, () => Open(url));
            }
        }
    }
}
