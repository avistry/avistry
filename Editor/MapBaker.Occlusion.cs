using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public static partial class MapBaker
    {
        public static AoResult BakeAO(MeshUV uv, Texture mainTex, int w, int h, AoSettings s)
        {
            var result = new AoResult { width = w, height = h };
            using var steps = OcclusionSteps(uv, mainTex, w, h, s, false, default, null, made => result = made);
            try
            {
                while (StepOcclusion(steps))
                    if (EditorUtility.DisplayCancelableProgressBar(Loc.T("Ambient occlusion"), Loc.T("Reading the model…"), steps.Current)) break;
            }
            finally { EditorUtility.ClearProgressBar(); }
            return result;
        }

        internal static bool StepOcclusion(IEnumerator<float> steps)
        {
            var surface = RenderTexture.active;
            var camera = Camera.current;
            try { Camera.SetupCurrent(null); return steps.MoveNext(); }
            finally { RenderTexture.active = surface; Camera.SetupCurrent(camera); }
        }

        internal static IEnumerator<float> OcclusionSteps(MeshUV uv, Texture mainTex, int w, int h, AoSettings s,
            bool background, CancellationToken cancel, Func<bool> current, Action<AoResult> completed)
        {
            var res = new AoResult { width = w, height = h };
            var t0 = EditorApplication.timeSinceStartup;
            uv.RefreshPose();
            var pos = uv.PosedCombinedCached();

            bool any = false;
            var rb = new Bounds();
            foreach (int i in uv.tris)
            {
                var p = pos[i];
                if (float.IsNaN(p.x)) continue;
                if (!any) { rb = new Bounds(p, Vector3.zero); any = true; }
                else rb.Encapsulate(p);
            }
            if (!any) throw new Exception("The mesh couldn't be read in its current pose.");

            var casters = new List<Renderer>();
            foreach (var part in uv.parts) if (part.renderer != null && !casters.Contains(part.renderer)) casters.Add(part.renderer);
            var root = uv.AvatarRoot();
            var avatarBounds = rb;
            if (root != null)
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!Usable(r)) continue;
                    avatarBounds.Encapsulate(r.bounds);
                    if (s.otherMeshes && !casters.Contains(r)) casters.Add(r);
                }
            float avatarSize = Mathf.Max(avatarBounds.size.x, Mathf.Max(avatarBounds.size.y, avatarBounds.size.z));
            float spread = avatarSize * (0.005f + 0.3f * s.spread * s.spread);

            Mesh recv = null, occ = null;
            RenderTexture posRT = null, nrmRT = null, accRT = null, aoRT = null, outRT = null;
            RenderTexture[] depthRT = null;
            Material mat = null;
            try
            {
                recv = ReceiverMesh(uv, pos);
                occ = CasterMesh(casters, out int casterCount);
                res.casters = casterCount;

                int n = Mathf.Clamp(s.samples, 16, 512);
                int S = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(w, h)), 512, 2048);
                float halfWidth = rb.extents.magnitude + spread;
                float texelWorld = 2f * halfWidth / S;
                var fmtF = SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RFloat) ? RenderTextureFormat.RFloat : RenderTextureFormat.RHalf;
                var fmtAcc = SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBFloat) ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf;

                posRT = NewRT(w, h, RenderTextureFormat.ARGBFloat);
                nrmRT = NewRT(w, h, RenderTextureFormat.ARGBHalf);
                depthRT = new RenderTexture[4];
                for (int i = 0; i < depthRT.Length; i++) depthRT[i] = NewRT(S, S, fmtF);
                accRT = NewRT(w, h, fmtAcc);
                mat = new Material(Mat) { hideFlags = HideFlags.HideAndDontSave };
                float peelGap = texelWorld * 2f;

                DrawTextureSpace(posRT, recv, mat, 0);
                DrawTextureSpace(nrmRT, recv, mat, 1);
                RenderTexture.active = accRT;
                GL.Clear(false, true, Color.clear);

                mat.SetTexture("_PosTex", posRT);
                mat.SetTexture("_NrmTex", nrmRT);
                mat.SetTexture("_DepthTex", depthRT[0]);
                mat.SetTexture("_DepthTex1", depthRT[1]);
                mat.SetTexture("_DepthTex2", depthRT[2]);
                mat.SetTexture("_DepthTex3", depthRT[3]);
                mat.SetVector("_LightCentre", new Vector4(rb.center.x, rb.center.y, rb.center.z, 1f / (2f * halfWidth)));
                mat.SetVector("_AoParams", new Vector4(texelWorld * 1.0f, texelWorld * 1.0f + spread * 0.01f, spread, 1f));
                float clearH = fmtF == RenderTextureFormat.RHalf ? -60000f : -1e6f;

                float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
                for (int i = 0; i < n; i++)
                {
                    yield return 0.05f + 0.8f * i / n;
                    float y = 1f - 2f * (i + 0.5f) / n;
                    float rad = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float phi = i * golden;
                    var d = new Vector3(Mathf.Cos(phi) * rad, y, Mathf.Sin(phi) * rad).normalized;
                    var right = Vector3.Cross(Mathf.Abs(d.y) < 0.99f ? Vector3.up : Vector3.right, d).normalized;
                    var up = Vector3.Cross(d, right);
                    mat.SetVector("_LightDir", d);
                    mat.SetVector("_LightRight", right);
                    mat.SetVector("_LightUp", up);

                    for (int layer = 0; layer < depthRT.Length; layer++)
                    {
                        mat.SetVector("_Peel", new Vector4(layer > 0 ? 1 : 0, peelGap, 0, 0));
                        if (layer > 0) mat.SetTexture("_PrevTex", depthRT[layer - 1]);
                        RenderTexture.active = depthRT[layer];
                        GL.Viewport(new Rect(0, 0, S, S));
                        GL.Clear(false, true, new Color(clearH, clearH, clearH, clearH));
                        GL.PushMatrix();
                        GL.LoadOrtho();
                        mat.SetPass(2);
                        Graphics.DrawMeshNow(occ, Matrix4x4.identity);
                        GL.PopMatrix();
                    }

                    Graphics.Blit(posRT, accRT, mat, 3);
                }

                yield return 0.88f;
                aoRT = NewRT(w, h, RenderTextureFormat.ARGBHalf);
                int step = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(w, h) / 1024f));
                float sigma = Mathf.Max(spread * 0.5f, texelWorld * 2f);
                mat.SetVector("_BlurParams", new Vector4(3, step, 1f / (sigma * sigma), 0));
                Graphics.Blit(accRT, aoRT, mat, 4);
                outRT = NewRT(w, h, RenderTextureFormat.ARGB32);
                Graphics.Blit(aoRT, outRT);
                var px = ReadRT(outRT);
                RenderTexture.active = null;
                Free(recv, occ, posRT, nrmRT, accRT, aoRT, outRT, mat);
                Free(depthRT);

                yield return 0.94f;
                byte[] png;
                if (background)
                {
                    float strength = s.strength;
                    var encoding = Task.Run(() => EncodeOcclusion(px, w, h, strength, cancel), cancel);
                    encoding.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    while (!encoding.IsCompleted) yield return 0.95f;
                    png = encoding.GetAwaiter().GetResult();
                }
                else png = EncodeOcclusion(px, w, h, s.strength, cancel);
                if (current != null && !current()) yield break;
                res.texture = SaveEncodedMap(mainTex, "AO", AoTag, png, w, h, out res.path);
                res.directions = n;
                res.seconds = EditorApplication.timeSinceStartup - t0;
                completed(res);
            }
            finally
            {
                Free(recv, occ, posRT, nrmRT, accRT, aoRT, outRT, mat);
                if (depthRT != null) Free(depthRT);
            }
        }

        static byte[] EncodeOcclusion(Color32[] px, int w, int h, float strength, CancellationToken cancel)
        {
            var val = new ushort[px.Length];
            var covered = new bool[px.Length];
            float k = Mathf.Max(0f, strength);
            for (int i = 0; i < px.Length; i++)
            {
                if ((i & 16383) == 0) cancel.ThrowIfCancellationRequested();
                covered[i] = px[i].a > 127;
                float a = covered[i] ? Mathf.Pow(px[i].r / 255f, k) : 1f;
                val[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt(a * 65535f), 0, 65535);
            }
            MeshUV.DilateValues(val, covered, w, h, Mathf.Max(4, Mathf.Max(w, h) / 128), cancel);
            for (int i = 0; i < px.Length; i++)
            {
                byte b = covered[i] ? (byte)(val[i] >> 8) : (byte)255;
                px[i] = new Color32(b, b, b, 255);
            }
            return MapPixels.EncodeRgba(px, w, h);
        }
    }
}
