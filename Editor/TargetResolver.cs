using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public enum TextureRole { Color, Mask, Normal }

    public class TextureSlot
    {
        public Texture2D texture;
        public List<string> properties = new List<string>();
        public string displayName;
        public TextureRole role;
        public int uvChannel;
        public string assetPath;
        public bool importedAsSRGB;

        public ShaderFamily family;
        public bool mixedRoles;
        public string channelInfo;
        public string featureToggle;
        public bool featureOff;
        public List<string> uvProblems = new List<string>();

        public string Label => $"{displayName}  ({texture.name}, {RoleLabel(role)})";

        public static string RoleLabel(TextureRole r) => r switch
        {
            TextureRole.Color => Loc.T("colour"),
            TextureRole.Mask => Loc.T("mask"),
            _ => Loc.T("normal map"),
        };
    }

    public static class TargetResolver
    {
        public static List<TextureSlot> GetTextureSlots(Material mat)
        {
            var result = new List<TextureSlot>();
            if (mat == null || mat.shader == null) return result;
            var shader = mat.shader;
            var family = ShaderProfiles.Detect(shader);
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                if (shader.GetPropertyTextureDimension(i) != TextureDimension.Tex2D) continue;
                string name = shader.GetPropertyName(i);
                var tex = mat.GetTexture(name) as Texture2D;
                if (tex == null) continue;
                if (ShaderProfiles.IsLookupTable(shader, i, name)) continue;

                string path = AssetDatabase.GetAssetPath(tex);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var role = ClassifyRole(shader, i, name, importer, family);

                var existing = result.FirstOrDefault(s => s.texture == tex);
                if (existing != null)
                {
                    existing.properties.Add(name);
                    if (existing.role != role) existing.mixedRoles = true;
                    if (existing.featureOff && !ShaderProfiles.FeatureIsOff(family, mat, name)) existing.featureOff = false;
                    continue;
                }

                var slot = new TextureSlot
                {
                    texture = tex,
                    displayName = ShaderProfiles.FriendlyName(name, CleanDescription(shader.GetPropertyDescription(i), null)),
                    role = role,
                    uvChannel = ReadUvChannel(mat, name),
                    assetPath = path,
                    importedAsSRGB = importer == null || importer.sRGBTexture,
                    family = family,
                    channelInfo = ShaderProfiles.ChannelInfo(family, mat, name),
                    featureToggle = ShaderProfiles.FeatureToggle(family, name),
                    featureOff = ShaderProfiles.FeatureIsOff(family, mat, name),
                    uvProblems = ShaderProfiles.UvProblems(family, mat, name),
                };
                slot.properties.Add(name);
                result.Add(slot);
            }

            var main = mat.mainTexture;
            return result
                .OrderBy(s => s.texture == main ? 0 : 1)
                .ThenBy(s => (int)s.role)
                .ToList();
        }

        static string CleanDescription(string desc, string fallback)
        {
            if (string.IsNullOrEmpty(desc)) return fallback;
            int cut = desc.IndexOf("--", System.StringComparison.Ordinal);
            if (cut >= 0) desc = desc.Substring(0, cut);
            desc = System.Text.RegularExpressions.Regex.Replace(desc, "<[^>]+>", "").Trim();
            return string.IsNullOrEmpty(desc) ? fallback : (fallback == null ? desc : $"{desc} [{fallback}]");
        }

        public static TextureRole RoleOf(Shader shader, int index, string name, ShaderFamily family) => ClassifyRole(shader, index, name, null, family);

        static TextureRole ClassifyRole(Shader shader, int index, string name, TextureImporter importer, ShaderFamily family)
        {
            if ((shader.GetPropertyFlags(index) & ShaderPropertyFlags.Normal) != 0) return TextureRole.Normal;
            if (family == ShaderFamily.LilToon && ShaderProfiles.LilRole(name) is TextureRole lil) return lil;
            if (importer != null && importer.textureType == TextureImporterType.NormalMap) return TextureRole.Normal;

            string lower = name.ToLowerInvariant();
            if (lower.Contains("bump") || lower.Contains("normal")) return TextureRole.Normal;

            foreach (var raw in shader.GetPropertyAttributes(index))
            {
                string a = raw.Trim('[', ']', ' ');
                if (a.StartsWith("sRGBWarning", System.StringComparison.OrdinalIgnoreCase))
                    return a.IndexOf("true", System.StringComparison.OrdinalIgnoreCase) >= 0 ? TextureRole.Color : TextureRole.Mask;
            }

            if (lower.Contains("mask") || lower.Contains("alpha") || lower.EndsWith("ao") || lower.Contains("aomap")
                || lower.Contains("occlusion") || lower.Contains("smoothness") || lower.Contains("metallic") || lower.Contains("strength")
                || lower.Contains("border") || lower.Contains("blur") || lower.Contains("curve"))
                return TextureRole.Mask;

            return TextureRole.Color;
        }

        internal static int ReadUvChannel(Material mat, string prop)
        {
            string uvProp = prop + "UV";
            if (!mat.HasProperty(uvProp)) return 0;
            int ch = Mathf.RoundToInt(mat.GetFloat(uvProp));
            return ch >= 0 && ch <= 3 ? ch : 0;
        }

        internal static int? CommonUvChannel(Material mat, IReadOnlyList<string> properties)
        {
            if (properties.Count == 0) return null;
            int channel = ReadUvChannel(mat, properties[0]);
            for (int i = 1; i < properties.Count; i++)
                if (ReadUvChannel(mat, properties[i]) != channel) return null;
            return channel;
        }

        public static Transform AvatarRoot(Renderer r) => r == null ? null : (MaterialAudit.FindAvatarRoot(r) ?? r.transform.root);

        public struct SlotRef
        {
            public Renderer renderer;
            public int materialIndex;
            internal int? uvChannel;
        }

        public static List<SlotRef> FindMaterialUsers(Transform root, Material mat)
        {
            var list = new List<SlotRef>();
            if (root == null || mat == null) return list;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == mat) list.Add(new SlotRef { renderer = r, materialIndex = i });
            }
            return list;
        }

        public class TextureUser
        {
            public SlotRef slot;
            public Material material;
            public List<string> properties = new List<string>();
        }

        internal static List<string> TextureProperties(Material material, Texture texture)
        {
            var properties = new List<string>();
            var shader = material != null ? material.shader : null;
            if (shader == null || texture == null) return properties;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                string property = shader.GetPropertyName(i);
                if (material.GetTexture(property) == texture) properties.Add(property);
            }
            return properties;
        }

        public static List<TextureUser> FindTextureUsers(Transform root, Material self, Texture tex)
        {
            var list = new List<TextureUser>();
            if (root == null || tex == null) return list;
            var matches = new Dictionary<Material, (List<string> properties, int? uvChannel)>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m == self || m.shader == null) continue;
                    if (!matches.TryGetValue(m, out var match))
                    {
                        var props = TextureProperties(m, tex);
                        match = (props, CommonUvChannel(m, props));
                        matches[m] = match;
                    }
                    if (match.properties.Count > 0)
                        list.Add(new TextureUser
                        {
                            slot = new SlotRef { renderer = r, materialIndex = i, uvChannel = match.uvChannel },
                            material = m, properties = match.properties,
                        });
                }
            }
            return list;
        }

        public static List<Material> FindOtherMaterialsUsingTexture(Transform root, Material self, Texture tex)
        {
            var set = new HashSet<Material>();
            if (root == null || tex == null) return set.ToList();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m == self || set.Contains(m) || m.shader == null) continue;
                    var s = m.shader;
                    for (int i = 0; i < s.GetPropertyCount(); i++)
                    {
                        if (s.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                        if (m.GetTexture(s.GetPropertyName(i)) == tex) { set.Add(m); break; }
                    }
                }
            }
            return set.ToList();
        }

        public static Mesh GetSharedMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r != null ? r.GetComponent<MeshFilter>() : null;
            return mf != null ? mf.sharedMesh : null;
        }

        public static bool IsLockedShader(Material mat) =>
            mat != null && mat.shader != null && mat.shader.name.StartsWith("Hidden/Locked/");
    }
}
