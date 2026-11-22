using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public enum TextureRole { Color, Mask, Normal }

    /// <summary>One distinct texture on a material, plus every property that references it.</summary>
    public class TextureSlot
    {
        public Texture2D texture;
        public List<string> properties = new List<string>();
        public string displayName;
        public TextureRole role;
        public int uvChannel;
        public string assetPath;
        public bool importedAsSRGB;

        // what the shader profiles know about it
        public ShaderFamily family;
        public bool mixedRoles;          // used as colour by one property and as data by another
        public string channelInfo;       // e.g. "R metallic · G occlusion · A smoothness"
        public string featureToggle;     // property that switches the feature using this texture
        public bool featureOff;          // that feature is switched off: the shader ignores this texture
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
        /// <summary>Collects the distinct 2D textures assigned on a material, with roles read from the shader.</summary>
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
                if (ShaderProfiles.IsLookupTable(shader, i, name)) continue; // DFG/LUT data, not an image to edit

                string path = AssetDatabase.GetAssetPath(tex);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var role = ClassifyRole(shader, i, name, importer, family);

                var existing = result.FirstOrDefault(s => s.texture == tex);
                if (existing != null)
                {
                    existing.properties.Add(name);
                    if (existing.role != role) existing.mixedRoles = true;
                    // A texture is "off" only if every feature using it is off.
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

            // Main texture first, then colour textures, then masks, then normals.
            var main = mat.mainTexture;
            return result
                .OrderBy(s => s.texture == main ? 0 : 1)
                .ThenBy(s => (int)s.role)
                .ToList();
        }

        /// <summary>Poiyomi/Thry descriptions look like "Texture--{reference_properties:[...]}"; keep the readable part.</summary>
        static string CleanDescription(string desc, string fallback)
        {
            if (string.IsNullOrEmpty(desc)) return fallback;
            int cut = desc.IndexOf("--", System.StringComparison.Ordinal);
            if (cut >= 0) desc = desc.Substring(0, cut);
            desc = System.Text.RegularExpressions.Regex.Replace(desc, "<[^>]+>", "").Trim(); // strip rich-text tags
            return string.IsNullOrEmpty(desc) ? fallback : (fallback == null ? desc : $"{desc} [{fallback}]");
        }

        /// <summary>The role of a texture property from the shader alone (for slots without a texture yet).</summary>
        public static TextureRole RoleOf(Shader shader, int index, string name, ShaderFamily family) => ClassifyRole(shader, index, name, null, family);

        static TextureRole ClassifyRole(Shader shader, int index, string name, TextureImporter importer, ShaderFamily family)
        {
            if ((shader.GetPropertyFlags(index) & ShaderPropertyFlags.Normal) != 0) return TextureRole.Normal;
            if (family == ShaderFamily.LilToon && ShaderProfiles.LilRole(name) is TextureRole lil) return lil;
            if (importer != null && importer.textureType == TextureImporterType.NormalMap) return TextureRole.Normal;

            string lower = name.ToLowerInvariant();
            if (lower.Contains("bump") || lower.Contains("normal")) return TextureRole.Normal;

            // Poiyomi/Thry: [sRGBWarning(true)] = colour texture, [sRGBWarning] = linear data (mask).
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

        /// <summary>Poiyomi stores the UV set per texture as a float named "&lt;prop&gt;UV" (0..3 = UV0..UV3).</summary>
        static int ReadUvChannel(Material mat, string prop)
        {
            string uvProp = prop + "UV";
            if (!mat.HasProperty(uvProp)) return 0;
            int ch = Mathf.RoundToInt(mat.GetFloat(uvProp));
            return ch >= 0 && ch <= 3 ? ch : 0;
        }

        /// <summary>The avatar root: the topmost transform above the renderer.</summary>
        public static Transform AvatarRoot(Renderer r) => r == null ? null : (MaterialAudit.FindAvatarRoot(r) ?? r.transform.root);

        public struct SlotRef
        {
            public Renderer renderer;
            public int materialIndex;
        }

        /// <summary>Every renderer/material-slot under the avatar that uses this material.</summary>
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

        /// <summary>A material slot of another material that shows the same texture (a mesh that can be edited along).</summary>
        public class TextureUser
        {
            public SlotRef slot;
            public Material material;
            public List<string> properties = new List<string>(); // this material's properties holding the texture
        }

        /// <summary>
        /// Every renderer slot under the avatar whose material is NOT <paramref name="self"/> but uses <paramref name="tex"/>
        /// (several hair pieces with their own materials sharing one texture, for example). Hierarchy order.
        /// </summary>
        public static List<TextureUser> FindTextureUsers(Transform root, Material self, Texture tex)
        {
            var list = new List<TextureUser>();
            if (root == null || tex == null) return list;
            var propsOf = new Dictionary<Material, List<string>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m == self || m.shader == null) continue;
                    if (!propsOf.TryGetValue(m, out var props))
                    {
                        props = new List<string>();
                        var s = m.shader;
                        for (int p = 0; p < s.GetPropertyCount(); p++)
                        {
                            if (s.GetPropertyType(p) != ShaderPropertyType.Texture) continue;
                            string name = s.GetPropertyName(p);
                            if (m.GetTexture(name) == tex) props.Add(name);
                        }
                        propsOf[m] = props;
                    }
                    if (props.Count > 0) list.Add(new TextureUser { slot = new SlotRef { renderer = r, materialIndex = i }, material = m, properties = props });
                }
            }
            return list;
        }

        /// <summary>Other materials under the avatar that use the same texture (they will NOT see the edit).</summary>
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
