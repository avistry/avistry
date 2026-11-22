using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    // Append-only: the numbers are saved in users' window layouts. Never renumber or reuse them.
    internal enum SelectMode { Click = 0, Box = 1, Lasso = 2, Wand = 3, Part = 4, Tile = 5 }
    internal enum SelectOp { Replace = 0, Add = 1, Subtract = 2 }
    internal enum WandOutput { Pieces = 0, LayerMask = 1 }

    /// <summary>
    /// Ways to select pieces: Click, Box, Lasso, Wand (by colour), Part (the whole part) and Tile (a UV tile), in the texture
    /// view and on the model in the Scene view, plus Grow and Shrink. The panel, the texture view, the Scene view and the
    /// Scene bar all call the methods here, so they can never disagree. Every selection change goes through
    /// ApplySelection or SetSelection, which record one "Select pieces" step.
    /// </summary>
    partial class AvatarTextureWindow
    {
        /// <summary>A piece matches the Wand when at least this share of its texels is close to the clicked colour.</summary>
        const float WandMinShare = 0.05f;
        const float MarqueeMinPixels = 3f;

        [SerializeField] SelectMode _selectMode;
        [SerializeField] SelectOp _selectOp;                // Shift and Ctrl override it for one click
        [SerializeField] float _wandTolerance = 0.15f;      // 0 to 1, the same scale as the Fill tool's tolerance
        [SerializeField] bool _wandContiguous = true;
        [SerializeField] WandOutput _wandOutput;

        // The drag in progress. Texture view: points are UVs, so zooming or panning during the drag changes nothing.
        // Scene view: points are GUI points.
        bool _marqueeActive, _marqueeInScene, _marqueeMoved;
        Vector2 _marqueeStart, _marqueeNow, _marqueeStartPx, _lassoLastPx;
        float _lassoGapSq = 16f;
        List<Vector2> _lasso;
        SelectOp _marqueeOp;

        static string[] SelectModeNames => new[] { Loc.T("Click"), Loc.T("Box"), Loc.T("Lasso"), Loc.T("Wand"), Loc.T("Part"), Loc.T("Tile") };

        /// <summary>Shift adds, Ctrl (Cmd on macOS) removes, otherwise the Replace / Add / Subtract switch decides.</summary>
        SelectOp SelectOpOf(Event e) => e.shift ? SelectOp.Add : (e.control || e.command) ? SelectOp.Subtract : _selectOp;

        void ApplyOp(List<int> islands, SelectOp op) => ApplySelection(islands, op == SelectOp.Add, op == SelectOp.Subtract);

        /// <summary>Picks a selection mode. From the Scene bar it also turns on picking in the Scene view, which is what the bar is for.</summary>
        internal void SetSelectMode(SelectMode mode, bool pickInScene = false)
        {
            SetTool(Tool.Select);
            CancelMarquee();
            _selectMode = mode;
            _hoverIsland = -1; // Box and Lasso do not update the hover, so an old one would stay drawn
            if (pickInScene && !_pickInScene) _pickInScene = true;
            Repaint();
            SceneView.RepaintAll();
        }

        void SelectUiChanged()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        void SelectEverything() { if (_uv != null) SetSelection(Enumerable.Range(0, _uv.islandCount)); }
        void SelectNothing() => SetSelection(Enumerable.Empty<int>());
        void InvertSelection() { if (_uv != null) { var now = new HashSet<int>(_selection); SetSelection(Enumerable.Range(0, _uv.islandCount).Where(i => !now.Contains(i))); } }

        /// <summary>Grow (+1) adds the pieces that touch the selection on the model. Shrink (-1) removes the selected pieces that touch an unselected one.</summary>
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

        /// <summary>
        /// Selects the pieces a shape touches in the texture: a piece counts when the centre of any of its triangles is inside,
        /// or when the shape's centre is on the piece (so a box drawn inside one big piece still picks it).
        /// </summary>
        internal List<int> SelectByShapeUV(Func<Vector2, bool> inside, Rect bounds, SelectOp op)
        {
            var picked = new List<int>();
            if (_uv == null) return picked;
            var seen = new HashSet<int>();
            var uv = _uv.uv;
            var tris = _uv.tris;
            for (int t = 0; t < _uv.triIsland.Length; t++)
            {
                Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c0 = uv[tris[t * 3 + 2]];
                var c = (a + b + c0) / 3f;
                if (float.IsNaN(c.x)) continue;
                // The centre is the fast test; a corner inside also counts, so a big triangle the shape only clips still picks its piece.
                bool hit = (bounds.Contains(c) && inside(c)) || (bounds.Contains(a) && inside(a)) || (bounds.Contains(b) && inside(b)) || (bounds.Contains(c0) && inside(c0));
                if (!hit) continue;
                if (seen.Add(_uv.triIsland[t])) picked.Add(_uv.triIsland[t]);
            }
            if (inside(bounds.center))
                foreach (int i in _uv.IslandsAtUV(bounds.center))
                    if (seen.Add(i)) picked.Add(i);
            picked.Sort();
            ApplyOp(picked, op);
            return picked;
        }

        List<int> SelectBoxUV(Vector2 a, Vector2 b, SelectOp op)
        {
            var box = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return SelectByShapeUV(p => true, box, op);
        }

        List<int> SelectLassoUV(List<Vector2> poly, SelectOp op)
        {
            if (poly == null || poly.Count < 3) return SelectByShapeUV(p => false, new Rect(), op);
            return SelectByShapeUV(p => InPolygon(poly, p), BoundsOf(poly), op);
        }

        static Rect BoundsOf(List<Vector2> pts)
        {
            var min = pts[0]; var max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
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

        /// <summary>
        /// Box or Lasso in the Scene view: the pieces with a visible spot inside the shape. Parts that cover the edited surface
        /// hide what is behind them, and so does the model itself. Returns the reason it could not run, or null.
        /// </summary>
        internal string SelectByShapeScene(Camera cam, Rect guiBounds, Func<Vector2, bool> inside, SelectOp op, out int count)
        {
            count = 0;
            if (_uv == null || cam == null) return Loc.T("Nothing to select: choose a target first.");
            if (_uv.PoseChanged()) _uv.RefreshPose();
            var blockers = new List<SceneMarquee.Blocker>();
            foreach (var (r, m, mu) in BlockerSlots())
                blockers.Add(new SceneMarquee.Blocker { verts = mu.PosedCombinedCached(), tris = mu.tris, doubleSided = IsDoubleSided(m), mirrored = r.localToWorldMatrix.determinant < 0f });
            // Each edited part is single-sided unless one of its materials draws both sides, like Click's raycast.
            bool PartDoubleSided(Renderer r) { foreach (var m in r.sharedMaterials) if (m != null && IsDoubleSided(m)) return true; return false; }
            var picked = SceneMarquee.PiecesInShape(_uv, cam, guiBounds, inside, blockers, PartDoubleSided, out int hidden);
            ApplyOp(picked, op);
            count = picked.Count;
            if (hidden > 0)
                Toast(hidden == 1 ? Loc.T("1 piece was hidden behind the model and was left out.") : Loc.F("{0} pieces were hidden behind the model and were left out.", hidden), MessageType.Info, 4f);
            return null;
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

        void UpdateMarquee(Vector2 now, Vector2 nowPx)
        {
            _marqueeNow = now;
            if ((nowPx - _marqueeStartPx).sqrMagnitude >= MarqueeMinPixels * MarqueeMinPixels) _marqueeMoved = true;
            // At least 4 pixels apart and at most 512 points, so the shape test at the end stays cheap. A long path drops every
            // other point and doubles the spacing, so the whole loop is kept instead of stopping early.
            if (_lasso != null && (nowPx - _lassoLastPx).sqrMagnitude >= _lassoGapSq)
            {
                if (_lasso.Count >= 512)
                {
                    for (int i = _lasso.Count - 1; i >= 1; i -= 2) _lasso.RemoveAt(i);
                    _lassoGapSq *= 4f;
                }
                _lasso.Add(now);
                _lassoLastPx = nowPx;
            }
        }

        void CancelMarquee()
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;
            _lasso = null;
            GUIUtility.hotControl = 0;
        }

        /// <summary>The drag in the texture view ended. A drag under 3 pixels is a plain click.</summary>
        void EndMarqueeCanvas()
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;
            var lasso = _lasso;
            _lasso = null;
            if (_uv == null) return;
            if (!_marqueeMoved) { ApplyOp(_uv.IslandsAtUV(_marqueeStart), _marqueeOp); return; }
            if (lasso != null && lasso.Count < 3) ApplyOp(_uv.IslandsAtUV(_marqueeStart), _marqueeOp); // too short to be a shape: a click
            else if (lasso != null) SelectLassoUV(lasso, _marqueeOp);
            else SelectBoxUV(_marqueeStart, _marqueeNow, _marqueeOp);
        }

        /// <summary>The drag in the Scene view ended.</summary>
        void EndMarqueeScene(SceneView sv)
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;
            var lasso = _lasso;
            _lasso = null;
            var op = _marqueeOp;
            if (!_marqueeMoved || (lasso != null && lasso.Count < 3))
            {
                ScenePickClick(HandleUtility.GUIPointToWorldRay(_marqueeStart), op == SelectOp.Add, op == SelectOp.Subtract);
                return;
            }
            Rect shape = lasso != null && lasso.Count >= 3 ? BoundsOf(lasso)
                : Rect.MinMaxRect(Mathf.Min(_marqueeStart.x, _marqueeNow.x), Mathf.Min(_marqueeStart.y, _marqueeNow.y),
                                  Mathf.Max(_marqueeStart.x, _marqueeNow.x), Mathf.Max(_marqueeStart.y, _marqueeNow.y));
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var view = new Rect(0f, 0f, sv.camera.pixelWidth / ppp, sv.camera.pixelHeight / ppp);
            var clipped = Rect.MinMaxRect(Mathf.Max(shape.xMin, view.xMin), Mathf.Max(shape.yMin, view.yMin), Mathf.Min(shape.xMax, view.xMax), Mathf.Min(shape.yMax, view.yMax));
            if (clipped.width < 1f || clipped.height < 1f) return;
            Func<Vector2, bool> inside = lasso != null && lasso.Count >= 3 ? (Func<Vector2, bool>)(p => InPolygon(lasso, p)) : (p => shape.Contains(p));
            SelectByShapeScene(sv.camera, clipped, inside, op, out _);
        }

        // The orange band: a rectangle with a faint fill, or the lasso path closed back to its start.
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

        /// <summary>A left click on the texture in the Select tool. True when it starts a drag (Box and Lasso) that needs the mouse until it is released.</summary>
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
                default: ApplyOp(_uv.IslandsAtUV(uv), op); break;
            }
            return false;
        }

        /// <summary>
        /// The Wand on a texture point: selects the pieces that have the colour of the original texture at that point.
        /// A piece matches when WandMinShare of its texels is close to it. With Contiguous on, only pieces connected to the
        /// clicked one through matching pieces on the model. Returns the pieces it picked.
        /// </summary>
        internal List<int> WandAt(Vector2 uv, SelectOp op, int clickedIsland = -1)
        {
            var picked = new List<int>();
            if (_comp == null || _uv == null) return picked;
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) { Toast(clickedIsland >= 0 ? Loc.T("That piece sits outside the texture, so the Wand has no colour to read there.") : Loc.T("Click inside the texture to use the Wand."), MessageType.Info, 3f); return picked; }
            int w = _comp.Width, h = _comp.Height;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * w), 0, w - 1), y = Mathf.Clamp(Mathf.FloorToInt(uv.y * h), 0, h - 1);
            var clicked = clickedIsland >= 0 ? new List<int> { clickedIsland } : _uv.IslandsAtUV(uv);
            if (clicked.Count == 0) { ApplyOp(picked, op); return picked; } // off the model: like Click, replace clears

            var src = _comp.SourcePixels();
            var ids = _comp.IslandIdsExact(_uv); // not the padded ids: the colour around a piece must not decide whether it matches
            var seed = src[y * w + x];
            int tol = Mathf.RoundToInt(Mathf.Clamp01(_wandTolerance) * 255f);
            var total = new int[_uv.islandCount];
            var close = new int[_uv.islandCount];
            for (int i = 0; i < ids.Length; i++)
            {
                int piece = ids[i] - 1;
                if (piece < 0 || piece >= total.Length) continue;
                total[piece]++;
                if (Compositor.Close(src[i], seed, tol)) close[piece]++;
            }
            var owner = WandOwners(ids, w, h, total);
            bool Matches(int piece) => clicked.Contains(piece) || (owner[piece] >= 0 && close[owner[piece]] >= total[owner[piece]] * WandMinShare);

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
                for (int i = 0; i < total.Length; i++)
                    if (Matches(i)) picked.Add(i);
            }
            ApplyOp(picked, op);
            return picked;
        }

        /// <summary>
        /// Which piece's texels speak for each piece. A piece that has texels of its own speaks for itself. A stacked or
        /// mirrored twin loses every shared texel to the piece rasterised after it, so it takes the piece that most of its
        /// triangle centres land on. -1 = no texels at all.
        /// </summary>
        int[] WandOwners(ushort[] ids, int w, int h, int[] total)
        {
            var owner = new int[total.Length];
            Dictionary<int, Dictionary<int, int>> votes = null;
            for (int i = 0; i < owner.Length; i++) owner[i] = total[i] > 0 ? i : -1;
            var uv = _uv.uv; var tris = _uv.tris;
            for (int t = 0; t < _uv.triIsland.Length; t++)
            {
                int piece = _uv.triIsland[t];
                if (piece < 0 || piece >= owner.Length || total[piece] > 0) continue;
                var c = (uv[tris[t * 3]] + uv[tris[t * 3 + 1]] + uv[tris[t * 3 + 2]]) / 3f;
                if (float.IsNaN(c.x) || c.x < 0f || c.y < 0f || c.x >= 1f || c.y >= 1f) continue;
                int under = ids[Mathf.FloorToInt(c.y * h) * w + Mathf.FloorToInt(c.x * w)] - 1;
                if (under < 0 || under >= owner.Length || total[under] == 0) continue;
                votes ??= new Dictionary<int, Dictionary<int, int>>();
                if (!votes.TryGetValue(piece, out var tally)) votes[piece] = tally = new Dictionary<int, int>();
                tally.TryGetValue(under, out int n);
                tally[under] = n + 1;
            }
            if (votes != null)
                foreach (var kv in votes)
                {
                    int best = -1, bestN = 0;
                    foreach (var t in kv.Value) if (t.Value > bestN) { best = t.Key; bestN = t.Value; }
                    owner[kv.Key] = best;
                }
            return owner;
        }

        /// <summary>
        /// The Wand with Layer mask output: masks the current layer to the colour area that was clicked, pixel by pixel. It
        /// adds a Fill stroke to the layer's mask, so it replays at save size like any mask stroke. Returns why it could not, or null.
        /// </summary>
        internal string WandMaskAt(Vector2 uv, SelectOp op)
        {
            if (_comp == null || _uv == null) return Loc.T("Pick a part and a texture first.");
            var layer = ActiveLayer;
            if (layer == null) return Loc.T("Pick or add a layer first.");
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return Loc.T("Click inside the texture to use the Wand.");
            int w = _comp.Width, h = _comp.Height;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * w), 0, w - 1), y = Mathf.Clamp(Mathf.FloorToInt(uv.y * h), 0, h - 1);
            if (_comp.IslandIds(_uv)[y * w + x] == 0) return Loc.T("That spot is not part of the model, so there is no colour area to mask.");

            EndStroke();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            // A layer that already has a mask keeps it, hand-painted strokes included: every Wand click adds an area to it.
            // Without a mask, Add and Replace start hidden; Subtract starts shown and hides the area.
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

        /// <summary>A Wand click in the texture view or the Scene view, with the output the user chose.</summary>
        void WandClick(Vector2 uv, SelectOp op, int clickedIsland = -1)
        {
            if (_wandOutput == WandOutput.LayerMask)
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

        internal List<int> SelectTile(Vector2Int tile, SelectOp op)
        {
            var list = new List<int>();
            if (_uv != null)
            {
                var tiles = _uv.PieceTiles();
                for (int i = 0; i < tiles.Length; i++) if (tiles[i] == tile) list.Add(i);
            }
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

        /// <summary>A click on the model for Wand, Part or Tile: what the visible surface under the pointer is, or what covers it.</summary>
        void SceneSelectClick(Ray ray, SelectMode mode, SelectOp op)
        {
            bool seen = RaycastVisible(ray, out var hit, out var covered);
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
                case SelectMode.Part: SelectPartOf(under, op); break;
                case SelectMode.Tile: SelectTilesOf(under, op); break;
                default: ApplyOp(under, op); break;
            }
        }

        static string TileLabel(Vector2Int t) => t.x >= 0 && t.x <= 9 && t.y >= 0 ? (1001 + t.x + 10 * t.y).ToString() : $"({t.x}, {t.y})";

        struct TileCell { public Vector2Int tile; public Rect rect; public int count; }
        readonly List<TileCell> _tileCells = new List<TileCell>();

        bool HasOtherTiles => _uv != null && _uv.TileCounts().Keys.Any(t => t != Vector2Int.zero);

        /// <summary>The small tile grid at the top right of the texture view. Only when a piece lies outside the texture's own tile, and only up to 4 × 4 tiles.</summary>
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
            if (columns > 4 || rows > 4) return false; // too many tiles for a grid: the panel lists them
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

        /// <summary>A click on a tile cell selects that tile, in any select mode. True when the click was on the grid.</summary>
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
                    if (c.tile == Vector2Int.zero) StudioGUI.DrawBorder(c.rect, StudioGUI.Accent, 1f); // this texture
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
                default: tip = Loc.T("Tile: click a piece to select every piece in the same UV tile."); break;
            }
            // Text for all six: only four modes have a built-in icon, and a mixed strip looks uneven.
            return new GUIContent(SelectModeNames[(int)mode], tip);
        }

        /// <summary>One line that says what the current mode does.</summary>
        string SelectModeHint()
        {
            switch (_selectMode)
            {
                case SelectMode.Box: return Loc.T("Drag a box: it selects every piece it touches. Shift adds, Ctrl removes.");
                case SelectMode.Lasso: return Loc.T("Drag a free shape around pieces. Shift adds, Ctrl removes.");
                case SelectMode.Wand: return _wandOutput == WandOutput.LayerMask
                    ? Loc.T("Click a colour to add its area to the current layer's mask. Ctrl removes an area.")
                    : Loc.T("Click a colour to select the pieces that have it. Shift adds, Ctrl removes.");
                case SelectMode.Part: return Loc.T("Click a piece to select every piece of its part. Shift adds, Ctrl removes.");
                case SelectMode.Tile: return Loc.T("Click a piece to select every piece in its UV tile. Shift adds, Ctrl removes.");
                default: return Loc.T("Click pieces on the texture. Shift adds, Ctrl removes.");
            }
        }

        /// <summary>The mode strip. In the Scene bar any click turns on picking in the Scene view; while it is off no mode shows as active.</summary>
        void DrawSelectModeStrip(bool bar)
        {
            var contents = new GUIContent[SelectModeNames.Length];
            for (int i = 0; i < contents.Length; i++) contents[i] = SelectModeContent((SelectMode)i);
            bool idle = bar && !_pickInScene;
            int shown = idle ? -1 : (int)_selectMode;
            // miniButton, not ToolButtonStyle: a toolbar's middle buttons ignore a copied style's padding and clip "Lasso" to "Lassc".
            int now = WrapToolbar(shown, contents, EditorStyles.miniButton, 20f, bar ? 246f : -1f);
            if (now != shown) SetSelectMode((SelectMode)now, bar);
        }

        /// <summary>Replace, Add or Subtract: for people who do not hold Shift or Ctrl.</summary>
        void DrawSelectOpSwitch()
        {
            var contents = new[]
            {
                new GUIContent(Loc.T("Replace"), Loc.T("A new selection replaces the old one.")),
                new GUIContent(Loc.T("Add"), Loc.T("Add to the selection (the same as holding Shift).")),
                new GUIContent(Loc.T("Subtract"), Loc.T("Take away from the selection (the same as holding Ctrl).")),
            };
            var rect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            int now = GUI.Toolbar(rect, (int)_selectOp, contents, ToolButtonStyle);
            if (now != (int)_selectOp) { _selectOp = (SelectOp)now; SelectUiChanged(); }
        }

        /// <summary>The settings of the current mode. Only the Wand has any.</summary>
        void DrawSelectModeOptions()
        {
            if (_selectMode != SelectMode.Wand) return;
            float percent = EditorGUILayout.Slider(new GUIContent(Loc.T("Tolerance"), Loc.T("How different a colour may be and still match. 0 = only the exact colour. Click again after changing it.")), _wandTolerance * 100f, 0f, 100f);
            if (!Mathf.Approximately(percent, _wandTolerance * 100f)) { _wandTolerance = percent / 100f; SelectUiChanged(); }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_wandOutput == WandOutput.LayerMask))
                {
                    bool contiguous = GUILayout.Toggle(_wandContiguous, new GUIContent(Loc.T("Contiguous"), Loc.T("On: only pieces joined to the one you click, through pieces of that colour. Off: every piece with that colour.")), GUILayout.Width(88f));
                    if (contiguous != _wandContiguous) { _wandContiguous = contiguous; SelectUiChanged(); }
                }
                var outputs = new[]
                {
                    new GUIContent(Loc.T("Pieces"), Loc.T("Select the pieces that have the colour.")),
                    new GUIContent(Loc.T("Layer mask"), Loc.T("Mask the current layer to the colour area you click, pixel by pixel.")),
                };
                var rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                int now = GUI.Toolbar(rect, (int)_wandOutput, outputs, ToolButtonStyle);
                if (now != (int)_wandOutput) { _wandOutput = (WandOutput)now; SelectUiChanged(); }
            }
            GUILayout.Label(_wandOutput == WandOutput.LayerMask
                ? Loc.T("The mask is one connected colour area and stays inside the piece you click. Every click adds another area; Ctrl-click hides one.")
                : Loc.T("Wand compares the original texture colours."), StudioGUI.Hint);
        }
    }
}
