using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class AvatarTextureWindow
    {
        Mesh _hlSelection, _hlHover;
        Material _hlMat;
        [SerializeField] bool _showSceneUvWire;
        Mesh _sceneUvWire;
        MeshUV _sceneOverlayFor;
        int _sceneOverlayPose, _sceneOverlayVersion, _wireVersion, _selectionVersion, _hoverVersion;
        bool[] _shownSceneParts;
        int _hoverIsland = -1;
        List<int> _scenePieceCandidates, _partCandidates;
        readonly HashSet<int> _scenePieceSet = new HashSet<int>();
        MeshUV _previewMesh, _partCandidateMesh, _pendingPieceMesh;
        bool _previewWandContiguous;
        float _previewWandTolerance;
        Renderer _partCandidateRenderer;
        SceneView _pendingPieceView;
        SelectMode _pendingPieceMode, _previewMode;
        MeshUV.SurfaceHit _pendingPieceHit;
        double _nextPiecePreview;
        const double ScenePreviewInterval = 1.0 / 15.0;
        bool _sceneHitValid;
        MeshUV.SurfaceHit _sceneHit;
        const int MaxSceneStrokeSegments = 8;
        int _lastStrokeIsland = -1;
        Vector2 _lastSceneStrokeMouse;
        bool _reportedStrokeCover;
        SceneView _sceneStrokeView;
        int _sceneStrokeControl;

        void DestroySceneResources()
        {
            if (_hlSelection != null) DestroyImmediate(_hlSelection);
            if (_hlHover != null) DestroyImmediate(_hlHover);
            if (_hlMat != null) DestroyImmediate(_hlMat);
            if (_sceneUvWire != null) DestroyImmediate(_sceneUvWire);
            _hlSelection = _hlHover = null;
            _hlMat = null;
            _sceneUvWire = null;
            _sceneOverlayFor = null;
            _shownSceneParts = null;
            ReleaseSceneSelectionCaches();
            _hoverFaces = null;
            _sceneHitValid = false;
            foreach (var pm in _pickMeshes.Values) pm?.ReleaseSurfaceMesh();
            _pickMeshes.Clear();
        }

        void ReleaseSceneSelectionCaches()
        {
            if (_marqueeActive && _marqueeInScene) CancelMarquee();
            else ClearScenePiecePreview();
            _tileCandidates = null;
            _tileCandidateMesh = null;
            _partCandidates = null;
            _partCandidateMesh = null;
            _partCandidateRenderer = null;
            _wandSource = null;
            _wandIds = null;
            _wandMesh = null;
            _wandMinimum = _wandOwners = _wandClose = null;
            _wandResult = null;
        }

        bool ScenePainting => CanvasPainting && _paintInScene && !GradientToolActive &&
            _comp != null && CurrentSlot != null && CurrentSlot.role != TextureRole.Normal;
        bool ScenePicking => PickingForPaint ? _paintInScene : _tool == Tool.Select && _pickInScene && _tab != Tab.Colour;
        bool ScenePartPicking => _recolourInScene && PartPicking;

        void BuildSelectionHighlight()
        {
            if (HighlightElements.Count == 0)
            {
                if (_hlSelection != null && _hlSelection.vertexCount != 0) _hlSelection.Clear();
                return;
            }
            if (_uv == null) return;
            if (_hlSelection == null) _hlSelection = new Mesh { name = "ATS selection", hideFlags = HideFlags.HideAndDontSave };
            UpdateSceneOverlayGeometry();
            bool faces = HighlightUsesFaces;
            var selected = new HashSet<int>(HighlightElements);
            _uv.BuildSceneHighlight(_hlSelection, selected.Contains, faces, _shownSceneParts);
            _selectionVersion = _sceneOverlayVersion;
        }

        void BuildHoverHighlight()
        {
            if (_uv == null) return;
            if (_hlHover == null) _hlHover = new Mesh { name = "ATS hover", hideFlags = HideFlags.HideAndDontSave };
            UpdateSceneOverlayGeometry();
            if (ScenePicking && FaceSelectionTool && _hoverFaces != null)
            {
                var faces = new HashSet<int>(_hoverFaces);
                _uv.BuildSceneHighlight(_hlHover, faces.Contains, true, _shownSceneParts);
            }
            else if (ScenePicking && _scenePieceCandidates != null && _previewMesh == _uv && _previewMode == _selectMode)
            {
                _uv.BuildSceneHighlight(_hlHover, _scenePieceSet.Contains, false, _shownSceneParts);
            }
            else
            {
                int hover = _hoverIsland;
                _uv.BuildSceneHighlight(_hlHover, i => i == hover, false, _shownSceneParts);
            }
            _hoverVersion = _sceneOverlayVersion;
        }

        List<int> ScenePartCandidates(MeshUV.SurfaceHit hit)
        {
            var renderer = _uv.PartOfTriangle(hit.triangle)?.renderer;
            if (_partCandidateMesh != _uv || _partCandidateRenderer != renderer || _partCandidates == null)
            {
                _partCandidates = IslandsOfRenderer(renderer);
                _partCandidateMesh = _uv;
                _partCandidateRenderer = renderer;
            }
            return _partCandidates;
        }

        void ClearScenePiecePreview()
        {
            EditorApplication.update -= RefreshScenePiecePreview;
            _pendingPieceView = null;
            _pendingPieceMesh = null;
            _scenePieceCandidates = null;
            _previewMesh = null;
            _scenePieceSet.Clear();
            _hoverIsland = -1;
            _nextPiecePreview = 0;
        }

        void QueueScenePiecePreview(SceneView view, MeshUV.SurfaceHit hit = default)
        {
            if (_pendingPieceView == null) EditorApplication.update += RefreshScenePiecePreview;
            _pendingPieceView = view;
            _pendingPieceMesh = _uv;
            _pendingPieceMode = _selectMode;
            _pendingPieceHit = hit;
            if (EditorApplication.timeSinceStartup >= _nextPiecePreview) RefreshScenePiecePreview();
        }

        void RefreshScenePiecePreview()
        {
            if (EditorApplication.timeSinceStartup < _nextPiecePreview) return;
            var view = _pendingPieceView;
            EditorApplication.update -= RefreshScenePiecePreview;
            _pendingPieceView = null;
            if (!view || !ScenePicking || _uv == null || _uv != _pendingPieceMesh || _pendingPieceMode != _selectMode ||
                _needsRebuild || SceneTargetOutsideCurrentStage) return;
            if (UpdateScenePiecePreview(view.camera, _pendingPieceHit)) view.Repaint();
            _nextPiecePreview = EditorApplication.timeSinceStartup + ScenePreviewInterval;
        }

        bool UpdateScenePiecePreview(Camera camera, MeshUV.SurfaceHit hit)
        {
            if (_selectMode == SelectMode.Wand && !WandMasksLayer)
                return SetScenePieceCandidates(WandCandidates(hit.uv, hit.island));
            if (_selectMode == SelectMode.Part)
                return SetScenePieceCandidates(ScenePartCandidates(hit));
            if (_selectMode == SelectMode.Tile)
                return SetScenePieceCandidates(TileCandidates(_uv.PieceTiles()[hit.island]));
            if (_marqueeActive && _marqueeInScene && _marqueeMoved && (_lasso == null || _lasso.Count >= 3))
                return SetScenePieceCandidates(SceneMarqueeCandidates(camera, out _));
            return false;
        }

        bool SetScenePieceCandidates(List<int> candidates)
        {
            bool changed = _previewMesh != _uv || _previewMode != _selectMode || (_hoverIsland >= 0) != (candidates?.Count > 0) ||
                !SameScenePieces(candidates, _scenePieceCandidates);
            _scenePieceCandidates = candidates;
            _previewMesh = _uv;
            _previewMode = _selectMode;
            _previewWandTolerance = _wandTolerance;
            _previewWandContiguous = _wandContiguous;
            if (!changed) return false;
            _scenePieceSet.Clear();
            if (candidates != null) _scenePieceSet.UnionWith(candidates);
            _hoverIsland = candidates?.Count > 0 ? candidates[0] : -1;
            if (_hoverIsland >= 0) BuildHoverHighlight();
            return true;
        }

        static bool SameScenePieces(List<int> a, List<int> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        internal bool SceneUvWireEnabled => _showSceneUvWire;

        internal void SetSceneUvWire(bool enabled)
        {
            if (_showSceneUvWire == enabled) return;
            Undo.RecordObject(this, "Toggle model UV wireframe");
            _showSceneUvWire = enabled;
            Repaint();
            SceneView.RepaintAll();
        }

        bool HaveSceneHighlightMaterial()
        {
            if (_hlMat == null)
            {
                var shader = Shader.Find("Hidden/Avistry/Highlight");
                if (shader == null) return false;
                _hlMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return true;
        }

        void UpdateSceneOverlayGeometry()
        {
            bool changed = _sceneOverlayFor != _uv || _sceneOverlayPose != _uv.PoseVersion;
            if (_shownSceneParts == null || _shownSceneParts.Length != _uv.parts.Count)
                _shownSceneParts = new bool[_uv.parts.Count];
            for (int i = 0; i < _uv.parts.Count; i++)
            {
                var renderer = _uv.parts[i].renderer;
                bool shown = renderer != null && IsDrawn(renderer);
                changed |= _shownSceneParts[i] != shown;
                _shownSceneParts[i] = shown;
            }
            if (!changed) return;
            _sceneOverlayFor = _uv;
            _sceneOverlayPose = _uv.PoseVersion;
            _sceneOverlayVersion++;
        }

        void UpdateSceneUvWireMesh()
        {
            UpdateSceneOverlayGeometry();
            if (_sceneUvWire != null && _wireVersion == _sceneOverlayVersion) return;
            if (_sceneUvWire == null)
                _sceneUvWire = new Mesh { name = "Avistry model UV wireframe", hideFlags = HideFlags.HideAndDontSave };
            _uv.BuildPosedWireframe(_sceneUvWire, _shownSceneParts);
            _wireVersion = _sceneOverlayVersion;
        }

        void DrawSceneUvWire()
        {
            if (!HaveSceneHighlightMaterial()) return;
            UpdateSceneUvWireMesh();
            if (_sceneUvWire.vertexCount == 0) return;
            _hlMat.color = new Color(1f, 1f, 1f, 0.44f);
            if (_hlMat.SetPass(0)) Graphics.DrawMeshNow(_sceneUvWire, Matrix4x4.identity);
        }

        bool SceneHasHover => !_needsRebuild && !ScenePiecePreviewStale && (ScenePicking && FaceSelectionTool && _hoverFaces != null ||
            _hoverIsland >= 0 && (ScenePartPicking || ScenePicking && !WandMasksLayer));

        bool ScenePiecePreviewStale => _scenePieceCandidates != null && (_previewMesh != _uv || !ScenePicking ||
            ScenePartPicking || _previewMode != _selectMode || SceneWandPreviewSettingsChanged);

        bool SceneWandPreviewSettingsChanged => _previewMode == SelectMode.Wand &&
            (_previewWandTolerance != _wandTolerance || _previewWandContiguous != _wandContiguous || WandMasksLayer);

        void DrawSceneHighlight()
        {
            if (!HaveSceneHighlightMaterial()) return;
            UpdateSceneOverlayGeometry();
            if (_highlightInScene && HighlightElements.Count > 0)
            {
                if (_selectionVersion != _sceneOverlayVersion) BuildSelectionHighlight();
                DrawHighlightMesh(_hlSelection, new Color(1f, 0.65f, 0.18f), !CanvasPainting && !PartPicking);
            }
            if (SceneHasHover)
            {
                if (_hoverVersion != _sceneOverlayVersion) BuildHoverHighlight();
                Color edge = new Color(0.18f, 0.84f, 1f);
                if (ScenePicking)
                {
                    var op = _marqueeActive ? _marqueeOp : SelectOpOf(Event.current);
                    if (op == SelectOp.Subtract) edge = new Color(1f, 0.34f, 0.43f);
                    else if (op == SelectOp.Add) edge = new Color(0.28f, 0.94f, 0.53f);
                }
                DrawHighlightMesh(_hlHover, edge, !PartPicking);
            }
        }

        static readonly int HighlightFill = Shader.PropertyToID("_Fill");
        static readonly int HighlightScale = Shader.PropertyToID("_PixelsPerPoint");

        void DrawHighlightMesh(Mesh m, Color colour, bool fill)
        {
            if (m == null || m.vertexCount == 0) return;
            _hlMat.color = colour;
            _hlMat.SetFloat(HighlightFill, fill ? 1f : 0f);
            _hlMat.SetFloat(HighlightScale, EditorGUIUtility.pixelsPerPoint);
            if (_hlMat.SetPass(2)) Graphics.DrawMeshNow(m, Matrix4x4.identity, 0);
            if (_hlMat.SetPass(1)) Graphics.DrawMeshNow(m, Matrix4x4.identity, 0);
        }

        void OnSceneGUI(SceneView sv)
        {
            if (Event.current.type != EventType.Repaint && ScenePiecePreviewStale)
            {
                ClearScenePiecePreview();
                if (ScenePicking && _selectMode == SelectMode.Wand && !WandMasksLayer && !_needsRebuild &&
                    mouseOverWindow == sv && _uv != null && !SceneTargetOutsideCurrentStage &&
                    RaycastVisible(HandleUtility.GUIPointToWorldRay(Event.current.mousePosition), out var hit, out _))
                    QueueScenePiecePreview(sv, hit);
            }
            if (_sceneMarqueeControl != 0 && (!_sceneMarqueeView || (_sceneMarqueeView == sv &&
                (!ScenePicking || ScenePartPicking || SceneGradientActive || _matchPick != MatchPick.None || _scenePickArmed ||
                GUIUtility.hotControl != _sceneMarqueeControl || Event.current.type == EventType.Ignore))))
                CancelMarquee();
            if (SceneTargetOutsideCurrentStage)
            {
                DiscardOffStageSceneInput(sv);
                if (!_scenePickArmed)
                {
                    if (ScenePainting || SceneGradientActive || ScenePicking || ScenePartPicking)
                        DrawSceneOverlay(Loc.T("Pick a part you can see in the Scene view."));
                    return;
                }
            }
            if (!SceneGradientActive || _matchPick != MatchPick.None || _scenePickArmed) CancelSceneGradient();
            ReleaseSceneGradientControl(sv);
            CheckSceneStrokeCapture(sv);
            if (_matchPick != MatchPick.None) { HandleMatchPick(sv, Event.current); return; }
            if (_scenePickArmed) { HandleSceneColourPick(sv, Event.current); return; }
            if (_renderer == null || _uv == null) return;
            var e = Event.current;
            if (!PartPicking && (ScenePainting || SceneGradientActive || PickingForPaint && _paintInScene) &&
                CanUseShortcuts(e) && HandlePaintToolShortcut(e, sv))
                return;

            if (e.type == EventType.Repaint)
            {
                bool hasHover = SceneHasHover;
                bool wantHighlight = (_highlightInScene && HighlightElements.Count > 0) || ((ScenePicking || ScenePartPicking) && hasHover);
                bool wantMirror = SymmetryActive && CurrentSymmetry != null;
                if ((wantHighlight || wantMirror || _showSceneUvWire) && _uv.PoseChanged())
                    _uv.RefreshPose();
                if (_showSceneUvWire) DrawSceneUvWire();
                if (wantHighlight) DrawSceneHighlight();
                bool paintCursor = ScenePainting && _sceneHitValid && !PartPicking &&
                    (_liveStroke != null || !ScenePaintColourPick(e));
                if (paintCursor)
                {
                    if (_brush.fillTool) DrawSceneFillMarker(_sceneHit.point,
                        BasicFillCursorProblem(_sceneHit.uv) == null ? Color.white : StudioGUI.Danger);
                    else DrawSceneBrush();
                }
                DrawSceneSymmetry(paintCursor);
                RefreshSceneGradientPose();
            }

            if (ScenePartPicking) HandleScenePartPick(sv, e);
            else if (SceneGradientActive) HandleSceneGradient(sv, e);
            else if (ScenePainting) HandleScenePaint(sv, e);
            else if (ScenePicking) HandleScenePick(sv, e);
        }

        void ReleaseSceneStrokeControl(SceneView view)
        {
            if (_sceneStrokeView != view) return;
            ClearSceneStrokeControl();
        }

        void ClearSceneStrokeControl()
        {
            if (_sceneStrokeControl != 0 && GUIUtility.hotControl == _sceneStrokeControl) GUIUtility.hotControl = 0;
            _sceneStrokeView = null;
            _sceneStrokeControl = 0;
            _lastStrokeIsland = -1;
        }

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
                picked = texel.colour;
                from = texel.onTarget ? Loc.F("texture colour on {0}", _renderer.name) : Loc.F("texture colour of {0}", texel.material.name);
            }
            else
            {
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
            RegisterPickedColour(picked);
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
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            try
            {
                const float left = 48f;
                float sceneWidth = SceneView.currentDrawingSceneView?.position.width ?? EditorGUIUtility.currentViewWidth;
                float available = sceneWidth - left - Mathf.Min(88f, sceneWidth * 0.25f);
                if (available < 48f) return;

                var content = new GUIContent(text);
                var style = StudioGUI.Overlay;
                float width = Mathf.Min(style.CalcSize(content).x, available);
                var r = new Rect(left, 10f, width, style.CalcHeight(content, width));
                EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.6f));
                GUI.Label(r, content, style);
            }
            finally { Handles.EndGUI(); }
        }

        void HandleScenePick(SceneView sv, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.Repaint)
                DrawSceneOverlay(Loc.F("<b>Avistry · {0}</b>   {1}", SelectModeNames[(int)_selectMode], SceneTopologyHint(sv)));
            if (_sceneMarqueeControl != 0 && _sceneMarqueeView != sv) return;
            DrawMarqueeScene();
            bool shape = _selectMode == SelectMode.Box || _selectMode == SelectMode.Lasso;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                {
                    if (shape || WandMasksLayer) break;
                    bool seen = RaycastVisible(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hit, out _);
                    if (FaceSelectionTool)
                    {
                        SetSceneFaceHover(seen ? SceneFacesAt(hit) : null, sv);
                        break;
                    }
                    if (_selectMode == SelectMode.Part || _selectMode == SelectMode.Tile || _selectMode == SelectMode.Wand)
                    {
                        if (seen) QueueScenePiecePreview(sv, hit);
                        else if (_hoverIsland >= 0 || _pendingPieceView != null)
                        {
                            ClearScenePiecePreview();
                            sv.Repaint();
                        }
                        break;
                    }
                    if (_scenePieceCandidates != null || _pendingPieceView != null) ClearScenePiecePreview();
                    int island = seen ? hit.island : -1;
                    if (island != _hoverIsland)
                    {
                        _hoverIsland = island;
                        if (island >= 0) BuildHoverHighlight();
                        sv.Repaint();
                    }
                    break;
                }
                case EventType.MouseLeaveWindow:
                    if (_hoverFaces != null) SetSceneFaceHover(null, sv);
                    if (!_marqueeActive && (_hoverIsland >= 0 || _pendingPieceView != null))
                    {
                        ClearScenePiecePreview();
                        sv.Repaint();
                    }
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt:
                {
                    ClearSceneTopologyFeedback();
                    var op = SelectOpOf(e);
                    if (shape)
                    {
                        ClearScenePiecePreview();
                        BeginMarquee(e.mousePosition, e.mousePosition, true, op);
                        _sceneMarqueeView = sv;
                        _sceneMarqueeControl = id;
                        GUIUtility.hotControl = id;
                    }
                    else if (_selectMode == SelectMode.Click) ScenePickClick(HandleUtility.GUIPointToWorldRay(e.mousePosition), op == SelectOp.Add, op == SelectOp.Subtract);
                    else SceneSelectClick(HandleUtility.GUIPointToWorldRay(e.mousePosition), _selectMode, op, sv);
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when GUIUtility.hotControl == id && _marqueeActive && _marqueeInScene:
                    if (UpdateMarquee(e.mousePosition, e.mousePosition) && _marqueeMoved) QueueScenePiecePreview(sv);
                    e.Use();
                    sv.Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id && e.button == 0 && _marqueeActive && _marqueeInScene:
                    UpdateMarquee(e.mousePosition, e.mousePosition);
                    EndMarqueeScene(sv.camera);
                    e.Use();
                    sv.Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && _marqueeActive && _marqueeInScene:
                    CancelMarquee();
                    e.Use();
                    sv.Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && PickingForPaint:
                    SetPickForPaint(false);
                    e.Use();
                    break;
            }
        }

        const float SceneStrokeTraceStep = 6f;

        int TraceSceneStroke(Vector2 next, Span<MeshUV.SurfaceHit> samples, bool reuseStrokeBatch, out Renderer coveredBy)
        {
            coveredBy = null;
            if (_lastStrokeIsland < 0) return -1;

            int steps = Mathf.Min(MaxSceneStrokeSegments, Mathf.CeilToInt(Vector2.Distance(_lastSceneStrokeMouse, next) / SceneStrokeTraceStep));
            int count = 0;
            for (int i = 1; i < steps; i++)
            {
                var ray = HandleUtility.GUIPointToWorldRay(Vector2.Lerp(_lastSceneStrokeMouse, next, i / (float)steps));
                if (!RaycastVisibleForStroke(ray, out var hit, out coveredBy, reuseStrokeBatch)) return -1;
                samples[count++] = hit;
            }
            return count;
        }

        void ContinueSceneStroke(Vector2 mouse, float pressure, MeshUV.SurfaceHit end, bool reuseStrokeBatch, out Renderer coveredBy)
        {
            Span<MeshUV.SurfaceHit> samples = stackalloc MeshUV.SurfaceHit[MaxSceneStrokeSegments - 1];
            int count = TraceSceneStroke(mouse, samples, reuseStrokeBatch, out coveredBy);
            int previousIsland = _lastStrokeIsland;
            if (count > 0)
            {
                float previousPressure = _liveStroke.points[_liveStroke.points.Count - 1].z;
                for (int i = 0; i < count; i++)
                {
                    var hit = samples[i];
                    float samplePressure = Mathf.Lerp(previousPressure, pressure, (i + 1f) / (count + 1f));
                    AddStrokePoint(hit.uv, samplePressure, previousIsland == hit.island, hit.island, hit, true);
                    previousIsland = hit.island;
                }
            }

            bool connected = count >= 0;
            AddStrokePoint(end.uv, pressure, connected && previousIsland == end.island, end.island, end, connected);
            _lastStrokeIsland = end.island;
            _lastSceneStrokeMouse = mouse;
        }

        void ScenePickClick(Ray ray, bool add, bool remove)
        {
            bool seen = RaycastVisibleWithUvStatus(ray, out var hit, out var covered, out var changedUv);
            if (changedUv) { Toast(IncludedUvChangedHint, MessageType.Info, 4f); return; }
            if (covered != null) { Toast(CoveredMessage(covered), MessageType.Info, 3f); return; }
            ApplySelection(seen ? new List<int> { hit.island } : new List<int>(), add, remove);
        }

        bool ScenePaintColourPick(Event e) => _eyedropperArmed || e.control || e.command;
        static string ScenePaintColourPickHint => Loc.T("Click the edited part to pick a colour.");

        void DrawScenePaintColourPickHint(SceneView scene)
        {
            string hint = ScenePaintColourPickHint;
            if (_eyedropperArmed) hint += " " + Loc.T("Press I or Esc to cancel.");
            DrawSceneOverlay(Loc.F("<b>Avistry · {0}</b>   {1}", Loc.T("Eyedropper"), hint));
            EditorGUIUtility.AddCursorRect(new Rect(0, 0, scene.position.width, scene.position.height), MouseCursor.ArrowPlus);
        }

        static void RepaintSceneOnColourPickModifier(SceneView scene, Event e)
        {
            if ((e.type is EventType.KeyDown or EventType.KeyUp) &&
                (e.keyCode is KeyCode.LeftControl or KeyCode.RightControl or KeyCode.LeftCommand or KeyCode.RightCommand))
                scene.Repaint();
        }

        void HandleScenePaint(SceneView sv, Event e)
        {
            if (_sceneStrokeControl != 0 && _sceneStrokeView != sv) return;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.Repaint)
            {
                if (_liveStroke == null && ScenePaintColourPick(e))
                    DrawScenePaintColourPickHint(sv);
                else if (_brush.fillTool)
                    DrawSceneOverlay(SceneFillHint());
                else
                {
                    bool smudge = BlendToolActive && _brush.blendKind == StrokeTool.Smudge;
                    string paintHead = BlendToolActive ? (smudge ? Loc.T("Smudge") : Loc.T("Blur"))
                        : MaskEditing ? (_brush.mode == BrushMode.Erase ? Loc.T("Hide") : Loc.T("Show"))
                        : _brush.mode == BrushMode.Erase ? Loc.T("Erase") : Loc.T("Paint");
                    DrawSceneOverlay(_brush.surfaceBrush && _brush.shape.SurfaceCapable && !smudge
                        ? Loc.F("<b>Avistry · {0}</b>   drag on the model (smooth across joins) · I or Ctrl+click pick colour · [ ] size ({1:0}px) · Esc stop", paintHead, _brush.size)
                        : Loc.F("<b>Avistry · {0}</b>   drag on the model · I or Ctrl+click pick colour · [ ] size ({1:0}px) · Esc stop", paintHead, _brush.size));
                }
            }

            RepaintSceneOnColourPickModifier(sv, e);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseMove:
                    _sceneHitValid = RaycastVisibleWithUvStatus(HandleUtility.GUIPointToWorldRay(e.mousePosition),
                        out _sceneHit, out _, out _, respectEditedCulling: true);
                    sv.Repaint();
                    break;

                case EventType.MouseLeaveWindow:
                    _sceneHitValid = false;
                    _lastStrokeIsland = -1;
                    sv.Repaint();
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt && GUIUtility.hotControl == 0:
                {
                    bool pickingColour = ScenePaintColourPick(e);
                    bool filling = _brush.fillTool && !pickingColour;
                    var pointer = BrushPointer(e, start: true);
                    var ray = HandleUtility.GUIPointToWorldRay(pointer.position);
                    _sceneHitValid = RaycastVisibleWithUvStatus(ray, out _sceneHit, out var coveredBy, out bool changedUv,
                        respectEditedCulling: true);
                    if (!_sceneHitValid)
                    {
                        if (coveredBy != null) ReportBlockedScenePaint(sv, coveredBy);
                        else
                        {
                            var hint = changedUv ? IncludedUvChangedHint : pickingColour ? ScenePaintColourPickHint : filling ? SceneFillClickHint
                                : Loc.T("Click a visible piece on the part you are editing.");
                            if (filling && !changedUv) Toast(hint, MessageType.Info, 4f, Msg.BtnUseBrush, () => SetPaintTool(0));
                            else Toast(hint, MessageType.Info, 3f);
                            sv.ShowNotification(new GUIContent(hint), 3.0);
                        }
                        if (filling || coveredBy != null || changedUv) e.Use();
                        sv.Repaint();
                        break;
                    }
                    if (pickingColour) PickColour(_sceneHit.uv);
                    else
                    {
                        var problem = BeginBrushStroke(_sceneHit.uv, pointer.pressure, _sceneHit.island, _sceneHit, fromScene: true);
                        if (problem != null) sv.ShowNotification(new GUIContent(problem), 3.0);
                        if (_liveStroke != null)
                        {
                            _lastStrokeIsland = _sceneHit.island;
                            _lastSceneStrokeMouse = pointer.position;
                            _reportedStrokeCover = false;
                            _sceneStrokeView = sv;
                            _sceneStrokeControl = id;
                            GUIUtility.hotControl = id;
                        }
                    }
                    e.Use();
                    sv.Repaint();
                    break;
                }

                case EventType.MouseDrag when _sceneStrokeView == sv && GUIUtility.hotControl == id && _liveStroke != null:
                {
                    var pointer = BrushPointer(e, start: false);
                    var ray = HandleUtility.GUIPointToWorldRay(pointer.position);
                    bool trace = _lastStrokeIsland >= 0 && (pointer.position - _lastSceneStrokeMouse).sqrMagnitude > SceneStrokeTraceStep * SceneStrokeTraceStep;
                    bool reuseStrokeBatch = trace && BeginStrokeRayBatch();
                    Renderer coveredBy;
                    try
                    {
                        _sceneHitValid = RaycastVisibleForStroke(ray, out _sceneHit, out coveredBy, reuseStrokeBatch);
                        if (_sceneHitValid) ContinueSceneStroke(pointer.position, pointer.pressure, _sceneHit, reuseStrokeBatch, out coveredBy);
                        else _lastStrokeIsland = -1;
                    }
                    finally { if (reuseStrokeBatch) EndStrokeRayBatch(); }
                    if (coveredBy != null && !_reportedStrokeCover)
                    {
                        _reportedStrokeCover = true;
                        sv.ShowNotification(new GUIContent(CoveredMessage(coveredBy)), 3.0);
                    }
                    e.Use();
                    sv.Repaint();
                    break;
                }

                case EventType.MouseUp when _sceneStrokeView == sv && GUIUtility.hotControl == id && e.button == 0:
                    EndStroke();
                    ReleaseSceneStrokeControl(sv);
                    e.Use();
                    break;

                case EventType.KeyDown when CanUseShortcuts(e):
                    if (e.keyCode == KeyCode.LeftBracket) { ResizeBrush(1f / 1.2f); e.Use(); }
                    else if (e.keyCode == KeyCode.RightBracket) { ResizeBrush(1.2f); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape)
                    {
                        if (_eyedropperArmed && _liveStroke == null)
                        {
                            _eyedropperArmed = false;
                            Repaint();
                        }
                        else
                        {
                            SetTool(Tool.Select);
                            ReleaseSceneStrokeControl(sv);
                        }
                        e.Use();
                        sv.Repaint();
                    }
                    else if (e.keyCode == KeyCode.I && _liveStroke == null)
                    {
                        _eyedropperArmed = !_eyedropperArmed;
                        e.Use();
                        Repaint();
                        sv.Repaint();
                    }
                    break;
            }
        }

        float SceneBrushWorldRadius()
        {
            if (_sceneHit.uvPerWorld <= 0 || _comp == null) return 0f;
            if (_liveStroke?.IsSurface == true) return _liveStroke.surfRadius * _uv.PosedSize;
            float uvRadius = _brush.size * 0.5f / Mathf.Max(1, _comp.SourceWidth);
            return Mathf.Min(uvRadius / _sceneHit.uvPerWorld, 2f);
        }

        void DrawSceneBrush()
        {
            float worldR = SceneBrushWorldRadius();
            if (worldR <= 0f) return;
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
