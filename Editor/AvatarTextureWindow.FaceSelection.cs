using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] List<int> _selectedFaces = new List<int>();
        [SerializeField] string _selectedFaceLayout;
        IReadOnlyList<int> _hoverFaces;
        SceneView _sceneTopologyFeedbackView;
        SelectMode _sceneTopologyFeedbackMode;

        bool VertexEdgeSelectionTool => _selectMode == SelectMode.Vertex || _selectMode == SelectMode.Edge;
        bool FaceSelectionTool => _selectMode == SelectMode.Face || _selectMode == SelectMode.BoundaryLoop ||
            _selectMode == SelectMode.FaceLoop || VertexEdgeSelectionTool;
        bool HasFaceSelection => _uv != null && _selectedFaces.Count > 0 && _selectedFaceLayout == _uv.FaceLayout;
        int SelectedElementCount => HasFaceSelection ? _selectedFaces.Count : _selection.Count;

        string CanvasSelectionStatus()
        {
            string mode = SelectModeNames[(int)_selectMode];
            int count = SelectedElementCount;
            if (!FaceSelectionTool) return Loc.F("{0}: {1} selected", mode, count);
            bool faces = HasFaceSelection || _selection.Count == 0;
            return mode + ": " + Loc.F(faces ? "{0} face{1} selected" : "{0} piece{1} selected", count, count == 1 ? "" : "s");
        }

        string CanvasSelectHint() => _selectMode switch
        {
            SelectMode.Vertex => Loc.T("Click near a vertex"),
            SelectMode.Edge or SelectMode.FaceLoop or SelectMode.BoundaryLoop => Loc.T("Click near an edge"),
            _ => SelectModeHint().Replace(". ", "  ·  ").TrimEnd('.'),
        };

        bool IsCurrentFace(int triangle) => _uv != null && triangle >= 0 && triangle < _uv.TriangleCount;
        bool HasCurrentFaceLimit(Layer layer) => _uv != null && layer.faceLayout == _uv.FaceLayout &&
            layer.faces != null && layer.faces.Any(IsCurrentFace);

        bool ActiveFaceLimitNeedsUpdate(Layer layer) => layer?.maskMode == MaskMode.SelectedFaces &&
            (MaskEditing || !HasFaceSelection && layer is PaintLayer) && !HasCurrentFaceLimit(layer);

        void ClearFaceSelection()
        {
            _selectedFaces.Clear();
            _selectedFaceLayout = null;
            _hoverFaces = null;
            ClearSceneTopologyFeedback();
        }

        void ApplyFaceSelection(IEnumerable<int> faces, SelectOp op)
        {
            if (_uv == null) return;
            if (op == SelectOp.Subtract && !HasFaceSelection) return;
            var next = op == SelectOp.Replace || !HasFaceSelection
                ? new HashSet<int>() : new HashSet<int>(_selectedFaces);
            if (op == SelectOp.Subtract) next.ExceptWith(faces);
            else next.UnionWith(faces.Where(IsCurrentFace));
            if (op != SelectOp.Replace && HasFaceSelection && next.SetEquals(_selectedFaces)) return;
            Undo.RecordObject(this, "Select UV faces");
            _selection.Clear();
            _selectedFaces = next.OrderBy(i => i).ToList();
            _selectedFaceLayout = _uv.FaceLayout;
            RebuildSelectionVisuals();
            Repaint();
        }

        bool CaptureFaceSelection(Layer layer)
        {
            if (!HasFaceSelection) return false;
            layer.maskMode = MaskMode.SelectedFaces;
            layer.faceLayout = _selectedFaceLayout;
            layer.faces = new List<int>(_selectedFaces);
            layer.invertMask = false;
            return true;
        }

        bool MatchesFaceSelection(Layer layer) => !HasFaceSelection || (layer.maskMode == MaskMode.SelectedFaces &&
            !layer.invertMask && layer.faceLayout == _selectedFaceLayout && layer.faces != null && _selectedFaces.SequenceEqual(layer.faces));

        List<int> FocusFaces()
        {
            if (!CanvasPainting || !_focusWhilePainting || _uv == null || _comp == null) return null;
            if (HasFaceSelection) return _selectedFaces;
            if (ActiveLayer is PaintLayer layer && layer.maskMode == MaskMode.SelectedFaces && !layer.invertMask &&
                HasCurrentFaceLimit(layer)) return layer.faces;
            return null;
        }

        IReadOnlyList<int> SceneFacesAt(MeshUV.SurfaceHit hit)
        {
            if (VertexEdgeSelectionTool) return PickSceneTopology(hit).faces ?? Array.Empty<int>();
            if (_selectMode == SelectMode.FaceLoop)
            {
                FindSceneFaceLoop(hit, out var loop);
                return loop;
            }
            if (_selectMode == SelectMode.BoundaryLoop)
            {
                FindSceneBoundaryLoop(hit, out var loop);
                return loop;
            }
            return new[] { hit.triangle };
        }

        void SetSceneFaceHover(IReadOnlyList<int> faces, SceneView view)
        {
            if (faces?.Count == 0) faces = null;
            if ((_hoverFaces == null && faces == null) ||
                (_hoverFaces != null && faces != null && _hoverFaces.SequenceEqual(faces))) return;
            _hoverFaces = faces;
            _hoverIsland = -1;
            if (faces != null) BuildHoverHighlight();
            view.Repaint();
        }

        void PickFacesOnScene(MeshUV.SurfaceHit hit, SelectOp op, SceneView scene)
        {
            if (VertexEdgeSelectionTool)
            {
                ApplyTopologyPick(PickSceneTopology(hit), op, scene);
                return;
            }
            if (_selectMode == SelectMode.FaceLoop)
            {
                var status = FindSceneFaceLoop(hit, out var loop);
                ApplyFaceLoop(status, loop, op, scene);
                return;
            }
            if (_selectMode == SelectMode.BoundaryLoop)
            {
                var status = FindSceneBoundaryLoop(hit, out var loop);
                ApplyBoundaryLoop(status, loop, op, scene);
                return;
            }
            ApplyFaceSelection(new[] { hit.triangle }, op);
        }

        void PickFacesOnTexture(Vector2 uv, SelectOp op)
        {
            if (VertexEdgeSelectionTool)
            {
                var pixelsPerUv = new Vector2(_comp.Width, _comp.Height) * _zoom;
                ApplyTopologyPick(_uv.PickTextureTopology(uv, pixelsPerUv, _selectMode == SelectMode.Vertex), op);
                return;
            }
            var faces = _uv.TrianglesAtUV(uv);
            if (_selectMode == SelectMode.FaceLoop)
            {
                var status = _uv.FindFaceLoop(faces, i => UVToCanvas(_uv.uv[i]), UVToCanvas(uv), out var loop);
                ApplyFaceLoop(status, loop, op);
            }
            else if (_selectMode == SelectMode.BoundaryLoop && faces.Count > 0)
            {
                var status = _uv.FindBoundaryLoop(faces, i => UVToCanvas(_uv.uv[i]), UVToCanvas(uv), out var loop);
                ApplyBoundaryLoop(status, loop, op);
            }
            else ApplyFaceSelection(faces, op);
        }

        MeshUV.TopologyHit PickSceneTopology(MeshUV.SurfaceHit hit)
        {
            var posed = _uv.PosedCombinedCached();
            Vector2 Project(int i)
            {
                var screen = HandleUtility.WorldToGUIPointWithDepth(posed[i]);
                return screen.z >= 0f ? (Vector2)screen : new Vector2(float.NaN, float.NaN);
            }
            return _uv.PickSceneTopology(hit.triangle, Project, HandleUtility.WorldToGUIPoint(hit.point), _selectMode == SelectMode.Vertex);
        }

        void ApplyTopologyPick(MeshUV.TopologyHit hit, SelectOp op, SceneView scene = null)
        {
            if (hit.faces != null)
            {
                ApplyFaceSelection(hit.faces, op);
                return;
            }
            string hint = hit.ambiguous && scene == null
                ? Loc.T("Several targets are equally close. Zoom in or pick on the model.")
                : _selectMode == SelectMode.Vertex
                    ? Loc.T("Click closer to one vertex to select the faces that touch it.")
                    : Loc.T("Click closer to one edge, away from its corners, to select the faces that touch it.");
            ReportTopologyFeedback(hint, scene);
        }

        void ReportTopologyFeedback(string hint, SceneView scene)
        {
            Toast(hint, MessageType.Info, 4f);
            if (scene == null) return;
            _sceneTopologyFeedbackView = scene;
            _sceneTopologyFeedbackMode = _selectMode;
            scene.Repaint();
        }

        string SceneTopologyHint(SceneView scene) =>
            scene != null && _sceneTopologyFeedbackView == scene && _sceneTopologyFeedbackMode == _selectMode &&
            !string.IsNullOrEmpty(_toastText) && EditorApplication.timeSinceStartup <= _toastUntil
                ? _toastText : SelectModeHint(scene: true);

        void ClearSceneTopologyFeedback()
        {
            var scene = _sceneTopologyFeedbackView;
            _sceneTopologyFeedbackView = null;
            if (scene != null) scene.Repaint();
        }
    }
}
