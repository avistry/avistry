using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal enum SelectMode { Click = 0, Box = 1, Lasso = 2, Wand = 3, Part = 4, Tile = 5, Face = 6, BoundaryLoop = 7,
        FaceLoop = 8, Vertex = 9, Edge = 10 }
    internal enum SelectOp { Replace = 0, Add = 1, Subtract = 2 }
    internal enum WandOutput { Pieces = 0, LayerMask = 1 }
    internal enum WandMaskAction { PreviousOperation = 0, Reveal = 1, Hide = 2 }

    partial class AvatarTextureWindow
    {
        const float WandMinShare = 0.05f;
        const float MarqueeMinPixels = 3f;

        [SerializeField] SelectMode _selectMode;
        [SerializeField] SelectOp _selectOp;
        [SerializeField] float _wandTolerance = 0.15f;
        [SerializeField] bool _wandContiguous = true;
        [SerializeField] WandOutput _wandOutput;
        [SerializeField] WandMaskAction _wandMaskAction;

        bool _marqueeActive, _marqueeInScene, _marqueeMoved;
        readonly SceneMarquee _sceneMarqueeRaster = new SceneMarquee();
        SceneView _sceneMarqueeView;
        int _sceneMarqueeControl;
        Vector2 _marqueeStart, _marqueeNow, _marqueeStartPx, _lassoLastPx;
        float _lassoGapSq = 16f;
        List<Vector2> _lasso;
        SelectOp _marqueeOp;

        static string[] SelectModeNames => new[] { Loc.T("Click"), Loc.T("Box"), Loc.T("Lasso"), Loc.T("Wand"), Loc.T("Part"), Loc.T("Tile"),
            Loc.T("Face"), Loc.T("Boundary loop"), Loc.T("Face loop"), Loc.T("Vertex"), Loc.T("Edge") };

        SelectOp SelectOpOf(Event e) => e.shift ? SelectOp.Add : (e.control || e.command) ? SelectOp.Subtract :
            WandMasksLayer ? WandHides ? SelectOp.Subtract : SelectOp.Add : _selectOp;

        void ApplyOp(List<int> islands, SelectOp op) => ApplySelection(islands, op == SelectOp.Add, op == SelectOp.Subtract);

        internal void SetSelectMode(SelectMode mode, bool pickInScene = false)
        {
            ClearSceneTopologyFeedback();
            if (!PickingForPaint) SetTool(Tool.Select);
            CancelMarquee();
            _selectMode = mode;
            ClearScenePiecePreview();
            _hoverFaces = null;
            if (pickInScene)
            {
                if (PickingForPaint) _paintInScene = true;
                else _pickInScene = true;
            }
            Repaint();
            SceneView.RepaintAll();
        }

        void SelectUiChanged()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        bool SelectionUsesFaces(Layer pendingLimit) => pendingLimit != null ? _layerLimitUsesFaces :
            HasFaceSelection || (FaceSelectionTool && _selection.Count == 0);

        void SelectEverything()
        {
            if (_uv == null) return;
            if (SelectionUsesFaces(PendingLayerLimit)) ApplyFaceSelection(Enumerable.Range(0, _uv.TriangleCount), SelectOp.Replace);
            else SetSelection(Enumerable.Range(0, _uv.islandCount));
        }
        void SelectNothing() => SetSelection(Enumerable.Empty<int>());
        void InvertSelection()
        {
            if (_uv == null) return;
            if (SelectionUsesFaces(PendingLayerLimit))
            {
                var now = HasFaceSelection ? new HashSet<int>(_selectedFaces) : new HashSet<int>();
                ApplyFaceSelection(Enumerable.Range(0, _uv.TriangleCount).Where(i => !now.Contains(i)), SelectOp.Replace);
            }
            else
            {
                var now = new HashSet<int>(_selection);
                SetSelection(Enumerable.Range(0, _uv.islandCount).Where(i => !now.Contains(i)));
            }
        }

        internal List<int> GrowSelection(int dir)
        {
            if (_uv == null || _selection.Count == 0) return new List<int>(_selection);
            var near = _uv.PieceNeighbours();
            var now = new HashSet<int>(_selection);
            var next = new HashSet<int>(now);
            if (dir > 0)
            {
                foreach (int i in now)
                    if (i >= 0 && i < near.Length) foreach (int j in near[i]) next.Add(j);
            }
            else
            {
                foreach (int i in now)
                    if (i >= 0 && i < near.Length && near[i].Any(j => !now.Contains(j))) next.Remove(i);
            }
            if (next.SetEquals(now)) { Toast(dir > 0 ? Loc.T("Nothing more touches the selection.") : Loc.T("No selected piece touches an unselected one."), MessageType.Info, 3f); return new List<int>(_selection); }
            SetSelection(next.OrderBy(i => i));
            return new List<int>(_selection);
        }

        List<int> SelectByShapeUV(IReadOnlyList<Vector2> outline, Func<Vector2, bool> inside, SelectOp op)
        {
            var picked = new List<int>();
            if (_uv == null) return picked;
            if (outline == null || outline.Count < 3) { ApplyOp(picked, op); return picked; }

            var (min, max) = ShapeExtents(outline);
            bool InsideShape(Vector2 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && inside(p);
            var seen = new HashSet<int>();
            var uv = _uv.uv;
            var tris = _uv.tris;
            for (int t = 0; t < _uv.triIsland.Length; t++)
            {
                int island = _uv.triIsland[t];
                if (seen.Contains(island)) continue;
                Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c = uv[tris[t * 3 + 2]];
                if (!FiniteUV(a) || !FiniteUV(b) || !FiniteUV(c)) continue;
                var tMin = Vector2.Min(a, Vector2.Min(b, c));
                var tMax = Vector2.Max(a, Vector2.Max(b, c));
                if (!BoundsTouch(min, max, tMin, tMax)) continue;
                if (!InsideShape(a) && !InsideShape(b) && !InsideShape(c) &&
                    !TriangleContains(a, b, c, outline[0]) && !OutlineTouchesTriangle(outline, tMin, tMax, a, b, c))
                    continue;
                seen.Add(island);
                picked.Add(island);
            }
            picked.Sort();
            ApplyOp(picked, op);
            return picked;
        }

        static bool FiniteUV(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y);

        static bool BoundsTouch(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax) =>
            aMin.x <= bMax.x && aMax.x >= bMin.x && aMin.y <= bMax.y && aMax.y >= bMin.y;

        static double SideOfEdge(Vector2 a, Vector2 b, Vector2 p) =>
            ((double)b.x - a.x) * ((double)p.y - a.y) - ((double)b.y - a.y) * ((double)p.x - a.x);

        static bool TriangleContains(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            double area = SideOfEdge(a, b, c);
            if (area == 0) return false;
            double winding = Math.Sign(area);
            return winding * SideOfEdge(a, b, p) >= 0 && winding * SideOfEdge(b, c, p) >= 0 && winding * SideOfEdge(c, a, p) >= 0;
        }

        static bool StraddlesEdge(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double cSide = SideOfEdge(a, b, c), dSide = SideOfEdge(a, b, d);
            return (cSide <= 0 && dSide >= 0) || (cSide >= 0 && dSide <= 0);
        }

        static bool SegmentsTouch(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            BoundsTouch(Vector2.Min(a, b), Vector2.Max(a, b), Vector2.Min(c, d), Vector2.Max(c, d)) &&
            StraddlesEdge(a, b, c, d) && StraddlesEdge(c, d, a, b);

        static bool OutlineTouchesTriangle(IReadOnlyList<Vector2> outline, Vector2 tMin, Vector2 tMax, Vector2 a, Vector2 b, Vector2 c)
        {
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                Vector2 p = outline[j], q = outline[i];
                if (!BoundsTouch(tMin, tMax, Vector2.Min(p, q), Vector2.Max(p, q))) continue;
                if (SegmentsTouch(p, q, a, b) || SegmentsTouch(p, q, b, c) || SegmentsTouch(p, q, c, a)) return true;
            }
            return false;
        }

        List<int> SelectBoxUV(Vector2 a, Vector2 b, SelectOp op)
        {
            var corners = new[] { a, new Vector2(a.x, b.y), b, new Vector2(b.x, a.y) };
            return SelectByShapeUV(corners, p => true, op);
        }

        List<int> SelectLassoUV(List<Vector2> poly, SelectOp op) => SelectByShapeUV(poly, p => InPolygon(poly, p), op);

        static (Vector2 min, Vector2 max) ShapeExtents(IReadOnlyList<Vector2> points)
        {
            var min = points[0]; var max = points[0];
            for (int i = 1; i < points.Count; i++) { min = Vector2.Min(min, points[i]); max = Vector2.Max(max, points[i]); }
            return (min, max);
        }

        static Rect BoundsOf(List<Vector2> pts)
        {
            var (min, max) = ShapeExtents(pts);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static bool InPolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        internal string SelectByShapeScene(Camera cam, Rect guiBounds, Func<Vector2, bool> inside, SelectOp op, out int count)
        {
            count = 0;
            if (_uv == null || cam == null) return Loc.T("Nothing to select: choose a target first.");
            var picked = PiecesInSceneShape(cam, guiBounds, inside, out int hidden);
            ApplyOp(picked, op);
            count = picked.Count;
            ReportHiddenPieces(hidden);
            return null;
        }

        List<int> PiecesInSceneShape(Camera cam, Rect guiBounds, Func<Vector2, bool> inside, out int hidden)
        {
            if (_uv.PoseChanged()) _uv.RefreshPose();
            var blockers = new List<SceneMarquee.Blocker>();
            foreach (var (_, mesh, tileFilter, cull, cutout) in BlockerSlots())
                blockers.Add(new SceneMarquee.Blocker { verts = mesh.PosedCombinedCached(), tris = mesh.tris, cull = cull,
                    tileFilter = tileFilter, cutout = cutout, uv = mesh.uv });
            bool PartDoubleSided(Renderer r) { foreach (var m in r.sharedMaterials) if (MaterialIsTwoSided(m)) return true; return false; }
            Predicate<MeshUV.Part> canSelect = _alsoUsers.Count == 0 ? null : IncludedHitUsesCurrentUv;
            return _sceneMarqueeRaster.PiecesInShape(_uv, cam, guiBounds, inside, blockers, IsDrawn, PartDoubleSided,
                _editedPartTileFilter ??= EditedPartTileFilter, canSelect, out hidden);
        }

        void ReportHiddenPieces(int hidden)
        {
            if (hidden > 0)
                Toast(hidden == 1 ? Loc.T("1 piece was hidden behind the model and was left out.") : Loc.F("{0} pieces were hidden behind the model and were left out.", hidden), MessageType.Info, 4f);
        }

        void BeginMarquee(Vector2 start, Vector2 startPx, bool inScene, SelectOp op)
        {
            _marqueeActive = true;
            _marqueeInScene = inScene;
            _marqueeStart = _marqueeNow = start;
            _marqueeStartPx = _lassoLastPx = startPx;
            _marqueeMoved = false;
            _marqueeOp = op;
            _lasso = _selectMode == SelectMode.Lasso ? new List<Vector2> { start } : null;
            _lassoGapSq = 16f;
        }

        bool UpdateMarquee(Vector2 now, Vector2 nowPx)
        {
            bool changed = _lasso == null && now != _marqueeNow;
            _marqueeNow = now;
            if ((nowPx - _marqueeStartPx).sqrMagnitude >= MarqueeMinPixels * MarqueeMinPixels) _marqueeMoved = true;
            if (_lasso != null && (nowPx - _lassoLastPx).sqrMagnitude >= _lassoGapSq)
            {
                if (_lasso.Count >= 512)
                {
                    for (int i = _lasso.Count - 1; i >= 1; i -= 2) _lasso.RemoveAt(i);
                    _lassoGapSq *= 4f;
                }
                _lasso.Add(now);
                _lassoLastPx = nowPx;
                changed = true;
            }
            return changed;
        }

        void CancelMarquee()
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;
            _lasso = null;
            ClearScenePiecePreview();
            if (!_marqueeInScene || (_sceneMarqueeControl != 0 && GUIUtility.hotControl == _sceneMarqueeControl))
                GUIUtility.hotControl = 0;
            _sceneMarqueeView = null;
            _sceneMarqueeControl = 0;
        }

        void EndMarqueeCanvas()
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;
            var lasso = _lasso;
            _lasso = null;
            if (_uv == null) return;
            if (!_marqueeMoved) { ApplyOp(_uv.IslandsAtUV(_marqueeStart), _marqueeOp); return; }
            if (lasso != null && lasso.Count < 3) ApplyOp(_uv.IslandsAtUV(_marqueeStart), _marqueeOp);
            else if (lasso != null) SelectLassoUV(lasso, _marqueeOp);
            else SelectBoxUV(_marqueeStart, _marqueeNow, _marqueeOp);
        }

        void EndMarqueeScene(Camera camera)
        {
            if (!_marqueeActive) return;
            var lasso = _lasso;
            var op = _marqueeOp;
            List<int> picked = null;
            int hidden = 0;
            if (_marqueeMoved && (lasso == null || lasso.Count >= 3))
                picked = SceneMarqueeCandidates(camera, out hidden);
            CancelMarquee();
            if (!_marqueeMoved || (lasso != null && lasso.Count < 3))
            {
                ScenePickClick(HandleUtility.GUIPointToWorldRay(_marqueeStart), op == SelectOp.Add, op == SelectOp.Subtract);
                return;
            }
            if (picked != null) ApplyOp(picked, op);
            ReportHiddenPieces(hidden);
        }

        List<int> SceneMarqueeCandidates(Camera camera, out int hidden)
        {
            hidden = 0;
            var lasso = _lasso;
            Rect shape = lasso != null ? BoundsOf(lasso)
                : Rect.MinMaxRect(Mathf.Min(_marqueeStart.x, _marqueeNow.x), Mathf.Min(_marqueeStart.y, _marqueeNow.y),
                                  Mathf.Max(_marqueeStart.x, _marqueeNow.x), Mathf.Max(_marqueeStart.y, _marqueeNow.y));
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var view = new Rect(0f, 0f, camera.pixelWidth / ppp, camera.pixelHeight / ppp);
            var clipped = Rect.MinMaxRect(Mathf.Max(shape.xMin, view.xMin), Mathf.Max(shape.yMin, view.yMin), Mathf.Min(shape.xMax, view.xMax), Mathf.Min(shape.yMax, view.yMax));
            if (clipped.width < 1f || clipped.height < 1f) return null;
            Func<Vector2, bool> inside = lasso != null ? LassoScanline(lasso) : (Func<Vector2, bool>)shape.Contains;
            return PiecesInSceneShape(camera, clipped, inside, out hidden);
        }

        static Func<Vector2, bool> LassoScanline(List<Vector2> path)
        {
            var crossings = new List<float>(path.Count);
            float previousY = float.NaN;
            int next = 0;
            return p =>
            {
                if (p.y != previousY)
                {
                    previousY = p.y;
                    crossings.Clear();
                    for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
                    {
                        var a = path[i];
                        var b = path[j];
                        if ((a.y > p.y) != (b.y > p.y)) crossings.Add((b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x);
                    }
                    crossings.Sort();
                    next = 0;
                }
                while (next < crossings.Count && p.x >= crossings[next]) next++;
                return ((crossings.Count - next) & 1) != 0;
            };
        }

        void DrawBand(Vector2 a, Vector2 b, List<Vector2> lassoPx)
        {
            var orange = StudioGUI.Accent;
            Handles.color = orange;
            if (lassoPx == null)
            {
                var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                EditorGUI.DrawRect(r, new Color(orange.r, orange.g, orange.b, 0.10f));
                Handles.DrawAAPolyLine(2f, new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin), new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax), new Vector3(r.xMin, r.yMin));
                return;
            }
            if (lassoPx.Count < 2) return;
            var pts = new Vector3[lassoPx.Count + 1];
            for (int i = 0; i < lassoPx.Count; i++) pts[i] = lassoPx[i];
            pts[lassoPx.Count] = lassoPx[0];
            Handles.DrawAAPolyLine(2f, pts);
        }

        void DrawMarqueeScene()
        {
            if (!_marqueeActive || !_marqueeInScene || !_marqueeMoved || Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            DrawBand(_marqueeStart, _marqueeNow, _lasso);
            Handles.EndGUI();
        }

        void DrawMarqueeCanvas(Rect canvas)
        {
            if (!_marqueeActive || _marqueeInScene || !_marqueeMoved || _comp == null || Event.current.type != EventType.Repaint) return;
            GUI.BeginClip(canvas);
            List<Vector2> path = null;
            if (_lasso != null) path = _lasso.Select(UVToCanvas).ToList();
            DrawBand(UVToCanvas(_marqueeStart), UVToCanvas(_marqueeNow), path);
            GUI.EndClip();
        }

        bool CanvasSelectDown(Vector2 uv, Event e)
        {
            var op = SelectOpOf(e);
            switch (_selectMode)
            {
                case SelectMode.Box:
                case SelectMode.Lasso:
                    BeginMarquee(uv, e.mousePosition, false, op);
                    return true;
                case SelectMode.Wand: WandClick(uv, op); break;
                case SelectMode.Part: SelectPartOf(_uv.IslandsAtUV(uv), op); break;
                case SelectMode.Tile: SelectTilesOf(_uv.IslandsAtUV(uv), op); break;
                case SelectMode.Face:
                case SelectMode.FaceLoop:
                case SelectMode.Vertex:
                case SelectMode.Edge:
                case SelectMode.BoundaryLoop: PickFacesOnTexture(uv, op); break;
                default: ApplyOp(_uv.IslandsAtUV(uv), op); break;
            }
            return false;
        }

        internal List<int> WandAt(Vector2 uv, SelectOp op, int clickedIsland = -1)
        {
            var picked = WandCandidates(uv, clickedIsland);
            if (picked != null) ApplyOp(picked, op);
            else if (_comp != null && _uv != null)
                Toast(clickedIsland >= 0 ? Loc.T("That piece sits outside the texture, so the Wand has no colour to read there.") :
                    Loc.T("Click inside the texture to use the Wand."), MessageType.Info, 3f);
            return picked ?? new List<int>();
        }

        MeshUV _wandMesh;
        Color32[] _wandSource;
        ushort[] _wandIds;
        int[] _wandMinimum, _wandOwners, _wandClose;
        Color32 _wandSeed;
        int _wandTol, _wandIsland;
        bool _wandJoined;
        List<int> _wandResult;

        List<int> WandCandidates(Vector2 uv, int clickedIsland)
        {
            if (_comp == null || _uv == null || uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return null;
            var canvasPieces = clickedIsland < 0 ? _uv.IslandsAtUV(uv) : null;
            if (canvasPieces?.Count == 0) return canvasPieces;
            int w = _comp.Width, h = _comp.Height;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * w), 0, w - 1), y = Mathf.Clamp(Mathf.FloorToInt(uv.y * h), 0, h - 1);
            var src = _comp.SourcePixels();
            var ids = _comp.IslandIdsExact(_uv);
            var seed = src[y * w + x];
            int tol = Mathf.RoundToInt(Mathf.Clamp01(_wandTolerance) * 255f);
            EnsureWandGeometry(ids, w, h);
            if (clickedIsland >= 0 && _wandResult != null && _wandIsland == clickedIsland && _wandSource == src &&
                _wandSeed.Equals(seed) && _wandTol == tol && _wandJoined == _wandContiguous)
                return _wandResult;

            var picked = new List<int>();
            var clicked = canvasPieces ?? new List<int> { clickedIsland };
            CountWandMatches(src, ids, seed, tol, _wandMinimum, _wandClose);
            var owner = _wandOwners;
            bool Matches(int piece) => clicked.Contains(piece) || (owner[piece] >= 0 && _wandClose[owner[piece]] >= _wandMinimum[owner[piece]]);

            if (_wandContiguous)
            {
                var near = _uv.PieceNeighbours();
                var seen = new HashSet<int>(clicked);
                var queue = new Queue<int>(clicked);
                while (queue.Count > 0)
                {
                    int p = queue.Dequeue();
                    picked.Add(p);
                    foreach (int n in near[p])
                        if (!seen.Contains(n) && Matches(n)) { seen.Add(n); queue.Enqueue(n); }
                }
                picked.Sort();
            }
            else
            {
                for (int i = 0; i < _wandMinimum.Length; i++)
                    if (Matches(i)) picked.Add(i);
            }
            _wandSource = src;
            _wandSeed = seed;
            _wandTol = tol;
            _wandIsland = clickedIsland;
            _wandJoined = _wandContiguous;
            _wandResult = picked;
            return picked;
        }

        bool EnsureWandGeometry(ushort[] ids, int width, int height)
        {
            if (_wandMesh == _uv && _wandIds == ids) return false;
            int[] totals, owners;
            if (_comp.TryPreparedWandGeometry(_uv, ids, out totals, out owners)) totals = (int[])totals.Clone();
            else
            {
                totals = new int[_uv.islandCount];
                foreach (int id in ids)
                    if (id > 0 && id <= totals.Length) totals[id - 1]++;
                owners = WandOwners(ids, width, height, totals);
            }
            for (int i = 0; i < totals.Length; i++) totals[i] = Mathf.CeilToInt(totals[i] * WandMinShare);
            _wandMinimum = totals;
            _wandOwners = owners;
            _wandClose = new int[totals.Length];
            _wandMesh = _uv;
            _wandIds = ids;
            _wandResult = null;
            return true;
        }

        static void CountWandMatches(Color32[] pixels, ushort[] ids, Color32 seed, int tolerance, int[] minimum, int[] close)
        {
            Array.Clear(close, 0, close.Length);
            int rMin = seed.r - tolerance, gMin = seed.g - tolerance, bMin = seed.b - tolerance, aMin = seed.a - tolerance;
            uint width = (uint)(2 * tolerance);
            for (int i = 0; i < ids.Length; i++)
            {
                int piece = ids[i] - 1;
                if ((uint)piece >= (uint)close.Length || close[piece] >= minimum[piece]) continue;
                var c = pixels[i];
                if ((uint)(c.r - rMin) <= width && (uint)(c.g - gMin) <= width &&
                    (uint)(c.b - bMin) <= width && (uint)(c.a - aMin) <= width)
                    close[piece]++;
            }
        }

        int[] WandOwners(ushort[] ids, int w, int h, int[] total) => _uv.WandOwners(ids, w, h, total, default);

        internal string WandMaskAt(Vector2 uv, SelectOp op)
        {
            if (_comp == null || _uv == null) return Loc.T("Pick a part and a texture first.");
            if (!TabAvailable(Tab.Paint, out var blocked)) return blocked;
            var layer = ActiveLayer;
            if (layer == null) return Loc.T("Pick or add a layer first.");
            if (layer is MixedMergeLayer) return Loc.T("Separate the merged layer to change its original settings.");
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return Loc.T("Click inside the texture to use the Wand.");
            int w = _comp.Width, h = _comp.Height;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * w), 0, w - 1), y = Mathf.Clamp(Mathf.FloorToInt(uv.y * h), 0, h - 1);
            if (_comp.IslandIds(_uv)[y * w + x] == 0) return Loc.T("That spot is not part of the model, so there is no colour area to mask.");

            EndStroke();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            if (!layer.hasPaintMask) AddPaintMask(layer, op != SelectOp.Subtract);
            Undo.RecordObject(_recipe, "Mask layer by colour");
            if (!layer.enabled) layer.enabled = true;
            bool reveal = op != SelectOp.Subtract;
            layer.maskStrokes.Add(new PaintStroke
            {
                mode = reveal == layer.paintMaskStartsHidden ? BrushMode.Paint : BrushMode.Erase,
                color = Color.white,
                opacity = 1f,
                radius = _brush.size * 0.5f / Mathf.Max(1, _comp.SourceWidth),
                shape = (_brush.shape ?? new BrushShape()).Clone(),
                seed = Guid.NewGuid().GetHashCode(),
                clip = false,
                isFill = true,
                fillWholeIsland = false,
                fillTolerance = _wandTolerance,
                fillAreaScaling = _brush.fillAreaScaling,
                tool = StrokeTool.Brush,
                points = { new Vector4(uv.x, uv.y, 1f, 0f) },
            });
            Undo.SetCurrentGroupName("Mask layer by colour");
            Undo.CollapseUndoOperations(group);
            MarkEdited();
            Learn.Did(LearnEvents.MaskMade);
            return null;
        }

        void WandClick(Vector2 uv, SelectOp op, int clickedIsland = -1)
        {
            if (WandMasksLayer)
            {
                var why = WandMaskAt(uv, op);
                if (why != null) Toast(why, MessageType.Warning, 4f);
                return;
            }
            WandAt(uv, op, clickedIsland);
        }

        internal List<int> SelectPartOf(List<int> islands, SelectOp op)
        {
            var list = new List<int>();
            foreach (var r in islands.Select(RendererOfIsland).Where(r => r != null).Distinct())
                list.AddRange(IslandsOfRenderer(r));
            list = list.Distinct().OrderBy(i => i).ToList();
            ApplyOp(list, op);
            return list;
        }

        MeshUV _tileCandidateMesh;
        Vector2Int _candidateTile;
        List<int> _tileCandidates;

        List<int> TileCandidates(Vector2Int tile)
        {
            if (_tileCandidates != null && _tileCandidateMesh == _uv && _candidateTile == tile) return _tileCandidates;
            var list = new List<int>();
            if (_uv != null)
            {
                var tiles = _uv.PieceTiles();
                for (int i = 0; i < tiles.Length; i++) if (tiles[i] == tile) list.Add(i);
            }
            _tileCandidateMesh = _uv;
            _candidateTile = tile;
            return _tileCandidates = list;
        }

        internal List<int> SelectTile(Vector2Int tile, SelectOp op)
        {
            var list = TileCandidates(tile);
            ApplyOp(list, op);
            return list;
        }

        List<int> SelectTilesOf(List<int> islands, SelectOp op)
        {
            var list = new List<int>();
            if (_uv != null && islands.Count > 0)
            {
                var tiles = _uv.PieceTiles();
                var wanted = new HashSet<Vector2Int>(islands.Select(i => tiles[i]));
                for (int i = 0; i < tiles.Length; i++) if (wanted.Contains(tiles[i])) list.Add(i);
            }
            ApplyOp(list, op);
            return list;
        }

        void SceneSelectClick(Ray ray, SelectMode mode, SelectOp op, SceneView scene)
        {
            bool seen = RaycastVisibleWithUvStatus(ray, out var hit, out var covered, out var changedUv);
            if (changedUv) { Toast(IncludedUvChangedHint, MessageType.Info, 4f); return; }
            if (covered != null)
            {
                Toast(mode == SelectMode.Wand ? Loc.T("That part uses another texture. Pick it in 1 · Pick to use the wand there.") : CoveredMessage(covered), MessageType.Info, 4f);
                return;
            }
            var under = seen ? new List<int> { hit.island } : new List<int>();
            switch (mode)
            {
                case SelectMode.Wand:
                    if (seen) WandClick(hit.uv, op, hit.island);
                    else ApplyOp(under, op);
                    break;
                case SelectMode.Part: ApplyOp(seen ? ScenePartCandidates(hit) : under, op); break;
                case SelectMode.Tile: ApplyOp(seen ? TileCandidates(_uv.PieceTiles()[hit.island]) : under, op); break;
                case SelectMode.Vertex:
                case SelectMode.Edge:
                    if (seen) PickFacesOnScene(hit, op, scene);
                    else ApplyTopologyPick(default, op, scene);
                    break;
                case SelectMode.Face:
                case SelectMode.FaceLoop:
                case SelectMode.BoundaryLoop:
                    if (seen) PickFacesOnScene(hit, op, scene);
                    else ApplyFaceSelection(Array.Empty<int>(), op);
                    break;
                default: ApplyOp(under, op); break;
            }
        }

        static string TileLabel(Vector2Int t) => t.x >= 0 && t.x <= 9 && t.y >= 0 ? (1001 + t.x + 10 * t.y).ToString() : $"({t.x}, {t.y})";

        struct TileCell { public Vector2Int tile; public Rect rect; public int count; }
        readonly List<TileCell> _tileCells = new List<TileCell>();

        bool HasOtherTiles => _uv != null && _uv.TileCounts().Keys.Any(t => t != Vector2Int.zero);

        bool BuildTileGrid(Rect canvas)
        {
            _tileCells.Clear();
            if (_uv == null || _tool != Tool.Select || !HasOtherTiles) return false;
            var counts = _uv.TileCounts();
            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (var t in counts.Keys)
            {
                minX = Mathf.Min(minX, t.x); maxX = Mathf.Max(maxX, t.x);
                minY = Mathf.Min(minY, t.y); maxY = Mathf.Max(maxY, t.y);
            }
            int columns = maxX - minX + 1, rows = maxY - minY + 1;
            if (columns > 4 || rows > 4) return false;
            const float cell = 18f, pad = 8f;
            float x0 = canvas.xMax - pad - columns * cell, y0 = canvas.y + pad;
            for (int ty = maxY; ty >= minY; ty--)
                for (int tx = minX; tx <= maxX; tx++)
                {
                    var tile = new Vector2Int(tx, ty);
                    counts.TryGetValue(tile, out int n);
                    _tileCells.Add(new TileCell { tile = tile, count = n, rect = new Rect(x0 + (tx - minX) * cell, y0 + (maxY - ty) * cell, cell - 1f, cell - 1f) });
                }
            return true;
        }

        bool TileGridClick(Rect canvas, Event e)
        {
            if (!BuildTileGrid(canvas)) return false;
            foreach (var c in _tileCells)
            {
                if (!c.rect.Contains(e.mousePosition)) continue;
                if (c.count > 0) SelectTile(c.tile, SelectOpOf(e));
                return true;
            }
            return false;
        }

        void DrawTileGrid(Rect canvas)
        {
            if (!BuildTileGrid(canvas)) return;
            var first = _tileCells[0].rect;
            var last = _tileCells[_tileCells.Count - 1].rect;
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(Rect.MinMaxRect(first.xMin - 3f, first.yMin - 3f, last.xMax + 3f, last.yMax + 3f), new Color(0.09f, 0.09f, 0.09f, 0.85f));
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 9, padding = new RectOffset(0, 0, 0, 0) };
            foreach (var c in _tileCells)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(c.rect, c.count > 0 ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, 0.04f));
                    if (c.tile == Vector2Int.zero) StudioGUI.DrawBorder(c.rect, StudioGUI.Accent, 1f);
                }
                GUI.Label(c.rect, new GUIContent(c.count > 0 ? c.count.ToString() : "", Loc.F("Tile {0} · {1} piece{2}", TileLabel(c.tile), c.count, (c.count == 1 ? "" : "s"))), style);
            }
        }

        static GUIContent SelectModeContent(SelectMode mode)
        {
            string tip;
            switch (mode)
            {
                case SelectMode.Click: tip = Loc.T("Click: select the piece under the pointer. Shift adds, Ctrl removes."); break;
                case SelectMode.Box: tip = Loc.T("Box: drag a rectangle to select every piece it touches. On the model it skips pieces that other parts cover."); break;
                case SelectMode.Lasso: tip = Loc.T("Lasso: drag a free shape to select every piece it touches. On the model it skips pieces that other parts cover."); break;
                case SelectMode.Wand: tip = Loc.T("Wand: click a colour to select the pieces that have it. It compares the original texture colours."); break;
                case SelectMode.Part: tip = Loc.T("Select whole part: click a piece to select every piece of its part."); break;
                case SelectMode.Face: tip = Loc.T("Select an individual triangle of the UV mesh."); break;
                case SelectMode.BoundaryLoop: tip = Loc.T("Select the faces along one closed UV boundary. Rims that meet only at a point stay separate."); break;
                case SelectMode.FaceLoop: tip = FaceLoopTip; break;
                case SelectMode.Vertex: tip = Loc.T("Select the UV faces that touch a vertex. Paint selected uses those faces as its limit."); break;
                case SelectMode.Edge: tip = Loc.T("Select the UV faces that touch a triangle edge. Paint selected uses those faces as its limit."); break;
                default: tip = Loc.T("Tile: click a piece to select every piece in the same UV tile."); break;
            }
            return new GUIContent(SelectModeNames[(int)mode], tip);
        }

        string SelectModeHint(bool scene = false)
        {
            switch (_selectMode)
            {
                case SelectMode.Box: return Loc.T("Drag a box: it selects every piece it touches. Shift adds, Ctrl removes.");
                case SelectMode.Lasso: return Loc.T("Drag a free shape around pieces. Shift adds, Ctrl removes.");
                case SelectMode.Wand: return WandMasksLayer
                    ? WandHides ? Loc.T("Click a colour to hide its area. Shift-click reveals it.")
                        : Loc.T("Click a colour to reveal its area. Ctrl-click hides it.")
                    : Loc.T("Click a colour to select the pieces that have it. Shift adds, Ctrl removes.");
                case SelectMode.Part: return Loc.T("Click a piece to select every piece of its part. Shift adds, Ctrl removes.");
                case SelectMode.Tile: return Loc.T("Click a piece to select every piece in its UV tile. Shift adds, Ctrl removes.");
                case SelectMode.Face: return scene
                    ? Loc.T("Click a triangle on the model. Shift adds, Ctrl removes. Shared pixels are painted together.")
                    : Loc.T("Click a triangle on the texture. Shift adds, Ctrl removes. Shared pixels are painted together.");
                case SelectMode.BoundaryLoop: return scene
                    ? Loc.T("Click a face beside a closed UV boundary on the model. Shift adds, Ctrl removes.")
                    : Loc.T("Click a face beside a closed UV boundary on the texture. Shift adds, Ctrl removes.");
                case SelectMode.FaceLoop: return FaceLoopHint;
                case SelectMode.Vertex: return Loc.T("Click near a vertex to select every face it touches. Shift adds, Ctrl removes.");
                case SelectMode.Edge: return Loc.T("Click near an edge to select every face it touches. Shift adds, Ctrl removes.");
                default: return scene ? Loc.T("Click pieces on the model. Shift adds, Ctrl removes.")
                    : Loc.T("Click pieces on the texture. Shift adds, Ctrl removes.");
            }
        }

        int SelectionSettingsGrid(string control, int current, GUIContent[] choices, float width)
        {
            string hint = Loc.T("Click a choice, then use arrow keys or Shift+scroll to switch.");
            foreach (var choice in choices) choice.tooltip += "\n" + hint;
            int next = WrapToolbar(current, choices, EditorStyles.miniButton, 20f,
                width > 0f ? width : PanelContentWidth, navigate: true);
            StudioGUI.Count(control, GUILayoutUtility.GetLastRect());
            return next;
        }

        bool WandMasksLayer => _selectMode == SelectMode.Wand && !PickingForPaint && _wandOutput == WandOutput.LayerMask;
        bool WandHides => _wandMaskAction == WandMaskAction.Hide ||
            (_wandMaskAction == WandMaskAction.PreviousOperation && _selectOp == SelectOp.Subtract);

        void DrawSelectModeOptions(float width = -1f)
        {
            if (_selectMode != SelectMode.Wand) return;
            float percent = EditorGUILayout.Slider(new GUIContent(Loc.T("Tolerance"), Loc.T("How different a colour may be and still match. 0 = only the exact colour. Click again after changing it.")), _wandTolerance * 100f, 0f, 100f);
            if (!Mathf.Approximately(percent, _wandTolerance * 100f)) { _wandTolerance = percent / 100f; SelectUiChanged(); }

            if (!PickingForPaint && (Advanced || WandMasksLayer))
            {
                GUILayout.Label(Loc.T("Wand output"), StudioGUI.Hint, GUILayout.MinWidth(0f));
                var outputs = new[]
                {
                    new GUIContent(Loc.T("Pieces"), Loc.T("Select the pieces that have the colour.")),
                    new GUIContent(Loc.T("Layer mask"), Loc.T("Mask the current layer to the colour area you click, pixel by pixel.")),
                };
                int now = SelectionSettingsGrid("limit.wandOutput", (int)_wandOutput, outputs, width);
                if (now != (int)_wandOutput)
                    ChangeSceneBar("Set Wand output", () =>
                    {
                        _wandOutput = (WandOutput)now;
                        if (_wandOutput == WandOutput.LayerMask && _wandMaskAction == WandMaskAction.PreviousOperation)
                            _wandMaskAction = WandMaskAction.Reveal;
                    });
            }
            if (WandMasksLayer)
            {
                if (!TabAvailable(Tab.Paint, out var blocked))
                {
                    GUILayout.Label(blocked, StudioGUI.Hint, GUILayout.MinWidth(0f));
                    if (GUILayout.Button(Msg.BtnGoToPick, EditorStyles.miniButton)) SetTab(Tab.Target);
                }
                else if (ActiveLayer is Layer layer)
                    GUILayout.Label(Loc.F("Masking ‘{0}’. Each click affects one connected colour area.", layer.name), StudioGUI.Hint, GUILayout.MinWidth(0f));
                else
                {
                    GUILayout.Label(Loc.T("Pick or add a layer first."), StudioGUI.Hint);
                    if (GUILayout.Button(Loc.T("Go to Paint"), EditorStyles.miniButton)) OpenPaintLayers();
                }
            }
            else
            {
                bool contiguous = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Connected pieces only"),
                    Loc.T("On: only pieces joined to the one you click, through pieces of that colour. Off: every piece with that colour.")), _wandContiguous);
                if (contiguous != _wandContiguous) { _wandContiguous = contiguous; SelectUiChanged(); }
                GUILayout.Label(Loc.T("Wand compares the original texture colours."), StudioGUI.Hint);
            }
        }
    }
}
