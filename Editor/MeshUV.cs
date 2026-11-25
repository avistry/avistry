using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public enum GradientMode
    {
        [InspectorName("Per piece, along UV")] PerStrandUV = 0,
        [InspectorName("Per piece, top to bottom")] PerStrandHeight = 1,
        [InspectorName("Whole part, top to bottom")] WholeHairHeight = 2,
        [InspectorName("Texture, top to bottom")] UvV = 3,
    }

    public partial class MeshUV
    {
        public class Part
        {
            public Renderer renderer;
            public Mesh mesh;
            public int submesh;
            internal int materialSlot;
            internal int uvChannel, vertexCount;
            internal ulong bindPositionsHash;
            internal TileMembership[] tileMembership;
            public int vertexOffset;
            public int triStart;
            public int triCount;
        }

        public readonly List<Part> parts = new List<Part>();
        public readonly List<string> meshesMissingChannel = new List<string>();
        public int uvChannel;
        public Vector2[] uv;
        public int[] tris;
        public int[] triIsland;
        public int islandCount;
        public Vector3[] lineSegments;
        int[] _wireVertices;
        int[] _weldedUvVertices;
        bool[] _referencedVertices;
        HashSet<(Renderer, int)> _slotsMissingUv;
        string _faceLayout;
        public float[] worldHeight;

        public int TriangleCount => tris.Length / 3;

        internal static bool HasUvChannel(Mesh mesh, int channel) => mesh != null && channel >= 0 && channel <= 3 &&
            mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel));

        internal bool SlotMissingUv(Renderer renderer, int slot) => _slotsMissingUv?.Contains((renderer, slot)) == true;

        internal string FaceLayout
        {
            get
            {
                if (_faceLayout != null) return _faceLayout;
                ulong hash = 14695981039346656037UL;
                void Add(int value) { unchecked { hash = (hash ^ (uint)value) * 1099511628211UL; } }
                Add(uvChannel);
                Add(uv.Length);
                Add(tris.Length);
                foreach (int index in tris) Add(index);
                foreach (var point in uv)
                {
                    Add(BitConverter.SingleToInt32Bits(point.x));
                    Add(BitConverter.SingleToInt32Bits(point.y));
                }
                return _faceLayout = hash.ToString("X16");
            }
        }

        public static MeshUV Build(Renderer renderer, int materialIndex, int uvChannel) =>
            Build(new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = renderer, materialIndex = materialIndex } }, uvChannel);

        internal static MeshUV BuildScenePicker(Renderer renderer, Mesh mesh, int materialIndex, ref PoseSample pose)
        {
            if (mesh == null || mesh.subMeshCount == 0) return null;
            var coords = mesh.uv;
            if (coords.Length == 0) return null;
            int sub = Mathf.Clamp(materialIndex, 0, mesh.subMeshCount - 1);
            var picker = new MeshUV { uv = coords, tris = mesh.GetTriangles(sub) };
            picker.parts.Add(new Part
            {
                renderer = renderer, mesh = mesh, submesh = sub, materialSlot = materialIndex,
                vertexCount = coords.Length, triCount = picker.TriangleCount,
            });
            picker.RefreshPoseUsing(ref pose);
            return picker;
        }

        public static MeshUV Build(List<TargetResolver.SlotRef> slots, int uvChannel)
        {
            PoseSample pose = default;
            var m = new MeshUV { uvChannel = uvChannel };
            var uvAll = new List<Vector2>();
            var posAll = new List<Vector3>();
            var partIdAll = new List<int>();
            var trisAll = new List<int>();
            var seen = new HashSet<(Mesh, int, int)>();
            var tmp = new List<Vector2>();

            foreach (var s in slots)
            {
                var mesh = TargetResolver.GetSharedMesh(s.renderer);
                if (mesh == null || mesh.subMeshCount == 0) continue;
                int sub = Mathf.Clamp(s.materialIndex, 0, mesh.subMeshCount - 1);
                int channel = s.uvChannel ?? uvChannel;
                if (s.uvChannel.HasValue && !HasUvChannel(mesh, channel))
                {
                    (m._slotsMissingUv ??= new HashSet<(Renderer, int)>()).Add((s.renderer, s.materialIndex));
                    continue;
                }
                if (!seen.Add((mesh, sub, channel))) continue;

                tmp.Clear();
                mesh.GetUVs(channel, tmp);
                if (s.uvChannel.HasValue && tmp.Count != mesh.vertexCount)
                {
                    (m._slotsMissingUv ??= new HashSet<(Renderer, int)>()).Add((s.renderer, s.materialIndex));
                    continue;
                }
                if (tmp.Count == 0 && channel != 0)
                {
                    m.meshesMissingChannel.Add(mesh.name);
                    channel = 0;
                    mesh.GetUVs(0, tmp);
                }
                if (tmp.Count == 0) continue;

                var positions = mesh.vertices;
                var part = new Part
                {
                    renderer = s.renderer, mesh = mesh, submesh = sub, materialSlot = s.materialIndex,
                    uvChannel = channel, vertexCount = tmp.Count, bindPositionsHash = BindPositionsHash(positions),
                    vertexOffset = uvAll.Count, triStart = trisAll.Count / 3,
                };
                uvAll.AddRange(tmp);
                posAll.AddRange(positions);
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
            m.RefreshPoseUsing(ref pose);
            if (m.meshesMissingChannel.Count > 0)
                Debug.LogWarning($"[Avistry] UV{uvChannel} is missing on {string.Join(", ", m.meshesMissingChannel)}; using UV0 for them.");
            return m;
        }

        public Part PartOfTriangle(int tri)
        {
            foreach (var p in parts)
                if (tri >= p.triStart && tri < p.triStart + p.triCount) return p;
            return null;
        }

        public (int renderer, int materialSlot, int localTri)[] IslandKeys()
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
                    keys[isl] = (rid, p.materialSlot, t - p.triStart);
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

            var weld = new Dictionary<(int, int, int, int, int, int), int>();
            _weldedUvVertices = new int[vCount];
            var used = _referencedVertices = new bool[vCount];
            foreach (var t in tris) used[t] = true;
            for (int i = 0; i < vCount; i++)
            {
                if (!used[i]) continue;
                var p = positions[i];
                var key = (partOf[i], Mathf.RoundToInt(uv[i].x * 65536f), Mathf.RoundToInt(uv[i].y * 65536f),
                           Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
                if (weld.TryGetValue(key, out int other)) Union(other, i);
                else weld[key] = other = i;
                _weldedUvVertices[i] = other;
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
            var seen = new HashSet<(int, int)>();
            var segs = new List<Vector3>(tris.Length * 2);
            var endpoints = new List<int>(tris.Length);
            for (int t = 0; t < tris.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    if (!seen.Add(key)) continue;
                    endpoints.Add(a);
                    endpoints.Add(b);
                    segs.Add(new Vector3(uv[a].x, uv[a].y, 0));
                    segs.Add(new Vector3(uv[b].x, uv[b].y, 0));
                }
            }
            lineSegments = segs.ToArray();
            _wireVertices = endpoints.ToArray();
        }

        internal void BuildPosedWireframe(Mesh mesh, IReadOnlyList<bool> shown)
        {
            var positions = PosedCombinedCached();
            var vertices = new List<Vector3>(_wireVertices.Length);
            int part = 0;
            for (int i = 0; i < _wireVertices.Length; i += 2)
            {
                int a = _wireVertices[i];
                while (part + 1 < parts.Count && a >= parts[part + 1].vertexOffset) part++;
                if (!shown[part]) continue;
                var from = positions[a];
                var to = positions[_wireVertices[i + 1]];
                if (!float.IsFinite(from.x) || !float.IsFinite(to.x)) continue;
                vertices.Add(from);
                vertices.Add(to);
            }

            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            var lines = new int[vertices.Count];
            for (int i = 0; i < lines.Length; i++) lines[i] = i;
            mesh.SetIndices(lines, MeshTopology.Lines, 0);
        }

        public Vector3[] SegmentsForIslands(HashSet<int> islands)
            => islands == null || islands.Count == 0 ? Array.Empty<Vector3>() :
                SegmentsForTriangles(t => islands.Contains(triIsland[t]));

        internal Vector3[] SegmentsForTriangles(Func<int, bool> selected)
        {
            var segs = new List<Vector3>();
            for (int t = 0; t < triIsland.Length; t++)
            {
                if (!selected(t)) continue;
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t * 3 + e], b = tris[t * 3 + (e + 1) % 3];
                    segs.Add(new Vector3(uv[a].x, uv[a].y, 0));
                    segs.Add(new Vector3(uv[b].x, uv[b].y, 0));
                }
            }
            return segs.ToArray();
        }


        public List<int> IslandsAtUV(Vector2 p)
        {
            var found = new List<int>();
            IncludeIslandsAtUV(p, found);
            return found;
        }

        internal void IncludeIslandsAtUV(Vector2 p, List<int> found)
        {
            var cell = _uvGrid[GridCell(p.x) + GridCell(p.y) * UvGridN];
            if (cell == null) return;
            foreach (int t in cell)
            {
                if (CoversIslandPoint(t, p) && !found.Contains(triIsland[t]))
                    found.Add(triIsland[t]);
            }
        }

        internal bool HasIslandAtUV(Vector2 point)
        {
            var cell = _uvGrid[GridCell(point.x) + GridCell(point.y) * UvGridN];
            if (cell != null)
                foreach (int triangle in cell)
                    if (CoversIslandPoint(triangle, point)) return true;
            return false;
        }

        bool CoversIslandPoint(int triangle, Vector2 p)
        {
            int first = triangle * 3;
            Vector2 a = uv[tris[first]], b = uv[tris[first + 1]], c = uv[tris[first + 2]];
            if (p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x))) return false;
            if (p.y < Mathf.Min(a.y, Mathf.Min(b.y, c.y)) || p.y > Mathf.Max(a.y, Mathf.Max(b.y, c.y))) return false;
            return Barycentric(p, a, b, c, out _, out _, out _);
        }

        internal List<int> TrianglesAtUV(Vector2 point)
        {
            var found = new List<int>();
            var cell = _uvGrid[GridCell(point.x) + GridCell(point.y) * UvGridN];
            if (cell == null) return found;
            foreach (int t in cell)
            {
                int i = t * 3;
                if (Barycentric(point, uv[tris[i]], uv[tris[i + 1]], uv[tris[i + 2]], out _, out _, out _))
                    found.Add(t);
            }
            return found;
        }

        internal bool TryTriangleAtUV(Vector2 point, int preferPiece, out int triangle)
        {
            triangle = -1;
            if (!float.IsFinite(point.x + point.y)) return false;
            var cell = _uvGrid[GridCell(point.x) + GridCell(point.y) * UvGridN];
            if (cell == null) return false;
            foreach (int face in cell)
            {
                int first = face * 3;
                if (!Barycentric(point, uv[tris[first]], uv[tris[first + 1]], uv[tris[first + 2]], out _, out _, out _)) continue;
                if (triangle < 0) triangle = face;
                if (preferPiece < 0 || triIsland[face] == preferPiece) { triangle = face; return true; }
            }
            return triangle >= 0;
        }

        List<int>[] _pieceNeighbours;
        Vector2Int[] _pieceTiles;
        Dictionary<Vector2Int, int> _tileCounts;

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

        internal Dictionary<Vector2Int, int> TileCounts()
        {
            if (_tileCounts != null) return _tileCounts;
            var counts = new Dictionary<Vector2Int, int>();
            foreach (var t in PieceTiles()) counts[t] = counts.TryGetValue(t, out int n) ? n + 1 : 1;
            return _tileCounts = counts;
        }

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
        Bounds?[] _posedBounds;
        Vector3 _poseKey;
        int _poseHash;

        internal struct PoseSample
        {
            public Renderer renderer;
            public Mesh mesh;
            public Vector3[] world;
            public Bounds bounds;
        }

        public void RefreshPose()
        {
            PoseSample pose = default;
            RefreshPoseUsing(ref pose);
        }

        internal void RefreshPoseUsing(ref PoseSample pose)
        {
            _posed = new Vector3[parts.Count][];
            _posedBounds = new Bounds?[parts.Count];
            var heights = new float[uv.Length];
            bool heightsOk = true;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.renderer == null) { heightsOk = false; continue; }
                Vector3[] w;
                try
                {
                    if (pose.world == null || pose.renderer != part.renderer || pose.mesh != part.mesh)
                    {
                        w = PosedWorldVertices(part.renderer, part.mesh);
                        if (w == null || w.Length == 0) { heightsOk = false; continue; }
                        var bounds = new Bounds(w[0], Vector3.zero);
                        for (int k = 1; k < w.Length; k++) bounds.Encapsulate(w[k]);
                        bounds.Expand(0.002f);
                        pose = new PoseSample { renderer = part.renderer, mesh = part.mesh, world = w, bounds = bounds };
                    }
                    w = _posed[i] = pose.world;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Avistry] Could not bake mesh pose, height gradients disabled: " + e.Message);
                    heightsOk = false;
                    continue;
                }
                for (int k = 0; k < w.Length && part.vertexOffset + k < heights.Length; k++) heights[part.vertexOffset + k] = w[k].y;
                _posedBounds[i] = pose.bounds;
            }
            worldHeight = heightsOk ? heights : null;
            _poseKey = PoseKey();
            _poseHash = PoseHash();
            _combined = null;
            _posedSize = null;
            PoseVersion++;
            _surfaceDirty = true;
        }


        public int PoseVersion { get; private set; }
        Vector3[] _combined;
        float? _posedSize;
        Mesh _surfaceMesh;
        bool _surfaceDirty = true;

        public Vector3[] PosedCombinedCached()
        {
            if (_posed == null) RefreshPose();
            return _combined ??= PosedCombined();
        }

        public float PosedSize
        {
            get
            {
                if (_posed == null) RefreshPose();
                return _posedSize ??= MeasurePosedSize();
            }
        }

        float MeasurePosedSize()
        {
            if (_referencedVertices == null) return MeasureExceptionalPosedSize();
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var world = _posed[pi];
                if (world == null) continue;
                int start = parts[pi].vertexOffset;
                int end = pi + 1 < parts.Count ? parts[pi + 1].vertexOffset : _referencedVertices.Length;
                if (world.Length > end - start) return MeasureExceptionalPosedSize();
                for (int i = 0; i < world.Length; i++)
                {
                    if (!_referencedVertices[start + i]) continue;
                    var point = world[i];
                    if (float.IsNaN(point.x)) continue;
                    if (!float.IsFinite(point.x + point.y + point.z)) return MeasureExceptionalPosedSize();
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }
            return min.x <= max.x ? (max - min).magnitude : 0f;
        }

        float MeasureExceptionalPosedSize()
        {
            var positions = PosedCombinedCached();
            var b = new Bounds();
            bool any = false;
            foreach (int i in tris)
            {
                var p = positions[i];
                if (float.IsNaN(p.x)) continue;
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
            return any ? b.size.magnitude : 0f;
        }

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

        [StructLayout(LayoutKind.Sequential)]
        struct SurfaceVertex
        {
            internal Vector3 uvPosition, normal, worldPosition;
        }

        static readonly VertexAttributeDescriptor[] SurfaceLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),
        };

        public Mesh SurfaceMesh()
        {
            if (_surfaceMesh != null && !_surfaceDirty) return _surfaceMesh;
            var pos = PosedCombinedCached();
            var vertices = new SurfaceVertex[tris.Length];
            int count = 0;
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
                    vertices[count++] = new SurfaceVertex
                    {
                        uvPosition = new Vector3(uv[i].x, uv[i].y, 0),
                        normal = fn,
                        worldPosition = pos[i],
                    };
                }
            }
            if (_surfaceMesh == null)
                _surfaceMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "ATS surface (UV space)", indexFormat = IndexFormat.UInt32 };
            bool rebuildIndices = count == 0 || _surfaceMesh.vertexCount != count;
            if (rebuildIndices)
            {
                _surfaceMesh.Clear();
                _surfaceMesh.indexFormat = IndexFormat.UInt32;
            }
            if (count > 0)
            {
                if (rebuildIndices) _surfaceMesh.SetVertexBufferParams(count, SurfaceLayout);
                _surfaceMesh.SetVertexBufferData(vertices, 0, 0, count, 0, MeshUpdateFlags.DontRecalculateBounds);
            }
            else
            {
                var empty = Array.Empty<Vector3>();
                _surfaceMesh.SetVertices(empty);
                _surfaceMesh.SetNormals(empty);
                _surfaceMesh.SetUVs(0, empty);
            }
            if (rebuildIndices)
            {
                var indices = count > 0 ? new int[count] : Array.Empty<int>();
                for (int i = 0; i < count; i++) indices[i] = i;
                _surfaceMesh.SetTriangles(indices, 0, false);
            }
            _surfaceMesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0), new Vector3(1000, 1000, 1000));
            _surfaceDirty = false;
            return _surfaceMesh;
        }

        public void ReleaseSurfaceMesh()
        {
            if (_surfaceMesh != null) UnityEngine.Object.DestroyImmediate(_surfaceMesh);
            _surfaceMesh = null;
        }

        public bool PoseChanged() => _posed == null || (PoseKey() - _poseKey).sqrMagnitude > 1e-10f || PoseHash() != _poseHash;

        int PoseHash()
        {
            var hc = new HashCode();
            foreach (var p in parts)
            {
                var r = p.renderer;
                if (r == null) continue;
                AddPoseMatrix(ref hc, r.transform.localToWorldMatrix);
                if (r is SkinnedMeshRenderer smr)
                {
                    var root = smr.rootBone;
                    hc.Add(root);
                    if (root != null) AddPoseMatrix(ref hc, root.localToWorldMatrix);
                    foreach (var bone in smr.bones ?? Array.Empty<Transform>())
                    {
                        bool hasBone = bone != null;
                        hc.Add(hasBone);
                        if (hasBone) AddPoseMatrix(ref hc, bone.localToWorldMatrix);
                    }
                    var sm = smr.sharedMesh;
                    int bs = sm != null ? sm.blendShapeCount : 0;
                    for (int k = 0; k < bs; k++) hc.Add(smr.GetBlendShapeWeight(k));
                }
            }
            return hc.ToHashCode();
        }

        static void AddPoseMatrix(ref HashCode hash, Matrix4x4 matrix)
        {
            hash.Add(matrix.m00); hash.Add(matrix.m10); hash.Add(matrix.m20);
            hash.Add(matrix.m01); hash.Add(matrix.m11); hash.Add(matrix.m21);
            hash.Add(matrix.m02); hash.Add(matrix.m12); hash.Add(matrix.m22);
            hash.Add(matrix.m03); hash.Add(matrix.m13); hash.Add(matrix.m23);
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

        public Transform AvatarRoot()
        {
            var r = parts.Count > 0 ? parts[0].renderer : null;
            if (r == null) return null;
            var anim = r.GetComponentInParent<Animator>();
            return anim != null ? anim.transform : r.transform.root;
        }

        public static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w) =>
            Barycentric(p, a, b, c, out u, out v, out w);

        int[] _highlightNeighbours;
        Vector3[] _highlightFaceNormals;
        HighlightVertex[] _highlightVertices;
        int[] _highlightIndices;
        readonly List<int> _highlightLines = new List<int>();

        [StructLayout(LayoutKind.Sequential)]
        struct HighlightVertex
        {
            internal Vector3 position, normal;
            internal Color32 corner;
            internal Vector3 outgoing, incoming;
        }

        static readonly VertexAttributeDescriptor[] HighlightLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3)
        };

        int[] HighlightNeighbours()
        {
            if (_highlightNeighbours != null) return _highlightNeighbours;
            var neighbours = new int[tris.Length];
            Array.Fill(neighbours, -1);
            var edges = new Dictionary<(Vector3, Vector3), BrushEdgeUse>();
            foreach (var part in parts)
            {
                if (part.renderer == null || part.mesh == null || part.mesh.vertexCount != part.vertexCount) continue;
                var local = part.mesh.vertices;
                edges.Clear();
                for (int t = part.triStart; t < part.triStart + part.triCount; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = t * 3 + k, b = t * 3 + (k + 1) % 3;
                        CountBrushEdge(edges, local[tris[a] - part.vertexOffset], local[tris[b] - part.vertexOffset], a);
                    }
                foreach (var edge in edges.Values)
                    if (edge.count == 2)
                    {
                        int a = WeldedCorner(edge.first), b = WeldedCorner(NextBoundaryCorner(edge.first));
                        int c = WeldedCorner(edge.second), d = WeldedCorner(NextBoundaryCorner(edge.second));
                        int seam = (a == c && b == d || a == d && b == c) ? 0 : 1;
                        neighbours[edge.first] = (edge.second / 3 << 1) | seam;
                        neighbours[edge.second] = (edge.first / 3 << 1) | seam;
                    }
            }
            return _highlightNeighbours = neighbours;
        }

        public void BuildHighlight(Mesh mesh, Func<int, bool> islandFilter)
            => BuildSceneHighlight(mesh, islandFilter, false, null);

        internal void BuildSceneHighlight(Mesh mesh, Func<int, bool> selected, bool faces, IReadOnlyList<bool> shown)
        {
            mesh.Clear();
            var positions = PosedCombinedCached();
            var neighbours = HighlightNeighbours();
            var faceNormals = _highlightFaceNormals ??= new Vector3[TriangleCount];
            Array.Clear(faceNormals, 0, faceNormals.Length);
            int corners = 0;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                if (shown != null && !shown[pi]) continue;
                if (part.renderer == null || !part.renderer.gameObject.activeInHierarchy) continue;
                for (int t = part.triStart; t < part.triStart + part.triCount; t++)
                {
                    if (!selected(faces ? t : triIsland[t])) continue;
                    int i = t * 3;
                    var a = positions[tris[i]];
                    var cross = Vector3.Cross(positions[tris[i + 1]] - a, positions[tris[i + 2]] - a);
                    float length = cross.magnitude;
                    if (!(length > 0f) || !float.IsFinite(length)) continue;
                    faceNormals[t] = cross / length;
                    corners += 3;
                }
            }

            if (corners == 0) { mesh.subMeshCount = 2; return; }
            if (_highlightVertices == null || _highlightVertices.Length < corners)
            {
                int capacity = Mathf.NextPowerOfTwo(corners);
                _highlightVertices = new HighlightVertex[capacity];
                _highlightIndices = new int[capacity];
                for (int i = 0; i < capacity; i++) _highlightIndices[i] = i;
            }
            _highlightLines.Clear();
            Vector3 Across(int t, int edge)
            {
                int neighbour = neighbours[t * 3 + edge];
                if (neighbour < 0) return Vector3.zero;
                return faceNormals[neighbour >> 1] * (1 + (neighbour & 1));
            }
            int start = 0;
            for (int t = 0; t < TriangleCount; t++)
            {
                var normal = faceNormals[t];
                if (normal == Vector3.zero) continue;
                for (int k = 0; k < 3; k++)
                {
                    var edge = Across(t, k);
                    _highlightVertices[start + k] = new HighlightVertex
                    {
                        position = positions[tris[t * 3 + k]], normal = normal,
                        outgoing = edge, incoming = Across(t, (k + 2) % 3),
                        corner = new Color32((byte)(k == 0 ? 255 : 0), (byte)(k == 1 ? 255 : 0),
                            (byte)(k == 2 ? 255 : 0), 255)
                    };
                    if (edge == Vector3.zero) { _highlightLines.Add(start + k); _highlightLines.Add(start + (k + 1) % 3); }
                }
                start += 3;
            }
            mesh.indexFormat = corners > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertexBufferParams(corners, HighlightLayout);
            mesh.SetVertexBufferData(_highlightVertices, 0, 0, corners, 0, MeshUpdateFlags.DontRecalculateBounds);
            mesh.subMeshCount = 2;
            mesh.SetIndices(_highlightIndices, 0, corners, MeshTopology.Triangles, 0, false);
            mesh.SetIndices(_highlightLines, MeshTopology.Lines, 1, false);
            mesh.RecalculateBounds();
        }

        public struct SurfaceHit
        {
            public Vector3 point;
            public Vector3 normal;
            public Vector2 uv;
            public int island;
            public int triangle;
            public float uvPerWorld;
            public Vector3 bary;
            public bool backFace;
        }

        public bool Raycast(Ray ray, out SurfaceHit hit) => RaycastFiltered(ray, out hit, null);

        internal bool RaycastPaintGeometry(Ray ray, out SurfaceHit hit) => RaycastCore(ray, out hit, null, default, null, includeHidden: true);

        internal bool RaycastFiltered(Ray ray, out SurfaceHit hit, Predicate<Part> includePart) =>
            RaycastCore(ray, out hit, includePart, default, null);

        internal bool RaycastBlocker(Ray ray, out SurfaceHit hit, PoiyomiTileDiscard.TriangleFilter tileFilter, CullMode cull, SceneCutout cutout) =>
            RaycastCore(ray, out hit, null, tileFilter, null, cull, cutout);

        internal bool RaycastScene(Ray ray, out SurfaceHit hit, Predicate<Part> includePart,
                                   Func<Part, PoiyomiTileDiscard.TriangleFilter> partFilter, Func<Part, CullMode> partCull = null) =>
            RaycastCore(ray, out hit, includePart, default, partFilter, partCull: partCull);

        bool RaycastCore(Ray ray, out SurfaceHit hit, Predicate<Part> includePart, PoiyomiTileDiscard.TriangleFilter tileFilter,
                         Func<Part, PoiyomiTileDiscard.TriangleFilter> partFilter, CullMode cull = CullMode.Off, SceneCutout cutout = default,
                         Func<Part, CullMode> partCull = null, bool includeHidden = false)
        {
            hit = default;
            if (tileFilter.AllDiscarded) return false;
            var closest = new RaycastCandidate { distance = float.MaxValue, triangle = -1 };
            int bestPart = -1;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                if (part.renderer == null || !includeHidden && (!part.renderer.enabled || !part.renderer.gameObject.activeInHierarchy)) continue;
                if (includePart != null && !includePart(part)) continue;
                var world = Posed(pi);
                if (world == null) continue;
                if (_posedBounds[pi] is Bounds pb && !pb.IntersectRay(ray)) continue;
                var filter = partFilter != null ? partFilter(part) : tileFilter;
                if (filter.AllDiscarded) continue;
                int previous = closest.triangle;
                RaycastPart(pi, world, ray, filter, partCull?.Invoke(part) ?? cull, cutout, ref closest);
                if (closest.triangle != previous) bestPart = pi;
            }
            if (bestPart < 0) return false;

            int bestTri = closest.triangle, at = bestTri * 3, off = parts[bestPart].vertexOffset;
            var positions = _posed[bestPart];
            Vector3 b0 = positions[tris[at] - off], b1 = positions[tris[at + 1] - off], b2 = positions[tris[at + 2] - off];
            Vector2 t0 = uv[tris[at]], t1 = uv[tris[at + 1]], t2 = uv[tris[at + 2]];
            float bu = closest.u, bv = closest.v;
            var n = Vector3.Cross(b1 - b0, b2 - b0);
            float worldArea = n.magnitude * 0.5f;
            float uvArea = Mathf.Abs((t1.x - t0.x) * (t2.y - t0.y) - (t1.y - t0.y) * (t2.x - t0.x)) * 0.5f;
            n = n.normalized;
            bool backFace = Vector3.Dot(n, ray.direction) > 0;
            if (backFace) n = -n;

            hit = new SurfaceHit
            {
                point = ray.GetPoint(closest.distance),
                normal = n,
                uv = t0 * (1 - bu - bv) + t1 * bu + t2 * bv,
                island = triIsland == null ? -1 : triIsland[bestTri],
                triangle = bestTri,
                uvPerWorld = worldArea > 1e-12f && uvArea > 0 ? Mathf.Sqrt(uvArea / worldArea) : 0f,
                bary = new Vector3(1 - bu - bv, bu, bv),
                backFace = backFace,
            };
            return true;
        }

        public int PickIslandWorld(Ray ray) => Raycast(ray, out var hit) ? hit.island : -1;

        static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, CullMode cull,
                                out float dist, out float u, out float v)
        {
            dist = 0; u = 0; v = 0;
            Vector3 e1 = v1 - v0, e2 = v2 - v0;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f || (cull == CullMode.Back && det < 0f) || (cull == CullMode.Front && det > 0f)) return false;
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

        internal static Matrix4x4 BakeSkinPose(SkinnedMeshRenderer renderer, Mesh baked)
        {
            renderer.BakeMesh(baked, false);
            var transform = renderer.transform;
            return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        }

        public static Vector3[] PosedWorldVertices(Renderer renderer, Mesh shared)
        {
            Vector3[] local;
            Matrix4x4 toWorld;
            if (renderer is SkinnedMeshRenderer smr)
            {
                var baked = new Mesh();
                try
                {
                    toWorld = BakeSkinPose(smr, baked);
                    local = baked.vertices;
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            else
            {
                local = shared.vertices;
                toWorld = renderer.transform.localToWorldMatrix;
            }
            if (local == null || local.Length == 0) return null;
            for (int i = 0; i < local.Length; i++) local[i] = toWorld.MultiplyPoint3x4(local[i]);
            return local;
        }


        public float[] ComputeGradientT(GradientMode mode)
        {
            var t = new float[uv.Length];
            if (mode == GradientMode.UvV || worldHeight == null)
            {
                for (int i = 0; i < uv.Length; i++) t[i] = 1f - uv[i].y;
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


        public void Rasterize(int w, int h, Func<int, bool> triFilter, Action<int, int, float, float, float> write) =>
            RasterizeTriangles(w, h, triFilter, write, default);

        internal void RasterizeTriangles(int w, int h, Func<int, bool> triFilter, Action<int, int, float, float, float> write,
                                CancellationToken cancellation)
        {
            for (int t = 0; t < triIsland.Length; t++)
            {
                cancellation.ThrowIfCancellationRequested();
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
                    if ((y & 15) == 0) cancellation.ThrowIfCancellationRequested();
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

        internal struct PieceInfo
        {
            public int id;
            public int part;
            public Rect bounds;
            public float area;
            public Color meanColour;
        }

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

        public byte[] RasterIslandMask(int w, int h, HashSet<int> islands, int dilate) =>
            RasterIslandMaskWithCancellation(w, h, islands, dilate, default);

        internal byte[] RasterIslandMaskWithCancellation(int w, int h, HashSet<int> islands, int dilate, CancellationToken cancellation)
        {
            var mask = new byte[w * h];
            if (islands?.Count == 0) return mask;
            int radiusX = Math.Min(Math.Max(0, dilate), w - 1);
            int radiusY = Math.Min(Math.Max(0, dilate), h - 1);
            RasterIslandSpans(w, h, 0, h, islands, (y, from, to, id) =>
            {
                int left = from - Math.Min(radiusX, from);
                int right = to + Math.Min(radiusX, w - 1 - to);
                int bottom = y + Math.Min(radiusY, h - 1 - y);
                for (int row = Math.Max(0, y - radiusY); row <= bottom; row++)
                    mask.AsSpan(row * w + left, right - left + 1).Fill(255);
            }, cancellation);
            return mask;
        }

        internal byte[] RasterFaceMask(int w, int h, HashSet<int> faces)
        {
            var mask = new byte[w * h];
            Rasterize(w, h, faces.Contains, (pixel, t, a, b, c) => mask[pixel] = 255);
            return mask;
        }

        public ushort[] RasterIslandIds(int w, int h, int dilate) => RasterIslandIdMap(w, h, dilate, default);

        internal MeshUV CopyIslandRasterInput() => new MeshUV
        {
            uv = (Vector2[])uv.Clone(),
            tris = (int[])tris.Clone(),
            triIsland = (int[])triIsland.Clone(),
        };

        internal bool MatchesIslandRasterInput(MeshUV snapshot) =>
            MemoryMarshal.AsBytes(uv.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(snapshot.uv.AsSpan())) &&
            tris.AsSpan().SequenceEqual(snapshot.tris) && triIsland.AsSpan().SequenceEqual(snapshot.triIsland);

        internal ushort[] RasterIslandIdMap(int w, int h, int dilate, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var val = new ushort[w * h];
            var covered = new bool[w * h];
            RasterizeTriangles(w, h, null, (px, t, a, b, c) =>
            {
                val[px] = (ushort)Mathf.Min(triIsland[t] + 1, 65535);
                covered[px] = true;
            }, cancellation);
            DilateValues(val, covered, w, h, dilate, cancellation);
            return val;
        }

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

        public static void Dilate(ushort[] val, bool[] covered, int w, int h, int radius) =>
            DilateValues(val, covered, w, h, radius, default);

        internal static void DilateValues(ushort[] val, bool[] covered, int w, int h, int radius, CancellationToken cancellation,
                                 List<int> frontier = null)
        {
            if (radius <= 0) return;
            bool orderedPaint = frontier != null;
            if (frontier == null)
            {
                frontier = new List<int>();
                for (int i = 0; i < covered.Length; i++)
                {
                    if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
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
            }

            var next = new List<int>();
            for (int step = 0; step < radius && frontier.Count > 0; step++)
            {
                next.Clear();
                int processed = 0;
                foreach (int i in frontier)
                {
                    if ((processed++ & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                    if (orderedPaint)
                    {
                        int ownerY = i / w;
                        int row = ownerY * w;
                        int ownerX = i - row;
                        int left = ownerX > 0 ? ownerX - 1 : ownerX;
                        int right = ownerX + 1 < w ? ownerX + 1 : ownerX;
                        int top = ownerY > 0 ? row - w : row;
                        int bottom = ownerY + 1 < h ? row + w : row;
                        ushort owner = val[i];
                        for (int neighbourRow = top; neighbourRow <= bottom; neighbourRow += w)
                            for (int n = neighbourRow + left, last = neighbourRow + right; n <= last; n++)
                            {
                                if (covered[n]) continue;
                                covered[n] = true;
                                val[n] = owner;
                                next.Add(n);
                            }
                        continue;
                    }
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

        public Color32[] SelectionOverlay(int w, int h, HashSet<int> islands, Color32 color)
        {
            var px = new Color32[w * h];
            if (islands == null || islands.Count == 0) return px;
            RasterIslandSpans(w, h, 0, h, islands, (y, from, to, id) =>
                px.AsSpan(y * w + from, to - from + 1).Fill(color), default);
            return px;
        }

        internal Color32[] FaceSelectionOverlay(int w, int h, HashSet<int> faces, Color32 color)
        {
            var px = new Color32[w * h];
            Rasterize(w, h, faces.Contains, (i, t, a, b, c) => px[i] = color);
            return px;
        }
    }
}
