using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer
    {
        internal struct SurfaceCursor
        {
            internal int Count, Site;
            internal float Carry, Pressure;
            internal bool HavePrevious;
            internal Vector3 Position, Normal;
        }

        internal interface ISurfaceDabSink
        {
            void Emit(Vector4 position, Vector4 normal, Vector4 tangent, Vector4 stamp);
        }

        struct SurfaceBatch : ISurfaceDabSink
        {
            readonly BrushRenderer _owner;
            RenderTexture _prefix;
            readonly Func<RenderTexture> _createPrefix;
            readonly Mesh _mesh;
            readonly int _pass;
            readonly Vector4[] _positions, _normals, _tangents, _stamps;
            int _count;
            Vector3 _min, _max;
            internal RenderTexture Prefix => _prefix;
            internal bool Failed { get; private set; }

            internal SurfaceBatch(BrushRenderer owner, RenderTexture prefix, Mesh mesh, int pass, Vector4[] positions, Vector4[] normals,
                                  Func<RenderTexture> createPrefix = null)
            {
                _owner = owner; _prefix = prefix; _mesh = mesh; _pass = pass;
                _positions = positions; _normals = normals;
                _tangents = ReferenceEquals(positions, owner._liveSurfacePositions) ? owner._liveSurfaceTangents : owner._surfaceTangents;
                _stamps = ReferenceEquals(positions, owner._liveSurfacePositions) ? owner._liveSurfaceStamps : owner._surfaceStamps;
                _count = 0; _min = _max = default;
                _createPrefix = createPrefix; Failed = false;
            }

            public void Emit(Vector4 position, Vector4 normal, Vector4 tangent, Vector4 stamp)
            {
                if (Failed) return;
                var point = new Vector3(position.x, position.y, position.z);
                var radius = Vector3.one * (position.w * (stamp.w > 0 ? 1.5f : 1f));
                var lo = point - radius;
                var hi = point + radius;
                if (_count == 0) { _min = lo; _max = hi; }
                else { _min = Vector3.Min(_min, lo); _max = Vector3.Max(_max, hi); }
                _positions[_count] = position;
                _normals[_count] = normal;
                _tangents[_count] = tangent;
                _stamps[_count] = stamp;
                if (++_count != DabsPerBatch) return;
                if (_prefix == null)
                {
                    _prefix = _createPrefix?.Invoke();
                    if (_prefix == null) { Failed = true; _count = 0; return; }
                    RenderTexture.active = _prefix;
                    GL.Clear(false, true, Color.clear);
                }
                Draw(_prefix);
                _count = 0;
            }

            void SetDabGroupBounds(MaterialPropertyBlock properties, RenderTexture target)
            {
                bool useGroups = _count > DabsPerGroup && _stamps[0].w == 0 &&
                    (long)target.width * target.height > 2048L * 2048L;
                properties.SetFloat("_GroupDabs", useGroups ? 1 : 0);
                if (!useGroups) return;

                for (int first = 0; first < _count; first += DabsPerGroup)
                {
                    Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                    int end = Mathf.Min(_count, first + DabsPerGroup);
                    for (int k = first; k < end; k++)
                    {
                        Vector3 point = _positions[k];
                        var margin = Vector3.one * (Mathf.Max(1e-6f, _positions[k].w) * 1.00001f + 1e-7f);
                        lo = Vector3.Min(lo, point - margin);
                        hi = Vector3.Max(hi, point + margin);
                    }
                    _owner._dabGroupMin[first / DabsPerGroup] = lo;
                    _owner._dabGroupMax[first / DabsPerGroup] = hi;
                }
                properties.SetVectorArray("_DabGroupMin", _owner._dabGroupMin);
                properties.SetVectorArray("_DabGroupMax", _owner._dabGroupMax);
            }

            internal void Draw(RenderTexture target, bool empty = false)
            {
                if (_count == 0 && !empty) return;
                var mpb = _owner._mpb;
                mpb.SetVectorArray("_DabPos", _positions);
                mpb.SetVectorArray("_DabNrm", _normals);
                mpb.SetVectorArray("_DabTan", _tangents);
                mpb.SetVectorArray("_DabStamp", _stamps);
                mpb.SetFloat("_DabCount", _count);
                SetDabGroupBounds(mpb, target);
                mpb.SetVector("_BatchMin", _min);
                mpb.SetVector("_BatchMax", _max);
                var cmd = _owner._cmd;
                cmd.Clear();
                cmd.SetRenderTarget(target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, target.width, target.height));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 100));
                cmd.DrawMesh(_mesh, Matrix4x4.identity, _owner.Material, 0, _pass, mpb);
                Graphics.ExecuteCommandBuffer(cmd);
            }
        }

        const int DabsPerGroup = 8;
        readonly Vector4[] _dabGroupMin = new Vector4[DabsPerBatch / DabsPerGroup], _dabGroupMax = new Vector4[DabsPerBatch / DabsPerGroup];
        readonly List<Vector4> _liveSurfacePoints = new List<Vector4>();
        readonly List<Vector4> _liveSurfacePath = new List<Vector4>();
        readonly List<Vector2> _liveSurfaceAngles = new List<Vector2>();
        readonly Vector4[] _liveSurfacePositions = new Vector4[DabsPerBatch];
        readonly Vector4[] _liveSurfaceNormals = new Vector4[DabsPerBatch];
        readonly Vector4[] _surfaceTangents = new Vector4[DabsPerBatch], _surfaceStamps = new Vector4[DabsPerBatch];
        readonly Vector4[] _liveSurfaceTangents = new Vector4[DabsPerBatch], _liveSurfaceStamps = new Vector4[DabsPerBatch];
        PaintStroke _liveSurfaceStroke;
        MeshUV _liveSurfaceMesh;
        RenderTexture _liveSurfacePrefix;
        int _liveSurfacePose;
        Vector2Int _liveSurfaceSize;
        SurfaceCursor _liveSurfaceCursor;
        SurfaceBatch _liveSurfaceBatch;
        SurfaceSettings _liveSurfaceSettings;
        internal bool HasLiveSurfaceCoverage => _liveSurfaceStroke != null;

        internal void InvalidateLiveSurfaceCoverage()
        {
            _liveSurfaceStroke = null;
            _liveSurfaceMesh = null;
            _liveSurfacePrefix = null;
            _liveSurfacePoints.Clear();
            _liveSurfacePath.Clear();
            _liveSurfaceAngles.Clear();
            _liveSurfaceCursor = default;
            _liveSurfaceBatch = default;
        }

        internal bool CanContinueLiveSurfaceCoverage(PaintStroke stroke, RenderTexture prefix, MeshUV mesh, float grainCellsPerUV)
        {
            int count = _liveSurfaceCursor.Count;
            if (!ReferenceEquals(stroke, _liveSurfaceStroke) || !ReferenceEquals(prefix, _liveSurfacePrefix) ||
                !ReferenceEquals(mesh, _liveSurfaceMesh) || mesh == null ||
                !ReferenceEquals(prefix, null) &&
                    (prefix == null || !prefix.IsCreated() || prefix.width != _liveSurfaceSize.x || prefix.height != _liveSurfaceSize.y) ||
                _liveSurfacePose != mesh.PoseVersion || !stroke.IsSurface || stroke.points.Count < count ||
                !_liveSurfaceSettings.Equals(new SurfaceSettings(stroke, grainCellsPerUV))) return false;
            for (int i = 0; i < count; i++)
                if (!_liveSurfacePoints[i].Equals(stroke.points[i]) || !_liveSurfacePath[i].Equals(stroke.surf[i]) ||
                    !_liveSurfaceAngles[i].Equals(TipSample(stroke, i))) return false;
            return true;
        }

        internal void RenderLiveSurfaceCoverage(PaintStroke stroke, RenderTexture target, RenderTexture prefix,
                                                MeshUV mesh, float grainCellsPerUV, Func<RenderTexture> createPrefix = null)
        {
            if (prefix == null && createPrefix != null) prefix = _liveSurfacePrefix;
            if (prefix != null && (prefix.width != target.width || prefix.height != target.height))
            {
                InvalidateLiveSurfaceCoverage();
                throw new ArgumentException("Surface coverage and its prefix must have matching dimensions.", nameof(prefix));
            }
            bool resume = CanContinueLiveSurfaceCoverage(stroke, prefix, mesh, grainCellsPerUV);
            if (!resume)
            {
                InvalidateLiveSurfaceCoverage();
                if (prefix != null)
                {
                    RenderTexture.active = prefix;
                    GL.Clear(false, true, Color.clear);
                }
                _liveSurfaceBatch = new SurfaceBatch(this, prefix, mesh.SurfaceMesh(), stroke.shape?.buildUp == true ? 7 : 6,
                    _liveSurfacePositions, _liveSurfaceNormals, createPrefix);
            }
            ConfigureSurface(stroke, target, grainCellsPerUV);
            int previous = _liveSurfaceCursor.Count;
            GL.PushMatrix();
            try
            {
                StreamSurfaceDabs(stroke, mesh, ref _liveSurfaceCursor, ref _liveSurfaceBatch);
                if (_liveSurfaceBatch.Failed)
                {
                    InvalidateLiveSurfaceCoverage();
                    RenderSurface(stroke, target, mesh, grainCellsPerUV);
                    return;
                }
                prefix = _liveSurfaceBatch.Prefix;
                if (prefix != null) Graphics.Blit(prefix, target, Material, 11);
                else
                {
                    RenderTexture.active = target;
                    GL.Clear(false, true, Color.clear);
                }
                _liveSurfaceBatch.Draw(target, empty: _liveSurfaceCursor.Site > 0);
            }
            catch
            {
                InvalidateLiveSurfaceCoverage();
                throw;
            }
            finally { GL.PopMatrix(); }
            for (int i = previous; i < stroke.points.Count; i++)
            {
                _liveSurfacePoints.Add(stroke.points[i]);
                _liveSurfacePath.Add(stroke.surf[i]);
                _liveSurfaceAngles.Add(TipSample(stroke, i));
            }
            _liveSurfaceStroke = stroke;
            _liveSurfaceMesh = mesh;
            _liveSurfacePrefix = prefix;
            _liveSurfacePose = mesh.PoseVersion;
            _liveSurfaceSize = new Vector2Int(target.width, target.height);
            _liveSurfaceSettings = new SurfaceSettings(stroke, grainCellsPerUV);
            RenderTexture.active = target;
        }

        void ConfigureSurface(PaintStroke stroke, RenderTexture target, float grainCellsPerUV)
        {
            var shape = stroke.shape ?? DefaultDabShape;
            ConfigureStamp(shape, preview: false);
            Material.SetFloat("_Hardness", shape.hardness);
            Material.SetFloat("_Grain", shape.grain);
            Material.SetVector("_GrainFreq", new Vector4(grainCellsPerUV, grainCellsPerUV * target.height / target.width, 0, 0));
            Material.SetFloat("_Accumulate", shape.buildUp ? 1 : 0);
            Material.SetFloat("_TwoSided", stroke.surfTwoSided ? 1 : 0);
        }

        internal static void StreamSurfaceDabs<T>(PaintStroke stroke, MeshUV mesh, ref SurfaceCursor cursor, ref T sink)
            where T : struct, ISurfaceDabSink
        {
            var shape = stroke.shape ?? DefaultDabShape;
            float radius = stroke.surfRadius * mesh.PosedSize;
            if (radius <= 0f) { cursor.Count = stroke.surf.Count; return; }
            for (int i = cursor.Count; i < stroke.surf.Count; i++)
            {
                var sp = stroke.surf[i];
                var bary = new Vector3(1f - sp.y - sp.z, sp.y, sp.z);
                if (!mesh.SurfacePoint(Mathf.RoundToInt(sp.x), bary, out var pos, out var nrm))
                {
                    cursor.HavePrevious = false;
                    continue;
                }
                int flags = Mathf.RoundToInt(sp.w);
                if ((flags & 2) != 0) nrm = -nrm;
                float pressure = stroke.points[i].z;
                if (!cursor.HavePrevious || (flags & 1) == 0)
                {
                    EmitSurfaceDab(stroke, shape, mesh, pos, nrm, Vector3.zero, pressure, radius, PenAngle(stroke, i), ref cursor, ref sink);
                    cursor.Carry = 0f;
                }
                else
                {
                    float length = Vector3.Distance(cursor.Position, pos);
                    float step = Mathf.Max(radius * .02f, shape.spacing * radius * SizeFactor(stroke, Mathf.Min(cursor.Pressure, pressure)));
                    float at = step - cursor.Carry;
                    while (at <= length)
                    {
                        float k = length > 0 ? at / length : 1f;
                        EmitSurfaceDab(stroke, shape, mesh, Vector3.Lerp(cursor.Position, pos, k), Vector3.Slerp(cursor.Normal, nrm, k),
                            pos - cursor.Position,
                            Mathf.Lerp(cursor.Pressure, pressure, k), radius, InterpolatePenAngle(stroke, i - 1, i, k), ref cursor, ref sink);
                        at += step;
                    }
                    cursor.Carry = length - (at - step);
                }
                cursor.Position = pos; cursor.Normal = nrm; cursor.Pressure = pressure; cursor.HavePrevious = true;
            }
            cursor.Count = stroke.surf.Count;
        }

        static void EmitSurfaceDab<T>(PaintStroke stroke, BrushShape shape, MeshUV mesh, Vector3 pos, Vector3 nrm, Vector3 direction, float pressure,
                                     float baseRadius, float penAngle, ref SurfaceCursor cursor, ref T sink) where T : struct, ISurfaceDabSink
        {
            int site = cursor.Site++;
            if (shape.tip == BrushTip.Stamp)
            {
                EmitSurfaceStamp(stroke, shape, site, pos, nrm, direction, pressure, baseRadius, penAngle, ref sink);
                return;
            }
            var rng = new BrushRng(stroke.seed, site);
            float radius = baseRadius * SizeFactor(stroke, pressure);
            float opacity = OpacityFactor(stroke, pressure);
            if (shape.sizeJitter > 0) radius *= 1f - shape.sizeJitter * rng.Next();
            if (shape.opacityJitter > 0) opacity *= 1f - shape.opacityJitter * rng.Next();
            if (shape.buildUp) opacity *= shape.flow;
            radius = Mathf.Max(radius, 1e-6f);
            if (shape.tip == BrushTip.Drips)
            {
                BrushDrips.Emit(stroke, mesh, stroke.seed, site, pos, nrm, radius, opacity, ref sink);
                return;
            }
            sink.Emit(new Vector4(pos.x, pos.y, pos.z, radius), new Vector4(nrm.x, nrm.y, nrm.z, opacity), default, default);
        }

        static void EmitSurfaceStamp<T>(PaintStroke stroke, BrushShape shape, int site, Vector3 position, Vector3 normal,
            Vector3 direction, float pressure, float baseRadius, float penAngle, ref T sink) where T : struct, ISurfaceDabSink
        {
            var rng = new BrushRng(stroke.seed, site);
            uint seed = StampSeed(stroke, site);
            normal.Normalize();
            Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;
            if (along.sqrMagnitude < .001f) along = Vector3.Cross(Vector3.forward, normal).normalized;
            bool follows = FollowsDirection(stroke, shape);
            if (!float.IsFinite(penAngle) && follows && direction.sqrMagnitude > 1e-12f)
            {
                var following = Vector3.ProjectOnPlane(direction, normal);
                if (following.sqrMagnitude > 1e-12f) along = following.normalized;
            }
            var across = Vector3.Cross(normal, along);
            float radius = baseRadius * SizeFactor(stroke, pressure);
            float footprint = radius;
            float angle = (shape.angle + (follows ? -90f : 0f) + (float.IsFinite(penAngle) ? penAngle : 0f)) * Mathf.Deg2Rad;
            if (shape.angleJitter > 0) angle += (StampRandom(seed, 1) * 2 - 1) * shape.angleJitter * Mathf.PI;
            var tangent = along * Mathf.Cos(angle) + across * Mathf.Sin(angle);
            var bitangent = Vector3.Cross(normal, tangent);
            if (shape.sizeJitter > 0)
                radius *= shape.jitterVersion > 0 ? Mathf.Max(.05f, 1 + (StampRandom(seed, 2) * 2 - 1) * shape.sizeJitter) :
                    1 - shape.sizeJitter * rng.Next();
            float opacity = OpacityFactor(stroke, pressure);
            if (shape.opacityJitter > 0) opacity *= 1 - shape.opacityJitter *
                (shape.jitterVersion > 0 ? StampRandom(seed, 5) : rng.Next());
            if (shape.scatter > 0)
            {
                Vector2 offset = shape.jitterVersion > 0 ? new Vector2(StampRandom(seed, 3) * 2 - 1, StampRandom(seed, 4) * 2 - 1) : rng.InDisc();
                position += shape.jitterVersion > 0 ? (tangent * offset.x + bitangent * offset.y) * (footprint * shape.scatter) :
                    (along * offset.x + across * offset.y) * (baseRadius * shape.scatter);
            }
            if (shape.buildUp) opacity *= shape.flow;
            sink.Emit(new Vector4(position.x, position.y, position.z, Mathf.Max(radius, 1e-6f)),
                new Vector4(normal.x, normal.y, normal.z, opacity), new Vector4(tangent.x, tangent.y, tangent.z, 0),
                new Vector4(seed & 65535u, seed >> 16, Mathf.Max(.05f, shape.roundness), .35f));
        }

        readonly struct SurfaceSettings : IEquatable<SurfaceSettings>
        {
            readonly (int seed, BrushTip tip, float radius, bool pressureSize, bool pressureOpacity, bool twoSided, float grainCells) _stroke;
            readonly (float hardness, float grain, float spacing, float sizeJitter, float opacityJitter, float flow, bool buildUp) _shape;
            readonly (string id, int version, float angle, float roundness, float scatter, float jitter, int jitterVersion, bool follow) _stamp;
            readonly BrushPenSettings.Key _pen;

            internal SurfaceSettings(PaintStroke stroke, float grainCells)
            {
                var shape = stroke.shape ?? DefaultDabShape;
                _stroke = (stroke.seed, shape.tip, stroke.surfRadius, stroke.pressureSize, stroke.pressureOpacity, stroke.surfTwoSided, grainCells);
                _shape = (shape.hardness, shape.grain, shape.spacing, shape.sizeJitter, shape.opacityJitter, shape.flow, shape.buildUp);
                _stamp = (shape.alphaId, shape.alphaVersion, shape.angle, shape.roundness, shape.scatter,
                    shape.angleJitter, shape.jitterVersion, shape.followStroke);
                _pen = new BrushPenSettings.Key(stroke);
            }

            public bool Equals(SurfaceSettings other) => _stroke.Equals(other._stroke) && _shape.Equals(other._shape) &&
                _stamp.Equals(other._stamp) && _pen.Equals(other._pen);
        }
    }
}
