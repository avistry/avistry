using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal class GlossaryWindow : EditorWindow
    {
        internal enum View { Tree = 0, AZ = 1 }

        const string SearchName = "avistry-glossary-search";
        const float ToolbarH = 38f, NoticeH = 24f, ScrollBarW = 15f, DefaultW = 760f, DefaultH = 560f;

        [SerializeField] int _view;
        [SerializeField] string _search = "";
        [SerializeField] string _selected;
        [SerializeField] string _azOpen;
        [SerializeField] bool _sortByName = true, _sized;

        [NonSerialized] SkillTreeView _tree;
        [NonSerialized] bool _resetNotice;
        [NonSerialized] Vector2 _azScroll;
        [NonSerialized] string _azScrollTo;

        static GlossaryWindow s_instance;

        struct AzItem
        {
            public Glossary.Entry E;
            public SkillTreeView.TierHead Head;
            public float Y, H, HeadH;
            public int DisclosureId;
        }

        [NonSerialized] List<AzItem> _az;
        [NonSerialized] float _azH;
        [NonSerialized] int _azCount;
        [NonSerialized] int _azKeyW;
        [NonSerialized] string _azKeyOpen, _azKeySearch;
        [NonSerialized] bool _azKeySort, _azKeyAdv;
        [NonSerialized] IReadOnlyList<Glossary.Entry> _azKeyGloss;

        [MenuItem("Tools/Avistry/Skill tree", false, 52)]
        static void OpenTreeMenu()
        {
            var window = OpenIn(null, View.Tree);
            window._selected = null;
            window._tree.ShowChapters();
        }

        [MenuItem("Tools/Avistry/Glossary", false, 53)]
        static void OpenGlossaryMenu()
        {
            var window = OpenIn(null, View.AZ);
            window._azOpen = window._azScrollTo = null;
            window._azScroll = Vector2.zero;
        }

        public static void Open(string conceptId = null) => OpenIn(conceptId, null);

        public static void Open(string conceptId, View view) => OpenIn(conceptId, view);

        static GlossaryWindow OpenIn(string conceptId, View? view)
        {
            var w = GetWindow<GlossaryWindow>(Loc.T("Glossary"));
            w.titleContent = new GUIContent(Loc.T("Glossary"), DiagnosticsReport.Logo);
            w.minSize = new Vector2(360, 280);
            if (!w._sized)
            {
                w._sized = true;
                var p = w.position;
                w.position = new Rect(p.x, p.y, DefaultW, DefaultH);
            }
            if (view.HasValue) { w._view = (int)view.Value; w._search = ""; }
            else if (!string.IsNullOrEmpty(conceptId)) w._view = (int)View.Tree;
            if (!string.IsNullOrEmpty(conceptId) && Glossary.Has(conceptId)) w.GoTo(conceptId);
            w.Repaint();
            return w;
        }

        internal static GlossaryWindow OpenSized(View view, int width, int height)
        {
            var w = OpenIn(null, view);
            var p = w.position;
            w.position = new Rect(p.x, p.y, width, height);
            return w;
        }

        internal static GlossaryWindow Instance => s_instance;

        internal string Describe() =>
            "view=" + (View)_view + " w=" + Mathf.RoundToInt(position.width) + " mode=" + (_view == (int)View.Tree ? _tree.LastMode : "list") +
            " h=" + Mathf.RoundToInt(_view == (int)View.Tree ? _tree.LastHeight : _azH) + " selected=" + (_selected ?? "");

        void OnEnable()
        {
            s_instance = this;
            minSize = new Vector2(360, 280);
            wantsMouseMove = true;
            _tree = new SkillTreeView(Repaint) { Selected = _selected };
            Learn.Changed += OnLearnChanged;
            StudioMode.Changed += Repaint;
        }

        void OnDisable()
        {
            if (s_instance == this) s_instance = null;
            Learn.Changed -= OnLearnChanged;
            StudioMode.Changed -= Repaint;
        }

        void OnLearnChanged() => _tree?.OnLearnChanged();

        void GoTo(string conceptId)
        {
            _search = "";
            if (Event.current != null) GUI.FocusControl(null);
            if (_view == (int)View.Tree) { _tree.Select(conceptId, true); _selected = _tree.Selected; }
            else OpenRow(conceptId);
        }

        void OpenRow(string conceptId)
        {
            _azOpen = conceptId;
            _azScrollTo = conceptId;
            Learn.MarkRead(conceptId);
            Repaint();
        }


        static GUIStyle s_count, s_placeholder, s_notice, s_tab;
        static bool s_stylesPro;

        static void Styles()
        {
            if (s_count != null && s_stylesPro == EditorGUIUtility.isProSkin) return;
            s_stylesPro = EditorGUIUtility.isProSkin;
            s_count = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            s_placeholder = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            s_placeholder.normal.textColor = StudioGUI.TextDim;
            s_notice = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            s_notice.normal.textColor = StudioGUI.CardText;
            s_tab = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            s_tab.normal.textColor = StudioGUI.CardText;
        }


        void OnGUI()
        {
            Styles();
            var ev = Event.current;
            if (_tree == null) _tree = new SkillTreeView(Repaint) { Selected = _selected };
            _tree.Refresh();
            HandleGlobalKeys(ev);
            if (ev.type == EventType.MouseMove && _view == (int)View.Tree) Repaint();

            float w = position.width, h = position.height;
            DrawToolbar(new Rect(0, 0, w, ToolbarH));
            float y = ToolbarH;
            if (_resetNotice)
            {
                DrawNotice(new Rect(0, y, w, NoticeH));
                y += NoticeH;
            }
            var body = new Rect(0, y, w, Mathf.Max(0f, h - y));
            if (_view == (int)View.Tree && string.IsNullOrWhiteSpace(_search))
            {
                _tree.Draw(body);
                _selected = _tree.Selected;
            }
            else DrawAZ(body);
        }

        static void HandleGlobalKeys(Event ev)
        {
            if (ev.type != EventType.KeyDown) return;
            bool inSearch = GUI.GetNameOfFocusedControl() == SearchName;
            var win = s_instance;
            if (win == null) return;
            if (!inSearch && (ev.character == '/' || ev.keyCode == KeyCode.Slash || ((ev.control || ev.command) && ev.keyCode == KeyCode.F)))
            {
                EditorGUI.FocusTextInControl(SearchName);
                ev.Use();
            }
            else if (ev.keyCode == KeyCode.Escape && (inSearch || win._search.Length > 0))
            {
                win._search = "";
                GUI.FocusControl(null);
                ev.Use();
                win.Repaint();
            }
        }

        void DrawToolbar(Rect r)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint) EditorStyles.toolbar.Draw(r, false, false, false, false);
            var section = new Rect(r.x + 7f, r.y + 6f, 84f, r.height - 12f);
            ViewButton(section, Loc.T("Skill tree"), View.Tree);
            section.x += section.width + 3f;
            section.width = 82f;
            ViewButton(section, Loc.T("Glossary"), View.AZ);
            float x = section.xMax + 9f;

            float right = r.xMax - 4f;
            var more = new Rect(right - 22f, r.y, 22f, r.height);
            right = more.x - 6f;
            if (GUI.Button(more, new GUIContent("", Loc.T("More")), EditorStyles.toolbarButton)) ShowMenu(more);
            if (ev.type == EventType.Repaint)
                for (int k = -1; k <= 1; k++) EditorGUI.DrawRect(new Rect(more.center.x - 1f, more.center.y - 1f + k * 4f, 2f, 2f), StudioGUI.TextDim);

            if (r.width >= 620f)
            {
                var bar = new Rect(right - 64f, r.y + (r.height - 6f) * 0.5f, 64f, 6f);
                if (ev.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(bar, StudioGUI.CardBorder);
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * _tree.ProgressFraction, bar.height), StudioGUI.CardGood);
                }
                right = bar.x - 6f;
            }
            if (r.width >= 450f)
            {
                var count = new Rect(right - 64f, r.y, 64f, r.height);
                GUI.Label(count, _tree.ProgressText, s_count);
                right = count.x - 12f;
            }

            var field = new Rect(x, r.center.y - 9f, Mathf.Max(40f, right - x), 18f);
            GUI.SetNextControlName(SearchName);
            var query = GUI.TextField(field, _search, EditorStyles.toolbarSearchField) ?? "";
            if (query != _search)
            {
                _search = query;
                _azScroll = Vector2.zero;
                _azScrollTo = null;
            }
            if (ev.type == EventType.Repaint && _search.Length == 0 && GUI.GetNameOfFocusedControl() != SearchName)
                GUI.Label(new Rect(field.x + 18f, field.y, field.width - 24f, field.height), Loc.T("Search skills and words"), s_placeholder);
        }

        void ViewButton(Rect r, string label, View view)
        {
            bool on = _view == (int)view;
            if (Event.current.type == EventType.Repaint)
            {
                StudioGUI.DrawRounded(r, on ? StudioGUI.CardTryFill : StudioGUI.CardInset, 5f);
                if (on) EditorGUI.DrawRect(new Rect(r.x + 8f, r.yMax - 2f, r.width - 16f, 2f), StudioGUI.CardAccent);
                GUI.Label(r, label, s_tab);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            }
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                _view = (int)view;
                _search = "";
                GUI.FocusControl(null);
                Repaint();
            }
        }

        void ShowMenu(Rect anchor)
        {
            var menu = new GenericMenu();
            int toasts = AvatarTextureWindow.LearnToastPrefs.Mode;
            menu.AddItem(new GUIContent(Loc.T("Show toasts/Automatic (on in Simple, off in Advanced)")), toasts < 0, () => AvatarTextureWindow.LearnToastPrefs.Mode = -1);
            menu.AddItem(new GUIContent(Loc.T("Show toasts/Always")), toasts == 1, () => AvatarTextureWindow.LearnToastPrefs.Mode = 1);
            menu.AddItem(new GUIContent(Loc.T("Show toasts/Never")), toasts == 0, () => AvatarTextureWindow.LearnToastPrefs.Mode = 0);
            menu.AddItem(new GUIContent(Loc.T("Show dots on new info icons")), StudioGUI.ShowDots, () => StudioGUI.ShowDots = !StudioGUI.ShowDots);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("I know Unity already")), false, () => Learn.MarkTierDone(1));
            menu.AddItem(new GUIContent(Loc.T("A–Z order/By tier")), !_sortByName, () => { _sortByName = false; Repaint(); });
            menu.AddItem(new GUIContent(Loc.T("A–Z order/By name")), _sortByName, () => { _sortByName = true; Repaint(); });
            menu.AddSeparator("");
            if (Learn.HasBackup) menu.AddItem(new GUIContent(Loc.T("Bring back my skill progress")), false, BringBack);
            else menu.AddDisabledItem(new GUIContent(Loc.T("Bring back my skill progress")));
            menu.AddItem(new GUIContent(Loc.T("Reset skill progress…")), false, ResetProgress);
            menu.AddSeparator("");
            OnlineGuide.AddMenu(menu, null);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Validate glossary")), false, () => EditorApplication.ExecuteMenuItem("Tools/Avistry/Validate glossary"));
            menu.DropDown(anchor);
        }

        void ResetProgress()
        {
            if (!StudioDialog.Ask(Loc.T("Reset your skill progress?"),
                    Loc.T("This clears what you've read and done on this computer. Your textures, layers and saves aren't touched."),
                    Loc.T("Reset"), Loc.T("Cancel"))) return;
            Learn.Reset();
            _resetNotice = true;
            Repaint();
        }

        void BringBack()
        {
            Learn.BringBack();
            _resetNotice = false;
            Repaint();
        }

        void DrawNotice(Rect r)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.CardTryFill);
                GUI.Label(new Rect(r.x + 12f, r.y, 180f, r.height), Loc.T("Progress reset."), s_notice);
            }
            if (GUI.Button(new Rect(r.x + 100f, r.y + 2f, 80f, r.height - 4f), Loc.T("Bring back"), EditorStyles.miniButton)) BringBack();
            if (GUI.Button(new Rect(r.xMax - 26f, r.y + 2f, 22f, r.height - 4f), new GUIContent("×", Loc.T("Dismiss")), EditorStyles.miniButton))
            {
                _resetNotice = false;
                Repaint();
            }
        }


        void BuildAz(int width, bool adv)
        {
            var all = Glossary.All;
            if (_az != null && _azKeyW == width && _azKeyOpen == _azOpen && _azKeySearch == _search && _azKeySort == _sortByName && _azKeyAdv == adv && ReferenceEquals(_azKeyGloss, all)) return;
            _azKeyW = width; _azKeyOpen = _azOpen; _azKeySearch = _search; _azKeySort = _sortByName; _azKeyAdv = adv; _azKeyGloss = all;
            _az = new List<AzItem>(64);

            var list = new List<Glossary.Entry>(all.Count);
            string query = _search.Trim();
            string[] words = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var e in all)
            {
                bool match = true;
                foreach (string word in words)
                    if (!SkillTreeView.Matches(e, word, adv)) { match = false; break; }
                if (match) list.Add(e);
            }
            _azCount = list.Count;
            if (_sortByName || words.Length > 0) list.Sort((a, b) =>
            {
                bool aTitle = a.title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase);
                bool bTitle = b.title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase);
                return aTitle != bTitle ? bTitle.CompareTo(aTitle) : string.Compare(a.title, b.title, StringComparison.CurrentCultureIgnoreCase);
            });

            float y = 0f;
            int lastTier = -1;
            var rowRect = new Rect(0, 0, width, 0);
            if (!_sortByName && words.Length == 0)
            {
                list.Sort((a, b) =>
                {
                    int ta = a.bonus ? 99 : Glossary.TierOf(a), tb = b.bonus ? 99 : Glossary.TierOf(b);
                    return ta != tb ? ta.CompareTo(tb) : a.order.CompareTo(b.order);
                });
            }
            foreach (var e in list)
            {
                if (!_sortByName && words.Length == 0)
                {
                    int tier = e.bonus ? 0 : Glossary.TierOf(e);
                    if (tier != lastTier)
                    {
                        lastTier = tier;
                        var head = _tree.MakeHead(tier, width, 0f);
                        _az.Add(new AzItem { Head = head, Y = y, H = 34f + head.Extra });
                        y += 34f + head.Extra;
                    }
                }
                float rowHead = ConceptCard.Layout(e, rowRect, CardMode.Row, false, false);
                float full = e.id == _azOpen ? ConceptCard.Layout(e, rowRect, CardMode.Row, false, true) : rowHead;
                _az.Add(new AzItem { E = e, Y = y, H = full + 1f, HeadH = rowHead });
                y += full + 1f;
            }
            _azH = y;
        }

        void DrawAZ(Rect area)
        {
            var ev = Event.current;
            bool adv = StudioMode.Advanced;
            int width = Mathf.FloorToInt(area.width - ScrollBarW);
            BuildAz(width, adv);
            var title = new Rect(area.x + 12f, area.y + 3f, width - 24f, 30f);
            if (ev.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, StudioGUI.CardBg);
                string heading = _search.Length == 0 ? Loc.T("Glossary") : _azCount == 1 ? Loc.T("1 match") : Loc.F("{0} matches", _azCount);
                GUI.Label(title, heading, StudioGUI.CardTitleRow);
                EditorGUI.DrawRect(new Rect(area.x, title.yMax, area.width, 1f), StudioGUI.CardDivider);
            }
            area.yMin = title.yMax + 1f;
            if (_az.Count == 0)
            {
                if (ev.type == EventType.Repaint) GUI.Label(area, Loc.T("Nothing matches that search."), EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (_azScrollTo != null)
            {
                foreach (var it in _az)
                    if (it.E != null && it.E.id == _azScrollTo) { _azScroll.y = Mathf.Max(0f, it.Y - 4f); break; }
                _azScrollTo = null;
            }

            _azScroll = GUI.BeginScrollView(area, _azScroll, new Rect(0, 0, width, _azH));
            float top = _azScroll.y - 20f, bottom = _azScroll.y + area.height + 20f;
            for (int i = 0; i < _az.Count; i++)
            {
                var it = _az[i];
                if (it.E == null || it.Y + it.H < top || it.Y > bottom) continue;
                it.DisclosureId = GUIUtility.GetControlID(it.E.id.GetHashCode(), FocusType.Keyboard, new Rect(0, it.Y, width, it.HeadH));
                _az[i] = it;
            }
            string toggle = null, goTo = null, showMe = null, openTree = null;
            for (int i = 0; i < _az.Count; i++)
            {
                var it = _az[i];
                if (it.Y + it.H < top || it.Y > bottom) continue;
                if (it.E == null)
                {
                    _tree.DrawHead(new Rect(0, it.Y + 3f, width, 28f + it.Head.Extra), it.Head);
                    continue;
                }
                bool open = it.E.id == _azOpen;
                var res = default(CardResult);
                ConceptCard.Layout(it.E, new Rect(0, it.Y, width, 0), CardMode.Row, true, ref res, open);
                if (ev.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(0, it.Y + it.H - 1f, width, 1f), StudioGUI.CardDivider);
                var headRect = new Rect(0, it.Y, width, it.HeadH);
                var chip = new Rect(headRect.x + 14f, headRect.y + 12f, 18f, 18f);
                if (StudioGUI.FoldoutMarker(headRect, chip, open, it.DisclosureId) != open)
                    toggle = it.E.id;
                if (res.GoTo != null) goTo = res.GoTo;
                if (res.ShowMe) showMe = it.E.id;
                if (res.OpenTree) openTree = it.E.id;
            }
            GUI.EndScrollView();

            if (toggle != null)
            {
                if (_azOpen == toggle) _azOpen = null;
                else { _azOpen = toggle; Learn.MarkRead(toggle); }
                Repaint();
            }
            if (goTo != null) { _view = (int)View.AZ; _search = ""; OpenRow(goTo); }
            if (openTree != null) { _view = (int)View.Tree; GoTo(openTree); }
            if (showMe != null) EditorApplication.delayCall += () => ConceptCard.ShowMeHandler?.Invoke(showMe);
        }
    }
}
