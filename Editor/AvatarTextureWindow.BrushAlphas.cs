using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        const int BrushAlphaPageSize = 12;
        [SerializeField] bool _foldBrushAlphas;
        string _brushAlphaSearch = "", _brushAlphaCategory = "";
        int _brushAlphaPage;
        [NonSerialized] bool _revealBrushTile;
        [NonSerialized] string _alphaPickerLanguage;
        [NonSerialized] GUIContent[] _alphaPickerChoices;
        [NonSerialized] int[] _alphaPickerIndices;
        [NonSerialized] string[] _alphaCategories, _alphaCategoryLabels;
        readonly Dictionary<string, string> _alphaLabels = new Dictionary<string, string>();
        readonly Dictionary<string, GUIContent> _alphaTileContents = new Dictionary<string, GUIContent>();
        readonly Dictionary<string, BrushShape> _alphaSamples = new Dictionary<string, BrushShape>();
        readonly BrushAlphaResults _alphaResults = new BrushAlphaResults();
        [NonSerialized] GUIContent[] _brushTipLabels;
        [NonSerialized] GUIContent _alphaPrevious, _alphaNext, _alphaShapeSelector;
        [NonSerialized] string _alphaPageLabel;
        (int first, int end, int count) _alphaPageLabelRange;
        static GUIStyle s_alphaTileLabel;
        static bool s_alphaTileProSkin;
        static readonly GUIContent AlphaGroupTitle = new GUIContent();
        static readonly Vector3[] AlphaCheck = new Vector3[3];

        internal sealed class BrushAlphaResults
        {
            readonly List<BrushStampLibrary.Definition> _matches = new List<BrushStampLibrary.Definition>(74);
            IReadOnlyList<BrushStampLibrary.Definition> _source;
            IReadOnlyDictionary<string, string> _labels;
            string _search, _category, _language;

            internal IReadOnlyList<BrushStampLibrary.Definition> Get(IReadOnlyList<BrushStampLibrary.Definition> source,
                IReadOnlyDictionary<string, string> labels, string search, string category, string language)
            {
                search ??= "";
                category ??= "";
                if (ReferenceEquals(_source, source) && ReferenceEquals(_labels, labels) &&
                    _search == search && _category == category && _language == language) return _matches;
                _source = source; _labels = labels; _search = search; _category = category; _language = language;
                _matches.Clear();
                string query = search.Trim();
                for (int index = 0; index < source.Count; index++)
                {
                    var definition = source[index];
                    if (category.Length != 0 && definition.Category != category) continue;
                    if (query.Length == 0 || definition.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        labels[definition.Id].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        definition.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) _matches.Add(definition);
                }
                return _matches;
            }
        }

        void EnsureBrushAlphaLabels()
        {
            var all = BrushStampLibrary.All;
            if (_alphaPickerLanguage == Loc.Current && _alphaPickerChoices != null &&
                _alphaLabels.Count == all.Count && _alphaTileContents.Count == all.Count) return;
            _alphaPickerChoices = new GUIContent[all.Count + 1];
            _alphaPickerIndices = Enumerable.Range(0, all.Count + 1).ToArray();
            _alphaPickerChoices[0] = new GUIContent(Loc.T("Current tip"));
            _alphaLabels.Clear();
            _alphaTileContents.Clear();
            for (int index = 0; index < all.Count; index++)
            {
                var definition = all[index];
                string label = Loc.T(definition.Label);
                _alphaLabels.Add(definition.Id, label);
                _alphaTileContents.Add(definition.Id, new GUIContent("", label));
                _alphaPickerChoices[index + 1] = new GUIContent(Loc.T(definition.Category) + "/" + NoSubmenu(label));
            }
            _alphaCategories = new[] { "" }.Concat(all.Select(definition => definition.Category).Distinct()).ToArray();
            _alphaCategoryLabels = _alphaCategories.Select(category => category.Length == 0 ? Loc.T("All shapes") : Loc.T(category)).ToArray();
            _brushTipLabels = AllBrushTips.Select(value => new GUIContent(value switch
            {
                BrushTip.Round => Loc.T("Round"),
                BrushTip.Square => Loc.T("Square"),
                BrushTip.Bristle => Loc.T("Bristle (streaks)"),
                BrushTip.Spray => Loc.T("Spray (particles)"),
                BrushTip.Stamp => Loc.T("Stamp (alpha shape)"),
                BrushTip.Drips => Loc.T("Drips (gravity)"),
                _ => value.ToString(),
            })).ToArray();
            _alphaPrevious = new GUIContent("‹", Loc.T("Previous shapes"));
            _alphaNext = new GUIContent("›", Loc.T("Next shapes"));
            _alphaShapeSelector = new GUIContent(Loc.T("Brush shape"));
            _alphaPageLabel = null;
            _alphaPickerLanguage = Loc.Current;
        }

        string CurrentBrushAlphaLabel()
        {
            return _brush.shape?.tip == BrushTip.Stamp && BrushStampLibrary.TryFind(_brush.shape.alphaId, out var definition)
                ? Loc.T(definition.Label) : Loc.T("Current tip");
        }

        void SetBrushAlpha(string id)
        {
            if (!BrushStampLibrary.TryFind(id, out _) || _brush.shape.tip == BrushTip.Stamp && _brush.shape.alphaId == id &&
                _brush.shape.alphaVersion == BrushStampLibrary.Version) return;
            Undo.RecordObject(this, Loc.T("Choose brush shape"));
            _brush.shape.tip = BrushTip.Stamp;
            _brush.shape.alphaId = id;
            _brush.shape.alphaVersion = BrushStampLibrary.Version;
            _brush.shape.jitterVersion = 1;
            BrushStampCache.RequestWarm(_brush.shape);
            MarkBrushEdited();
        }

        void DrawBrushAlphaChooser()
        {
            AlphaGroupTitle.text = Loc.T("Brush shapes");
            _foldBrushAlphas = StudioGUI.Foldout(_foldBrushAlphas, AlphaGroupTitle, CurrentBrushAlphaLabel());
            StudioGUI.Count("paint.brushShapes", GUILayoutUtility.GetLastRect());
            if (!_foldBrushAlphas) return;
            EnsureBrushAlphaLabels();
            EditorGUI.BeginChangeCheck();
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            try
            {
                EditorGUIUtility.labelWidth = Mathf.Min(oldLabelWidth, Mathf.Max(48f, PanelContentWidth * .38f));
                _brushAlphaSearch = EditorGUILayout.TextField(Loc.T("Search shapes"), _brushAlphaSearch);
                int currentCategory = Mathf.Max(0, Array.IndexOf(_alphaCategories, _brushAlphaCategory));
                int category = EditorGUILayout.Popup(Loc.T("Category"), currentCategory, _alphaCategoryLabels);
                _brushAlphaCategory = _alphaCategories[category];
            }
            finally { EditorGUIUtility.labelWidth = oldLabelWidth; }
            if (EditorGUI.EndChangeCheck()) _brushAlphaPage = 0;
            var visible = _alphaResults.Get(BrushStampLibrary.All, _alphaLabels, _brushAlphaSearch,
                _brushAlphaCategory, _alphaPickerLanguage);
            if (visible.Count == 0)
            {
                GUILayout.Label(Loc.T("No shapes match this search."), StudioGUI.Hint);
                return;
            }

            int pages = (visible.Count + BrushAlphaPageSize - 1) / BrushAlphaPageSize;
            _brushAlphaPage = Mathf.Clamp(_brushAlphaPage, 0, pages - 1);
            int first = _brushAlphaPage * BrushAlphaPageSize, end = Mathf.Min(first + BrushAlphaPageSize, visible.Count);
            DrawBrushAlphaTiles(visible, first, end);
            GUILayout.Space(4f);
            var range = (first + 1, end, visible.Count);
            if (_alphaPageLabel == null || !_alphaPageLabelRange.Equals(range))
            {
                _alphaPageLabelRange = range;
                _alphaPageLabel = Loc.F("{0}–{1} of {2}", range.Item1, range.Item2, range.Count);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (pages > 1)
                {
                    using (new EditorGUI.DisabledScope(_brushAlphaPage == 0))
                        if (GUILayout.Button(_alphaPrevious, GUILayout.Width(38f), GUILayout.Height(24f)))
                            _brushAlphaPage--;
                }
                GUILayout.Label(_alphaPageLabel, EditorStyles.centeredGreyMiniLabel);
                if (pages > 1)
                {
                    using (new EditorGUI.DisabledScope(_brushAlphaPage + 1 >= pages))
                        if (GUILayout.Button(_alphaNext, GUILayout.Width(38f), GUILayout.Height(24f)))
                            _brushAlphaPage++;
                }
            }
        }

        void DrawBrushAlphaTiles(IReadOnlyList<BrushStampLibrary.Definition> visible, int first, int end)
        {
            const float height = 80f, gap = 4f;
            int columns = Mathf.Clamp(Mathf.FloorToInt((PanelContentWidth + gap) / (84f + gap)), 1, 4);
            int rows = (end - first + columns - 1) / columns;
            var bounds = EditorGUILayout.GetControlRect(false, rows * (height + gap) - gap);
            StudioGUI.Track("paint.brushShapeGrid", bounds);
            float width = Mathf.Max(1f, (bounds.width - gap * (columns - 1)) / columns);
            int focus = GUIUtility.GetControlID(FocusType.Keyboard, bounds);
            var evt = Event.current;
            int selected = -1, pick = -1;
            Rect focusTile = default;
            for (int index = first; index < end; index++)
            {
                var definition = visible[index];
                int cell = index - first;
                var tile = new Rect(bounds.x + (cell % columns) * (width + gap),
                    bounds.y + (cell / columns) * (height + gap), width, height);
                bool active = _brush.shape.tip == BrushTip.Stamp && _brush.shape.alphaId == definition.Id &&
                    _brush.shape.alphaVersion == BrushStampLibrary.Version;
                if (active) selected = index;
                if (active || index == first) focusTile = tile;
                if (evt.type == EventType.MouseDown && evt.button == 0 && GUI.enabled && tile.Contains(evt.mousePosition))
                    GUIUtility.keyboardControl = focus;
                if (GUI.Button(tile, _alphaTileContents[definition.Id], GUIStyle.none)) pick = index;
                if (evt.type == EventType.Repaint)
                    DrawBrushAlphaTile(tile, definition.Id, active);
            }
            if (evt.type == EventType.Repaint)
            {
                if (GUIUtility.keyboardControl == focus)
                {
                    if (_revealBrushTile && selected >= 0) GUI.ScrollTo(focusTile);
                    StudioGUI.DrawRoundedBorder(new Rect(focusTile.x + 2f, focusTile.y + 2f, focusTile.width - 4f, focusTile.height - 4f), StudioGUI.InfoBlue, 4f);
                }
                _revealBrushTile = false;
            }
            if (GUIUtility.keyboardControl == focus && GUI.enabled && evt.type == EventType.KeyDown && !evt.alt && !evt.control && !evt.command && !evt.shift)
            {
                int from = selected < 0 ? first : selected;
                switch (evt.keyCode)
                {
                    case KeyCode.LeftArrow: pick = from - 1; break;
                    case KeyCode.RightArrow: pick = from + 1; break;
                    case KeyCode.UpArrow: pick = from - columns; break;
                    case KeyCode.DownArrow: pick = from + columns; break;
                    case KeyCode.Return: case KeyCode.KeypadEnter: case KeyCode.Space: pick = from; break;
                    default: return;
                }
                pick = Mathf.Clamp(pick, 0, visible.Count - 1);
                _revealBrushTile = true;
                evt.Use();
            }
            if (pick >= 0)
            {
                _brushAlphaPage = pick / BrushAlphaPageSize;
                SetBrushAlpha(visible[pick].Id);
            }
        }

        void DrawBrushAlphaTile(Rect tile, string id, bool active)
        {
            bool hovered = GUI.enabled && tile.Contains(Event.current.mousePosition);
            Color border = active ? StudioGUI.CardAccent : hovered ? StudioGUI.TextDim : StudioGUI.HeaderLine;
            StudioGUI.DrawRoundedBox(tile, active || hovered ? StudioGUI.TabActiveBg : StudioGUI.ListBg, border, 5f);
            if (active) StudioGUI.DrawRoundedBorder(tile, border, 5f, 2f);
            if (!_alphaSamples.TryGetValue(id, out var sample))
                _alphaSamples.Add(id, sample = new BrushShape { tip = BrushTip.Stamp, alphaId = id, alphaVersion = BrushStampLibrary.Version });
            var well = new Rect(tile.x + 4f, tile.y + 4f, Mathf.Max(1f, tile.width - 8f), 43f);
            StudioGUI.DrawRounded(well, StudioGUI.CanvasBg, 3f);
            var image = new Rect(well.center.x - 19f, well.center.y - 19f, 38f, 38f);
            GUI.DrawTexture(image, BrushStampCache.GetPreview(sample), ScaleMode.ScaleToFit, true);
            if (active)
            {
                var mark = new Rect(well.xMax - 14f, well.y, 14f, 14f);
                StudioGUI.DrawRounded(mark, StudioGUI.CardAccent, 3f);
                AlphaCheck[0] = new Vector2(mark.x + 3f, mark.y + 7f);
                AlphaCheck[1] = new Vector2(mark.x + 6f, mark.y + 10f);
                AlphaCheck[2] = new Vector2(mark.x + 11f, mark.y + 4f);
                using (new Handles.DrawingScope(StudioGUI.CardBg))
                    Handles.DrawAAPolyLine(2f, AlphaCheck);
            }
            if (s_alphaTileLabel == null || s_alphaTileProSkin != EditorGUIUtility.isProSkin)
            {
                s_alphaTileProSkin = EditorGUIUtility.isProSkin;
                s_alphaTileLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, wordWrap = true,
                    normal = { textColor = StudioGUI.CardText },
                };
            }
            GUI.Label(new Rect(well.x, well.yMax + 1f, well.width, tile.yMax - well.yMax - 3f), _alphaLabels[id], s_alphaTileLabel);
        }

        void DrawSceneBrushAlpha(float width, ref Action change)
        {
            EnsureBrushAlphaLabels();
            int current = 0;
            for (int index = 0; index < BrushStampLibrary.All.Count; index++)
                if (_brush.shape.tip == BrushTip.Stamp && _brush.shape.alphaVersion == BrushStampLibrary.Version &&
                    _brush.shape.alphaId == BrushStampLibrary.All[index].Id)
                { current = index + 1; break; }
            int next = SteppedChoice(_alphaShapeSelector, current, _alphaPickerIndices,
                (rect, value) => EditorGUI.Popup(rect, value, _alphaPickerChoices), width);
            StudioGUI.Count("scene.brushShape", GUILayoutUtility.GetLastRect());
            if (next <= 0 || next == current) return;
            string id = BrushStampLibrary.All[next - 1].Id;
            change = () => SetBrushAlpha(id);
        }
    }
}
