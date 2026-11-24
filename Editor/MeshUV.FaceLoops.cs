using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        internal enum FaceLoopStatus { Found, Stopped, NoPair, NeedEdge, Overlap, TooLarge }
        const int FaceLoopMaxTriangles = 250000, FaceLoopMaxLength = 4096;

        struct LoopEdge
        {
            public int first, second, count;
        }

        readonly struct LoopFace
        {
            public readonly int first, second, a, b, c, d;
            readonly int boundary;

            public LoopFace(int first, int second, int a, int b, int c, int d, int boundary = -1)
            {
                this.first = first; this.second = second;
                this.a = a; this.b = b; this.c = c; this.d = d;
                this.boundary = boundary;
            }

            public int Sides => second < 0 ? 3 : 4;
            public bool CanCross(int side) => side != boundary;
            public int Exit(int entered) => Sides == 4 ? (entered + 2) & 3 : 3 - boundary - entered;
            public int Vertex(int corner) => (corner % Sides) switch { 0 => a, 1 => b, 2 => c, _ => d };
        }

        Dictionary<(int, int), LoopEdge> _loopEdges;
        LoopFace[] _loopFaces;
        int[] _triangleLoopFace;
        (int, int) _lastLoopEdge;
        IReadOnlyList<int> _lastLoopTriangles;
        FaceLoopStatus _lastLoopStatus;

        static (int, int) LoopEdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

        internal FaceLoopStatus FindFaceLoop(IReadOnlyList<int> hits, Func<int, Vector2> project, Vector2 pointer,
                                             out IReadOnlyList<int> selected)
        {
            selected = Array.Empty<int>();
            if (TriangleCount > FaceLoopMaxTriangles) return FaceLoopStatus.TooLarge;
            if (_loopFaces == null) BuildFaceLoopTopology();
            int pickedFace = -1, pickedSide = -1;
            var key = (-1, -1);
            bool sawUnsupported = false, sawFaceWithoutEdge = false;
            int testedFace = -1;
            foreach (int t in hits)
            {
                if (t < 0 || t >= _triangleLoopFace.Length) continue;
                int face = _triangleLoopFace[t];
                if (face < 0) { sawUnsupported = true; continue; }
                if (face == testedFace || face == pickedFace) continue;
                testedFace = face;
                int side = FindLoopSide(_loopFaces[face], project, pointer);
                if (side < 0)
                {
                    sawFaceWithoutEdge = true;
                    continue;
                }
                var seed = LoopEdgeKey(_loopFaces[face].Vertex(side), _loopFaces[face].Vertex(side + 1));
                if (pickedFace >= 0 && seed != key) return FaceLoopStatus.Overlap;
                key = seed;
                pickedFace = face;
                pickedSide = side;
            }
            if (pickedFace < 0) return sawUnsupported ? FaceLoopStatus.NoPair : FaceLoopStatus.NeedEdge;
            if (sawUnsupported || sawFaceWithoutEdge) return FaceLoopStatus.Overlap;
            if (_lastLoopTriangles != null && key == _lastLoopEdge)
            {
                selected = _lastLoopTriangles;
                return _lastLoopStatus;
            }
            _lastLoopEdge = key;
            _lastLoopStatus = FollowFaceLoop(pickedFace, pickedSide, out _lastLoopTriangles);
            selected = _lastLoopTriangles;
            return _lastLoopStatus;
        }

        static int FindLoopSide(LoopFace face, Func<int, Vector2> project, Vector2 pointer)
        {
            const float nearEdge = 10f, nearCorner = 4f, axisSeparation = 1.25f;
            float best = float.PositiveInfinity, even = best, odd = best;
            int side = -1, triangleAxis = 0;
            var from = project(face.Vertex(0));
            for (int e = 0; e < face.Sides; e++)
            {
                var to = project(face.Vertex(e + 1));
                var edge = to - from;
                float length = edge.sqrMagnitude;
                int axis = face.Sides == 4 ? e & 1 : face.CanCross(e) ? triangleAxis++ : -1;
                if (axis >= 0 && float.IsFinite(length) && length > 1e-6f)
                {
                    float along = Mathf.Clamp01(Vector2.Dot(pointer - from, edge) / length);
                    float distance = (pointer - (from + along * edge)).magnitude;
                    if (distance < best) { best = distance; side = e; }
                    if (axis == 0) even = Mathf.Min(even, distance);
                    else odd = Mathf.Min(odd, distance);
                }
                from = to;
            }
            if (best > nearEdge || (even <= nearCorner && odd <= nearCorner) || Mathf.Abs(even - odd) < axisSeparation)
                return -1;
            return side;
        }

        void BuildFaceLoopTopology()
        {
            _loopEdges = new Dictionary<(int, int), LoopEdge>();
            var positions = new Vector3[uv.Length];
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                int end = i + 1 < parts.Count ? parts[i + 1].vertexOffset : positions.Length;
                if (part.mesh == null || part.mesh.vertexCount != end - part.vertexOffset)
                {
                    Array.Fill(positions, new Vector3(float.NaN, float.NaN, float.NaN), part.vertexOffset, end - part.vertexOffset);
                    continue;
                }
                var vertices = part.mesh.vertices;
                Array.Copy(vertices, 0, positions, part.vertexOffset, vertices.Length);
            }
            for (int t = 0; t < TriangleCount; t++)
                for (int e = 0; e < 3; e++)
                {
                    var key = LoopEdgeKey(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3]);
                    _loopEdges.TryGetValue(key, out var edge);
                    if (edge.count == 0) edge.first = t;
                    if (edge.count == 1) edge.second = t;
                    edge.count++;
                    _loopEdges[key] = edge;
                }

            var candidates = new List<LoopFace>();
            var counts = new int[TriangleCount];
            foreach (var entry in _loopEdges)
            {
                var edge = entry.Value;
                if (edge.count != 2 || edge.first == edge.second ||
                    !TryLoopQuad(entry.Key, edge.first, edge.second, positions, out var quad)) continue;
                counts[quad.first]++;
                counts[quad.second]++;
                candidates.Add(quad);
            }

            _triangleLoopFace = new int[TriangleCount];
            Array.Fill(_triangleLoopFace, -1);
            var accepted = new List<LoopFace>();
            foreach (var quad in candidates)
            {
                if (counts[quad.first] != 1 || counts[quad.second] != 1) continue;
                _triangleLoopFace[quad.first] = _triangleLoopFace[quad.second] = accepted.Count;
                accepted.Add(quad);
            }
            for (int t = 0; t < TriangleCount; t++)
            {
                if (counts[t] != 0 || !TryLoopTriangle(t, positions, out var triangle)) continue;
                _triangleLoopFace[t] = accepted.Count;
                accepted.Add(triangle);
            }
            _loopFaces = accepted.ToArray();
        }

        bool TryLoopTriangle(int triangle, Vector3[] positions, out LoopFace face)
        {
            face = default;
            int start = triangle * 3;
            int a = tris[start], b = tris[start + 1], c = tris[start + 2];
            var ab = positions[b] - positions[a];
            var ac = positions[c] - positions[a];
            float scale = Mathf.Max(ab.sqrMagnitude, ac.sqrMagnitude);
            float area = Vector3.Cross(ab, ac).sqrMagnitude;
            if (!float.IsFinite(area) || !float.IsFinite(scale) || area <= scale * scale * 1e-8f) return false;
            int boundary = -1;
            for (int side = 0; side < 3; side++)
            {
                int from = tris[start + side], to = tris[start + (side + 1) % 3];
                var edge = _loopEdges[LoopEdgeKey(from, to)];
                if (edge.count == 1)
                {
                    if (boundary >= 0) return false;
                    boundary = side;
                }
                else if (edge.count != 2 || DirectedLoopSide(edge.first == triangle ? edge.second : edge.first, to, from) < 0)
                    return false;
            }
            if (boundary < 0) return false;
            face = new LoopFace(triangle, -1, a, b, c, -1, boundary);
            return true;
        }

        bool TryLoopQuad((int, int) edge, int first, int second, Vector3[] positions, out LoopFace quad)
        {
            quad = default;
            int a = edge.Item1, c = edge.Item2;
            int diag = DirectedLoopSide(first, a, c);
            if (diag < 0) { (a, c) = (c, a); diag = DirectedLoopSide(first, a, c); }
            int reverse = DirectedLoopSide(second, c, a);
            if (diag < 0 || reverse < 0) return false;
            int d = tris[first * 3 + (diag + 2) % 3], b = tris[second * 3 + (reverse + 2) % 3];
            if (a == c || b == d || b == a || b == c || d == a || d == c) return false;
            var pa = positions[a]; var pb = positions[b]; var pc = positions[c]; var pd = positions[d];
            float diagonal = (pc - pa).sqrMagnitude;
            float longest = Mathf.Max(Mathf.Max((pb - pa).sqrMagnitude, (pc - pb).sqrMagnitude),
                Mathf.Max((pd - pc).sqrMagnitude, (pa - pd).sqrMagnitude));
            if (!float.IsFinite(diagonal) || !float.IsFinite(longest) || diagonal <= 1.08f * longest) return false;
            var n0 = Vector3.Cross(pc - pa, pd - pa);
            var n1 = Vector3.Cross(pa - pc, pb - pc);
            float crossFloor = diagonal * diagonal * 1e-8f;
            if (n0.sqrMagnitude <= crossFloor || n1.sqrMagnitude <= crossFloor || Vector3.Dot(n0.normalized, n1.normalized) < .985f) return false;
            var normal = (n0.normalized + n1.normalized).normalized;
            float convexFloor = diagonal * 1e-4f;
            bool Convex(Vector3 x, Vector3 y, Vector3 z) => Vector3.Dot(Vector3.Cross(y - x, z - y), normal) > convexFloor;
            if (!Convex(pa, pb, pc) || !Convex(pb, pc, pd) || !Convex(pc, pd, pa) || !Convex(pd, pa, pb)) return false;
            quad = new LoopFace(first, second, a, b, c, d);
            return true;
        }

        int DirectedLoopSide(int triangle, int a, int b)
        {
            for (int i = 0; i < 3; i++)
                if (tris[triangle * 3 + i] == a && tris[triangle * 3 + (i + 1) % 3] == b) return i;
            return -1;
        }

        FaceLoopStatus FollowFaceLoop(int seedFace, int seedSide, out IReadOnlyList<int> selected)
        {
            var visited = new HashSet<int> { seedFace };
            bool stopped = false, tooLarge = false;
            void Walk(int current, int side)
            {
                while (true)
                {
                    var face = _loopFaces[current];
                    int a = face.Vertex(side), b = face.Vertex(side + 1);
                    var edge = _loopEdges[LoopEdgeKey(a, b)];
                    if (edge.count == 1) return;
                    if (edge.count != 2) { stopped = true; return; }
                    int next = _triangleLoopFace[edge.first] == current ? _triangleLoopFace[edge.second] : _triangleLoopFace[edge.first];
                    if (next < 0) { stopped = true; return; }
                    var candidate = _loopFaces[next];
                    int enter = 0;
                    while (enter < candidate.Sides && (candidate.Vertex(enter) != b || candidate.Vertex(enter + 1) != a)) enter++;
                    if (enter == candidate.Sides || !candidate.CanCross(enter)) { stopped = true; return; }
                    if (!visited.Add(next)) return;
                    if (visited.Count > FaceLoopMaxLength) { tooLarge = true; return; }
                    current = next;
                    side = candidate.Exit(enter);
                }
            }
            Walk(seedFace, seedSide);
            if (!tooLarge) Walk(seedFace, _loopFaces[seedFace].Exit(seedSide));
            if (tooLarge) { selected = Array.Empty<int>(); return FaceLoopStatus.TooLarge; }

            var faces = new List<int>(visited.Count * 2);
            foreach (int face in visited)
            {
                var step = _loopFaces[face];
                faces.Add(step.first);
                if (step.second >= 0) faces.Add(step.second);
            }
            faces.Sort();
            selected = faces;
            return stopped ? FaceLoopStatus.Stopped : FaceLoopStatus.Found;
        }
    }
}
