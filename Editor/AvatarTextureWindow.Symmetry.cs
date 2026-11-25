using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _symmetry;
        [SerializeField] SymmetryMap.Axis _symmetryAxis = SymmetryMap.Axis.Auto;
        [SerializeField] bool _showMirrorPlane = true;

        SymmetryMap _symMap;
        MeshUV _symMapFor;
        (SymmetryMap.Axis axis, Vector3 angles, float shift) _symMapSettings;
        int _symMapPose;
        MeshUV _symTriedFor;
        (SymmetryMap.Axis axis, Vector3 angles, float shift) _symTriedSettings;
        int _symTriedPose;
        double _symQueuedAt = -10;
        PaintStroke _liveMirror;
        readonly List<float> _mirrorScales = new List<float>();
        readonly List<float> _mirrorSorted = new List<float>();
        float _mirrorMedian = 1f;
        int _mirrorMedianCount = -1;
        bool _mirrorPrevOk;
        int _mirrorPrevIsland = -1;

        bool SymmetryActive => _symmetry && CanvasPainting && _uv != null && !GradientToolActive && !MirrorNeedsClassicAxis;

        (SymmetryMap.Axis axis, Vector3 angles, float shift) MirrorSettings => _symmetryAxis == SymmetryMap.Axis.Custom
            ? (_symmetryAxis, _mirrorPlaneAngles, _mirrorPlaneShift) : (_symmetryAxis, Vector3.zero, 0f);

        SymmetryMap CurrentSymmetry => _symMap != null && _uv != null && _symMapFor == _uv &&
            _symMapSettings == MirrorSettings && _symMapPose == _uv.PoseVersion ? _symMap : null;

        bool EnsureSymmetry(bool force = false)
        {
            if (_uv == null) return false;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            if (!force && CurrentSymmetry != null) return true;
            var settings = MirrorSettings;
            if (!force && _symTriedFor == _uv && _symTriedSettings == settings && _symTriedPose == _uv.PoseVersion) return false;
            try
            {
                EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Finding the mirror plane of the model…"), 0.5f);
                _symMap = SymmetryMap.BuildWithPlane(_uv, settings.axis, settings.angles, settings.shift);
            }
            finally { EditorUtility.ClearProgressBar(); }
            _symMapFor = _uv;
            _symMapSettings = settings;
            _symMapPose = _uv.PoseVersion;
            _symTriedFor = _uv;
            _symTriedSettings = settings;
            _symTriedPose = _uv.PoseVersion;
            SceneView.RepaintAll();
            return _symMap != null;
        }

        void QueueEnsureSymmetry(bool force)
        {
            var mesh = _uv;
            if (mesh == null) return;
            var settings = MirrorSettings;
            _symQueuedAt = EditorApplication.timeSinceStartup;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                _symQueuedAt = -10;
                if (SymmetryActive && !_needsRebuild && _uv == mesh && MirrorSettings == settings) EnsureSymmetry(force);
                Repaint();
            };
        }

        void SetSymmetryEnabled(bool on)
        {
            if (_symmetry == on) return;
            Undo.RecordObject(this, "Toggle mirror painting");
            _symmetry = on;
            if (on) QueueEnsureSymmetry(false);
            Repaint();
            SceneView.RepaintAll();
        }

        void StartMirrorStroke()
        {
            _liveMirror = null;
            _advancedMirrorRuns = null;
            _mirrorScales.Clear();
            _mirrorMedianCount = -1;
            _mirrorPrevOk = false;
            _mirrorPrevIsland = -1;
            if (_liveStroke == null || !SymmetryActive || !EnsureSymmetry()) return;
            if (AdvancedMirrorAxis)
            {
                StartAdvancedMirrorStroke();
                return;
            }
            _liveMirror = JsonUtility.FromJson<PaintStroke>(JsonUtility.ToJson(_liveStroke));
            _liveMirror.points = new List<Vector4>();
            _liveMirror.tipAngles = _liveStroke.tipAngles != null ? new List<Vector2>() : null;
            _liveMirror.surf = new List<Vector4>();
            _liveMirror.clipIslands = new List<int>();
            _comp.LiveMirror = _liveMirror;
        }

        void MirrorStrokePoint(Vector2 uv, float pressure, bool joined, int island, bool surfJoined = true, bool backFace = false,
            MeshUV.SurfaceHit? hit = null)
        {
            var map = CurrentSymmetry;
            if (_advancedMirrorRuns != null)
            {
                if (map != null) AddAdvancedMirrorPoint(map, uv, island, hit, pressure, joined, surfJoined);
                else foreach (var run in _advancedMirrorRuns) run.continued = false;
                return;
            }
            if (_liveMirror == null || map == null) return;
            var r = map.MirrorUV(uv, island, out var m, out int mi, out float scale, out int mTri, out var mBary);
            if (r != SymmetryMap.Result.Mirrored || !SceneTriangleUsesCurrentUv(mTri)) { _mirrorPrevOk = false; return; }
            if (_liveMirror.isFill)
            {
                if (PlanFill(m, mi, mTri, out var fill) != null) return;
                _liveMirror.fillRegion = fill;
            }
            bool join = joined && _mirrorPrevOk && mi == _mirrorPrevIsland;
            if (_liveMirror.surface) _liveMirror.surf.Add(new Vector4(mTri, mBary.y, mBary.z, (surfJoined && _mirrorPrevOk ? 1 : 0) + (backFace ? 2 : 0)));
            _liveMirror.points.Add(new Vector4(m.x, m.y, pressure, join ? 1 : 0));
            _liveMirror.tipAngles?.Add(MirrorPenAngle(map, uv, island, hit, mTri, 1));
            _mirrorScales.Add(scale);
            AddBrushClipPieces(_liveMirror, _liveMirror.clipIslands, m, mi, pressure, mTri, mBary);
            _mirrorPrevOk = true;
            _mirrorPrevIsland = mi;
            if (!_liveMirror.surface) _liveMirror.radius = _liveStroke.radius * MedianScale();
        }

        float MedianScale()
        {
            if (_mirrorScales.Count == 0) return 1f;
            if (_mirrorMedianCount != _mirrorScales.Count)
            {
                _mirrorSorted.Clear();
                _mirrorSorted.AddRange(_mirrorScales);
                _mirrorSorted.Sort();
                _mirrorMedian = Mathf.Clamp(_mirrorSorted[_mirrorSorted.Count / 2], 0.25f, 4f);
                _mirrorMedianCount = _mirrorScales.Count;
            }
            return _mirrorMedian;
        }

        PaintStroke TakeMirrorStroke()
        {
            if (_liveStroke?.mirrorPaths != null)
            {
                _liveStroke.mirrorPaths.RemoveAll(path => path.points.Count == 0);
                if (_liveStroke.mirrorPaths.Count == 0) _liveStroke.mirrorPaths = null;
            }
            var m = _liveMirror;
            _liveMirror = null;
            if (_comp != null) _comp.LiveMirror = null;
            return m != null && m.points.Count > 0 ? m : null;
        }

        static void AddMirror(List<PaintStroke> list, PaintStroke s, PaintStroke m)
        {
            if (m == null) return;
            if (s.isFill && s.fillRegion != null && m.fillRegion != null)
            {
                s.fillMirrorRegion = m.fillRegion;
                s.points.AddRange(m.points);
                if (s.tipAngles != null && m.tipAngles != null) s.tipAngles.AddRange(m.tipAngles);
            }
            else if (!s.isFill && s.radius > 0 && Mathf.Abs(m.radius / s.radius - 1f) < 0.15f)
            {
                var first = m.points[0];
                m.points[0] = new Vector4(first.x, first.y, first.z, 0);
                s.points.AddRange(m.points);
                if (s.tipAngles != null && m.tipAngles != null) s.tipAngles.AddRange(m.tipAngles);
                if (s.surface && m.surf.Count > 0)
                {
                    var f0 = m.surf[0];
                    m.surf[0] = new Vector4(f0.x, f0.y, f0.z, Mathf.RoundToInt(f0.w) & 2);
                    s.surf.AddRange(m.surf);
                }
                foreach (int i in m.clipIslands)
                    if (!s.clipIslands.Contains(i)) s.clipIslands.Add(i);
            }
            else list.Add(m);
        }

        static string MirrorAxisLabel(SymmetryMap.Axis axis)
        {
            switch (axis)
            {
                case SymmetryMap.Axis.LeftRight: return Loc.T("Left ↔ right");
                case SymmetryMap.Axis.UpDown: return Loc.T("Up ↔ down");
                case SymmetryMap.Axis.FrontBack: return Loc.T("Front ↔ back");
                case SymmetryMap.Axis.Both: return Loc.T("Both axes");
                case SymmetryMap.Axis.Custom: return Loc.T("Custom plane");
                default: return Loc.T("Auto (best match)");
            }
        }

        static readonly SymmetryMap.Axis[] MirrorAxes =
        {
            SymmetryMap.Axis.Auto, SymmetryMap.Axis.LeftRight, SymmetryMap.Axis.UpDown,
            SymmetryMap.Axis.FrontBack, SymmetryMap.Axis.Both, SymmetryMap.Axis.Custom,
        };

        SymmetryMap.Axis DrawMirrorAxisChoice(string controlId, float width)
        {
            var axes = Advanced && !MirrorToolRequiresClassicAxis ? MirrorAxes : Array.FindAll(MirrorAxes,
                axis => axis <= SymmetryMap.Axis.FrontBack || axis == _symmetryAxis);
            var names = Array.ConvertAll(axes, MirrorAxisLabel);
            var label = new GUIContent(Loc.T("Mirror direction"), !Advanced && !MirrorToolRequiresClassicAxis
                ? Loc.T("Advanced adds Both axes and Custom plane.")
                : Loc.T("Which way the model is mirrored. Auto picks the best match (almost always left ↔ right)."));
            var current = Array.IndexOf(axes, _symmetryAxis) >= 0 ? _symmetryAxis : SymmetryMap.Axis.Auto;
            float labelWidth = EditorStyles.label.CalcSize(label).x + 6f;
            bool stack = width < StackedChoiceWidth || width < Mathf.Max(EditorGUIUtility.labelWidth, labelWidth) + 120f;
            if (stack) GUILayout.Label(label, EditorStyles.miniLabel);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            if (!stack) EditorGUIUtility.labelWidth = Mathf.Max(previousLabelWidth, labelWidth);
            SymmetryMap.Axis next;
            try
            {
                next = SteppedChoice(stack ? GUIContent.none : label, current, axes,
                    (rect, axis) => axes[EditorGUI.Popup(rect, Array.IndexOf(axes, axis), names)], width);
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            var field = GUILayoutUtility.GetLastRect();
            field.width = Mathf.Min(field.width, width);
            StudioGUI.Count(controlId, field);
            return next == current ? _symmetryAxis : next;
        }

        void SetMirrorAxis(SymmetryMap.Axis axis)
        {
            if (axis == _symmetryAxis) return;
            ChangeSceneBar("Change mirror direction", () =>
            {
                _symmetryAxis = axis;
                QueueEnsureSymmetry(true);
            });
        }

        void DrawSceneMirrorSettings(float width, ref Action change)
        {
            var toggle = SceneBarRow(width);
            bool enabled = EditorGUI.ToggleLeft(toggle, Loc.T("Mirror"), _symmetry);
            StudioGUI.Count("scene.mirror", toggle);
            if (enabled != _symmetry) change = () => SetSymmetryEnabled(enabled);
            if (!_symmetry) return;

            var axis = DrawMirrorAxisChoice("scene.mirrorAxis", width);
            if (axis != _symmetryAxis) change = () => SetMirrorAxis(axis);
        }

        void DrawSymmetryControls()
        {
            bool on;
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("paint.mirror");
                on = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Mirror strokes to the other side (symmetry)"),
                    Loc.T("Everything you paint is also painted on the other side of the model, mirrored through the 3D part (works even when the texture isn't laid out symmetrically).")), _symmetry);
                StudioGUI.Info("seam");
            }
            SetSymmetryEnabled(on);
            if (_symmetry)
            {
                if (GradientToolActive) GUILayout.Label(Loc.T("Gradients aren't mirrored (they're layers across the texture)."), StudioGUI.Hint);
                var check = new GUIContent(Loc.T("Check again"), Loc.T("Re-measure, e.g. after moving or posing the avatar"));
                float checkWidth = EditorStyles.miniButton.CalcSize(check).x + 6f;
                SymmetryMap.Axis axis;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                        axis = DrawMirrorAxisChoice("paint.mirrorAxis", Mathf.Max(1f, PanelContentWidth - checkWidth - 4f));
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(checkWidth)))
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(MirrorNeedsClassicAxis))
                            if (GUILayout.Button(check, EditorStyles.miniButton)) QueueEnsureSymmetry(true);
                        StudioGUI.Count("paint.mirrorCheck", GUILayoutUtility.GetLastRect());
                    }
                }
                SetMirrorAxis(axis);
                if (_symmetryAxis == SymmetryMap.Axis.Custom && Advanced) DrawCustomMirrorSettings();
                if (_symmetryAxis == SymmetryMap.Axis.Both)
                    GUILayout.Label(Loc.T("Mirrors left ↔ right and up ↔ down together."), StudioGUI.Hint);
                var map = CurrentSymmetry;
                if (MirrorNeedsClassicAxis) GUILayout.Label(MirrorClassicHint, StudioGUI.Hint);
                else if (map == null)
                {
                    if (_symTriedFor != _uv || _symTriedSettings != MirrorSettings)
                    {
                        GUILayout.Label(Loc.T("Measuring…"), StudioGUI.Hint);
                        if (EditorApplication.timeSinceStartup - _symQueuedAt > 1.5) QueueEnsureSymmetry(false);
                    }
                    else if (_uv != null && _symTriedPose != _uv.PoseVersion)
                        GUILayout.Label(Loc.T("The pose changed. Press Check again or paint a stroke to update the mirror."), StudioGUI.Hint);
                    else StudioGUI.CheckRow(StudioGUI.Check.Problem, SymmetryFailReason());
                }
                else
                {
                    if (_symmetryAxis == SymmetryMap.Axis.Auto)
                        GUILayout.Label(Loc.F("Auto chose {0}.", MirrorAxisLabel(map.axis)), StudioGUI.Hint);
                    int pct = Mathf.RoundToInt(map.score * 100f);
                    string err = map.meanError * 1000f < 0.05f ? Loc.T("under 0.1 mm") : Loc.F("{0:0.0} mm", map.meanError * 1000f);
                    if (map.score >= 0.95f)
                        StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("Lines up: {0}% of this part matches its mirror image (average error {1}).", pct, err));
                    else if (map.score >= 0.8f)
                        StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("Mostly symmetric ({0}%). Parts without a match on the other side aren't mirrored.", pct));
                    else
                        StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.F("Only {0}% of this part is symmetric (common for hair and accessories). Strokes are only mirrored where the other side matches.", pct));
                }
                StudioGUI.Count("paint.mirrorPlane");
                bool plane = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Show the mirror plane in the Scene view")), _showMirrorPlane);
                if (plane != _showMirrorPlane)
                {
                    Undo.RecordObject(this, "Show or hide mirror plane");
                    _showMirrorPlane = plane;
                    SceneView.RepaintAll();
                }
            }
        }

        string SymmetryFailReason()
        {
            if (_uv == null || _uv.parts.Count == 0 || _uv.parts.TrueForAll(p => p.renderer == null))
                return Loc.T("Couldn't mirror: no part was found in the scene for this texture. Pick the part again in 1 · Pick, then press Check again.");
            if (_uv.TriangleCount == 0)
                return Loc.T("Couldn't mirror: this part has no triangles to measure. Pick a different part in 1 · Pick.");
            return Loc.T("Couldn't mirror: the part isn't mirrored (no usable shape was found to measure). Try another Mirror direction, or turn mirroring off for this part.");
        }

        SymmetryMap _cursorMap;
        Vector2 _cursorUv;
        int _cursorPiece, _cursorMirroredPiece, _cursorTriangle;
        Vector3 _cursorBarycentric;
        SymmetryMap.Result _cursorResult;
        Vector2 _cursorMirrored;
        float _cursorScale;

        void SampleMirrorCursor(SymmetryMap map, Vector2 uv, int piece)
        {
            if (_cursorMap == map && _cursorUv == uv && _cursorPiece == piece) return;
            _cursorResult = map.MirrorUV(uv, piece, out _cursorMirrored, out _cursorMirroredPiece, out _cursorScale, out _cursorTriangle, out _cursorBarycentric);
            if (_cursorResult == SymmetryMap.Result.Mirrored && !SceneTriangleUsesCurrentUv(_cursorTriangle))
                _cursorResult = SymmetryMap.Result.None;
            _cursorMap = map;
            _cursorUv = uv;
            _cursorPiece = piece;
        }

        void DrawCanvasMirrorCursor(Vector2 mp, float rPx)
        {
            var map = CurrentSymmetry;
            if (!SymmetryActive || map == null || _comp == null) return;
            if (AdvancedMirrorAxis)
            {
                foreach (var hit in SampleAdvancedMirrorCursor(map, CanvasToUV(mp), null))
                    if (InsideMirrorPaintArea(hit.uv) && SceneTriangleUsesCurrentUv(hit.triangle))
                        DrawTextureMirrorRing(hit.uv, rPx * hit.scale);
                return;
            }
            SampleMirrorCursor(map, CanvasToUV(mp), -1);
            if (_cursorResult == SymmetryMap.Result.Mirrored) DrawTextureMirrorRing(_cursorMirrored, rPx * _cursorScale);
        }

        void DrawTextureMirrorRing(Vector2 uv, float radius)
        {
            var c = (Vector3)UVToCanvas(uv);
            var col = StudioGUI.Accent;
            Handles.color = new Color(0, 0, 0, 0.6f);
            Handles.DrawWireDisc(c, Vector3.forward, radius + 1f);
            Handles.color = new Color(col.r, col.g, col.b, 0.95f);
            Handles.DrawWireDisc(c, Vector3.forward, radius);
            Handles.DrawLine(c + Vector3.left * 3, c + Vector3.right * 3);
            Handles.DrawLine(c + Vector3.up * 3, c + Vector3.down * 3);
        }

        void DrawSceneSymmetry(bool brushValid)
        {
            var map = CurrentSymmetry;
            if (!SymmetryActive || map == null) return;
            using (new Handles.DrawingScope())
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                if (_showMirrorPlane)
                {
                    DrawMirrorPlane(map.centre, map.planeU, map.planeV);
                    if (map.axis == SymmetryMap.Axis.Both) DrawMirrorPlane(map.secondCentre, map.secondU, map.secondV);
                }
                if (!brushValid) return;
                var col = StudioGUI.Accent;
                if (_brush.fillTool)
                {
                    if (BasicFillCursorProblem(_sceneHit.uv) == null && TryGetSceneMirrorCursor(map, out var seed, out _))
                        DrawSceneFillMarker(seed, col);
                    return;
                }
                float worldRadius = SceneBrushWorldRadius();
                if (worldRadius <= 0f) return;
                Handles.color = new Color(col.r, col.g, col.b, 0.9f);
                if (AdvancedMirrorAxis)
                {
                    foreach (var hit in SampleAdvancedMirrorCursor(map, _sceneHit.uv, _sceneHit))
                        if (InsideMirrorPaintArea(hit.uv) && SceneTriangleUsesCurrentUv(hit.triangle) &&
                            _uv.SurfacePoint(hit.triangle, hit.bary, out var p, out var n))
                            Handles.DrawWireDisc(p + n * worldRadius * 0.02f, n, worldRadius);
                }
                else if (TryGetSceneMirrorCursor(map, out var point, out var normal))
                    Handles.DrawWireDisc(point + normal * worldRadius * 0.02f, normal, worldRadius);
            }
        }

        static void DrawMirrorPlane(Vector3 centre, Vector3 u, Vector3 v)
        {
            var verts = new[] { centre - u - v, centre - u + v, centre + u + v, centre + u - v };
            var a = StudioGUI.Accent;
            Handles.DrawSolidRectangleWithOutline(verts, new Color(a.r, a.g, a.b, 0.04f), new Color(a.r, a.g, a.b, 0.6f));
        }

        bool TryGetSceneMirrorCursor(SymmetryMap map, out Vector3 point, out Vector3 normal)
        {
            SampleMirrorCursor(map, _sceneHit.uv, _sceneHit.island);
            point = normal = default;
            return _cursorResult == SymmetryMap.Result.Mirrored &&
                (!_brush.fillTool || PlanFill(_cursorMirrored, _cursorMirroredPiece, _cursorTriangle, out _) == null) &&
                _uv.SurfacePoint(_cursorTriangle, _cursorBarycentric, out point, out normal);
        }
    }
}
