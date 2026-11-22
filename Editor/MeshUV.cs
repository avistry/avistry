using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>How the ombré gradient runs (enum names kept for saved recipes; labels are what the UI shows).</summary>
    public enum GradientMode
    {
        [InspectorName("Per piece, along UV")] PerStrandUV = 0,        // along each island's main UV axis, top end found from 3D height
        [InspectorName("Per piece, top to bottom")] PerStrandHeight = 1, // each island's own top-to-bottom height in 3D
        [InspectorName("Whole part, top to bottom")] WholeHairHeight = 2, // one gradient across every mesh using the material
        [InspectorName("Texture, top to bottom")] UvV = 3,              // raw UV V (top of the texture = start)
    }

    /// <summary>
    /// UV data for one submesh: triangles, islands, wireframe segments, picking and CPU rasterisation.
    /// CPU rasterisation keeps texel orientation identical to Texture2D (row 0 = v 0) on every graphics API.
    /// </summary>
    public class MeshUV
    {
        /// <summary>One renderer submesh that samples the texture. Several meshes often share one texture.</summary>
        public class Part
        {
            public Renderer renderer;
            public Mesh mesh;
            public int submesh;
            public int vertexOffset;
            public int triStart;   // first triangle (not index) of this part in the combined arrays
            public int triCount;
        }

        public readonly List<Part> parts = new List<Part>();
        /// <summary>Names of meshes that lacked the chosen UV channel and fell back to UV0.</summary>
        public readonly List<string> meshesMissingChannel = new List<string>();
        public int uvChannel;
        public Vector2[] uv;
        public int[] tris;           // combined triangle indices (3 per triangle), offset per part
        public int[] triIsland;      // island id per triangle
        public int islandCount;
        public Vector3[] lineSegments; // pairs of (u, v, 0) for Handles.DrawLines
        public float[] worldHeight;  // per vertex, posed world Y (from BakeMesh); null if unavailable

        public int TriangleCount => tris.Length / 3;

        public static MeshUV Build(Renderer renderer, int materialIndex, int uvChannel) =>
            Build(new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = renderer, materialIndex = materialIndex } }, uvChannel);

        /// <summary>Combines the UVs of every renderer slot that uses the material, so islands cover the whole texture.</summary>
        public static MeshUV Build(List<TargetResolver.SlotRef> slots, int uvChannel)
        {
            var m = new MeshUV { uvChannel = uvChannel };
            var uvAll = new List<Vector2>();
            var posAll = new List<Vector3>();
            var partIdAll = new List<int>();
            var trisAll = new List<int>();
            var seen = new HashSet<(Mesh, int)>();
            var tmp = new List<Vector2>();

            foreach (var s in slots)
            {
                var mesh = TargetResolver.GetSharedMesh(s.renderer);
                if (mesh == null || mesh.subMeshCount == 0) continue;
                int sub = Mathf.Clamp(s.materialIndex, 0, mesh.subMeshCount - 1);
                if (!seen.Add((mesh, sub))) continue; // same mesh used twice (e.g. duplicated object): UVs identical

                tmp.Clear();
                mesh.GetUVs(uvChannel, tmp);
                if (tmp.Count == 0 && uvChannel != 0)
                {
                    m.meshesMissingChannel.Add(mesh.name);
                    mesh.GetUVs(0, tmp);
                }
                if (tmp.Count == 0) continue;

                var part = new Part
                {
                    renderer = s.renderer, mesh = mesh, submesh = sub,
                    vertexOffset = uvAll.Count, triStart = trisAll.Count / 3,
                };
                uvAll.AddRange(tmp);
                posAll.AddRange(mesh.vertices);
                for (int i = 0; i < tmp.Count; i++) partIdAll.Add(m.parts.Count);
                var t = mesh.GetTriangles(sub);
                foreach (var idx in t) trisAll.Add(idx + part.vertexOffset);
                part.triCount = t.Length / 3;
                m.parts.Add(part);
            }
            if (m.parts.Count == 0) return null;

            m.uv = uvAll.ToArray();
            m.tris = trisAll.ToArray();
            m.BuildIslands(posAll.ToArray(), partIdAll.ToArray());
            m.BuildEdges();
            m.RefreshPose(); // bakes the pose once and derives worldHeight from it
            if (m.meshesMissingChannel.Count > 0)
                Debug.LogWarning($"[Avistry] UV{uvChannel} is missing on {string.Join(", ", m.meshesMissingChannel)}; using UV0 for them.");
            return m;
        }

        /// <summary>The part (mesh) a combined triangle belongs to, or null.</summary>
        public Part PartOfTriangle(int tri)
        {
            foreach (var p in parts)
                if (tri >= p.triStart && tri < p.triStart + p.triCount) return p;
            return null;
        }

        /// <summary>
        /// A key per island that stays the same when other meshes are added or removed: the renderer, its submesh and
        /// the island's first triangle within that mesh. Used to carry selections across a rebuild with other meshes.
        /// </summary>
        public (int renderer, int submesh, int localTri)[] IslandKeys()
        {
            var keys = new (int, int, int)[islandCount];
            var done = new bool[islandCount];
            foreach (var p in parts)
            {
                int rid = p.renderer != null ? p.renderer.GetInstanceID() : 0;
                for (int t = p.triStart; t < p.triStart + p.triCount; t++)
                {
                    int isl = triIsland[t];
                    if (done[isl]) continue;
                    done[isl] = true;
                    keys[isl] = (rid, p.submesh, t - p.triStart);
                }
            }
            return keys;
        }


        void BuildIslands(Vector3[] positions, int[] partOf)
        {
            int vCount = uv.Length;
            var parent = new int[vCount];
            for (int i = 0; i < vCount; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            void Union(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a != b) parent[b] = a;
            }

            // Weld vertices that were split only for normals/tangents: same UV AND same position.
            // (Welding by UV alone would merge mirrored/stacked islands that share UV space.)
            var weld = new Dictionary<(int, int, int, int, int, int), int>();
            var used = new bool[vCount];
            foreach (var t in tris) used[t] = true;
            for (int i = 0; i < vCount; i++)
            {
                if (!used[i]) continue;
                var p = positions[i];
                var key = (partOf[i], Mathf.RoundToInt(uv[i].x * 65536f), Mathf.RoundToInt(uv[i].y * 65536f),
                           Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
                if (weld.TryGetValue(key, out int other)) Union(other, i);
                else weld[key] = i;
            }

            for (int t = 0; t < tris.Length; t += 3)
            {
                Union(tris[t], tris[t + 1]);
                Union(tris[t], tris[t + 2]);
            }

            var rootToIsland = new Dictionary<int, int>();
            triIsland = new int[tris.Length / 3];
            for (int t = 0; t < triIsland.Length; t++)
            {
                int root = Find(tris[t * 3]);
                if (!rootToIsland.TryGetValue(root, out int id))
                {
                    id = rootToIsland.Count;
                    rootToIsland[root] = id;
                }
                triIsland[t] = id;
            }
            islandCount = rootToIsland.Count;
        }

        void BuildEdges()
        {
            var seen = new HashSet<long>();
            var segs = new List<Vector3>(tris.Length * 2);
            for (int t = 0; t < tris.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!seen.Add(key)) continue;
                    segs.Add(new Vector3(uv[a].x, uv[a].y, 0));
                    segs.Add(new Vector3(uv[b].x, uv[b].y, 0));
                }
            }
            lineSegments = segs.ToArray();
        }

        public Vector3[] SegmentsForIslands(HashSet<int> islands)
        {
            var segs = new List<Vector3>();
            if (islands == null || islands.Count == 0) return segs.ToArray();
            for (int t = 0; t < triIsland.Length; t++)
            {
                if (!islands.Contains(triIsland[t])) continue;
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t * 3 + e], b = tris[t * 3 + (e + 1) % 3];
                    segs.Add(new Vector3(uv[a].x, uv[a].y, 0));
                    segs.Add(new Vector3(uv[b].x, uv[b].y, 0));
                }
            }
            return segs.ToArray();
        }


        /// <summary>All islands with a triangle covering this UV point (mirrored parts return both sides).</summary>
        public List<int> IslandsAtUV(Vector2 p)
        {
            var found = new List<int>();
            var cell = _uvGrid[GridCell(p.x) + GridCell(p.y) * UvGridN];
            if (cell == null) return found;
            foreach (int t in cell)
            {
                Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c = uv[tris[t * 3 + 2]];
                if (p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x))) continue;
                if (p.y < Mathf.Min(a.y, Mathf.Min(b.y, c.y)) || p.y > Mathf.Max(a.y, Mathf.Max(b.y, c.y))) continue;
                if (Barycentric(p, a, b, c, out _, out _, out _) && !found.Contains(triIsland[t]))
                    found.Add(triIsland[t]);
            }
            return found;
        }


        List<int>[] _pieceNeighbours;
        Vector2Int[] _pieceTiles;
        Dictionary<Vector2Int, int> _tileCounts;

        /// <summary>
        /// Per piece, the other pieces that touch it on the model: they share a vertex position (rest pose, rounded to 1e-5)
        /// within one part. Pieces of different parts never touch.
        /// </summary>
        internal List<int>[] PieceNeighbours()
        {
            if (_pieceNeighbours != null) return _pieceNeighbours;
            var result = new List<int>[islandCount];
            for (int i = 0; i < islandCount; i++) result[i] = new List<int>();
            var pairs = new HashSet<long>();
            var atPosition = new Dictionary<(int, int, int), List<int>>();
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                Vector3[] local;
                try { local = part.mesh.vertices; } catch { continue; }
                atPosition.Clear();
                for (int t = part.triStart; t < part.triStart + part.triCount && t < triIsland.Length; t++)
                {
                    int island = triIsland[t];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[t * 3 + k] - part.vertexOffset;
                        if (v < 0 || v >= local.Length) continue;
                        var p = local[v];
                        var key = (Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                        if (!atPosition.TryGetValue(key, out var here)) atPosition[key] = here = new List<int>(2);
                        if (here.Contains(island)) continue;
                        // A position shared by many pieces (a pinched vertex) links only a few, so one bad vertex stays cheap.
                        for (int h = 0; h < here.Count && h < 12; h++)
                        {
                            int other = here[h];
                            long pair = ((long)Mathf.Min(island, other) << 32) | (uint)Mathf.Max(island, other);
                            if (!pairs.Add(pair)) continue;
                            result[island].Add(other);
                            result[other].Add(island);
                        }
                        here.Add(island);
                    }
                }
            }
            return _pieceNeighbours = result;
        }

        /// <summary>Per piece, the UV tile (floor of u and v) of the mean of its triangle centroids. (0, 0) is the texture itself.</summary>
        internal Vector2Int[] PieceTiles()
        {
            if (_pieceTiles != null) return _pieceTiles;
            var sum = new Vector2[islandCount];
            var count = new int[islandCount];
            for (int t = 0; t < triIsland.Length; t++)
            {
                Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c = uv[tris[t * 3 + 2]];
                var centre = (a + b + c) / 3f;
                if (float.IsNaN(centre.x) || float.IsNaN(centre.y)) continue;
                sum[triIsland[t]] += centre;
                count[triIsland[t]]++;
            }
            var tiles = new Vector2Int[islandCount];
            for (int i = 0; i < islandCount; i++)
                if (count[i] > 0) tiles[i] = new Vector2Int(Mathf.FloorToInt(sum[i].x / count[i]), Mathf.FloorToInt(sum[i].y / count[i]));
            return _pieceTiles = tiles;
        }

        /// <summary>How many pieces sit in each UV tile.</summary>
        internal Dictionary<Vector2Int, int> TileCounts()
        {
            if (_tileCounts != null) return _tileCounts;
            var counts = new Dictionary<Vector2Int, int>();
            foreach (var t in PieceTiles()) counts[t] = counts.TryGetValue(t, out int n) ? n + 1 : 1;
            return _tileCounts = counts;
        }

        // Uniform UV grid of triangle indices (out-of-range UVs clamp into the edge cells, for triangles and queries alike).
        const int UvGridN = 64;
        List<int>[] _uvGridCells;

        List<int>[] _uvGrid
        {
            get
            {
                if (_uvGridCells != null) return _uvGridCells;
                var g = new List<int>[UvGridN * UvGridN];
                for (int t = 0; t < triIsland.Length; t++)
                {
                    Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c = uv[tris[t * 3 + 2]];
                    int x0 = GridCell(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = GridCell(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                    int y0 = GridCell(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), y1 = GridCell(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                            (g[x + y * UvGridN] ??= new List<int>()).Add(t);
                }
                return _uvGridCells = g;
            }
        }

        static int GridCell(float f) => float.IsNaN(f) ? 0 : Mathf.Clamp(Mathf.FloorToInt(f * UvGridN), 0, UvGridN - 1);


        Vector3[][] _posed;
        Bounds?[] _posedBounds; // per part, from the baked vertices
        Vector3 _poseKey;
        int _poseHash;

        /// <summary>Re-bakes the current pose of every part (call when the avatar may have moved).</summary>
        public void RefreshPose()
        {
            _posed = new Vector3[parts.Count][];
            _posedBounds = new Bounds?[parts.Count];
            var heights = new float[uv.Length];
            bool heightsOk = true;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].renderer == null) { heightsOk = false; continue; }
                Vector3[] w;
                try { w = _posed[i] = PosedWorldVertices(parts[i].renderer, parts[i].mesh); }
                catch (Exception e)
                {
                    Debug.LogWarning("[Avistry] Could not bake mesh pose, height gradients disabled: " + e.Message);
                    heightsOk = false;
                    continue;
                }
                if (w == null || w.Length == 0) { heightsOk = false; continue; }
                for (int k = 0; k < w.Length && parts[i].vertexOffset + k < heights.Length; k++) heights[parts[i].vertexOffset + k] = w[k].y;
                var b = new Bounds(w[0], Vector3.zero);
                for (int k = 1; k < w.Length; k++) b.Encapsulate(w[k]);
                b.Expand(0.002f); // flat parts (planes) would otherwise have a zero-thickness box that rays can miss
                _posedBounds[i] = b;
            }
            worldHeight = heightsOk ? heights : null;
            _poseKey = PoseKey();
            _poseHash = PoseHash();
            _combined = null;
            PoseVersion++;
            _surfaceDirty = true;
        }


        /// <summary>Goes up every time the pose is re-baked (3D-brush strokes depend on the pose).</summary>
        public int PoseVersion { get; private set; }
        Vector3[] _combined;
        float _posedSize;
        Mesh _surfaceMesh;
        bool _surfaceDirty = true;

        /// <summary>PosedCombined(), cached until the pose changes.</summary>
        public Vector3[] PosedCombinedCached()
        {
            if (_posed == null) RefreshPose();
            if (_combined != null) return _combined;
            _combined = PosedCombined();
            var b = new Bounds();
            bool any = false;
            foreach (int i in tris)
            {
                var p = _combined[i];
                if (float.IsNaN(p.x)) continue;
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
            _posedSize = any ? b.size.magnitude : 0f;
            return _combined;
        }

        /// <summary>Diagonal of the posed mesh's bounds (world units). 3D brush sizes are stored relative to it.</summary>
        public float PosedSize { get { PosedCombinedCached(); return _posedSize; } }

        /// <summary>World position and face normal of a point given as triangle + barycentric weights.</summary>
        public bool SurfacePoint(int tri, Vector3 bary, out Vector3 world, out Vector3 normal)
        {
            world = default; normal = Vector3.up;
            if (tri < 0 || tri >= TriangleCount) return false;
            var pos = PosedCombinedCached();
            Vector3 a = pos[tris[tri * 3]], b = pos[tris[tri * 3 + 1]], c = pos[tris[tri * 3 + 2]];
            if (float.IsNaN(a.x) || float.IsNaN(b.x) || float.IsNaN(c.x)) return false;
            world = a * bary.x + b * bary.y + c * bary.z;
            var n = Vector3.Cross(b - a, c - a);
            float len = Mathf.Sqrt(n.sqrMagnitude);
            normal = len > 1e-20f ? n / len : Vector3.up;
            return true;
        }

        /// <summary>
        /// The mesh laid out in texture space for the 3D brush: vertex = (u, v, 0), TEXCOORD0 = posed world position,
        /// NORMAL = posed world normal. Drawing it into the texture lets a shader measure 3D distances per texel, so
        /// brush dabs are spheres on the model and paint crosses UV seams.
        /// </summary>
        public Mesh SurfaceMesh()
        {
            if (_surfaceMesh != null && !_surfaceDirty) return _surfaceMesh;
            var pos = PosedCombinedCached();
            // Every triangle gets its own three vertices carrying the true face normal. Smoothed vertex normals are
            // useless on hair cards and other two-sided geometry, where front and back faces share vertices and cancel out.
            var verts = new List<Vector3>(tris.Length);
            var world = new List<Vector3>(tris.Length);
            var normals = new List<Vector3>(tris.Length);
            var indices = new List<int>(tris.Length);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (float.IsNaN(pos[a].x) || float.IsNaN(pos[b].x) || float.IsNaN(pos[c].x)) continue;
                var fn = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                float len = Mathf.Sqrt(fn.sqrMagnitude);
                fn = len > 1e-20f ? fn / len : Vector3.up;
                for (int k = 0; k < 3; k++)
                {
                    int i = tris[t + k];
                    indices.Add(verts.Count);
                    verts.Add(new Vector3(uv[i].x, uv[i].y, 0));
                    world.Add(pos[i]);
                    normals.Add(fn);
                }
            }
            if (_surfaceMesh == null) _surfaceMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "ATS surface (UV space)" };
            _surfaceMesh.Clear();
            _surfaceMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _surfaceMesh.SetVertices(verts);
            _surfaceMesh.SetNormals(normals);
            _surfaceMesh.SetUVs(0, world);
            _surfaceMesh.SetTriangles(indices, 0, false);
            _surfaceMesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0), new Vector3(1000, 1000, 1000));
            _surfaceDirty = false;
            return _surfaceMesh;
        }

        /// <summary>Frees the cached 3D-brush mesh (call when this MeshUV is no longer used).</summary>
        public void ReleaseSurfaceMesh()
        {
            if (_surfaceMesh != null) UnityEngine.Object.DestroyImmediate(_surfaceMesh);
            _surfaceMesh = null;
        }

        /// <summary>True when the renderers moved since the last RefreshPose (cheap: compares bounds, transforms, a few bones and blendshape weights).</summary>
        public bool PoseChanged() => _posed == null || (PoseKey() - _poseKey).sqrMagnitude > 1e-10f || PoseHash() != _poseHash;

        // Things the renderer bounds can miss: the renderer's transform (incl. rotation/scale), a few bones, blendshape weights.
        int PoseHash()
        {
            var hc = new HashCode();
            foreach (var p in parts)
            {
                var r = p.renderer;
                if (r == null) continue;
                var m = r.transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) hc.Add(m[i]);
                if (r is SkinnedMeshRenderer smr)
                {
                    var bones = smr.bones;
                    int n = bones != null ? bones.Length : 0;
                    for (int k = 0; k < 4 && n > 0; k++)
                    {
                        var b = bones[k * (n - 1) / 3];
                        if (b == null) continue;
                        hc.Add(b.position);
                        hc.Add(b.rotation);
                    }
                    var sm = smr.sharedMesh;
                    int bs = sm != null ? sm.blendShapeCount : 0;
                    for (int k = 0; k < bs; k++) hc.Add(smr.GetBlendShapeWeight(k));
                }
            }
            return hc.ToHashCode();
        }

        Vector3 PoseKey()
        {
            var k = Vector3.zero;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].renderer != null) { var b = parts[i].renderer.bounds; k += b.center * (i + 1) + b.size * (i + 7); }
            return k;
        }

        Vector3[] Posed(int partIndex)
        {
            if (_posed == null) RefreshPose();
            return _posed[partIndex];
        }

        /// <summary>
        /// Posed world position of every vertex in the combined arrays (indexed like uv / tris). Parts that can't be posed
        /// get NaN, so callers can skip their triangles.
        /// </summary>
        public Vector3[] PosedCombined()
        {
            var all = new Vector3[uv.Length];
            for (int i = 0; i < all.Length; i++) all[i] = new Vector3(float.NaN, float.NaN, float.NaN);
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var w = Posed(pi);
                if (w == null || w.Length == 0) continue;
                int off = parts[pi].vertexOffset;
                for (int i = 0; i < w.Length && off + i < all.Length; i++) all[off + i] = w[i];
            }
            return all;
        }

        /// <summary>The avatar the mesh belongs to (nearest parent with an Animator, else the top of the hierarchy).</summary>
        public Transform AvatarRoot()
        {
            var r = parts.Count > 0 ? parts[0].renderer : null;
            if (r == null) return null;
            var anim = r.GetComponentInParent<Animator>();
            return anim != null ? anim.transform : r.transform.root;
        }

        public static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w) =>
            Barycentric(p, a, b, c, out u, out v, out w);

        /// <summary>
        /// Fills a world-space mesh with the triangles of the islands that pass the filter.
        /// Submesh 0 = triangles, submesh 1 = outline lines (edges used by only one of those triangles).
        /// </summary>
        public void BuildHighlight(Mesh mesh, Func<int, bool> islandFilter)
        {
            mesh.Clear();
            var verts = new List<Vector3>();
            var triIdx = new List<int>();
            var lineIdx = new List<int>();
            var edges = new Dictionary<long, (int count, int a, int b)>();

            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                var w = Posed(pi);
                if (w == null || part.renderer == null || !part.renderer.gameObject.activeInHierarchy) continue;
                int off = part.vertexOffset;
                for (int t = part.triStart; t < part.triStart + part.triCount; t++)
                {
                    if (!islandFilter(triIsland[t])) continue;
                    int b = verts.Count;
                    for (int k = 0; k < 3; k++)
                    {
                        verts.Add(w[tris[t * 3 + k] - off]);
                        triIdx.Add(b + k);
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        int i0 = tris[t * 3 + k], i1 = tris[t * 3 + (k + 1) % 3];
                        long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
                        edges[key] = edges.TryGetValue(key, out var e) ? (e.count + 1, e.a, e.b) : (1, b + k, b + (k + 1) % 3);
                    }
                }
            }
            foreach (var e in edges.Values)
                if (e.count == 1) { lineIdx.Add(e.a); lineIdx.Add(e.b); }

            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetIndices(triIdx, MeshTopology.Triangles, 0, false);
            mesh.SetIndices(lineIdx, MeshTopology.Lines, 1, false);
            mesh.RecalculateBounds();
        }

        public struct SurfaceHit
        {
            public Vector3 point;
            public Vector3 normal;      // faces the ray
            public Vector2 uv;
            public int island;
            public int triangle;
            public float uvPerWorld;    // UV units per world unit at the hit (texel density)
            public Vector3 bary;        // weights of the triangle's three vertices at the hit
            public bool backFace;       // the ray hit the back of the face (its real normal points away from the viewer)
        }

        /// <summary>Ray against every part's posed mesh (cached pose); closest hit with its UV.</summary>
        public bool Raycast(Ray ray, out SurfaceHit hit)
        {
            hit = default;
            float best = float.MaxValue, bu = 0, bv = 0;
            int bestTri = -1;
            Vector3 b0 = default, b1 = default, b2 = default;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                if (part.renderer == null || !part.renderer.enabled || !part.renderer.gameObject.activeInHierarchy) continue;
                var world = Posed(pi);
                if (world == null) continue;
                if (_posedBounds[pi] is Bounds pb && !pb.IntersectRay(ray)) continue;
                int off = part.vertexOffset;
                for (int t = part.triStart; t < part.triStart + part.triCount; t++)
                {
                    Vector3 v0 = world[tris[t * 3] - off], v1 = world[tris[t * 3 + 1] - off], v2 = world[tris[t * 3 + 2] - off];
                    if (RayTriangle(ray, v0, v1, v2, out float d, out float u, out float v) && d < best)
                    {
                        best = d; bestTri = t; bu = u; bv = v;
                        b0 = v0; b1 = v1; b2 = v2;
                    }
                }
            }
            if (bestTri < 0) return false;

            Vector2 t0 = uv[tris[bestTri * 3]], t1 = uv[tris[bestTri * 3 + 1]], t2 = uv[tris[bestTri * 3 + 2]];
            var n = Vector3.Cross(b1 - b0, b2 - b0);
            float worldArea = n.magnitude * 0.5f;
            float uvArea = Mathf.Abs((t1.x - t0.x) * (t2.y - t0.y) - (t1.y - t0.y) * (t2.x - t0.x)) * 0.5f;
            n = n.normalized;
            bool backFace = Vector3.Dot(n, ray.direction) > 0;
            if (backFace) n = -n;

            hit = new SurfaceHit
            {
                point = ray.GetPoint(best),
                normal = n,
                uv = t0 * (1 - bu - bv) + t1 * bu + t2 * bv,
                island = triIsland[bestTri],
                triangle = bestTri,
                uvPerWorld = worldArea > 1e-12f && uvArea > 0 ? Mathf.Sqrt(uvArea / worldArea) : 0f,
                bary = new Vector3(1 - bu - bv, bu, bv),
                backFace = backFace,
            };
            return true;
        }

        /// <summary>Island of the closest hit triangle, or -1.</summary>
        public int PickIslandWorld(Ray ray) => Raycast(ray, out var hit) ? hit.island : -1;

        static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float dist, out float u, out float v)
        {
            // Möller–Trumbore, double-sided. Hit point = (1-u-v)·v0 + u·v1 + v·v2.
            dist = 0; u = 0; v = 0;
            Vector3 e1 = v1 - v0, e2 = v2 - v0;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = ray.origin - v0;
            u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(s, e1);
            v = Vector3.Dot(ray.direction, q) * inv;
            if (v < 0 || u + v > 1) return false;
            dist = Vector3.Dot(e2, q) * inv;
            return dist > 0;
        }

        /// <summary>
        /// World-space vertices of the mesh in its current pose. BakeMesh's scale handling has changed between
        /// Unity versions, so try both interpretations and keep the one that matches renderer.bounds.
        /// </summary>
        public static Vector3[] PosedWorldVertices(Renderer renderer, Mesh shared)
        {
            Vector3[] local;
            if (renderer is SkinnedMeshRenderer smr)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                local = baked.vertices;
                UnityEngine.Object.DestroyImmediate(baked);
            }
            else local = shared.vertices;
            if (local == null || local.Length == 0) return null;

            var t = renderer.transform;
            var withScale = t.localToWorldMatrix;
            var noScale = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            var target = renderer.bounds;

            Vector3[] Apply(Matrix4x4 m)
            {
                var o = new Vector3[local.Length];
                for (int i = 0; i < local.Length; i++) o[i] = m.MultiplyPoint3x4(local[i]);
                return o;
            }
            float Score(Vector3[] pts)
            {
                var b = new Bounds(pts[0], Vector3.zero);
                for (int i = 1; i < pts.Length; i++) b.Encapsulate(pts[i]);
                return (b.center - target.center).magnitude + (b.size - target.size).magnitude;
            }

            var a = Apply(withScale);
            if (renderer is SkinnedMeshRenderer && t.lossyScale != Vector3.one)
            {
                var b = Apply(noScale);
                return Score(b) < Score(a) ? b : a;
            }
            return a;
        }


        /// <summary>Per-vertex t in 0..1 (0 = top / start, 1 = bottom / end) for the given gradient mode.</summary>
        public float[] ComputeGradientT(GradientMode mode)
        {
            var t = new float[uv.Length];
            if (mode == GradientMode.UvV || worldHeight == null)
            {
                for (int i = 0; i < uv.Length; i++) t[i] = 1f - uv[i].y; // top of texture = root
                return t;
            }

            if (mode == GradientMode.WholeHairHeight)
            {
                float hi = float.MinValue, lo = float.MaxValue;
                foreach (var v in tris) { hi = Mathf.Max(hi, worldHeight[v]); lo = Mathf.Min(lo, worldHeight[v]); }
                float range = Mathf.Max(hi - lo, 1e-6f);
                for (int i = 0; i < uv.Length; i++) t[i] = Mathf.Clamp01((hi - worldHeight[i]) / range);
                return t;
            }

            // Per-island modes: gather vertices of each island.
            var islandVerts = new List<int>[islandCount];
            var mark = new int[uv.Length];
            for (int i = 0; i < mark.Length; i++) mark[i] = -1;
            for (int tri = 0; tri < triIsland.Length; tri++)
            {
                int isl = triIsland[tri];
                islandVerts[isl] ??= new List<int>();
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[tri * 3 + k];
                    if (mark[v] == isl) continue;
                    mark[v] = isl;
                    islandVerts[isl].Add(v);
                }
            }

            foreach (var verts in islandVerts)
            {
                if (verts == null || verts.Count == 0) continue;
                if (mode == GradientMode.PerStrandHeight)
                {
                    float hi = float.MinValue, lo = float.MaxValue;
                    foreach (var v in verts) { hi = Mathf.Max(hi, worldHeight[v]); lo = Mathf.Min(lo, worldHeight[v]); }
                    float range = hi - lo;
                    foreach (var v in verts) t[v] = range < 1e-6f ? 0.5f : (hi - worldHeight[v]) / range;
                    continue;
                }

                // PerStrandUV: choose the UV axis that correlates best with height; root = higher end.
                float cu = Correlation(verts, v => uv[v].x), cv = Correlation(verts, v => uv[v].y);
                bool useU = Mathf.Abs(cu) > Mathf.Abs(cv);
                float corr = useU ? cu : cv;
                float min = float.MaxValue, max = float.MinValue;
                foreach (var v in verts)
                {
                    float a = useU ? uv[v].x : uv[v].y;
                    min = Mathf.Min(min, a); max = Mathf.Max(max, a);
                }
                float span = max - min;
                foreach (var v in verts)
                {
                    float a = useU ? uv[v].x : uv[v].y;
                    float n = span < 1e-6f ? 0.5f : (a - min) / span;
                    // corr > 0: height grows with the UV axis, so the root sits at the high end.
                    t[v] = corr >= 0 ? 1f - n : n;
                }
            }
            return t;
        }

        float Correlation(List<int> verts, Func<int, float> axis)
        {
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            int n = verts.Count;
            foreach (var v in verts)
            {
                double x = axis(v), y = worldHeight[v];
                sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
            }
            double cov = sxy / n - sx / n * (sy / n);
            double vx = sxx / n - sx / n * (sx / n), vy = syy / n - sy / n * (sy / n);
            if (vx <= 1e-12 || vy <= 1e-12) return 0;
            return (float)(cov / Math.Sqrt(vx * vy));
        }


        /// <summary>Rasterises triangles into a w×h grid (row 0 = v 0). Calls write(pixel, tri, b0, b1, b2).</summary>
        public void Rasterize(int w, int h, Func<int, bool> triFilter, Action<int, int, float, float, float> write)
        {
            for (int t = 0; t < triIsland.Length; t++)
            {
                if (triFilter != null && !triFilter(t)) continue;
                Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c = uv[tris[t * 3 + 2]];
                Vector2 pa = new Vector2(a.x * w, a.y * h), pb = new Vector2(b.x * w, b.y * h), pc = new Vector2(c.x * w, c.y * h);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))));
                int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))));
                if (x0 > x1 || y0 > y1) continue;

                float area = Edge(pa, pb, pc);
                if (Mathf.Abs(area) < 1e-12f) continue;
                float invArea = 1f / area;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float w0 = Edge(pb, pc, p) * invArea;
                        float w1 = Edge(pc, pa, p) * invArea;
                        float w2 = Edge(pa, pb, p) * invArea;
                        if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                        write(y * w + x, t, w0, w1, w2);
                    }
                }
            }
        }

        static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static bool Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            float area = Edge(a, b, c);
            u = v = w = 0;
            if (Mathf.Abs(area) < 1e-12f) return false;
            u = Edge(b, c, p) / area;
            v = Edge(c, a, p) / area;
            w = Edge(a, b, p) / area;
            return u >= -1e-5f && v >= -1e-5f && w >= -1e-5f;
        }

        /// <summary>What the AI helper may know about one piece: where it is on the texture and what colour it is.</summary>
        internal struct PieceInfo
        {
            public int id;
            public int part;          // index in <see cref="parts"/>
            public Rect bounds;       // x, y, width, height in UV space
            public float area;        // share of the whole texture, 0 to 1
            public Color meanColour;  // gamma colour averaged over the piece's texels
        }

        /// <summary>
        /// One entry per piece. <paramref name="pixels"/> is a size×size sample of the texture (row 0 = v 0); pass null to
        /// skip the colour. Each triangle is looked at once, and the texels are looked at once through the id raster.
        /// </summary>
        internal PieceInfo[] PieceStats(Color[] pixels, int size)
        {
            var info = new PieceInfo[islandCount];
            var seen = new bool[islandCount];
            for (int i = 0; i < islandCount; i++)
                info[i] = new PieceInfo { id = i, part = -1, bounds = new Rect(1f, 1f, 0f, 0f) };
            var min = new Vector2[islandCount];
            var max = new Vector2[islandCount];
            for (int p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                for (int t = part.triStart; t < part.triStart + part.triCount && t < triIsland.Length; t++)
                {
                    int isl = triIsland[t];
                    Vector2 a = uv[tris[t * 3]], b = uv[tris[t * 3 + 1]], c = uv[tris[t * 3 + 2]];
                    if (!seen[isl])
                    {
                        seen[isl] = true;
                        info[isl].part = p;
                        min[isl] = Vector2.Min(a, Vector2.Min(b, c));
                        max[isl] = Vector2.Max(a, Vector2.Max(b, c));
                    }
                    else
                    {
                        min[isl] = Vector2.Min(min[isl], Vector2.Min(a, Vector2.Min(b, c)));
                        max[isl] = Vector2.Max(max[isl], Vector2.Max(a, Vector2.Max(b, c)));
                    }
                    info[isl].area += Mathf.Abs(Edge(a, b, c)) * 0.5f;
                }
            }
            for (int i = 0; i < islandCount; i++)
                if (seen[i]) info[i].bounds = new Rect(min[i].x, min[i].y, max[i].x - min[i].x, max[i].y - min[i].y);

            if (pixels != null && size > 0 && pixels.Length == size * size)
            {
                var ids = RasterIslandIds(size, size, 0);
                var sum = new Vector3[islandCount];
                var count = new int[islandCount];
                for (int i = 0; i < ids.Length; i++)
                {
                    int isl = ids[i] - 1;
                    if (isl < 0 || isl >= islandCount || pixels[i].a < 0.1f) continue;
                    sum[isl] += new Vector3(pixels[i].r, pixels[i].g, pixels[i].b);
                    count[isl]++;
                }
                for (int i = 0; i < islandCount; i++)
                    if (count[i] > 0) info[i].meanColour = new Color(sum[i].x / count[i], sum[i].y / count[i], sum[i].z / count[i], 1f);
            }
            return info;
        }

        /// <summary>R8 mask: 1 inside the given islands (or every island when islands == null), dilated.</summary>
        public byte[] RasterIslandMask(int w, int h, HashSet<int> islands, int dilate)
        {
            var val = new ushort[w * h];
            var covered = new bool[w * h];
            Rasterize(w, h, islands == null ? null : (Func<int, bool>)(t => islands.Contains(triIsland[t])),
                (px, t, a, b, c) => { val[px] = 65535; covered[px] = true; });
            Dilate(val, covered, w, h, dilate);
            var o = new byte[w * h];
            for (int i = 0; i < o.Length; i++) o[i] = (byte)(val[i] >> 8);
            return o;
        }

        /// <summary>Island id + 1 per texel (0 = no island), dilated into the padding. Overlapping islands keep the last one.</summary>
        public ushort[] RasterIslandIds(int w, int h, int dilate)
        {
            var val = new ushort[w * h];
            var covered = new bool[w * h];
            Rasterize(w, h, null, (px, t, a, b, c) =>
            {
                val[px] = (ushort)Mathf.Min(triIsland[t] + 1, 65535);
                covered[px] = true;
            });
            Dilate(val, covered, w, h, dilate);
            return val;
        }

        /// <summary>R16 gradient values (0..65535), dilated into the padding so seams do not show.</summary>
        public ushort[] RasterGradient(int w, int h, float[] vertexT, int dilate)
        {
            var val = new ushort[w * h];
            var covered = new bool[w * h];
            Rasterize(w, h, null, (px, t, a, b, c) =>
            {
                float v = vertexT[tris[t * 3]] * a + vertexT[tris[t * 3 + 1]] * b + vertexT[tris[t * 3 + 2]] * c;
                val[px] = (ushort)Mathf.Clamp(Mathf.RoundToInt(v * 65535f), 0, 65535);
                covered[px] = true;
            });
            Dilate(val, covered, w, h, dilate);
            return val;
        }

        /// <summary>Multi-source BFS (8-connected): copies the nearest covered value outward up to radius texels.</summary>
        public static void Dilate(ushort[] val, bool[] covered, int w, int h, int radius)
        {
            if (radius <= 0) return;
            var frontier = new List<int>();
            for (int i = 0; i < covered.Length; i++)
            {
                if (!covered[i]) continue;
                int x = i % w, y = i / w;
                bool edge = false;
                for (int dy = -1; dy <= 1 && !edge; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        if (!covered[ny * w + nx]) { edge = true; break; }
                    }
                if (edge) frontier.Add(i);
            }

            var next = new List<int>();
            for (int step = 0; step < radius && frontier.Count > 0; step++)
            {
                next.Clear();
                foreach (int i in frontier)
                {
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int n = ny * w + nx;
                            if (covered[n]) continue;
                            covered[n] = true;
                            val[n] = val[i];
                            next.Add(n);
                        }
                }
                (frontier, next) = (next, frontier);
            }
        }

        /// <summary>RGBA overlay with alpha where the selected islands are (for display in the canvas).</summary>
        public Color32[] SelectionOverlay(int w, int h, HashSet<int> islands, Color32 color)
        {
            var px = new Color32[w * h];
            if (islands == null || islands.Count == 0) return px;
            Rasterize(w, h, t => islands.Contains(triIsland[t]), (i, t, a, b, c) => px[i] = color);
            return px;
        }
    }
}
