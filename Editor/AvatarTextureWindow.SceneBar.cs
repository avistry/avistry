using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        const int PlacedGradientView = -1;
        const float SceneBarWidth = 250f;
        static readonly SelectGlyph[] WorkflowGlyphs = { SelectGlyph.Click, SelectGlyph.Recolour, SelectGlyph.Paint };
        static string s_workflowLanguage;
        static GUIContent[] s_workflowLabels;
        [SerializeField] bool _recolourInScene = true;
        [SerializeField] bool _sceneGradientOptions;
        [SerializeField] bool _sceneToolsCollapsed;
        static GUIStyle s_sceneSelectionButton;

        internal static AvatarTextureWindow SceneBarOwner;

        internal bool SceneBarVisible => _renderer != null && _uv != null && _matchPick == MatchPick.None && !_scenePickArmed;

        internal static AvatarTextureWindow SceneBarWindow()
        {
            if (SceneBarOwner != null && SceneBarOwner._uv != null) return SceneBarOwner;
            foreach (var w in Resources.FindObjectsOfTypeAll<AvatarTextureWindow>())
                if (!w._automationSandbox && w._uv != null && w.SceneBarVisible) return w;
            return SceneBarOwner;
        }

        void ClaimSceneBar()
        {
            if (!_automationSandbox) SceneBarOwner = this;
        }

        void ReleaseSceneBar()
        {
            if (SceneBarOwner != this) return;
            SceneBarOwner = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<AvatarTextureWindow>())
                if (w != this && !w._automationSandbox) { SceneBarOwner = w; break; }
        }

        void OnFocus() => ClaimSceneBar();

        int SceneBarMode => _tab == Tab.Colour ? 1 : _tool == Tool.Paint ? 2 : 0;

        internal string SceneSelectionHoverTip { get; private set; }

        internal void DrawSceneBar(SceneView scene, float availableWidth, float availableHeight, float panelSpace,
            ref (int view, Vector2 offset) details, ref float detailsTop)
        {
            SceneSelectionHoverTip = null;
            if (_uv == null) return;
            float width = availableWidth > 1f ? Mathf.Min(SceneBarWidth, Mathf.Floor(availableWidth)) : SceneBarWidth;
            Action change = null;
            int mode = SceneBarMode;
            int view = mode == 2 ? (PickingForPaint ? 2 : PaintToolIndex + 3) : mode;
            bool showGradientMode = mode == 2 && !PickingForPaint && !MaskEditing
                && PaintToolIndex == 3 && SelectedGradient?.sceneProjection == true;
            if (showGradientMode && EditingPlacedSceneGradient) view = PlacedGradientView;
            if (details.view != view) details = (view, Vector2.zero);
            bool canPaint = CanPaint(out string why);
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(64f, width * 0.43f);
            try
            {
                using (new GUILayout.VerticalScope(GUILayout.Width(width)))
                {
                    DrawSceneBarModes(mode, canPaint, why, width, ref change);
                    int paintToolKeyboard = mode == 2 ? GUIUtility.GetControlID(FocusType.Keyboard) : 0;
                    if (mode != 1) DrawSceneBarInput(mode == 2, canPaint, ref change);
                    if (mode == 2 && canPaint && ScenePainting && _uvPaintLimit != UvPaintLimit.None)
                        DrawScenePaintAreaStatus(width);
                    if (mode == 2 && ScenePainting && EmptyLockedPaintDestination is PaintLayer blockedPaint)
                        DrawSceneAllowNewPaint(width, blockedPaint, ref change);
                    if (mode == 0) DrawSceneSelectionHandoff(scene, width, ref change);
                    if (mode == 2 && canPaint && _tab == Tab.Paint && !MaskEditing)
                        DrawQuickScenePaintSelection(width, ref change);
                    bool isolated = SceneIsIsolated;
                    if (mode == 2 || isolated) DrawSceneIsolation(width, isolated, canPaint, ref change);
                    if (showGradientMode)
                    {
                        using (new EditorGUI.DisabledScope(!canPaint))
                        {
                            DrawSceneGradientMode(width, ref change);
                            DrawSceneGradientFrame(scene, width, ref change);
                        }
                    }
                    if (SceneGradientActive) DrawSceneGradientStopsButton(scene, width, ref change);
                    DrawSceneGradientRecovery(width, ref change);
                    var toolsRect = SceneBarRow(width, 22f);
                    var show = new GUIContent(Loc.T("Tool controls"), _sceneToolsCollapsed
                        ? Loc.T("Show Avistry tool controls") : Loc.T("Fold tool controls to make room for the model"));
                    bool open = StudioGUI.Foldout(toolsRect, !_sceneToolsCollapsed, show);
                    if (open == _sceneToolsCollapsed) change = SetSceneToolsCollapsed;
                    StudioGUI.Count("scene.toolControls", toolsRect);
                    if (!_sceneToolsCollapsed)
                    {
                        if (availableHeight > 1f && (availableHeight < 650f || width < 190f))
                        {
                            var start = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(false));
                            var height = Mathf.Max(72f, Mathf.Floor(availableHeight * .62f) - 90f);
                            if (detailsTop > 0f && float.IsFinite(panelSpace))
                                height = Mathf.Max(1f, Mathf.Min(height, Mathf.Floor(panelSpace - detailsTop - 4f)));
                            DrawScrollableSceneTools(mode, canPaint, width, height, paintToolKeyboard, ref details.offset, ref change);
                            if (Event.current.type == EventType.Repaint) detailsTop = start.yMax;
                        }
                        else
                            DrawSceneBarDetails(mode, canPaint, width, paintToolKeyboard, ref change);
                    }
                }
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; }
            change?.Invoke();
        }

        void DrawScrollableSceneTools(int mode, bool canPaint, float width, float height, int paintToolKeyboard,
            ref Vector2 offset, ref Action change)
        {
            var scrollbar = GUI.skin.verticalScrollbar;
            float contentWidth = Mathf.Max(1f, width - scrollbar.fixedWidth - scrollbar.margin.horizontal - 2f);
            using (var scroll = new EditorGUILayout.ScrollViewScope(offset, false, false,
                GUIStyle.none, scrollbar, GUIStyle.none, GUILayout.Width(width), GUILayout.MaxHeight(height)))
            {
                offset = scroll.scrollPosition;
                using (new GUILayout.VerticalScope(GUILayout.Width(contentWidth)))
                    DrawSceneBarDetails(mode, canPaint, contentWidth, paintToolKeyboard, ref change);
            }
            StudioGUI.Count("scene.toolScroll", GUILayoutUtility.GetLastRect());
        }

        static Rect SceneBarRow(float width, float height = 18f)
        {
            var rect = EditorGUILayout.GetControlRect(false, height);
            rect.width = Mathf.Min(rect.width, width);
            return rect;
        }

        static GUIContent SceneSliderLabel(GUIContent label, float width)
        {
            if (width >= StackedChoiceWidth && EditorStyles.label.CalcSize(label).x + 4f <= EditorGUIUtility.labelWidth)
                return label;
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.MaxWidth(width));
            return GUIContent.none;
        }

        static float DrawSceneSlider(GUIContent label, float value, float min, float max, float width, string control)
        {
            label = SceneSliderLabel(label, width);
            var rect = SceneBarRow(width);
            float next = EditorGUI.Slider(rect, label, value, min, max);
            StudioGUI.Count(control, rect);
            return next;
        }

        void SetSceneToolsCollapsed()
        {
            Undo.RecordObject(this, "Toggle Avistry Scene controls");
            _sceneToolsCollapsed = !_sceneToolsCollapsed;
            SelectUiChanged();
        }

        void DrawSceneSelectionActions(SceneView scene, float width, ref Action change)
        {
            if (PendingLayerLimit is Layer limitTarget)
            {
                using (new EditorGUI.DisabledScope(!CanApplyLayerLimit(limitTarget)))
                    if (SceneSelectionButton(width, "scene.applyLayerLimit", new GUIContent(LayerLimitLabel, LayerLimitPrompt(limitTarget))))
                        change = () => ApplyPendingLayerLimitFromView(limitTarget, scene);
                return;
            }

            if (!CanPaintCurrentSelection) return;
            var paint = PaintSelectionContent();
            var gradient = CanGradientCurrentSelection ? GradientSelectionContent() : null;
            const string paintId = "scene.paintSelected";
            const string gradientId = "scene.gradientSelected";
            float paintWidth = StudioGUI.SelectionCellWidth(paint);
            float gradientWidth = gradient == null ? 0f : StudioGUI.SelectionCellWidth(gradient);
            var paintRect = SceneBarRow(width, 25f);
            Rect gradientRect = default;
            if (gradient != null)
            {
                if (paintWidth + gradientWidth + 4f <= width)
                {
                    float split = Mathf.Floor((paintRect.width - 4f) * paintWidth / (paintWidth + gradientWidth));
                    gradientRect = new Rect(paintRect.x + split + 4f, paintRect.y, paintRect.width - split - 4f, paintRect.height);
                    paintRect.width = split;
                }
                else gradientRect = SceneBarRow(width, 25f);
            }
            if (StudioGUI.SelectionCell(paintRect, SelectGlyph.PaintSelected, paint, false, true)) change = PaintCurrentSceneSelection;
            StudioGUI.Count(paintId, paintRect);
            if (gradient == null) return;
            if (StudioGUI.SelectionCell(gradientRect, SelectGlyph.Gradient, gradient, false, true)) change = GradientCurrentSceneSelection;
            StudioGUI.Count(gradientId, gradientRect);
        }

        void DrawSceneSelectionHandoff(SceneView scene, float width, ref Action change)
        {
            int selected = SelectedElementCount;
            bool faces = HasFaceSelection || selected == 0 && FaceSelectionTool;
            GUILayout.Label(SceneSelectionSummary(selected, faces), EditorStyles.miniLabel, GUILayout.MaxWidth(width));
            StudioGUI.Count("scene.selectionCount", GUILayoutUtility.GetLastRect());
            DrawSceneSelectionActions(scene, width, ref change);
        }

        static string SceneSelectionSummary(int selected, bool faces)
        {
            string plural = selected == 1 ? "" : "s";
            return faces ? Loc.F("{0} face{1} selected", selected, plural) : Loc.F("{0} piece{1} selected", selected, plural);
        }

        static bool SceneSelectionButton(float width, string controlId, GUIContent content)
        {
            s_sceneSelectionButton ??= new GUIStyle(EditorStyles.miniButton) { wordWrap = true, fixedHeight = 0f };
            float height = Mathf.Max(18f, s_sceneSelectionButton.CalcHeight(content, Mathf.Max(1f, width - 6f)));
            var rect = SceneBarRow(width, height);
            bool clicked = GUI.Button(rect, content, s_sceneSelectionButton);
            StudioGUI.Count(controlId, rect);
            return clicked;
        }

        void DrawSceneBarDetails(int mode, bool canPaint, float width, int paintToolKeyboard, ref Action change)
        {
            if (mode == 1) DrawSceneBarRecolour(width, ref change);
            else if (mode == 2) DrawSceneBarPaint(canPaint, width, paintToolKeyboard, ref change);
            else DrawSceneBarSelect(width);

            if (mode == 0 || mode == 2 && canPaint && _tab == Tab.Paint && !MaskEditing) return;
            string selection = SceneSelectionSummary(HighlightElements.Count, HighlightUsesFaces);
            var row = SceneBarRow(width, 24f);
            var spot = new Rect(row.xMax - 26f, row.y, 26f, row.height);
            var wire = new Rect(spot.x - 29f, row.y, 26f, row.height);
            GUI.Label(new Rect(row.x + 2f, row.y, Mathf.Max(0f, wire.x - row.x - 5f), row.height), selection, EditorStyles.miniLabel);
            DrawSceneViewGuides(wire, spot);
        }

        void DrawSceneViewGuides(Rect wire, Rect spotlight)
        {
            if (SceneToolCell(wire, SelectGlyph.Wire, SelectionLabel(SelectGlyph.Wire), SceneUvWireEnabled))
                SetSceneUvWire(!SceneUvWireEnabled);
            if (SceneToolCell(spotlight, SelectGlyph.Spotlight, SelectionLabel(SelectGlyph.Spotlight), _focusWhilePainting))
            {
                Undo.RecordObject(this, "Toggle Spotlight");
                _focusWhilePainting = !_focusWhilePainting;
                SelectUiChanged();
            }
            StudioGUI.Count("scene.uvWire", wire);
            StudioGUI.Count("scene.spotlight", spotlight);
        }

        void DrawSceneBarModes(int mode, bool canPaint, string why, float width, ref Action change)
        {
            int keyboard = GUIUtility.GetControlID(FocusType.Keyboard);
            if (s_workflowLabels == null || s_workflowLanguage != Loc.Current)
            {
                s_workflowLanguage = Loc.Current;
                string tip = " " + Loc.T("Use arrow keys after choosing a tool, or hold Shift while scrolling here.");
                s_workflowLabels = new[] {
                    new GUIContent(Loc.T("Select"), Loc.T("Switch to selecting pieces on the model") + tip),
                    new GUIContent(Loc.T("Recolour"), Loc.T("Adjust colours while looking at the model") + tip),
                    new GUIContent(Loc.T("Paint"), Loc.T("Switch to painting on the model") + tip)
                };
            }
            int active = PickingForPaint ? 0 : mode;
            string paintTip = s_workflowLabels[2].tooltip;
            if (!canPaint) s_workflowLabels[2].tooltip = why;
            int next = ToolPalette(active, WorkflowGlyphs, s_workflowLabels, width, true, keyboard: keyboard);
            s_workflowLabels[2].tooltip = paintTip;
            StudioGUI.Count("scene.workflow", GUILayoutUtility.GetLastRect());
            if (next == active) return;
            bool keepFocus = GUIUtility.keyboardControl == keyboard;
            change = () =>
            {
                SetSceneBarMode(next);
                if (keepFocus) GUIUtility.keyboardControl = keyboard;
            };
        }

        void SetSceneBarMode(int mode)
        {
            if (mode == 0) SetSceneBarInput(Tool.Select, true);
            else if (mode == 2)
            {
                if (PickingForPaint) ToggleScenePaintPicking(false);
                else if (CanPaint(out var why)) SetSceneBarInput(Tool.Paint, true);
                else Toast(why, MessageType.Warning);
            }
            else if (mode == 1)
                ChangeSceneBar("Recolour on model", () =>
                {
                    SetTab(Tab.Colour);
                    _recolourInScene = true;
                    if (!ByPart && !Recolour.Supports(_material) && CanRecolourPieces) EnterByPart();
                });
        }

        bool CanRecolourPieces => _comp != null && _uv != null && MaterialMainTexture is Texture2D main &&
            _slots.Exists(slot => slot.texture == main && slot.role == TextureRole.Color);

        void DrawSceneBarRecolour(float width, ref Action change)
        {
            bool materialColour = Recolour.Supports(_material);
            if (materialColour)
            {
                int scope = DrawRecolourChoice(true, ByPart ? 1 : 0, width, true);
                if (scope != (ByPart ? 1 : 0))
                    change = () => ChangeSceneBar("Change recolour scope", () =>
                    {
                        if (scope == 1) EnterByPart();
                        else LeaveByPart();
                    });
            }
            if (!materialColour && !ByPart)
            {
                if (CanRecolourPieces && SceneToolCell(SceneBarRow(width, 26f), SelectGlyph.Piece,
                        new GUIContent(Loc.T("Pieces"), Loc.T("Recolour pieces"))))
                    change = () => ChangeSceneBar("Recolour pieces", EnterByPart);
                else if (!CanRecolourPieces)
                    GUILayout.Label(Loc.T("Pick a part with a colour texture to recolour it here."), StudioGUI.Hint);
                return;
            }

            if (ByPart)
            {
                DrawColourPartActions(width, true, ref change);
                var part = CurrentPart;
                if (part == null || !MainSlotEdited)
                {
                    GUILayout.Label(MainSlotEdited ? Loc.T("Click a piece to recolour it.") : CanRecolourPieces
                        ? Loc.T("Open the main colour texture to recolour pieces.")
                        : Loc.T("Pick a part with a colour texture to recolour it here."), StudioGUI.Hint);
                    if (!MainSlotEdited && CanRecolourPieces && GUILayout.Button(Loc.T("Open main colour texture"), EditorStyles.miniButton))
                        change = QueuePrepareByPart;
                    return;
                }
                int currentMethod = part.IsTint ? 1 : 0;
                int method = DrawRecolourChoice(false, currentMethod, width, true);
                StudioGUI.Count("scene.recolourMethod", GUILayoutUtility.GetLastRect());
                if (method != currentMethod)
                    change = () => ChangeRecolourChoice(() => SetPartMode(part,
                        method == 1 ? RecolourPartLayer.PartMode.Tint : RecolourPartLayer.PartMode.Shift));
                if (part.IsTint) DrawPartTintControls(part, width);
                else DrawSceneBarColourSliders(part, width);
                return;
            }

            if (_rcSessionFor != _material)
            {
                if (GUILayout.Button(Loc.T("Start recolouring"), EditorStyles.miniButton))
                    change = () => { KeepRecolourBeforeSwitch(); StartRecolourSession(); };
                return;
            }
            if (MaterialWriter.IsPoiyomiLocked(_material))
            {
                GUILayout.Label(Loc.T("Open Recolour in Avistry to unlock this material."), StudioGUI.Hint);
                return;
            }
            FinalizeRecolourIfIdle();
            DrawSceneBarColourSliders(null, width);
            using (new GUILayout.HorizontalScope())
            {
                if (_rcEntries.Count > 0) DrawMainTintAction(true);
                if (CanAddColourMatchLayer(out _)) DrawAddColourMatchButton(width);
            }
        }

        void DrawSceneBarColourSliders(RecolourPartLayer part, float width)
        {
            var current = part != null ? part.Settings : _rc;
            EditorGUI.BeginChangeCheck();
            float hue = DrawSceneSlider(new GUIContent(Loc.T("Hue")), current.hue, -180f, 180f, width, "scene.recolourHue");
            float saturation = DrawSceneSlider(new GUIContent(Loc.T("Saturation")), current.saturation, -1f, 1f, width, "scene.recolourSaturation");
            float brightness = DrawSceneSlider(new GUIContent(Loc.T("Brightness")), current.brightness, -1f, 1f, width, "scene.recolourBrightness");
            if (!EditorGUI.EndChangeCheck()) return;
            if (part != null) SetPartValues(part, hue, saturation, brightness);
            else
            {
                Undo.RecordObject(this, "Recolour");
                _rc.hue = hue;
                _rc.saturation = saturation;
                _rc.brightness = brightness;
                ApplyRecolour();
            }
        }

        void DrawSceneBarInput(bool paint, bool canPaint, ref Action change)
        {
            bool active = paint && !PickingForPaint ? (ScenePainting || SceneGradientActive) : ScenePicking;
            string label = paint && !PickingForPaint ? Loc.T("Paint on model") : Loc.T("Select on model");
            using (new EditorGUI.DisabledScope(paint && !canPaint))
            {
                var toggle = new GUIContent(label,
                    Loc.T("Turn off to use the Scene view normally. Your tools and selected pieces stay in place."));
                bool on = EditorGUILayout.ToggleLeft(toggle, active);
                if (on != active) change = () => SetSceneBarInput(paint ? Tool.Paint : Tool.Select, on);
            }
        }

        void SetSceneBarInput(Tool tool, bool enabled)
        {
            if (tool == Tool.Paint && !CanPaint(out _)) return;
            ChangeSceneBar(tool == Tool.Paint ? "Paint on model" : "Select on model", () =>
            {
                if (enabled) SetTab(tool == Tool.Paint ? Tab.Paint : Tab.Select);
                if (tool == Tool.Paint)
                {
                    _paintInScene = enabled;
                    if (enabled && MaskEditing && PaintToolIndex >= 3) SetPaintTool(0);
                }
                else
                {
                    _pickInScene = enabled;
                    _hoverIsland = -1;
                    if (!enabled) CancelMarquee();
                }
            });
        }

        void ChangeSceneBar(string action, Action change)
        {
            EndStroke();
            CancelMarquee();
            CancelGradientDrag();
            CancelSceneGradient();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, action);
            change();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            SelectUiChanged();
        }

        void DrawSceneBarSelect(float width, bool onModel = true)
        {
            var captionRow = onModel ? SceneBarRow(width, 24f) : default;
            DrawSelectModeStrip(onModel, width);
            DrawSelectModeOptions(width);
            if (onModel && width >= 178f)
            {
                float half = (width - 10f) * .5f;
                using (new GUILayout.HorizontalScope())
                {
                    using (new GUILayout.VerticalScope(GUILayout.Width(half))) DrawSelectOpSwitch(half, true);
                    GUILayout.Space(6f);
                    using (new GUILayout.VerticalScope(GUILayout.Width(half))) DrawSelectionSetActions(half, true);
                }
            }
            else
            {
                DrawSelectOpSwitch(width, onModel);
                DrawSelectionSetActions(width, onModel);
            }
            if (onModel)
            {
                var spotlight = new Rect(captionRow.xMax - 26f, captionRow.y, 26f, captionRow.height);
                var wire = new Rect(spotlight.x - 29f, captionRow.y, 26f, captionRow.height);
                var wireLabel = SelectionLabel(SelectGlyph.Wire);
                var spotLabel = SelectionLabel(SelectGlyph.Spotlight);
                DrawSceneViewGuides(wire, spotlight);
                var pointer = Event.current.mousePosition;
                var hover = wire.Contains(pointer) ? wireLabel : spotlight.Contains(pointer) ? spotLabel : _hoveredSelection;
                if (hover != null) ShowSceneToolTip(hover);
                var caption = hover ?? _focusedSelection ?? SelectionLabel((SelectGlyph)_selectMode);
                GUI.Label(new Rect(captionRow.x + 4f, captionRow.y, Mathf.Max(0f, wire.x - captionRow.x - 8f), captionRow.height), caption, EditorStyles.miniBoldLabel);
            }
        }

        void DrawSceneBarPaint(bool canPaint, float width, int keyboard, ref Action change)
        {
            using (new EditorGUI.DisabledScope(!canPaint))
            {
                int toolIndex = PaintToolIndex;
                bool mask = MaskEditing;
                if (!mask && _tab == Tab.Paint)
                {
                    if (PickingForPaint) { DrawSceneBarSelect(width); return; }
                }
                int shown = mask && toolIndex >= 3 ? -1 : toolIndex;
                int selected = PaintToolPalette(shown, mask, width, scene: true, keyboard: keyboard);
                StudioGUI.Count("scene.paintTools", GUILayoutUtility.GetLastRect());
                if (selected != shown)
                    change = () => ChangeSceneBar("Select paint tool", () =>
                    {
                        if (_tab != Tab.Paint) SetTab(Tab.Paint);
                        SetPaintTool(selected);
                    });

                if (!mask && (toolIndex == 0 || toolIndex == 2)) DrawScenePaintColours(width, toolIndex == 0);
                if (toolIndex != 2 && toolIndex != 3) DrawSceneBrushPreset(width, ref change);
                if (mask && toolIndex == 2)
                {
                    GUILayout.Label(new GUIContent(Loc.T("On the mask"), Loc.T("What a fill does to the layer mask")), EditorStyles.miniLabel);
                    int action = _maskFillHides ? 1 : 0;
                    int fill = ToolPalette(action, MaskFillGlyphs, PaintLabels.maskFill, width, scene: true);
                    StudioGUI.Count("scene.maskFillAction", GUILayoutUtility.GetLastRect());
                    if (fill != action)
                        change = () => ChangeSceneBar("Set mask fill action", () => _maskFillHides = fill == 1);
                }
                if (toolIndex == 2) DrawSceneFillSettings(width, ref change);
                else if (!mask) DrawScenePaintVariant(toolIndex, width, ref change);
                if (_symmetry && MirrorNeedsClassicAxis) GUILayout.Label(MirrorClassicHint, StudioGUI.Hint);
                if (toolIndex != 3)
                {
                    DrawScenePaintStrength(toolIndex, width);
                    DrawScenePaintOptions(toolIndex, width, ref change);
                }
            }
        }

        void DrawScenePaintVariant(int tool, float width, ref Action change)
        {
            if (tool == 3) DrawSceneGradientControls(width, ref change);
            else if (tool == 4)
            {
                int kind = _brush.blendKind == StrokeTool.Smudge ? 1 : 0;
                int next = ToolPalette(kind, BlendGlyphs, PaintLabels.blend, width, scene: true);
                StudioGUI.Count("scene.blendKind", GUILayoutUtility.GetLastRect());
                if (next != kind)
                    change = () => ChangeSceneBar("Change blend tool", () =>
                    {
                        _brush.blendKind = next == 1 ? StrokeTool.Smudge : StrokeTool.Blur;
                    });
                DrawSceneBlendAmount(width);
            }
        }

        void DrawSceneGradientControls(float width, ref Action change)
        {
            var placed = SelectedGradient;
            if (EditingPlacedSceneGradient)
            {
                DrawPlacedSceneGradientControls(placed, width, ref change);
                return;
            }
            if (placed?.sceneProjection != true) GUILayout.Label(Loc.T("New gradient"), EditorStyles.miniLabel);
            if (!Advanced) DrawGradientDirection(width);
            EditorGUI.BeginChangeCheck();
            var rampRect = SceneBarRow(width);
            var ramp = EditorGUI.GradientField(rampRect, new GUIContent(Loc.T("Colours"),
                Loc.T("Edit the colours and opacity of the next gradient.")), NewGradientRamp(_grad.colours));
            bool rampChanged = EditorGUI.EndChangeCheck();
            StudioGUI.Count("scene.gradientRamp", rampRect);

            var shape = (GradientShape)ToolPalette((int)_grad.shape, GradientShapeGlyphs, PaintLabels.gradientShape, width, scene: true);
            StudioGUI.Count("scene.gradientShape", GUILayoutUtility.GetLastRect());
            float flatness = shape == GradientShape.Ellipse
                ? DrawSceneSlider(new GUIContent(Loc.T("Flatness")), _grad.flatness, .05f, 1f, width, "scene.newGradientFlatness")
                : _grad.flatness;
            if (rampChanged || shape != _grad.shape)
            {
                change = () => ChangeSceneBar("Edit new gradient settings", () =>
                {
                    if (rampChanged) StoreCustomGradientRamp(ramp);
                    _grad.shape = shape;
                    _grad.flatness = flatness;
                });
            }
            else if (flatness != _grad.flatness)
            {
                Undo.RecordObject(this, "Adjust gradient flatness");
                _grad.flatness = flatness;
                SelectUiChanged();
            }
            DrawScenePaintStrength(3, width);

            var options = new GUIContent(Loc.T("Gradient options"), Loc.T("Choose a colour source, direction or detailed stop editor."));
            var optionsRect = SceneBarRow(width);
            _sceneGradientOptions = StudioGUI.Foldout(optionsRect, _sceneGradientOptions, options);
            StudioGUI.Count("scene.gradientOptions", optionsRect);
            if (!_sceneGradientOptions) return;

            var colours = DrawGradientColourChoice(new GUIContent(Loc.T("Source")), width);
            StudioGUI.Count("scene.gradientSource", GUILayoutUtility.GetLastRect());
            var blend = DrawSceneGradientBlendChoice(_grad.blend, "scene.newGradientBlend", width);
            var edge = DrawGradientEdgeChoice(_grad.edge, "scene.newGradientEdge", width);
            if (edge != _grad.edge || blend != _grad.blend)
                change = () => ChangeSceneBar("Edit new gradient settings", () =>
                {
                    _grad.edge = edge;
                    _grad.blend = blend;
                });
            else if (!rampChanged && colours != _grad.colours)
                change = () => ChangeSceneBar("Change gradient colours", () => _grad.colours = colours);
            if (Advanced) DrawGradientDirection(width);
            if (placed != null && !placed.sceneProjection &&
                GUI.Button(SceneBarRow(width), Loc.T("Edit selected gradient"), EditorStyles.miniButton))
                change = OpenPlacedSceneGradientInPaint;
        }

    }
}
