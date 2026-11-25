using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public static class MaterialSettingsMatch
    {
        public enum Group { Lighting, RimShine, Outline, Emission }

        public class Change
        {
            public string property;
            public Group group;
            public ShaderPropertyType type;
            public Vector4 from, to;
            public bool isInt;
            internal int? originalInteger, copiedInteger;
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
            if (n.StartsWith("_Rim") || n.StartsWith("_Matcap") || n.StartsWith("_MatCap") || n.StartsWith("_Backlight") ||
                n is "_EnableRimLighting" or "_EnableRim2Lighting" or "_UseRim" or "_UseMatCap" or "_UseMatCap2nd" or "_UseBacklight")
                return Group.RimShine;
            if (n.StartsWith("_Outline") && type == ShaderPropertyType.Color) return Group.Outline;
            return null;
        }

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
                int targetIndex = target.shader.FindPropertyIndex(n), sourceIndex = source.shader.FindPropertyIndex(n);
                if (targetIndex < 0 || sourceIndex < 0 || target.shader.GetPropertyType(targetIndex) != type ||
                    source.shader.GetPropertyType(sourceIndex) != type) continue;
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
                        int from = target.GetInteger(n), to = source.GetInteger(n);
                        if (from == to) continue;
                        c.originalInteger = from;
                        c.copiedInteger = to;
                        c.from = new Vector4(from, 0, 0, 0);
                        c.to = new Vector4(to, 0, 0, 0);
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

        public static int Apply(Material target, List<Change> changes)
            => Write(target, changes, false);

        internal static void PrepareCopy(Material copy, List<Change> changes) => Write(copy, changes, false, recordUndo: false);

        internal static int Restore(Material target, List<Change> changes)
        {
            if (target == null || MaterialWriter.IsPoiyomiLocked(target) || MaterialWriter.IsSectionLocked(target)) return 0;
            var restorable = changes.FindAll(c => target.HasProperty(c.property) && HasCopiedValue(target, c));
            return Write(target, restorable, true);
        }

        static bool HasCopiedValue(Material target, Change c)
        {
            switch (c.type)
            {
                case ShaderPropertyType.Color: return target.GetColor(c.property).Equals((Color)c.to);
                case ShaderPropertyType.Vector: return target.GetVector(c.property).Equals(c.to);
                default: return c.isInt
                    ? target.GetInteger(c.property) == (c.copiedInteger ?? Mathf.RoundToInt(c.to.x))
                    : target.GetFloat(c.property).Equals(c.to.x);
            }
        }

        static int Write(Material target, List<Change> changes, bool restore, bool recordUndo = true)
        {
            if (target == null || changes == null || changes.Count == 0) return 0;
            if (recordUndo) Undo.RecordObject(target, restore ? Loc.T("Restore material settings") : "Match material settings");
            MaterialWriter.EnsureEditable(new[] { target });
            int written = 0;
            foreach (var c in changes)
            {
                if (!target.HasProperty(c.property)) continue;
                var value = restore ? c.from : c.to;
                switch (c.type)
                {
                    case ShaderPropertyType.Color: target.SetColor(c.property, value); break;
                    case ShaderPropertyType.Vector: target.SetVector(c.property, value); break;
                    default:
                        if (c.isInt) target.SetInteger(c.property, (restore ? c.originalInteger : c.copiedInteger) ?? Mathf.RoundToInt(value.x));
                        else target.SetFloat(c.property, value.x);
                        break;
                }
                written++;
            }
            EditorUtility.SetDirty(target);
            switch (ShaderProfiles.Detect(target.shader))
            {
                case ShaderFamily.Poiyomi: MaterialWriter.FixPoiyomiKeywords(new[] { target }); break;
                case ShaderFamily.LilToon: MaterialWriter.AfterLilToonEdit(new[] { target }); break;
            }
            return written;
        }

        public static string Describe(Change c)
        {
            string name = ShaderProfiles.FriendlyName(c.property, null);
            if (c.type == ShaderPropertyType.Color)
                return $"{name}: {DescribeColour(c.from)} → {DescribeColour(c.to)}";
            if (c.type == ShaderPropertyType.Vector) return $"{name}: {((Vector4)c.from).ToString("0.##")} → {((Vector4)c.to).ToString("0.##")}";
            return $"{name}: {c.from.x:0.###} → {c.to.x:0.###}";
        }

        static string DescribeColour(Vector4 rgba) => rgba.x >= 0f && rgba.x <= 1f && rgba.y >= 0f && rgba.y <= 1f &&
            rgba.z >= 0f && rgba.z <= 1f && rgba.w >= 0f && rgba.w <= 1f
            ? "#" + ColorUtility.ToHtmlStringRGBA((Color)rgba)
            : rgba.ToString("0.###");
    }
}
