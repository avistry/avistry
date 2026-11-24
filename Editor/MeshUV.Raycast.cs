using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        const int RaycastTreeThreshold = 512;
        RaycastTree[] _raycastTrees;

        internal sealed class TileMembership
        {
            internal ushort[] triangles;
            internal int lastDiscarded;
            internal bool allDiscarded;
        }

        internal PoiyomiTileDiscard.TriangleFilter TileDiscardFilter(Part part, int channel, int discarded)
        {
            part.tileMembership ??= new TileMembership[4];
            var membership = part.tileMembership[channel] ??= new TileMembership { triangles = BuildTileMembership(part, channel) };
            if (membership.triangles == null) return default;
            if (membership.lastDiscarded != discarded)
            {
                membership.lastDiscarded = discarded;
                membership.allDiscarded = Array.TrueForAll(membership.triangles, tiles => (tiles & discarded) != 0);
            }
            return new PoiyomiTileDiscard.TriangleFilter(membership.triangles, discarded, part.triStart, membership.allDiscarded);
        }

        ushort[] BuildTileMembership(Part part, int channel)
        {
            if (!part.mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel)) return null;
            var coords = new List<Vector2>(part.mesh.vertexCount);
            part.mesh.GetUVs(channel, coords);
            if (coords.Count != part.mesh.vertexCount) return null;
            var result = new ushort[part.triCount];
            int offset = part.vertexOffset;
            for (int t = 0; t < result.Length; t++)
            {
                int at = (part.triStart + t) * 3;
                result[t] = (ushort)(PoiyomiTileDiscard.Tile(coords[tris[at] - offset]) |
                    PoiyomiTileDiscard.Tile(coords[tris[at + 1] - offset]) |
                    PoiyomiTileDiscard.Tile(coords[tris[at + 2] - offset]));
            }
            return result;
        }

        struct RaycastCandidate
        {
            public int triangle;
            public float distance, u, v;
        }

        void RaycastPart(int partIndex, Vector3[] world, Ray ray, PoiyomiTileDiscard.TriangleFilter tileFilter,
                         CullMode cull, in SceneCutout cutout, ref RaycastCandidate closest)
        {
            var part = parts[partIndex];
            if (part.triCount >= RaycastTreeThreshold && _posedBounds[partIndex] is Bounds bounds)
            {
                _raycastTrees ??= new RaycastTree[parts.Count];
                var tree = _raycastTrees[partIndex] ??= new RaycastTree(this, part);
                if (tree.TryRaycast(world, bounds, ray, tileFilter, cull, cutout, ref closest)) return;
            }
            for (int t = part.triStart; t < part.triStart + part.triCount; t++)
                TestRayTriangle(t, part.vertexOffset, world, ray, tileFilter, cull, cutout, ref closest);
        }

        void TestRayTriangle(int triangle, int offset, Vector3[] world, Ray ray, PoiyomiTileDiscard.TriangleFilter tileFilter,
                             CullMode cull, in SceneCutout cutout, ref RaycastCandidate closest)
        {
            if (tileFilter.Discards(triangle)) return;
            int at = triangle * 3;
            if (!RayTriangle(ray, world[tris[at] - offset], world[tris[at + 1] - offset], world[tris[at + 2] - offset], cull,
                out float distance, out float u, out float v)) return;

            if (!(distance < closest.distance || distance == closest.distance && closest.triangle >= 0 && triangle < closest.triangle)) return;
            if (cutout.Active && cutout.Discards(uv[tris[at]] * (1f - u - v) + uv[tris[at + 1]] * u + uv[tris[at + 2]] * v)) return;
            closest = new RaycastCandidate { triangle = triangle, distance = distance, u = u, v = v };
        }

        sealed class RaycastTree
        {
            const int LeafSize = 16;
            readonly MeshUV _mesh;
            readonly Part _part;
            int[] _orderedTriangles;
            Box[] _nodes;
            int _lastQueryPose = -1;
            int _boundsPose = -1;
            float _padding;

            struct Box
            {
                public Vector3 min, max;

                public void Include(Vector3 point)
                {
                    if (point.x < min.x) min.x = point.x;
                    if (point.y < min.y) min.y = point.y;
                    if (point.z < min.z) min.z = point.z;
                    if (point.x > max.x) max.x = point.x;
                    if (point.y > max.y) max.y = point.y;
                    if (point.z > max.z) max.z = point.z;
                }
            }

            public RaycastTree(MeshUV mesh, Part part) { _mesh = mesh; _part = part; }

            void Build(Vector3[] world, Bounds bounds)
            {
                _orderedTriangles = new int[_part.triCount];
                int leaves = Mathf.NextPowerOfTwo((_part.triCount + LeafSize - 1) / LeafSize);
                _nodes = new Box[leaves * 2 - 1];
                var keys = ArrayPool<uint>.Shared.Rent(_part.triCount);
                try
                {
                    var min = bounds.min;
                    var size = bounds.size;
                    var scale = new Vector3(1023f / size.x, 1023f / size.y, 1023f / size.z);
                    for (int i = 0; i < _orderedTriangles.Length; i++)
                    {
                        int triangle = _orderedTriangles[i] = _part.triStart + i;
                        int at = triangle * 3, offset = _part.vertexOffset;
                        var centre = (world[_mesh.tris[at] - offset] + world[_mesh.tris[at + 1] - offset] +
                            world[_mesh.tris[at + 2] - offset]) / 3f;
                        var cell = Vector3.Scale(centre - min, scale);
                        keys[i] = Spread(cell.x) | Spread(cell.y) << 1 | Spread(cell.z) << 2;
                    }
                    Array.Sort(keys, _orderedTriangles, 0, _part.triCount);
                }
                finally { ArrayPool<uint>.Shared.Return(keys); }
            }

            static uint Spread(float coordinate)
            {
                uint value = float.IsFinite(coordinate) ? (uint)Mathf.Clamp(coordinate, 0f, 1023f) : 0;
                value = (value | value << 16) & 0x030000ff;
                value = (value | value << 8) & 0x0300f00f;
                value = (value | value << 4) & 0x030c30c3;
                return (value | value << 2) & 0x09249249;
            }

            public bool TryRaycast(Vector3[] world, Bounds partBounds, Ray ray, PoiyomiTileDiscard.TriangleFilter tileFilter,
                                   CullMode cull, in SceneCutout cutout, ref RaycastCandidate closest)
            {
                int pose = _mesh.PoseVersion;
                if (_boundsPose != pose)
                {
                    if (_lastQueryPose != pose) { _lastQueryPose = pose; return false; }
                    float magnitude = Mathf.Max(partBounds.min.sqrMagnitude, partBounds.max.sqrMagnitude);
                    if (!float.IsFinite(magnitude)) return false;
                    _padding = Mathf.Max(0.002f, Mathf.Sqrt(magnitude) * 0.000002f);
                    if (_orderedTriangles == null) Build(world, partBounds);
                    Refit(0, 0, _orderedTriangles.Length, world);
                    _boundsPose = pose;
                }

                float padding = Mathf.Max(_padding, (ray.origin - partBounds.center).magnitude * 0.000004f);
                if (!float.IsFinite(padding)) return false;
                Visit(0, 0, _orderedTriangles.Length, world, ray, padding, tileFilter, cull, cutout, ref closest);
                return true;
            }

            Box Refit(int node, int start, int count, Vector3[] world)
            {
                if (count > LeafSize)
                {
                    int half = count / 2;
                    var left = Refit(node * 2 + 1, start, half, world);
                    var right = Refit(node * 2 + 2, start + half, count - half, world);
                    return _nodes[node] = new Box { min = Vector3.Min(left.min, right.min), max = Vector3.Max(left.max, right.max) };
                }

                var box = new Box { min = Vector3.positiveInfinity, max = Vector3.negativeInfinity };
                int offset = _part.vertexOffset;
                var tris = _mesh.tris;
                for (int i = start; i < start + count; i++)
                {
                    int at = _orderedTriangles[i] * 3;
                    box.Include(world[tris[at] - offset]);
                    box.Include(world[tris[at + 1] - offset]);
                    box.Include(world[tris[at + 2] - offset]);
                }
                return _nodes[node] = box;
            }

            void Visit(int node, int start, int count, Vector3[] world, Ray ray, float padding,
                       PoiyomiTileDiscard.TriangleFilter tileFilter, CullMode cull, in SceneCutout cutout, ref RaycastCandidate closest)
            {
                var box = _nodes[node];
                var size = box.max - box.min;
                if (size.x >= 0 && size.y >= 0 && size.z >= 0 && float.IsFinite(size.sqrMagnitude))
                {
                    var bounds = new Bounds(box.min + size * 0.5f, size);
                    bounds.Expand(padding);
                    if (!bounds.IntersectRay(ray)) return;
                }
                if (count <= LeafSize)
                {
                    int end = start + count;
                    for (int i = start; i < end; i++)
                        _mesh.TestRayTriangle(_orderedTriangles[i], _part.vertexOffset, world, ray, tileFilter, cull, cutout, ref closest);
                    return;
                }
                int half = count / 2;
                Visit(node * 2 + 1, start, half, world, ray, padding, tileFilter, cull, cutout, ref closest);
                Visit(node * 2 + 2, start + half, count - half, world, ray, padding, tileFilter, cull, cutout, ref closest);
            }
        }
    }
}
