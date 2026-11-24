using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        string _combinedLayout;

        internal string CombinedLayout
        {
            get
            {
                if (_combinedLayout != null) return _combinedLayout;
                var hash = new LayoutHash(0);
                hash.Add(uvChannel);
                hash.Add(parts.Count);
                foreach (var part in parts)
                {
                    hash.Add(part.materialSlot);
                    hash.Add(part.submesh);
                    hash.Add(part.uvChannel);
                    hash.Add(part.vertexOffset);
                    hash.Add(part.vertexCount);
                    hash.Add(part.triStart);
                    hash.Add(part.triCount);
                    hash.Add(unchecked((int)part.bindPositionsHash));
                    hash.Add(unchecked((int)(part.bindPositionsHash >> 32)));
                }
                hash.Add(uv.Length);
                foreach (var point in uv)
                {
                    hash.Add(point.x);
                    hash.Add(point.y);
                }
                hash.Add(tris.Length);
                foreach (int index in tris) hash.Add(index);
                hash.Add(islandCount);
                foreach (int island in triIsland) hash.Add(island);
                return _combinedLayout = $"1-{hash.Value:X16}";
            }
        }

        internal (int[] triangles, int[] islands) RemapTo(MeshUV next)
        {
            var triangles = new int[TriangleCount];
            var islands = new int[islandCount];
            Array.Fill(triangles, -1);
            Array.Fill(islands, -1);
            if (next == null) return (triangles, islands);

            var bySlot = new Dictionary<(Renderer, int), Part>();
            foreach (var part in next.parts)
            {
                var key = (part.renderer, part.materialSlot);
                if (!bySlot.TryAdd(key, part)) bySlot[key] = null;
            }
            foreach (var part in parts)
            {
                if (!bySlot.TryGetValue((part.renderer, part.materialSlot), out var candidate) ||
                    !PartGeometryMatches(part, next, candidate)) continue;
                for (int local = 0; local < part.triCount; local++)
                {
                    int oldTriangle = part.triStart + local;
                    int newTriangle = candidate.triStart + local;
                    triangles[oldTriangle] = newTriangle;
                    islands[triIsland[oldTriangle]] = next.triIsland[newTriangle];
                }
            }
            return (triangles, islands);
        }

        bool PartGeometryMatches(Part part, MeshUV next, Part candidate)
        {
            if (candidate == null || part.mesh != candidate.mesh || part.submesh != candidate.submesh ||
                part.uvChannel != candidate.uvChannel || part.bindPositionsHash != candidate.bindPositionsHash ||
                part.vertexCount != candidate.vertexCount || part.triCount != candidate.triCount) return false;
            for (int i = 0; i < part.vertexCount; i++)
            {
                var a = uv[part.vertexOffset + i];
                var b = next.uv[candidate.vertexOffset + i];
                if (BitConverter.SingleToInt32Bits(a.x) != BitConverter.SingleToInt32Bits(b.x) ||
                    BitConverter.SingleToInt32Bits(a.y) != BitConverter.SingleToInt32Bits(b.y)) return false;
            }
            for (int i = 0; i < part.triCount * 3; i++)
                if (tris[part.triStart * 3 + i] - part.vertexOffset != next.tris[candidate.triStart * 3 + i] - candidate.vertexOffset)
                    return false;
            return true;
        }

        static ulong BindPositionsHash(Vector3[] positions)
        {
            var hash = new LayoutHash(positions.Length);
            foreach (var point in positions)
            {
                hash.Add(point.x);
                hash.Add(point.y);
                hash.Add(point.z);
            }
            return hash.Value;
        }

        struct LayoutHash
        {
            internal ulong Value { get; private set; }

            internal LayoutHash(int first)
            {
                Value = 14695981039346656037UL;
                Add(first);
            }

            internal void Add(int value) => Value = unchecked((Value ^ (uint)value) * 1099511628211UL);
            internal void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));
        }
    }
}
