using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    // Scene view: selection highlight, island picking (Select tool) and painting on the avatar (Paint tool).
    public partial class AvatarTextureWindow
    {
        Mesh _hlSelection, _hlHover;
        Material _hlMat;
        int _hoverIsland = -1;
        bool _sceneHitValid;
        MeshUV.SurfaceHit _sceneHit;
        int _lastStrokeIsland = -1;

        void DestroySceneResources()
        {
            if (_hlSelection != null) DestroyImmediate(_hlSelection);
            if (_hlHover != null) DestroyImmediate(_hlHover);
            if (_hlMat != null) DestroyImmediate(_hlMat);
            _hlSelection = _hlHover = null;
            _hlMat = null;
            _hoverIsland = -1;
            _sceneHitValid = false;
            foreach (var pm in _pickMeshes.Values) pm?.ReleaseSurfaceMesh();
            _pickMeshes.Clear();
        }

        bool ScenePainting => _tool == Tool.Paint && _paintInScene && !GradientToolActive && _comp != null && CurrentSlot != null && CurrentSlot.role != TextureRole.Normal;
        bool ScenePicking => _tool == Tool.Select && _pickInScene;

        void BuildSelectionHighlight()
        {
            if (_uv == null) return;
            if (_hlSelection == null) _hlSelection = new Mesh { name = "ATS selection", hideFlags = HideFlags.HideAndDontSave };
            var set = new HashSet<int>(_selection);
            _uv.BuildHighlight(_hlSelection, set.Contains);
        }

        void BuildHoverHighlight()
        {
            if (_uv == null) return;
            if (_hlHover == null) _hlHover = new Mesh { name = "ATS hover", hideFlags = HideFlags.HideAndDontSave };
            int hover = _hoverIsland;
            _uv.BuildHighlight(_hlHover, i => i == hover);
        }

        void DrawSceneHighlight()
        {
            if (_hlMat == null)
            {
                var shader = Shader.Find("Hidden/Avistry/Highlight");
                if (shader == null) return;
                _hlMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_highlightInScene && _selection.Count > 0)
            {
                // While painting, outline only, so the fill doesn't hide the paint.
                float fill = _tool == Tool.Paint || PartPicking ? 0f : 0.38f; // By part: outline only, so the colour shows
                DrawHighlightMesh(_hlSelection, new Color(1f, 0.55f, 0.1f, fill), new Color(1f, 0.62f, 0.15f, 1f));
            }
            if ((ScenePicking || PartPicking) && _hoverIsland >= 0)
                DrawHighlightMesh(_hlHover, new Color(1f, 1f, 1f, 0.15f), new Color(1f, 1f, 1f, 0.85f));
        }

        void DrawHighlightMesh(Mesh m, Color fill, Color outline)
        {
            if (m == null || m.vertexCount == 0) return;
            var id = Matrix4x4.identity;
            if (fill.a > 0)
            {
                _hlMat.color = new Color(fill.r, fill.g, fill.b, fill.a * 0.35f); // parts hidden behind other geometry
                _hlMat.SetPass(1);
                Graphics.DrawMeshNow(m, id, 0);
                _hlMat.color = fill;
                _hlMat.SetPass(0);
                Graphics.DrawMeshNow(m, id, 0);
            }
            _hlMat.color = outline;
            _hlMat.SetPass(0);
            Graphics.DrawMeshNow(m, id, 1);
        }

        void OnSceneGUI(SceneView sv)
        {
            if (_matchPick != MatchPick.None) { HandleMatchPick(sv, Event.current); return; }
            if (_scenePickArmed) { HandleSceneColourPick(sv, Event.current); return; }
            if (_renderer == null || _uv == null) return;
            var e = Event.current;

            if (e.type == EventType.Repaint)
            {
                bool wantHighlight = (_highlightInScene && _selection.Count > 0) || ((ScenePicking || PartPicking) && _hoverIsland >= 0);
                if (wantHighlight && _uv.PoseChanged())
                {
                    // The avatar moved: re-pose the cached geometry and rebuild the highlight meshes.
                    _uv.RefreshPose();
                    BuildSelectionHighlight();
                    if (_hoverIsland >= 0) BuildHoverHighlight();
                }
                if (wantHighlight) DrawSceneHighlight();
                if (ScenePainting && _sceneHitValid && !PartPicking) DrawSceneBrush();
                DrawSceneSymmetry(ScenePainting && _sceneHitValid);
            }

            if (PartPicking) HandleScenePartPick(sv, e); // 2 · Colour › By part: clicks pick the parts
            else if (ScenePainting) HandleScenePaint(sv, e);
            else if (ScenePicking) HandleScenePick(sv, e);
        }

        /// <summary>
        /// Scene colour picker: one click picks a colour from anything in the Scene view. On the mesh being edited it reads
        /// the exact (unlit) texture colour at that point; elsewhere, or with Shift, the colour as shown on screen.
        /// </summary>
        void HandleSceneColourPick(SceneView sv, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            DrawSceneOverlay(Loc.T("<b>Avistry · Pick colour</b>   click anything · Shift = colour as seen on screen · Esc cancel"));
            EditorGUIUtility.AddCursorRect(new Rect(0, 0, sv.position.width, sv.position.height), MouseCursor.ArrowPlus);

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _scenePickArmed = false;
                e.Use();
                Repaint();
                return;
            }
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;

            Color picked;
            string from;
            if (!e.shift && PickSceneTexel(e, false, false, out var texel) && !texel.fromScreen)
            {
                // The real texture colour, on this mesh (as edited) or on any other part of the avatar.
                picked = texel.colour;
                from = texel.onTarget ? Loc.F("texture colour on {0}", _renderer.name) : Loc.F("texture colour of {0}", texel.material.name);
            }
            else
            {
                // What's on screen (lighting, shading and post effects included).
                var screen = GUIUtility.GUIToScreenPoint(e.mousePosition) * EditorGUIUtility.pixelsPerPoint;
                var px = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(screen, 1, 1);
                picked = px != null && px.Length > 0 ? px[0] : Color.black;
                picked.a = 1;
                from = Loc.T("on-screen colour");
            }

            Undo.RecordObject(this, "Pick colour");
            _brush.color = picked;
            _brush.mode = BrushMode.Paint;
            _scenePickArmed = false;
            e.Use();
            Toast(Loc.F("Picked #{0} ({1}).", ColorUtility.ToHtmlStringRGB(picked), from), MessageType.Info, 3f);
            Repaint();
            sv.Repaint();
        }

        bool TryRaycastCurrentMesh(Event e, out MeshUV.SurfaceHit hit)
        {
            hit = default;
            if (_uv == null) return false;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            return _uv.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out hit);
        }

        void DrawSceneOverlay(string text)
        {
            Handles.BeginGUI();
            var content = new GUIContent(text);
            var size = StudioGUI.Overlay.CalcSize(content);
            var r = new Rect(10, 10, size.x, size.y);
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.6f));
            GUI.Label(r, content, StudioGUI.Overlay);
            Handles.EndGUI();
        }

        void HandleScenePick(SceneView sv, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            DrawSceneOverlay(Loc.F("<b>Avistry · {0}</b>   {1}", SelectModeNames[(int)_selectMode], SelectModeHint()));
            DrawMarqueeScene();
            bool shape = _selectMode == SelectMode.Box || _selectMode == SelectMode.Lasso;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                {
                    if (shape) break; // no piece hover while a Box or Lasso is the tool
                    if (_uv.PoseChanged()) _uv.RefreshPose();
                    int island = _uv.PickIslandWorld(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                    if (island != _hoverIsland)
                    {
                        _hoverIsland = island;
                        if (island >= 0) BuildHoverHighlight();
                        sv.Repaint();
                    }
                    break;
                }
                case EventType.MouseLeaveWindow:
                    if (_hoverIsland >= 0) { _hoverIsland = -1; sv.Repaint(); }
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt: // Alt stays orbit
                {
                    var op = SelectOpOf(e);
                    if (shape)
                    {
                        _hoverIsland = -1;
                        BeginMarquee(e.mousePosition, e.mousePosition, true, op);
                        GUIUtility.hotControl = id;
                    }
                    else if (_selectMode == SelectMode.Click) ScenePickClick(HandleUtility.GUIPointToWorldRay(e.mousePosition), op == SelectOp.Add, op == SelectOp.Subtract);
                    else SceneSelectClick(HandleUtility.GUIPointToWorldRay(e.mousePosition), _selectMode, op);
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when GUIUtility.hotControl == id && _marqueeActive && _marqueeInScene:
                    UpdateMarquee(e.mousePosition, e.mousePosition);
                    e.Use();
                    sv.Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id && e.button == 0 && _marqueeActive && _marqueeInScene:
                    EndMarqueeScene(sv);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    sv.Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && _marqueeActive && _marqueeInScene:
                    CancelMarquee();
                    e.Use();
                    sv.Repaint();
                    break;
            }
        }

        bool SceneRaycast(Event e, out MeshUV.SurfaceHit hit) =>
            _uv.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out hit);

        /// <summary>A click in "Click the model to select pieces": picks the piece you can see, or says what covers it.</summary>
        void ScenePickClick(Ray ray, bool add, bool remove)
        {
            bool seen = RaycastVisible(ray, out var hit, out var covered);
            if (covered != null) { Toast(CoveredMessage(covered), MessageType.Info, 3f); return; }
            ApplySelection(seen ? new List<int> { hit.island } : new List<int>(), add, remove);
        }

        void HandleScenePaint(SceneView sv, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            string paintHead = _brush.mode == BrushMode.Erase ? Loc.T("Erase") : Loc.T("Paint");
            DrawSceneOverlay(_brush.surfaceBrush && _brush.shape.SurfaceCapable && !_brush.fillTool
                ? Loc.F("<b>Avistry · {0}</b>   drag on the model (smooth across joins) · I or Ctrl+click pick colour · [ ] size ({1:0}px) · Esc stop", paintHead, _brush.size)
                : Loc.F("<b>Avistry · {0}</b>   drag on the model · I or Ctrl+click pick colour · [ ] size ({1:0}px) · Esc stop", paintHead, _brush.size));

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                    if (_uv.PoseChanged()) _uv.RefreshPose();
                    _sceneHitValid = SceneRaycast(e, out _sceneHit);
                    sv.Repaint();
                    break;

                case EventType.MouseLeaveWindow:
                    _sceneHitValid = false;
                    sv.Repaint();
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt:
                {
                    if (_uv.PoseChanged()) _uv.RefreshPose();
                    _sceneHitValid = SceneRaycast(e, out _sceneHit);
                    if (!_sceneHitValid)
                    {
                        Toast(Loc.T("Click on the model itself to paint. (Alt+drag or right-drag moves the Scene camera.)"), MessageType.Info, 3f);
                        break;
                    }
                    if (e.control || e.command || _eyedropperArmed) PickColour(_sceneHit.uv);
                    else
                    {
                        BeginStroke(_sceneHit.uv, Pressure(e), _sceneHit.island, _sceneHit);
                        _lastStrokeIsland = _sceneHit.island;
                        GUIUtility.hotControl = id;
                    }
                    e.Use();
                    break;
                }

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                {
                    _sceneHitValid = SceneRaycast(e, out _sceneHit);
                    if (_sceneHitValid)
                    {
                        // Only join points on the same island: crossing a UV seam would otherwise
                        // drag a line of paint across the texture between two islands.
                        bool joined = _lastStrokeIsland == _sceneHit.island;
                        AddStrokePoint(_sceneHit.uv, Pressure(e), joined, _sceneHit.island, _sceneHit, _lastStrokeIsland >= 0);
                        _lastStrokeIsland = _sceneHit.island;
                    }
                    else _lastStrokeIsland = -1;
                    e.Use();
                    break;
                }

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    EndStroke();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.LeftBracket) { ResizeBrush(1f / 1.2f); e.Use(); }
                    else if (e.keyCode == KeyCode.RightBracket) { ResizeBrush(1.2f); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape) { SetTool(Tool.Select); e.Use(); }
                    else if (e.keyCode == KeyCode.I)
                    {
                        _eyedropperArmed = !_eyedropperArmed;
                        if (_eyedropperArmed) Toast(Loc.T("Eyedropper: click the texture or the model to pick a colour."), MessageType.Info, 3f);
                        e.Use();
                        Repaint();
                    }
                    break;
            }
        }

        void DrawSceneBrush()
        {
            if (_sceneHit.uvPerWorld <= 0 || _comp == null) return;
            float uvRadius = _brush.size * 0.5f / Mathf.Max(1, _comp.SourceWidth);
            float worldR = Mathf.Min(uvRadius / _sceneHit.uvPerWorld, 2f);
            var p = _sceneHit.point + _sceneHit.normal * worldR * 0.02f;
            var col = _brush.mode == BrushMode.Erase ? new Color(1f, 0.55f, 0.55f) : _brush.color;
            using (new Handles.DrawingScope())
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                Handles.color = new Color(col.r, col.g, col.b, 0.18f);
                Handles.DrawSolidDisc(p, _sceneHit.normal, worldR);
                Handles.color = new Color(0, 0, 0, 0.6f);
                Handles.DrawWireDisc(p, _sceneHit.normal, worldR * 1.04f);
                Handles.color = Color.white;
                Handles.DrawWireDisc(p, _sceneHit.normal, worldR);
                if (_brush.shape.hardness < 0.95f)
                {
                    Handles.color = new Color(1, 1, 1, 0.35f);
                    Handles.DrawWireDisc(p, _sceneHit.normal, worldR * Mathf.Max(0.05f, _brush.shape.hardness));
                }
            }
        }
    }
}
