using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Symmetry painting: every brush, eraser, fill, blend and mask stroke is mirrored to the other side of the model
    /// (through the 3D mesh, see SymmetryMap). The mirrored half joins the same stroke when it's the same size, so where
    /// the two halves meet on the centre line the paint doesn't double up; one undo removes both.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _symmetry;
        [SerializeField] SymmetryMap.Axis _symmetryAxis = SymmetryMap.Axis.Auto;
        [SerializeField] bool _showMirrorPlane = true;

        SymmetryMap _symMap;
        MeshUV _symMapFor;
        SymmetryMap.Axis _symMapAxis;
        MeshUV _symTriedFor;                 // the last mesh/axis a build was attempted for (so a failure isn't retried every frame)
        SymmetryMap.Axis _symTriedAxis;
        double _symQueuedAt = -10;           // when a build was queued (a stale queue, e.g. lost in a script reload, doesn't block a new one)
        PaintStroke _liveMirror;
        readonly List<float> _mirrorScales = new List<float>();
        readonly List<float> _mirrorSorted = new List<float>();
        float _mirrorMedian = 1f;
        int _mirrorMedianCount = -1;
        bool _mirrorPrevOk;
        int _mirrorPrevIsland = -1;

        bool SymmetryActive => _symmetry && _tool == Tool.Paint && _uv != null && !GradientToolActive;

        /// <summary>The current map, if it was built for this mesh (never builds: safe to call while drawing).</summary>
        SymmetryMap CurrentSymmetry => _symMap != null && _symMapFor == _uv && _symMapAxis == _symmetryAxis ? _symMap : null;

        /// <summary>Builds (or rebuilds after the avatar moved) the mirror map for the current mesh.</summary>
        bool EnsureSymmetry(bool force = false)
        {
            if (_uv == null) return false;
            if (_uv.PoseChanged()) { _uv.RefreshPose(); _symMap = null; }
            if (!force && CurrentSymmetry != null) return true;
            try
            {
                EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Finding the mirror plane of the model…"), 0.5f);
                _symMap = SymmetryMap.Build(_uv, _symmetryAxis);
            }
            finally { EditorUtility.ClearProgressBar(); }
            _symMapFor = _uv;
            _symMapAxis = _symmetryAxis;
            _symTriedFor = _uv;
            _symTriedAxis = _symmetryAxis;
            SceneView.RepaintAll();
            return _symMap != null;
        }

        /// <summary>EnsureSymmetry after this GUI pass, so the (blocking) build doesn't run inside OnGUI.</summary>
        void QueueEnsureSymmetry(bool force)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (_symmetry && _uv != null) EnsureSymmetry(force);
                Repaint();
            };
        }

        /// <summary>Call right after _liveStroke is created: prepares its mirrored half.</summary>
        void StartMirrorStroke()
        {
            _liveMirror = null;
            _mirrorScales.Clear();
            _mirrorMedianCount = -1;
            _mirrorPrevOk = false;
            _mirrorPrevIsland = -1;
            if (_liveStroke == null || !SymmetryActive || !EnsureSymmetry()) return;
            _liveMirror = JsonUtility.FromJson<PaintStroke>(JsonUtility.ToJson(_liveStroke));
            _liveMirror.points = new List<Vector4>();
            _liveMirror.surf = new List<Vector4>();
            _liveMirror.clipIslands = new List<int>();
            _comp.LiveMirror = _liveMirror;
        }

        /// <summary>Adds the mirror image of a stroke point (skipped on the centre line, shared texture areas, or no match).</summary>
        void MirrorStrokePoint(Vector2 uv, float pressure, bool joined, int island, bool surfJoined = true, bool backFace = false)
        {
            var map = CurrentSymmetry;
            if (_liveMirror == null || map == null) return;
            var r = map.MirrorUV(uv, island, out var m, out int mi, out float scale, out int mTri, out var mBary);
            if (r != SymmetryMap.Result.Mirrored) { _mirrorPrevOk = false; return; }
            bool join = joined && _mirrorPrevOk && mi == _mirrorPrevIsland; // a new island on the other side starts a new run
            if (_liveMirror.surface) _liveMirror.surf.Add(new Vector4(mTri, mBary.y, mBary.z, (surfJoined && _mirrorPrevOk ? 1 : 0) + (backFace ? 2 : 0)));
            _liveMirror.points.Add(new Vector4(m.x, m.y, pressure, join ? 1 : 0));
            _mirrorScales.Add(scale);
            if (_liveMirror.clip)
            {
                if (!_liveMirror.clipIslands.Contains(mi)) _liveMirror.clipIslands.Add(mi);
                foreach (int i in _uv.IslandsAtUV(m))
                    if (!_liveMirror.clipIslands.Contains(i)) _liveMirror.clipIslands.Add(i);
            }
            _mirrorPrevOk = true;
            _mirrorPrevIsland = mi;
            if (!_liveMirror.surface) _liveMirror.radius = _liveStroke.radius * MedianScale(); // a 3D brush is the same size on both sides
        }

        float MedianScale()
        {
            if (_mirrorScales.Count == 0) return 1f;
            if (_mirrorMedianCount != _mirrorScales.Count) // only recompute when scales were added
            {
                _mirrorSorted.Clear();
                _mirrorSorted.AddRange(_mirrorScales);
                _mirrorSorted.Sort();
                _mirrorMedian = Mathf.Clamp(_mirrorSorted[_mirrorSorted.Count / 2], 0.25f, 4f);
                _mirrorMedianCount = _mirrorScales.Count;
            }
            return _mirrorMedian;
        }

        /// <summary>The finished mirrored half (null if nothing was mirrored). Call before CancelStroke.</summary>
        PaintStroke TakeMirrorStroke()
        {
            var m = _liveMirror;
            _liveMirror = null;
            if (_comp != null) _comp.LiveMirror = null;
            return m != null && m.points.Count > 0 ? m : null;
        }

        /// <summary>
        /// Stores the mirrored half: inside the same stroke when the brush size matches (no doubled paint where the halves
        /// overlap, one undo), otherwise as its own stroke right after it.
        /// </summary>
        static void AddMirror(List<PaintStroke> list, PaintStroke s, PaintStroke m)
        {
            if (m == null) return;
            if (!s.isFill && s.radius > 0 && Mathf.Abs(m.radius / s.radius - 1f) < 0.15f)
            {
                var first = m.points[0];
                m.points[0] = new Vector4(first.x, first.y, first.z, 0); // a separate run, not connected to the original
                s.points.AddRange(m.points);
                if (s.surface && m.surf.Count > 0)
                {
                    var f0 = m.surf[0];
                    m.surf[0] = new Vector4(f0.x, f0.y, f0.z, Mathf.RoundToInt(f0.w) & 2); // not joined; keep the back-face flag
                    s.surf.AddRange(m.surf);
                }
                foreach (int i in m.clipIslands)
                    if (!s.clipIslands.Contains(i)) s.clipIslands.Add(i);
            }
            else list.Add(m);
        }

        /// <summary>Symmetry controls, drawn inside the "Painting options" group.</summary>
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
            if (on != _symmetry)
            {
                _symmetry = on;
                if (on) QueueEnsureSymmetry(false);
                Repaint();
                SceneView.RepaintAll();
            }
            if (_symmetry)
            {
                if (GradientToolActive) GUILayout.Label(Loc.T("Gradients aren't mirrored (they're layers across the texture)."), StudioGUI.Hint);
                using (new EditorGUILayout.HorizontalScope())
                {
                    StudioGUI.Count("paint.mirrorAxis");
                    var axis = (SymmetryMap.Axis)EditorGUILayout.EnumPopup(new GUIContent(Loc.T("Mirror"), Loc.T("Which way the model is mirrored. Auto picks the best match (almost always left ↔ right).")), _symmetryAxis);
                    if (axis != _symmetryAxis) { _symmetryAxis = axis; QueueEnsureSymmetry(true); }
                    StudioGUI.Count("paint.mirrorCheck");
                    if (GUILayout.Button(new GUIContent(Loc.T("Check again"), Loc.T("Re-measure, e.g. after moving or posing the avatar")), EditorStyles.miniButton, GUILayout.Width(80))) QueueEnsureSymmetry(true);
                }
                var map = CurrentSymmetry;
                if (map == null)
                {
                    if (_symTriedFor != _uv || _symTriedAxis != _symmetryAxis)
                    {
                        // The map just hasn't been built for this mesh yet: build it once, after this layout pass.
                        GUILayout.Label(Loc.T("Measuring…"), StudioGUI.Hint);
                        if (EditorApplication.timeSinceStartup - _symQueuedAt > 1.5) // once per mesh, not every frame
                        {
                            _symQueuedAt = EditorApplication.timeSinceStartup;
                            EditorApplication.delayCall += () =>
                            {
                                _symQueuedAt = -10;
                                if (this != null && _symmetry && _uv != null && CurrentSymmetry == null) EnsureSymmetry();
                                if (this != null) Repaint();
                            };
                        }
                    }
                    else StudioGUI.CheckRow(StudioGUI.Check.Problem, SymmetryFailReason());
                }
                else
                {
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
                if (plane != _showMirrorPlane) { _showMirrorPlane = plane; SceneView.RepaintAll(); }
            }
        }

        /// <summary>Plain-words reason the mirror map couldn't be built, with what to do about it.</summary>
        string SymmetryFailReason()
        {
            if (_uv == null || _uv.parts.Count == 0 || _uv.parts.TrueForAll(p => p.renderer == null))
                return Loc.T("Couldn't mirror: no part was found in the scene for this texture. Pick the part again in 1 · Pick, then press Check again.");
            if (_uv.TriangleCount == 0)
                return Loc.T("Couldn't mirror: this part has no triangles to measure. Pick a different part in 1 · Pick.");
            return Loc.T("Couldn't mirror: the part isn't mirrored (no usable shape was found to measure). Try another Mirror direction, or turn mirroring off for this part.");
        }

        // Last mirror-cursor lookup, reused while the UV and the map are unchanged (a new map object is made on every rebuild).
        SymmetryMap _cursorMap;
        Vector2 _cursorUv;
        SymmetryMap.Result _cursorResult;
        Vector2 _cursorMirrored;
        float _cursorScale;

        /// <summary>Second brush ring on the texture where the mirrored paint will land.</summary>
        void DrawCanvasMirrorCursor(Vector2 mp, float rPx)
        {
            var map = CurrentSymmetry;
            if (!SymmetryActive || map == null || _comp == null) return;
            var uvAt = CanvasToUV(mp);
            if (_cursorMap != map || _cursorUv != uvAt)
            {
                _cursorResult = map.MirrorUV(uvAt, -1, out _cursorMirrored, out _, out _cursorScale);
                _cursorMap = map;
                _cursorUv = uvAt;
            }
            var r = _cursorResult;
            var m = _cursorMirrored;
            float scale = _cursorScale;
            if (r != SymmetryMap.Result.Mirrored) return;
            var c = (Vector3)UVToCanvas(m);
            var col = StudioGUI.Accent;
            Handles.color = new Color(0, 0, 0, 0.6f);
            Handles.DrawWireDisc(c, Vector3.forward, rPx * scale + 1f);
            Handles.color = new Color(col.r, col.g, col.b, 0.95f);
            Handles.DrawWireDisc(c, Vector3.forward, rPx * scale);
            Handles.DrawLine(c + Vector3.left * 3, c + Vector3.right * 3);
            Handles.DrawLine(c + Vector3.up * 3, c + Vector3.down * 3);
        }

        /// <summary>Scene view: the mirror plane and the mirrored brush on the other side of the model.</summary>
        void DrawSceneSymmetry(bool brushValid)
        {
            var map = CurrentSymmetry;
            if (!SymmetryActive || map == null) return;
            using (new Handles.DrawingScope())
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                if (_showMirrorPlane)
                {
                    var c = map.centre;
                    var verts = new[] { c - map.planeU - map.planeV, c - map.planeU + map.planeV, c + map.planeU + map.planeV, c + map.planeU - map.planeV };
                    var a = StudioGUI.Accent;
                    Handles.DrawSolidRectangleWithOutline(verts, new Color(a.r, a.g, a.b, 0.04f), new Color(a.r, a.g, a.b, 0.6f));
                }
                if (brushValid && _sceneHit.uvPerWorld > 0 && _comp != null)
                {
                    float uvRadius = _brush.size * 0.5f / Mathf.Max(1, _comp.SourceWidth);
                    float worldR = Mathf.Min(uvRadius / _sceneHit.uvPerWorld, 2f);
                    var p = map.Mirror(_sceneHit.point);
                    var n = _sceneHit.normal - 2f * Vector3.Dot(_sceneHit.normal, map.normal) * map.normal;
                    var col = StudioGUI.Accent;
                    Handles.color = new Color(col.r, col.g, col.b, 0.9f);
                    Handles.DrawWireDisc(p + n * worldR * 0.02f, n, worldR);
                }
            }
        }
    }
}
