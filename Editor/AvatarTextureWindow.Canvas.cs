using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    // Toolbar, texture canvas, status bar and canvas input.
    public partial class AvatarTextureWindow
    {
        const float StatusHeight = 20f;

        Vector2 _pan, _panelScroll, _lastCanvasSize;
        float _zoom;
        bool _viewFit = true; // true while the view is at "frame texture"; cleared by zoom / pan
        bool _panning;
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
            GUILayout.BeginArea(r);
            StudioGUI.Count("tab.toolbar");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // Tools (switch the canvas mode and the panel tab).
                bool canPaint = CurrentSlot == null || CurrentSlot.role != TextureRole.Normal;
                ToolButton(Tool.Select, StudioGUI.Icon("Grid.BoxTool", Loc.T(" Select"), Loc.T("Select pieces: the separate pieces of the texture sheet (S)")), _comp != null);
                ToolButton(Tool.Paint, StudioGUI.Icon("Grid.PaintTool", Loc.T(" Paint"), canPaint
                    ? Loc.T("Paint with a brush (B). E = eraser, I = eyedropper.")
                    : Loc.T("Normal maps can't be painted.")), _comp != null && canPaint);
                // Always present (so the buttons to its right never move); only active while painting.
                using (new EditorGUI.DisabledScope(_tool != Tool.Paint))
                    _focusWhilePainting = GUILayout.Toggle(_focusWhilePainting, new GUIContent(Loc.T("Spotlight"),
                        Loc.T("While painting, dim everything outside the selected pieces (or the Paint layer's piece mask) " +
                        "and hide the orange selection, so you can see what you're painting.")), EditorStyles.toolbarButton);
                using (new EditorGUI.DisabledScope(_tool != Tool.Paint))
                {
                    var oldSymBg = GUI.backgroundColor;
                    if (_symmetry) GUI.backgroundColor = StudioGUI.Accent;
                    bool sym = GUILayout.Toggle(_symmetry, new GUIContent(Loc.T("Mirror"), Loc.T("Symmetry: also paint the mirror image on the other side of the model (see Symmetry in 4 · Paint)")), EditorStyles.toolbarButton);
                    GUI.backgroundColor = oldSymBg;
                    if (sym != _symmetry)
                    {
                        _symmetry = sym;
                        if (sym) EnsureSymmetry();
                        SceneView.RepaintAll();
                    }
                }

                GUILayout.FlexibleSpace();

                // View: compare is used often, so it gets its own button; the rest live in one menu.
                var oldBg = GUI.backgroundColor;
                if (_showOriginal) GUI.backgroundColor = StudioGUI.Accent; // impossible to miss while it hides your edits
                bool orig = GUILayout.Toggle(_showOriginal, new GUIContent(_showOriginal ? Loc.T("Compare: Original (on)") : Loc.T("Compare: Original"), Loc.T("Compare: shows the untouched original texture, here and on the avatar. Your edits are hidden while it's on.")), EditorStyles.toolbarButton);
                GUI.backgroundColor = oldBg;
                if (orig != _showOriginal) { _showOriginal = orig; _dirty = true; }
                if (GUILayout.Button(new GUIContent(Loc.T("View"), Loc.T("What the texture view and avatar show")), EditorStyles.toolbarDropDown))
                {
                    var m = new GenericMenu();
                    m.AddItem(new GUIContent(Loc.T("UV wireframe")), _showWire, () => { _showWire = !_showWire; Repaint(); });
                    m.AddItem(new GUIContent(Loc.T("Selected pieces (orange)")), _showSelection, () => { _showSelection = !_showSelection; Repaint(); });
                    m.AddItem(new GUIContent(Loc.T("Transparency checkerboard")), _alphaBlend, () => { _alphaBlend = !_alphaBlend; Repaint(); });
                    m.AddSeparator("");
                    m.AddItem(new GUIContent(Loc.T("Live preview on the avatar")), _previewOnAvatar, () => { _previewOnAvatar = !_previewOnAvatar; _dirty = true; Repaint(); });
                    m.AddItem(new GUIContent(Loc.T("Highlight selection in the Scene view")), _highlightInScene, () => { _highlightInScene = !_highlightInScene; SceneView.RepaintAll(); });
                    m.AddSeparator("");
                    m.AddItem(new GUIContent(Loc.T("Frame texture  (F)")), false, () => { _zoom = 0; Repaint(); });
                    m.DropDown(GUILayoutUtility.GetLastRect());
                }
                if (GUILayout.Button(StudioGUI.Icon("ViewToolZoom", "", Loc.T("Frame the texture (F)")), EditorStyles.toolbarButton, GUILayout.Width(28))) _zoom = 0;
                if (GUILayout.Button(new GUIContent(Advanced ? Loc.T("Advanced") : Loc.T("Simple"), ModeSwitchTip), EditorStyles.toolbarButton, GUILayout.Width(64))) SetAdvanced(!Advanced);
            }
            GUILayout.EndArea();
        }

        void ToolButton(Tool tool, GUIContent content, bool enabled)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                bool on = GUILayout.Toggle(_tool == tool, content, EditorStyles.toolbarButton);
                if (on && _tool != tool) SetTab(tool == Tool.Paint ? Tab.Paint : Tab.Select);
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
            bool dragging = HandleMeshDrop(r);

            if (_comp == null)
            {
                // Empty state: one clear instruction and a big drop target.
                var box = new Rect(r.center.x - Mathf.Min(170, r.width / 2 - 12), r.center.y - 60, Mathf.Min(340, r.width - 24), 120);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(box, dragging ? new Color(1f, 0.6f, 0.15f, 0.18f) : new Color(1, 1, 1, 0.03f));
                    StudioGUI.DrawBorder(box, dragging ? StudioGUI.Accent : new Color(1, 1, 1, 0.15f), 1);
                }
                GUI.Label(box, _renderer == null
                        ? Loc.T("Drag a part here from the Hierarchy\n\nor select it and press Use Selection\nin 1 · Pick")
                        : _statusFix == StatusFix.PickAnotherMesh ? SlotStatusText()
                        : (string.IsNullOrEmpty(_status) ? Loc.T("This part has no editable texture.") : _status),
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true, fontSize = 12 });
                if (_renderer != null && _statusFix == StatusFix.PickAnotherMesh)
                {
                    StudioGUI.Count("canvas.pickAnotherMesh");
                    if (GUI.Button(new Rect(box.center.x - 70, box.yMax + 6, 140, 22), Msg.BtnPickMesh)) DrawPartListMenu();
                }
                DrawToast(r);
                DrawStatusBar(status, "", "");
                return;
            }

            if (r.size != _lastCanvasSize)
            {
                // re-frame on resize only if the view was framed; otherwise keep the centre stable
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
                GUI.DrawTexture(texRect, _showOriginal ? _comp.SourcePreview : _comp.Preview, ScaleMode.StretchToFill, _alphaBlend);
                if (MaskEditing && _showMaskOverlay && !_showOriginal)
                {
                    var overlay = _comp.MaskOverlay(ActiveLayer, _uv); // hidden parts of the layer in red
                    if (overlay != null) GUI.DrawTexture(texRect, overlay, ScaleMode.StretchToFill, true);
                }
                var focus = FocusIslands();
                if (focus != null)
                {
                    // Focus while painting: everything outside the islands almost disappears, no orange overlay,
                    // and only the focused islands keep a faint wireframe as a guide.
                    GUI.DrawTexture(texRect, FocusOverlay(focus), ScaleMode.StretchToFill, true);
                    if (_showWire && _focusSegments.Length > 0)
                    {
                        var old = Handles.matrix;
                        Handles.matrix = Matrix4x4.TRS(new Vector3(texRect.x, texRect.yMax, 0), Quaternion.identity, new Vector3(texRect.width, -texRect.height, 1));
                        Handles.color = new Color(1f, 1f, 1f, 0.12f);
                        Handles.DrawLines(_focusSegments);
                        Handles.matrix = old;
                    }
                }
                else if (_showSelection && _selOverlay != null && _selection.Count > 0) GUI.DrawTexture(texRect, _selOverlay, ScaleMode.StretchToFill, true);
                // While painting, show where the model actually is: texture areas no mesh uses are dimmed (painting there shows nowhere).
                if (focus == null && _tool == Tool.Paint && _uv != null && _uv.islandCount > 0)
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
                if (GradientToolActive) DrawGradientOverlay();
                DrawImageHandlesOverlay();
                if (_tool == Tool.Paint && !_panning) DrawCanvasBrushCursor(new Rect(0, 0, r.width, r.height));
            }
            GUI.EndClip();

            if (_panning) EditorGUIUtility.AddCursorRect(r, MouseCursor.Pan);
            if (dragging && Event.current.type == EventType.Repaint) StudioGUI.DrawBorder(r, StudioGUI.Accent, 2);
            DrawBeforeBanner(r);
            DrawMarqueeCanvas(r);
            DrawTileGrid(r);
            DrawToast(r);

            var focused = FocusIslands();
            string left = $"{_comp.Width}×{_comp.Height}" + (_comp.SourceWidth != _comp.Width ? " " + Loc.F("(saves at {0})", _comp.SourceWidth) : "") + $"  ·  {_zoom * 100f:0}%" +
                          (focused != null ? "  ·  " + Loc.F("focus: {0} piece{1}", focused.Count, focused.Count == 1 ? "" : "s") : "") +
                          (_tool == Tool.Select ? "  ·  " + Loc.F("{0}: {1} selected", SelectModeNames[(int)_selectMode], _selection.Count) : "");
            string right = _tool == Tool.Paint
                ? (_matchPick != MatchPick.None ? Loc.T("Colour Match: click the colour here or in the Scene view  ·  Esc cancels")
                   : _eyedropperArmed ? Loc.T("Eyedropper: click to pick a colour  ·  I cancel")
                   : MaskEditing ? Loc.F("Mask of “{0}”: B reveal  ·  E hide  ·  G fill  ·  red = hidden", ActiveLayer.name)
                   : BlendToolActive ? (_brush.blendKind == StrokeTool.Smudge ? Loc.T("Drag to smudge colour along the stroke  ·  [ ] size  ·  B back to the brush") : Loc.T("Drag to blur  ·  [ ] size  ·  B back to the brush"))
                   : GradientToolActive ? Loc.T("Drag to draw a gradient  ·  Shift snaps 45°  ·  drag the dots to adjust  ·  Ctrl+click pick colour")
                                    : Loc.T("Drag paint  ·  I or Ctrl+click pick colour  ·  [ ] size  ·  E erase"))
                : SelectModeHint().Replace(". ", "  ·  ").TrimEnd('.') + "  ·  " + Loc.T("Wheel zoom  ·  Right-drag pan");
            if (SelectedImage != null && _matchPick == MatchPick.None && !_eyedropperArmed) right = ImageHandlesHint();
            if (PartPicking) right = StudioGUI.Label("colour.onePiece", "One piece at a time") + Loc.T(": click a piece to add or remove it  ·  Shift add  ·  Ctrl remove  ·  Right-drag pan");
            DrawStatusBar(status, left, right);
        }

        /// <summary>Drag a GameObject / renderer from the Hierarchy onto the canvas to edit it. Returns true while dragging over.</summary>
        bool HandleMeshDrop(Rect r)
        {
            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !r.Contains(e.mousePosition)) return false;
            var target = DragAndDrop.objectReferences.Select(ResolveRenderer).FirstOrDefault(x => x != null);
            if (target == null) { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return false; }
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetRendererChecked(target);
                _tab = Tab.Target;
            }
            e.Use();
            return true;
        }

        // Toasts show feedback right where the user is looking

        string _toastText, _toastAction, _toastAction2, _lastToastAction, _lastToastAction2;
        System.Action _toastCallback, _toastCallback2;
        MessageType _toastType;
        double _toastUntil;
        Rect _toastRect;
        bool ToastHit(Vector2 p) => !string.IsNullOrEmpty(_toastText) && EditorApplication.timeSinceStartup <= _toastUntil && _toastRect.Contains(p);

        /// <summary>Short message over the canvas, optionally with a one-click fix button.</summary>
        void Toast(string text, MessageType type = MessageType.Info, float seconds = 5f, string actionLabel = null, System.Action action = null)
            => Toast(text, type, seconds, actionLabel, action, null, null);

        void Toast(string text, MessageType type, float seconds, string actionLabel, System.Action action, string actionLabel2, System.Action action2)
        {
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

        /// <summary>
        /// Islands to focus on while painting: the island selection, or else the active Paint layer's island mask.
        /// Null when focus doesn't apply (Select tool, Focus off, or nothing to focus on).
        /// </summary>
        System.Collections.Generic.List<int> FocusIslands()
        {
            if (_tool != Tool.Paint || !_focusWhilePainting || _uv == null || _comp == null) return null;
            if (_selection.Count > 0) return _selection;
            if (ActiveLayer is PaintLayer pl && pl.maskMode == MaskMode.SelectedIslands && pl.islands.Count > 0) return pl.islands;
            return null;
        }

        /// <summary>Canvas-coloured, nearly opaque everywhere except the given islands (cached per island set).</summary>
        Texture2D FocusOverlay(System.Collections.Generic.List<int> islands, byte dimAlpha = 236)
        {
            // null = every island (no list built per repaint). Otherwise an order-independent hash avoids sorting and joining each repaint.
            string key;
            if (islands == null) key = $"{_comp.Width}x{_comp.Height}:{dimAlpha}:all{_uv.islandCount}";
            else
            {
                uint sum = 0, mix = 0;
                for (int i = 0; i < islands.Count; i++) { uint h = (uint)islands[i] * 2654435761u; sum += h; mix ^= (h << 13) | (h >> 19); }
                key = $"{_comp.Width}x{_comp.Height}:{dimAlpha}:{islands.Count}:{sum}:{mix}";
            }
            if (_focusOverlay != null && key == _focusKey) return _focusOverlay;
            _focusKey = key;
            var sorted = islands != null ? new System.Collections.Generic.List<int>(islands) : Enumerable.Range(0, _uv.islandCount).ToList();
            sorted.Sort();

            int ow = Mathf.Min(1024, _comp.Width);
            int oh = Mathf.Max(1, Mathf.RoundToInt(_comp.Height * (ow / (float)_comp.Width)));
            var set = new System.Collections.Generic.HashSet<int>(sorted);
            var px = _uv.SelectionOverlay(ow, oh, set, new Color32(255, 255, 255, 255));
            var bg = StudioGUI.CanvasBg;
            var dim = new Color32((byte)(bg.r * 255), (byte)(bg.g * 255), (byte)(bg.b * 255), dimAlpha);
            for (int i = 0; i < px.Length; i++) px[i] = px[i].a > 0 ? new Color32(0, 0, 0, 0) : dim;

            if (_focusOverlay == null || _focusOverlay.width != ow || _focusOverlay.height != oh)
            {
                if (_focusOverlay != null) DestroyImmediate(_focusOverlay);
                // sRGB (linear: false) so the dim colour matches the canvas background exactly in Linear projects.
                _focusOverlay = new Texture2D(ow, oh, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            _focusOverlay.SetPixels32(px);
            _focusOverlay.Apply(false);
            _focusSegments = _uv.SegmentsForIslands(set);
            return _focusOverlay;
        }

        /// <summary>"Before" hides every edit (canvas and avatar). Editing while it's on looks like nothing works, so turn it off.</summary>
        void ShowEditsIfBeforeOn()
        {
            if (!_showOriginal) return;
            _showOriginal = false;
            _dirty = true;
            Toast(Loc.T("“Compare: Original” was on, so you were seeing the original texture. Showing your edits again."), MessageType.Info, 4f);
        }

        /// <summary>While "Before" is on, say so on the canvas itself, with a one-click way back.</summary>
        void DrawBeforeBanner(Rect canvas)
        {
            if (!_showOriginal) return;
            var r = new Rect(canvas.x + 10, canvas.yMax - 40, Mathf.Min(canvas.width - 20, 330), 30);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0.09f, 0.09f, 0.09f, 0.95f));
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), StudioGUI.Accent);
                StudioGUI.DrawBorder(canvas, new Color(StudioGUI.Accent.r, StudioGUI.Accent.g, StudioGUI.Accent.b, 0.5f), 2);
            }
            GUI.Label(new Rect(r.x + 12, r.y, r.width - 130, r.height), Loc.T("Showing the ORIGINAL (Compare: Original is on)"), new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = StudioGUI.Accent } });
            if (GUI.Button(new Rect(r.xMax - 112, r.y + 5, 104, 20), Loc.T("Show my edits")))
            {
                _showOriginal = false;
                _dirty = true;
                Repaint();
            }
        }

        void DrawStatusBar(Rect r, string left, string right)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.StatusBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), StudioGUI.HeaderLine);
            }
            var leftW = Mathf.Min(r.width * 0.45f, StudioGUI.Status.CalcSize(new GUIContent(left)).x + 4);
            GUI.Label(new Rect(r.x, r.y, leftW, r.height), left, StudioGUI.Status);
            var hint = new GUIStyle(StudioGUI.Status) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(r.x + leftW, r.y, r.width - leftW, r.height), right, hint);
        }

        void DrawCanvasBrushCursor(Rect clip)
        {
            var mp = Event.current.mousePosition;
            if (!clip.Contains(mp)) return;
            if (_eyedropperArmed || Event.current.control || Event.current.command) { DrawPickerCircle(mp); return; }
            bool gradient = GradientToolActive;
            float rPx = _brush.fillTool || gradient ? 6f : _brush.size * 0.5f / Mathf.Max(1, _comp.SourceWidth) * _comp.Width * _zoom; // fill / gradient: a small target marker
            var c = new Vector3(mp.x, mp.y, 0);
            if (gradient && GradientHandleAt(mp) != GradHandle.None) EditorGUIUtility.AddCursorRect(new Rect(mp.x - 10, mp.y - 10, 20, 20), MouseCursor.MoveArrow);

            // Over texture space no mesh uses, a stroke can't show on the model (and with "stay inside" it paints nothing):
            // say so right at the cursor, before the user drags. (A gradient may start or end anywhere.)
            bool offModel = !gradient && _uv != null && _uv.IslandsAtUV(CanvasToUV(mp)).Count == 0;
            bool maskHides = MaskEditing && (_brush.fillTool ? _maskFillHides : _brush.mode == BrushMode.Erase);
            var ring = offModel ? StudioGUI.Danger : _brush.mode == BrushMode.Erase || maskHides ? new Color(1f, 0.55f, 0.55f)
                     : BlendToolActive ? new Color(0.55f, 0.85f, 1f) : Color.white;

            Handles.color = new Color(0, 0, 0, 0.7f);
            Handles.DrawWireDisc(c, Vector3.forward, rPx + 1f);
            Handles.color = ring;
            Handles.DrawWireDisc(c, Vector3.forward, rPx);
            if (offModel)
            {
                var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = StudioGUI.Danger } };
                string text = _brush.fillTool ? Loc.T("not part of the model: nothing to fill here") : _brush.stayInsideIslands ? Loc.T("not part of the model: nothing will paint here") : Loc.T("not part of the model: won't show on the avatar");
                var size = style.CalcSize(new GUIContent(text));
                var lr = new Rect(mp.x + rPx + 6, mp.y - 8, size.x + 6, 16);
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
            if (!gradient && !offModel) DrawCanvasMirrorCursor(mp, rPx); // where the mirrored paint will land
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
                case EventType.MouseDown when inside && !ToastHit(e.mousePosition):
                    if (_marqueeActive) { e.Use(); break; } // another button during a Box or Lasso drag must not take the mouse
                    if (e.button == 2 || e.button == 1 || (e.button == 0 && e.alt))
                    {
                        _panning = true;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    else if (e.button == 0)
                    {
                        var uv = CanvasToUV(local);
                        if (HandleCanvasMatchPick(uv)) { e.Use(); break; } // a Colour Match pick is waiting for a click
                        if (PartPicking) { PartPick(_uv.IslandsAtUV(uv), e.shift, e.control || e.command, null); e.Use(); break; } // 2 · Colour › By part
                        if (!_eyedropperArmed && !((_tool == Tool.Paint) && (e.control || e.command)) && BeginImageDrag(local, e))
                        {
                            GUIUtility.hotControl = id; // grabbed a handle of the selected Image layer
                            e.Use();
                            break;
                        }
                        if (_tool == Tool.Paint)
                        {
                            if (e.control || e.command || _eyedropperArmed) PickColour(uv);
                            else if (GradientToolActive)
                            {
                                if (BeginGradientDrag(local)) GUIUtility.hotControl = id;
                            }
                            else if (CanPaint(out var why))
                            {
                                BeginStroke(uv, Pressure(e), -1);
                                GUIUtility.hotControl = id;
                            }
                            else Toast(why, MessageType.Warning);
                            e.Use();
                        }
                        else if (_uv != null)
                        {
                            if (TileGridClick(r, e)) { e.Use(); break; } // a click on a cell of the tile grid selects that tile
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
                    else if (_liveStroke != null) AddStrokePoint(CanvasToUV(local), Pressure(e), true, -1);
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    if (_marqueeActive && e.button != 0) { e.Use(); break; } // only the left button ends the drag
                    if (_panning) _panning = false;
                    else if (_gradDrag != null) EndGradientDrag();
                    else if (_imgDrag != null) EndImageDrag();
                    else if (_marqueeActive && !_marqueeInScene) EndMarqueeCanvas();
                    else EndStroke();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.MouseMove when _tool == Tool.Paint:
                case EventType.MouseLeaveWindow:
                    Repaint(); // brush cursor follows the mouse
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
