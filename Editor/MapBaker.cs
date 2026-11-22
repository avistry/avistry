using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Avistry.Textures
{
    /// <summary>
    /// Generated maps: an ambient occlusion bake from the posed model (GPU, Hidden/Avistry/Maps) and a normal map made from
    /// the light and dark details of a colour texture (CPU). Both are saved as PNGs next to the texture.
    /// </summary>
    public static class MapBaker
    {
        public const string AoTag = "AvatarTextureStudio:ao";
        public const string NormalTag = "AvatarTextureStudio:normal";
        public const string AoLayerMark = "ao";

        [Serializable]
        public class AoSettings
        {
            [Tooltip("How many directions the shading is measured from. More = smoother, a little slower")]
            public int samples = 128;
            [Tooltip("How far the shading reaches from creases and from parts that are close. Low = only tight creases, high = broad soft shading")]
            [Range(0, 1)] public float spread = 0.35f;
            [Tooltip("How dark the shading gets. You can also lower the layer's Opacity afterwards")]
            [Range(0, 2)] public float strength = 1f;
            [Tooltip("Clothing, hair and the avatar's other parts also shade this one (for example a collar on the neck). " +
                     "Leave it off if those parts can be toggled off in game, or their shadow stays on the skin")]
            public bool otherMeshes;
            [Tooltip("Size of the baked image (0 = the same as the texture)")]
            public int size;
        }

        [Serializable]
        public class NormalSettings
        {
            [Tooltip("On: use the texture with your layers, as you see it. Off: use the original texture")]
            public bool withLayers = true;
            [Tooltip("How deep the bumps look")]
            [Range(0, 5)] public float strength = 1f;
            [Tooltip("Softens the bumps: low keeps fine grain and noise, high keeps only broad shapes")]
            [Range(0, 1)] public float smoothness = 0.3f;
            [Tooltip("Off: light parts stick out. On: dark parts stick out (useful when dark lines are raised seams)")]
            public bool darkRaised;
            [Tooltip("Keep the bumps of the normal map the material already has and add these on top")]
            public bool combine = true;
        }


        /// <summary>Where a map for this texture goes: reuses the file this tool made before, never overwrites anything else.</summary>
        public static string MapPath(Texture mainTex, string suffix, string tag)
        {
            string dir = Exporter.WritableFolder(AssetDatabase.GetAssetPath(mainTex));
            string path = $"{dir}/{mainTex.name}_{suffix}.png";
            var existing = AssetImporter.GetAtPath(path) as TextureImporter;
            if (existing != null && !(existing.userData ?? "").StartsWith(tag)) path = AssetDatabase.GenerateUniqueAssetPath(path);
            return path;
        }

        internal static Texture2D SavePng(string path, Color32[] px, int w, int h, string userData, bool normalMap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                int maxSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(w, h)), 32, 8192);
                // The import above already applied the file; only reimport when a setting actually differs.
                bool changed = imp.userData != userData || imp.textureType != type || imp.sRGBTexture
                    || imp.alphaSource != TextureImporterAlphaSource.None || !imp.mipmapEnabled
                    || (!normalMap && imp.wrapMode != TextureWrapMode.Clamp) || imp.maxTextureSize != maxSize
                    || imp.textureCompression != TextureImporterCompression.CompressedHQ;
                if (changed)
                {
                    imp.userData = userData;
                    imp.textureType = type;
                    imp.sRGBTexture = false;
                    imp.alphaSource = TextureImporterAlphaSource.None;
                    imp.mipmapEnabled = true;
                    if (!normalMap) imp.wrapMode = TextureWrapMode.Clamp;
                    imp.maxTextureSize = maxSize;
                    imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material _mat;
        static bool _reloadHooked;
        static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                if (!_reloadHooked)
                {
                    _reloadHooked = true;
                    // HideAndDontSave objects survive a domain reload as orphans, so free the material before it.
                    AssemblyReloadEvents.beforeAssemblyReload += () =>
                    {
                        if (_mat != null) Object.DestroyImmediate(_mat);
                        _mat = null;
                    };
                }
                var s = Shader.Find("Hidden/Avistry/Maps");
                if (s == null) throw new Exception("Shader 'Hidden/Avistry/Maps' not found. Is the Avistry package complete?");
                return _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        static RenderTexture NewRT(int w, int h, RenderTextureFormat fmt, FilterMode filter = FilterMode.Point)
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(w, h, fmt, 0) { sRGB = false, msaaSamples = 1 })
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = filter };
            rt.Create();
            return rt;
        }

        static void Free(params Object[] objs)
        {
            foreach (var o in objs)
            {
                if (o == null) continue;
                if (o is RenderTexture rt) rt.Release();
                Object.DestroyImmediate(o);
            }
        }

        static Color32[] ReadRT(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }


        public struct AoResult
        {
            public Texture2D texture;
            public string path;
            public int width, height, directions, casters;
            public double seconds;
        }

        /// <summary>
        /// Bakes ambient occlusion for the mesh being edited, in its current pose, and saves it as &lt;texture&gt;_AO.png
        /// (white = open, darker = in a crease). Shows a progress bar; returns null texture when cancelled.
        /// </summary>
        public static AoResult BakeAO(MeshUV uv, Texture mainTex, int w, int h, AoSettings s)
        {
            var res = new AoResult { width = w, height = h };
            var t0 = EditorApplication.timeSinceStartup;
            uv.RefreshPose(); // the avatar may have moved or been posed since the mesh was read
            var pos = uv.PosedCombinedCached();

            // Receivers (the mesh being edited) and their bounds.
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

            // Shadow casters: the edited renderers (all their materials), plus the rest of the avatar if asked.
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
            var prevActive = RenderTexture.active;
            // With a camera still current (e.g. a bake started from Scene view code), DrawMeshNow under
            // GL.LoadOrtho draws nothing and the map comes out all white.
            var prevCam = Camera.current;
            Camera.SetupCurrent(null);
            try
            {
                EditorUtility.DisplayProgressBar(Loc.T("Ambient occlusion"), Loc.T("Reading the model…"), 0f);
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
                depthRT = new[] { NewRT(S, S, fmtF), NewRT(S, S, fmtF), NewRT(S, S, fmtF), NewRT(S, S, fmtF) };
                accRT = NewRT(w, h, fmtAcc);
                var mat = Mat;
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
                    if (i % 16 == 0 && EditorUtility.DisplayCancelableProgressBar(Loc.T("Ambient occlusion"), Loc.F("Shading from direction {0} of {1}…", i + 1, n), 0.05f + 0.8f * i / n))
                        return res; // cancelled: nothing saved
                    // Fibonacci sphere: evenly spread directions
                    float y = 1f - 2f * (i + 0.5f) / n;
                    float rad = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float phi = i * golden;
                    var d = new Vector3(Mathf.Cos(phi) * rad, y, Mathf.Sin(phi) * rad).normalized;
                    var right = Vector3.Cross(Mathf.Abs(d.y) < 0.99f ? Vector3.up : Vector3.right, d).normalized;
                    var up = Vector3.Cross(d, right);
                    mat.SetVector("_LightDir", d);
                    mat.SetVector("_LightRight", right);
                    mat.SetVector("_LightUp", up);

                    // Peel the four surfaces nearest the sky, so a texel can find the closest thing above it, not
                    // only the top of whatever is in the way.
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

                EditorUtility.DisplayProgressBar(Loc.T("Ambient occlusion"), Loc.T("Smoothing…"), 0.88f);
                aoRT = NewRT(w, h, RenderTextureFormat.ARGBHalf);
                int step = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(w, h) / 1024f));
                float sigma = Mathf.Max(spread * 0.5f, texelWorld * 2f);
                mat.SetVector("_BlurParams", new Vector4(3, step, 1f / (sigma * sigma), 0));
                Graphics.Blit(accRT, aoRT, mat, 4);
                outRT = NewRT(w, h, RenderTextureFormat.ARGB32);
                Graphics.Blit(aoRT, outRT); // to 8-bit
                var px = ReadRT(outRT);

                EditorUtility.DisplayProgressBar(Loc.T("Ambient occlusion"), Loc.T("Filling the seams and saving…"), 0.94f);
                var val = new ushort[px.Length];
                var covered = new bool[px.Length];
                float k = Mathf.Max(0f, s.strength);
                for (int i = 0; i < px.Length; i++)
                {
                    covered[i] = px[i].a > 127;
                    float a = covered[i] ? Mathf.Pow(px[i].r / 255f, k) : 1f;
                    val[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt(a * 65535f), 0, 65535);
                }
                MeshUV.Dilate(val, covered, w, h, Mathf.Max(4, Mathf.Max(w, h) / 128));
                for (int i = 0; i < px.Length; i++)
                {
                    byte b = covered[i] ? (byte)(val[i] >> 8) : (byte)255; // empty texture space: no shading
                    px[i] = new Color32(b, b, b, 255);
                }
                res.path = MapPath(mainTex, "AO", AoTag);
                res.texture = SavePng(res.path, px, w, h, AoTag, false);
                res.directions = n;
                res.seconds = EditorApplication.timeSinceStartup - t0;
                return res;
            }
            finally
            {
                RenderTexture.active = prevActive;
                Camera.SetupCurrent(prevCam);
                EditorUtility.ClearProgressBar();
                Free(recv, occ, posRT, nrmRT, accRT, aoRT, outRT);
                if (depthRT != null) Free(depthRT);
            }
        }

        static bool Usable(Renderer r) =>
            r != null && r.enabled && r.gameObject.activeInHierarchy && r.hideFlags == HideFlags.None &&
            MeshOf(r) != null;

        static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            if (!(r is MeshRenderer)) return null;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        static void DrawTextureSpace(RenderTexture rt, Mesh m, Material mat, int pass)
        {
            RenderTexture.active = rt;
            GL.Viewport(new Rect(0, 0, rt.width, rt.height));
            GL.Clear(false, true, Color.clear);
            GL.PushMatrix();
            GL.LoadOrtho();
            mat.SetPass(pass);
            Graphics.DrawMeshNow(m, Matrix4x4.identity);
            GL.PopMatrix();
        }

        /// <summary>
        /// The edited mesh laid out in texture space (vertex = UV, TEXCOORD0 = posed world position) with smooth normals.
        /// Normals are averaged only over faces that point roughly the same way, so the front and back of hair cards
        /// (which can share vertices) don't cancel out, and hard edges stay hard.
        /// </summary>
        static Mesh ReceiverMesh(MeshUV uv, Vector3[] pos)
        {
            var tris = uv.tris;
            int triCount = tris.Length / 3;
            float q = Mathf.Max(uv.PosedSize * 1e-5f, 1e-7f);
            var weld = new int[pos.Length];
            var ids = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < pos.Length; i++)
            {
                var p = pos[i];
                if (float.IsNaN(p.x)) { weld[i] = -1; continue; }
                var key = new Vector3Int(Mathf.RoundToInt(p.x / q), Mathf.RoundToInt(p.y / q), Mathf.RoundToInt(p.z / q));
                if (!ids.TryGetValue(key, out int id)) { id = ids.Count; ids[key] = id; }
                weld[i] = id;
            }
            var fn = new Vector3[triCount];
            var fu = new Vector3[triCount];
            var valid = new bool[triCount];
            var count = new int[ids.Count + 1];
            for (int t = 0; t < triCount; t++)
            {
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                if (weld[a] < 0 || weld[b] < 0 || weld[c] < 0) continue;
                fn[t] = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                float len = fn[t].magnitude;
                fu[t] = len > 1e-20f ? fn[t] / len : Vector3.up;
                valid[t] = true;
                for (int k = 0; k < 3; k++) count[weld[tris[t * 3 + k]] + 1]++;
            }
            for (int i = 1; i < count.Length; i++) count[i] += count[i - 1]; // CSR offsets
            var fill = (int[])count.Clone();
            var faces = new int[count[count.Length - 1]];
            for (int t = 0; t < triCount; t++)
                if (valid[t]) for (int k = 0; k < 3; k++) faces[fill[weld[tris[t * 3 + k]]]++] = t;

            var verts = new List<Vector3>(tris.Length);
            var world = new List<Vector3>(tris.Length);
            var normals = new List<Vector3>(tris.Length);
            var indices = new List<int>(tris.Length);
            for (int t = 0; t < triCount; t++)
            {
                if (!valid[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int i = tris[t * 3 + k], wv = weld[i];
                    var sum = Vector3.zero;
                    for (int f = count[wv]; f < count[wv + 1]; f++)
                    {
                        int o = faces[f];
                        if (Vector3.Dot(fu[o], fu[t]) > 0.5f) sum += fn[o];
                    }
                    float len = sum.magnitude;
                    indices.Add(verts.Count);
                    verts.Add(new Vector3(uv.uv[i].x, uv.uv[i].y, 0));
                    world.Add(pos[i]);
                    normals.Add(len > 1e-20f ? sum / len : fu[t]);
                }
            }
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "ATS AO receiver", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, world);
            m.SetTriangles(indices, 0, false);
            m.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0), new Vector3(1000, 1000, 1000));
            return m;
        }

        /// <summary>Every triangle of the given renderers in their current pose, in world space, as one mesh.</summary>
        static Mesh CasterMesh(List<Renderer> renderers, out int used)
        {
            var verts = new List<Vector3>();
            var indices = new List<int>();
            used = 0;
            foreach (var r in renderers)
            {
                var mesh = MeshOf(r);
                if (mesh == null) continue;
                Vector3[] w;
                try { w = MeshUV.PosedWorldVertices(r, mesh); }
                catch (Exception e) { Debug.LogWarning($"[Avistry] Skipped {r.name} as a shadow caster: {e.Message}"); continue; }
                if (w == null) continue;
                int off = verts.Count;
                verts.AddRange(w);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    foreach (int i in mesh.GetTriangles(sub)) indices.Add(i + off);
                }
                used++;
            }
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "ATS AO casters", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetTriangles(indices, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return m;
        }


        /// <summary>
        /// A tangent-space normal map (OpenGL style, green up, as Unity expects) from the brightness of <paramref name="src"/>:
        /// blur (Smoothness) then Sobel (Strength). Gradients never cross a UV island edge, so seams stay flat.
        /// <paramref name="existing"/> (0..1 encoded, may be null) is combined with Reoriented Normal Mapping.
        /// </summary>
        public static Color32[] NormalsFromColour(Color32[] src, int w, int h, ushort[] islandIds, NormalSettings s, Color32[] existing,
            Func<float, bool> progress = null)
        {
            int n = w * h;
            var hgt = new float[n];
            var msk = new float[n];
            for (int i = 0; i < n; i++)
            {
                var c = src[i];
                float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                hgt[i] = s.darkRaised ? 1f - l : l;
                msk[i] = islandIds == null || islandIds[i] > 0 ? 1f : 0f;
            }
            int radius = Mathf.RoundToInt(s.smoothness * Mathf.Max(w, h) / 160f);
            if (radius > 0)
            {
                var hm = new float[n];
                for (int i = 0; i < n; i++) hm[i] = hgt[i] * msk[i];
                var mm = (float[])msk.Clone();
                var tmp = new float[n];
                for (int it = 0; it < 2; it++) // two box passes ≈ a soft (tent) blur
                {
                    if (progress != null && progress(0.1f * it)) return null;
                    BoxBlur(hm, tmp, w, h, radius); BoxBlur(mm, tmp, w, h, radius);
                }
                for (int i = 0; i < n; i++) if (msk[i] > 0 && mm[i] > 1e-6f) hgt[i] = hm[i] / mm[i];
            }

            float k = s.strength * 0.75f * Mathf.Max(w, h) / 1024f;
            var o = new Color32[n];
            for (int y = 0; y < h; y++)
            {
                if (progress != null && (y & 63) == 0 && progress(0.2f + 0.7f * y / h)) return null; // per row band
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (msk[i] <= 0) { o[i] = existing != null ? existing[i] : new Color32(128, 128, 255, 255); continue; }
                    int id = islandIds != null ? islandIds[i] : 0;
                    float hc = hgt[i];
                    float H(int dx, int dy)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) return hc;
                        int j = ny * w + nx;
                        if (islandIds != null && islandIds[j] != id) return hc;
                        return hgt[j];
                    }
                    float gx = (H(1, -1) + 2 * H(1, 0) + H(1, 1)) - (H(-1, -1) + 2 * H(-1, 0) + H(-1, 1));
                    float gy = (H(-1, 1) + 2 * H(0, 1) + H(1, 1)) - (H(-1, -1) + 2 * H(0, -1) + H(1, -1));
                    var nrm = new Vector3(-gx * k, -gy * k, 1f).normalized;
                    if (existing != null)
                    {
                        var e = existing[i];
                        var b = new Vector3(e.r / 127.5f - 1f, e.g / 127.5f - 1f, e.b / 127.5f - 1f);
                        // Reoriented Normal Mapping (Barré-Brisebois & Hill): the new detail follows the existing surface.
                        var t = new Vector3(b.x, b.y, b.z + 1f);
                        var u = new Vector3(-nrm.x, -nrm.y, nrm.z);
                        nrm = (t * (Vector3.Dot(t, u) / Mathf.Max(t.z, 1e-4f)) - u).normalized;
                    }
                    o[i] = new Color32((byte)Mathf.RoundToInt((nrm.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nrm.y * 0.5f + 0.5f) * 255f),
                                       (byte)Mathf.RoundToInt((nrm.z * 0.5f + 0.5f) * 255f), 255);
                }
            }
            if (progress != null && progress(0.9f)) return null;
            if (islandIds != null) DilateColours(o, msk, w, h, Mathf.Max(2, Mathf.Max(w, h) / 128)); // into the seam padding
            return o;
        }

        /// <summary>Copies the nearest covered texel's colour outward (8-connected BFS), up to radius texels.</summary>
        internal static void DilateColours(Color32[] px, float[] covered, int w, int h, int radius)
        {
            var done = new bool[px.Length];
            var frontier = new List<int>();
            for (int i = 0; i < px.Length; i++) if (covered[i] > 0) { done[i] = true; frontier.Add(i); }
            var next = new List<int>();
            for (int step = 0; step < radius && frontier.Count > 0; step++)
            {
                next.Clear();
                foreach (int i in frontier)
                {
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int n = ny * w + nx;
                            if (done[n]) continue;
                            done[n] = true;
                            px[n] = px[i];
                            next.Add(n);
                        }
                }
                (frontier, next) = (next, frontier);
            }
        }

        /// <summary>Separable box blur in place (running sums, edges clamped).</summary>
        static void BoxBlur(float[] a, float[] tmp, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float sum = 0;
                for (int x = -r; x <= r; x++) sum += a[row + Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = sum * inv;
                    sum += a[row + Mathf.Min(x + r + 1, w - 1)] - a[row + Mathf.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int y = -r; y <= r; y++) sum += tmp[Mathf.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    a[y * w + x] = sum * inv;
                    sum += tmp[Mathf.Min(y + r + 1, h - 1) * w + x] - tmp[Mathf.Max(y - r, 0) * w + x];
                }
            }
        }

        /// <summary>Any normal map texture as plain 0..1-encoded normals at w×h (handles Unity's packed normal map formats).</summary>
        public static Color32[] ReadNormalMap(Texture normal, int w, int h)
        {
            var prev = RenderTexture.active;
            var rt = NewRT(w, h, RenderTextureFormat.ARGB32, FilterMode.Bilinear);
            try
            {
                Graphics.Blit(normal, rt, Mat, 5);
                return ReadRT(rt);
            }
            finally
            {
                RenderTexture.active = prev;
                Free(rt);
            }
        }

        /// <summary>Saves &lt;texture&gt;_normal.png. <paramref name="combinedWith"/> is remembered, so making it again combines with the same map.</summary>
        public static Texture2D SaveNormalMap(Texture mainTex, Color32[] px, int w, int h, Texture combinedWith, out string path)
        {
            path = MapPath(mainTex, "normal", NormalTag);
            string guid = combinedWith != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(combinedWith)) : null;
            return SavePng(path, px, w, h, string.IsNullOrEmpty(guid) ? NormalTag : NormalTag + "|" + guid, true);
        }

        /// <summary>True when this tool made the normal map; <paramref name="combinedWith"/> = the map it was combined with, if any.</summary>
        public static bool IsMadeNormal(Texture tex, out Texture2D combinedWith)
        {
            combinedWith = null;
            if (tex == null || !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter imp)) return false;
            string ud = imp.userData ?? "";
            if (!ud.StartsWith(NormalTag)) return false;
            int bar = ud.IndexOf('|');
            if (bar > 0) combinedWith = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(ud.Substring(bar + 1)));
            return true;
        }

        /// <summary>The material's normal map property, or null if its shader has none this tool knows.</summary>
        public static string NormalProperty(Material m) => m != null && m.HasProperty("_BumpMap") ? "_BumpMap" : null;

        /// <summary>Puts the normal map on the material (undoable), switching the shader's normal map feature on.</summary>
        public static void AssignNormalMap(Material m, Texture2D normal)
        {
            string prop = NormalProperty(m);
            if (prop == null) return;
            Undo.RecordObject(m, "Use normal map");
            m.SetTexture(prop, normal);
            if (m.HasProperty("_UseBumpMap")) m.SetFloat("_UseBumpMap", 1f); // lilToon's switch
            if (m.HasProperty("_BumpScale") && m.GetFloat("_BumpScale") <= 0f) m.SetFloat("_BumpScale", 1f);
            EditorUtility.SetDirty(m);
            MaterialWriter.FixPoiyomiKeywords(new[] { m });
            MaterialWriter.AfterLilToonEdit(new[] { m });
        }
    }
}
