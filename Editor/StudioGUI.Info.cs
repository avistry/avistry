using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    static partial class StudioGUI
    {
        public static bool Counting;
        public static int VisibleControls;
        public static readonly List<string> VisibleControlIds = new List<string>();

        public static void BeginCount()
        {
            VisibleControls = 0;
            VisibleControlIds.Clear();
            Counting = true;
        }

        public static int EndCount()
        {
            Counting = false;
            return VisibleControls;
        }

        public static void Count(string controlId)
        {
            AvatarTextureWindow.LearnPulse.Probe(controlId);
            Tally(controlId);
        }

        public static void Count(string controlId, Rect rect)
        {
            AvatarTextureWindow.LearnPulse.PanelControlDrawn?.Invoke(controlId, rect);
            Tally(controlId);
            Track(controlId, rect);
        }

        static void Tally(string controlId)
        {
            if (!Counting || Event.current == null || Event.current.type != EventType.Repaint) return;
            VisibleControls++;
            VisibleControlIds.Add(controlId);
        }

        struct Seen { public Rect Rect; public double Time; }
        static readonly Dictionary<string, Seen> s_seen = new Dictionary<string, Seen>();
        static string s_pulse;
        static double s_pulseStart;
        const double PulseSeconds = 1.6, PulseFade = 0.4;

        internal static string Pulse
        {
            get => s_pulse != null && EditorApplication.timeSinceStartup - s_pulseStart < PulseSeconds ? s_pulse : null;
            set
            {
                s_pulse = value;
                s_pulseStart = EditorApplication.timeSinceStartup;
                if (value == null) return;
                EditorApplication.update -= PulseTick;
                EditorApplication.update += PulseTick;
            }
        }

        static void PulseTick()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (Pulse != null) return;
            EditorApplication.update -= PulseTick;
        }

        internal static void Track(string controlId, Rect rect)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            s_seen[controlId] = new Seen { Rect = rect, Time = EditorApplication.timeSinceStartup };
            var pulse = Pulse;
            if (pulse == null || pulse != controlId) return;
            double left = PulseSeconds - (EditorApplication.timeSinceStartup - s_pulseStart);
            var c = CardAccent;
            c.a = left < PulseFade ? (float)(left / PulseFade) : 1f;
            DrawRoundedBorder(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), c, 4f, 2f);
        }

        internal static bool TryGetRect(string controlId, out Rect rect)
        {
            if (s_seen.TryGetValue(controlId, out var s) && EditorApplication.timeSinceStartup - s.Time < 1.0)
            {
                rect = s.Rect;
                return true;
            }
            rect = default;
            return false;
        }

        public static string Label(string controlId, string simpleFallback, string advancedFallback = null)
        {
            var c = Glossary.Control(controlId);
            if (c != null)
            {
                if (StudioMode.Advanced && !string.IsNullOrEmpty(c.labelAdvanced)) return c.labelAdvanced;
                if (!string.IsNullOrEmpty(c.label)) return c.label;
            }
            return (StudioMode.Advanced && advancedFallback != null) ? advancedFallback : (simpleFallback ?? controlId);
        }

        public static GUIContent Content(string controlId, string simpleFallback, string advancedFallback = null)
        {
            var tip = Glossary.Control(controlId)?.tip;
            return new GUIContent(Label(controlId, simpleFallback, advancedFallback), string.IsNullOrEmpty(tip) ? null : tip);
        }

        const float InfoHit = 18f;
        const float InfoDraw = 14f;
        static readonly int InfoHint = "ATSInfoIcon".GetHashCode();
        static Texture2D s_infoRing, s_infoFilled;

        static Color Hex(int rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        static Color InfoIdle => Pro ? Hex(0x9AA5B1, 0.8f) : Hex(0x6B7785, 0.8f);
        static Color InfoHover => Pro ? Hex(0x6AA2FF) : Hex(0x2F6FDE);
        static Color InfoActive => Pro ? Hex(0x8DBBFF) : Hex(0x1D4FB0);

        static Texture2D InfoIcon(bool filled)
        {
            if (filled ? s_infoFilled == null : s_infoRing == null)
            {
                const int n = 28;
                const float c = (n - 1) * 0.5f;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        float disc = Mathf.Clamp01(13.5f - d + 0.5f);
                        float inner = Mathf.Clamp01(11.5f - d + 0.5f);
                        float dot = Mathf.Clamp01(2.2f - Mathf.Sqrt((x - c) * (x - c) + (y - 19.5f) * (y - 19.5f)) + 0.5f);
                        float stem = Mathf.Abs(x - c) <= 1.5f && y >= 7 && y <= 16 ? 1f : 0f;
                        float glyph = Mathf.Max(dot, stem);
                        float a = filled ? disc * (1f - glyph) : Mathf.Max(disc - inner, glyph);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px);
                tex.Apply(false, true);
                if (filled) s_infoFilled = tex; else s_infoRing = tex;
            }
            return filled ? s_infoFilled : s_infoRing;
        }

        const string DotsKey = "Avistry.Learn.Dots";
        static int s_dots = -1;
        static int s_topTier = -1;
        static bool s_learnHooked;

        internal static bool ShowDots
        {
            get
            {
                if (s_dots < 0) s_dots = EditorPrefs.GetBool(DotsKey, true) ? 1 : 0;
                return s_dots == 1;
            }
            set
            {
                s_dots = value ? 1 : 0;
                EditorPrefs.SetBool(DotsKey, value);
            }
        }

        static void LearnChanged() => s_topTier = -1;

        static bool ShowsUnseenDot(Glossary.Entry entry)
        {
            if (StudioMode.Advanced || !ShowDots || Learn.StateOf(entry.id) != Learn.State.New) return false;
            if (!s_learnHooked) { s_learnHooked = true; Learn.Changed += LearnChanged; }
            if (s_topTier < 0) s_topTier = Learn.HighestTierWithProgress();
            return Glossary.TierOf(entry) <= s_topTier + 1;
        }

        static bool HasInfo(string conceptId) => !string.IsNullOrEmpty(conceptId) && Glossary.Get(conceptId) != null;

        public static void Info(string conceptId)
        {
            if (StudioMode.Advanced || !HasInfo(conceptId)) return;
            var r = GUILayoutUtility.GetRect(InfoHit, InfoHit, GUILayout.Width(InfoHit), GUILayout.Height(InfoHit));
            DrawInfo(r, conceptId);
        }

        public static void Info(Rect r, string conceptId)
        {
            if (StudioMode.Advanced) return;
            DrawInfo(r, conceptId);
        }

        static readonly Dictionary<string, string> s_infoNames = new Dictionary<string, string>();
        static readonly Dictionary<string, GUIContent> s_infoTips = new Dictionary<string, GUIContent>();

        static string InfoName(string conceptId)
        {
            if (!s_infoNames.TryGetValue(conceptId, out var n)) s_infoNames[conceptId] = n = "ats-info-" + conceptId;
            return n;
        }

        static GUIContent InfoTip(string conceptId, string tip)
        {
            if (!s_infoTips.TryGetValue(conceptId, out var c)) s_infoTips[conceptId] = c = new GUIContent(string.Empty, tip);
            else if (c.tooltip != tip) c.tooltip = tip;
            return c;
        }

        static void DrawInfo(Rect r, string conceptId)
        {
            var entry = Glossary.Get(conceptId);
            if (entry == null) return;
            var e = Event.current;
            GUI.SetNextControlName(InfoName(conceptId));
            int id = GUIUtility.GetControlID(InfoHint, FocusType.Keyboard, r);
            bool focused = GUIUtility.keyboardControl == id;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.keyboardControl = id;
                        e.Use();
                        InfoPopup.Show(r, conceptId);
                    }
                    break;
                case EventType.KeyDown:
                    if (focused && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space))
                    {
                        e.Use();
                        InfoPopup.Show(r, conceptId);
                    }
                    break;
                case EventType.Repaint:
                    {
                        var icon = new Rect(r.center.x - InfoDraw * 0.5f, r.center.y - InfoDraw * 0.5f, InfoDraw, InfoDraw);
                        bool open = InfoPopup.Current == conceptId;
                        bool hover = r.Contains(e.mousePosition);
                        var old = GUI.color;
                        GUI.color = open ? InfoActive : hover ? InfoHover : InfoIdle;
                        GUI.DrawTexture(icon, InfoIcon(open || hover), ScaleMode.StretchToFill, true);
                        GUI.color = old;
                        if (ShowsUnseenDot(entry)) DrawRounded(new Rect(icon.xMax - 3f, icon.y - 2f, 5f, 5f), CardAccent, 2.5f);
                        if (focused && GUIUtility.hotControl == 0) DrawBorder(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), InfoHover, 1f);
                        EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                        break;
                    }
            }
            if (e.type == EventType.Repaint) GUI.Label(r, InfoTip(conceptId, entry.tip), GUIStyle.none);
        }

        public static void LabelWithInfo(string controlId, string simpleFallback = null, GUIStyle style = null)
        {
            var concept = Glossary.Control(controlId)?.concept;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Content(controlId, simpleFallback), style ?? EditorStyles.label, GUILayout.ExpandWidth(false));
                if (Event.current.type == EventType.Repaint) Track(controlId, GUILayoutUtility.GetLastRect());
                if (StudioMode.Simple && HasInfo(concept)) { GUILayout.Space(2); Info(concept); }
                GUILayout.FlexibleSpace();
            }
        }

        public static Rect PrefixWithInfo(string controlId, string simpleFallback = null)
        {
            var row = EditorGUILayout.GetControlRect();
            Count(controlId, row);
            var content = Content(controlId, simpleFallback);
            var field = EditorGUI.PrefixLabel(row, content);
            var concept = Glossary.Control(controlId)?.concept;
            if (StudioMode.Simple && HasInfo(concept))
            {
                var start = EditorGUI.IndentedRect(row).x;
                float x = Mathf.Min(start + EditorStyles.label.CalcSize(content).x + 2f, row.x + EditorGUIUtility.labelWidth - InfoHit);
                DrawInfo(new Rect(x, row.y + (row.height - InfoHit) * 0.5f, InfoHit, InfoHit), concept);
            }
            return field;
        }

        public static bool FoldoutWithInfo(ref bool open, string controlId, string simpleFallback = null)
        {
            var row = EditorGUILayout.GetControlRect(false, 22f);
            Count(controlId, row);
            var content = Content(controlId, simpleFallback);
            var concept = Glossary.Control(controlId)?.concept;
            open = FoldoutWithDetail(row, open, content.text, null, content.tooltip, StudioMode.Simple ? concept : null);
            return open;
        }
    }
}
