using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    /// <summary>
    /// A material's main texture as the material colours it, before lighting: lilToon's Main Color, HSV / Gamma,
    /// adjust mask, gradation map and 2nd / 3rd layers, or Poiyomi's Color and colour adjust (hue shift, saturation,
    /// brightness, gamma). It runs each shader's own texture baker (shipped with lilToon and Poiyomi), so it matches
    /// them exactly. Other shaders: texture × _Color. Cached per material until its settings change.
    /// </summary>
    public static class MaterialLook
    {
        const string LilBaker = "Hidden/ltsother_baker";
        const string PoiBaker = "Hidden/Poi/ColorAdjustBaker";

        struct Entry { public uint crc; public int size; public Color[] px; }
        static readonly Dictionary<int, Entry> s_cache = new Dictionary<int, Entry>();

        /// <summary>The main texture as the material shows it, size×size gamma colours (null without a main texture).</summary>
        public static Color[] Pixels(Material m, int size)
        {
            if (m == null || !m.HasProperty("_MainTex")) return null;
            var tex = m.GetTexture("_MainTex");
            if (tex == null) return null;
            uint crc = unchecked((uint)m.ComputeCRC() ^ (uint)tex.GetInstanceID() ^ (uint)tex.imageContentsHash.GetHashCode());
            if (s_cache.TryGetValue(m.GetInstanceID(), out var e) && e.crc == crc && e.size == size) return e.px;
            var px = Bake(m, tex, size) ?? Recolour.SampleTexture(tex, size, Tint(m));
            s_cache[m.GetInstanceID()] = new Entry { crc = crc, size = size, px = px };
            return px;
        }

        /// <summary>The colour at a mesh UV as the material shows it (its main texture tiling applied). Gamma.</summary>
        public static Color Texel(Material m, Vector2 meshUV, int size = 256)
        {
            var px = Pixels(m, size);
            if (px == null || px.Length < size * size) return Tint(m);
            var s = m.GetTextureScale("_MainTex");
            var o = m.GetTextureOffset("_MainTex");
            float u = Mathf.Repeat(meshUV.x * s.x + o.x, 1f), v = Mathf.Repeat(meshUV.y * s.y + o.y, 1f);
            int x = Mathf.Clamp(Mathf.FloorToInt(u * size), 0, size - 1), y = Mathf.Clamp(Mathf.FloorToInt(v * size), 0, size - 1);
            var c = px[y * size + x];
            c.a = 1f;
            return c;
        }

        /// <summary>What the material does to its texture's colours beyond a white tint, in words (null if nothing).</summary>
        public static string Describe(Material m)
        {
            if (m == null) return null;
            var parts = new List<string>();
            var tint = Tint(m);
            if (Mathf.Abs(tint.r - 1) > 0.01f || Mathf.Abs(tint.g - 1) > 0.01f || Mathf.Abs(tint.b - 1) > 0.01f) parts.Add(Loc.T("its colour tint"));
            switch (ShaderProfiles.Detect(m.shader))
            {
                case ShaderFamily.LilToon:
                    if (m.HasProperty("_MainTexHSVG") && m.GetVector("_MainTexHSVG") != new Vector4(0, 1, 1, 1)) parts.Add(Loc.T("HSV / Gamma"));
                    if (m.HasProperty("_MainGradationStrength") && m.GetFloat("_MainGradationStrength") > 0 && m.GetTexture("_MainGradationTex") != null) parts.Add(Loc.T("gradation map"));
                    if (m.HasProperty("_UseMain2ndTex") && m.GetFloat("_UseMain2ndTex") > 0.5f) parts.Add(Loc.T("2nd layer"));
                    if (m.HasProperty("_UseMain3rdTex") && m.GetFloat("_UseMain3rdTex") > 0.5f) parts.Add(Loc.T("3rd layer"));
                    break;
                case ShaderFamily.Poiyomi:
                    if (m.HasProperty("_MainColorAdjustToggle") && m.GetFloat("_MainColorAdjustToggle") > 0.5f) parts.Add(Loc.T("colour adjust (hue shift, saturation, brightness)"));
                    break;
            }
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        public static void Forget(Material m) { if (m != null) s_cache.Remove(m.GetInstanceID()); }

        static Color Tint(Material m)
        {
            var c = m != null && m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            c.a = 1f;
            return c;
        }

        /// <summary>Runs the shader's own baker over the main texture. Null when there is none for this shader.</summary>
        static Color[] Bake(Material m, Texture tex, int size)
        {
            string bakerName = null;
            switch (ShaderProfiles.Detect(m.shader))
            {
                case ShaderFamily.LilToon: bakerName = LilBaker; break;
                case ShaderFamily.Poiyomi:
                    if (m.HasProperty("_MainColorAdjustToggle") && m.GetFloat("_MainColorAdjustToggle") > 0.5f) bakerName = PoiBaker;
                    break;
            }
            var shader = bakerName != null ? Shader.Find(bakerName) : null;
            if (shader == null || !shader.isSupported) return null;

            var baker = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Texture2D read = null;
            try
            {
                CopyMatchingProperties(m, baker);
                baker.SetTexture("_MainTex", tex);
                baker.SetTextureScale("_MainTex", Vector2.one);  // bake the whole texture; tiling is applied when sampling
                baker.SetTextureOffset("_MainTex", Vector2.zero);
                Graphics.Blit(tex, rt, baker);
                RenderTexture.active = rt;
                read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                read.Apply(false);
                return read.GetPixels();
            }
            catch { return null; }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) Object.DestroyImmediate(read);
                Object.DestroyImmediate(baker);
            }
        }

        /// <summary>Every property the baker has that the material also has, by name (values, textures and tiling).</summary>
        static void CopyMatchingProperties(Material from, Material to)
        {
            var sh = to.shader;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string p = sh.GetPropertyName(i);
                if (!from.HasProperty(p)) continue;
                switch (sh.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: to.SetColor(p, from.GetColor(p)); break;
                    case ShaderPropertyType.Vector: to.SetVector(p, from.GetVector(p)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: to.SetFloat(p, from.GetFloat(p)); break;
                    case ShaderPropertyType.Int: to.SetInteger(p, from.GetInteger(p)); break;
                    case ShaderPropertyType.Texture:
                        to.SetTexture(p, from.GetTexture(p));
                        to.SetTextureScale(p, from.GetTextureScale(p));
                        to.SetTextureOffset(p, from.GetTextureOffset(p));
                        break;
                }
            }
        }
    }
}
