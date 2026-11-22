using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The plain-language glossary, the label/tip copy of every control and the UI strings, loaded from
    /// Editor/Glossary/en.json. For another language, the text of &lt;lang&gt;.json is laid over it field by field;
    /// ids, order and structure always come from en.json.
    /// </summary>
    internal static class Glossary
    {
        [System.Serializable] internal class Data { public int schema = 1; public string language = "en"; public bool machineTranslated; public Tier[] tiers; public Entry[] entries; public ControlCopy[] controls; public StringCopy[] strings; }
        [System.Serializable] internal class StringCopy { public string id, text; }
        [System.Serializable] internal class Tier { public int n; public string name, blurb, achievement; }
        [System.Serializable] internal class Entry
        {
            public string id, title, level, tip, definition, what, when, example, whereInUnity, docsUrl;
            public int order;
            public string[] related, usedBy, aliases;
            // Schema 2. Missing arrays stay null when the file is schema 1; read them through the helpers below.
            public int tier;                    // 1 to 6; 0 = not set, derived from level
            public bool bonus;                  // shown in the tree but never counted
            public bool gotIt;                  // has an "I've got it" button
            public string[] requires, doneBy, alsoCalled;
            public string tryIt, tryTab, tryControl, watchOut;
        }
        [System.Serializable] internal class ControlCopy { public string id, label, labelAdvanced, tip, concept; public bool simple; }

        const string RelativePath = "/Editor/Glossary/en.json";
        internal const int MaxTipWords = 15;
        internal const int MaxBodyWords = 60;

        internal const int TierCount = 6;
        internal const int CountedTotal = 40;
        internal const int MinTierEntries = 5, MaxTierEntries = 8;
        internal const int MaxTitleChars = 24, MaxBlurbWords = 12;
        internal const int MaxDefinitionWords = 20, MaxWhatWords = 25, MaxWhenWords = 20, MaxExampleWords = 25, MaxTryItWords = 15, MaxWatchOutWords = 18;

        /// <summary>Tab names (the Tab enum) that appear in the tab bar. Layers is not one of them.</summary>
        internal static readonly string[] TryTabs = { "Target", "Colour", "Select", "Paint", "Save" };
        internal static readonly string[] WindowTargets = { "window:hierarchy", "window:scene", "window:inspector", "window:project" };

        // Used when a schema 1 file has no tiers[]; schema 2 files carry their own.
        static readonly Tier[] DefaultTiers =
        {
            new Tier { n = 1, name = "Find your way", blurb = "The Unity windows and safety nets you use every day.", achievement = "Medal: Found my way" },
            new Tier { n = 2, name = "First recolour", blurb = "Pick a part, change its colour, save a new PNG.", achievement = "Medal: First recolour" },
            new Tier { n = 3, name = "Pieces and paint", blurb = "Keep edits on one area and paint on layers.", achievement = "Medal: Piece picker" },
            new Tier { n = 4, name = "Layer craft", blurb = "Mix, clip and shape layers like a painting app.", achievement = "Medal: Layer crafter" },
            new Tier { n = 5, name = "Shaders and upload", blurb = "How materials, shaders and VRChat treat your texture.", achievement = "Medal: Shader savvy" },
            new Tier { n = 6, name = "Power tools", blurb = "Import settings, colour space and baked maps.", achievement = "Medal: Power user" },
        };

        static Data s_data;
        static List<Entry> s_all;
        static Dictionary<string, Entry> s_entries;
        static Dictionary<string, ControlCopy> s_controls;
        static Dictionary<string, string[]> s_doneBy;
        static Tier[] s_tiers;
        static int[] s_tierSizes;
        static int s_counted;
        static string s_path;
        static Dictionary<string, string> s_strings;
        static bool s_englishOnly, s_provisional;
        static readonly HashSet<string> s_warned = new HashSet<string>();

        public static string JsonPath
        {
            get
            {
                if (!string.IsNullOrEmpty(s_path)) return s_path;
                var pi = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Glossary).Assembly);
                if (pi != null) return s_path = pi.assetPath + RelativePath;
                // Used from the Assets folder instead of as a package.
                foreach (var guid in AssetDatabase.FindAssets("en t:TextAsset"))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.EndsWith(RelativePath)) return s_path = p;
                }
                return null;
            }
        }

        public static IReadOnlyList<Entry> All
        {
            get { Load(); return s_all; }
        }

        internal static int Schema
        {
            get { Load(); return s_data.schema; }
        }

        /// <summary>The six tiers, in order. Falls back to built-in names when the file has none.</summary>
        internal static IReadOnlyList<Tier> Tiers
        {
            get { Load(); return s_tiers; }
        }

        /// <summary>Entries that count towards "n of 40" (everything except bonus nodes).</summary>
        internal static int CountedCount
        {
            get { Load(); return s_counted; }
        }

        public static Entry Get(string conceptId)
        {
            if (string.IsNullOrEmpty(conceptId)) return null;
            Load();
            if (s_entries.TryGetValue(conceptId, out var e)) return e;
            WarnOnce("concept:" + conceptId, "Glossary: unknown concept id '" + conceptId + "'.");
            return null;
        }

        /// <summary>True when the id is an entry. Unlike Get, it never logs.</summary>
        internal static bool Has(string conceptId)
        {
            if (string.IsNullOrEmpty(conceptId)) return false;
            Load();
            return s_entries.ContainsKey(conceptId);
        }

        public static ControlCopy Control(string controlId)
        {
            if (string.IsNullOrEmpty(controlId)) return null;
            Load();
            if (s_controls.TryGetValue(controlId, out var c)) return c;
            WarnOnce("control:" + controlId, "Glossary: unknown control id '" + controlId + "'.");
            return null;
        }

        internal static Tier TierInfo(int n)
        {
            Load();
            return n >= 1 && n <= TierCount ? s_tiers[n - 1] : null;
        }

        /// <summary>The entry's tier: its own when set, otherwise derived from level (basic 1, intermediate 3, advanced 5).</summary>
        internal static int TierOf(Entry e)
        {
            if (e == null) return 1;
            if (e.tier >= 1 && e.tier <= TierCount) return e.tier;
            switch (e.level)
            {
                case "intermediate": return 3;
                case "advanced": return 5;
                default: return 1;
            }
        }

        /// <summary>Counted entries in a tier (bonus nodes left out).</summary>
        internal static int TierSize(int n)
        {
            Load();
            return n >= 1 && n <= TierCount ? s_tierSizes[n] : 0;
        }

        /// <summary>Ids of the concepts that a learn event completes, or null. One dictionary lookup.</summary>
        internal static string[] ConceptsDoneBy(string learnEvent)
        {
            if (string.IsNullOrEmpty(learnEvent)) return null;
            Load();
            return s_doneBy.TryGetValue(learnEvent, out var ids) ? ids : null;
        }

        /// <summary>The translation of a UI string in the current language. False when there is none.</summary>
        internal static bool TryString(string en, out string text)
        {
            Load();
            return s_strings.TryGetValue(en, out text);
        }

        /// <summary>True when Editor/Glossary/&lt;code&gt;.json says it is an unreviewed machine translation.</summary>
        internal static bool IsMachineTranslated(string code)
        {
            var asset = LanguageAsset(code);
            if (asset == null) return false;
            try { return JsonUtility.FromJson<Data>(asset.text).machineTranslated; }
            catch (System.Exception) { return false; }
        }

        static TextAsset LanguageAsset(string code)
        {
            var path = JsonPath;
            if (string.IsNullOrEmpty(path) || code == "en") return null;
            return AssetDatabase.LoadAssetAtPath<TextAsset>(path.Substring(0, path.Length - "en.json".Length) + code + ".json");
        }

        public static void Reload()
        {
            s_data = null;
            s_strings = null;
            s_all = null;
            s_entries = null;
            s_controls = null;
            s_doneBy = null;
            s_tiers = null;
            s_tierSizes = null;
            s_path = null;
            s_warned.Clear();
        }

        /// <summary>
        /// Replaces {ctl:id} and {tab:Name} with the live label in quotes (the Advanced label when asked for).
        /// A token that does not resolve is left as written.
        /// </summary>
        internal static string ResolveTokens(string text, bool advanced)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            Load();
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open < 0 ? -1 : text.IndexOf('}', open);
                if (close < 0) break;
                sb.Append(text, i, open - i);
                if (TryResolveToken(text.Substring(open + 1, close - open - 1), advanced, out var label)) sb.Append('“').Append(label).Append('”');
                else sb.Append(text, open, close - open + 1);
                i = close + 1;
            }
            sb.Append(text, i, text.Length - i);
            return sb.ToString();
        }

        static bool TryResolveToken(string inner, bool advanced, out string label)
        {
            label = null;
            int colon = inner.IndexOf(':');
            if (colon <= 0) return false;
            string kind = inner.Substring(0, colon), arg = inner.Substring(colon + 1);
            string controlId;
            if (kind == "ctl") controlId = arg;
            else if (kind == "tab" && System.Array.IndexOf(TryTabs, arg) >= 0) controlId = "tab." + arg.ToLowerInvariant();
            else return false;
            if (!s_controls.TryGetValue(controlId, out var c)) return false;
            label = advanced && !string.IsNullOrEmpty(c.labelAdvanced) ? c.labelAdvanced : c.label;
            return !string.IsNullOrEmpty(label);
        }

        /// <summary>Copy problems, one line each; empty means the glossary is fine.</summary>
        public static List<string> Validate()
        {
            // The copy rules (word counts, title length) are rules for the English text, so en.json is checked on its own.
            bool translated = !Loc.English;
            if (translated) { Reload(); s_englishOnly = true; }
            try { return ValidateLoaded(); }
            finally { if (translated) { s_englishOnly = false; Reload(); } }
        }

        static List<string> ValidateLoaded()
        {
            Load();
            var issues = new List<string>();
            if (s_data.schema != 1 && s_data.schema != 2) issues.Add("schema is " + s_data.schema + " (must be 1 or 2)");
            var ids = new HashSet<string>();
            foreach (var e in s_data.entries)
            {
                if (e == null) continue;
                if (!ids.Add(e.id ?? "")) issues.Add("duplicate entry id: " + e.id);
            }
            var controlIds = new HashSet<string>();
            foreach (var c in s_data.controls)
            {
                if (c == null) continue;
                if (!controlIds.Add(c.id ?? "")) issues.Add("duplicate control id: " + c.id);
                if (!string.IsNullOrEmpty(c.concept) && !ids.Contains(c.concept))
                    issues.Add("control " + c.id + ": concept '" + c.concept + "' is not an entry id");
                int controlTip = WordCount(c.tip);
                if (controlTip > MaxTipWords) issues.Add("control " + c.id + ": tip has " + controlTip + " words (max " + MaxTipWords + ")");
            }
            var orders = new HashSet<int>();
            foreach (var e in s_data.entries)
            {
                if (e == null) continue;
                if (!orders.Add(e.order)) issues.Add(e.id + ": order " + e.order + " is used by another entry");
                int tip = WordCount(e.tip);
                if (tip > MaxTipWords) issues.Add(e.id + ": tip has " + tip + " words (max " + MaxTipWords + ")");
                int body = WordCount(e.what) + WordCount(e.when) + WordCount(e.example);
                if (body > MaxBodyWords) issues.Add(e.id + ": what+when+example has " + body + " words (max " + MaxBodyWords + ")");
                if (e.related != null)
                    foreach (var r in e.related)
                        if (!ids.Contains(r)) issues.Add(e.id + ": related '" + r + "' is not an entry id");
                if (e.usedBy != null)
                    foreach (var u in e.usedBy)
                        if (!controlIds.Contains(u)) issues.Add(e.id + ": usedBy '" + u + "' is not a control id");
            }
            if (s_data.schema >= 2) ValidateSchema2(issues, ids, controlIds);
            return issues;
        }

        static void ValidateSchema2(List<string> issues, HashSet<string> ids, HashSet<string> controlIds)
        {
            // Tiers
            var tiers = s_data.tiers;
            if (tiers == null || tiers.Length != TierCount) issues.Add("tiers must list exactly " + TierCount + " tiers");
            else
                for (int i = 0; i < tiers.Length; i++)
                {
                    var t = tiers[i];
                    if (t == null || t.n != i + 1) { issues.Add("tiers[" + i + "]: n must be " + (i + 1)); continue; }
                    if (string.IsNullOrWhiteSpace(t.name)) issues.Add("tier " + t.n + ": name is empty");
                    if (string.IsNullOrWhiteSpace(t.achievement)) issues.Add("tier " + t.n + ": achievement is empty");
                    int blurb = WordCount(t.blurb);
                    if (blurb == 0) issues.Add("tier " + t.n + ": blurb is empty");
                    else if (blurb > MaxBlurbWords) issues.Add("tier " + t.n + ": blurb has " + blurb + " words (max " + MaxBlurbWords + ")");
                }

            // Counts
            if (s_counted != CountedTotal) issues.Add("there are " + s_counted + " counted entries (must be " + CountedTotal + ")");
            for (int n = 1; n <= TierCount; n++)
                if (s_tierSizes[n] < MinTierEntries || s_tierSizes[n] > MaxTierEntries)
                    issues.Add("tier " + n + " has " + s_tierSizes[n] + " entries (must be " + MinTierEntries + " to " + MaxTierEntries + ")");

            var usedEvents = new HashSet<string>();
            foreach (var e in s_data.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                string who = e.id + ": ";

                if (e.tier < 1 || e.tier > TierCount) issues.Add(who + "tier must be 1 to " + TierCount);
                else
                {
                    string expected = e.tier <= 2 ? "basic" : e.tier <= 4 ? "intermediate" : "advanced";
                    if (e.level != expected) issues.Add(who + "level '" + e.level + "' does not match tier " + e.tier + " (" + expected + ")");
                }

                if (e.requires != null)
                    foreach (var r in e.requires)
                    {
                        if (!s_entries.TryGetValue(r ?? "", out var req)) issues.Add(who + "requires '" + r + "' is not an entry id");
                        else if (r == e.id) issues.Add(who + "requires itself");
                        else if (TierOf(req) > TierOf(e)) issues.Add(who + "requires '" + r + "' from a higher tier");
                    }

                bool hasEvents = e.doneBy != null && e.doneBy.Length > 0;
                if (!hasEvents && !e.gotIt) issues.Add(who + "needs doneBy events or gotIt");
                if (hasEvents)
                    foreach (var ev in e.doneBy)
                    {
                        if (!LearnEvents.Has(ev)) issues.Add(who + "doneBy '" + ev + "' is not in LearnEvents");
                        else usedEvents.Add(ev);
                    }

                if (string.IsNullOrWhiteSpace(e.title)) issues.Add(who + "title is empty");
                else if (e.title.Length > MaxTitleChars) issues.Add(who + "title has " + e.title.Length + " characters (max " + MaxTitleChars + ")");
                if (string.IsNullOrWhiteSpace(e.definition)) issues.Add(who + "definition is empty");
                else if (!string.IsNullOrWhiteSpace(e.tip) && string.Equals(e.definition.Trim(), e.tip.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    issues.Add(who + "definition is the same as the tip");
                CheckWords(issues, who, "definition", e.definition, MaxDefinitionWords);
                CheckWords(issues, who, "what", e.what, MaxWhatWords);
                CheckWords(issues, who, "when", e.when, MaxWhenWords);
                CheckWords(issues, who, "example", e.example, MaxExampleWords);
                CheckWords(issues, who, "tryIt", e.tryIt, MaxTryItWords);
                CheckWords(issues, who, "watchOut", e.watchOut, MaxWatchOutWords);

                CheckTokens(issues, who + "example", e.example);
                CheckTokens(issues, who + "tryIt", e.tryIt);
                CheckTokens(issues, who + "watchOut", e.watchOut);

                if (!string.IsNullOrEmpty(e.tryTab) && System.Array.IndexOf(TryTabs, e.tryTab) < 0)
                    issues.Add(who + "tryTab '" + e.tryTab + "' is not a tab in the tab bar");
                if (!string.IsNullOrEmpty(e.tryControl) && !controlIds.Contains(e.tryControl) && System.Array.IndexOf(WindowTargets, e.tryControl) < 0)
                    issues.Add(who + "tryControl '" + e.tryControl + "' is not a control id");
            }

            foreach (var ev in LearnEvents.All)
                if (!usedEvents.Contains(ev)) issues.Add("learn event '" + ev + "' is not used by any entry");

            // Cycles in requires, by depth-first search. 0 = new, 1 = on the path, 2 = done.
            var state = new Dictionary<string, int>();
            foreach (var e in s_data.entries)
                if (e != null && !string.IsNullOrEmpty(e.id) && s_entries.ContainsKey(e.id) && Visit(e.id, state, issues)) break;
        }

        static bool Visit(string id, Dictionary<string, int> state, List<string> issues)
        {
            state.TryGetValue(id, out int s);
            if (s == 2) return false;
            if (s == 1) { issues.Add("requires has a cycle through '" + id + "'"); return true; }
            state[id] = 1;
            var req = s_entries[id].requires;
            if (req != null)
                foreach (var r in req)
                    if (r != null && s_entries.ContainsKey(r) && Visit(r, state, issues)) return true;
            state[id] = 2;
            return false;
        }

        static void CheckWords(List<string> issues, string who, string field, string text, int max)
        {
            int n = WordCount(text);
            if (n > max) issues.Add(who + field + " has " + n + " words (max " + max + ")");
        }

        // Every {…} must be a token that resolves, so a typo shows up here and not as raw braces in the card.
        static void CheckTokens(List<string> issues, string where, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = 0;
            while (true)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) return;
                int close = text.IndexOf('}', open);
                if (close < 0) { issues.Add(where + ": '{' without '}'"); return; }
                string inner = text.Substring(open + 1, close - open - 1);
                if (!TryResolveToken(inner, false, out _)) issues.Add(where + ": token {" + inner + "} does not resolve");
                i = close + 1;
            }
        }

        static int WordCount(string s) => string.IsNullOrWhiteSpace(s) ? 0 : s.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries).Length;

        static void WarnOnce(string key, string message)
        {
            if (s_warned.Add(key)) Debug.LogWarning(message);
        }

        static void Load()
        {
            if (s_data != null && !s_provisional) return;
            Data data = null;
            var path = JsonPath;
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) WarnOnce("file", "Glossary: could not load " + RelativePath.Substring(1) + ".");
            else
            {
                try { data = JsonUtility.FromJson<Data>(asset.text); }
                catch (System.Exception ex) { WarnOnce("parse", "Glossary: could not read the file (" + ex.Message + ")."); }
            }
            data ??= new Data();
            data.entries ??= new Entry[0];
            data.controls ??= new ControlCopy[0];
            s_strings = new Dictionary<string, string>();
            if (!s_englishOnly && !Loc.English) Overlay(data, Loc.Current);
            // Read while Unity builds a window, the language is not known yet: use English now, load again next time.
            s_provisional = !Loc.Resolved;
            s_data = data;
            s_entries = new Dictionary<string, Entry>();
            s_controls = new Dictionary<string, ControlCopy>();
            s_all = new List<Entry>();
            foreach (var e in data.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || s_entries.ContainsKey(e.id)) continue;
                s_entries[e.id] = e;
                s_all.Add(e);
            }
            s_all.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var c in data.controls)
                if (c != null && !string.IsNullOrEmpty(c.id) && !s_controls.ContainsKey(c.id)) s_controls[c.id] = c;

            s_tiers = ValidTiers(data.tiers) ? data.tiers : DefaultTiers;
            s_tierSizes = new int[TierCount + 1];
            s_counted = 0;
            var byEvent = new Dictionary<string, List<string>>();
            foreach (var e in s_all)
            {
                if (!e.bonus)
                {
                    s_counted++;
                    s_tierSizes[TierOf(e)]++;
                }
                if (e.doneBy == null) continue;
                foreach (var ev in e.doneBy)
                {
                    if (string.IsNullOrEmpty(ev)) continue;
                    if (!byEvent.TryGetValue(ev, out var list)) byEvent[ev] = list = new List<string>();
                    list.Add(e.id);
                }
            }
            s_doneBy = new Dictionary<string, string[]>();
            foreach (var kv in byEvent) s_doneBy[kv.Key] = kv.Value.ToArray();
        }

        // Lays the text of <lang>.json over the English data. Anything it does not have stays English.
        static void Overlay(Data en, string lang)
        {
            var asset = LanguageAsset(lang);
            if (asset == null) { WarnOnce("lang:" + lang, "Glossary: there is no " + lang + ".json, so the interface stays in English."); return; }
            Data tr;
            try { tr = JsonUtility.FromJson<Data>(asset.text); }
            catch (System.Exception ex) { WarnOnce("lang:" + lang, "Glossary: could not read " + lang + ".json (" + ex.Message + "). The interface stays in English."); return; }
            int missing = 0;
            string Pick(string english, string translated)
            {
                if (string.IsNullOrEmpty(english)) return english;
                if (string.IsNullOrEmpty(translated)) { missing++; return english; }
                return translated;
            }

            var tiers = new Dictionary<int, Tier>();
            if (tr.tiers != null) foreach (var t in tr.tiers) if (t != null) tiers[t.n] = t;
            if (en.tiers != null)
                foreach (var t in en.tiers)
                {
                    if (t == null) continue;
                    tiers.TryGetValue(t.n, out var x);
                    t.name = Pick(t.name, x?.name);
                    t.blurb = Pick(t.blurb, x?.blurb);
                    t.achievement = Pick(t.achievement, x?.achievement);
                }

            var entries = new Dictionary<string, Entry>();
            if (tr.entries != null) foreach (var e in tr.entries) if (e != null && e.id != null) entries[e.id] = e;
            foreach (var e in en.entries)
            {
                if (e == null || e.id == null) continue;
                entries.TryGetValue(e.id, out var x);
                e.title = Pick(e.title, x?.title);
                e.tip = Pick(e.tip, x?.tip);
                e.definition = Pick(e.definition, x?.definition);
                e.what = Pick(e.what, x?.what);
                e.when = Pick(e.when, x?.when);
                e.example = Pick(e.example, x?.example);
                e.tryIt = Pick(e.tryIt, x?.tryIt);
                e.watchOut = Pick(e.watchOut, x?.watchOut);
                e.whereInUnity = Pick(e.whereInUnity, x?.whereInUnity);
                if (e.alsoCalled != null && x?.alsoCalled != null && x.alsoCalled.Length == e.alsoCalled.Length) e.alsoCalled = x.alsoCalled;
            }

            var controls = new Dictionary<string, ControlCopy>();
            if (tr.controls != null) foreach (var c in tr.controls) if (c != null && c.id != null) controls[c.id] = c;
            foreach (var c in en.controls)
            {
                if (c == null || c.id == null) continue;
                controls.TryGetValue(c.id, out var x);
                c.label = Pick(c.label, x?.label);
                c.labelAdvanced = Pick(c.labelAdvanced, x?.labelAdvanced);
                c.tip = Pick(c.tip, x?.tip);
            }

            if (tr.strings != null)
                foreach (var s in tr.strings)
                    if (s != null && !string.IsNullOrEmpty(s.id) && !string.IsNullOrEmpty(s.text)) s_strings[s.id] = s.text;
            if (en.strings != null)
                foreach (var s in en.strings)
                    if (s != null && !string.IsNullOrEmpty(s.id) && !s_strings.ContainsKey(s.id)) missing++;

            if (missing > 0) WarnOnce("lang:" + lang, "Glossary: " + lang + ".json has no text for " + missing + " strings; they show in English.");
        }

        static bool ValidTiers(Tier[] t)
        {
            if (t == null || t.Length != TierCount) return false;
            for (int i = 0; i < t.Length; i++)
                if (t[i] == null || t[i].n != i + 1 || string.IsNullOrEmpty(t[i].name)) return false;
            return true;
        }
    }
}
