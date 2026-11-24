using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        internal enum BoundaryLoopStatus { Found, Missing, Ambiguous }

        Dictionary<int, List<(int corner, int[] faces)>> _boundaryLoops;

        internal BoundaryLoopStatus FindBoundaryLoop(IReadOnlyList<int> hits, Func<int, Vector2> project,
            Vector2 pointer, out IReadOnlyList<int> selected)
        {
            selected = Array.Empty<int>();
            if (_boundaryLoops == null) BuildBoundaryLoops();
            int[] loop = null;
            foreach (int triangle in hits)
            {
                if (!_boundaryLoops.TryGetValue(triangle, out var edges)) continue;
                int[] nearest = null;
                float best = float.PositiveInfinity, other = float.PositiveInfinity;
                foreach (var edge in edges)
                {
                    float distance = EdgeDistanceSquared(project(tris[edge.corner]) - pointer,
                        project(tris[NextBoundaryCorner(edge.corner)]) - pointer);
                    if (distance < best)
                    {
                        if (edge.faces != nearest) other = best;
                        best = distance;
                        nearest = edge.faces;
                    }
                    else if (edge.faces != nearest) other = Mathf.Min(other, distance);
                }
                if (nearest == null) continue;
                if (Mathf.Sqrt(other) - Mathf.Sqrt(best) <= AmbiguousPickDistance || (loop != null && loop != nearest))
                    return BoundaryLoopStatus.Ambiguous;
                loop = nearest;
            }
            if (loop == null) return BoundaryLoopStatus.Missing;

            foreach (int triangle in hits)
            {
                if (_boundaryLoops.ContainsKey(triangle)) continue;
                bool sharesEdge = false;
                foreach (int boundary in hits)
                    if (_boundaryLoops.ContainsKey(boundary) && ShareBoundaryEdge(triangle, boundary)) { sharesEdge = true; break; }
                if (!sharesEdge) return BoundaryLoopStatus.Ambiguous;
            }
            selected = loop;
            return BoundaryLoopStatus.Found;
        }

        int WeldedCorner(int corner) => _weldedUvVertices[tris[corner]];
        static int NextBoundaryCorner(int corner) => corner % 3 == 2 ? corner - 2 : corner + 1;

        bool ShareBoundaryEdge(int first, int second)
        {
            for (int i = first * 3; i < first * 3 + 3; i++)
                for (int j = second * 3; j < second * 3 + 3; j++)
                    if (WeldedCorner(i) == WeldedCorner(NextBoundaryCorner(j)) &&
                        WeldedCorner(NextBoundaryCorner(i)) == WeldedCorner(j)) return true;
            return false;
        }

        void BuildBoundaryLoops()
        {
            _boundaryLoops = new Dictionary<int, List<(int, int[])>>();
            var uses = new Dictionary<(int, int), (int first, int second, int count)>();
            for (int corner = 0; corner < tris.Length; corner++)
            {
                int a = WeldedCorner(corner), b = WeldedCorner(NextBoundaryCorner(corner));
                if (a == b) continue;
                var key = LoopEdgeKey(a, b);
                uses.TryGetValue(key, out var use);
                if (use.count == 0) use.first = corner;
                else if (use.count == 1) use.second = corner;
                use.count++;
                uses[key] = use;
            }

            var parents = new int[tris.Length];
            for (int i = 0; i < parents.Length; i++) parents[i] = i;
            int Find(int corner)
            {
                while (parents[corner] != corner) { parents[corner] = parents[parents[corner]]; corner = parents[corner]; }
                return corner;
            }
            foreach (var use in uses.Values)
            {
                if (use.count != 2 || use.first / 3 == use.second / 3) continue;
                int nextFirst = NextBoundaryCorner(use.first), nextSecond = NextBoundaryCorner(use.second);
                if (WeldedCorner(use.first) != WeldedCorner(nextSecond) || WeldedCorner(use.second) != WeldedCorner(nextFirst)) continue;
                parents[Find(use.first)] = Find(nextSecond);
                parents[Find(use.second)] = Find(nextFirst);
            }

            var edges = new List<(int corner, int a, int b)>();
            var fans = new Dictionary<int, List<int>>();
            void Link(int fan)
            {
                if (!fans.TryGetValue(fan, out var list)) fans[fan] = list = new List<int>();
                list.Add(edges.Count);
            }
            foreach (var use in uses.Values)
            {
                if (use.count != 1) continue;
                int a = Find(use.first), b = Find(NextBoundaryCorner(use.first));
                Link(a);
                Link(b);
                edges.Add((use.first, a, b));
            }

            var seen = new bool[edges.Count];
            var component = new List<int>();
            for (int start = 0; start < edges.Count; start++)
            {
                if (seen[start]) continue;
                component.Clear();
                component.Add(start);
                seen[start] = true;
                bool closed = true;
                void Follow(int fan)
                {
                    var next = fans[fan];
                    if (next.Count != 2) closed = false;
                    foreach (int i in next)
                        if (!seen[i]) { seen[i] = true; component.Add(i); }
                }
                for (int i = 0; i < component.Count; i++)
                {
                    var edge = edges[component[i]];
                    Follow(edge.a);
                    Follow(edge.b);
                }
                if (!closed || component.Count < 3) continue;

                var faces = new HashSet<int>();
                foreach (int i in component) faces.Add(edges[i].corner / 3);
                var picked = new int[faces.Count];
                faces.CopyTo(picked);
                Array.Sort(picked);
                foreach (int i in component)
                {
                    int corner = edges[i].corner, face = corner / 3;
                    if (!_boundaryLoops.TryGetValue(face, out var list)) _boundaryLoops[face] = list = new List<(int, int[])>();
                    list.Add((corner, picked));
                }
            }
        }
    }
}
