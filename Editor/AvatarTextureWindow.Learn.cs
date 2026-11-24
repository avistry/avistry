using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
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

        internal static class LearnPulse
        {
            internal const double Seconds = 1.6;

            static string s_id;
            static double s_until;
            static bool s_latched;
            internal static System.Action<string, Rect> PanelControlDrawn;

            internal static string Id => s_id;
            internal static bool Active => s_id != null && EditorApplication.timeSinceStartup < s_until;

            internal static void Start(string controlId)
            {
                s_id = controlId;
                s_until = EditorApplication.timeSinceStartup + Seconds;
                StudioGUI.Pulse = controlId;
            }

            internal static void Clear()
            {
                s_id = null;
                StudioGUI.Pulse = null;
            }

            internal static void Latch()
            {
                if (Event.current.type == EventType.Layout) s_latched = Active;
            }

            internal static void Probe(string controlId)
            {
                if (!s_latched || controlId != s_id) return;
                var r = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
                if (Event.current.type != EventType.Repaint) return;
                PanelControlDrawn?.Invoke(controlId, r);
                var box = new Rect(r.x - 2f, r.y - 2f, r.width + 4f, EditorGUIUtility.singleLineHeight + 4f);
                float left = (float)(s_until - EditorApplication.timeSinceStartup);
                float pulse = 0.55f + 0.45f * Mathf.Sin((float)EditorApplication.timeSinceStartup * 9f);
                var c = StudioGUI.CardAccent;
                c.a = Mathf.Clamp01(left / 0.4f) * pulse;
                StudioGUI.DrawRoundedBorder(box, c, 3f, 2f);
            }
        }


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
                _lnMore++;
                _lnEnd = System.Math.Max(_lnEnd, now + 1.5);
                _lnLine2 = SkillLine2(_lnTier, total);
                return;
            }
            if (kind == LearnToastKind.Skill && live)
            {
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

        void DrawLearnToast(Rect area)
        {
            if (_lnKind == LearnToastKind.None || Learn.Sandbox || !LearnToastLive || Event.current.type != EventType.Repaint) return;
            double now = EditorApplication.timeSinceStartup;
            float a = Mathf.Clamp01((float)((now - _lnStart) / ToastFadeIn)) * Mathf.Clamp01((float)((_lnEnd - now) / ToastFadeOut));
            bool medal = _lnKind != LearnToastKind.Skill;
            float w = Mathf.Min(medal ? 320f : 280f, area.width - 24f), h = medal ? 68f : 56f;
            if (w < 120f) return;
            var box = new Rect(area.xMax - 12f - w, area.yMax - StatusHeight - 12f - h, w, h);
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


        static readonly string[] s_windowTargets = { "hierarchy", "scene", "inspector", "project" };
        static readonly string[] s_windowMenus = { "Window/General/Hierarchy", "Window/General/Scene", "Window/General/Inspector", "Window/General/Project" };
        static readonly string[] s_windowEvents = { LearnEvents.WindowHierarchy, LearnEvents.WindowScene, LearnEvents.WindowInspector, LearnEvents.WindowProject };
        string _revealLearnControl;
        float _learnViewportHeight;

        void RevealLearnControl(string id, Rect rect)
        {
            if (_revealLearnControl != id || Event.current.type != EventType.Repaint) return;
            _revealLearnControl = null;
            if (rect.yMin >= _panelScroll.y && rect.yMax + 20f <= _panelScroll.y + _learnViewportHeight) return;
            _panelScroll.y = Mathf.Max(0f, rect.yMin - 12f);
            Repaint();
        }

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
            if (tab == Tab.Layers) tab = Tab.Paint;
            string hint = null;
            if (!TabAvailable(tab, out var why))
            {
                tab = Tab.Target;
                control = _renderer == null || CurrentSlot == null ? "pick.partList" : "pick.image";
                hint = why;
            }

            var copy = string.IsNullOrEmpty(control) ? null : Glossary.Control(control);
            if (!Advanced && copy != null && !copy.simple)
            {
                SetTab(tab);
                Toast(Loc.F("“{0}” is in Advanced mode.", StudioGUI.Label(control, control)),
                    MessageType.Info, 6f, Loc.T("Switch to Advanced"), () =>
                    {
                        SetAdvanced(true);
                        ShowMeHere(conceptId, false);
                    });
                return "tab=" + tab + " advancedOnly";
            }

            bool wholeColour = Recolour.Supports(_material);
            if (control == "colour.cover" && !wholeColour && !CanRecolourPieces && TabAvailable(Tab.Paint, out _))
            {
                tab = Tab.Paint;
                control = "layers.toolbar";
                hint = Loc.T("Use layers in Paint to recolour this texture.");
            }
            if (control == "colour.cover" && (!ByPart || CurrentPart == null))
            {
                control = ByPart ? "colour.parts" : wholeColour ? "colour.scope" : "colour.recolourPieces";
                hint = wholeColour ? Loc.T("Choose Pieces, then pick the part you want to colour.")
                    : Loc.T("Choose Recolour pieces, then pick the piece you want to colour.");
            }
            if (tab == Tab.Target)
            {
                if (control == "audit.check") _foldAudit = true;
                if (control == "masks.rows" || control == "masks.add") _foldMaskBuilder = true;
            }
            if (tab == Tab.Paint)
            {
                if (control == "paint.smoothJoins")
                {
                    SetPickForPaint(false);
                    _openPaintOptions = true;
                    if (GradientToolActive)
                    {
                        _openBrush = true;
                        control = "paint.tools";
                        hint = Loc.T("Choose Brush, then turn on Smooth across joins.");
                    }
                    else if (!_paintInScene)
                    {
                        control = "paint.onAvatar";
                        hint = Loc.T("Turn on Paint directly on the model to see Smooth across joins.");
                    }
                }
                if (control == "paint.gradientMap" && _recipe.layers.Count > 0)
                {
                    control = "layers.toolbar";
                    hint = Loc.T("Open Add layer and choose Gradient Map.");
                }
                if (control == "paint.gradientMap" || control == "masks.channels" ||
                    (control != null && control.StartsWith("layers.", System.StringComparison.Ordinal)))
                {
                    _openLayers = true;
                    if (control != "layers.toolbar" && control != "paint.gradientMap")
                    {
                        if (ActiveLayer == null)
                        {
                            control = "layers.toolbar";
                            hint = Loc.T("Add or choose a layer in the Layers list first.");
                        }
                        else if (control != "layers.hideMask") _openLayerSettings = _foldLayerWhere = true;
                    }
                }
            }
            if (tab == Tab.Target && control != null && control.StartsWith("maps.", System.StringComparison.Ordinal))
                control = RevealMapTool(control);
            if (tab == Tab.Save && control == "save.save")
            {
                if (_recipe.layers.Count == 0) control = CurrentSaveState == SaveState.Saved ? "save.continueEditing" : "save.goToPaint";
                else if (!_recipe.layers.Exists(IsVisibleForSave)) control = "save.reviewLayers";
            }

            SetTab(tab);
            if (string.IsNullOrEmpty(control)) return "tab=" + tab;
            RevealPanelGroup(control);

            if (hint != null) Toast(hint, MessageType.Info, 5f);
            _revealLearnControl = control;
            LearnPulse.Start(control);
            if (sync) return FinishShowMe(tab, control, hint);
            EditorApplication.delayCall += () => { if (this != null && _tab == tab) FinishShowMe(tab, control, hint); };
            return "tab=" + tab + " pulse=" + control;
        }

        string FinishShowMe(Tab tab, string control, string hint)
        {
            CountDrawnControls();
            if (StudioGUI.VisibleControlIds.Contains(control))
            {
                LearnPulse.Start(control);
                Repaint();
                return "tab=" + tab + " pulse=" + control;
            }
            LearnPulse.Clear();
            _revealLearnControl = null;
            string label = StudioGUI.Label(control, control);
            Toast(hint ?? Loc.F("Look for “{0}” on this tab.", label), MessageType.Info, 6f);
            return "tab=" + tab + " look=" + label;
        }


        internal string AutoShowMe(string conceptId) => ShowMeHere(conceptId, true);

        internal string AutoLearnToast()
        {
            if (_lnKind == LearnToastKind.None) return "none";
            return _lnLine1 + " | " + _lnLine2 + " | done: " + string.Join(", ", _lnLog) + " | shown=" + _lnShown;
        }
    }
}
