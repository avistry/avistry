using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Avistry.Textures
{
    public static partial class MapBaker
    {
        public const string AoTag = "AvatarTextureStudio:ao";
        public const string NormalTag = "AvatarTextureStudio:normal";
        public const string AoLayerMark = "ao";
        internal const string StandardNormalKeyword = "_NORMALMAP";

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


        static string AssetKey(Texture texture)
        {
            if (texture == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long fileId))
                return null;
            return string.IsNullOrEmpty(guid) ? null : guid + ":" + fileId.ToString(CultureInfo.InvariantCulture);
        }

        static string MapField(string[] parts, string prefix)
        {
            for (int i = 1; i < parts.Length; i++)
                if (parts[i].StartsWith(prefix, StringComparison.Ordinal)) return parts[i].Substring(prefix.Length);
            return null;
        }

        public static string MapPath(Texture mainTex, string suffix, string tag) => MapPath(mainTex, suffix, tag, null);

        static string MapPath(Texture mainTex, string suffix, string tag, string targetKey)
        {
            string dir = Exporter.WritableFolder(AssetDatabase.GetAssetPath(mainTex));
            string name = $"{mainTex.name}_{suffix}";
            string numberedPrefix = name + "_";
            string source = AssetKey(mainTex);
            if (source != null && Directory.Exists(dir))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.png"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (stem != name && !(stem.StartsWith(numberedPrefix, StringComparison.Ordinal) &&
                        int.TryParse(stem.Substring(numberedPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                        continue;
                    string path = $"{dir}/{Path.GetFileName(file)}";
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                    string[] parts = (importer.userData ?? "").Split('|');
                    if (parts[0] == tag && MapField(parts, "source=") == source && MapField(parts, "target=") == targetKey) return path;
                }
            }

            for (int n = 1; ; n++)
            {
                string tail = n == 1 ? "" : "_" + n.ToString(CultureInfo.InvariantCulture);
                string path = $"{dir}/{name}{tail}.png";
                if (!File.Exists(path) && !Directory.Exists(path) && !File.Exists(path + ".meta") &&
                    AssetImporter.GetAtPath(path) == null) return path;
            }
        }

        internal static Texture2D SaveMap(Texture source, string suffix, string tag, Color32[] px, int w, int h,
            out string path, Texture combinedWith = null, string targetKey = null)
        {
            return SaveEncodedMap(source, suffix, tag, MapPixels.EncodeRgba(px, w, h), w, h, out path, combinedWith, targetKey);
        }

        internal static Texture2D SaveEncodedMap(Texture source, string suffix, string tag, byte[] png, int w, int h,
            out string path, Texture combinedWith = null, string targetKey = null)
        {
            path = MapPath(source, suffix, tag, targetKey);
            string userData = tag;
            string sourceKey = AssetKey(source);
            if (sourceKey != null) userData += "|source=" + sourceKey;
            if (targetKey != null) userData += "|target=" + targetKey;
            string baseKey = AssetKey(combinedWith);
            if (baseKey != null) userData += "|base=" + baseKey;
            bool normalMap = tag == NormalTag;
            File.WriteAllBytes(path, png);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                int maxSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(w, h)), 32, 8192);
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
            for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
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


        public static Color32[] NormalsFromColour(Color32[] src, int w, int h, ushort[] islandIds, NormalSettings s, Color32[] existing,
            Func<float, bool> progress = null) => BuildNormalPixels(src, w, h, islandIds, s, existing, progress, default);

        internal static Color32[] BuildNormalPixels(Color32[] src, int w, int h, ushort[] islandIds, NormalSettings s, Color32[] existing,
            Func<float, bool> progress, System.Threading.CancellationToken cancel)
        {
            int n = w * h;
            var hgt = new float[n];
            int radius = Mathf.RoundToInt(s.smoothness * Mathf.Max(w, h) / 160f);
            var weights = radius > 0 ? new float[n] : null;
            for (int i = 0; i < n; i++)
            {
                if ((i & 16383) == 0) cancel.ThrowIfCancellationRequested();
                if (islandIds != null && islandIds[i] == 0) continue;
                var c = src[i];
                float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                hgt[i] = s.darkRaised ? 1f - l : l;
                if (weights != null) weights[i] = 1;
            }
            if (radius > 0)
            {
                var tmp = new float[n];
                for (int it = 0; it < 2; it++)
                {
                    if (progress != null && progress(0.1f * it)) return null;
                    BoxBlur(hgt, tmp, w, h, radius, cancel);
                    BoxBlur(weights, tmp, w, h, radius, cancel);
                }
                for (int i = 0; i < n; i++)
                    if (weights[i] > 1e-6f) hgt[i] /= weights[i];
            }

            float k = s.strength * 0.75f * Mathf.Max(w, h) / 1024f;
            var o = new Color32[n];
            for (int y = 0; y < h; y++)
            {
                if (progress != null && (y & 63) == 0 && progress(0.2f + 0.7f * y / h)) return null;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (islandIds != null && islandIds[i] == 0) { o[i] = existing != null ? existing[i] : new Color32(128, 128, 255, 255); continue; }
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
                        var t = new Vector3(b.x, b.y, b.z + 1f);
                        var u = new Vector3(-nrm.x, -nrm.y, nrm.z);
                        nrm = (t * (Vector3.Dot(t, u) / Mathf.Max(t.z, 1e-4f)) - u).normalized;
                    }
                    o[i] = new Color32((byte)Mathf.RoundToInt((nrm.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nrm.y * 0.5f + 0.5f) * 255f),
                                       (byte)Mathf.RoundToInt((nrm.z * 0.5f + 0.5f) * 255f), 255);
                }
            }
            if (progress != null && progress(0.9f)) return null;
            if (islandIds != null) MapPixels.PadEdges(o, islandIds, w, h, Mathf.Max(2, Mathf.Max(w, h) / 128), false, cancel);
            return o;
        }

        static void BoxBlur(float[] a, float[] tmp, int w, int h, int r, System.Threading.CancellationToken cancel)
        {
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                if ((y & 31) == 0) cancel.ThrowIfCancellationRequested();
                int row = y * w;
                float sum = 0;
                for (int x = -r; x <= r; x++) sum += a[row + Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = sum * inv;
                    sum += a[row + Mathf.Min(x + r + 1, w - 1)] - a[row + Mathf.Max(x - r, 0)];
                }
            }
            var sums = new float[w];
            for (int y = -r; y <= r; y++)
            {
                int row = Mathf.Clamp(y, 0, h - 1) * w;
                for (int x = 0; x < w; x++) sums[x] += tmp[row + x];
            }
            for (int y = 0; y < h; y++)
            {
                if ((y & 31) == 0) cancel.ThrowIfCancellationRequested();
                int row = y * w, enter = Mathf.Min(y + r + 1, h - 1) * w, leave = Mathf.Max(y - r, 0) * w;
                for (int x = 0; x < w; x++)
                {
                    a[row + x] = sums[x] * inv;
                    sums[x] += tmp[enter + x] - tmp[leave + x];
                }
            }
        }

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

        public static Texture2D SaveNormalMap(Texture mainTex, Color32[] px, int w, int h, Texture combinedWith, out string path) =>
            SaveMap(mainTex, "normal", NormalTag, px, w, h, out path, combinedWith);

        internal static Texture2D SaveNormalMapForTarget(Texture mainTex, Color32[] px, int w, int h, Texture combinedWith, string targetKey) =>
            SaveMap(mainTex, "normal", NormalTag, px, w, h, out _, combinedWith, targetKey);

        public static bool IsMadeNormal(Texture tex, out Texture2D combinedWith)
        {
            combinedWith = null;
            if (tex == null || !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter imp)) return false;
            string[] parts = (imp.userData ?? "").Split('|');
            if (parts[0] != NormalTag) return false;
            string baseKey = MapField(parts, "base=");
            int separator = baseKey?.IndexOf(':') ?? -1;
            if (separator > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(baseKey.Substring(0, separator));
                if (!string.IsNullOrEmpty(path))
                    combinedWith = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>()
                        .FirstOrDefault(t => AssetKey(t) == baseKey);
            }
            else if (parts.Length == 2 && !parts[1].Contains("="))
                combinedWith = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(parts[1]));
            return true;
        }

        public static string NormalProperty(Material m) => m != null && m.HasProperty("_BumpMap") ? "_BumpMap" : null;

        internal static bool UsesStandardNormalKeyword(Material material) =>
            material.shader != null && (material.shader.name == "Standard" || material.shader.name == "Standard (Specular setup)");

        public static void AssignNormalMap(Material m, Texture2D normal)
        {
            string prop = NormalProperty(m);
            if (prop == null) return;
            Undo.RecordObject(m, "Use normal map");
            m.SetTexture(prop, normal);
            if (normal != null && UsesStandardNormalKeyword(m)) m.EnableKeyword(StandardNormalKeyword);
            if (m.HasProperty("_UseBumpMap")) m.SetFloat("_UseBumpMap", 1f);
            if (m.HasProperty("_BumpScale") && m.GetFloat("_BumpScale") <= 0f) m.SetFloat("_BumpScale", 1f);
            EditorUtility.SetDirty(m);
            MaterialWriter.FixPoiyomiKeywords(new[] { m });
            MaterialWriter.AfterLilToonEdit(new[] { m });
        }
    }
}
