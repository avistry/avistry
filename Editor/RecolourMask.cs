using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Partial recolour: the island selection becomes the shader's own colour-adjust mask, so only those parts change
    /// colour, with no new main texture. lilToon reads R of _MainColorAdjustMask (black = unchanged);
    /// Poiyomi reads _MainColorAdjustTexture as R hue · G brightness · B saturation · A gamma masks.
    /// </summary>
    public static class RecolourMask
    {
        const string Tag = "AvatarTextureStudio:recolourMask";

        public static string MaskProperty(Material m)
        {
            if (m == null) return null;
            switch (ShaderProfiles.Detect(m.shader))
            {
                case ShaderFamily.Poiyomi: return m.HasProperty("_MainColorAdjustTexture") ? "_MainColorAdjustTexture" : null;
                case ShaderFamily.LilToon: return m.HasProperty("_MainColorAdjustMask") ? "_MainColorAdjustMask" : null;
                default: return null;
            }
        }

        /// <summary>
        /// Rasterises the islands (white, dilated into the padding) and saves them as a data texture next to the main
        /// texture. Re-uses (overwrites) the mask this tool made before for the same texture; never overwrites anything else.
        /// </summary>
        public static Texture2D Write(Texture mainTex, MeshUV uv, ICollection<int> islands)
        {
            if (mainTex == null || uv == null || islands == null || islands.Count == 0) return null;
            int w = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Min(1024, mainTex.width)), 64, 1024);
            int h = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.RoundToInt(w * (mainTex.height / (float)mainTex.width)))), 64, 1024);

            string mainPath = AssetDatabase.GetAssetPath(mainTex);
            string dir = Exporter.WritableFolder(mainPath);
            string path = $"{dir}/{mainTex.name}_recolour_mask.png";
            var existing = AssetImporter.GetAtPath(path) as TextureImporter;
            if (existing != null && existing.userData != Tag) path = AssetDatabase.GenerateUniqueAssetPath(path);

            var bytes = uv.RasterIslandMask(w, h, new HashSet<int>(islands), Mathf.Max(2, w / 256));
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(bytes[i], bytes[i], bytes[i], bytes[i]); // all four masks: hue, brightness, saturation, gamma
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply(false);
            try
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                tex = null;

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) { Debug.LogError($"Avistry: couldn't import the recolour mask at {path}."); return null; }
                imp.userData = Tag;
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;                       // data texture
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = Mathf.Max(w, h);
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            catch (System.Exception ex)
            {
                if (tex != null) Object.DestroyImmediate(tex);
                Debug.LogError($"Avistry: couldn't write the recolour mask at {path}: {ex.Message}");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Selection signature: regenerate the mask only when the islands (or texture) change.</summary>
        public static string Key(Texture mainTex, ICollection<int> islands)
        {
            var list = new List<int>(islands);
            list.Sort();
            return (mainTex != null ? mainTex.GetInstanceID().ToString() : "-") + ":" + string.Join(",", list);
        }
    }
}
