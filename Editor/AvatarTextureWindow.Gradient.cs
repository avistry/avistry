using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Gradient tool (CLIP STUDIO PAINT-style): drag on the texture to draw a gradient. Each drag makes an editable
    /// gradient layer (start/end points, linear / circle / ellipse, edge process, dithering). With a gradient layer
    /// selected, its start and end dots can be dragged to adjust it, or the middle square to move it.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] GradientToolSettings _grad = new GradientToolSettings();

        enum GradHandle { None = -1, Start = 0, End = 1, Move = 2 }

        DrawnGradientLayer _gradDrag;
        GradHandle _gradHandle = GradHandle.None;
        bool _gradCreated;
        int _gradUndoGroup;
        Vector2 _gradGrabUV, _gradGrabStart, _gradGrabEnd;
        static Texture2D s_gradientIcon;

        bool GradientToolActive => _tool == Tool.Paint && _brush.gradientTool && _brush.mode == BrushMode.Paint && !MaskEditing;
        DrawnGradientLayer SelectedGradient => ActiveLayer as DrawnGradientLayer;

        static void DestroyGradientIcon()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyGradientIcon;
            if (s_gradientIcon != null) Object.DestroyImmediate(s_gradientIcon);
            s_gradientIcon = null;
        }

        static GUIContent GradientToolContent(string text, string tip)
        {
            if (s_gradientIcon == null)
            {
                AssemblyReloadEvents.beforeAssemblyReload -= DestroyGradientIcon;
                AssemblyReloadEvents.beforeAssemblyReload += DestroyGradientIcon;
                s_gradientIcon = new Texture2D(16, 16, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                var px = new Color32[256];
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    bool border = x == 1 || x == 14 || y == 2 || y == 13;
                    bool inside = x > 1 && x < 14 && y > 2 && y < 13;
                    byte v = (byte)(40 + x * 13);
                    px[y * 16 + x] = border && x >= 1 && x <= 14 && y >= 2 && y <= 13 ? new Color32(200, 200, 200, 255)
                                    : inside ? new Color32(v, v, v, 255) : new Color32(0, 0, 0, 0);
                }
                s_gradientIcon.SetPixels32(px);
                s_gradientIcon.Apply(false);
            }
            return new GUIContent(text, s_gradientIcon, tip);
        }

        /// <summary>Switches the Paint tab's tool: 0 brush, 1 eraser, 2 fill, 3 gradient, 4 blend (blur / smudge).</summary>
        void SetPaintTool(int t)
        {
            if (_gradDrag != null) EndGradientDrag();
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
            var g = SelectedGradient;
            if (g == null || !g.enabled || _comp == null) return GradHandle.None;
            Vector2 a = UVToCanvas(g.start), b = UVToCanvas(g.end);
            if (Vector2.Distance(local, b) <= 9f) return GradHandle.End;
            if (Vector2.Distance(local, a) <= 9f) return GradHandle.Start;
            if (Vector2.Distance(local, (a + b) * 0.5f) <= 7f) return GradHandle.Move;
            return GradHandle.None;
        }

        /// <summary>Mouse down with the Gradient tool: grab a handle of the selected gradient, or start drawing a new one.</summary>
        bool BeginGradientDrag(Vector2 local)
        {
            if (!CanPaint(out var why)) { Toast(why, MessageType.Warning); return false; }
            EndStroke();
            ShowEditsIfBeforeOn();
            var uv = CanvasToUV(local);
            Undo.IncrementCurrentGroup();
            _gradUndoGroup = Undo.GetCurrentGroup();
            var handle = GradientHandleAt(local);
            if (handle != GradHandle.None)
            {
                _gradDrag = SelectedGradient;
                _gradHandle = handle;
                _gradCreated = false;
                if (!_gradDrag.enabled) _gradDrag.enabled = true;
            }
            else
            {
                var layer = new DrawnGradientLayer
                {
                    name = $"Gradient ({_grad.shape.ToString().ToLowerInvariant()})",
                    gradient = _grad.Build(_brush.color, _brush.subColor),
                    shape = _grad.shape,
                    edge = _grad.edge,
                    flatness = _grad.flatness,
                    dither = _grad.dither,
                    blend = _grad.blend,
                    opacity = _brush.opacity,
                    start = uv,
                    end = uv,
                };
                InsertLayer(layer, "Draw gradient");
                if (_grad.colours != GradientColours.Custom) RememberColour(_brush.color);
                _gradDrag = layer;
                _gradHandle = GradHandle.End;
                _gradCreated = true;
            }
            _gradGrabUV = uv;
            _gradGrabStart = _gradDrag.start;
            _gradGrabEnd = _gradDrag.end;
            return true;
        }

        void UpdateGradientDrag(Vector2 local, bool snap)
        {
            if (_gradDrag == null || _comp == null) return;
            var uv = CanvasToUV(local);
            switch (_gradHandle)
            {
                case GradHandle.Start: _gradDrag.start = snap ? Snap45(_gradDrag.end, uv) : uv; break;
                case GradHandle.End: _gradDrag.end = snap ? Snap45(_gradDrag.start, uv) : uv; break;
                case GradHandle.Move:
                    var d = uv - _gradGrabUV;
                    _gradDrag.start = _gradGrabStart + d;
                    _gradDrag.end = _gradGrabEnd + d;
                    break;
            }
            ComposeNow();
            Repaint();
        }

        /// <summary>Shift: snap the direction to 45° steps, measured in texture pixels (so it also works on non-square textures).</summary>
        Vector2 Snap45(Vector2 anchor, Vector2 uv)
        {
            var scale = new Vector2(_comp.Width, _comp.Height);
            var d = Vector2.Scale(uv - anchor, scale);
            if (d.sqrMagnitude < 1e-6f) return uv;
            float a = Mathf.Round(Mathf.Atan2(d.y, d.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            var s = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d.magnitude;
            return anchor + new Vector2(s.x / scale.x, s.y / scale.y);
        }

        void EndGradientDrag()
        {
            if (_gradDrag == null) return;
            var g = _gradDrag;
            _gradDrag = null;
            Vector2 s = g.start, e = g.end;
            float lengthPx = _comp != null ? Vector2.Scale(e - s, new Vector2(_comp.Width, _comp.Height)).magnitude : 0f;
            if (_gradCreated && lengthPx < 3f)
            {
                // A click, not a drag: take the empty layer away again and explain.
                Undo.RevertAllDownToGroup(_gradUndoGroup);
                Toast(Loc.T("Drag across the texture to draw a gradient: it starts where you press and ends where you let go. Shift snaps to 45°."), MessageType.Info, 5f);
                ComposeNow();
                return;
            }
            if (!_gradCreated && (s - _gradGrabStart).sqrMagnitude < 1e-12f && (e - _gradGrabEnd).sqrMagnitude < 1e-12f) { Repaint(); return; }

            // Record the whole drag as one undo step (the layer was edited live while dragging).
            g.start = _gradGrabStart;
            g.end = _gradGrabEnd;
            string undoName = _gradCreated ? "Draw gradient" : "Adjust gradient";
            Undo.RecordObject(_recipe, undoName);
            g.start = s;
            g.end = e;
            Undo.SetCurrentGroupName(undoName);
            Undo.CollapseUndoOperations(_gradUndoGroup);
            MarkEdited();
            ComposeNow();
        }

        /// <summary>The selected gradient's line, ends and outline, drawn over the texture (canvas-local coordinates).</summary>
        void DrawGradientOverlay()
        {
            var g = SelectedGradient;
            if (g == null || !g.enabled || _comp == null || Event.current.type != EventType.Repaint) return;
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
                // Linear: lines across the start and end, where the gradient begins and finishes.
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

        void DrawGradientSettings()
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("paint.gradientColours");
                _grad.colours = (GradientColours)EditorGUILayout.EnumPopup(new GUIContent(Loc.T("Colours"), Loc.T("Where the gradient's colours come from. Editing the bar below makes it a custom gradient.")), _grad.colours);
                StudioGUI.Info("gradient-map");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("paint.gradientBar");
                var shown = _grad.colours == GradientColours.Custom ? _grad.custom : _grad.Build(_brush.color, _brush.subColor);
                EditorGUI.BeginChangeCheck();
                var edited = EditorGUILayout.GradientField(GUIContent.none, shown);
                if (EditorGUI.EndChangeCheck() && edited != null)
                {
                    _grad.custom = GradientToolSettings.Clone(edited);
                    _grad.colours = GradientColours.Custom;
                }
                StudioGUI.Count("paint.gradientSets");
                if (GUILayout.Button(new GUIContent(Loc.T("Sets"), Loc.T("Ready-made gradient sets")), EditorStyles.miniPullDown, GUILayout.Width(46)))
                {
                    var menu = new GenericMenu();
                    foreach (var (name, make) in GradientToolSettings.Sets)
                    {
                        var mk = make;
                        menu.AddItem(new GUIContent(name), false, () => { _grad.custom = mk(); _grad.colours = GradientColours.Custom; Repaint(); });
                    }
                    menu.ShowAsContext();
                }
            }

            StudioGUI.Count("paint.gradientShape");
            var sr = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(Loc.T("Shape"), Loc.T("Linear: a straight blend. Circle and Ellipse: from the centre (start point) outwards.")));
            _grad.shape = (GradientShape)GUI.Toolbar(sr, (int)_grad.shape, new[] { Loc.T("Linear"), Loc.T("Circle"), Loc.T("Ellipse") }, EditorStyles.miniButton);
            if (_grad.shape == GradientShape.Ellipse)
            {
                StudioGUI.Count("paint.gradientFlatness");
                _grad.flatness = EditorGUILayout.Slider(new GUIContent(Loc.T("Flatness"), Loc.T("1 = circle, lower = flatter across the drag direction")), _grad.flatness, 0.05f, 1f);
            }
            if (Advanced)
            {
                StudioGUI.Count("paint.gradientEdge");
                _grad.edge = (GradientEdge)EditorGUILayout.EnumPopup(new GUIContent(Loc.T("Edge process"), Loc.T("What happens past the start and end points")), _grad.edge);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("paint.gradientBlend");
                _grad.blend = (BlendMode)EditorGUILayout.EnumPopup(new GUIContent(Loc.T("Blend"), Loc.T("How the gradient mixes with the texture. Normal paints it on; Multiply shades; Color tints but keeps shading.")), _grad.blend);
                StudioGUI.Info("blend-mode");
            }
            if (Advanced)
            {
                StudioGUI.Count("paint.gradientDither");
                _grad.dither = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Dithering (no banding)"), Loc.T("Adds invisible noise so smooth gradients don't show steps after saving")), _grad.dither);
            }
            if (EditorGUI.EndChangeCheck()) Repaint();

            var sel = SelectedGradient;
            if (sel != null)
            {
                if (Advanced)
                {
                StudioGUI.Count("paint.gradientApply");
                if (GUILayout.Button(new GUIContent(Loc.F("Use these settings on “{0}”", sel.name), Loc.T("Apply the colours, shape, edge process, blend, dithering and opacity above to the selected gradient layer")), EditorStyles.miniButton))
                {
                    Undo.RecordObject(_recipe, "Gradient settings");
                    sel.gradient = _grad.Build(_brush.color, _brush.subColor);
                    sel.shape = _grad.shape;
                    sel.edge = _grad.edge;
                    sel.flatness = _grad.flatness;
                    sel.dither = _grad.dither;
                    sel.blend = _grad.blend;
                    sel.opacity = _brush.opacity;
                    MarkEdited();
                }
                }
                GUILayout.Label(Loc.T("Drag across the texture for a new gradient layer. Drag the start / end dots to adjust the selected one, the middle square to move it. Shift snaps to 45°."), StudioGUI.Hint);
            }
            else GUILayout.Label(Loc.T("Drag across the texture: the gradient starts where you press and ends where you let go. Shift snaps to 45°. Each gradient is its own layer you can adjust later."), StudioGUI.Hint);
        }

        /// <summary>"Where the paint goes" for the Gradient tool.</summary>
        void DrawGradientTargetGroup()
        {
            StudioGUI.BeginGroup(Loc.T("Where the gradient goes"), null, "mask");
            var sel = SelectedGradient;
            if (sel != null) GUILayout.Label(Loc.F("Selected: “{0}”. Its dots are shown on the texture.", sel.name), StudioGUI.CheckLabel);
            GUILayout.Label(_selection.Count > 0
                ? Loc.F("A new drag adds a gradient layer above the selected layer, limited to the {0} selected piece(s).", _selection.Count)
                : Loc.T("A new drag adds a gradient layer above the selected layer, over the whole texture. Select pieces in 3 · Limit to limit it."), StudioGUI.CheckLabel);
            if (sel != null && sel.maskMode == MaskMode.SelectedIslands)
                StudioGUI.CheckRow(StudioGUI.Check.Note, sel.invertMask ? Loc.F("“{0}” is limited to {1} piece{2} (inverted).", sel.name, sel.islands.Count, (sel.islands.Count == 1 ? "" : "s")) : Loc.F("“{0}” is limited to {1} piece{2}.", sel.name, sel.islands.Count, (sel.islands.Count == 1 ? "" : "s")));
            if (sel != null && sel.opacity <= 0.001f) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("The selected gradient is at 0% opacity, so it won't show."));
            GUILayout.Label(Loc.T("Gradients are drawn on the texture view (left), not on the model."), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }
    }
}
