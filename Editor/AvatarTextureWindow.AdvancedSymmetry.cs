using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] Vector3 _mirrorPlaneAngles;
        [SerializeField] float _mirrorPlaneShift;

        bool AdvancedMirrorAxis => _symmetryAxis == SymmetryMap.Axis.Both || _symmetryAxis == SymmetryMap.Axis.Custom;
        bool MirrorToolRequiresClassicAxis => _brush.fillTool || BlendToolActive;
        bool MirrorNeedsClassicAxis => AdvancedMirrorAxis && MirrorToolRequiresClassicAxis;
        static string MirrorClassicHint => Loc.T("Fill and Blend use Auto, Left ↔ right, Up ↔ down, or Front ↔ back.");

        sealed class MirrorRun
        {
            internal readonly MirroredPaintPath path;
            internal readonly List<float> scales = new List<float>();
            internal int previousPiece = -1;
            internal bool continued;

            internal MirrorRun(float radius) { path = new MirroredPaintPath { radius = radius }; }
        }

        MirrorRun[] _advancedMirrorRuns;
        readonly List<SymmetryMap.MirrorDestination> _advancedDestinations = new List<SymmetryMap.MirrorDestination>(3);

        void StartAdvancedMirrorStroke()
        {
            int count = _symmetryAxis == SymmetryMap.Axis.Both ? 3 : 1;
            _advancedMirrorRuns = new MirrorRun[count];
            var paths = new List<MirroredPaintPath>(count);
            for (int index = 0; index < count; index++)
            {
                var run = new MirrorRun(_liveStroke.radius);
                if (_liveStroke.tipAngles != null) run.path.tipAngles = new List<Vector2>();
                _advancedMirrorRuns[index] = run;
                paths.Add(run.path);
            }
            _liveStroke.mirrorPaths = paths;
        }

        void AddAdvancedMirrorPoint(SymmetryMap map, Vector2 uv, int island, MeshUV.SurfaceHit? hit, float pressure, bool joined, bool surfJoined)
        {
            map.MirrorDestinations(uv, island, hit, _advancedDestinations);
            int seen = 0;
            foreach (var destination in _advancedDestinations)
            {
                if (destination.branch >= _advancedMirrorRuns.Length || !_liveStroke.IncludesPaintUV(destination.uv) ||
                    !SceneTriangleUsesCurrentUv(destination.triangle)) continue;
                seen |= 1 << destination.branch;
                var run = _advancedMirrorRuns[destination.branch];
                var path = run.path;
                bool connect = run.continued && run.previousPiece == destination.piece;
                path.points.Add(new Vector4(destination.uv.x, destination.uv.y, pressure, joined && connect ? 1f : 0f));
                path.tipAngles?.Add(MirrorPenAngle(map, uv, island, hit, destination.triangle, destination.branch + 1));
                if (_liveStroke.surface)
                {
                    float flags = surfJoined && run.continued ? 1f : 0f;
                    if (hit?.backFace == true) flags += 2f;
                    path.surfacePoints.Add(new Vector4(destination.triangle, destination.bary.y, destination.bary.z, flags));
                }
                else
                {
                    run.scales.Add(destination.scale);
                    _mirrorSorted.Clear();
                    _mirrorSorted.AddRange(run.scales);
                    _mirrorSorted.Sort();
                    path.radius = _liveStroke.radius * _mirrorSorted[_mirrorSorted.Count / 2];
                }
                AddBrushClipPieces(_liveStroke, path.pieces, destination.uv, destination.piece, pressure,
                    destination.triangle, destination.bary);
                run.previousPiece = destination.piece;
            }
            for (int index = 0; index < _advancedMirrorRuns.Length; index++)
                _advancedMirrorRuns[index].continued = (seen & (1 << index)) != 0;
        }

        void DrawCustomMirrorSettings()
        {
            EditorGUI.BeginChangeCheck();
            Vector3 angles = EditorGUILayout.Vector3Field(Loc.T("Plane rotation (°)"), _mirrorPlaneAngles);
            float offset = EditorGUILayout.Slider(new GUIContent(Loc.T("Plane offset (%)"), Loc.T("Shift the plane from the centre, as a share of the model's size.")),
                _mirrorPlaneShift * 100f, -50f, 50f) * 0.01f;
            if (!EditorGUI.EndChangeCheck()) return;
            if (!float.IsFinite(angles.x) || !float.IsFinite(angles.y) || !float.IsFinite(angles.z) || !float.IsFinite(offset)) return;
            if (angles == _mirrorPlaneAngles && offset == _mirrorPlaneShift) return;
            Undo.RecordObject(this, Loc.T("Move custom mirror plane"));
            _mirrorPlaneAngles = angles;
            _mirrorPlaneShift = offset;
            QueueEnsureSymmetry(false);
        }

        bool InsideMirrorPaintArea(Vector2 point) => _liveStroke?.IncludesPaintUV(point) ?? InsidePaintUvLimit(point);

        readonly List<SymmetryMap.MirrorDestination> _advancedMirrorCursor = new List<SymmetryMap.MirrorDestination>(3);
        SymmetryMap _advancedCursorMap;
        Vector2 _advancedCursorUv;
        int _advancedCursorTri = -1;

        List<SymmetryMap.MirrorDestination> SampleAdvancedMirrorCursor(SymmetryMap map, Vector2 uv, MeshUV.SurfaceHit? hit)
        {
            int triangle = hit?.triangle ?? -1;
            if (_advancedCursorMap != map || _advancedCursorUv != uv || _advancedCursorTri != triangle)
            {
                map.MirrorDestinations(uv, hit?.island ?? -1, hit, _advancedMirrorCursor);
                _advancedCursorMap = map;
                _advancedCursorUv = uv;
                _advancedCursorTri = triangle;
            }
            return _advancedMirrorCursor;
        }
    }
}
