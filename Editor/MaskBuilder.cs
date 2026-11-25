using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public static class MaskBuilder
    {
        public const string Tag = "AvatarTextureStudio:mask";

        public class EmptySlot
        {
            public string property;
            public string displayName;
            public string channelInfo;
            public string featureToggle;
            public bool featureOff;
            public bool known;
            public bool startsWhite;
            public int uvChannel;
        }

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
                if (family == ShaderFamily.Poiyomi && (name.Contains("Set_") || name == "_MetallicGlossMap" || name == "_SmoothnessTex")) continue;
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

        public static Texture2D Create(Material mat, EmptySlot slot, Texture reference, MeshUV uv, ICollection<int> onlyIslands)
            => Create(mat, slot, reference, uv, onlyIslands, null, null, out _);

        internal static Texture2D Create(Material mat, EmptySlot slot, Texture reference, MeshUV uv, ICollection<int> onlyIslands,
            System.Func<bool> canAssign, System.Action beginAssignment, out bool assigned)
        {
            assigned = false;
            if (mat == null || slot == null) return null;
            var size = Dimensions(reference);
            var png = EncodeMask(size.x, size.y, slot.startsWhite, uv, onlyIslands, default);
            return ImportAndAssign(mat, slot, reference, size, png, canAssign, beginAssignment, out assigned);
        }

        internal static Vector2Int Dimensions(Texture reference)
        {
            int rw = reference != null ? reference.width : 1024, rh = reference != null ? reference.height : 1024;
            int w = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Min(2048, rw)), 256, 2048);
            int h = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.RoundToInt(w * (rh / (float)rw)))), 256, 2048);
            return new Vector2Int(w, h);
        }

        internal static byte[] EncodeMask(int w, int h, bool startsWhite, MeshUV uv, ICollection<int> onlyIslands,
            System.Threading.CancellationToken cancel)
        {
            var px = new Color32[w * h];
            if (onlyIslands != null && onlyIslands.Count > 0 && uv != null)
            {
                var bytes = uv.RasterIslandMaskWithCancellation(w, h, new HashSet<int>(onlyIslands), Mathf.Max(2, w / 256), cancel);
                for (int i = 0; i < px.Length; i++)
                {
                    if ((i & 16383) == 0) cancel.ThrowIfCancellationRequested();
                    px[i] = new Color32(bytes[i], bytes[i], bytes[i], 255);
                }
            }
            else
            {
                byte v = startsWhite ? (byte)255 : (byte)0;
                var c = new Color32(v, v, v, 255);
                System.Array.Fill(px, c);
            }
            cancel.ThrowIfCancellationRequested();
            return MapPixels.EncodeRgba(px, w, h);
        }

        internal static Texture2D ImportAndAssign(Material mat, EmptySlot slot, Texture reference, Vector2Int size, byte[] png,
            System.Func<bool> canAssign, System.Action beginAssignment, out bool assigned)
        {
            assigned = false;
            string refPath = reference != null ? AssetDatabase.GetAssetPath(reference) : AssetDatabase.GetAssetPath(mat);
            string dir = Exporter.WritableFolder(refPath);
            string baseName = reference != null ? reference.name : mat.name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}_{slot.property.TrimStart('_')}.png");

            File.WriteAllBytes(path, png);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.userData = Tag;
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = Mathf.Max(size.x, size.y);
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            var created = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (created == null) return null;
            if (mat == null || !mat.HasProperty(slot.property) || mat.GetTexture(slot.property) != null ||
                (canAssign != null && !canAssign())) return created;

            beginAssignment?.Invoke();
            Undo.RecordObject(mat, "Add mask texture");
            MaterialWriter.EnsureEditable(new[] { mat });
            mat.SetTexture(slot.property, created);
            EditorUtility.SetDirty(mat);
            switch (ShaderProfiles.Detect(mat.shader))
            {
                case ShaderFamily.Poiyomi: MaterialWriter.FixPoiyomiKeywords(new[] { mat }); break;
                case ShaderFamily.LilToon: MaterialWriter.AfterLilToonEdit(new[] { mat }); break;
            }
            assigned = true;
            return created;
        }
    }
}
