using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Avistry.Textures
{
    public enum ShaderFamily { Other, Poiyomi, LilToon }

    /// <summary>
    /// What the tool knows about Poiyomi Pro (11.x / 9.x) and lilToon (2.x) textures: friendly names, roles,
    /// packed-channel meanings, which feature toggle uses a texture, and UV settings that make baked results misalign.
    /// Sources: Poiyomi shader attributes, lilToon's fragment shader.
    /// </summary>
    public static class ShaderProfiles
    {
        public static ShaderFamily Detect(Shader s)
        {
            if (s == null) return ShaderFamily.Other;
            s = MaterialWriter.SourceShader(s); // Poiyomi's temporary section-locked shader -> the real one
            string n = s.name;
            if (n.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0) return ShaderFamily.Poiyomi;
            if (n.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0 || n.StartsWith("_lil/")) return ShaderFamily.LilToon;
            return ShaderFamily.Other;
        }

        /// <summary>lilToon Lite shaders have a reduced property set (no reflection, glitter or second shadow).</summary>
        internal static bool IsLilLite(Shader s) => s != null && MaterialWriter.SourceShader(s).name.IndexOf("Lite", StringComparison.OrdinalIgnoreCase) >= 0;

        public static string FamilyLabel(ShaderFamily f) => f switch
        {
            ShaderFamily.Poiyomi => "Poiyomi",
            ShaderFamily.LilToon => "lilToon",
            _ => Loc.T("Other"),
        };


        static readonly string[] LookupNames = { "DFG", "SkinLUT", "_TPS_BakedMesh", "_TextGlyphs", "_DitherTex", "_ClothDFG" };

        /// <summary>Lookup tables and generated data that must not be edited as images.</summary>
        public static bool IsLookupTable(Shader s, int index, string prop)
        {
            foreach (var raw in s.GetPropertyAttributes(index))
            {
                string a = raw.Trim('[', ']', ' ');
                if (a.StartsWith("NonModifiableTextureData", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("DoNotLock", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return LookupNames.Any(n => prop.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // lilToon declares no roles besides [Normal].

        static readonly HashSet<string> LilColour = new HashSet<string>
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_Main2ndTex", "_Main3rdTex",
            "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex", "_ReflectionColorTex",
            "_MatCapTex", "_MatCap2ndTex", "_RimColorTex", "_EmissionMap", "_Emission2ndMap",
            "_BacklightColorTex", "_OutlineTex", "_GlitterColorTex",
        };

        static readonly HashSet<string> LilNormal = new HashSet<string>
        {
            "_BumpMap", "_Bump2ndMap", "_MatCapBumpMap", "_MatCap2ndBumpMap", "_AnisotropyTangentMap", "_OutlineVectorTex", "_FurVectorTex",
        };

        /// <summary>lilToon role from its texture name (null = unknown, fall back to heuristics).</summary>
        public static TextureRole? LilRole(string prop)
        {
            if (LilNormal.Contains(prop)) return TextureRole.Normal;
            if (LilColour.Contains(prop)) return TextureRole.Color;
            if (prop.EndsWith("GradTex") || prop == "_MainGradationTex") return TextureRole.Mask; // linear ramps
            if (prop.Contains("Mask") || prop.EndsWith("Tex") && (prop.Contains("Smoothness") || prop.Contains("Metallic"))) return TextureRole.Mask;
            return null;
        }


        static string KnownName(string prop) => prop switch
        {
            // shared / Poiyomi
            "_MainTex" => Loc.T("Main colour"), "_BumpMap" => Loc.T("Normal map"), "_AlphaMask" => Loc.T("Transparency mask"),
            "_Bump2ndMap" => Loc.T("Detail normal map"), "_Bump2ndScaleMask" => Loc.T("Detail normal mask"),
            "_DetailTex" => Loc.T("Detail colour"), "_DetailMask" => Loc.T("Detail mask"), "_DetailNormalMap" => Loc.T("Detail normal map"),
            "_MainColorAdjustTexture" => Loc.T("Colour adjust mask"),
            "_ShadowColorTex" => Loc.T("Shadow colour"), "_Shadow2ndColorTex" => Loc.T("Shadow 2 colour"), "_Shadow3rdColorTex" => Loc.T("Shadow 3 colour"),
            "_1st_ShadeMap" => Loc.T("Shade map 1"), "_2nd_ShadeMap" => Loc.T("Shade map 2"), "_ToonRamp" => Loc.T("Toon ramp"),
            "_ShadowStrengthMask" => Loc.T("Shadow strength mask"), "_ShadowBorderMask" => Loc.T("Shadow AO mask"), "_ShadowBlurMask" => Loc.T("Shadow blur mask"),
            "_LightingAOMaps" => Loc.T("AO maps"), "_LightingDetailShadowMaps" => Loc.T("Detail shadow maps"), "_LightingShadowMasks" => Loc.T("Shadow masks"),
            "_Matcap" => Loc.T("Matcap"), "_Matcap2" => Loc.T("Matcap 2"), "_Matcap3" => Loc.T("Matcap 3"), "_Matcap4" => Loc.T("Matcap 4"),
            "_MatcapMask" => Loc.T("Matcap mask"), "_Matcap2Mask" => Loc.T("Matcap 2 mask"), "_Matcap3Mask" => Loc.T("Matcap 3 mask"), "_Matcap4Mask" => Loc.T("Matcap 4 mask"),
            "_RimTex" => Loc.T("Rim texture"), "_RimColorTex" => Loc.T("Rim colour"), "_RimMask" => Loc.T("Rim mask"), "_Rim2ColorTex" => Loc.T("Rim 2 colour"), "_Rim2Mask" => Loc.T("Rim 2 mask"),
            "_EmissionMap" => Loc.T("Emission"), "_EmissionMap1" => Loc.T("Emission 2"), "_EmissionMap2" => Loc.T("Emission 3"), "_EmissionMap3" => Loc.T("Emission 4"),
            "_EmissionMask" => Loc.T("Emission mask"), "_EmissionMask1" => Loc.T("Emission 2 mask"), "_EmissionMask2" => Loc.T("Emission 3 mask"), "_EmissionMask3" => Loc.T("Emission 4 mask"),
            "_OutlineTexture" => Loc.T("Outline colour"), "_OutlineMask" => Loc.T("Outline mask"), "_OutlineEmissionMap" => Loc.T("Outline emission"),
            "_BacklightColorTex" => Loc.T("Backlight colour"), "_ReflectionColorTex" => Loc.T("Reflection colour"),
            "_PoiPBRMaskMap" => Loc.T("PBR mask (metal/AO/smooth)"), "_MochieMetallicMaps" => Loc.T("Metallic maps"),
            "_RGBMask" => Loc.T("RGBA colour mask"), "_DecalMask" => Loc.T("Decal mask"), "_GlitterColorMap" => Loc.T("Glitter colour"), "_GlitterMask" => Loc.T("Glitter mask"),
            "_DecalTexture" => Loc.T("Decal 1"), "_DecalTexture1" => Loc.T("Decal 2"), "_DecalTexture2" => Loc.T("Decal 3"), "_DecalTexture3" => Loc.T("Decal 4"),
            "_DissolveMask" => Loc.T("Dissolve mask"), "_DissolveNoiseTexture" => Loc.T("Dissolve noise"), "_BackFaceTexture" => Loc.T("Back-face texture"),
            // lilToon
            "_Main2ndTex" => Loc.T("Main colour layer 2"), "_Main3rdTex" => Loc.T("Main colour layer 3"),
            "_Main2ndBlendMask" => Loc.T("Layer 2 mask"), "_Main3rdBlendMask" => Loc.T("Layer 3 mask"), "_MainColorAdjustMask" => Loc.T("Colour adjust mask"),
            "_MatCapTex" => Loc.T("Matcap"), "_MatCap2ndTex" => Loc.T("Matcap 2"), "_MatCapBlendMask" => Loc.T("Matcap mask"), "_MatCap2ndBlendMask" => Loc.T("Matcap 2 mask"),
            "_MatCapBumpMap" => Loc.T("Matcap normal"), "_RimShadeMask" => Loc.T("Rim shade mask"),
            "_Emission2ndMap" => Loc.T("Emission 2"), "_EmissionBlendMask" => Loc.T("Emission mask"), "_Emission2ndBlendMask" => Loc.T("Emission 2 mask"),
            "_OutlineTex" => Loc.T("Outline colour"), "_OutlineWidthMask" => Loc.T("Outline width mask"), "_OutlineVectorTex" => Loc.T("Outline direction"),
            "_SmoothnessTex" => Loc.T("Smoothness"), "_MetallicGlossMap" => Loc.T("Metallic"), "_GlitterColorTex" => Loc.T("Glitter colour"),
            "_TriMask" => Loc.T("Tri-mask (matcap/rim/emission)"), "_ParallaxMap" => Loc.T("Height map"), "_FurMask" => Loc.T("Fur mask"),
            _ => null,
        };

        /// <summary>True for textures this tool has a plain name for.</summary>
        public static bool HasFriendlyName(string prop) => KnownName(prop) != null;

        public static string FriendlyName(string prop, string fallback)
        {
            if (KnownName(prop) is string n) return n;
            // Use the shader's own label unless it's generic ("Texture", "Mask") or a localisation key ("sBorder").
            bool generic = string.IsNullOrEmpty(fallback) || fallback == "Texture" || fallback == "Mask" ||
                           System.Text.RegularExpressions.Regex.IsMatch(fallback, "^s[A-Z]");
            if (!generic) return fallback;
            // "_Main2ndTex" -> "Main 2nd Tex"
            var t = prop.TrimStart('_');
            return System.Text.RegularExpressions.Regex.Replace(t, "(?<=[a-z0-9])(?=[A-Z])", " ");
        }


        static string PoiChannelText(string prop) => prop switch
        {
            "_MainColorAdjustTexture" => Loc.T("R hue · G brightness · B saturation · A gamma"),
            "_MatcapMask" => Loc.T("R mask · A smoothness"), "_Matcap2Mask" => Loc.T("R mask · A smoothness"),
            "_PoiPBRMaskMap" => Loc.T("R metallic · G occlusion · A smoothness"),
            "_MochieMetallicMaps" => Loc.T("R metallic · G smoothness · B reflection · A specular"),
            "_RGBMask" => Loc.T("R/G/B/A = colour masks 1–4"), "_DecalMask" => Loc.T("R/G/B/A = decals 1–4"),
            "_DetailMask" => Loc.T("R texture · G normal"), "_RimMask" => Loc.T("R mask · G bias"), "_Rim2Mask" => Loc.T("R mask · G bias"),
            "_SSSThicknessMap" => Loc.T("RGB colour · A thickness"),
            _ => null,
        };

        static string LilChannelText(string prop) => prop switch
        {
            "_ShadowStrengthMask" => Loc.T("R strength (SDF: R/G left/right, B blend, A alt)"),
            "_ShadowBorderMask" => Loc.T("R/G/B = AO for shadow 1/2/3"),
            "_ShadowBlurMask" => Loc.T("R/G/B = blur for shadow 1/2/3"),
            "_MatCapBlendMask" => Loc.T("RGB mask"), "_MatCap2ndBlendMask" => Loc.T("RGB mask"),
            "_EmissionBlendMask" => Loc.T("RGB mask (can tint)"), "_Emission2ndBlendMask" => Loc.T("RGB mask (can tint)"),
            "_TriMask" => Loc.T("R matcap · G rim · B emission"),
            _ => null,
        };

        static readonly string[] Rgba = { "R", "G", "B", "A" };

        /// <summary>What the channels of a packed/mask texture mean, if known.</summary>
        public static string ChannelInfo(ShaderFamily f, Material m, string prop)
        {
            if (f == ShaderFamily.Poiyomi)
            {
                if (PoiChannelText(prop) is string c) return c;
                string chProp = prop + "Channel";
                if (m.HasProperty(chProp))
                {
                    int ch = Mathf.RoundToInt(m.GetFloat(chProp));
                    if (ch >= 0 && ch < 4) return Loc.F("uses the {0} channel", Rgba[ch]);
                }
            }
            else if (f == ShaderFamily.LilToon)
            {
                if (LilChannelText(prop) is string c) return c;
                if (prop.Contains("Mask") || prop == "_SmoothnessTex" || prop == "_MetallicGlossMap") return Loc.T("uses the R channel");
            }
            return null;
        }


        /// <summary>The toggle property a texture depends on (if the toggle is 0, the shader ignores the texture).</summary>
        public static string FeatureToggle(ShaderFamily f, string prop)
        {
            if (f == ShaderFamily.LilToon)
            {
                if (prop.StartsWith("_Main2nd")) return "_UseMain2ndTex";
                if (prop.StartsWith("_Main3rd")) return "_UseMain3rdTex";
                if (prop.StartsWith("_Shadow")) return "_UseShadow";
                if (prop.StartsWith("_RimShade")) return "_UseRimShade";
                if (prop.StartsWith("_Rim")) return "_UseRim";
                if (prop.StartsWith("_MatCap2nd")) return "_UseMatCap2nd";
                if (prop.StartsWith("_MatCap")) return "_UseMatCap";
                if (prop.StartsWith("_Emission2nd")) return "_UseEmission2nd";
                if (prop.StartsWith("_Emission")) return "_UseEmission";
                if (prop.StartsWith("_Bump2nd")) return "_UseBump2ndMap";
                if (prop == "_BumpMap") return "_UseBumpMap";
                if (prop.StartsWith("_Backlight")) return "_UseBacklight";
                if (prop.StartsWith("_Reflection") || prop == "_SmoothnessTex" || prop == "_MetallicGlossMap") return "_UseReflection";
                if (prop.StartsWith("_Glitter")) return "_UseGlitter";
                if (prop == "_MainColorAdjustMask") return null;
                return null;
            }
            if (f == ShaderFamily.Poiyomi)
            {
                var em = System.Text.RegularExpressions.Regex.Match(prop, @"^_Emission(?:Map|Mask)(\d?)$");
                if (em.Success) return "_EnableEmission" + em.Groups[1].Value;
                if (prop == "_Matcap" || prop == "_MatcapMask") return "_MatcapEnable";
                var mc = System.Text.RegularExpressions.Regex.Match(prop, @"^_Matcap([234])(Mask)?$");
                if (mc.Success) return $"_Matcap{mc.Groups[1].Value}Enable";
                if (prop.StartsWith("_Rim2")) return "_EnableRim2Lighting";
                if (prop.StartsWith("_Rim")) return "_EnableRimLighting";
                var dc = System.Text.RegularExpressions.Regex.Match(prop, @"^_DecalTexture(\d?)$");
                if (dc.Success) return "_DecalEnabled" + dc.Groups[1].Value;
                if (prop.StartsWith("_Backlight")) return "_BacklightEnabled";
                if (prop == "_MainColorAdjustTexture") return "_MainColorAdjustToggle";
                if (prop.StartsWith("_Glitter")) return "_GlitterEnable";
                if (prop.StartsWith("_Shadow") || prop.Contains("_ShadeMap") || prop == "_ToonRamp") return "_ShadingEnabled";
            }
            return null;
        }

        /// <summary>True when the texture's feature toggle exists and is switched off.</summary>
        public static bool FeatureIsOff(ShaderFamily f, Material m, string prop)
        {
            var t = FeatureToggle(f, prop);
            return t != null && m.HasProperty(t) && m.GetFloat(t) < 0.5f;
        }


        /// <summary>Settings that mean a baked texture or island painting won't line up with the model.</summary>
        public static List<string> UvProblems(ShaderFamily f, Material m, string prop)
        {
            var list = new List<string>();
            if (f == ShaderFamily.Poiyomi)
            {
                if (m.HasProperty(prop + "UV"))
                {
                    int uv = Mathf.RoundToInt(m.GetFloat(prop + "UV"));
                    if (uv >= 4) list.Add(Loc.T("This texture isn't mapped by part UVs (panosphere / world / screen…), so piece selection and painting won't line up."));
                }
                if (m.HasProperty(prop + "Pan") && m.GetVector(prop + "Pan").sqrMagnitude > 1e-8f)
                    list.Add(Loc.T("This texture scrolls (Pan), so painting lines up only with its starting position."));
            }
            else if (f == ShaderFamily.LilToon && m.HasProperty("_MainTex_ScrollRotate") && m.GetVector("_MainTex_ScrollRotate").sqrMagnitude > 1e-8f)
                list.Add(Loc.T("The main texture scrolls/rotates, so painting lines up only with its starting position."));

            string stProp = f == ShaderFamily.LilToon && !prop.StartsWith("_Main2nd") && !prop.StartsWith("_Main3rd") && !prop.StartsWith("_Emission") ? "_MainTex" : prop;
            if (m.HasProperty(stProp))
            {
                var scale = m.GetTextureScale(stProp);
                var offset = m.GetTextureOffset(stProp);
                if ((scale - Vector2.one).sqrMagnitude > 1e-6f || offset.sqrMagnitude > 1e-6f)
                    list.Add(Loc.F("Tiling/offset is not 1×1 ({0:0.##}×{1:0.##}), so the texture repeats or shifts on the model; piece outlines may not match.", scale.x, scale.y));
            }
            return list;
        }
    }
}
