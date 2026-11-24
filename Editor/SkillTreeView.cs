using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal sealed partial class SkillTreeView
    {
        internal const float MedalsH = 24f, FooterH = 24f, SideMinWidth = 640f, RowsMaxWidth = 420f;

        const float MinNodeW = 132f, NodeH = 54f, SubH = 26f, Gap = 6f, Margin = 12f, TierH = 28f, RowH = 28f, RowGap = 2f;
        const float BlockGap = 10f, TopGap = 8f, ScrollBarW = 15f, RightZone = 70f;
        const int NextCount = 3;

        internal enum Mode { Side, Inline, Rows }

        struct NodeBox
        {
            public Glossary.Entry E;
            public Rect R;
            public GUIContent Title, SubNew, Tip;
            public float TitleW;
        }

        internal sealed class TierHead
        {
            public int Tier;
            public GUIContent Name, Blurb;
            public float NameW, BlurbX, Lead;
            public GUIContent Wrap;
            public float Extra;
        }

        sealed class Plan
        {
            public float Height;
            public TierHead Head;
            public NodeBox[] Nodes;
            public readonly Dictionary<string, int> Index = new Dictionary<string, int>();
        }

        readonly Action _repaint;
        readonly Dictionary<(int width, int tier, bool rows, bool advanced), Plan> _plans = new Dictionary<(int, int, bool, bool), Plan>();
        IReadOnlyList<Glossary.Entry> _gloss;

        internal string Selected;
        Vector2 _scroll, _cardScroll;
        string _cursor, _hover, _scrollTo;
        bool _scrollTop, _browseLessons, _cursorVisible;
        Plan _lastPlan;
        int _version, _cachedVersion = -1;

        string _cardId;
        float _cardW, _cardH;
        bool _cardAdv;


        readonly List<string> _next = new List<string>(NextCount);
        GUIContent[] _tierCount;
        GUIContent[] _medalTip;
        bool[] _tierComplete;
        float[] _tierFraction;
        GUIContent _medalsText, _nextLabel;
        readonly List<GUIContent> _nextLinks = new List<GUIContent>(NextCount);
        readonly List<float> _nextLinkW = new List<float>(NextCount);
        float _nextLabelW;
        string _webFor, _webUrl;
        GUIContent _webLabel, _webHover;
        float _webW;
        IReadOnlyList<Glossary.Entry> _webGloss;
        int _doneCount, _total;

        internal string ProgressText { get; private set; } = "";
        internal float ProgressFraction { get; private set; }
        internal string LastMode { get; private set; } = "";
        internal float LastHeight { get; private set; }

        static GUIContent s_done => new GUIContent(Loc.T("Done"));
        static GUIContent s_read => new GUIContent(Loc.T("Read"));
        static GUIContent s_nextUp => new GUIContent(Loc.T("Next up"));
        static readonly GUIContent s_emptyInline = new GUIContent("");

        public SkillTreeView(Action repaint) { _repaint = repaint; }

        internal void OnLearnChanged()
        {
            _version++;
            _repaint?.Invoke();
        }


        static GUIStyle s_tierBlurbWrap, s_nodeTitle, s_nodeSub, s_nodeSubWrap, s_nodeNext, s_tierName, s_tierBlurb, s_count, s_countDone, s_medals, s_footer, s_link;
        static bool s_stylesPro;

        static GUIStyle Plain(int size, Color colour, FontStyle weight, TextAnchor anchor)
        {
            var s = new GUIStyle(EditorStyles.label)
            {
                fontSize = size,
                fontStyle = weight,
                alignment = anchor,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                margin = new RectOffset(),
                padding = new RectOffset(),
            };
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = colour;
            return s;
        }

        static void Styles()
        {
            if (s_nodeTitle != null && s_stylesPro == EditorGUIUtility.isProSkin) return;
            s_stylesPro = EditorGUIUtility.isProSkin;
            s_nodeTitle = Plain(12, StudioGUI.CardText, FontStyle.Bold, TextAnchor.MiddleLeft);
            s_nodeSub = Plain(10, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_nodeNext = Plain(10, StudioGUI.CardAccent, FontStyle.Bold, TextAnchor.MiddleLeft);
            s_nodeSubWrap = Plain(10, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.UpperLeft);
            s_nodeSubWrap.wordWrap = true;
            s_tierName = Plain(11, StudioGUI.CardText, FontStyle.Bold, TextAnchor.MiddleLeft);
            s_tierBlurb = Plain(11, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_tierBlurbWrap = Plain(11, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.UpperLeft);
            s_tierBlurbWrap.wordWrap = true;
            s_count = Plain(11, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleRight);
            s_countDone = Plain(11, StudioGUI.CardGood, FontStyle.Bold, TextAnchor.MiddleRight);
            s_medals = Plain(11, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_footer = Plain(11, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_link = Plain(11, StudioGUI.CardAccent, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static GUIContent Fit(string text, GUIStyle style, float maxW)
        {
            if (string.IsNullOrEmpty(text) || maxW <= 8f) return new GUIContent("");
            var c = new GUIContent(text);
            if (style.CalcSize(c).x <= maxW) return c;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, mid).TrimEnd() + "…")).x <= maxW) lo = mid; else hi = mid - 1;
            }
            c.text = lo > 0 ? text.Substring(0, lo).TrimEnd() + "…" : "…";
            return c;
        }

        static GUIContent FitWrapped(string text, GUIStyle style, float maxW, float maxH)
        {
            if (string.IsNullOrEmpty(text) || maxW <= 8f) return new GUIContent("");
            var c = new GUIContent(text);
            if (style.CalcHeight(c, maxW) <= maxH) return c;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcHeight(new GUIContent(text.Substring(0, mid).TrimEnd() + "…"), maxW) <= maxH) lo = mid; else hi = mid - 1;
            }
            c.text = lo > 0 ? text.Substring(0, lo).TrimEnd() + "…" : "…";
            return c;
        }


        internal void Refresh()
        {
            var all = Glossary.All;
            if (!ReferenceEquals(all, _gloss))
            {
                _gloss = all;
                int slots = Glossary.TierCount + 1;
                _tierCount = new GUIContent[slots];
                _medalTip = new GUIContent[slots];
                _tierComplete = new bool[slots];
                _tierFraction = new float[slots];
                _cardId = null;
                _plans.Clear();
                _version++;
            }
            if (_cachedVersion == _version) return;
            _cachedVersion = _version;
            Styles();

            _doneCount = Learn.DoneCount();
            _total = Glossary.CountedCount;
            ProgressText = Loc.F("{0} of {1}", _doneCount, _total);
            ProgressFraction = _total > 0 ? Mathf.Clamp01(_doneCount / (float)_total) : 0f;

            var medals = new StringBuilder();
            int earned = 0;
            for (int t = 1; t <= Glossary.TierCount; t++)
            {
                int size = Glossary.TierSize(t), done = Learn.TierDoneCount(t);
                _tierFraction[t] = size > 0 ? done / (float)size : 0f;
                _tierComplete[t] = size > 0 && done >= size;
                _tierCount[t] = new GUIContent(done + "/" + size);
                string name = MedalName(t);
                if (_tierComplete[t])
                {
                    if (earned++ > 0) medals.Append(" · ");
                    medals.Append(name);
                    _medalTip[t] = new GUIContent("", Loc.F("Medal: {0}", name));
                }
                else _medalTip[t] = new GUIContent("", Loc.F("Finish all {0} skills in tier {1} to earn the medal “{2}”.", size, t, name));
            }
            bool graduate = Learn.IsGraduate;
            if (Learn.HasGraduateMedal) { if (earned++ > 0) medals.Append(" · "); medals.Append(Learn.GraduateName); }
            _medalsText = new GUIContent(earned == 0 ? Loc.T("No medals yet. Finish a tier to earn one.") : Loc.F("Medals: {0}", medals));

            Learn.NextUp(_next, NextCount);
            _nextLinks.Clear();
            _nextLinkW.Clear();
            _nextLabel = new GUIContent(_next.Count > 0 ? Loc.T("Next up:") : (graduate ? Loc.F("{0}: all {1} skills done.", Learn.GraduateName, _total) : Loc.T("Nothing left to try in the tree.")));
            _nextLabelW = s_footer.CalcSize(_nextLabel).x;
            foreach (var id in _next)
            {
                var e = Glossary.Get(id);
                var c = new GUIContent(e != null ? e.title : id);
                _nextLinks.Add(c);
                _nextLinkW.Add(s_link.CalcSize(c).x);
            }
        }

        static string MedalName(int tier)
        {
            var t = Glossary.TierInfo(tier);
            string a = t != null ? t.achievement : null;
            if (string.IsNullOrEmpty(a)) return t != null ? t.name : Loc.F("Tier {0}", tier);
            const string prefix = "Medal: ";
            return a.StartsWith(prefix, StringComparison.Ordinal) ? a.Substring(prefix.Length) : a;
        }

        internal GUIContent CountOf(int tier) => tier >= 1 && tier <= Glossary.TierCount ? _tierCount[tier] : s_emptyInline;
        internal bool IsComplete(int tier) => tier >= 1 && tier <= Glossary.TierCount && _tierComplete[tier];


        internal static bool Matches(Glossary.Entry e, string q, bool advanced)
        {
            if (string.IsNullOrEmpty(q)) return true;
            if (Has(e.id, q) || Has(e.whereInUnity, q) || Has(e.title, q) || Has(e.definition, q) || Has(e.what, q) || Has(e.when, q)) return true;
            if (Has(Glossary.ResolveTokens(e.example, advanced), q) || Has(Glossary.ResolveTokens(e.tryIt, advanced), q) || Has(Glossary.ResolveTokens(e.watchOut, advanced), q)) return true;
            if (e.alsoCalled != null) foreach (var a in e.alsoCalled) if (Has(a, q)) return true;
            if (e.aliases != null) foreach (var a in e.aliases) if (Has(a, q)) return true;
            return false;
        }

        static bool Has(string s, string q) => s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        internal void Select(string conceptId, bool toTop)
        {
            if (!Glossary.Has(conceptId)) return;
            Selected = conceptId;
            _cursor = conceptId;
            _cardScroll = Vector2.zero;
            _scrollTo = conceptId;
            _scrollTop = toTop;
            _browseLessons = false;
            Learn.MarkRead(conceptId);
            _repaint?.Invoke();
        }

        internal void ShowChapters()
        {
            Selected = null;
            _lastPlan = null;
            _browseLessons = false;
            _repaint?.Invoke();
        }

        static Mode ModeFor(float width) => width >= SideMinWidth ? Mode.Side : width < RowsMaxWidth ? Mode.Rows : Mode.Inline;


        internal TierHead MakeHead(int tier, float width, float lead)
        {
            Styles();
            var h = new TierHead { Tier = tier, Lead = lead };
            string name, blurb;
            if (tier == 0) { name = Loc.T("Optional lessons"); blurb = ""; }
            else
            {
                var t = Glossary.TierInfo(tier);
                name = tier + "  " + (t != null && t.name != null ? t.name.ToUpperInvariant() : "");
                blurb = t != null ? t.blurb : "";
            }
            h.Name = new GUIContent(name);
            h.NameW = s_tierName.CalcSize(h.Name).x + 2f;
            h.BlurbX = Margin + lead + h.NameW + 12f;
            float avail = width - h.BlurbX - RightZone - 8f;
            if (!string.IsNullOrEmpty(blurb))
            {
                var full = new GUIContent(blurb);
                if (avail >= 48f && s_tierBlurb.CalcSize(full).x <= avail) h.Blurb = full;
                else
                {
                    h.Wrap = full;
                    h.Extra = Mathf.Ceil(s_tierBlurbWrap.CalcHeight(full, Mathf.Max(60f, width - Margin * 2f - lead))) - 4f;
                }
            }
            return h;
        }

        Plan GetPlan(int width, bool rows)
        {
            bool adv = StudioMode.Advanced;
            var chapter = Glossary.Get(Selected);
            int tier = chapter.bonus ? 0 : Glossary.TierOf(chapter);
            var key = (width, tier, rows, adv);
            if (_plans.TryGetValue(key, out var plan)) return plan;
            if (_plans.Count > 16) _plans.Clear();
            plan = Build(width, rows, adv, tier);
            _plans[key] = plan;
            return plan;
        }

        Plan Build(int width, bool rows, bool adv, int chapter)
        {
            Styles();
            var plan = new Plan();
            var nodes = new List<NodeBox>();
            foreach (var e in _gloss)
            {
                if (chapter == 0 ? e.bonus : !e.bonus && Glossary.TierOf(e) == chapter)
                    nodes.Add(new NodeBox { E = e, Title = new GUIContent(e.title), Tip = new GUIContent("", Glossary.ResolveTokens(e.tip, adv)) });
            }
            plan.Head = MakeHead(chapter, width, 0f);
            float avail = width - Margin * 2f;
            float minW = MinNodeW;
            foreach (var n in nodes) minW = Mathf.Max(minW, s_nodeTitle.CalcSize(n.Title).x + 36f);
            int cols = rows || chapter == 0 ? 1 : Mathf.Max(1, Mathf.FloorToInt((avail + Gap) / (minW + Gap)));
            float nodeW = (avail - (cols - 1) * Gap) / cols;
            float nodeH = rows ? RowH : NodeH;
            float rowGap = rows ? RowGap : Gap;
            float y = TopGap + TierH + plan.Head.Extra + 4f;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                n.R = new Rect(Margin + (i % cols) * (nodeW + Gap), y + (i / cols) * (nodeH + rowGap), nodeW, nodeH);
                FitNode(ref n, n.E, nodeW, rows, adv);
                nodes[i] = n;
            }
            plan.Height = y + Mathf.CeilToInt(nodes.Count / (float)cols) * (nodeH + rowGap) + BlockGap;
            plan.Nodes = nodes.ToArray();
            for (int i = 0; i < plan.Nodes.Length; i++) plan.Index[plan.Nodes[i].E.id] = i;
            return plan;
        }

        static void FitNode(ref NodeBox box, Glossary.Entry e, float w, bool rows, bool adv)
        {
            string snippet = e.bonus ? e.tip : Glossary.ResolveTokens(e.tryIt, adv);
            if (string.IsNullOrEmpty(snippet)) snippet = e.bonus ? "" : Loc.T("Not started");
            if (rows)
            {
                float natural = s_nodeTitle.CalcSize(box.Title).x + 2f;
                float titleW = Mathf.Min(natural, (w - 34f) * 0.62f);
                if (titleW < natural) box.Title = Fit(e.title, s_nodeTitle, titleW);
                box.TitleW = titleW;
                box.SubNew = Fit(snippet, s_nodeSub, w - 34f - titleW - 8f);
            }
            else
            {
                float textW = w - 34f;
                box.Title = Fit(e.title, s_nodeTitle, textW);
                box.TitleW = textW;
                box.SubNew = FitWrapped(snippet, s_nodeSubWrap, textW, SubH);
            }
        }

        float CardHeight(Glossary.Entry e, float width)
        {
            bool adv = StudioMode.Advanced;
            if (_cardId == e.id && Mathf.Approximately(_cardW, width) && _cardAdv == adv) return _cardH;
            _cardId = e.id;
            _cardW = width;
            _cardAdv = adv;
            _cardH = Mathf.Ceil(ConceptCard.Layout(e, new Rect(0, 0, width, 0), CardMode.Detail, false));
            return _cardH;
        }


        internal void Draw(Rect area)
        {
            Styles();
            Refresh();
            var mode = ModeFor(area.width);
            LastMode = mode == Mode.Side ? "side" : mode == Mode.Rows ? "rows" : "inline";
            if (Selected == null || !Glossary.Has(Selected))
            {
                DrawChapters(area);
                return;
            }

            var nav = new Rect(area.x, area.y, area.width, 36f);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(nav, StudioGUI.CardInset);
            var back = new Rect(nav.x + Margin, nav.y + 5f, 126f, 26f);
            if (GUI.Button(back, "‹  " + Loc.T("All chapters"), EditorStyles.miniButton))
            {
                ShowChapters();
                return;
            }
            if (mode != Mode.Side)
            {
                var choose = new Rect(back.xMax + 6f, back.y, 100f, back.height);
                if (GUI.Button(choose, Loc.T("Lessons"), EditorStyles.miniButton))
                {
                    _browseLessons = !_browseLessons;
                    _scrollTo = Selected;
                    _repaint?.Invoke();
                }
            }
            DrawFooter(new Rect(area.x, area.yMax - FooterH, area.width, FooterH));
            var body = new Rect(area.x, nav.yMax, area.width, Mathf.Max(0f, area.height - nav.height - FooterH));

            if (mode == Mode.Side)
            {
                float detailWidth = Mathf.Clamp(body.width * .55f, 330f, 720f);
                var tree = new Rect(body.x, body.y, body.width - detailWidth - 1f, body.height);
                DrawTree(tree, false);
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(tree.xMax, body.y, 1f, body.height), StudioGUI.CardBorder);
                DrawDetail(new Rect(tree.xMax + 1f, body.y, detailWidth, body.height));
            }
            else if (_browseLessons) DrawTree(body, true);
            else DrawDetail(body);

            if (mode == Mode.Side || _browseLessons) HandleKeys();
        }

        void DrawMedals(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.CardBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), StudioGUI.CardDivider);
            }
            float x = r.x + Margin;
            for (int t = 1; t <= Glossary.TierCount; t++)
            {
                var m = new Rect(x, r.y + 4f, 16f, 16f);
                StudioGUI.DrawMedalGlyph(m, _tierComplete[t] ? StudioGUI.MedalGold : Fade(StudioGUI.CardTextDim, 0.45f));
                if (Event.current.type == EventType.Repaint) GUI.Label(m, _medalTip[t], GUIStyle.none);
                x += 20f;
            }
            if (Event.current.type == EventType.Repaint && _medalsText != null)
                GUI.Label(new Rect(x + 6f, r.y, Mathf.Max(0f, r.xMax - x - 6f - Margin), r.height), _medalsText, s_medals);
        }

        void DrawFooter(Rect r)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.CardBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), StudioGUI.CardDivider);
            }
            float limit = r.xMax - Margin;
            string web = r.width >= RowsMaxWidth ? WebLink() : null;
            if (web != null) limit -= _webW + 12f;
            if (ev.type == EventType.Repaint)
                GUI.Label(new Rect(r.x + Margin, r.y, Mathf.Max(0f, limit - r.x - Margin), r.height), _nextLabel, s_footer);
            if (web != null)
            {
                var wr = new Rect(limit + 12f, r.y + 2f, _webW, r.height - 4f);
                if (ev.type == EventType.Repaint)
                {
                    bool hover = wr.Contains(ev.mousePosition);
                    GUI.Label(new Rect(wr.x, wr.y, _webW - 14f, wr.height), _webLabel, s_link);
                    StudioGUI.DrawExternalGlyph(new Rect(wr.xMax - 10f, wr.y + (wr.height - 10f) * 0.5f, 10f, 10f), StudioGUI.CardAccent);
                    if (hover) EditorGUI.DrawRect(new Rect(wr.x, wr.yMax - 3f, _webW - 14f, 1f), StudioGUI.CardAccent);
                    GUI.Label(wr, _webHover, GUIStyle.none);
                    EditorGUIUtility.AddCursorRect(wr, MouseCursor.Link);
                }
                else if (ev.type == EventType.MouseDown && ev.button == 0 && wr.Contains(ev.mousePosition))
                {
                    ev.Use();
                    OnlineGuide.Open(web);
                }
            }
            float x = r.x + Margin + _nextLabelW + 6f;
            for (int i = 0; i < _nextLinks.Count; i++)
            {
                float w = _nextLinkW[i] + 2f;
                var lr = new Rect(x, r.y + 2f, w, r.height - 4f);
                if (lr.xMax > limit) break;
                if (ev.type == EventType.Repaint)
                {
                    GUI.Label(lr, _nextLinks[i], s_link);
                    EditorGUIUtility.AddCursorRect(lr, MouseCursor.Link);
                    if (lr.Contains(ev.mousePosition)) EditorGUI.DrawRect(new Rect(lr.x, lr.yMax - 3f, lr.width, 1f), StudioGUI.CardAccent);
                }
                else if (ev.type == EventType.MouseDown && ev.button == 0 && lr.Contains(ev.mousePosition))
                {
                    ev.Use();
                    GUI.FocusControl(null);
                    Select(_next[i], true);
                }
                x += w;
                if (i < _nextLinks.Count - 1)
                {
                    if (ev.type == EventType.Repaint) GUI.Label(new Rect(x, r.y, 14f, r.height), "·", s_footer);
                    x += 14f;
                }
            }
        }

        internal string WebLink()
        {
            string key = Selected ?? "";
            if (_webLabel != null && _webFor == key && ReferenceEquals(_webGloss, Glossary.All)) return _webUrl;
            _webFor = key;
            _webGloss = Glossary.All;
            _webUrl = OnlineGuide.SkillUrl(Glossary.Has(Selected) ? Selected : null);
            _webLabel = new GUIContent(OnlineGuide.Label("web.tree", "Skill tree online"));
            string tip = OnlineGuide.Tip("web.tree");
            _webHover = new GUIContent("", string.IsNullOrEmpty(tip) ? _webUrl : tip + "\n" + _webUrl);
            _webW = s_link.CalcSize(_webLabel).x + 14f;
            return _webUrl;
        }

        void DrawTree(Rect area, bool rows)
        {
            var ev = Event.current;
            int width = Mathf.FloorToInt(area.width - ScrollBarW);
            var plan = GetPlan(width, rows);
            LastHeight = plan.Height;
            _lastPlan = plan;
            if (_scrollTo != null) ResolveScroll(plan, area.height);

            if (ev.type == EventType.Repaint) EditorGUI.DrawRect(area, StudioGUI.CardBg);
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, width, plan.Height));
            float top = _scroll.y - 60f, bottom = _scroll.y + area.height + 60f;

            if (ev.type == EventType.Repaint || ev.type == EventType.MouseMove || ev.type == EventType.MouseDown)
            {
                string over = null;
                for (int i = 0; i < plan.Nodes.Length && over == null; i++)
                {
                    var r = plan.Nodes[i].R;
                    if (r.width <= 0f) continue;

                    if (r.Contains(ev.mousePosition) && InView(area, ev.mousePosition)) over = plan.Nodes[i].E.id;
                }
                if (over != _hover)
                {
                    _hover = over;
                    if (ev.type == EventType.MouseMove) _repaint?.Invoke();
                }
            }
            if (ev.type == EventType.MouseLeaveWindow && _hover != null) { _hover = null; _repaint?.Invoke(); }

            DrawHead(new Rect(0f, TopGap, width, TierH + plan.Head.Extra), plan.Head);
            if (ev.type == EventType.Repaint && !rows) DrawEdges(plan, _hover ?? Selected);

            for (int i = 0; i < plan.Nodes.Length; i++)
            {
                ref var n = ref plan.Nodes[i];
                if (n.R.width <= 0f) continue;
                var r = n.R;

                if (r.yMax < top || r.y > bottom) continue;
                string id = n.E.id;
                bool isSel = id == Selected;
                bool isNext = _next.Contains(id);
                DrawNode(ref n, r, isSel, isNext, id == _hover, _cursorVisible && id == _cursor, rows);

                if (ev.type == EventType.Repaint) EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                else if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition))
                {
                    ev.Use();
                    GUI.FocusControl(null);
                    _cursorVisible = false;
                    Select(id, false);
                    if (ev.clickCount >= 2 && CanShowMe(n.E)) RunShowMe(id);
                }
            }

            GUI.EndScrollView();
        }

        bool InView(Rect view, Vector2 contentMouse)
        {
            float my = contentMouse.y - _scroll.y;
            return my >= 0f && my <= view.height;
        }

        void ResolveScroll(Plan plan, float viewH)
        {
            string id = _scrollTo;
            _scrollTo = null;
            if (!plan.Index.TryGetValue(id, out int i) || plan.Nodes[i].R.width <= 0f) return;
            var r = plan.Nodes[i].R;

            if (_scrollTop) _scroll.y = Mathf.Max(0f, r.y - TierH - 12f);
            else if (r.y < _scroll.y + TierH) _scroll.y = Mathf.Max(0f, r.y - TierH - 12f);
            else if (r.yMax > _scroll.y + viewH - 8f) _scroll.y = r.yMax - viewH + 12f;

        }

        void DrawEdges(Plan plan, string focus)
        {
            if (focus == null || !plan.Index.TryGetValue(focus, out int fi)) return;
            var f = plan.Nodes[fi];
            if (f.R.width <= 0f || f.E.requires == null) return;
            var to = f.R;

            foreach (var rid in f.E.requires)
            {
                if (!plan.Index.TryGetValue(rid, out int ri) || plan.Nodes[ri].R.width <= 0f) continue;
                var from = plan.Nodes[ri].R;

                var req = plan.Nodes[ri].E;
                var colour = Learn.IsDone(rid) ? Fade(StudioGUI.TierColour(Glossary.TierOf(req)), 0.75f) : Fade(StudioGUI.CardTextDim, 0.4f);
                Vector3 a, b, ta, tb;
                if (from.yMax <= to.y) { a = new Vector3(from.center.x, from.yMax); b = new Vector3(to.center.x, to.y); ta = a + Vector3.up * 24f; tb = b + Vector3.down * 24f; }
                else if (from.y >= to.yMax) { a = new Vector3(from.center.x, from.y); b = new Vector3(to.center.x, to.yMax); ta = a + Vector3.down * 24f; tb = b + Vector3.up * 24f; }
                else if (from.x <= to.x) { a = new Vector3(from.xMax, from.center.y); b = new Vector3(to.x, to.center.y); ta = a + Vector3.right * 16f; tb = b + Vector3.left * 16f; }
                else { a = new Vector3(from.x, from.center.y); b = new Vector3(to.xMax, to.center.y); ta = a + Vector3.left * 16f; tb = b + Vector3.right * 16f; }
                Handles.DrawBezier(a, b, ta, tb, colour, null, 1.5f);
            }
        }

        internal void DrawHead(Rect r, TierHead h)
        {
            if (Event.current.type != EventType.Repaint) return;
            Styles();
            int tier = h.Tier;
            EditorGUI.DrawRect(new Rect(r.x, r.y + 2f, 4f, r.height - 4f), tier == 0 ? StudioGUI.CardBorder : StudioGUI.TierColour(tier));
            GUI.Label(new Rect(r.x + Margin + h.Lead, r.y, h.NameW, TierH), h.Name, s_tierName);
            if (h.Blurb != null) GUI.Label(new Rect(r.x + h.BlurbX, r.y, Mathf.Max(0f, r.width - h.BlurbX - RightZone - 8f), TierH), h.Blurb, s_tierBlurb);
            else if (h.Wrap != null) GUI.Label(new Rect(r.x + Margin + h.Lead, r.y + TierH - 6f, Mathf.Max(0f, r.width - Margin * 2f - h.Lead), h.Extra + 4f), h.Wrap, s_tierBlurbWrap);
            if (tier > 0)
            {
                bool done = _tierComplete[tier];
                var count = new Rect(r.xMax - Margin - 34f, r.y, 34f, TierH);
                GUI.Label(count, _tierCount[tier], done ? s_countDone : s_count);
                if (done) StudioGUI.DrawMedalGlyph(new Rect(count.x - 20f, r.y + (TierH - 16f) * 0.5f, 16f, 16f), StudioGUI.MedalGold);
            }
        }

        void DrawNode(ref NodeBox n, Rect r, bool selected, bool next, bool hover, bool cursor, bool rows)
        {
            var ev = Event.current;
            if (ev.type != EventType.Repaint) return;
            var e = n.E;
            int tier = e.bonus ? 0 : Glossary.TierOf(e);
            var state = Learn.StateOf(e.id);
            var fill = hover ? Color.Lerp(StudioGUI.CardInset, StudioGUI.CardBorder, .4f) : StudioGUI.CardInset;
            StudioGUI.DrawRounded(r, fill, 6f);
            if (selected) StudioGUI.DrawRoundedBorder(r, tier > 0 ? StudioGUI.TierColour(tier) : StudioGUI.CardAccent, 6f, 2f);
            else if (next) StudioGUI.DrawRoundedBorder(r, StudioGUI.CardAccent, 6f, 2f);
            else StudioGUI.DrawRoundedBorder(r, StudioGUI.CardBorder, 6f, 1f);
            if (cursor) StudioGUI.DrawRoundedBorder(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Fade(StudioGUI.CardText, .8f), 8f, 1f);
            Color glyph = state == Learn.State.Done ? StudioGUI.CardGood : state == Learn.State.Read ? (tier > 0 ? StudioGUI.TierColour(tier) : StudioGUI.CardAccent) : StudioGUI.CardTextDim;
            float gy = rows ? r.y + (r.height - 12f) * .5f : r.y + 9f;
            StudioGUI.DrawStateGlyph(new Rect(r.x + 8f, gy, 12f, 12f), state, glyph);
            var sub = state == Learn.State.Done ? s_done : state == Learn.State.Read ? (next ? s_nextUp : s_read) : (next ? s_nextUp : n.SubNew);
            var subStyle = next && state != Learn.State.Done ? s_nodeNext : s_nodeSub;
            if (rows)
            {
                GUI.Label(new Rect(r.x + 26f, r.y, n.TitleW + 2f, r.height), n.Title, s_nodeTitle);
                GUI.Label(new Rect(r.x + 26f + n.TitleW + 8f, r.y, Mathf.Max(0f, r.width - 34f - n.TitleW - 8f), r.height), sub, subStyle);
            }
            else
            {
                GUI.Label(new Rect(r.x + 26f, r.y + 6f, r.width - 34f, 16f), n.Title, s_nodeTitle);
                if (ReferenceEquals(sub, n.SubNew)) GUI.Label(new Rect(r.x + 26f, r.y + 24f, r.width - 34f, SubH), sub, s_nodeSubWrap);
                else GUI.Label(new Rect(r.x + 26f, r.y + 24f, r.width - 34f, 14f), sub, subStyle);
            }
            GUI.Label(r, n.Tip, GUIStyle.none);
        }


        void DrawDetail(Rect area)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint) EditorGUI.DrawRect(area, StudioGUI.CardBg);
            var e = Glossary.Get(Selected);
            float cw = area.width - ScrollBarW;
            float h = CardHeight(e, cw);
            if (area.width < SideMinWidth) LastHeight = h;
            var res = default(CardResult);
            _cardScroll = GUI.BeginScrollView(area, _cardScroll, new Rect(0, 0, cw, h));
            ConceptCard.Layout(e, new Rect(0, 0, cw, 0), CardMode.Detail, true, ref res);
            GUI.EndScrollView();
            Act(res, e.id);
        }

        void Act(CardResult res, string conceptId)
        {
            if (res.GoTo != null) Select(res.GoTo, true);
            else if (res.ShowMe) RunShowMe(conceptId);
        }

        static bool CanShowMe(Glossary.Entry e) => ConceptCard.ShowMeHandler != null && !string.IsNullOrEmpty(e.tryIt) && (!string.IsNullOrEmpty(e.tryTab) || !string.IsNullOrEmpty(e.tryControl));

        static void RunShowMe(string conceptId)
        {
            EditorApplication.delayCall += () => ConceptCard.ShowMeHandler?.Invoke(conceptId);
        }


        void HandleKeys()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown || ev.alt || ev.control || ev.command) return;
            if (GUI.GetNameOfFocusedControl() != "") return;
            var plan = _lastPlan;
            if (plan == null) return;
            switch (ev.keyCode)
            {
                case KeyCode.LeftArrow: Step(plan, -1, 0); break;
                case KeyCode.RightArrow: Step(plan, 1, 0); break;
                case KeyCode.UpArrow: Step(plan, 0, -1); break;
                case KeyCode.DownArrow: Step(plan, 0, 1); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_cursor != null) Select(_cursor, false);
                    break;
                default: return;
            }
            ev.Use();
        }


        void Step(Plan plan, int dx, int dy)
        {
            int cur = -1;
            if (_cursor != null) plan.Index.TryGetValue(_cursor, out cur);
            else if (Selected != null && !plan.Index.TryGetValue(Selected, out cur)) cur = -1;
            if (_cursor == null && cur < 0 && _next.Count > 0 && !plan.Index.TryGetValue(_next[0], out cur)) cur = -1;
            if (cur < 0 || plan.Nodes[cur].R.width <= 0f)
            {
                for (int i = 0; i < plan.Nodes.Length; i++) if (plan.Nodes[i].R.width > 0f) { cur = i; break; }
                if (cur < 0) return;
                SetCursor(plan, cur);
                return;
            }
            int best = -1;
            if (dx != 0)
            {
                for (int i = cur + dx; i >= 0 && i < plan.Nodes.Length; i += dx)
                    if (plan.Nodes[i].R.width > 0f) { best = i; break; }
            }
            else
            {
                var c = plan.Nodes[cur].R.center;
                float bestScore = float.MaxValue;
                for (int i = 0; i < plan.Nodes.Length; i++)
                {
                    var r = plan.Nodes[i].R;
                    if (i == cur || r.width <= 0f) continue;
                    float ddy = (r.center.y - c.y) * dy;
                    if (ddy < 1f) continue;
                    float score = ddy * 3f + Mathf.Abs(r.center.x - c.x);
                    if (score < bestScore) { bestScore = score; best = i; }
                }
            }
            if (best >= 0) SetCursor(plan, best);
        }

        void SetCursor(Plan plan, int index)
        {
            _cursorVisible = true;
            _cursor = plan.Nodes[index].E.id;
            _scrollTo = _cursor;
            _scrollTop = false;
            _repaint?.Invoke();
        }
    }
}
