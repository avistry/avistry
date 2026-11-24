using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class BrushDrips
    {
        // Adapted from TextureCraft's particle brush under MIT; see Third Party Notices.md.
        internal const int Particles = 4, Lifetime = 24;
        const int MaxCachedSites = 512;
        const long MaximumCachedBytes = 8L * 1024 * 1024;
        const float ParticleSize = .35f;
        static readonly ConditionalWeakTable<PaintStroke, Cache> Strokes = new ConditionalWeakTable<PaintStroke, Cache>();
        static readonly LinkedList<Bank> Banks = new LinkedList<Bank>();
        static readonly List<Dab> Scratch = new List<Dab>(Particles * Lifetime);
        static long s_cachedBytes;

        internal readonly struct Dab
        {
            internal readonly Vector4 Position, Normal;
            internal Dab(Vector3 position, Vector3 normal, float radius, float opacity)
            {
                Position = new Vector4(position.x, position.y, position.z, radius);
                Normal = new Vector4(normal.x, normal.y, normal.z, opacity);
            }
        }

        sealed class Cache
        {
            internal WeakReference<MeshUV> Mesh;
            internal int Pose = -1;
            internal Dictionary<Site, Dab[]> Sites;
            internal Bank Bank;
        }

        sealed class Bank
        {
            internal readonly WeakReference<Cache> Cache;
            internal readonly LinkedListNode<Bank> Node;
            internal long Bytes;
            internal Bank(Cache cache)
            {
                Cache = new WeakReference<Cache>(cache);
                Node = new LinkedListNode<Bank>(this);
            }
        }

        readonly struct Site : IEquatable<Site>
        {
            readonly int _seed, _site;
            readonly Vector4 _position, _normal;
            internal Site(int seed, int site, Vector3 position, Vector3 normal, float radius, float opacity)
            {
                _seed = seed; _site = site;
                _position = new Vector4(position.x, position.y, position.z, radius);
                _normal = new Vector4(normal.x, normal.y, normal.z, opacity);
            }
            public bool Equals(Site other) => _seed == other._seed && _site == other._site &&
                _position.Equals(other._position) && _normal.Equals(other._normal);
            public override bool Equals(object other) => other is Site site && Equals(site);
            public override int GetHashCode() => ((_seed * 397 ^ _site) * 397 ^ _position.GetHashCode()) * 397 ^ _normal.GetHashCode();
        }

        internal static void Emit<T>(PaintStroke stroke, MeshUV mesh, int seed, int site, Vector3 position, Vector3 normal,
            float radius, float opacity, ref T sink) where T : struct, BrushRenderer.ISurfaceDabSink
        {
            var dabs = Prepare(stroke, mesh, seed, site, position, normal, radius, opacity);
            for (int i = 0; i < dabs.Count; i++) sink.Emit(dabs[i].Position, dabs[i].Normal, default, default);
        }

        internal static IReadOnlyList<Dab> Prepare(PaintStroke stroke, MeshUV mesh, int seed, int site, Vector3 position,
                                                  Vector3 normal, float radius, float opacity)
        {
            if (stroke == null || mesh == null || radius <= 0 || !float.IsFinite(radius + opacity + position.x + position.y + position.z +
                normal.x + normal.y + normal.z) || normal.sqrMagnitude < .0001f) return Array.Empty<Dab>();
            int pose = mesh.PoseVersion;
            var prepared = Strokes.GetValue(stroke, _ => new Cache());
            if (prepared.Pose != pose || prepared.Mesh == null || !prepared.Mesh.TryGetTarget(out var previous) ||
                !ReferenceEquals(previous, mesh))
            {
                ReleaseBank(prepared);
                prepared.Mesh = new WeakReference<MeshUV>(mesh);
                prepared.Pose = pose;
            }
            prepared.Bank ??= new Bank(prepared);
            if (prepared.Bank.Node.List != null) Banks.Remove(prepared.Bank.Node);
            Banks.AddLast(prepared.Bank.Node);
            var key = new Site(seed, site, position, normal, radius, opacity);
            if (prepared.Sites != null && prepared.Sites.TryGetValue(key, out var cached)) return cached;
            var tracks = Scratch;
            tracks.Clear();
            normal.Normalize();
            var tangent = Vector3.Cross(Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right, normal).normalized;
            var bitangent = Vector3.Cross(normal, tangent);
            float size = radius * ParticleSize, step = size, fall = radius * 20f;
            for (int particle = 0; particle < Particles; particle++)
            {
                Start(seed, site, particle, out float angle, out float distance);
                var start = position + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * (distance * radius);
                if (!Snap(mesh, start, normal, radius, out var at)) continue;
                for (int life = 0; life < Lifetime; life++)
                {
                    tracks.Add(new Dab(at.point, at.normal, size, opacity * (1f - life / (float)Lifetime)));
                    if (life + 1 == Lifetime) break;
                    var slide = Vector3.down - at.normal * Vector3.Dot(at.normal, Vector3.down);
                    MeshUV.SurfaceHit next;
                    bool found;
                    if (slide.sqrMagnitude > .0025f)
                    {
                        var to = at.point + slide.normalized * step;
                        found = Snap(mesh, to, at.normal, step, out next) ||
                            Cast(mesh, to + at.normal * step, Vector3.down, fall, out next);
                    }
                    else if (at.normal.y < 0)
                        found = Cast(mesh, at.point + at.normal * step, Vector3.down, fall, out next);
                    else break;
                    if (!found || (next.point - at.point).sqrMagnitude < step * step * .0001f) break;
                    at = next;
                }
            }
            if ((prepared.Sites?.Count ?? 0) >= MaxCachedSites) return tracks;
            long bytes = 64L + tracks.Count * 32L;
            MakeRoom(prepared, bytes);
            var result = tracks.ToArray();
            (prepared.Sites ??= new Dictionary<Site, Dab[]>()).Add(key, result);
            prepared.Bank.Bytes += bytes;
            s_cachedBytes += bytes;
            return result;
        }

        static void ReleaseBank(Cache cache)
        {
            cache.Sites = null;
            if (cache.Bank == null) return;
            s_cachedBytes -= cache.Bank.Bytes;
            cache.Bank.Bytes = 0;
        }

        static void MakeRoom(Cache current, long bytes)
        {
            while (s_cachedBytes + bytes > MaximumCachedBytes && Banks.First != null)
            {
                var bank = Banks.First.Value;
                Banks.RemoveFirst();
                if (bank.Cache.TryGetTarget(out var cache)) ReleaseBank(cache);
                else { s_cachedBytes -= bank.Bytes; bank.Bytes = 0; }
            }
        }
        internal static void Start(int seed, int site, int particle, out float angle, out float distance)
        {
            unchecked
            {
                uint x = (uint)seed ^ (uint)site * 0x9E3779B9u, y = (uint)particle, z = 0xD057;
                x = x * 1664525u + 1013904223u; y = y * 1664525u + 1013904223u; z = z * 1664525u + 1013904223u;
                x += y * z; y += z * x; z += x * y;
                x ^= x >> 16; y ^= y >> 16; z ^= z >> 16;
                x += y * z; y += z * x;
                angle = x / (float)uint.MaxValue * (Mathf.PI * 2f);
                distance = Mathf.Sqrt(y / (float)uint.MaxValue);
            }
        }

        static bool Snap(MeshUV mesh, Vector3 point, Vector3 normal, float reach, out MeshUV.SurfaceHit hit) =>
            Cast(mesh, point + normal * reach, -normal, reach * 2f, out hit);

        static bool Cast(MeshUV mesh, Vector3 origin, Vector3 direction, float reach, out MeshUV.SurfaceHit hit) =>
            mesh.RaycastPaintGeometry(new Ray(origin, direction), out hit) && (hit.point - origin).sqrMagnitude <= reach * reach;
    }
}
