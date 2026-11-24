using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        const float TopologyPickRadius = 10f;
        const float AmbiguousPickDistance = .5f;

        internal readonly struct TopologyHit
        {
            public readonly bool ambiguous;
            public readonly IReadOnlyList<int> faces;

            public TopologyHit(bool ambiguous, IReadOnlyList<int> faces)
            {
                this.ambiguous = ambiguous;
                this.faces = faces;
            }
        }

        int[] _incidentFaceStarts, _incidentFaces;

        internal TopologyHit PickTextureTopology(Vector2 point, Vector2 pixelsPerUv, bool vertex)
        {
            if (pixelsPerUv.x <= 0f || pixelsPerUv.y <= 0f) return default;
            var radius = new Vector2(TopologyPickRadius / pixelsPerUv.x, TopologyPickRadius / pixelsPerUv.y);
            int left = GridCell(point.x - radius.x), right = GridCell(point.x + radius.x);
            int bottom = GridCell(point.y - radius.y), top = GridCell(point.y + radius.y);
            var triangles = new HashSet<int>();
            var grid = _uvGrid;
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    var cell = grid[x + y * UvGridN];
                    if (cell != null) triangles.UnionWith(cell);
                }
            return PickTopology(triangles, i => Vector2.Scale(uv[i] - point, pixelsPerUv), vertex);
        }

        internal TopologyHit PickSceneTopology(int triangle, Func<int, Vector2> project, Vector2 pointer, bool vertex) =>
            PickTopology(new[] { triangle }, i => project(i) - pointer, vertex);

        TopologyHit PickTopology(IEnumerable<int> triangles, Func<int, Vector2> fromPointer, bool pickVertex)
        {
            int first = -1, second = -1;
            float closest = float.PositiveInfinity, runnerUp = float.PositiveInfinity;
            foreach (int face in triangles)
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[face * 3 + k];
                    int b = pickVertex ? -1 : tris[face * 3 + (k + 1) % 3];
                    if (!pickVertex && a > b) (a, b) = (b, a);
                    if (a == first && b == second) continue;
                    var from = fromPointer(a);
                    float distance = pickVertex ? from.sqrMagnitude : EdgeDistanceSquared(from, fromPointer(b));
                    if (!float.IsFinite(distance)) continue;
                    if (distance < closest)
                    {
                        runnerUp = closest;
                        closest = distance;
                        first = a;
                        second = b;
                    }
                    else runnerUp = Mathf.Min(runnerUp, distance);
                }

            if (closest > TopologyPickRadius * TopologyPickRadius) return default;
            if (Mathf.Sqrt(runnerUp) - Mathf.Sqrt(closest) <= AmbiguousPickDistance)
                return new TopologyHit(true, null);
            return new TopologyHit(false, IncidentFaces(first, second));
        }

        static float EdgeDistanceSquared(Vector2 a, Vector2 b)
        {
            var edge = b - a;
            float length = edge.sqrMagnitude;
            if (!float.IsFinite(length) || length <= 1e-6f) return float.PositiveInfinity;
            return (a + Mathf.Clamp01(-Vector2.Dot(a, edge) / length) * edge).sqrMagnitude;
        }

        IReadOnlyList<int> IncidentFaces(int vertex, int edgeEnd)
        {
            if (_incidentFaces == null) BuildIncidentFaceIndex();
            var result = new List<int>();
            int previous = -1;
            for (int i = _incidentFaceStarts[vertex]; i < _incidentFaceStarts[vertex + 1]; i++)
            {
                int face = _incidentFaces[i];
                if (face == previous) continue;
                previous = face;
                int t = face * 3;
                if (edgeEnd < 0 || tris[t] == edgeEnd || tris[t + 1] == edgeEnd || tris[t + 2] == edgeEnd)
                    result.Add(face);
            }
            return result;
        }

        void BuildIncidentFaceIndex()
        {
            _incidentFaceStarts = new int[uv.Length + 1];
            foreach (int vertex in tris) _incidentFaceStarts[vertex + 1]++;
            for (int i = 1; i < _incidentFaceStarts.Length; i++) _incidentFaceStarts[i] += _incidentFaceStarts[i - 1];
            var next = (int[])_incidentFaceStarts.Clone();
            _incidentFaces = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) _incidentFaces[next[tris[i]]++] = i / 3;
        }
    }
}
