using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class DiagnosticsReport
    {
        public static bool SkipPreview;
        static Texture2D s_logo;

        internal static Texture2D Logo => s_logo ? s_logo : s_logo = AssetDatabase.LoadAssetAtPath<Texture2D>(
            AssetDatabase.GUIDToAssetPath("081f83aa59264406861d8a2c505ccb6a"));

        public static string Build(Renderer renderer, Material material, IReadOnlyList<TextureSlot> slots)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Avistry: " + AvatarTextureWindow.Version);
            sb.AppendLine("Unity: " + Application.unityVersion);
            string issue = SupportSession.LastIssue;
            if (!string.IsNullOrEmpty(issue)) sb.AppendLine("Last event: " + issue);
            sb.AppendLine("Operating system family: " + SystemInfo.operatingSystemFamily);
            sb.AppendLine($"Graphics: {SystemInfo.graphicsDeviceType}, vendor {SystemInfo.graphicsDeviceVendorID:X4}, device {SystemInfo.graphicsDeviceID:X4}");
            sb.AppendLine("Compute shaders: " + SystemInfo.supportsComputeShaders);
            sb.AppendLine("Async GPU readback: " + SystemInfo.supportsAsyncGPUReadback);
            sb.AppendLine("RenderTexture ARGBHalf: " + SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf));
            sb.AppendLine("RenderTexture ARGBFloat: " + SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat));
            sb.AppendLine("Texture RGBAHalf: " + SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf));
            sb.AppendLine("Texture RGBAFloat: " + SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat));
            sb.AppendLine("Max texture size: " + SystemInfo.maxTextureSize);
            sb.AppendLine("Renderer: " + (renderer is SkinnedMeshRenderer ? "Skinned mesh" : renderer is MeshRenderer ? "Mesh" : "none"));
            var shader = material ? material.shader : null;
            sb.AppendLine("Shader profile: " + (!shader ? "none" : shader.name == "Standard" ? "Standard" : ShaderProfiles.Detect(shader).ToString()));
            for (int i = 0; slots != null && i < slots.Count; i++)
            {
                var t = slots[i]?.texture;
                sb.AppendLine(t ? $"Slot {i + 1}: {t.width} x {t.height}, {t.format}, sRGB {t.isDataSRGB}" : $"Slot {i + 1}: empty");
            }
            return sb.ToString();
        }
    }
}
