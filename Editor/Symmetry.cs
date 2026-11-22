using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Symmetry painting: mirrors texture points through the 3D model rather than the texture (avatar UVs are rarely laid
    /// out symmetrically). A UV point is found on the posed mesh, reflected across the avatar's mirror plane, snapped to
    /// the nearest surface on the other side, and that surface point's UV is where the mirrored paint goes.
    ///
    /// Line-up: the plane starts at the avatar's own left/right axis and is refined so it sits exactly on the model's
    /// centre. The score is the share of the mesh whose mirror image lands on the mesh again (within 0.5% of its size).
    /// </summary>
    public sealed class SymmetryMap
    {
        public enum Axis
        {
            [InspectorName("Auto (best match)")] Auto = 0,
            [InspectorName("Left ↔ right")] LeftRight = 1,
            [InspectorName("Up ↔ down")] UpDown = 2,
            [InspectorName("Front ↔ back")] FrontBack = 3,
        }

        public enum Result { None, Same, Mirrored }

        public Vector3 normal;          // plane: dot(normal, p) = offset (world space)
        public float offset;
        public Axis axis;               // the axis actually used
        public float score;             // 0..1 share of the mesh that lines up with its mirror
        public float meanError;         // average distance (world units) from a mirrored vertex to the surface
        public float tolerance;         // world units counted as "lines up"
        public float size;              // bounds diagonal (world units)
        public Vector3 centre;          // a point on the plane (for drawing it)
        public Vector3 planeU, planeV;  // half extents of the plane rectangle (for drawing it)

        readonly MeshUV _mesh;
        readonly Vector3[] _pos;
        readonly bool[] _triOk;
        readonly Vector3[] _triNormal;

        // world-space grid of triangles (compressed rows)
        Vector3 _gMin;
        float _cell;
        int _nx, _ny, _nz;
        int[] _cellStart, _cellItems;
        int[] _stamp;
        int _stampId;

        // UV-space grid of triangles
        const int UvGrid = 128;
        int[] _uvStart, _uvItems;

        SymmetryMap(MeshUV mesh)
        {
            _mesh = mesh;
            _pos = mesh.PosedCombined();
            int tc = mesh.TriangleCount;
            _triOk = new bool[tc];
            _triNormal = new Vector3[tc];
            for (int t = 0; t < tc; t++)
            {
                Vector3 a = _pos[mesh.tris[t * 3]], b = _pos[mesh.tris[t * 3 + 1]], c = _pos[mesh.tris[t * 3 + 2]];
                bool ok = !float.IsNaN(a.x) && !float.IsNaN(b.x) && !float.IsNaN(c.x);
                var n = ok ? Vector3.Cross(b - a, c - a) : Vector3.zero;
                _triOk[t] = ok && n.sqrMagnitude > 1e-24f;
                // Not n.normalized: Unity returns zero below a length of 1e-5, which small triangles (in metres) are.
                _triNormal[t] = _triOk[t] ? n * (1f / Mathf.Sqrt(n.sqrMagnitude)) : Vector3.zero;
            }
            _stamp = new int[tc];
            BuildWorldGrid();
            BuildUvGrid();
        }

        /// <summary>Builds the map and finds the mirror plane. Returns null if the mesh has no usable posed geometry.</summary>
        public static SymmetryMap Build(MeshUV mesh, Axis axis)
        {
            if (mesh == null || mesh.TriangleCount == 0) return null;
            var map = new SymmetryMap(mesh);
            if (map._cellStart == null) return null;
            map.DetectPlane(axis);
            return map;
        }


        void DetectPlane(Axis wanted)
        {
            var root = _mesh.AvatarRoot();
            Vector3 right = root != null ? root.right : Vector3.right, up = root != null ? root.up : Vector3.up, fwd = root != null ? root.forward : Vector3.forward;
            var candidates = new List<(Axis axis, Vector3 n)>();
            if (wanted == Axis.Auto || wanted == Axis.LeftRight) candidates.Add((Axis.LeftRight, right));
            if (wanted == Axis.Auto || wanted == Axis.UpDown) candidates.Add((Axis.UpDown, up));
            if (wanted == Axis.Auto || wanted == Axis.FrontBack) candidates.Add((Axis.FrontBack, fwd));

            float bestScore = -1;
            foreach (var (ax, n) in candidates)
            {
                Evaluate(n.normalized, out float off, out float sc, out float err);
                // Avatars are almost always left/right symmetric: only pick another axis if it is clearly better.
                float bias = ax == Axis.LeftRight ? 0.1f : 0f;
                if (sc + bias > bestScore)
                {
                    bestScore = sc + bias;
                    axis = ax; normal = n.normalized; offset = off; score = sc; meanError = err;
                }
                if (wanted == Axis.Auto && ax == Axis.LeftRight && sc >= 0.85f) break; // clearly left/right: done
            }

            // Rectangle for drawing the plane: the mesh bounds projected onto it.
            var b = Bounds();
            centre = b.center - (Vector3.Dot(normal, b.center) - offset) * normal;
            var u = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, up)) < 0.9f ? up : fwd).normalized;
            var v = Vector3.Cross(normal, u).normalized;
            float hu = 0, hv = 0;
            foreach (var c in Corners(b))
            {
                hu = Mathf.Max(hu, Mathf.Abs(Vector3.Dot(c - centre, u)));
                hv = Mathf.Max(hv, Mathf.Abs(Vector3.Dot(c - centre, v)));
            }
            planeU = u * hu * 1.05f;
            planeV = v * hv * 1.05f;
        }

        /// <summary>Finds the plane offset along n that best maps the mesh onto itself, and how well it does.</summary>
        void Evaluate(Vector3 n, out float off, out float sc, out float err)
        {
            var b = Bounds();
            off = Vector3.Dot(n, b.center);
            var samples = SampleVertices(1500);
            float reach = size * 0.03f;
            for (int iter = 0; iter < 4; iter++)
            {
                var est = new List<float>(samples.Count);
                foreach (var p in samples)
                {
                    var m = p - 2f * (Vector3.Dot(n, p) - off) * n;
                    if (!Closest(m, reach, out int t, out var s, out _)) continue;
                    // Only surfaces facing along the axis tell us where the centre is (others just slide along it).
                    if (Mathf.Abs(Vector3.Dot(_triNormal[t], n)) < 0.5f) continue;
                    est.Add((Vector3.Dot(n, p) + Vector3.Dot(n, s)) * 0.5f);
                }
                if (est.Count < 8) break;
                est.Sort();
                float next = est[est.Count / 2];
                bool settled = Mathf.Abs(next - off) < size * 1e-5f;
                off = next;
                if (settled) break;
            }

            int good = 0, found = 0;
            double sum = 0;
            foreach (var p in samples)
            {
                var m = p - 2f * (Vector3.Dot(n, p) - off) * n;
                if (!Closest(m, reach, out _, out _, out float d)) d = reach;
                else found++;
                sum += Mathf.Min(d, reach);
                if (d <= tolerance) good++;
            }
            sc = samples.Count > 0 ? good / (float)samples.Count : 0f;
            err = samples.Count > 0 ? (float)(sum / samples.Count) : reach;
        }

        List<Vector3> SampleVertices(int max)
        {
            var used = new bool[_pos.Length];
            int count = 0;
            for (int t = 0; t < _triOk.Length; t++)
            {
                if (!_triOk[t]) continue;
                for (int k = 0; k < 3; k++) { int i = _mesh.tris[t * 3 + k]; if (!used[i]) { used[i] = true; count++; } }
            }
            var list = new List<Vector3>(Mathf.Min(count, max));
            int stride = Mathf.Max(1, count / max), seen = 0;
            for (int i = 0; i < _pos.Length; i++)
            {
                if (!used[i]) continue;
                if (seen++ % stride == 0) list.Add(_pos[i]);
            }
            return list;
        }

        Bounds Bounds()
        {
            var b = new Bounds();
            bool any = false;
            for (int t = 0; t < _triOk.Length; t++)
            {
                if (!_triOk[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = _pos[_mesh.tris[t * 3 + k]];
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }


        public Vector3 Mirror(Vector3 p) => p - 2f * (Vector3.Dot(normal, p) - offset) * normal;

        /// <summary>
        /// The mirrored texture point. Same = the point is already its own mirror in the texture (on the centre line, or the
        /// two sides share UV space), so painting it once covers both sides. None = nothing found on the other side.
        /// </summary>
        /// <param name="preferIsland">When several parts overlap in the texture, use this island's surface (from a Scene view hit).</param>
        /// <param name="scale">How much bigger a brush must be on the other side to cover the same surface area.</param>
        public Result MirrorUV(Vector2 uvPoint, int preferIsland, out Vector2 mirroredUv, out int mirroredIsland, out float scale) =>
            MirrorUV(uvPoint, preferIsland, out mirroredUv, out mirroredIsland, out scale, out _, out _);

        /// <summary>As above, and also where on the mesh the mirrored point is (triangle + barycentric weights), for 3D-brush strokes.</summary>
        public Result MirrorUV(Vector2 uvPoint, int preferIsland, out Vector2 mirroredUv, out int mirroredIsland, out float scale, out int mirroredTri, out Vector3 mirroredBary)
        {
            mirroredUv = uvPoint; mirroredIsland = -1; scale = 1f; mirroredTri = -1; mirroredBary = default;
            var cands = TrianglesAtUV(uvPoint, out var bary);
            if (cands.Count == 0) return Result.None;
            int pick = 0;
            if (preferIsland >= 0)
                for (int i = 0; i < cands.Count; i++)
                    if (_mesh.triIsland[cands[i]] == preferIsland) { pick = i; break; }
            int t0 = cands[pick];
            var p = Interp(_pos, t0, bary[pick]);
            var m = Mirror(p);
            // Only where the other side really matches: close to the mirrored point (1% of the mesh size) and facing the
            // mirrored way, so asymmetric parts (hair, buckles) are left out instead of landing on a different strand.
            // Facing filter: an inner layer or back face at the same spot faces the other way, so it's skipped and the
            // paint lands on the matching outer surface.
            var n0 = _triNormal[t0];
            var mirroredNormal = n0 - 2f * Vector3.Dot(n0, normal) * normal;
            if (!Closest(m, tolerance * 2f, out int t1, out _, out _, out var b1, mirroredNormal)) return Result.None;
            mirroredUv = InterpUV(t1, b1);
            mirroredIsland = _mesh.triIsland[t1];
            mirroredTri = t1;
            mirroredBary = b1;
            // Already its own mirror: on the centre line, or both halves use the same texture area.
            if ((mirroredUv - uvPoint).sqrMagnitude < 1e-6f || cands.Contains(t1)) return Result.Same;
            float d0 = UvPerWorld(t0), d1 = UvPerWorld(t1);
            scale = d0 > 1e-9f && d1 > 1e-9f ? Mathf.Clamp(d1 / d0, 0.25f, 4f) : 1f;
            return Result.Mirrored;
        }

        /// <summary>World position of a texture point (first part covering it), for drawing the mirrored brush.</summary>
        public bool SurfacePoint(Vector2 uvPoint, int preferIsland, out Vector3 world)
        {
            world = default;
            var cands = TrianglesAtUV(uvPoint, out var bary);
            if (cands.Count == 0) return false;
            int pick = 0;
            if (preferIsland >= 0)
                for (int i = 0; i < cands.Count; i++)
                    if (_mesh.triIsland[cands[i]] == preferIsland) { pick = i; break; }
            world = Interp(_pos, cands[pick], bary[pick]);
            return true;
        }

        Vector3 Interp(Vector3[] arr, int t, Vector3 b) =>
            arr[_mesh.tris[t * 3]] * b.x + arr[_mesh.tris[t * 3 + 1]] * b.y + arr[_mesh.tris[t * 3 + 2]] * b.z;

        Vector2 InterpUV(int t, Vector3 b) =>
            _mesh.uv[_mesh.tris[t * 3]] * b.x + _mesh.uv[_mesh.tris[t * 3 + 1]] * b.y + _mesh.uv[_mesh.tris[t * 3 + 2]] * b.z;

        float UvPerWorld(int t)
        {
            Vector3 a = _pos[_mesh.tris[t * 3]], b = _pos[_mesh.tris[t * 3 + 1]], c = _pos[_mesh.tris[t * 3 + 2]];
            Vector2 ua = _mesh.uv[_mesh.tris[t * 3]], ub = _mesh.uv[_mesh.tris[t * 3 + 1]], uc = _mesh.uv[_mesh.tris[t * 3 + 2]];
            float world = Vector3.Cross(b - a, c - a).magnitude;
            float uvA = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (ub.y - ua.y) * (uc.x - ua.x));
            return world > 1e-12f ? Mathf.Sqrt(uvA / world) : 0f;
        }


        void BuildUvGrid()
        {
            int tc = _mesh.TriangleCount;
            var counts = new int[UvGrid * UvGrid + 1];
            ForUvCells(tc, (t, cell) => counts[cell]++);
            _uvStart = new int[UvGrid * UvGrid + 1];
            for (int i = 0; i < UvGrid * UvGrid; i++) _uvStart[i + 1] = _uvStart[i] + counts[i];
            _uvItems = new int[_uvStart[UvGrid * UvGrid]];
            var fill = (int[])_uvStart.Clone();
            ForUvCells(tc, (t, cell) => _uvItems[fill[cell]++] = t);
        }

        void ForUvCells(int tc, Action<int, int> visit)
        {
            for (int t = 0; t < tc; t++)
            {
                if (!_triOk[t]) continue;
                Vector2 a = _mesh.uv[_mesh.tris[t * 3]], b = _mesh.uv[_mesh.tris[t * 3 + 1]], c = _mesh.uv[_mesh.tris[t * 3 + 2]];
                int x0 = Cell(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Cell(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int y0 = Cell(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), y1 = Cell(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    visit(t, y * UvGrid + x);
            }
        }

        // Tiled UVs wrap back into 0..1, but a positive whole value (1.0, 2.0...) maps to the last cell, not cell 0,
        // so triangles/points that touch the far edge stay in the last cell for both the grid build and lookups.
        static int Cell(float v)
        {
            float r = Mathf.Repeat(v, 1f);
            if (r == 0f && v > 0f) r = 1f;
            return Mathf.Clamp(Mathf.FloorToInt(r * UvGrid), 0, UvGrid - 1);
        }

        List<int> TrianglesAtUV(Vector2 p, out List<Vector3> bary)
        {
            var found = new List<int>();
            bary = new List<Vector3>();
            int cell = Cell(p.y) * UvGrid + Cell(p.x);
            for (int i = _uvStart[cell]; i < _uvStart[cell + 1]; i++)
            {
                int t = _uvItems[i];
                Vector2 a = _mesh.uv[_mesh.tris[t * 3]], b = _mesh.uv[_mesh.tris[t * 3 + 1]], c = _mesh.uv[_mesh.tris[t * 3 + 2]];
                if (MeshUV.PointInTriangle(p, a, b, c, out float u, out float v, out float w))
                {
                    found.Add(t);
                    bary.Add(new Vector3(u, v, w));
                }
            }
            return found;
        }


        void BuildWorldGrid()
        {
            var b = Bounds();
            size = b.size.magnitude;
            if (size <= 0) return;
            tolerance = size * 0.005f;
            int tc = 0;
            foreach (var ok in _triOk) if (ok) tc++;
            if (tc == 0) return;
            var ext = b.size + Vector3.one * (size * 1e-3f);
            float volume = Mathf.Max(ext.x * ext.y * ext.z, size * size * size * 1e-6f);
            _cell = Mathf.Max(Mathf.Pow(volume / Mathf.Max(1, tc / 2f), 1f / 3f), size / 96f);
            _gMin = b.min - Vector3.one * (size * 5e-4f);
            _nx = Mathf.Clamp(Mathf.CeilToInt(ext.x / _cell), 1, 96);
            _ny = Mathf.Clamp(Mathf.CeilToInt(ext.y / _cell), 1, 96);
            _nz = Mathf.Clamp(Mathf.CeilToInt(ext.z / _cell), 1, 96);
            int cells = _nx * _ny * _nz;
            var counts = new int[cells + 1];
            ForWorldCells((t, c) => counts[c]++);
            _cellStart = new int[cells + 1];
            for (int i = 0; i < cells; i++) _cellStart[i + 1] = _cellStart[i] + counts[i];
            _cellItems = new int[_cellStart[cells]];
            var fill = (int[])_cellStart.Clone();
            ForWorldCells((t, c) => _cellItems[fill[c]++] = t);
        }

        void ForWorldCells(Action<int, int> visit)
        {
            for (int t = 0; t < _triOk.Length; t++)
            {
                if (!_triOk[t]) continue;
                Vector3 a = _pos[_mesh.tris[t * 3]], b = _pos[_mesh.tris[t * 3 + 1]], c = _pos[_mesh.tris[t * 3 + 2]];
                var lo = Vector3.Min(a, Vector3.Min(b, c));
                var hi = Vector3.Max(a, Vector3.Max(b, c));
                int x0 = CX(lo.x), x1 = CX(hi.x), y0 = CY(lo.y), y1 = CY(hi.y), z0 = CZ(lo.z), z1 = CZ(hi.z);
                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    visit(t, (z * _ny + y) * _nx + x);
            }
        }

        int CX(float v) => Mathf.Clamp(Mathf.FloorToInt((v - _gMin.x) / _cell), 0, _nx - 1);
        int CY(float v) => Mathf.Clamp(Mathf.FloorToInt((v - _gMin.y) / _cell), 0, _ny - 1);
        int CZ(float v) => Mathf.Clamp(Mathf.FloorToInt((v - _gMin.z) / _cell), 0, _nz - 1);

        bool Closest(Vector3 p, float maxDist, out int tri, out Vector3 point, out float dist) =>
            Closest(p, maxDist, out tri, out point, out dist, out _);

        /// <summary>
        /// Nearest surface point within maxDist (grid search in growing shells of cells). With facing set, only triangles
        /// facing roughly that way count (dot > 0.3).
        /// </summary>
        bool Closest(Vector3 p, float maxDist, out int tri, out Vector3 point, out float dist, out Vector3 bary, Vector3 facing = default)
        {
            bool filter = facing.sqrMagnitude > 0.25f;
            tri = -1; point = default; bary = default; dist = float.MaxValue;
            if (_cellStart == null) return false;
            _stampId++;
            int cx = CX(p.x), cy = CY(p.y), cz = CZ(p.z);
            int maxRing = Mathf.Min(Mathf.CeilToInt(maxDist / _cell) + 1, Mathf.Max(_nx, Mathf.Max(_ny, _nz)));
            float best2 = maxDist * maxDist;
            for (int r = 0; r <= maxRing; r++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (z < 0 || z >= _nz) continue;
                    for (int y = cy - r; y <= cy + r; y++)
                    {
                        if (y < 0 || y >= _ny) continue;
                        for (int x = cx - r; x <= cx + r; x++)
                        {
                            if (x < 0 || x >= _nx) continue;
                            if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Max(Mathf.Abs(y - cy), Mathf.Abs(z - cz))) != r) continue; // shell only
                            int cell = (z * _ny + y) * _nx + x;
                            for (int i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
                            {
                                int t = _cellItems[i];
                                if (_stamp[t] == _stampId) continue;
                                _stamp[t] = _stampId;
                                if (filter && Vector3.Dot(_triNormal[t], facing) < 0.3f) continue;
                                Vector3 a = _pos[_mesh.tris[t * 3]], b = _pos[_mesh.tris[t * 3 + 1]], c = _pos[_mesh.tris[t * 3 + 2]];
                                var q = ClosestOnTriangle(p, a, b, c, out var bc);
                                float d2 = (q - p).sqrMagnitude;
                                if (d2 < best2) { best2 = d2; tri = t; point = q; bary = bc; }
                            }
                        }
                    }
                }
                if (tri >= 0 && Mathf.Sqrt(best2) <= r * _cell) break; // nothing in farther shells can be closer
            }
            if (tri < 0) return false;
            dist = Mathf.Sqrt(best2);
            return true;
        }

        /// <summary>Closest point on triangle abc to p, with barycentric weights (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) { bary = new Vector3(1, 0, 0); return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); bary = new Vector3(1 - v, v, 0); return a + v * ab; }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); bary = new Vector3(1 - w, 0, w); return a + w * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                bary = new Vector3(0, 1 - w, w);
                return b + w * (c - b);
            }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            bary = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }
}
