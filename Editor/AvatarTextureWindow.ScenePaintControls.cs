using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _scenePaintOptions;

        void DrawSceneAllowNewPaint(float width, PaintLayer paint, ref Action change)
        {
            const string control = "scene.allowNewPaint";
            var row = SceneBarRow(width);
            var content = StudioGUI.Content(control, "Allow new paint");
            content.tooltip = EmptyPaintLockMessage + " " + content.tooltip;
            if (GUI.Button(row, content, EditorStyles.miniButton))
            {
                var recipe = _recipe;
                var compositor = _comp;
                change = () => AllowNewPaint(recipe, compositor, paint);
            }
            StudioGUI.Count(control, row);
        }

        void DrawScenePaintAreaStatus(float width)
        {
            const string control = "scene.paintAreaStatus";
            const float inset = 7f;
            var content = StudioGUI.Content(control, "Paint area limited");
            var style = StudioGUI.Hint;
            var row = SceneBarRow(width, Mathf.Max(18f, style.CalcHeight(content, Mathf.Max(1f, width - inset)) + 2f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(row.x + 1f, row.y + 1f, 2f, row.height - 2f), StudioGUI.InfoBlue);
            var label = new Rect(row.x + inset, row.y + 1f, row.width - inset, row.height - 2f);
            GUI.Label(label, content, style);
            StudioGUI.Count(control, row);
        }

        void DrawScenePaintColours(float width, bool second)
        {
            var row = SceneBarRow(width, second ? 33f : 18f);
            if (second)
            {
                const float swapWidth = 22f, gap = 3f;
                float swatchWidth = Mathf.Max(1f, (row.width - swapWidth - 2f * gap) / 2f);
                var main = new Rect(row.x, row.y, swatchWidth, 18f);
                var swap = new Rect(main.xMax + gap, row.y, swapWidth, main.height);
                var sub = new Rect(swap.xMax + gap, row.y, swatchWidth, main.height);
                DrawScenePaintSwatch(main, true);
                DrawScenePaintSwatch(sub, false);
                if (GUI.Button(swap, new GUIContent("↔", Loc.T("Swap main and second colour (X)")), EditorStyles.miniButton))
                {
                    SwapColours();
                    SceneView.RepaintAll();
                }
                StudioGUI.Count("scene.swapColours", swap);
                GUI.Label(new Rect(main.x, main.yMax, main.width, 15f), Loc.T("Main"), EditorStyles.miniLabel);
                GUI.Label(new Rect(sub.x, sub.yMax, sub.width, 15f), Loc.T("Second"), EditorStyles.miniLabel);
            }
            else DrawScenePaintSwatch(EditorGUI.PrefixLabel(row, new GUIContent(Loc.T("Colour"))), true);
            DrawRecentTiles(width, "scene.recentColours");
        }

        void DrawScenePaintSwatch(Rect rect, bool main)
        {
            HandleBrushColourCode(rect, main);
            var old = main ? _brush.color : _brush.subColor;
            var hint = main ? Loc.T("Main colour. Click for the colour wheel and sliders.")
                : Loc.T("Second colour: X swaps it with the main colour. Used by Colour variety per stroke and gradients.");
            var picked = EditorGUI.ColorField(rect, new GUIContent("", hint + " " + Loc.T("Right-click for colour codes.")), old, false, true, false);
            StudioGUI.Count(main ? "scene.mainColour" : "scene.secondColour", rect);
            if (picked == old) return;
            Undo.RecordObject(this, "Change brush colour");
            if (main) _brush.color = picked;
            else _brush.subColor = picked;
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawSceneBrushPreset(float width, ref Action change)
        {
            var builtIn = BrushLibrary.BuiltIn;
            var saved = BrushLibrary.User;
            var choices = new int[builtIn.Length + saved.Count];
            var labels = new GUIContent[choices.Length];
            int current = -1;
            bool activeUser = ActiveBrushIsUser;
            string keepsSize = Loc.T("Keeps your current brush size.");
            for (int i = 0; i < choices.Length; i++)
            {
                bool user = i >= builtIn.Length;
                var brush = user ? saved[i - builtIn.Length] : builtIn[i];
                string label = user ? Loc.T("My brushes") + "/★ " + NoSubmenu(brush.name)
                    : Loc.T(BrushLibrary.CategoryOf(brush)) + "/" + NoSubmenu(BrushLabel(brush.name));
                choices[i] = i;
                if (brush.name == _brush.presetName && user == activeUser)
                {
                    current = i;
                    if (_brush.edited) label += "*";
                }
                labels[i] = new GUIContent(label, label + "\n" + keepsSize);
            }
            int next = SteppedChoice(new GUIContent(Loc.T("Brush"), keepsSize), current, choices,
                (rect, value) => EditorGUI.Popup(rect, value, labels), width);
            StudioGUI.Count("scene.brushPreset", GUILayoutUtility.GetLastRect());
            if (next != current && next >= 0)
            {
                bool fromSaved = next >= builtIn.Length;
                var preset = fromSaved ? saved[next - builtIn.Length] : builtIn[next];
                change = () => ChangeSceneBar(Loc.T("Choose brush preset"), () => SetBrushPreset(preset, fromSaved));
            }
            DrawBristleLoad(width);
            if (_brush.shape?.tip == BrushTip.Drips)
                GUILayout.Label(Loc.T("On the model, paint runs down the surface. On the texture, it runs toward the bottom."),
                    StudioGUI.Hint, GUILayout.MaxWidth(width));
            if (_brush.shape == null || (_brush.shape.tip != BrushTip.Spray && _brush.shape.scatter <= 0f)) return;
            GUILayout.Label(Loc.T("This brush scatters dots around the ring."), StudioGUI.Hint, GUILayout.MaxWidth(width));
            var smooth = builtIn[0];
            var smoothRect = SceneBarRow(width);
            if (GUI.Button(smoothRect, Loc.F("Use {0}", BrushLabel(smooth.name)), EditorStyles.miniButton))
                change = () => ChangeSceneBar(Loc.T("Choose brush preset"), () => SetBrushPreset(smooth, false));
            StudioGUI.Count("scene.smoothBrush", smoothRect);
        }

        void DrawScenePaintStrength(int tool, float width)
        {
            bool brush = tool != 2 && tool != 3;
            float size = _brush.size;
            if (brush)
                size = DrawBrushSize(Loc.T("Size"), width, "scene.brushPresetSize", scene: true);
            float strength = DrawSceneSlider(new GUIContent(tool == 3 ? Loc.T("Opacity") : Loc.T("Strength"),
                tool == 3 ? Loc.T("Layer opacity of new gradient layers") : Loc.T("How strong one stroke (or fill) is")),
                _brush.opacity, 0f, 1f, width, tool == 3 ? "scene.newGradientOpacity" : "scene.paintStrength");
            float hardness = _brush.shape.hardness;
            if (brush)
                hardness = DrawSceneSlider(new GUIContent(Loc.T("Hardness"), Loc.T("0 = soft edge, 1 = hard edge")),
                    hardness, 0f, 1f, width, "scene.brushHardness");
            bool styleChanged = strength != _brush.opacity || hardness != _brush.shape.hardness;
            if (size == _brush.size && !styleChanged) return;
            Undo.RecordObject(this, "Change paint tool settings");
            _brush.size = size;
            _brush.opacity = strength;
            _brush.shape.hardness = hardness;
            if (styleChanged) MarkBrushEdited();
            else
            {
                Repaint();
                SceneView.RepaintAll();
            }
        }

        void DrawSceneBlendAmount(float width)
        {
            bool smudge = _brush.blendKind == StrokeTool.Smudge;
            GUILayout.Label(smudge ? Loc.T("Smudge length") : Loc.T("Blur amount"), EditorStyles.miniLabel);
            var rect = SceneBarRow(width);
            float amount = EditorGUI.Slider(rect, _brush.blendAmount, .1f, 3f);
            StudioGUI.Count("scene.blendAmount", rect);
            var scope = new GUIContent(Loc.T("Stay in piece"), Loc.T("Only blend within the same piece"));
            var scopeRect = SceneBarRow(width);
            bool stay = EditorGUI.ToggleLeft(scopeRect, scope, _brush.blendKeepIsland);
            StudioGUI.Count("scene.blendScope", scopeRect);
            if (amount == _brush.blendAmount && stay == _brush.blendKeepIsland) return;
            Undo.RecordObject(this, "Change blend tool settings");
            _brush.blendAmount = amount;
            _brush.blendKeepIsland = stay;
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawScenePaintOptions(int tool, float width, ref Action change)
        {
            if (tool != 2) DrawPenControls(width);
            var fold = SceneBarRow(width);
            bool open = StudioGUI.Foldout(fold, _scenePaintOptions, new GUIContent(Loc.T("Painting options")));
            StudioGUI.Count("scene.paintOptions", fold);
            if (open != _scenePaintOptions)
                change = () => ChangeSceneBar("Toggle painting options", () => _scenePaintOptions = open);
            if (!_scenePaintOptions) return;

            if (tool != 2)
            {
                DrawBrushTip(width);
                DrawSceneBrushAlpha(width, ref change);
                DrawBrushDotSize(width);
                var join = new GUIContent(Loc.T("Smooth across joins"),
                    Loc.T("On the model, the brush is a round spot on the surface: lines continue cleanly across UV seams and aren't stretched by the texture layout."));
                bool smooth = EditorGUI.ToggleLeft(SceneBarRow(width), join, _brush.surfaceBrush);
                if (smooth != _brush.surfaceBrush)
                {
                    Undo.RecordObject(this, "Toggle smooth joins");
                    _brush.surfaceBrush = smooth;
                    Repaint();
                    SceneView.RepaintAll();
                }
                if (smooth && BlendToolActive && _brush.blendKind == StrokeTool.Smudge)
                    GUILayout.Label(Loc.T("Smudge is painted flat on the texture, so it stops at seams. Blur crosses them."), StudioGUI.Hint);
                else if (smooth && !_brush.shape.SurfaceCapable)
                    GUILayout.Label(Loc.T("This brush tip is painted flat on the texture, so it stops at seams. Round tips without scatter cross them."), StudioGUI.Hint);
            }
            DrawSceneMirrorSettings(width, ref change);
            DrawPaintUvLimit(width);
        }
    }
}
