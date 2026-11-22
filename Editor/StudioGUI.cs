using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Shared styles, colours, icons and section headers for the Avistry window.</summary>
    static partial class StudioGUI
    {
        static bool Pro => EditorGUIUtility.isProSkin;

        public static Color HeaderBg => Pro ? new Color(0.205f, 0.205f, 0.205f) : new Color(0.80f, 0.80f, 0.80f);
        public static Color HeaderLine => Pro ? new Color(0.11f, 0.11f, 0.11f) : new Color(0.62f, 0.62f, 0.62f);
        public static Color ListBg => Pro ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.88f, 0.88f, 0.88f);
        public static Color RowAlt => Pro ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.035f);
        public static Color RowActive => Pro ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.58f, 0.72f, 0.92f);
        public static Color CanvasBg => new Color(0.115f, 0.115f, 0.115f);
        public static Color StatusBg => Pro ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.76f, 0.76f, 0.76f);
        public static readonly Color Accent = new Color(1f, 0.6f, 0.15f);
        public static readonly Color BakeButton = new Color(0.45f, 0.85f, 0.55f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.55f);
        public static readonly Color Danger = new Color(1f, 0.42f, 0.38f);
        public static readonly Color InfoBlue = new Color(0.35f, 0.65f, 1f);
        public static Color TabBg => Pro ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.76f, 0.76f, 0.76f);
        public static Color TabActiveBg => Pro ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.87f, 0.87f, 0.87f);
        public static Color GroupBg => Pro ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.84f, 0.84f, 0.84f);
        public static Color TextDim => Pro ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.38f, 0.38f, 0.38f);

        static GUIStyle _tabLabel, _groupTitle, _contextTitle, _toastText, _checkLabel, _emptyTitle;
        public static GUIStyle TabLabel => _tabLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
        public static GUIStyle GroupTitle { get { SkinCheck(); return _groupTitle ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = TextDim }, margin = new RectOffset(0, 0, 0, 2) }; } }
        public static GUIStyle ContextTitle => _contextTitle ??= new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
        public static GUIStyle ToastText => _toastText ??= new GUIStyle(EditorStyles.wordWrappedLabel) { normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }, alignment = TextAnchor.MiddleLeft };
        public static GUIStyle CheckLabel => _checkLabel ??= new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
        public static GUIStyle EmptyTitle => _emptyTitle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

        public static void DrawBorder(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        static GUIStyle _group;
        static GUIStyle GroupStyle => _group ??= new GUIStyle { padding = new RectOffset(8, 8, 6, 8), margin = new RectOffset(0, 0, 0, 6) };

        /// <summary>A titled box (law of common region): related controls share one background.</summary>
        public static Rect BeginGroup(string title, string badge = null, string infoId = null) // infoId: drawn by StudioGUI.Info.cs
        {
            var r = EditorGUILayout.BeginVertical(GroupStyle);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, GroupBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), HeaderLine);
            }
            if (!string.IsNullOrEmpty(title))
            {
                var tr = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
                bool info = HasInfo(infoId);
                var lr = info ? new Rect(tr.x, tr.y, tr.width - InfoHit - 2, tr.height) : tr;
                GUI.Label(lr, title.ToUpperInvariant(), GroupTitle);
                if (!string.IsNullOrEmpty(badge)) GUI.Label(lr, badge, Badge);
                if (info) DrawInfo(new Rect(tr.xMax - InfoHit, tr.center.y - InfoHit * 0.5f, InfoHit, InfoHit), infoId);
            }
            return r;
        }

        public static void EndGroup() => EditorGUILayout.EndVertical();

        static GUIStyle _foldTitle;
        static GUIStyle FoldTitle => _foldTitle ??= new GUIStyle(GroupTitle) { clipping = TextClipping.Clip };

        /// <summary>
        /// Same box as BeginGroup, with a clickable header that folds the group. When folded, <paramref name="summary"/> (the
        /// key value) is shown after the title; when open, <paramref name="badge"/> sits on the right as usual.
        /// Always call EndGroup. Returns whether the content should be drawn.
        /// </summary>
        public static bool BeginFoldGroup(ref bool open, string title, string summary = null, string badge = null, string tooltip = null, string infoId = null) // infoId: drawn by StudioGUI.Info.cs
        {
            var r = EditorGUILayout.BeginVertical(GroupStyle);
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, GroupBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), HeaderLine);
            }
            var tr = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
            bool info = HasInfo(infoId);
            var lr = info ? new Rect(tr.x, tr.y, tr.width - InfoHit - 2, tr.height) : tr;
            string text = (open ? "▾ " : "▸ ") + title.ToUpperInvariant() + (!open && !string.IsNullOrEmpty(summary) ? "  ·  " + summary : "");
            GUI.Label(lr, new GUIContent(text, string.IsNullOrEmpty(tooltip) ? (open ? Loc.T("Click to fold this section") : Loc.T("Click to open this section")) : tooltip), FoldTitle);
            if (open && !string.IsNullOrEmpty(badge)) GUI.Label(lr, badge, Badge);
            // Before the fold click so a click on the icon is used here and does not fold the group.
            if (info) DrawInfo(new Rect(tr.xMax - InfoHit, tr.center.y - InfoHit * 0.5f, InfoHit, InfoHit), infoId);
            if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition))
            {
                open = !open;
                e.Use();
                GUI.changed = true;
            }
            return open;
        }

        static GUIStyle _primary;
        static float _primaryHeight;
        static GUIStyle PrimaryStyle(float height)
        {
            _primary ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            if (_primaryHeight != height) { _primaryHeight = height; _primary.fixedHeight = height; }
            return _primary;
        }

        /// <summary>The one highlighted action of a tab (Von Restorff): tinted, taller, full width.</summary>
        public static bool PrimaryButton(GUIContent content, Color tint, float height = 30f)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            bool clicked = GUILayout.Button(content, PrimaryStyle(height));
            GUI.backgroundColor = old;
            return clicked;
        }

        /// <summary>A destructive button: red tint so it never looks like the safe actions next to it (law of similarity).</summary>
        public static bool DangerButton(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = Danger;
            bool clicked = GUILayout.Button(content, style, options);
            GUI.backgroundColor = old;
            return clicked;
        }

        public enum Check { Ok, Problem, Note }

        /// <summary>Checklist line with a coloured mark (not colour alone: the mark differs too).</summary>
        public static void CheckRow(Check state, string text, string tooltip = null)
        {
            string mark = state == Check.Ok ? "✓" : state == Check.Problem ? "✕" : "!";
            var col = state == Check.Ok ? Good : state == Check.Problem ? Danger : Accent;
            using (new EditorGUILayout.HorizontalScope())
            {
                var old = GUI.contentColor;
                GUI.contentColor = col;
                GUILayout.Label(mark, EditorStyles.boldLabel, GUILayout.Width(16));
                GUI.contentColor = old;
                GUILayout.Label(new GUIContent(text, tooltip), CheckLabel, GUILayout.MinWidth(20));
            }
        }

        // The styles below carry skin colours; Unity can switch skins without reloading scripts, so drop them when it does.
        static bool _stylesPro;
        static void SkinCheck()
        {
            if (_stylesPro == Pro) return;
            _stylesPro = Pro;
            _hint = _rowLabelDisabled = _badge = _groupTitle = null;
        }

        static GUIStyle _headerFoldout, _body, _mini, _miniRight, _hint, _rowLabel, _rowLabelDisabled, _status, _overlay, _bigButton, _badge;

        public static GUIStyle HeaderFoldout => _headerFoldout ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
        public static GUIStyle Body => _body ??= new GUIStyle { padding = new RectOffset(8, 8, 6, 8) };
        public static GUIStyle Mini => _mini ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        public static GUIStyle MiniRight => _miniRight ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        public static GUIStyle Hint { get { SkinCheck(); return _hint ??= HintStyle(); } }

        /// <summary>Cuts <paramref name="text"/> to fit <paramref name="width"/> pixels in <paramref name="style"/> and ends it with an ellipsis.</summary>
        public static string Ellipsize(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= width) return text;
            int lo = 0, hi = text.Length; // longest prefix that fits with the ellipsis
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, mid) + "…")).x <= width) lo = mid; else hi = mid - 1;
            }
            return text.Substring(0, lo) + "…";
        }
        static GUIStyle HintStyle() => new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = Pro ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.35f, 0.35f, 0.35f) } };
        public static GUIStyle RowLabel => _rowLabel ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        public static GUIStyle RowLabelDisabled { get { SkinCheck(); return _rowLabelDisabled ??= new GUIStyle(RowLabel) { normal = { textColor = Pro ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.55f, 0.55f, 0.55f) } }; } }
        public static GUIStyle Status => _status ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset(6, 6, 0, 0) };
        public static GUIStyle Overlay => _overlay ??= new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 3, 3), richText = true };
        public static GUIStyle BigButton => _bigButton ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };
        public static GUIStyle Badge { get { SkinCheck(); return _badge ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = Pro ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.3f, 0.3f, 0.3f) } }; } }

        static readonly Dictionary<string, Texture> IconCache = new Dictionary<string, Texture>();

        /// <summary>Built-in editor icon (dark/light variant) with a text fallback; never logs an error if missing.</summary>
        public static GUIContent Icon(string name, string text, string tooltip = null)
        {
            if (!IconCache.TryGetValue(name, out var tex))
            {
                tex = Pro ? (EditorGUIUtility.FindTexture("d_" + name) ?? EditorGUIUtility.FindTexture(name))
                          : (EditorGUIUtility.FindTexture(name) ?? EditorGUIUtility.FindTexture("d_" + name));
                IconCache[name] = tex;
            }
            return tex != null ? new GUIContent(text, tex, tooltip) : new GUIContent(string.IsNullOrEmpty(text) ? name : text, tooltip);
        }

        /// <summary>A full-width foldout header bar. Returns whether the section is open.</summary>
        public static bool Section(ref bool open, string title, string badge = null, string infoId = null) // infoId: drawn by StudioGUI.Info.cs
        {
            var r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, HeaderBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), HeaderLine);
            }
            bool info = HasInfo(infoId);
            float reserve = info ? InfoHit + 2 : 0;
            if (!string.IsNullOrEmpty(badge))
                GUI.Label(new Rect(r.x + 120, r.y + 2, r.width - 128 - reserve, 18), badge, Badge);
            open = EditorGUI.Foldout(new Rect(r.x + 8, r.y + 2, r.width - 16 - reserve, 18), open, title, true, HeaderFoldout);
            if (info) DrawInfo(new Rect(r.xMax - InfoHit - 4, r.center.y - InfoHit * 0.5f, InfoHit, InfoHit), infoId);
            return open;
        }

        public static void Divider(float space = 4)
        {
            GUILayout.Space(space);
            var r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, HeaderLine);
            GUILayout.Space(space);
        }

        /// <summary>Colour chip per layer type, shown in the layer list.</summary>
        public static Color LayerChip(Layer l) => l switch
        {
            PaintLayer _ => new Color(0.95f, 0.45f, 0.70f),
            GradientMapLayer _ => new Color(0.60f, 0.42f, 0.95f),
            HslLayer _ => new Color(0.30f, 0.78f, 0.78f),
            RootTipGradientLayer _ => new Color(1f, 0.62f, 0.25f),
            SolidColorLayer s => new Color(s.color.r, s.color.g, s.color.b, 1f),
            RecolourPartLayer rp => new Color(rp.swatch.r, rp.swatch.g, rp.swatch.b, 1f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };

        /// <summary>Slider with logarithmic response (fine control for small brush sizes) and a number field.</summary>
        public static float LogSlider(GUIContent label, float value, float min, float max, string unit)
        {
            var r = EditorGUILayout.GetControlRect();
            r = EditorGUI.PrefixLabel(r, label);
            const float fieldW = 52;
            var sliderRect = new Rect(r.x, r.y, r.width - fieldW - 4, r.height);
            var fieldRect = new Rect(r.xMax - fieldW, r.y, fieldW, r.height);
            float lmin = Mathf.Log(min), lmax = Mathf.Log(max);
            float l = GUI.HorizontalSlider(sliderRect, Mathf.Log(Mathf.Clamp(value, min, max)), lmin, lmax);
            float v = Mathf.Exp(l);
            if (!Mathf.Approximately(l, Mathf.Log(Mathf.Clamp(value, min, max)))) value = Mathf.Round(v);
            value = EditorGUI.FloatField(fieldRect, value);
            if (!string.IsNullOrEmpty(unit))
                GUI.Label(new Rect(fieldRect.xMax - 20, fieldRect.y, 18, fieldRect.height), unit, EditorStyles.centeredGreyMiniLabel);
            return Mathf.Clamp(value, min, max);
        }
    }
}
