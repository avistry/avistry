using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    /// <summary>
    /// Mask builder: finds the mask slots a material's shader offers that have no texture yet (emission mask, rim mask,
    /// matcap mask…), and creates a correctly set-up mask texture for one so it can be painted in the tool.
    /// </summary>
    public static class MaskBuilder
    {
        public const string Tag = "AvatarTextureStudio:mask";

        public class EmptySlot
        {
            public string property;
            public string displayName;
            public string channelInfo;     // e.g. "R matcap · G rim · B emission"
            public string featureToggle;   // the property that switches the feature on
            public bool featureOff;        // the feature is off, so the mask won't show until it's switched on
            public bool known;             // a mask this tool has a plain name for
            public bool startsWhite;       // the shader treats "no texture" as white (effect everywhere)
            public int uvChannel;
        }

        /// <summary>Mask slots without a texture. Masks of features that are switched on come first.</summary>
        public static List<EmptySlot> EmptySlots(Material mat)
        {
            var result = new List<EmptySlot>();
            if (mat == null || mat.shader == null) return result;
            var shader = MaterialWriter.SourceShader(mat.shader) ?? mat.shader;
            var family = ShaderProfiles.Detect(shader);
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                if (shader.GetPropertyTextureDimension(i) != TextureDimension.Tex2D) continue;
                if ((shader.GetPropertyFlags(i) & (ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData)) != 0) continue;
                string name = shader.GetPropertyName(i);
                if (!mat.HasProperty(name) || mat.GetTexture(name) != null) continue;
                if (family == ShaderFamily.Poiyomi && (name.Contains("Set_") || name == "_MetallicGlossMap" || name == "_SmoothnessTex")) continue; // fallback-shader leftovers
                if (ShaderProfiles.IsLookupTable(shader, i, name)) continue;
                if (TargetResolver.RoleOf(shader, i, name, family) != TextureRole.Mask) continue;
                bool known = ShaderProfiles.HasFriendlyName(name);
                if (!known && name.IndexOf("mask", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                string label = shader.GetPropertyDescription(i);
                int cut = label != null ? label.IndexOf("--", System.StringComparison.Ordinal) : -1;
                if (cut >= 0) label = label.Substring(0, cut);
                result.Add(new EmptySlot
                {
                    property = name,
                    displayName = ShaderProfiles.FriendlyName(name, label),
                    channelInfo = ShaderProfiles.ChannelInfo(family, mat, name),
                    featureToggle = ShaderProfiles.FeatureToggle(family, name),
                    featureOff = ShaderProfiles.FeatureIsOff(family, mat, name),
                    known = known,
                    startsWhite = shader.GetPropertyTextureDefaultName(i) != "black",
                    uvChannel = mat.HasProperty(name + "UV") ? Mathf.Clamp(Mathf.RoundToInt(mat.GetFloat(name + "UV")), 0, 3) : 0,
                });
            }
            return result
                .OrderBy(s => s.featureOff ? 1 : 0)
                .ThenBy(s => s.known ? 0 : 1)
                .ThenBy(s => s.displayName)
                .ToList();
        }

        /// <summary>
        /// Creates the mask PNG next to the reference texture and assigns it to the material. Filled with the shader's
        /// "no texture" value (so nothing looks different yet), or white on the given islands and black elsewhere.
        /// </summary>
        public static Texture2D Create(Material mat, EmptySlot slot, Texture reference, MeshUV uv, ICollection<int> onlyIslands)
        {
            if (mat == null || slot == null) return null;
            int rw = reference != null ? reference.width : 1024, rh = reference != null ? reference.height : 1024;
            int w = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Min(2048, rw)), 256, 2048);
            int h = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.RoundToInt(w * (rh / (float)rw)))), 256, 2048);

            string refPath = reference != null ? AssetDatabase.GetAssetPath(reference) : AssetDatabase.GetAssetPath(mat);
            string dir = Exporter.WritableFolder(refPath);
            string baseName = reference != null ? reference.name : mat.name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}_{slot.property.TrimStart('_')}.png");

            var px = new Color32[w * h];
            if (onlyIslands != null && onlyIslands.Count > 0 && uv != null)
            {
                var bytes = uv.RasterIslandMask(w, h, new HashSet<int>(onlyIslands), Mathf.Max(2, w / 256));
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(bytes[i], bytes[i], bytes[i], 255);
            }
            else
            {
                byte v = slot.startsWhite ? (byte)255 : (byte)0;
                var c = new Color32(v, v, v, 255);
                for (int i = 0; i < px.Length; i++) px[i] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.userData = Tag;
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;                       // masks are data, not colour
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = Mathf.Max(w, h);
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            var created = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (created == null) return null;

            MaterialWriter.EnsureEditable(new[] { mat });
            Undo.RecordObject(mat, "Add mask texture");
            mat.SetTexture(slot.property, created);
            EditorUtility.SetDirty(mat);
            switch (ShaderProfiles.Detect(mat.shader))
            {
                case ShaderFamily.Poiyomi: MaterialWriter.FixPoiyomiKeywords(new[] { mat }); break;
                case ShaderFamily.LilToon: MaterialWriter.AfterLilToonEdit(new[] { mat }); break;
            }
            return created;
        }
    }
}
