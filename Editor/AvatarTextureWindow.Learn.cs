using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The window's side of the skill tree: deferred hook calls, the "Skill done" toasts and "Show me".
    /// The hooks themselves are one-line <c>Learn.Did(...)</c> calls where an action is committed.
    /// </summary>
    partial class AvatarTextureWindow
    {
        /// <summary>Whether skill toasts show. EditorPrefs <c>Avistry.Learn.Toasts</c>: -1 Auto (on in Simple, off in Advanced), 0 off, 1 on.</summary>
        internal static class LearnToastPrefs
        {
            internal const string Key = "Avistry.Learn.Toasts";

            internal static int Mode
            {
                get => EditorPrefs.GetInt(Key, -1);
                set => EditorPrefs.SetInt(Key, Mathf.Clamp(value, -1, 1));
            }

            internal static bool Enabled(bool advanced)
            {
                int m = Mode;
                return m == 1 || (m < 0 && !advanced);
            }
        }

        /// <summary>
        /// The outline that "Show me" draws round a control. The control reports itself from <c>StudioGUI.Count</c>
        /// through <see cref="Probe"/>, so the outline is drawn in the control's own scroll and clip space.
        /// </summary>
        internal static class LearnPulse
        {
            internal const double Seconds = 1.6;

            static string s_id;
            static double s_until;
            static bool s_latched;

            internal static string Id => s_id;
            internal static bool Active => s_id != null && EditorApplication.timeSinceStartup < s_until;

            internal static void Start(string controlId)
            {
                s_id = controlId;
                s_until = EditorApplication.timeSinceStartup + Seconds;
                // Controls that report a rect are outlined by StudioGUI.Track instead of the probe.
                StudioGUI.Pulse = controlId;
            }

            internal static void Clear()
            {
                s_id = null;
                StudioGUI.Pulse = null;
            }

            /// <summary>
            /// Decides once per frame, on Layout, whether the probe adds its zero-size layout element, so Layout and Repaint
            /// always agree on how many controls there are even when the pulse ends between them.
            /// </summary>
            internal static void Latch()
            {
                if (Event.current.type == EventType.Layout) s_latched = Active;
            }

            /// <summary>Call from <c>StudioGUI.Count</c> before the control is drawn. Costs one bool test when no pulse is running.</summary>
            internal static void Probe(string controlId)
            {
                if (!s_latched || controlId != s_id) return;
                var r = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                if (Event.current.type != EventType.Repaint) return;
                var box = new Rect(r.x - 2f, r.y - 2f, r.width + 4f, EditorGUIUtility.singleLineHeight + 4f);
                float left = (float)(s_until - EditorApplication.timeSinceStartup);
                float pulse = 0.55f + 0.45f * Mathf.Sin((float)EditorApplication.timeSinceStartup * 9f);
                var c = StudioGUI.CardAccent;
                c.a = Mathf.Clamp01(left / 0.4f) * pulse;
                StudioGUI.DrawRoundedBorder(box, c, 3f, 2f);
            }
        }

        // ---------------------------------------------------------------- hooks at commit points

        // Changes made while a slider is dragged count once, when the mouse is released. Anything the AI helper does is skipped
        // here, because the call may be settled after its Suppress scope has ended.
        readonly List<string> _learnPending = new List<string>(4);

        void LearnLater(string learnEvent)
        {
            if (Learn.Suppressed || _learnPending.Contains(learnEvent)) return;
            _learnPending.Add(learnEvent);
        }

        void LearnPoll()
        {
            if (_learnPending.Count == 0 || GUIUtility.hotControl != 0) return;
            for (int i = 0; i < _learnPending.Count; i++) Learn.Did(_learnPending[i]);
            _learnPending.Clear();
        }

        // Set when a part is chosen: the next texture that loads is the first one picked for it.
        bool _learnFirstTexture;

        void LearnTargetPicked(Renderer r)
        {
            Learn.Did(LearnEvents.TargetPicked);
            _learnFirstTexture = !Learn.Suppressed;
            bool poiyomi = false, lilToon = false;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                var family = ShaderProfiles.Detect(mats[i].shader);
                if (family == ShaderFamily.Poiyomi && !poiyomi) { poiyomi = true; Learn.Did(LearnEvents.TargetPoiyomi); }
                else if (family == ShaderFamily.LilToon && !lilToon) { lilToon = true; Learn.Did(LearnEvents.TargetLilToon); }
            }
        }

        void LearnTextureLoaded()
        {
            if (!_learnFirstTexture) return;
            _learnFirstTexture = false;
            Learn.Did(LearnEvents.TexturePicked);
        }

        static void ShowFileAndLearn(string assetPath)
        {
            ShowFile(assetPath);
            Learn.Did(LearnEvents.AssetPinged);
        }

        // ---------------------------------------------------------------- toasts

        const double ToastFadeIn = 0.12, ToastHold = 3.5, ToastFadeOut = 0.3;
        enum LearnToastKind { None, Skill, Medal, Graduate }

        LearnToastKind _lnKind;
        string _lnId, _lnLine1, _lnLine2;
        int _lnTier, _lnMore, _lnShown;
        double _lnStart, _lnEnd, _lnTickLast, _lnRepaintLast;
        bool _lnHover;
        Rect _lnRect;
        readonly List<string> _lnLog = new List<string>(4);
        static readonly List<string> s_lnNext = new List<string>(1);

        bool LearnToastLive => _lnKind != LearnToastKind.None && EditorApplication.timeSinceStartup < _lnEnd;

        void OnLearnConceptDone(string conceptId)
        {
            try { BuildLearnToast(conceptId); }
            catch (System.Exception ex) { Debug.LogWarning("[Avistry] The skill toast could not be built (" + ex.Message + ")."); }
        }

        // In the test sandbox the toast is built (so a test can read it) but never drawn and never ticks.
        void BuildLearnToast(string conceptId)
        {
            var entry = Glossary.Get(conceptId);
            if (entry == null || Learn.Suppressed) return;
            bool sandbox = Learn.Sandbox;
            if (!sandbox && !LearnToastPrefs.Enabled(Advanced)) return;

            int tier = Glossary.TierOf(entry);
            int total = Glossary.CountedCount;
            double now = EditorApplication.timeSinceStartup;
            bool live = LearnToastLive;

            var kind = LearnToastKind.Skill;
            if (!entry.bonus)
            {
                if (Learn.IsGraduate && !Learn.IsCelebrated(Learn.GraduateMark))
                {
                    kind = LearnToastKind.Graduate;
                    Learn.SetCelebrated(Learn.GraduateMark);
                    Learn.SetCelebrated(tier);
                }
                else if (Learn.TierComplete(tier) && !Learn.IsCelebrated(tier))
                {
                    kind = LearnToastKind.Medal;
                    Learn.SetCelebrated(tier);
                }
            }

            if (!live) { _lnLog.Clear(); _lnMore = 0; _lnShown++; }
            _lnLog.Add(entry.title);

            if (kind == LearnToastKind.Skill && live && _lnKind == LearnToastKind.Skill)
            {
                // At most one toast every 4 s: the next ones merge into "+N more skills".
                _lnMore++;
                _lnEnd = System.Math.Max(_lnEnd, now + 1.5);
                _lnLine2 = SkillLine2(_lnTier, total);
                return;
            }
            if (kind == LearnToastKind.Skill && live)
            {
                // A medal is already showing: it stays, and the skill is only counted.
                _lnMore++;
                return;
            }

            _lnKind = kind;
            _lnId = conceptId;
            _lnTier = tier;
            _lnStart = now;
            _lnEnd = now + ToastFadeIn + ToastHold + ToastFadeOut;
            _lnHover = false;
            switch (kind)
            {
                case LearnToastKind.Graduate:
                    _lnLine1 = Loc.F("{0}: all {1} skills done", Learn.GraduateName, total);
                    _lnLine2 = Loc.T("You have been through every tier.");
                    break;
                case LearnToastKind.Medal:
                    var info = Glossary.TierInfo(tier);
                    var next = Glossary.TierInfo(tier + 1);
                    _lnLine1 = string.IsNullOrEmpty(info?.achievement) ? Loc.F("Medal: {0}", info?.name) : info.achievement;
                    _lnLine2 = next != null ? Loc.F("Next: {0}", next.blurb) : Loc.F("{0} of {1} done", Learn.DoneCount(), total);
                    break;
                default:
                    _lnLine1 = Loc.F("Skill done: {0}", entry.title);
                    _lnLine2 = SkillLine2(tier, total);
                    break;
            }

            if (sandbox) return;
            EditorApplication.update -= LearnToastTick;
            EditorApplication.update += LearnToastTick;
            _lnTickLast = now;
            Repaint();
        }

        string SkillLine2(int tier, int total)
        {
            return _lnMore > 0 ? Loc.F("Tier {0} · {1} of {2} · +{3} more skill{4}", tier, Learn.DoneCount(), total, _lnMore, _lnMore == 1 ? "" : "s")
                               : Loc.F("Tier {0} · {1} of {2} · See the tree", tier, Learn.DoneCount(), total);
        }

        void LearnToastTick()
        {
            double now = EditorApplication.timeSinceStartup;
            double dt = now - _lnTickLast;
            _lnTickLast = now;
            // Hover pauses the toast: push the end back by the time spent hovering.
            if (_lnHover && now < _lnEnd) _lnEnd += dt;
            if (now >= _lnEnd)
            {
                EditorApplication.update -= LearnToastTick;
                _lnKind = LearnToastKind.None;
                _lnRect = default;
                Repaint();
                return;
            }
            bool fading = now - _lnStart < ToastFadeIn || _lnEnd - now < ToastFadeOut;
            if (fading && now - _lnRepaintLast > 0.03) { _lnRepaintLast = now; Repaint(); }
        }

        // Runs before the canvas sees the event, with last frame's rect, so only a click on the toast itself is taken.
        void LearnToastInput(Event e)
        {
            if (_lnKind == LearnToastKind.None || Learn.Sandbox || !LearnToastLive) return;
            switch (e.type)
            {
                case EventType.MouseMove:
                case EventType.MouseDrag:
                    _lnHover = _lnRect.Contains(e.mousePosition);
                    break;
                case EventType.MouseLeaveWindow:
                    _lnHover = false;
                    break;
                case EventType.MouseDown when e.button == 0 && _lnRect.Contains(e.mousePosition):
                    e.Use();
                    OpenLearnToastTarget();
                    _lnEnd = 0;
                    LearnToastTick();
                    break;
            }
        }

        void OpenLearnToastTarget()
        {
            string id = _lnId;
            if (_lnKind != LearnToastKind.Skill)
            {
                Learn.NextUp(s_lnNext, 1);
                id = s_lnNext.Count > 0 ? s_lnNext[0] : _lnId;
            }
            GlossaryWindow.Open(id);
        }

        static GUIStyle s_lnLine1Style, s_lnLine2Style;
        static bool s_lnStylesPro;

        static void EnsureLearnToastStyles()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (s_lnLine1Style != null && s_lnStylesPro == pro) return;
            s_lnStylesPro = pro;
            s_lnLine1Style = new GUIStyle(StudioGUI.CardHeading) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            s_lnLine2Style = new GUIStyle(StudioGUI.CardMeta) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip };
        }

        static Color Faded(Color c, float a) { c.a *= a; return c; }

        /// <summary>Draws over the bottom right of the canvas, above its status line. Never modal, never takes focus.</summary>
        void DrawLearnToast(Rect area)
        {
            if (_lnKind == LearnToastKind.None || Learn.Sandbox || !LearnToastLive || Event.current.type != EventType.Repaint) return;
            double now = EditorApplication.timeSinceStartup;
            float a = Mathf.Clamp01((float)((now - _lnStart) / ToastFadeIn)) * Mathf.Clamp01((float)((_lnEnd - now) / ToastFadeOut));
            bool medal = _lnKind != LearnToastKind.Skill;
            float w = Mathf.Min(medal ? 320f : 280f, area.width - 24f), h = medal ? 68f : 56f;
            if (w < 120f) return;
            var box = new Rect(area.xMax - 12f - w, area.yMax - StatusHeight - 12f - h, w, h);
            // The status toast sits at the top of the canvas; on a very short canvas, stay out from under it.
            if (!string.IsNullOrEmpty(_toastText) && now <= _toastUntil && box.Overlaps(_toastRect)) box.y = _toastRect.yMax + 6f;
            _lnRect = box;

            EnsureLearnToastStyles();
            StudioGUI.DrawRoundedBox(box, Faded(StudioGUI.CardBg, a), Faded(StudioGUI.CardBorder, a), 6f);
            float textX = box.x + 14f;
            if (medal)
            {
                StudioGUI.DrawMedalGlyph(new Rect(box.x + 12f, box.y + (h - 34f) * 0.5f, 34f, 34f), Faded(StudioGUI.MedalGold, a));
                textX = box.x + 56f;
            }
            else StudioGUI.DrawRounded(new Rect(box.x, box.y + 6f, 4f, h - 12f), Faded(StudioGUI.TierColour(_lnTier), a), 2f);

            var oldColour = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            float textW = box.xMax - 10f - textX;
            float line1H = medal ? 20f : 18f;
            float top = box.y + (medal ? 12f : 9f);
            GUI.Label(new Rect(textX, top, textW, line1H), _lnLine1, s_lnLine1Style);
            GUI.Label(new Rect(textX, top + line1H + 2f, textW, medal ? 30f : 18f), _lnLine2, medal ? StudioGUI.CardMeta : s_lnLine2Style);
            GUI.color = oldColour;
            EditorGUIUtility.AddCursorRect(box, MouseCursor.Link);
        }

        // ---------------------------------------------------------------- Show me

        static readonly string[] s_windowTargets = { "hierarchy", "scene", "inspector", "project" };
        static readonly string[] s_windowMenus = { "Window/General/Hierarchy", "Window/General/Scene", "Window/General/Inspector", "Window/General/Project" };
        static readonly string[] s_windowEvents = { LearnEvents.WindowHierarchy, LearnEvents.WindowScene, LearnEvents.WindowInspector, LearnEvents.WindowProject };

        /// <summary>
        /// The card's Show me button: opens this window if needed, switches to the card's tab and outlines its control.
        /// Returns what it did, for tests and logs. Never changes the recipe, a material or an asset, so it needs no Undo.
        /// </summary>
        [InitializeOnLoadMethod]
        static void HookShowMe() => ConceptCard.ShowMeHandler = id => ShowMe(id);

        internal static string ShowMe(string conceptId)
        {
            var entry = Glossary.Get(conceptId);
            if (entry == null) return "unknown concept";
            bool windowOnly = !string.IsNullOrEmpty(entry.tryControl) && entry.tryControl.StartsWith("window:");
            var w = Shown();
            if (w == null && !windowOnly) { Open(); w = Shown(); }
            if (w == null) return ShowMeWindow(entry.tryControl);
            if (!windowOnly) w.Focus();
            return w.ShowMeHere(conceptId, false);
        }

        static string ShowMeWindow(string target)
        {
            int i = System.Array.IndexOf(s_windowTargets, target.Substring("window:".Length));
            if (i < 0) return "unknown window";
            EditorApplication.ExecuteMenuItem(s_windowMenus[i]);
            Learn.Did(s_windowEvents[i]);
            return "window=" + s_windowTargets[i];
        }

        string ShowMeHere(string conceptId, bool sync)
        {
            var entry = Glossary.Get(conceptId);
            if (entry == null) return "unknown concept";
            string control = entry.tryControl;
            if (!string.IsNullOrEmpty(control) && control.StartsWith("window:")) return ShowMeWindow(control);
            if (string.IsNullOrEmpty(entry.tryTab) && string.IsNullOrEmpty(control)) return "nothing to show";

            if (!System.Enum.TryParse(entry.tryTab, true, out Tab tab)) tab = _tab;
            if (!TabAvailable(tab, out _))
            {
                _tab = Tab.Target;
                Toast(Loc.T("Pick a part first, then press Show me again."), MessageType.Info, 5f);
                Repaint();
                return "tab=Target needsPart";
            }

            var copy = string.IsNullOrEmpty(control) ? null : Glossary.Control(control);
            if (copy != null && !copy.simple && !Advanced)
            {
                SetTab(tab);
                Toast(Loc.F("“{0}” is in Advanced mode.", StudioGUI.Label(control, control)), MessageType.Info, 6f, Loc.T("Switch to Advanced"), () => SetAdvanced(true));
                return "tab=" + tab + " advancedOnly";
            }

            SetTab(tab);
            if (string.IsNullOrEmpty(control)) return "tab=" + tab;

            if (tab == Tab.Layers && control.StartsWith("layers.")) _openLayerSettings = true; // closed by default
            LearnPulse.Start(control);
            if (sync) return FinishShowMe(tab, control);
            // Whether the control is on screen is only known after a repaint, so look once this click has finished.
            EditorApplication.delayCall += () => { if (this != null) FinishShowMe(tab, control); };
            return "tab=" + tab + " pulse=" + control;
        }

        string FinishShowMe(Tab tab, string control)
        {
            var ids = AutoVisibleControlIds(tab.ToString());
            if (ids != null && ids.Contains(control))
            {
                LearnPulse.Start(control);
                Repaint();
                return "tab=" + tab + " pulse=" + control;
            }
            LearnPulse.Clear();
            string label = StudioGUI.Label(control, control);
            Toast(Loc.F("Look for “{0}” on this tab.", label), MessageType.Info, 6f);
            return "tab=" + tab + " look=" + label;
        }

        // ---------------------------------------------------------------- hooks for tests

        /// <summary>Shows a concept's card action in the window, like the Show me button. Returns for example "tab=Select pulse=limit.clickAvatar", "tab=Select look=…" or "window=hierarchy".</summary>
        internal string AutoShowMe(string conceptId) => ShowMeHere(conceptId, true);

        /// <summary>
        /// The newest skill toast: "line 1 | line 2 | done: titles that merged into it | shown=N". "none" when there is none.
        /// <c>shown</c> counts toasts that started from an idle state since the window opened; a medal replaces a toast that is showing.
        /// </summary>
        internal string AutoLearnToast()
        {
            if (_lnKind == LearnToastKind.None) return "none";
            return _lnLine1 + " | " + _lnLine2 + " | done: " + string.Join(", ", _lnLog) + " | shown=" + _lnShown;
        }
    }
}
