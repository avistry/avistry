using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class PoiyomiAnimationSettings
    {
        internal readonly struct Setting
        {
            internal readonly string Property, Label;
            internal readonly bool Renamed;

            internal Setting(string property, string label, bool renamed)
            {
                Property = property;
                Label = label;
                Renamed = renamed;
            }
        }

        internal static List<Setting> Read(Material material)
            => ReadMarks(material, locked: true);

        internal static List<Setting> ReadUnlocked(Material material)
            => ReadMarks(material, locked: false);

        internal static bool HasEditableShader(Material material) => material != null && material.shader != null &&
            ShaderProfiles.Detect(material.shader) == ShaderFamily.Poiyomi &&
            !MaterialWriter.IsPoiyomiLocked(material) && !MaterialWriter.IsSectionLocked(material);

        internal static bool HasMark(Material material, Setting setting) => material.HasProperty(setting.Property) &&
            material.GetTag(setting.Property + "Animated", false, "") == (setting.Renamed ? "2" : "1");

        internal static bool IsMarked(Material material, string property)
        {
            string flag = material.GetTag(property + "Animated", false, "");
            return flag == "1" || flag == "2";
        }

        internal static void ClearMark(Material material, Setting setting)
        {
            string tag = setting.Property + "Animated";
            material.SetOverrideTag(tag, "");
            if (IsMarked(material, setting.Property)) material.SetOverrideTag(tag, "0");
        }

        static List<Setting> ReadMarks(Material material, bool locked)
        {
            var result = new List<Setting>();
            if (locked ? !MaterialWriter.IsPoiyomiLocked(material) : !HasEditableShader(material)) return result;

            string suffix = material.GetTag("thry_locked_rename_suffix", false, "");
            if (suffix.Length == 0) suffix = material.GetTag("thry_rename_suffix", false, "");
            string ending = !locked || suffix.Length == 0 ? null : "_" + suffix;
            var shader = material.shader;
            for (int i = 0, count = shader.GetPropertyCount(); i < count; i++)
            {
                string property = shader.GetPropertyName(i);
                bool renamed = false;
                if (ending != null && property.EndsWith(ending, StringComparison.Ordinal))
                {
                    string original = property.Substring(0, property.Length - ending.Length);
                    if (material.GetTag(original + "Animated", false, "") == "2")
                    {
                        property = original;
                        renamed = true;
                    }
                }
                if (!renamed)
                {
                    string flag = material.GetTag(property + "Animated", false, "");
                    if (flag != "1" && flag != "2") continue;
                    if (flag == "2" && ending != null && material.HasProperty(property + ending)) continue;
                    renamed = !locked && flag == "2";
                }

                string label = shader.GetPropertyDescription(i);
                int metadata = label.IndexOf("--", StringComparison.Ordinal);
                if (metadata >= 0) label = label.Substring(0, metadata);
                label = label.Trim();
                result.Add(new Setting(property, label.Length != 0 ? label : property, renamed));
            }
            return result;
        }
    }
}
