using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class AvatarTextureWindow
    {
        const float StatusHeight = 20f;

        Vector2 _pan, _panelScroll, _lastCanvasSize;
        float _zoom;
        bool _viewFit = true;
        bool _panning;
        bool CanvasPainting => _tab == Tab.Paint && _tool == Tool.Paint && !PickingForPaint;
        bool CanvasSelecting => _tool == Tool.Select || PickingForPaint;
        bool CanvasImagePlacement => CanvasPainting || _tab == Tab.Select;
        Texture2D _selOverlay, _checker;
        Vector3[] _selSegments = System.Array.Empty<Vector3>();

        Texture2D _focusOverlay;
        string _focusKey;
        Vector3[] _focusSegments = System.Array.Empty<Vector3>();

        void DestroyCanvasResources()
        {
            if (_selOverlay != null) DestroyImmediate(_selOverlay);
            if (_checker != null) DestroyImmediate(_checker);
            if (_focusOverlay != null) DestroyImmediate(_focusOverlay);
            _selOverlay = _checker = _focusOverlay = null;
            _focusKey = null;
            _brushPreview?.Dispose();
            _chipThumbs?.Dispose();
            DestroyColourResources();
            _brushPreview = null;
            _chipThumbs = null;
        }

        void DrawToolbar(Rect r)
        {
            var button = EditorStyles.toolbarButton;
            var dropdown = EditorStyles.toolbarDropDown;
            var toolbar = EditorStyles.toolbar;
            bool showingOriginal = DisplayingOriginal;
            bool canPaint = CurrentSlot == null || CurrentSlot.role != TextureRole.Normal;
            var select = StudioGUI.Icon("Grid.BoxTool", Loc.T(" Select"), Loc.T("Select pieces: the separate pieces of the texture sheet (S)"));
            var paint = StudioGUI.Icon("Grid.PaintTool", Loc.T(" Paint"), PickingForPaint
                ? Loc.T("Back to paint")
                : canPaint ? Loc.T("Paint with a brush (B). E = eraser, I = eyedropper.")
                    : Loc.T("Normal maps can't be painted."));
            var spotlight = new GUIContent(Loc.T("Spotlight"),
                Loc.T("While painting, dim everything outside the selected pieces (or the Paint layer's piece mask) " +
                "and hide the orange selection, so you can see what you're painting."));
            var mirror = new GUIContent(Loc.T("Mirror"), GradientToolActive
                ? Loc.T("Gradients aren't mirrored (they're layers across the texture).")
                : MirrorNeedsClassicAxis ? MirrorClassicHint
                : Loc.T("Symmetry: also paint the mirror image on the other side of the model (see Symmetry in 4 · Paint)"));
            var compare = new GUIContent(Loc.T("Original"),
                Loc.T("Compare: shows the untouched original texture, here and on the avatar. Your edits are hidden while it's on."));
            var view = new GUIContent(Loc.T("View"), Loc.T("What the texture view and avatar show"));
            var settings = StudioGUI.Icon("Settings", Loc.T("Settings"), Loc.T("Settings, language and help"));
            var settingsIcon = StudioGUI.Icon("Settings", "", Loc.T("Settings, language and help"));
            float settingsLabelWidth = ToolbarItemWidth(button, settings), settingsIconWidth = ToolbarItemWidth(button, settingsIcon);

            float selectWidth = ToolbarItemWidth(button, select), paintWidth = ToolbarItemWidth(button, paint);
            float spotlightWidth = ToolbarItemWidth(button, spotlight), mirrorWidth = ToolbarItemWidth(button, mirror);
            float compareWidth = ToolbarItemWidth(button, compare), viewWidth = ToolbarItemWidth(dropdown, view);
            float withoutCompare = selectWidth + paintWidth + spotlightWidth + mirrorWidth + viewWidth + toolbar.padding.horizontal;
            const float frameWidth = 28f;
            float modeWidth = Mathf.Max(64f, Mathf.Max(ToolbarItemWidth(button, new GUIContent(Loc.T("Simple"))),
                ToolbarItemWidth(button, new GUIContent(Loc.T("Advanced")))));
            bool showMode = selectWidth + paintWidth + compareWidth + viewWidth + modeWidth + settingsIconWidth + toolbar.padding.horizontal <= r.width;
            bool inlinePaintViews = withoutCompare + compareWidth + settingsIconWidth + (showMode ? modeWidth : 0f) <= r.width;
            if (!inlinePaintViews) view.tooltip = Loc.T("Spotlight, Mirror and display options");
            bool wideActions = false;
            var mode = new GUIContent(Advanced ? Loc.T("Advanced") : Loc.T("Simple"), ModeSwitchTip);
            if (r.width >= 480f && inlinePaintViews)
            {
                string original = Loc.T("Compare: Original"), originalOn = Loc.T("Compare: Original (on)");
                float fullCompareWidth = Mathf.Max(ToolbarItemWidth(button, new GUIContent(original)),
                    ToolbarItemWidth(button, new GUIContent(originalOn)));
                wideActions = withoutCompare + fullCompareWidth + frameWidth + modeWidth + settingsIconWidth <= r.width;
                if (wideActions)
                {
                    compare.text = showingOriginal ? originalOn : original;
                    compareWidth = fullCompareWidth;
                }
            }
            float used = selectWidth + paintWidth + compareWidth + viewWidth + toolbar.padding.horizontal +
                (inlinePaintViews ? spotlightWidth + mirrorWidth : 0f) + (wideActions ? frameWidth : 0f) + (showMode ? modeWidth : 0f);
            bool settingsLabel = used + settingsLabelWidth <= r.width;

            GUILayout.BeginArea(r);
            StudioGUI.Count("tab.toolbar");
            using (new EditorGUILayout.HorizontalScope(toolbar))
            {
                ToolButton(Tool.Select, select, selectWidth, _comp != null);
                ToolButton(Tool.Paint, paint, paintWidth, _comp != null && canPaint);
                if (inlinePaintViews)
                {
                    using (new EditorGUI.DisabledScope(!CanvasPainting))
                        _focusWhilePainting = GUILayout.Toggle(_focusWhilePainting, spotlight, button, GUILayout.Width(spotlightWidth));
                    using (new EditorGUI.DisabledScope(!CanvasPainting || GradientToolActive || MirrorNeedsClassicAxis))
                    {
                        var oldSymBg = GUI.backgroundColor;
                        if (_symmetry && !GradientToolActive && !MirrorNeedsClassicAxis) GUI.backgroundColor = StudioGUI.Accent;
                        bool sym = GUILayout.Toggle(_symmetry, mirror, button, GUILayout.Width(mirrorWidth));
                        GUI.backgroundColor = oldSymBg;
                        SetSymmetryEnabled(sym);
                    }
                }

                GUILayout.FlexibleSpace();

                var oldBg = GUI.backgroundColor;
                if (showingOriginal) GUI.backgroundColor = StudioGUI.Accent;
                bool orig = GUILayout.Toggle(showingOriginal, compare, button, GUILayout.Width(compareWidth));
                GUI.backgroundColor = oldBg;
                if (orig != showingOriginal)
                {
                    if (_sceneStrokeDraft != null) EndStroke();
                    _showOriginal = orig;
                    _dirty = true;
                }
                if (GUILayout.Button(view, dropdown, GUILayout.Width(viewWidth)))
                    BuildCanvasViewMenu(!inlinePaintViews).DropDown(GUILayoutUtility.GetLastRect());
                if (wideActions)
                {
                    if (GUILayout.Button(StudioGUI.Icon("ViewToolZoom", "", Loc.T("Frame the texture (F)")),
                            button, GUILayout.Width(frameWidth))) _zoom = 0;
                }
                if (showMode)
                {
                    if (GUILayout.Button(mode, button, GUILayout.Width(modeWidth))) SetAdvanced(!Advanced);
                    StudioGUI.Count("help.simpleMode", GUILayoutUtility.GetLastRect());
                }
                DrawSettingsButton(settingsLabel ? settings : settingsIcon, settingsLabel ? settingsLabelWidth : settingsIconWidth, button);
            }
            GUILayout.EndArea();
        }

        static float ToolbarItemWidth(GUIStyle style, GUIContent content) =>
            Mathf.Ceil(style.CalcSize(content).x) + style.margin.horizontal;

        GenericMenu BuildCanvasViewMenu(bool includePaintViews)
        {
            var menu = new GenericMenu();
            if (includePaintViews)
            {
                var spotlight = new GUIContent(Loc.T("Spotlight"));
                if (CanvasPainting) menu.AddItem(spotlight, _focusWhilePainting, () =>
                {
                    if (!CanvasPainting) return;
                    _focusWhilePainting = !_focusWhilePainting;
                    Repaint();
                });
                else menu.AddDisabledItem(spotlight, _focusWhilePainting);

                var mirror = new GUIContent(Loc.T("Mirror"));
                if (CanvasPainting && !GradientToolActive && !MirrorNeedsClassicAxis)
                    menu.AddItem(mirror, _symmetry, () =>
                    {
                        if (CanvasPainting && !GradientToolActive && !MirrorNeedsClassicAxis)
                            SetSymmetryEnabled(!_symmetry);
                    });
                else menu.AddDisabledItem(mirror, _symmetry);
                menu.AddSeparator("");
            }
            menu.AddItem(new GUIContent(Loc.T("UV wireframe on the texture")), _showWire, () =>
            {
                Undo.RecordObject(this, "Toggle texture UV wireframe");
                _showWire = !_showWire;
                Repaint();
            });
            menu.AddItem(new GUIContent(Loc.T("UV wireframe on the model")), SceneUvWireEnabled, () => SetSceneUvWire(!SceneUvWireEnabled));
            menu.AddItem(new GUIContent(Loc.T("Selected pieces (orange)")), _showSelection, () => { _showSelection = !_showSelection; Repaint(); });
            menu.AddItem(new GUIContent(Loc.T("Transparency checkerboard")), _alphaBlend, () => { _alphaBlend = !_alphaBlend; Repaint(); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Live preview on the avatar")), _previewOnAvatar, () => { _previewOnAvatar = !_previewOnAvatar; _dirty = true; Repaint(); });
            menu.AddItem(new GUIContent(Loc.T("Highlight selection in the Scene view")), _highlightInScene, () => { _highlightInScene = !_highlightInScene; SceneView.RepaintAll(); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Frame texture  (F)")), false, () => { _zoom = 0; Repaint(); });
            return menu;
        }

        void ToolButton(Tool tool, GUIContent content, float width, bool enabled)
        {
            Tab tab = tool == Tool.Paint ? Tab.Paint : Tab.Select;
            bool returnToPaint = tool == Tool.Paint && PickingForPaint;
            bool active = _tab == tab && !returnToPaint;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (!GUILayout.Toggle(active, content, EditorStyles.toolbarButton, GUILayout.Width(width)) || active) return;
                if (returnToPaint) SetPickForPaint(false);
                else SetTab(tab);
            }
        }

        Texture2D Checker
        {
            get
            {
                if (_checker != null) return _checker;
                _checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                Color a = new Color(0.30f, 0.30f, 0.30f), b = new Color(0.22f, 0.22f, 0.22f);
                _checker.SetPixels(new[] { a, b, b, a });
                _checker.Apply();
                return _checker;
            }
        }

        void DrawCanvas(Rect area)
        {
            var r = new Rect(area.x, area.y, area.width, area.height - StatusHeight);
            var status = new Rect(area.x, r.yMax, area.width, StatusHeight);
            EditorGUI.DrawRect(r, StudioGUI.CanvasBg);
            HandleCanvasDrop(r);

            if (_comp == null)
            {
                if (_canvasDrop != CanvasDrop.None)
                {
                    DrawCanvasDropHint(r);
                    DrawStatusBar(status, "", "");
                    return;
                }
                var box = new Rect(r.center.x - Mathf.Min(170, r.width / 2 - 12), r.center.y - 60, Mathf.Min(340, r.width - 24), 120);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(box, new Color(1, 1, 1, 0.03f));
                    StudioGUI.DrawBorder(box, new Color(1, 1, 1, 0.15f), 1);
                }
                GUI.Label(box, _renderer == null
                        ? Loc.T("Drop a part, mesh, material or saved edit here, or choose a part in 1 · Pick.")
                        : WaitingForPreview ? PreviewWaitText
                        : CanStartUnityTexture ? Loc.T("Choose Create a texture and paint in 1 · Pick.")
                        : _statusFix == StatusFix.PickAnotherMesh ? SlotStatusText()
                        : (string.IsNullOrEmpty(_status) ? Loc.T("This part has no editable texture.") : _status),
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true, fontSize = 12 });
                if (!WaitingForPreview && _renderer != null && _statusFix == StatusFix.PickAnotherMesh)
                {
                    StudioGUI.Count("canvas.pickAnotherMesh");
                    if (GUI.Button(new Rect(box.center.x - 70, box.yMax + 6, 140, 22), Msg.BtnPickMesh)) DrawPartListMenu();
                }
                else if (!WaitingForPreview && _renderer != null && _statusFix == StatusFix.RetryPreview)
                {
                    float width = Mathf.Min(140f, box.width);
                    if (GUI.Button(new Rect(box.center.x - width * 0.5f, box.yMax + 6, width, 22), Loc.T("Retry preview"))) RetryTexturePreview();
                }
                DrawToast(r);
                DrawStatusBar(status, "", "");
                return;
            }

            if (r.size != _lastCanvasSize)
            {
                if (_viewFit || _lastCanvasSize == Vector2.zero) _zoom = 0;
                else _pan += (r.size - _lastCanvasSize) * 0.5f;
                _lastCanvasSize = r.size;
            }
            if (_zoom <= 0) Frame(r);
            HandleCanvasInput(r);

            GUI.BeginClip(r);
            var texRect = new Rect(_pan.x, _pan.y, _comp.Width * _zoom, _comp.Height * _zoom);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTextureWithTexCoords(texRect, Checker, new Rect(0, 0, texRect.width / 16f, texRect.height / 16f));
                GUI.DrawTexture(texRect, DisplayingOriginal ? _comp.SourcePreview : _comp.Preview, ScaleMode.StretchToFill, _alphaBlend);
                if (CanvasPainting && MaskEditing && _showMaskOverlay && !DisplayingOriginal)
                {
                    var overlay = _comp.MaskOverlay(ActiveLayer, _uv);
                    if (overlay != null) GUI.DrawTexture(texRect, overlay, ScaleMode.StretchToFill, true);
                }
                var focusFaces = FocusFaces();
                var focus = focusFaces ?? FocusIslands();
                if (focus != null)
                {
                    GUI.DrawTexture(texRect, FocusOverlay(focus, faces: focusFaces != null), ScaleMode.StretchToFill, true);
                    if (_showWire && _focusSegments.Length > 0)
                    {
                        var old = Handles.matrix;
                        Handles.matrix = Matrix4x4.TRS(new Vector3(texRect.x, texRect.yMax, 0), Quaternion.identity, new Vector3(texRect.width, -texRect.height, 1));
                        Handles.color = new Color(1f, 1f, 1f, 0.12f);
                        Handles.DrawLines(_focusSegments);
                        Handles.matrix = old;
                    }
                }
                else if (_showSelection && _uv != null && HighlightElements.Count > 0)
                {
                    PrepareCanvasSelection(_showWire);
                    GUI.DrawTexture(texRect, _selOverlay, ScaleMode.StretchToFill, true);
                }
                if (focus == null && CanvasPainting && _uv != null && _uv.islandCount > 0)
                    GUI.DrawTexture(texRect, FocusOverlay(null, 170), ScaleMode.StretchToFill, true);
                if (focus == null && _showWire && _uv != null)
                {
                    var old = Handles.matrix;
                    Handles.matrix = Matrix4x4.TRS(new Vector3(texRect.x, texRect.yMax, 0), Quaternion.identity, new Vector3(texRect.width, -texRect.height, 1));
                    Handles.color = new Color(1f, 1f, 1f, 0.22f);
                    Handles.DrawLines(_uv.lineSegments);
                    if (_showSelection && _selSegments.Length > 0)
                    {
                        Handles.color = new Color(1f, 0.6f, 0.1f, 0.9f);
                        Handles.DrawLines(_selSegments);
                    }
                    Handles.matrix = old;
                }
                if (CanvasPainting && GradientToolActive) DrawGradientOverlay();
                else if (CanvasPainting) DrawPaintUvLimitGuide(texRect);
                if (CanvasImagePlacement) DrawImageHandlesOverlay();
                if (CanvasPainting && !_panning) DrawCanvasBrushCursor(new Rect(0, 0, r.width, r.height));
            }
            GUI.EndClip();

            if (_panning) EditorGUIUtility.AddCursorRect(r, MouseCursor.Pan);
            DrawBeforeBanner(r);
            DrawMarqueeCanvas(r);
            DrawTileGrid(r);
            DrawToast(r);
            DrawCanvasDropHint(r);

            var focusedFaces = FocusFaces();
            var focused = focusedFaces ?? FocusIslands();
            string dimensions = $"{_comp.Width}×{_comp.Height}  ·  {_zoom * 100f:0}%";
            string context = PartPicking ? SceneSelectionSummary(HighlightElements.Count, HighlightUsesFaces) :
                CanvasSelecting ? CanvasSelectionStatus() : focused != null
                ? Loc.F(focusedFaces != null ? "focus: {0} face{1}" : "focus: {0} piece{1}", focused.Count, focused.Count == 1 ? "" : "s")
                : null;
            string left = dimensions + (_comp.SourceWidth != _comp.Width ? " " + Loc.F("(saves at {0})", _comp.SourceWidth) : "") +
                          (context != null ? "  ·  " + context : "");
            string navigation = Loc.T("Wheel zoom  ·  Right-drag pan");
            string right = _matchPick != MatchPick.None ? Loc.T("Colour Match: click the colour here or in the Scene view  ·  Esc cancels")
                : CanvasPainting
                ? (_eyedropperArmed ? Loc.T("Eyedropper: click to pick a colour  ·  I cancel")
                   : MaskEditing ? Loc.F("Mask of “{0}”: B reveal  ·  E hide  ·  G fill  ·  red = hidden", ActiveLayer.name)
                   : BlendToolActive ? (_brush.blendKind == StrokeTool.Smudge ? Loc.T("Drag to smudge colour along the stroke  ·  [ ] size  ·  B back to the brush") : Loc.T("Drag to blur  ·  [ ] size  ·  B back to the brush"))
                   : GradientToolActive ? (TextureGradientForHandles != null
                       ? Loc.T("Drag to draw a gradient  ·  Shift snaps 45°  ·  drag the dots to adjust  ·  Ctrl+click pick colour")
                       : Loc.T("Drag to draw a gradient  ·  Shift snaps 45°  ·  Ctrl+click pick colour"))
                   : _brush.fillTool ? Loc.T("Click to fill  ·  I or Ctrl+click picks a colour")
                                    : Loc.T("Drag paint  ·  I or Ctrl+click pick colour  ·  [ ] size  ·  E erase"))
                : PartPicking ? Loc.T("Click a piece to choose where the colour goes.")
                : CanvasSelecting ? CanvasSelectHint() : navigation;
            string selectNavigation = CanvasSelecting && _matchPick == MatchPick.None ? navigation : null;
            if (CanvasImagePlacement && SelectedImage != null && _matchPick == MatchPick.None && !_eyedropperArmed)
            {
                right = ImageHandlesHint();
                selectNavigation = null;
            }
            if (PartPicking)
            {
                right = StudioGUI.Label("colour.onePiece", "Pieces") + Loc.T(": click a piece to add or remove it  ·  Shift add  ·  Ctrl remove  ·  Right-drag pan");
                selectNavigation = null;
            }
            DrawStatusBar(status, left, right, context ?? dimensions, selectNavigation);
        }


        string _toastText, _toastAction, _toastAction2, _lastToastAction, _lastToastAction2;
        System.Action _toastCallback, _toastCallback2;
        MessageType _toastType;
        double _toastUntil;
        Rect _toastRect;
        bool ToastHit(Vector2 p) => !string.IsNullOrEmpty(_toastText) && EditorApplication.timeSinceStartup <= _toastUntil && _toastRect.Contains(p);

        void Toast(string text, MessageType type = MessageType.Info, float seconds = 5f, string actionLabel = null, System.Action action = null)
            => Toast(text, type, seconds, actionLabel, action, null, null);

        void Toast(string text, MessageType type, float seconds, string actionLabel, System.Action action, string actionLabel2, System.Action action2)
        {
            ClearSceneTopologyFeedback();
            _toastText = text;
            _toastType = type;
            _toastAction = actionLabel;
            _toastCallback = action;
            _toastAction2 = actionLabel2;
            _toastCallback2 = action2;
            _lastToastAction = actionLabel;
            _lastToastAction2 = actionLabel2;
            _toastUntil = EditorApplication.timeSinceStartup + seconds;
            EditorApplication.update -= ToastTick;
            EditorApplication.update += ToastTick;
            Repaint();
        }

        void ToastTick()
        {
            if (EditorApplication.timeSinceStartup > _toastUntil)
            {
                EditorApplication.update -= ToastTick;
                _toastText = null;
                _toastCallback = _toastCallback2 = null;
                ClearSceneTopologyFeedback();
                Repaint();
            }
        }

        void DrawToast(Rect canvas)
        {
            if (string.IsNullOrEmpty(_toastText) || EditorApplication.timeSinceStartup > _toastUntil) return;
            float w = Mathf.Min(canvas.width - 24f, 460f);
            var style = StudioGUI.ToastText;
            float textW = w - 40f - (_toastAction != null ? 104f : 0f) - (_toastAction2 != null ? 104f : 0f) - 22f;
            float h = Mathf.Max(34f, style.CalcHeight(new GUIContent(_toastText), textW) + 14f);
            var box = new Rect(canvas.x + (canvas.width - w) / 2f, canvas.y + 10f, w, h);
            _toastRect = box;
            var accent = _toastType == MessageType.Error ? StudioGUI.Danger : _toastType == MessageType.Warning ? StudioGUI.Accent : StudioGUI.InfoBlue;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(box, new Color(0.09f, 0.09f, 0.09f, 0.96f));
                EditorGUI.DrawRect(new Rect(box.x, box.y, 4, box.height), accent);
                if (_toastType != MessageType.Info) StudioGUI.DrawBorder(canvas, new Color(accent.r, accent.g, accent.b, 0.6f), 2);
            }
            var icon = _toastType == MessageType.Info ? "console.infoicon.sml" : _toastType == MessageType.Warning ? "console.warnicon.sml" : "console.erroricon.sml";
            GUI.Label(new Rect(box.x + 10, box.y + (box.height - 16) / 2, 16, 16), StudioGUI.Icon(icon, ""));
            GUI.Label(new Rect(box.x + 32, box.y + 7, textW, box.height - 14), _toastText, style);
            float x = box.xMax - 26;
            if (GUI.Button(new Rect(x, box.y + (box.height - 18) / 2, 18, 18), new GUIContent("✕", Loc.T("Dismiss")), EditorStyles.label)) { _toastUntil = 0; ToastTick(); }
            if (_toastAction != null)
            {
                var cb = _toastCallback;
                if (GUI.Button(new Rect(x - 104, box.y + (box.height - 20) / 2, 98, 20), _toastAction))
                {
                    _toastUntil = 0;
                    ToastTick();
                    cb?.Invoke();
                }
            }
            if (_toastAction2 != null)
            {
                var cb2 = _toastCallback2;
                float x2 = x - (_toastAction != null ? 104f : 0f);
                if (GUI.Button(new Rect(x2 - 104, box.y + (box.height - 20) / 2, 98, 20), _toastAction2))
                {
                    _toastUntil = 0;
                    ToastTick();
                    cb2?.Invoke();
                }
            }
        }

        System.Collections.Generic.List<int> FocusIslands()
        {
            if (!CanvasPainting || !_focusWhilePainting || _uv == null || _comp == null) return null;
            if (_selection.Count > 0) return _selection;
            if (ActiveLayer is PaintLayer pl && pl.maskMode == MaskMode.SelectedIslands && !pl.invertMask && pl.islands.Count > 0)
                return pl.islands;
            return null;
        }

        Texture2D FocusOverlay(System.Collections.Generic.List<int> islands, byte dimAlpha = 236, bool faces = false)
        {
            string key;
            if (islands == null) key = $"{_comp.Width}x{_comp.Height}:{dimAlpha}:all{_uv.islandCount}";
            else
            {
                uint sum = 0, mix = 0;
                for (int i = 0; i < islands.Count; i++) { uint h = (uint)islands[i] * 2654435761u; sum += h; mix ^= (h << 13) | (h >> 19); }
                key = $"{_comp.Width}x{_comp.Height}:{dimAlpha}:{faces}:{islands.Count}:{sum}:{mix}";
            }
            if (_focusOverlay != null && key == _focusKey) return _focusOverlay;
            _focusKey = key;
            int ow = Mathf.Min(1024, _comp.Width);
            int oh = Mathf.Max(1, Mathf.RoundToInt(_comp.Height * (ow / (float)_comp.Width)));
            var set = islands == null ? null : new HashSet<int>(islands);
            var bg = StudioGUI.CanvasBg;
            var dim = new Color32((byte)(bg.r * 255), (byte)(bg.g * 255), (byte)(bg.b * 255), dimAlpha);
            var px = new Color32[ow * oh];
            System.Array.Fill(px, dim);
            System.Func<int, bool> selected = set == null ? null : faces ? set.Contains : t => set.Contains(_uv.triIsland[t]);
            _uv.Rasterize(ow, oh, selected, (pixel, t, a, b, c) => px[pixel] = default);

            if (_focusOverlay == null || _focusOverlay.width != ow || _focusOverlay.height != oh)
            {
                if (_focusOverlay != null) DestroyImmediate(_focusOverlay);
                _focusOverlay = new Texture2D(ow, oh, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            _focusOverlay.SetPixels32(px);
            _focusOverlay.Apply(false);
            _focusSegments = selected == null ? System.Array.Empty<Vector3>() : _uv.SegmentsForTriangles(selected);
            return _focusOverlay;
        }

        void ShowEditsIfBeforeOn()
        {
            if (!_showOriginal) return;
            _showOriginal = false;
            _dirty = true;
            Toast(Loc.T("“Compare: Original” was on, so you were seeing the original texture. Showing your edits again."), MessageType.Info, 4f);
        }

        static Rect BeforeBannerRect(Rect canvas) =>
            new Rect(canvas.x + 10, canvas.yMax - 40, Mathf.Min(canvas.width - 20, 330), 30);

        void DrawBeforeBanner(Rect canvas)
        {
            if (!DisplayingOriginal) return;
            var r = BeforeBannerRect(canvas);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0.09f, 0.09f, 0.09f, 0.95f));
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), StudioGUI.Accent);
                StudioGUI.DrawBorder(canvas, new Color(StudioGUI.Accent.r, StudioGUI.Accent.g, StudioGUI.Accent.b, 0.5f), 2);
            }
            var showEdits = new GUIContent(Loc.T("Show my edits"));
            float buttonWidth = GUI.skin.button.CalcSize(showEdits).x + 8f;
            var button = new Rect(r.xMax - buttonWidth - 8f, r.y + 5f, buttonWidth, 20f);
            var label = new Rect(r.x + 12f, r.y + 5f, button.x - r.x - 18f, 20f);
            GUI.Label(label, new GUIContent(Loc.T("Original"), Loc.T("Showing the ORIGINAL (Compare: Original is on)")),
                new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = StudioGUI.Accent } });
            if (GUI.Button(button, showEdits))
            {
                _showOriginal = false;
                _dirty = true;
                Repaint();
            }
        }

        static GUIStyle _statusHint;

        void DrawStatusBar(Rect r, string left, string right, string compactStatus = null, string navigation = null)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.StatusBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), StudioGUI.HeaderLine);
            }
            if (DrawEditorActivity(r) || (left.Length == 0 && right.Length == 0)) return;

            var (readout, hint, split) = CanvasStatusLayout(r.width, left, right, compactStatus, navigation);
            GUI.Label(new Rect(r.x, r.y, split, r.height), readout, StudioGUI.Status);
            _statusHint ??= new GUIStyle(StudioGUI.Status) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(r.x + split, r.y, r.width - split, r.height), hint, _statusHint);
        }

        static (GUIContent readout, GUIContent hint, float split) CanvasStatusLayout(float width, string left, string right,
            string compactStatus, string navigation)
        {
            var style = StudioGUI.Status;
            var readout = new GUIContent(left, left);
            string fullHint = navigation == null ? right : right + "  ·  " + navigation;
            var hint = new GUIContent(fullHint, fullHint);
            float split = style.CalcSize(readout).x;
            float hintWidth = style.CalcSize(hint).x;
            if (split + hintWidth <= width) return (readout, hint, split);

            if (navigation != null)
            {
                hint.text = right;
                hintWidth = style.CalcSize(hint).x;
            }
            if (compactStatus != null && split + hintWidth > width)
            {
                readout.text = compactStatus;
                split = style.CalcSize(readout).x;
            }
            if (split + hintWidth > width)
            {
                split = Mathf.Min(split, width * 0.45f);
                readout.text = StudioGUI.Ellipsize(readout.text, style, split);
                string fitted = StudioGUI.Ellipsize(hint.text, style, width - split);
                if (fitted != hint.text)
                {
                    int boundary = Mathf.Max(fitted.LastIndexOf("  ·  ", System.StringComparison.Ordinal), fitted.LastIndexOf('。') + 1);
                    if (boundary > 0) fitted = fitted.Substring(0, boundary) + "…";
                }
                hint.text = fitted;
            }
            return (readout, hint, split);
        }

        void DrawCanvasBrushCursor(Rect clip)
        {
            var mp = Event.current.mousePosition;
            if (!clip.Contains(mp)) return;
            if (_eyedropperArmed || Event.current.control || Event.current.command) { DrawPickerCircle(mp); return; }
            bool gradient = GradientToolActive;
            float rPx = _brush.fillTool || gradient ? 6f : _brush.size * 0.5f / Mathf.Max(1, _comp.SourceWidth) * _comp.Width * _zoom;
            var c = new Vector3(mp.x, mp.y, 0);
            if (gradient && GradientHandleAt(mp) != GradHandle.None) EditorGUIUtility.AddCursorRect(new Rect(mp.x - 10, mp.y - 10, 20, 20), MouseCursor.MoveArrow);

            var uv = CanvasToUV(mp);
            string fillProblem = _brush.fillTool ? CanvasFillCursorProblem(uv) : null;
            bool offModel = !_brush.fillTool && !gradient && _uv != null && !_uv.HasIslandAtUV(uv);
            bool blocked = offModel || fillProblem != null;
            bool maskHides = MaskEditing && (_brush.fillTool ? _maskFillHides : _brush.mode == BrushMode.Erase);
            var ring = blocked ? StudioGUI.Danger : _brush.mode == BrushMode.Erase || maskHides ? new Color(1f, 0.55f, 0.55f)
                     : BlendToolActive ? new Color(0.55f, 0.85f, 1f) : Color.white;

            Handles.color = new Color(0, 0, 0, 0.7f);
            Handles.DrawWireDisc(c, Vector3.forward, rPx + 1f);
            Handles.color = ring;
            Handles.DrawWireDisc(c, Vector3.forward, rPx);
            if (blocked)
            {
                var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = StudioGUI.Danger } };
                string text = fillProblem ?? (_brush.stayInsideIslands ? Loc.T("not part of the model: nothing will paint here")
                    : Loc.T("not part of the model: won't show on the avatar"));
                var size = style.CalcSize(new GUIContent(text));
                float x = mp.x + rPx + 6f;
                if (fillProblem != null && x + size.x + 6f > clip.xMax)
                    x = Mathf.Max(clip.xMin, mp.x - rPx - size.x - 12f);
                var lr = new Rect(x, mp.y - 8, size.x + 6, 16);
                EditorGUI.DrawRect(lr, new Color(0.08f, 0.08f, 0.08f, 0.9f));
                GUI.Label(new Rect(lr.x + 3, lr.y, size.x, 16), text, style);
            }
            if (!gradient && _brush.shape.hardness < 0.95f && rPx > 6)
            {
                Handles.color = new Color(1, 1, 1, 0.35f);
                Handles.DrawWireDisc(c, Vector3.forward, rPx * Mathf.Max(0.05f, _brush.shape.hardness));
            }
            Handles.color = Color.white;
            Handles.DrawLine(c + Vector3.left * 3, c + Vector3.right * 3);
            Handles.DrawLine(c + Vector3.up * 3, c + Vector3.down * 3);
            if (!gradient && !blocked) DrawCanvasMirrorCursor(mp, rPx);
        }

        void Frame(Rect r)
        {
            if (_comp == null) return;
            _zoom = Mathf.Min((r.width - 24) / _comp.Width, (r.height - 24) / _comp.Height);
            _zoom = Mathf.Max(_zoom, 0.01f);
            _viewFit = true;
            _pan = new Vector2((r.width - _comp.Width * _zoom) * 0.5f, (r.height - _comp.Height * _zoom) * 0.5f);
        }

        Vector2 CanvasToUV(Vector2 local) =>
            new Vector2((local.x - _pan.x) / (_comp.Width * _zoom), 1f - (local.y - _pan.y) / (_comp.Height * _zoom));

        void HandleCanvasInput(Rect r)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 local = e.mousePosition - r.position;
            bool inside = r.Contains(e.mousePosition);

            if ((_gradDrag != null || _imgDrag != null) && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelGradientDrag();
                CancelImageDrag();
                ReleaseGradientDragControl();
                ReleaseImageDragControl();
                e.Use();
            }

            switch (e.GetTypeForControl(id))
            {
                case EventType.ScrollWheel when inside:
                {
                    float old = _zoom;
                    _viewFit = false;
                    _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0 ? 1f / 1.15f : 1.15f), 0.01f, 32f);
                    _pan = local - (local - _pan) * (_zoom / old);
                    e.Use();
                    Repaint();
                    break;
                }
                case EventType.MouseDown when inside && !ToastHit(e.mousePosition) &&
                    !(DisplayingOriginal && BeforeBannerRect(r).Contains(e.mousePosition)):
                    if (_marqueeActive || _gradDrag != null || _imgDrag != null) { e.Use(); break; }
                    if (e.button == 2 || e.button == 1 || (e.button == 0 && e.alt))
                    {
                        _panning = true;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    else if (e.button == 0)
                    {
                        var uv = CanvasToUV(local);
                        if (HandleCanvasMatchPick(uv)) { e.Use(); break; }
                        if (PartPicking) { PartPick(_uv.IslandsAtUV(uv), e.shift, e.control || e.command, null); e.Use(); break; }
                        bool pickColour = CanvasPainting && (e.control || e.command || _eyedropperArmed);
                        if (CanvasImagePlacement && !pickColour && BeginImageDrag(local, e))
                        {
                            GUIUtility.hotControl = _imgDragControl = id;
                            e.Use();
                        }
                        else if (CanvasPainting)
                        {
                            if (pickColour) PickColour(uv);
                            else if (GradientToolActive)
                            {
                                if (BeginGradientDrag(local)) GUIUtility.hotControl = _gradDragControl = id;
                            }
                            else if (CanPaint(out var why))
                            {
                                var pointer = BrushPointer(e, start: true);
                                BeginStroke(CanvasToUV(pointer.position - r.position), pointer.pressure, -1);
                                if (_liveStroke != null) GUIUtility.hotControl = id;
                            }
                            else Toast(why, MessageType.Warning);
                            e.Use();
                        }
                        else if (CanvasSelecting && _uv != null)
                        {
                            if (TileGridClick(r, e)) { e.Use(); break; }
                            if (CanvasSelectDown(uv, e)) GUIUtility.hotControl = id;
                            e.Use();
                        }
                    }
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    if (_panning) { _pan += e.delta; _viewFit = false; }
                    else if (_gradDrag != null) UpdateGradientDrag(local, e.shift);
                    else if (_imgDrag != null) UpdateImageDrag(local, e.shift);
                    else if (_marqueeActive && !_marqueeInScene) UpdateMarquee(CanvasToUV(local), e.mousePosition);
                    else if (_liveStroke != null)
                    {
                        var pointer = BrushPointer(e, start: false);
                        AddStrokePoint(CanvasToUV(pointer.position - r.position), pointer.pressure, true, -1);
                    }
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    if ((_marqueeActive || _gradDrag != null || _imgDrag != null) && e.button != 0) { e.Use(); break; }
                    if (_panning) _panning = false;
                    else if (_gradDrag != null)
                    {
                        UpdateGradientDrag(local, e.shift);
                        EndGradientDrag();
                        _gradDragControl = 0;
                    }
                    else if (_imgDrag != null)
                    {
                        UpdateImageDrag(local, e.shift);
                        EndImageDrag();
                        _imgDragControl = 0;
                    }
                    else if (_marqueeActive && !_marqueeInScene) EndMarqueeCanvas();
                    else EndStroke();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.MouseMove when CanvasPainting:
                case EventType.MouseLeaveWindow:
                    Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && _marqueeActive && !_marqueeInScene:
                    CancelMarquee();
                    e.Use();
                    Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && _matchPick != MatchPick.None:
                    CancelMatchPick();
                    e.Use();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.F && inside:
                    Frame(r);
                    e.Use();
                    Repaint();
                    break;
            }
        }
    }
}
