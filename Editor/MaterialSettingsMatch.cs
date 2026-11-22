using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    /// <summary>
    /// "Also match material settings": copies a material's colour and lighting settings (shadow colours and borders,
    /// lighting mode and limits, optionally rim / shine and outline colour) onto another material with the same shader
    /// family. Textures, masks, UV settings and the rendering mode are never touched.
    /// </summary>
    public static class MaterialSettingsMatch
    {
        public enum Group { Lighting, RimShine, Outline, Emission }

        public class Change
        {
            public string property;
            public Group group;
            public ShaderPropertyType type;
            public Vector4 from, to;   // floats in x; colours as rgba
            public bool isInt;
        }

        public static bool Compatible(Material target, Material source, out string why)
        {
            why = null;
            if (target == null || source == null) { why = Loc.T("Pick a material to match first."); return false; }
            var ft = ShaderProfiles.Detect(target.shader);
            var fs = ShaderProfiles.Detect(source.shader);
            if (ft == ShaderFamily.Other || fs == ShaderFamily.Other || ft != fs)
            {
                why = Loc.F("Only between materials with the same shader (both Poiyomi or both lilToon). “{0}” uses {1}, “{2}” uses {3}.", target.name, ShaderProfiles.FamilyLabel(ft), source.name, ShaderProfiles.FamilyLabel(fs));
                return false;
            }
            return true;
        }

        static readonly string[] Never =
        {
            "Tex", "Map", "Mask", "_ST", "UV", "Pan", "Speed", "Stencil", "ThemeIndex", "Cull", "ZWrite", "ZTest",
            "SrcBlend", "DstBlend", "BlendOp", "AlphaToMask", "Offset", "Texel", "Animated", "Version",
        };

        /// <summary>Which group a property belongs to, or null if it is never copied.</summary>
        public static Group? Classify(string n, ShaderPropertyType type)
        {
            if (string.IsNullOrEmpty(n) || n[0] != '_' || type == ShaderPropertyType.Texture) return null;
            foreach (var x in Never) if (n.Contains(x)) return null;
            if (n == "_Mode" || n == "_RenderingMode" || n == "_TransparentMode") return null;
            if (n.StartsWith("_EnableEmission") || n.StartsWith("_UseEmission") || n.StartsWith("_EmissionColor") ||
                n.StartsWith("_EmissionStrength") || n.StartsWith("_Emission2ndColor") || n.StartsWith("_EmissionMainStrength") ||
                n.StartsWith("_Emission2ndMainStrength") || n.StartsWith("_EmissionBlend") || n.StartsWith("_Emission2ndBlend"))
                return Group.Emission;
            if (n.Contains("Shadow") || n.Contains("Shade") || n.Contains("Shading") || n.StartsWith("_Lighting") ||
                n.StartsWith("_LightMin") || n.StartsWith("_LightMax") || n.Contains("Monochrome") || n == "_AsUnlit" ||
                n == "_lilDirectionalLightStrength" || n == "_BeforeExposureLimit")
                return Group.Lighting;
            if (n.StartsWith("_Rim") || n.StartsWith("_Matcap") || n.StartsWith("_MatCap") || n.StartsWith("_Backlight"))
                return Group.RimShine;
            if (n.StartsWith("_Outline") && type == ShaderPropertyType.Color) return Group.Outline;
            return null;
        }

        /// <summary>The settings that would change on target (only properties both materials have, and only if different).</summary>
        public static List<Change> Plan(Material target, Material source, bool lighting, bool rimShine, bool outline, bool emission = false)
        {
            var list = new List<Change>();
            if (!Compatible(target, source, out _)) return list;
            var shader = MaterialWriter.SourceShader(source.shader) ?? source.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string n = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                var g = Classify(n, type);
                if (g == null) continue;
                if ((g == Group.Lighting && !lighting) || (g == Group.RimShine && !rimShine) || (g == Group.Outline && !outline) || (g == Group.Emission && !emission)) continue;
                if (!target.HasProperty(n) || !source.HasProperty(n)) continue;
                var c = new Change { property = n, group = g.Value, type = type };
                switch (type)
                {
                    case ShaderPropertyType.Color:
                        c.from = target.GetColor(n); c.to = source.GetColor(n);
                        if ((c.from - c.to).sqrMagnitude < 1e-8f) continue;
                        break;
                    case ShaderPropertyType.Vector:
                        c.from = target.GetVector(n); c.to = source.GetVector(n);
                        if ((c.from - c.to).sqrMagnitude < 1e-10f) continue;
                        break;
                    case ShaderPropertyType.Int:
                        c.isInt = true;
                        c.from = new Vector4(target.GetInteger(n), 0, 0, 0); c.to = new Vector4(source.GetInteger(n), 0, 0, 0);
                        if (c.from.x == c.to.x) continue;
                        break;
                    default:
                        c.from = new Vector4(target.GetFloat(n), 0, 0, 0); c.to = new Vector4(source.GetFloat(n), 0, 0, 0);
                        if (Mathf.Abs(c.from.x - c.to.x) < 1e-6f) continue;
                        break;
                }
                list.Add(c);
            }
            return list;
        }

        /// <summary>Writes the planned values onto target (one undo step) and lets the shader tidy up afterwards.</summary>
        public static int Apply(Material target, List<Change> changes)
        {
            if (target == null || changes == null || changes.Count == 0) return 0;
            Undo.RecordObject(target, "Match material settings");
            MaterialWriter.EnsureEditable(new[] { target });
            foreach (var c in changes)
            {
                if (!target.HasProperty(c.property)) continue;
                switch (c.type)
                {
                    case ShaderPropertyType.Color: target.SetColor(c.property, c.to); break;
                    case ShaderPropertyType.Vector: target.SetVector(c.property, c.to); break;
                    default:
                        if (c.isInt) target.SetInteger(c.property, Mathf.RoundToInt(c.to.x));
                        else target.SetFloat(c.property, c.to.x);
                        break;
                }
            }
            EditorUtility.SetDirty(target);
            switch (ShaderProfiles.Detect(target.shader))
            {
                case ShaderFamily.Poiyomi: MaterialWriter.FixPoiyomiKeywords(new[] { target }); break;
                case ShaderFamily.LilToon: MaterialWriter.AfterLilToonEdit(new[] { target }); break;
            }
            return changes.Count;
        }

        public static string Describe(Change c)
        {
            string name = ShaderProfiles.FriendlyName(c.property, null);
            if (c.type == ShaderPropertyType.Color)
                return $"{name}: #{ColorUtility.ToHtmlStringRGB((Color)c.from)} → #{ColorUtility.ToHtmlStringRGB((Color)c.to)}";
            if (c.type == ShaderPropertyType.Vector) return $"{name}: {((Vector4)c.from).ToString("0.##")} → {((Vector4)c.to).ToString("0.##")}";
            return $"{name}: {c.from.x:0.###} → {c.to.x:0.###}";
        }
    }
}
