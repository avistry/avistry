using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] GradientToolSettings _grad = new GradientToolSettings();

        enum GradHandle { None = -1, Start = 0, End = 1, Move = 2 }

        DrawnGradientLayer _gradDrag, _gradOriginal;
        System.Func<bool> _gradTargetMatches;
        Layer _gradInsertAbove;
        int _gradInsertAt, _gradDragControl;
        GradHandle _gradHandle = GradHandle.None;
        bool _gradPreviewActive, _gradOriginalView;
        Color? _gradRecentColour;
        Vector2 _gradGrabUV, _gradGrabStart, _gradGrabEnd;
        bool _revealSelectedGradient;
        static readonly GradientColours[] GradientColourSources = (GradientColours[])System.Enum.GetValues(typeof(GradientColours));

        bool GradientToolActive => _tool == Tool.Paint && _brush.gradientTool && _brush.mode == BrushMode.Paint && !MaskEditing;
        DrawnGradientLayer SelectedGradient => ActiveLayer as DrawnGradientLayer;

        void SetPaintTool(int t)
        {
            SetPickForPaint(false);
            CancelSceneGradient();
            CancelGradientDrag();
            CancelImageDrag();
            _brush.mode = t == 1 ? BrushMode.Erase : BrushMode.Paint;
            _brush.fillTool = t == 2;
            _brush.gradientTool = t == 3;
            _brush.blendTool = t == 4;
            Repaint();
            SceneView.RepaintAll();
        }

        int PaintToolIndex => _brush.mode == BrushMode.Erase ? 1
                            : _brush.blendTool ? 4 : _brush.gradientTool ? 3 : _brush.fillTool ? 2 : 0;

        bool BlendToolActive => _tool == Tool.Paint && _brush.blendTool && _brush.mode == BrushMode.Paint && !_brush.fillTool;

        Vector2 UVToCanvas(Vector2 uv) => new Vector2(_pan.x + uv.x * _comp.Width * _zoom, _pan.y + (1f - uv.y) * _comp.Height * _zoom);

        GradHandle GradientHandleAt(Vector2 local)
        {
            var g = TextureGradientForHandles;
            if (g == null || _comp == null) return GradHandle.None;
            Vector2 a = UVToCanvas(g.start), b = UVToCanvas(g.end);
            if (Vector2.Distance(local, b) <= 9f) return GradHandle.End;
            if (Vector2.Distance(local, a) <= 9f) return GradHandle.Start;
            if (Vector2.Distance(local, (a + b) * 0.5f) <= 7f) return GradHandle.Move;
            return GradHandle.None;
        }

        bool BeginGradientDrag(Vector2 local) => BeginTextureGradient(local, allowAdjustment: true);

        bool BeginTextureGradient(Vector2 local, bool allowAdjustment)
        {
            if (!CanPaint(out var why)) { Toast(why, MessageType.Warning); return false; }
            CancelSceneGradient();
            CancelGradientDrag();
            CancelImageDrag();
            EndStroke();
            var targetMatches = CaptureSavedEditContext(_recipe, CurrentSlot);
            if (!targetMatches())
            {
                if (_needsRebuild) _preview.Clear();
                Toast(Loc.T("Edit changed. Redraw gradient."), MessageType.Warning);
                return false;
            }
            _gradTargetMatches = targetMatches;
            var uv = CanvasToUV(local);
            _gradOriginalView = _showOriginal;
            var handle = allowAdjustment ? GradientHandleAt(local) : GradHandle.None;
            if (handle != GradHandle.None)
            {
                _gradOriginal = SelectedGradient;
                _gradDrag = _gradOriginal.CopyPlacement();
                _gradHandle = handle;
            }
            else
            {
                var layer = NewDrawnGradientLayer();
                layer.start = layer.end = uv;
                CaptureGradientPieces(layer);
                _gradDrag = layer;
                _gradInsertAbove = ActiveLayer;
                _gradInsertAt = _recipe.layers.Count;
                _gradRecentColour = _grad.colours == GradientColours.Custom ? (Color?)null : _brush.color;
                _gradHandle = GradHandle.End;
            }
            _gradGrabUV = uv;
            _gradGrabStart = _gradDrag.start;
            _gradGrabEnd = _gradDrag.end;
            return true;
        }

        void CaptureGradientPieces(DrawnGradientLayer layer)
        {
            if (CaptureFaceSelection(layer)) return;
            foreach (int piece in _selection)
                if (IsCurrentPiece(piece)) layer.islands.Add(piece);
            if (layer.islands.Count > 0) layer.maskMode = MaskMode.SelectedIslands;
        }

        DrawnGradientLayer NewDrawnGradientLayer() => new DrawnGradientLayer
        {
            name = _grad.shape switch
            {
                GradientShape.Circle => Loc.T("Gradient (circle)"),
                GradientShape.Ellipse => Loc.T("Gradient (ellipse)"),
                _ => Loc.T("Gradient (linear)"),
            },
            gradient = NewGradientRamp(_grad.colours),
            shape = _grad.shape,
            edge = _grad.edge,
            flatness = _grad.flatness,
            dither = _grad.dither,
            blend = _grad.blend,
            opacity = _brush.opacity,
        };

        void UpdateGradientDrag(Vector2 local, bool snap)
        {
            if (_gradDrag == null || _comp == null) return;
            var d = CanvasToUV(local) - _gradGrabUV;
            switch (_gradHandle)
            {
                case GradHandle.Start:
                    var start = _gradGrabStart + d;
                    _gradDrag.start = d != Vector2.zero ? ConstrainTextureGradient(_gradGrabEnd, start, snap) : start;
                    break;
                case GradHandle.End:
                    var end = _gradGrabEnd + d;
                    _gradDrag.end = d != Vector2.zero ? ConstrainTextureGradient(_gradGrabStart, end, snap) : end;
                    break;
                case GradHandle.Move:
                    _gradDrag.start = _gradGrabStart + d;
                    _gradDrag.end = _gradGrabEnd + d;
                    break;
            }
            if (!_gradPreviewActive)
            {
                if (!GradientDragChanged()) return;
                _gradPreviewActive = true;
                ShowEditsIfBeforeOn();
            }
            ComposeNow();
            Repaint();
        }

        Vector2 Snap45(Vector2 anchor, Vector2 uv)
        {
            var scale = new Vector2(_comp.Width, _comp.Height);
            var d = Vector2.Scale(uv - anchor, scale);
            if (d.sqrMagnitude < 1e-6f) return uv;
            float a = Mathf.Round(Mathf.Atan2(d.y, d.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            var s = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d.magnitude;
            return anchor + new Vector2(s.x / scale.x, s.y / scale.y);
        }

        bool GradientDragChanged()
        {
            if (_gradDrag == null || _comp == null) return false;
            if (_gradOriginal == null)
                return Vector2.Scale(_gradDrag.end - _gradDrag.start, new Vector2(_comp.Width, _comp.Height)).sqrMagnitude >= 9f;
            return (_gradDrag.start - _gradGrabStart).sqrMagnitude >= 1e-12f ||
                   (_gradDrag.end - _gradGrabEnd).sqrMagnitude >= 1e-12f;
        }

        int GradientPlacementIndex()
        {
            if (_gradOriginal != null) return _recipe.layers.IndexOf(_gradOriginal);
            if (_gradInsertAbove == null) return Mathf.Min(_gradInsertAt, _recipe.layers.Count);
            int above = _recipe.layers.IndexOf(_gradInsertAbove);
            return above < 0 ? -1 : above + 1;
        }

        bool EndGradientDrag()
        {
            var draft = _gradDrag;
            if (draft == null) return false;
            var original = _gradOriginal;
            var recent = _gradRecentColour;
            if (!TextureGradientTargetStillMatches())
            {
                AbandonTextureGradient(targetChanged: true);
                return false;
            }
            if (!GradientDragChanged())
            {
                CancelGradientDrag();
                if (original == null)
                    Toast(Loc.T("Drag across the texture to draw a gradient: it starts where you press and ends where you let go. Shift snaps to 45°."), MessageType.Info, 5f);
                return false;
            }

            Undo.FlushUndoRecordObjects();
            if (_gradDrag != draft) return false;
            if (!TextureGradientTargetStillMatches())
            {
                AbandonTextureGradient(targetChanged: true);
                return false;
            }
            int index = GradientPlacementIndex();
            CancelGradientDrag(restoreComparison: false);
            Undo.IncrementCurrentGroup();
            string undoName = original == null ? "Draw gradient" : "Adjust gradient";
            Undo.SetCurrentGroupName(undoName);
            if (original == null)
            {
                InsertLayer(draft, undoName, captureSelection: false, insertionIndex: index);
                if (recent.HasValue) RememberColour(recent.Value);
            }
            else
            {
                Undo.RecordObject(_recipe, undoName);
                original.start = draft.start;
                original.end = draft.end;
                MarkEdited();
            }
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            ComposeNow();
            return true;
        }

        bool TextureGradientTargetStillMatches() =>
            GradientToolActive && _gradTargetMatches?.Invoke() == true && GradientPlacementIndex() >= 0;

        void AbandonTextureGradient(bool targetChanged)
        {
            CancelGradientDrag();
            if (_needsRebuild) _preview.Clear();
            else if (_dirty) ComposeNow();
            if (targetChanged) Toast(Loc.T("Edit changed. Redraw gradient."), MessageType.Warning);
        }

        void CancelGradientDrag(bool restoreComparison = true)
        {
            if (_gradDrag == null) return;
            _gradDrag = _gradOriginal = null;
            _gradTargetMatches = null;
            _gradInsertAbove = null;
            _gradRecentColour = null;
            _placementPreview.Clear();
            if (_gradPreviewActive)
            {
                if (restoreComparison) _showOriginal = _gradOriginalView;
                _dirty = true;
                SceneView.RepaintAll();
            }
            _gradPreviewActive = false;
            Repaint();
        }

        void ReleaseGradientDragControl()
        {
            if (_gradDragControl == 0) return;
            bool ownsPointer = GUIUtility.hotControl == _gradDragControl;
            if (_gradDrag != null && ownsPointer) return;
            if (ownsPointer) GUIUtility.hotControl = 0;
            _gradDragControl = 0;
            if (_gradDrag != null) AbandonTextureGradient(targetChanged: !TextureGradientTargetStillMatches());
        }

        void DrawGradientOverlay()
        {
            var g = _gradDrag ?? TextureGradientForHandles;
            if (g == null || g.sceneProjection || !g.enabled || _comp == null || Event.current.type != EventType.Repaint) return;
            Vector3 a = UVToCanvas(g.start), b = UVToCanvas(g.end);
            float len = Vector2.Distance(a, b);

            Handles.color = new Color(1, 1, 1, 0.45f);
            if (g.shape == GradientShape.Circle) Handles.DrawWireDisc(a, Vector3.forward, len);
            else if (g.shape == GradientShape.Ellipse && len > 1f)
            {
                var dir = (b - a) / len;
                var perp = new Vector3(-dir.y, dir.x, 0);
                var pts = new Vector3[65];
                for (int i = 0; i <= 64; i++)
                {
                    float t = i / 64f * Mathf.PI * 2f;
                    pts[i] = a + dir * (Mathf.Cos(t) * len) + perp * (Mathf.Sin(t) * len * Mathf.Max(0.05f, g.flatness));
                }
                Handles.DrawPolyLine(pts);
            }
            else if (len > 1f)
            {
                var dir = (b - a) / len;
                var perp = new Vector3(-dir.y, dir.x, 0) * 2000f;
                Handles.DrawDottedLine(a - perp, a + perp, 3f);
                Handles.DrawDottedLine(b - perp, b + perp, 3f);
            }

            Handles.color = new Color(0, 0, 0, 0.8f);
            Handles.DrawAAPolyLine(4f, a, b);
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f, a, b);
            DrawGradientDot(a, g.gradient.Evaluate(0f), Loc.T("start"));
            DrawGradientDot(b, g.gradient.Evaluate(1f), Loc.T("end"));
            var mid = (a + b) * 0.5f;
            EditorGUI.DrawRect(new Rect(mid.x - 4, mid.y - 4, 8, 8), new Color(0, 0, 0, 0.8f));
            EditorGUI.DrawRect(new Rect(mid.x - 3, mid.y - 3, 6, 6), Color.white);
            Handles.color = Color.white;
        }

        static void DrawGradientDot(Vector3 p, Color c, string label)
        {
            Handles.color = new Color(0, 0, 0, 0.85f);
            Handles.DrawSolidDisc(p, Vector3.forward, 7f);
            Handles.color = Color.white;
            Handles.DrawSolidDisc(p, Vector3.forward, 5.5f);
            Handles.color = new Color(c.r, c.g, c.b, 1f);
            Handles.DrawSolidDisc(p, Vector3.forward, 4f);
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
            var size = style.CalcSize(new GUIContent(label));
            var r = new Rect(p.x + 9, p.y - 8, size.x + 4, 15);
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f, 0.8f));
            GUI.Label(new Rect(r.x + 2, r.y - 1, size.x, 16), label, style);
        }

        static Gradient CopyRamp(Gradient ramp)
        {
            var copy = GradientToolSettings.Clone(ramp);
            if (ramp != null) copy.colorSpace = ramp.colorSpace;
            return copy;
        }

        Gradient NewGradientRamp(GradientColours colours) => colours == GradientColours.Custom
            ? CopyRamp(_grad.custom)
            : GradientToolSettings.FromColour(_brush.color, colours, _brush.subColor);

        Gradient DrawGradientRamp(Gradient workingCopy, bool forNew, System.Action<Gradient> chooseSet)
        {
            bool before = GUI.changed;
            bool changed;
            Gradient ramp;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                ramp = EditorGUILayout.GradientField(new GUIContent(Loc.T("Colour ramp"),
                    Loc.T("Click to edit colour and transparency stops. Left is the start, right is the end.")), workingCopy);
                changed = EditorGUI.EndChangeCheck();
                if (forNew) StudioGUI.Count("paint.gradientBar", GUILayoutUtility.GetLastRect());
                if (forNew) StudioGUI.Count("paint.gradientSets");
                if (GUILayout.Button(new GUIContent(Loc.T("Sets"), Loc.T("Choose a ready-made colour ramp")), EditorStyles.miniPullDown))
                {
                    var menu = new GenericMenu();
                    foreach (var (name, make) in GradientToolSettings.Sets)
                    {
                        var preset = make;
                        menu.AddItem(new GUIContent(name), false, () => { if (this != null) chooseSet(preset()); });
                    }
                    menu.ShowAsContext();
                }
            }
            changed |= DrawGradientStops(ramp, forNew);
            GUI.changed = before || changed;
            return changed ? ramp : null;
        }

        static string GradientChoiceHint(string purpose) =>
            purpose + "\n" + Loc.T("Click a choice, then use arrow keys or Shift+scroll to switch.");

        static readonly GradientEdge[] GradientEdges =
            { GradientEdge.NotRepeat, GradientEdge.Repeat, GradientEdge.ReverseRepeat, GradientEdge.DontDraw };

        static GradientEdge DrawGradientEdgeChoice(GradientEdge edge, string controlId, float width)
        {
            var names = new[] { Loc.T("Extend ends"), Loc.T("Repeat"), Loc.T("Reverse repeat"), Loc.T("Stop at ends") };
            var label = new GUIContent(Loc.T("Beyond the ends"), Loc.T("What happens past the start and end points"));
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            if (width >= StackedChoiceWidth)
                EditorGUIUtility.labelWidth = Mathf.Max(previousLabelWidth,
                    Mathf.Min(width * .55f, EditorStyles.label.CalcSize(label).x + 6f));
            try
            {
                edge = SteppedChoice(label, edge, GradientEdges,
                    (field, value) => (GradientEdge)EditorGUI.Popup(field, (int)value, names), width);
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            var rect = GUILayoutUtility.GetLastRect();
            if (width > 0f) rect.width = Mathf.Min(rect.width, width);
            StudioGUI.Count(controlId, rect);
            return edge;
        }

        (GradientShape shape, GradientEdge edge, float flatness, bool dither) DrawGradientShapeAndEdge(
            GradientShape shape, GradientEdge edge, float flatness, bool dither, bool forNew)
        {
            shape = (GradientShape)ToolPalette((int)shape, GradientShapeGlyphs, PaintLabels.gradientShape, PanelContentWidth, scene: false);
            StudioGUI.Count(forNew ? "paint.gradientShape" : "layers.fields", GUILayoutUtility.GetLastRect());
            if (shape == GradientShape.Ellipse)
            {
                if (forNew) StudioGUI.Count("paint.gradientFlatness");
                flatness = EditorGUILayout.Slider(new GUIContent(Loc.T("Flatness"),
                    Loc.T("1 = circle, lower = flatter across the drag direction")), flatness, 0.05f, 1f);
            }
            if (!forNew || Advanced)
            {
                edge = DrawGradientEdgeChoice(edge, forNew ? "paint.gradientEdge" : "layers.gradientEdge", PanelContentWidth);
                if (forNew) StudioGUI.Count("paint.gradientDither");
                dither = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Dithering (no banding)"),
                    Loc.T("Adds invisible noise so smooth gradients don't show steps after saving")), dither);
            }
            return (shape, edge, flatness, dither);
        }

        GradientColours DrawGradientColourChoice(GUIContent label, float width = -1f)
        {
            var choices = width > 0f && width < StackedChoiceWidth
                ? new[] { Loc.T("To transparent"), Loc.T("To white"), Loc.T("To black"), Loc.T("Custom"), Loc.T("To second") }
                : new[] { Loc.T("Main to transparent"), Loc.T("Main to white"), Loc.T("Main to black"),
                    Loc.T("Custom gradient"), Loc.T("Main to second colour") };
            return SteppedChoice(label, _grad.colours, GradientColourSources,
                (rect, current) => (GradientColours)EditorGUI.Popup(rect, (int)current, choices), width);
        }

        void DrawGradientSettings()
        {
            if (SelectedGradient != null && GUILayout.Button(Loc.T("Edit selected gradient"), EditorStyles.miniButton))
                RevealSelectedGradient();
            GUILayout.Label(Loc.T("New gradient defaults"), StudioGUI.GroupTitle);
            EditorGUI.BeginChangeCheck();
            StudioGUI.Count("paint.gradientColours");
            var colours = DrawGradientColourChoice(new GUIContent(Loc.T("Colours"),
                Loc.T("Where new gradients get their colours. Edit the ramp to make your own.")));
            var ramp = DrawGradientRamp(NewGradientRamp(colours), true, SetNewGradientRamp);
            var appearance = DrawGradientShapeAndEdge(_grad.shape, _grad.edge, _grad.flatness, _grad.dither, true);
            var blend = _grad.blend;
            InfoRow("paint.gradientBlend", "blend-mode", () => blend = SteppedChoice(new GUIContent(Loc.T("Blend mode")), blend,
                AllBlendModes, (rect, current) => (BlendMode)EditorGUI.EnumPopup(rect, current)));
            float opacity = _brush.opacity;
            InfoRow("paint.strength", "opacity", () => opacity = EditorGUILayout.Slider(new GUIContent(Loc.T("Shared opacity"),
                Loc.T("Sets opacity for new gradients and paint strength for the other paint tools.")), opacity, 0f, 1f));
            if (EditorGUI.EndChangeCheck())
            {
                bool shapeChanged = appearance.shape != _grad.shape;
                if (shapeChanged)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.IncrementCurrentGroup();
                }
                Undo.RecordObject(this, "Edit new gradient settings");
                _grad.colours = ramp != null ? GradientColours.Custom : colours;
                if (ramp != null) _grad.custom = CopyRamp(ramp);
                (_grad.shape, _grad.edge, _grad.flatness, _grad.dither) = appearance;
                _grad.blend = blend;
                if (_brush.opacity != opacity) { _brush.opacity = opacity; MarkBrushEdited(); }
                if (shapeChanged)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.IncrementCurrentGroup();
                }
                Repaint();
            }

            DrawGradientDirection();
            if (_gradientDirection == GradientDirection.Free)
                GUILayout.Label(Loc.T("Drag for a new layer. On the texture, hold Shift to snap the angle to 45°."), StudioGUI.Hint);
        }

        void RevealSelectedGradient()
        {
            _openLayerSettings = true;
            _revealSelectedGradient = true;
            Repaint();
        }

        void SetNewGradientRamp(Gradient ramp)
        {
            Undo.RecordObject(this, "Choose new gradient ramp");
            StoreCustomGradientRamp(ramp);
            Repaint();
        }

        void StoreCustomGradientRamp(Gradient ramp)
        {
            _grad.custom = CopyRamp(ramp);
            _grad.colours = GradientColours.Custom;
        }

        void DrawSelectedGradientSettings(DrawnGradientLayer layer)
        {
            EditorGUI.BeginChangeCheck();
            var ramp = DrawGradientRamp(CopyRamp(layer.gradient), false,
                preset => EditGradientLayer(layer, l => l.gradient = CopyRamp(preset)));
            var appearance = DrawGradientShapeAndEdge(layer.shape, layer.edge, layer.flatness, layer.dither, false);
            var start = layer.start;
            var end = layer.end;
            if (!layer.sceneProjection)
            {
                start = EditorGUILayout.Vector2Field(Loc.T("Start on texture"), start);
                end = EditorGUILayout.Vector2Field(Loc.T("End on texture"), end);
            }
            if (EditorGUI.EndChangeCheck()) EditGradientLayer(layer, l =>
            {
                if (ramp != null) l.gradient = CopyRamp(ramp);
                (l.shape, l.edge, l.flatness, l.dither) = appearance;
                if (!l.sceneProjection) { l.start = start; l.end = end; }
            }, isolateUndo: appearance.shape != layer.shape);

            if (Advanced)
            {
                StudioGUI.Count("paint.gradientApply");
                if (GUILayout.Button(new GUIContent(Loc.T("Copy defaults to this layer"),
                    Loc.T("Use the new ramp, shape, edges, dithering, blend mode and opacity on this layer."))))
                    ApplyGradientDefaults(layer);
            }
            GUILayout.Label(layer.sceneProjection
                ? Loc.T("Use the Gradient tool in the Scene view. Adjust the end dots or drag the middle square to move the gradient.")
                : Loc.T("Use the Gradient tool to drag the endpoints or middle handle on the texture."), StudioGUI.Hint);
        }

        bool EditGradientLayer(DrawnGradientLayer layer, System.Action<DrawnGradientLayer> edit, bool isolateUndo = false)
        {
            if (_recipe == null || !_recipe.layers.Contains(layer)) return false;
            if (isolateUndo)
            {
                if (CurrentSlot is not TextureSlot slot) return false;
                var sameTarget = CaptureSavedEditContext(_recipe, slot);
                Undo.FlushUndoRecordObjects();
                if (!sameTarget() || SelectedGradient != layer || !_recipe.layers.Contains(layer)) return false;
            }
            var candidate = layer.CopyPlacement();
            edit(candidate);
            if (!ValidatePlacedGradientAppearance(layer, candidate)) return false;
            if (isolateUndo) Undo.IncrementCurrentGroup();
            Undo.RecordObject(_recipe, "Edit gradient settings");
            layer.gradient = candidate.gradient;
            layer.shape = candidate.shape;
            layer.edge = candidate.edge;
            layer.flatness = candidate.flatness;
            layer.dither = candidate.dither;
            layer.start = candidate.start;
            layer.end = candidate.end;
            layer.blend = candidate.blend;
            layer.opacity = candidate.opacity;
            MarkEdited();
            if (isolateUndo)
            {
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            SceneView.RepaintAll();
            return true;
        }

        void ApplyGradientDefaults(DrawnGradientLayer layer) => EditGradientLayer(layer, l =>
        {
            l.gradient = NewGradientRamp(_grad.colours);
            l.shape = _grad.shape;
            l.edge = _grad.edge;
            l.flatness = _grad.flatness;
            l.dither = _grad.dither;
            l.blend = _grad.blend;
            l.opacity = _brush.opacity;
        });

        void DrawGradientTargetGroup()
        {
            var sel = SelectedGradient;
            if (!StudioGUI.BeginFoldGroup(ref _openPaintOptions, Loc.T("Where the gradient goes"), sel?.name, infoId: "mask"))
            {
                StudioGUI.EndGroup();
                return;
            }
            if (sel is { sceneProjection: true })
                GUILayout.Label(Loc.F("Selected: “{0}”. Drag its handles in the Scene view.", sel.name), StudioGUI.CheckLabel);
            else if (TextureGradientForHandles != null)
                GUILayout.Label(Loc.F("Selected: “{0}”. Its dots are shown on the texture.", sel.name), StudioGUI.CheckLabel);
            string newScope = HasFaceSelection
                ? Loc.F("A new gradient is limited to the {0} selected face(s).", SelectedElementCount)
                : _selection.Count > 0
                    ? Loc.F("A new drag adds a gradient layer above the selected layer, limited to the {0} selected piece(s).", _selection.Count)
                    : Loc.T("A new drag adds a gradient layer above the selected layer. Use Limit to keep it on chosen pieces.");
            GUILayout.Label(newScope, StudioGUI.CheckLabel);
            if (sel != null && sel.maskMode == MaskMode.SelectedIslands)
                StudioGUI.CheckRow(StudioGUI.Check.Note, sel.invertMask ? Loc.F("“{0}” is limited to {1} piece{2} (inverted).", sel.name, sel.islands.Count, (sel.islands.Count == 1 ? "" : "s")) : Loc.F("“{0}” is limited to {1} piece{2}.", sel.name, sel.islands.Count, (sel.islands.Count == 1 ? "" : "s")));
            if (sel != null && sel.opacity <= 0.001f) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("The selected gradient is at 0% opacity, so it won't show."));
            using (new EditorGUI.DisabledScope(_uv == null))
            {
                bool paintInScene = _paintInScene;
                InfoRow("paint.onAvatar", "scene-view", () => paintInScene = EditorGUILayout.ToggleLeft(new GUIContent(
                    Loc.T("Paint directly on the model (Scene view)"),
                    Loc.T("Start the gradient on the model. The other end can extend past it.")), _paintInScene));
                if (paintInScene != _paintInScene)
                {
                    Undo.RecordObject(this, "Paint on the model");
                    _paintInScene = paintInScene;
                    if (!_paintInScene) CancelSceneGradient();
                    SceneView.RepaintAll();
                    Repaint();
                }
            }
            if (_paintInScene && _uv != null)
                GUILayout.Label(Loc.T("Drag in the Scene view. The gradient can reach the back of selected pieces. " +
                    "Pieces that share texture space share the colour."), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }
    }
}
