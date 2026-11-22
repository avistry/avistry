using System;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Turns a stroke path into dabs and draws its coverage into a render target (GL quads, Hidden/Avistry/Paint).
    /// Dab placement is done in UV space, so a stroke looks the same at the working size and at bake size.
    /// Used by the compositor and by the brush preview in the panel.
    /// </summary>
    public sealed class BrushRenderer : IDisposable
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
            if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
            _cmd.Release();
        }

        static float SizeFactor(PaintStroke s, float pressure) => s.pressureSize ? Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(pressure)) : 1f;

        /// <summary>
        /// Clears <paramref name="target"/> and draws the stroke's coverage into it. The caller restores RenderTexture.active.
        /// </summary>
        /// <param name="grainCellsPerUV">Grain cells across the texture width (source width / grain size in px).</param>
        public void RenderCoverage(PaintStroke s, RenderTexture target, float grainCellsPerUV) =>
            Render(s, target, grainCellsPerUV, (s?.shape ?? new BrushShape()).buildUp ? 2 : 0);

        /// <summary>
        /// Smudge: draws the stroke's direction (pixel space, packed 0..1 in RG) under each dab, later dabs on top,
        /// so the smudge pass knows which way the brush was moving at every pixel.
        /// </summary>
        public void RenderDirection(PaintStroke s, RenderTexture target) => Render(s, target, 1f, 3);


        const int DabsPerBatch = 32;
        readonly Vector4[] _dabPos = new Vector4[DabsPerBatch];
        readonly Vector4[] _dabNrm = new Vector4[DabsPerBatch];
        // Kept for the renderer's lifetime: RenderSurface runs on every compose while a stroke on the model is drawn.
        readonly UnityEngine.Rendering.CommandBuffer _cmd = new UnityEngine.Rendering.CommandBuffer { name = "Avistry 3D brush" };
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        /// <summary>
        /// 3D brush: clears <paramref name="target"/> (R = coverage, G = mesh covers the texel) and draws the stroke as
        /// spheres on the posed model, by drawing the mesh in texture space and measuring 3D distances per texel.
        /// Dabs are placed along straight lines between the stroke's surface points, spaced in world units.
        /// </summary>
        public void RenderSurface(PaintStroke s, RenderTexture target, MeshUV mesh, float grainCellsPerUV)
        {
            int w = target.width, h = target.height;
            RenderTexture.active = target;
            GL.Viewport(new Rect(0, 0, w, h));
            GL.Clear(false, true, Color.clear);
            if (s == null || mesh == null || !s.IsSurface) return;
            var sh = s.shape ?? new BrushShape();
            float baseR = s.surfRadius * mesh.PosedSize;
            if (baseR <= 0) return;
            var surfaceMesh = mesh.SurfaceMesh();

            Material.SetFloat("_Hardness", sh.hardness);
            Material.SetFloat("_Grain", sh.grain);
            Material.SetVector("_GrainFreq", new Vector4(grainCellsPerUV, grainCellsPerUV * h / (float)w, 0, 0));
            Material.SetFloat("_Accumulate", sh.buildUp ? 1 : 0);
            Material.SetFloat("_TwoSided", s.surfTwoSided ? 1 : 0);
            int pass = sh.buildUp ? 7 : 6;

            // Drawn through a command buffer with its own matrices: DrawMeshNow depends on the camera state left behind
            // by the last render, and while the Scene view had a camera active the 3D brush painted nothing.
            var cmd = _cmd;
            var mpb = _mpb;
            var uvProj = Matrix4x4.Ortho(0, 1, 0, 1, -1, 100);
            int count = 0, site = 0;
            Vector3 bMin = Vector3.zero, bMax = Vector3.zero;

            void Flush()
            {
                if (count == 0) return;
                mpb.SetVectorArray("_DabPos", _dabPos);
                mpb.SetVectorArray("_DabNrm", _dabNrm);
                mpb.SetFloat("_DabCount", count);
                mpb.SetVector("_BatchMin", bMin);
                mpb.SetVector("_BatchMax", bMax);
                cmd.Clear();
                cmd.SetRenderTarget(target);
                cmd.SetViewport(new Rect(0, 0, w, h));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, uvProj);
                cmd.DrawMesh(surfaceMesh, Matrix4x4.identity, Material, 0, pass, mpb);
                Graphics.ExecuteCommandBuffer(cmd);
                count = 0;
            }

            void Emit(Vector3 pos, Vector3 nrm, float pressure)
            {
                var rng = new BrushRng(s.seed, site++);
                float r = baseR * SizeFactor(s, pressure);
                float a = s.pressureOpacity ? Mathf.Clamp01(pressure) : 1f;
                if (sh.sizeJitter > 0) r *= 1f - sh.sizeJitter * rng.Next();
                if (sh.opacityJitter > 0) a *= 1f - sh.opacityJitter * rng.Next();
                if (sh.buildUp) a *= sh.flow;
                r = Mathf.Max(r, 1e-6f);
                var lo = pos - Vector3.one * r;
                var hi = pos + Vector3.one * r;
                if (count == 0) { bMin = lo; bMax = hi; }
                else { bMin = Vector3.Min(bMin, lo); bMax = Vector3.Max(bMax, hi); }
                _dabPos[count] = new Vector4(pos.x, pos.y, pos.z, r);
                _dabNrm[count] = new Vector4(nrm.x, nrm.y, nrm.z, a);
                if (++count == DabsPerBatch) Flush();
            }

            bool havePrev = false;
            Vector3 prevPos = default, prevNrm = default;
            float prevPressure = 1f, carry = 0f;
            // The command buffer leaves its UV-space matrices current; callers expect the ones they had.
            GL.PushMatrix();
            try
            {
                for (int i = 0; i < s.surf.Count; i++)
                {
                    var sp = s.surf[i];
                    var bary = new Vector3(1f - sp.y - sp.z, sp.y, sp.z);
                    if (!mesh.SurfacePoint(Mathf.RoundToInt(sp.x), bary, out var pos, out var nrm)) { havePrev = false; continue; }
                    int flags = Mathf.RoundToInt(sp.w);
                    if ((flags & 2) != 0) nrm = -nrm; // the back of the face was painted: the brush faces the other way
                    float pressure = s.points[i].z;
                    if (!havePrev || (flags & 1) == 0)
                    {
                        Emit(pos, nrm, pressure);
                        carry = 0f;
                    }
                    else
                    {
                        float len = Vector3.Distance(prevPos, pos);
                        float step = Mathf.Max(baseR * 0.02f, sh.spacing * baseR * SizeFactor(s, Mathf.Min(prevPressure, pressure)));
                        float t = step - carry;
                        while (t <= len)
                        {
                            float k = len > 0 ? t / len : 1f;
                            Emit(Vector3.Lerp(prevPos, pos, k), Vector3.Slerp(prevNrm, nrm, k), Mathf.Lerp(prevPressure, pressure, k));
                            t += step;
                        }
                        carry = len - (t - step);
                    }
                    prevPos = pos; prevNrm = nrm; prevPressure = pressure; havePrev = true;
                }
                Flush();
            }
            finally { GL.PopMatrix(); }
            RenderTexture.active = target;
        }

        static Vector2 s_dir; // direction of the dab being emitted (read by Corner)

        void Render(PaintStroke s, RenderTexture target, float grainCellsPerUV, int pass)
        {
            int w = target.width, h = target.height;
            RenderTexture.active = target;
            GL.Viewport(new Rect(0, 0, w, h));
            GL.Clear(false, true, Color.clear);
            if (s?.points == null || s.points.Count == 0) return;
            var sh = s.shape ?? new BrushShape();

            Material.SetFloat("_Hardness", sh.hardness);
            Material.SetFloat("_Tip", sh.tip == BrushTip.Square ? 1 : 0);
            Material.SetFloat("_Grain", sh.grain);
            Material.SetVector("_GrainFreq", new Vector4(grainCellsPerUV, grainCellsPerUV * h / (float)w, 0, 0));
            Material.SetPass(pass);

            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);

            float baseR = s.radius * w; // pixels at this resolution
            int site = 0;
            float carry = 0f;
            var prev = s.points[0];
            var dir = RunDirection(s, 0, w, h);
            EmitSite(s, sh, prev, dir, baseR, w, h, site++);
            for (int i = 1; i < s.points.Count; i++)
            {
                var p = s.points[i];
                if (p.w < 0.5f) // not joined: start a new run (the brush crossed onto another UV island)
                {
                    dir = RunDirection(s, i, w, h);
                    EmitSite(s, sh, p, dir, baseR, w, h, site++);
                    carry = 0f;
                    prev = p;
                    continue;
                }
                var seg = new Vector2((p.x - prev.x) * w, (p.y - prev.y) * h);
                float len = seg.magnitude;
                if (len > 1e-4f) dir = seg / len;
                float step = Mathf.Max(0.5f, sh.spacing * baseR * SizeFactor(s, Mathf.Min(prev.z, p.z)));
                float t = step - carry;
                while (t <= len)
                {
                    EmitSite(s, sh, Vector4.Lerp(prev, p, len > 0 ? t / len : 1f), dir, baseR, w, h, site++);
                    t += step;
                }
                carry = len - (t - step);
                prev = p;
            }

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

        void EmitSite(PaintStroke s, BrushShape sh, Vector4 p, Vector2 dir, float baseR, int w, int h, int site)
        {
            var rng = new BrushRng(s.seed, site);
            s_dir = dir;
            float r = baseR * SizeFactor(s, p.z);
            float a = s.pressureOpacity ? Mathf.Clamp01(p.z) : 1f;
            if (sh.sizeJitter > 0) r *= 1f - sh.sizeJitter * rng.Next();
            if (sh.opacityJitter > 0) a *= 1f - sh.opacityJitter * rng.Next();
            var c = new Vector2(p.x * w, p.y * h);
            if (sh.scatter > 0) c += rng.InDisc() * (sh.scatter * baseR);
            if (sh.buildUp) a *= sh.flow;

            float angle = sh.angle * Mathf.Deg2Rad + (sh.followStroke ? Mathf.Atan2(dir.y, dir.x) : 0f);

            switch (sh.tip)
            {
                case BrushTip.Bristle:
                {
                    // Bristles keep the same offset, size and strength for the whole stroke, so each one draws a streak.
                    var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var perp = new Vector2(-along.y, along.x);
                    int n = Mathf.Max(1, sh.density);
                    for (int b = 0; b < n; b++)
                    {
                        var br = new BrushRng(s.seed, 1000003 + b);
                        float off = n == 1 ? 0f : (b / (n - 1f)) * 2f - 1f;
                        off += (br.Next() - 0.5f) * (1.6f / n);
                        float rb = r * Mathf.Lerp(0.05f, 0.15f, br.Next());
                        float ab = a * Mathf.Lerp(0.35f, 1f, br.Next());
                        Quad(c + perp * (off * r * 0.9f), rb, ab, angle, 1f, w, h);
                    }
                    break;
                }
                case BrushTip.Spray:
                {
                    int n = Mathf.Max(1, sh.density);
                    for (int k = 0; k < n; k++)
                    {
                        var pos = c + rng.InDisc() * r;
                        float rr = r * Mathf.Lerp(0.03f, 0.10f, rng.Next());
                        Quad(pos, rr, a * Mathf.Lerp(0.5f, 1f, rng.Next()), 0f, 1f, w, h);
                    }
                    break;
                }
                default:
                    Quad(c, r, a, angle, sh.roundness, w, h);
                    break;
            }
        }

        /// <summary>One dab: centre and radius in pixels, rotated by angle, squashed by roundness.</summary>
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
            GL.MultiTexCoord2(1, u, v);
            GL.MultiTexCoord2(2, s_dir.x, s_dir.y);
            GL.Vertex3(u, v, 0);
        }
    }
}
