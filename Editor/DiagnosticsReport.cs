using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class DiagnosticsReport
    {
        // Harness switch: no preview window and no clipboard write.
        public static bool SkipPreview;

        public static string Build(Renderer renderer, Material material, IReadOnlyList<TextureSlot> slots, string shaderProfile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Avistry: " + AvatarTextureWindow.Version);
            sb.AppendLine("Unity: " + Application.unityVersion);
            sb.AppendLine("OS: " + SystemInfo.operatingSystem);
            sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName);
            sb.AppendLine("Graphics API: " + SystemInfo.graphicsDeviceType);
            sb.AppendLine("Graphics API version: " + SystemInfo.graphicsDeviceVersion);
            sb.AppendLine("Compute shaders: " + SystemInfo.supportsComputeShaders);
            sb.AppendLine("Async GPU readback: " + SystemInfo.supportsAsyncGPUReadback);
            sb.AppendLine("RenderTexture ARGBHalf: " + SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf));
            sb.AppendLine("RenderTexture ARGBFloat: " + SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat));
            sb.AppendLine("Texture RGBAHalf: " + SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf));
            sb.AppendLine("Texture RGBAFloat: " + SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat));
            sb.AppendLine("Max texture size: " + SystemInfo.maxTextureSize);
            sb.AppendLine("Shader profile: " + shaderProfile);
            sb.AppendLine("Part: " + MeshName(renderer));
            sb.AppendLine("Material: " + (material != null ? material.name : "none"));
            sb.AppendLine("Shader: " + (material != null && material.shader != null ? material.shader.name : "none"));
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var t = slots[i]?.texture;
                    if (t == null) { sb.AppendLine($"Slot {i + 1}: empty"); continue; }
                    string file = Path.GetFileName(AssetDatabase.GetAssetPath(t));
                    if (string.IsNullOrEmpty(file)) file = "(not an asset)";
                    sb.AppendLine($"Slot {i + 1}: {t.width} x {t.height}, {t.format}, sRGB {t.isDataSRGB}, {file}");
                }
            }
            return Scrub(sb.ToString());
        }

        static string MeshName(Renderer r)
        {
            Mesh m = null;
            if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
            else if (r != null && r.TryGetComponent(out MeshFilter mf)) m = mf.sharedMesh;
            return m != null ? m.name : "none";
        }

        public static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string data = Application.dataPath;
            if (!string.IsNullOrEmpty(data))
            {
                s = ReplaceAll(s, data);
                string parent = Path.GetDirectoryName(data);
                if (!string.IsNullOrEmpty(parent)) s = ReplaceAll(s, parent);
            }
            string user = Environment.UserName;
            if (!string.IsNullOrEmpty(user) && user.Length >= 3) s = s.Replace(user, "<path>");
            return Regex.Replace(s, @"(?:[A-Za-z]:[\\/]|/Users/|/home/)\S*", "<path>");
        }

        // Also catches the other slash style of the same path.
        static string ReplaceAll(string s, string path)
        {
            s = s.Replace(path, "<path>");
            s = s.Replace(path.Replace("/", "\\"),"<path>");
            return s.Replace(path.Replace("\\", "/"), "<path>");
        }
    }
}
