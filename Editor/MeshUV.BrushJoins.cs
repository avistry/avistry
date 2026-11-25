using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        struct BrushEdgeUse
        {
            internal int first, second, count;
        }

        struct SharedBrushEdgeUse
        {
            internal int first, second, count;
            internal Part firstPart, secondPart;
        }

        List<(int from, int to)>[] _brushJoins;
        List<int> _brushJoinQueue;
        HashSet<int> _brushJoinVisited;
        Dictionary<Part, PoiyomiTileDiscard.TriangleFilter> _brushJoinTiles;
        Dictionary<(Part, Part), bool> _brushJoinSlotChecks;

        internal void PrepareBrushJoins()
        {
            if (islandCount > 1) _brushJoins ??= BuildBrushJoins();
        }

        internal void IncludeBrushJoins(int triangle, Vector3 bary, float radius, List<int> pieces,
            Func<Part, PoiyomiTileDiscard.TriangleFilter> visibility, Func<Part, Part, bool> canJoinSlots = null)
        {
            if ((uint)triangle >= (uint)TriangleCount || islandCount < 2 || !float.IsFinite(radius) || radius <= 0f) return;
            _brushJoins ??= BuildBrushJoins();
            int origin = triIsland[triangle];
            if (_brushJoins[origin] == null || !SurfacePoint(triangle, bary, out var point, out _)) return;

            var posed = PosedCombinedCached();
            float tolerance = Mathf.Max(1e-6f, PosedSize * 1e-5f);
            float toleranceSquared = tolerance * tolerance;
            float radiusSquared = radius * radius;
            _brushJoinQueue ??= new();
            _brushJoinVisited ??= new();
            _brushJoinQueue.Clear();
            _brushJoinVisited.Clear();
            _brushJoinTiles?.Clear();
            _brushJoinSlotChecks?.Clear();
            var originPart = PartOfTriangle(triangle);
            if (originPart == null) return;
            PoiyomiTileDiscard.TriangleFilter originTiles = default;
            bool readOriginTiles = false;
            Part Owner(int t) => t >= originPart.triStart && t < originPart.triStart + originPart.triCount
                ? originPart : PartOfTriangle(t);
            bool Visible(Part part, int t)
            {
                if (visibility == null) return true;
                if (part == originPart)
                {
                    if (!readOriginTiles)
                    {
                        originTiles = visibility(part);
                        readOriginTiles = true;
                    }
                    return !originTiles.Discards(t);
                }
                _brushJoinTiles ??= new();
                if (!_brushJoinTiles.TryGetValue(part, out var filter))
                    _brushJoinTiles.Add(part, filter = visibility(part));
                return !filter.Discards(t);
            }

            _brushJoinQueue.Add(origin);
            _brushJoinVisited.Add(origin);
            for (int i = 0; i < _brushJoinQueue.Count; i++)
            {
                foreach (var (from, to) in _brushJoins[_brushJoinQueue[i]])
                {
                    int next = triIsland[to / 3];
                    if (_brushJoinVisited.Contains(next)) continue;
                    var a = posed[tris[from]];
                    var b = posed[tris[NextBoundaryCorner(from)]];
                    var edge = b - a;
                    float length = edge.sqrMagnitude;
                    if (!(length > 0f)) continue;
                    var offset = point - a;
                    float at = Mathf.Clamp01(Vector3.Dot(offset, edge) / length);
                    if (!((offset - edge * at).sqrMagnitude < radiusSquared)) continue;

                    if (!((a - posed[tris[NextBoundaryCorner(to)]]).sqrMagnitude <= toleranceSquared &&
                          (b - posed[tris[to]]).sqrMagnitude <= toleranceSquared)) continue;
                    int fromTriangle = from / 3, toTriangle = to / 3;
                    var first = Owner(fromTriangle);
                    var second = Owner(toTriangle);
                    if (first == null || second == null) continue;
                    if (first != second)
                    {
                        if (canJoinSlots == null) continue;
                        _brushJoinSlotChecks ??= new();
                        if (!_brushJoinSlotChecks.TryGetValue((first, second), out bool allowed))
                            _brushJoinSlotChecks.Add((first, second), allowed = canJoinSlots(first, second));
                        if (!allowed) continue;
                    }
                    if (!Visible(first, fromTriangle) || !Visible(second, toTriangle)) continue;
                    _brushJoinVisited.Add(next);
                    _brushJoinQueue.Add(next);
                    if (!pieces.Contains(next)) pieces.Add(next);
                }
            }
        }

        List<(int from, int to)>[] BuildBrushJoins()
        {
            var joins = new List<(int, int)>[islandCount];
            var shared = parts.Count > 1 ? IndexSharedBrushBoundaries() : null;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                int vertexEnd = i + 1 < parts.Count ? parts[i + 1].vertexOffset : uv.Length;
                if (part.mesh == null || part.mesh.vertexCount != vertexEnd - part.vertexOffset) continue;
                var positions = part.mesh.vertices;
                Vector3 At(int corner) => positions[tris[corner] - part.vertexOffset];
                var edges = new Dictionary<(Vector3, Vector3), BrushEdgeUse>();
                int end = (part.triStart + part.triCount) * 3;
                for (int corner = part.triStart * 3; corner < end; corner++)
                    CountBrushEdge(edges, At(corner), At(NextBoundaryCorner(corner)), corner);

                Dictionary<(Vector3, Vector3), SharedBrushEdgeUse> boundaries = null;
                shared?.TryGetValue(part, out boundaries);
                foreach (var (key, use) in edges)
                {
                    if (use.count == 1 && boundaries != null)
                        CountSharedBrushBoundary(boundaries, key, use.first, part);
                    if (use.count != 2 || use.first / 3 == use.second / 3) continue;
                    int a = triIsland[use.first / 3], b = triIsland[use.second / 3];
                    if (a == b || !At(use.first).Equals(At(NextBoundaryCorner(use.second))) ||
                        !At(use.second).Equals(At(NextBoundaryCorner(use.first)))) continue;
                    (joins[a] ??= new()).Add((use.first, use.second));
                    (joins[b] ??= new()).Add((use.second, use.first));
                }
                AddNearBrushJoins(joins, edges.Values, At, part.mesh.bounds);
            }
            if (shared != null)
                foreach (var boundaries in shared.Values.Distinct()) AddSharedBrushJoins(joins, boundaries);
            return joins;
        }

        static void CountBrushEdge(Dictionary<(Vector3, Vector3), BrushEdgeUse> edges, Vector3 a, Vector3 b, int corner)
        {
            if (!TryBrushEdgeKey(a, b, out var key)) return;
            edges.TryGetValue(key, out var use);
            if (use.count == 0) use.first = corner;
            else if (use.count == 1) use.second = corner;
            use.count++;
            edges[key] = use;
        }

        static bool TryBrushEdgeKey(Vector3 a, Vector3 b, out (Vector3, Vector3) key)
        {
            key = default;
            if (a.Equals(b) || !float.IsFinite(a.x + a.y + a.z + b.x + b.y + b.z)) return false;
            key = BrushVertexBefore(a, b) ? (a, b) : (b, a);
            return true;
        }

        Dictionary<Part, Dictionary<(Vector3, Vector3), SharedBrushEdgeUse>> IndexSharedBrushBoundaries()
        {
            var shared = new Dictionary<Part, Dictionary<(Vector3, Vector3), SharedBrushEdgeUse>>();
            foreach (var group in parts.GroupBy(part => (part.renderer, part.mesh)))
            {
                if (group.Key.renderer == null || group.Key.mesh == null) continue;
                var slots = group.GroupBy(part => part.submesh).Where(s => s.Count() == 1)
                    .Select(s => s.First()).ToArray();
                if (slots.Length < 2) continue;
                var boundaries = new Dictionary<(Vector3, Vector3), SharedBrushEdgeUse>();
                foreach (var slot in slots) shared.Add(slot, boundaries);
            }
            return shared;
        }

        static void CountSharedBrushBoundary(Dictionary<(Vector3, Vector3), SharedBrushEdgeUse> boundaries,
            (Vector3, Vector3) key, int corner, Part part)
        {
            boundaries.TryGetValue(key, out var use);
            if (use.count == 0) { use.first = corner; use.firstPart = part; }
            else if (use.count == 1) { use.second = corner; use.secondPart = part; }
            use.count++;
            boundaries[key] = use;
        }

        void AddSharedBrushJoins(List<(int from, int to)>[] joins, Dictionary<(Vector3, Vector3), SharedBrushEdgeUse> boundaries)
        {
            var possible = boundaries.Where(e => e.Value.count == 2).ToArray();
            if (possible.Length == 0) return;
            var mesh = possible[0].Value.firstPart.mesh;
            var positions = mesh.vertices;
            var checkedParts = new Dictionary<Part, bool>();
            var indices = new int[mesh.subMeshCount][];
            var covered = new int[mesh.subMeshCount];
            var pairs = new Dictionary<(Vector3, Vector3), SharedBrushEdgeUse>();

            int[] Indices(int sub) => indices[sub] ??= mesh.GetTopology(sub) == MeshTopology.Triangles
                ? mesh.GetTriangles(sub) : Array.Empty<int>();
            bool Valid(Part part)
            {
                if ((uint)part.submesh >= (uint)indices.Length) return false;
                if (!checkedParts.TryGetValue(part, out bool matches))
                    checkedParts.Add(part, matches = MatchesSharedBrushIndices(part, positions.Length, Indices(part.submesh)));
                return matches;
            }
            Vector3 At(int corner, Part part) => positions[tris[corner] - part.vertexOffset];

            foreach (var (key, use) in possible)
            {
                var first = use.firstPart;
                var second = use.secondPart;
                if (!Valid(first) || !Valid(second) || triIsland[use.first / 3] == triIsland[use.second / 3]) continue;
                int afterFirst = NextBoundaryCorner(use.first), afterSecond = NextBoundaryCorner(use.second);
                if (!At(use.first, first).Equals(At(afterSecond, second)) ||
                    !At(afterFirst, first).Equals(At(use.second, second)) ||
                    At(NextBoundaryCorner(afterFirst), first).Equals(At(NextBoundaryCorner(afterSecond), second))) continue;
                pairs.Add(key, use);
                covered[first.submesh]++;
                covered[second.submesh]++;
            }

            int pairCount = pairs.Count;
            bool[] endpointVertices = null;
            for (int sub = 0; sub < indices.Length && pairs.Count > 0; sub++)
            {
                if (covered[sub] == pairCount) continue;
                var other = Indices(sub);
                if (other.Length == 0) continue;
                endpointVertices ??= SharedBrushEndpointVertices(positions, pairs.Keys);
                for (int corner = 0; corner < other.Length && pairs.Count > 0; corner++)
                {
                    int start = other[corner];
                    if (!endpointVertices[start]) continue;
                    int end = other[NextBoundaryCorner(corner)];
                    if (endpointVertices[end] && TryBrushEdgeKey(positions[start], positions[end], out var key) &&
                        pairs.TryGetValue(key, out var use) && sub != use.firstPart.submesh && sub != use.secondPart.submesh)
                        pairs.Remove(key);
                }
            }

            foreach (var use in pairs.Values)
            {
                int a = triIsland[use.first / 3], b = triIsland[use.second / 3];
                (joins[a] ??= new()).Add((use.first, use.second));
                (joins[b] ??= new()).Add((use.second, use.first));
            }
        }

        static bool[] SharedBrushEndpointVertices(Vector3[] positions, IEnumerable<(Vector3, Vector3)> keys)
        {
            var endpoints = new HashSet<Vector3>();
            foreach (var (a, b) in keys) { endpoints.Add(a); endpoints.Add(b); }
            var matches = new bool[positions.Length];
            for (int i = 0; i < positions.Length; i++) matches[i] = endpoints.Contains(positions[i]);
            return matches;
        }

        bool MatchesSharedBrushIndices(Part part, int vertexCount, int[] indices)
        {
            if (part.vertexCount != vertexCount || part.triCount * 3 != indices.Length) return false;
            int start = part.triStart * 3;
            for (int i = 0; i < indices.Length; i++)
                if (tris[start + i] != part.vertexOffset + indices[i]) return false;
            return true;
        }

        void AddNearBrushJoins(List<(int from, int to)>[] joins, IEnumerable<BrushEdgeUse> edges,
            Func<int, Vector3> at, Bounds bounds)
        {
            float tolerance = Mathf.Max(1e-6f, bounds.size.magnitude * 1e-5f);
            if (!float.IsFinite(tolerance)) return;
            float toleranceSquared = tolerance * tolerance;
            var min = bounds.min;
            Vector3Int Cell(Vector3 point) => Vector3Int.FloorToInt((point - min) / tolerance);
            var starts = new Dictionary<Vector3Int, List<int>>();
            foreach (var edge in edges)
            {
                if (edge.count != 1) continue;
                var a = at(edge.first);
                var b = at(NextBoundaryCorner(edge.first));
                if ((b - a).sqrMagnitude <= 4f * toleranceSquared) continue;
                var cell = Cell(a);
                if (!starts.TryGetValue(cell, out var entries)) starts.Add(cell, entries = new());
                entries.Add(edge.first);
            }

            var partners = new Dictionary<int, int>();
            foreach (var entries in starts.Values)
                foreach (int edge in entries)
                {
                    var a = at(edge);
                    var b = at(NextBoundaryCorner(edge));
                    int partner = FindNearBrushPartner(edge, a, b, Cell(b), toleranceSquared, starts, at);
                    if (partner >= 0) partners.Add(edge, partner);
                }

            foreach (var (edge, partner) in partners)
            {
                if (edge >= partner || !partners.TryGetValue(partner, out int reverse) || reverse != edge) continue;
                int a = triIsland[edge / 3], b = triIsland[partner / 3];
                if (a == b) continue;
                (joins[a] ??= new()).Add((edge, partner));
                (joins[b] ??= new()).Add((partner, edge));
            }
        }

        static int FindNearBrushPartner(int edge, Vector3 a, Vector3 b, Vector3Int finish,
            float toleranceSquared, Dictionary<Vector3Int, List<int>> starts, Func<int, Vector3> at)
        {
            int partner = -1;
            for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        if (!starts.TryGetValue(finish + new Vector3Int(x, y, z), out var nearby)) continue;
                        foreach (int other in nearby)
                        {
                            if (other / 3 == edge / 3 || (b - at(other)).sqrMagnitude > toleranceSquared ||
                                (a - at(NextBoundaryCorner(other))).sqrMagnitude > toleranceSquared) continue;
                            if (partner >= 0) return -1;
                            partner = other;
                        }
                    }
            return partner;
        }

        static bool BrushVertexBefore(Vector3 a, Vector3 b) =>
            a.x < b.x || a.x == b.x && (a.y < b.y || a.y == b.y && a.z < b.z);
    }
}
