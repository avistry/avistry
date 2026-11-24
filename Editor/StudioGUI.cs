using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
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

        public static Rect BeginGroup(string title, string badge = null, string infoId = null)
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

        static readonly Vector3[] FoldChevron = new Vector3[3];
        static readonly GUIContent FoldLabel = new GUIContent();

        public static bool BeginFoldGroup(ref bool open, string title, string summary = null, string badge = null, string tooltip = null, string infoId = null)
        {
            var r = EditorGUILayout.BeginVertical(GroupStyle);
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, GroupBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), HeaderLine);
            }
            var tr = GUILayoutUtility.GetRect(0, 24f, GUILayout.ExpandWidth(true));
            open = FoldoutWithDetail(tr, open, title, open ? badge : summary, tooltip, infoId);
            return open;
        }

        public static bool Foldout(bool open, GUIContent title, string detail = null)
        {
            return Foldout(EditorGUILayout.GetControlRect(false, 22f), open, title, detail);
        }

        public static bool Foldout(Rect row, bool open, GUIContent title, string detail = null)
        {
            return FoldoutWithDetail(row, open, title.text, detail, title.tooltip);
        }

        static bool FoldoutWithDetail(Rect row, bool open, string title, string detail, string tooltip, string infoId = null)
        {
            bool info = HasInfo(infoId);
            var hit = row;
            if (info) hit.width = Mathf.Max(0f, hit.width - InfoHit - 2f);
            int id = GUIUtility.GetControlID(FocusType.Keyboard, hit);
            open = DisclosureInput(hit, open, id, out bool focused, out bool hover);
            if (Event.current.type == EventType.Repaint)
                DrawFoldHeader(hit, open, hover, focused, title, detail, tooltip);
            if (info) DrawInfo(new Rect(row.xMax - InfoHit, row.center.y - InfoHit * 0.5f, InfoHit, InfoHit), infoId);
            return open;
        }

        internal static bool KeyboardButton(Rect r, int hint, out bool hover, out bool focus)
        {
            int id = GUIUtility.GetControlID(hint, FocusType.Keyboard, r);
            var e = Event.current;
            hover = GUI.enabled && r.Contains(e.mousePosition);
            focus = GUIUtility.keyboardControl == id;
            bool click = false;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && hover)
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        click = hover;
                    }
                    break;
                case EventType.KeyDown:
                    if (GUI.enabled && focus && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space))
                    {
                        e.Use();
                        click = true;
                    }
                    break;
                case EventType.Repaint:
                    if (GUI.enabled) EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                    break;
            }
            return click;
        }

        static bool DisclosureInput(Rect hit, bool open, int id, out bool focused, out bool hover)
        {
            var e = Event.current;
            focused = GUIUtility.keyboardControl == id;
            hover = GUI.enabled && hit.Contains(e.mousePosition);
            bool next = open;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when hover && e.button == 0:
                    GUIUtility.keyboardControl = id;
                    next = !open;
                    e.Use();
                    break;
                case EventType.KeyDown when focused && GUI.enabled && !e.alt && !e.control && !e.command && !e.shift:
                    switch (e.keyCode)
                    {
                        case KeyCode.LeftArrow: next = false; e.Use(); break;
                        case KeyCode.RightArrow: next = true; e.Use(); break;
                        case KeyCode.Return:
                        case KeyCode.KeypadEnter:
                        case KeyCode.Space: next = !open; e.Use(); break;
                    }
                    break;
            }
            if (next != open) GUI.changed = true;
            return next;
        }

        internal static bool FoldoutMarker(Rect hit, Rect chip, bool open, int id)
        {
            open = DisclosureInput(hit, open, id, out bool focused, out bool hover);
            if (Event.current.type == EventType.Repaint)
            {
                DrawDisclosureChip(chip, open, hover || focused);
                if (focused) DrawRoundedBorder(hit, CardAccent, 4f);
                if (GUI.enabled) EditorGUIUtility.AddCursorRect(hit, MouseCursor.Link);
            }
            return open;
        }

        static void DrawDisclosureChip(Rect chip, bool open, bool highlighted)
        {
            Color accent = CardAccent;
            DrawRounded(chip, open ? accent : CardTryFill, 4f);
            if (!open) DrawRoundedBorder(chip, highlighted ? accent : Color.Lerp(CardTryFill, accent, .55f), 4f);
            Vector3 c = chip.center;
            Vector3 along = open ? Vector3.right : Vector3.down;
            Vector3 tip = open ? Vector3.up : Vector3.right;
            FoldChevron[0] = c - along * 4f - tip * 2f;
            FoldChevron[1] = c + tip * 2f;
            FoldChevron[2] = c + along * 4f - tip * 2f;
            using (new Handles.DrawingScope(open ? CardBg : accent))
                Handles.DrawAAPolyLine(2.5f, FoldChevron);
        }

        static void DrawFoldHeader(Rect row, bool open, bool hover, bool focused, string title, string detail, string tooltip)
        {
            Color accent = CardAccent;
            DrawRounded(row, hover ? Color.Lerp(HeaderBg, accent, .12f) : HeaderBg, 4f);
            if (focused) DrawRoundedBorder(row, accent, 4f);
            float size = Mathf.Clamp(row.height - 4f, 10f, 18f);
            var chip = new Rect(row.x + 3f, row.center.y - size * .5f, size, size);
            DrawDisclosureChip(chip, open, hover || focused);

            var label = Rect.MinMaxRect(chip.xMax + 5f, row.y, Mathf.Max(chip.xMax + 5f, row.xMax - 5f), row.yMax);
            FoldLabel.text = title;
            FoldLabel.tooltip = null;
            var style = EditorStyles.boldLabel;
            float titleWidth = style.CalcSize(FoldLabel).x;
            GUI.Label(label, FoldLabel, style);
            bool clipped = titleWidth > label.width;
            if (!string.IsNullOrEmpty(detail))
            {
                FoldLabel.text = detail;
                float remaining = label.width - titleWidth - 6f;
                if (remaining >= Badge.CalcSize(FoldLabel).x)
                    GUI.Label(new Rect(label.xMax - remaining, row.y, remaining, row.height), FoldLabel, Badge);
                else clipped = true;
            }
            if (GUI.enabled) EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
            if (hover)
            {
                FoldLabel.text = "";
                string action = open ? Loc.T("Click to fold this section") : Loc.T("Click to open this section");
                FoldLabel.tooltip = string.IsNullOrEmpty(tooltip) ? action : action + "\n" + tooltip;
                if (clipped)
                    FoldLabel.tooltip = (string.IsNullOrEmpty(detail) ? title : title + " · " + detail) + "\n" + FoldLabel.tooltip;
                GUI.Label(row, FoldLabel, GUIStyle.none);
            }
        }

        static GUIStyle _primary;
        static float _primaryHeight;
        static GUIStyle PrimaryStyle(float height)
        {
            _primary ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            if (_primaryHeight != height) { _primaryHeight = height; _primary.fixedHeight = height; }
            return _primary;
        }

        public static bool PrimaryButton(GUIContent content, Color tint, float height = 30f)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            bool clicked = GUILayout.Button(content, PrimaryStyle(height));
            GUI.backgroundColor = old;
            return clicked;
        }

        public static bool DangerButton(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = Danger;
            bool clicked = GUILayout.Button(content, style, options);
            GUI.backgroundColor = old;
            return clicked;
        }

        public enum Check { Ok, Problem, Note }

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

        static bool _stylesPro;
        static void SkinCheck()
        {
            if (_stylesPro == Pro) return;
            _stylesPro = Pro;
            _hint = _rowLabelDisabled = _badge = _groupTitle = null;
        }

        static GUIStyle _body, _mini, _miniRight, _hint, _rowLabel, _rowLabelDisabled, _status, _overlay, _bigButton, _badge;

        public static GUIStyle Body => _body ??= new GUIStyle { padding = new RectOffset(8, 8, 6, 8) };
        public static GUIStyle Mini => _mini ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        public static GUIStyle MiniRight => _miniRight ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        public static GUIStyle Hint { get { SkinCheck(); return _hint ??= HintStyle(); } }

        public static string Ellipsize(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || style.CalcSize(new GUIContent(text)).x <= width) return text;
            int lo = 0, hi = text.Length;
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
        public static GUIStyle Overlay => _overlay ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 3, 3),
            richText = true,
            wordWrap = true,
        };
        public static GUIStyle BigButton => _bigButton ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };
        public static GUIStyle Badge { get { SkinCheck(); return _badge ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = Pro ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.3f, 0.3f, 0.3f) } }; } }

        static readonly Dictionary<string, Texture> IconCache = new Dictionary<string, Texture>();

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

        public static bool Section(ref bool open, string title, string badge = null, string infoId = null, string tooltip = null)
        {
            var r = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            open = FoldoutWithDetail(r, open, title, badge, tooltip, infoId);
            return open;
        }

        public static void Divider(float space = 4)
        {
            GUILayout.Space(space);
            var r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, HeaderLine);
            GUILayout.Space(space);
        }

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
