using System;
using UnityEngine;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer : IDisposable
    {
        public Material Material { get; }

        public BrushRenderer()
        {
            var s = Shader.Find("Hidden/Avistry/Paint");
            if (s == null) throw new Exception("Shader 'Hidden/Avistry/Paint' not found. Is the Avistry package complete?");
            Material = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Dispose()
        {
            InvalidateLiveSurfaceCoverage();
            _stampLease?.Dispose();
            _stampLease = null;
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            _cmd.Release();
        }

        internal static float SizeFactor(PaintStroke s, float pressure) => !s.pressureSize ? 1f : s.pressureModel == 1 && s.pen != null
            ? Mathf.Lerp(s.pen.minimumSize, 1f, s.pen.Response(pressure)) : Mathf.Lerp(.15f, 1f, Mathf.Clamp01(pressure));

        internal static float OpacityFactor(PaintStroke s, float pressure) => !s.pressureOpacity ? 1f : s.pressureModel == 1 && s.pen != null
            ? Mathf.Lerp(s.pen.minimumStrength, 1f, s.pen.Response(pressure)) : Mathf.Clamp01(pressure);

        public void RenderCoverage(PaintStroke s, RenderTexture target, float grainCellsPerUV) =>
            RenderWhole(s, target, grainCellsPerUV, s?.shape?.buildUp == true ? 2 : 0);

        public void RenderDirection(PaintStroke s, RenderTexture target) => RenderWhole(s, target, 1f, 3);


        const int DabsPerBatch = 32;
        readonly Vector4[] _dabPos = new Vector4[DabsPerBatch];
        readonly Vector4[] _dabNrm = new Vector4[DabsPerBatch];
        readonly UnityEngine.Rendering.CommandBuffer _cmd = new UnityEngine.Rendering.CommandBuffer { name = "Avistry 3D brush" };
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public void RenderSurface(PaintStroke s, RenderTexture target, MeshUV mesh, float grainCellsPerUV)
        {
            if (ReferenceEquals(target, _liveCoverageTarget)) InvalidateLiveCoverage();
            int w = target.width, h = target.height;
            RenderTexture.active = target;
            GL.Viewport(new Rect(0, 0, w, h));
            GL.Clear(false, true, Color.clear);
            if (s == null || mesh == null || !s.IsSurface) return;
            var sh = s.shape ?? DefaultDabShape;
            float baseR = s.surfRadius * mesh.PosedSize;
            if (baseR <= 0) return;
            var surfaceMesh = mesh.SurfaceMesh();
            ConfigureSurface(s, target, grainCellsPerUV);
            var batch = new SurfaceBatch(this, target, surfaceMesh, sh.buildUp ? 7 : 6, _dabPos, _dabNrm);
            var cursor = default(SurfaceCursor);
            GL.PushMatrix();
            try
            {
                StreamSurfaceDabs(s, mesh, ref cursor, ref batch);
                batch.Draw(target);
            }
            finally { GL.PopMatrix(); }
            RenderTexture.active = target;
        }

        (float offset, float radiusScale, float opacityScale)[] _bristles = Array.Empty<(float, float, float)>();
        int _bristleSeed, _bristleCount;

        int PrepareBristles(PaintStroke stroke, BrushShape shape)
        {
            if (shape.tip != BrushTip.Bristle) return 0;
            int count = Mathf.Max(1, shape.density);
            if (_bristleCount == count && _bristleSeed == stroke.seed) return count;
            if (_bristles.Length < count) _bristles = new (float, float, float)[count];
            for (int i = 0; i < count; i++)
            {
                var rng = new BrushRng(stroke.seed, 1000003 + i);
                float offset = count == 1 ? 0f : (i / (count - 1f)) * 2f - 1f;
                offset += (rng.Next() - 0.5f) * (1.6f / count);
                _bristles[i] = (offset, Mathf.Lerp(0.05f, 0.15f, rng.Next()), Mathf.Lerp(0.35f, 1f, rng.Next()));
            }
            _bristleSeed = stroke.seed;
            _bristleCount = count;
            return count;
        }

        void RenderWhole(PaintStroke s, RenderTexture target, float grainCellsPerUV, int pass)
        {
            if (ReferenceEquals(target, _liveCoverageTarget)) InvalidateLiveCoverage();
            var cursor = default(DabCursor);
            Render(s, target, grainCellsPerUV, pass, ref cursor);
        }

        void Render(PaintStroke s, RenderTexture target, float grainCellsPerUV, int pass, ref DabCursor cursor, bool retain = false)
        {
            int w = target.width, h = target.height;
            if (retain) BindLiveCoverageTarget(target, cursor.Count > 0);
            else RenderTexture.active = target;
            GL.Viewport(new Rect(0, 0, w, h));
            if (cursor.Count == 0) GL.Clear(false, true, Color.clear);
            if (s?.points == null || s.points.Count == 0) return;
            var sh = s.shape ?? DefaultDabShape;

            Material.SetFloat("_Hardness", sh.hardness);
            Material.SetFloat("_Tip", sh.tip == BrushTip.Square ? 1 : 0);
            Material.SetFloat("_Grain", sh.grain);
            Material.SetVector("_GrainFreq", new Vector4(grainCellsPerUV, grainCellsPerUV * h / (float)w, 0, 0));
            ConfigureStamp(sh, PreviewStamp);
            Material.SetPass(pass);
            bool coherentBristles = UsesCoherentBristles(s, sh);
            int bristleCount = coherentBristles ? 0 : PrepareBristles(s, sh);
            var bristles = coherentBristles ? BristlesFor(s, sh, retain, cursor.Count == 0) : null;

            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);

            float baseR = s.radius * w;
            float dotRadius = sh.HasDots && s.fixedDotRadius > 0f ? s.fixedDotRadius * w : 0f;
            int site = cursor.Site;
            float carry = cursor.Carry;
            int first = cursor.Count;
            Vector2 dir = cursor.Direction;
            var prev = s.points[Mathf.Max(0, first - 1)];
            if (first == 0)
            {
                dir = RunDirection(s, 0, w, h);
                EmitSite(s, sh, prev, dir, baseR, dotRadius, w, h, site++, bristleCount, bristles, PenAngle(s, 0));
                first = 1;
            }
            for (int i = first; i < s.points.Count; i++)
            {
                var p = s.points[i];
                if (p.w < 0.5f)
                {
                    dir = RunDirection(s, i, w, h);
                    bristles?.ResetRun();
                    EmitSite(s, sh, p, dir, baseR, dotRadius, w, h, site++, bristleCount, bristles, PenAngle(s, i));
                    carry = 0f;
                    prev = p;
                    continue;
                }
                var seg = new Vector2((p.x - prev.x) * w, (p.y - prev.y) * h);
                float len = seg.magnitude;
                if (len > 1e-4f) dir = seg / len;
                float step = coherentBristles ? CoherentBristleStep(sh, baseR) :
                    Mathf.Max(0.5f, sh.spacing * baseR * SizeFactor(s, Mathf.Min(prev.z, p.z)));
                float t = step - carry;
                while (t <= len)
                {
                    float blend = len > 0 ? t / len : 1f;
                    EmitSite(s, sh, Vector4.Lerp(prev, p, blend), dir, baseR, dotRadius, w, h, site++, bristleCount, bristles,
                        InterpolatePenAngle(s, i - 1, i, blend));
                    t += step;
                }
                carry = len - (t - step);
                prev = p;
            }
            cursor = new DabCursor { Count = s.points.Count, Site = site, Carry = carry, Direction = dir };

            GL.End();
            GL.PopMatrix();
        }

        static Vector2 RunDirection(PaintStroke s, int i, int w, int h)
        {
            if (i + 1 < s.points.Count && s.points[i + 1].w > 0.5f)
            {
                var a = s.points[i];
                var b = s.points[i + 1];
                var d = new Vector2((b.x - a.x) * w, (b.y - a.y) * h);
                if (d.sqrMagnitude > 1e-8f) return d.normalized;
            }
            return Vector2.right;
        }

        void EmitSite(PaintStroke s, BrushShape sh, Vector4 p, Vector2 dir, float baseR, float dotRadius, int w, int h, int site,
                      int bristleCount, BristleState bristles, float penAngle)
        {
            uint stampSeed = StampSeed(s, site);
            GL.MultiTexCoord2(2, dir.x, dir.y);
            GL.MultiTexCoord2(3, stampSeed & 65535u, stampSeed >> 16);
            if (bristles != null)
            {
                bristles.TipAngle = penAngle;
                var sink = new BristleDraw(w, h);
                EmitBristleDab(s, sh, p, dir, baseR, w, h, bristles, ref sink);
                return;
            }
            var rng = new BrushRng(s.seed, site);
            float size = SizeFactor(s, p.z);
            float r = baseR * size;
            float footprint = r;
            float fixedDot = dotRadius * size;
            float a = OpacityFactor(s, p.z);
            if (sh.sizeJitter > 0)
            {
                float jitter = sh.jitterVersion == 1
                    ? Mathf.Max(.05f, 1f + (StampRandom(stampSeed, 2) * 2 - 1) * sh.sizeJitter)
                    : 1f - sh.sizeJitter * rng.Next();
                r *= jitter;
                fixedDot *= jitter;
            }
            if (sh.opacityJitter > 0) a *= 1f - sh.opacityJitter *
                (sh.jitterVersion == 1 ? StampRandom(stampSeed, 5) : rng.Next());
            float angle = TipRotation(s, sh, dir, penAngle);
            if (sh.tip == BrushTip.Stamp && FollowsDirection(s, sh)) angle -= Mathf.PI * .5f;
            if (sh.jitterVersion == 1) angle += (StampRandom(stampSeed, 1) * 2 - 1) * sh.angleJitter * Mathf.PI;
            var c = new Vector2(p.x * w, p.y * h);
            if (sh.scatter > 0)
            {
                if (sh.jitterVersion == 1)
                {
                    var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var across = new Vector2(-along.y, along.x);
                    c += (along * (StampRandom(stampSeed, 3) * 2 - 1) + across * (StampRandom(stampSeed, 4) * 2 - 1)) *
                        (sh.scatter * footprint);
                }
                else c += rng.InDisc() * (sh.scatter * baseR);
            }
            if (sh.buildUp) a *= sh.flow;

            switch (sh.tip)
            {
                case BrushTip.Drips:
                    EmitFlatDrips(s.seed, site, c, r, a, w, h);
                    break;
                case BrushTip.Bristle:
                {
                    var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var perp = new Vector2(-along.y, along.x);
                    for (int b = 0; b < bristleCount; b++)
                    {
                        var bristle = _bristles[b];
                        var offset = perp * (bristle.offset * r * 0.9f);
                        Quad(c + offset, r * bristle.radiusScale, a * bristle.opacityScale, angle, 1f, w, h);
                    }
                    break;
                }
                case BrushTip.Spray:
                {
                    int n = Mathf.Max(1, sh.density);
                    for (int k = 0; k < n; k++)
                    {
                        var pos = c + rng.InDisc() * r;
                        float variation = rng.Next();
                        float rr = fixedDot > 0f ? fixedDot * Mathf.Lerp(0.65f, 1.35f, variation)
                            : r * Mathf.Lerp(0.03f, 0.10f, variation);
                        Quad(pos, rr, a * Mathf.Lerp(0.5f, 1f, rng.Next()), 0f, 1f, w, h);
                    }
                    break;
                }
                default:
                    Quad(c, fixedDot > 0f ? fixedDot : r, a, angle, sh.roundness, w, h);
                    break;
            }
        }

        static void Quad(Vector2 c, float r, float alpha, float angle, float roundness, int w, int h)
        {
            r = Mathf.Max(0.6f, r);
            float ry = Mathf.Max(0.6f, r * roundness);
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            var ax = new Vector2(ca, sa) * r;
            var ay = new Vector2(-sa, ca) * ry;
            Corner(c - ax - ay, -1, -1, alpha, w, h);
            Corner(c - ax + ay, -1, 1, alpha, w, h);
            Corner(c + ax + ay, 1, 1, alpha, w, h);
            Corner(c + ax - ay, 1, -1, alpha, w, h);
        }

        static void Corner(Vector2 px, float tx, float ty, float alpha, int w, int h)
        {
            float u = px.x / w, v = px.y / h;
            GL.MultiTexCoord3(0, tx, ty, alpha);
            GL.Vertex3(u, v, 0);
        }
    }
}
