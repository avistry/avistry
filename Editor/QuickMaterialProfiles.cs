using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    internal enum QKind { Colour, ColourHdr, Range, Toggle }

    /// <summary>One quick material control: which shader property it edits and how it is shown.</summary>
    internal sealed class QControl
    {
        public string id, group, label, tip, prop;
        public string toggle;          // property of the on/off switch this control depends on (null = always usable)
        public QKind kind;
        public float min, max;         // Range: used unless the shader declares its own range
        public float onValue = 1f;     // Toggle: the value that means "on"
        public string onSet;           // Toggle: a strength property that is raised to onSetValue when it is 0 as the effect turns on
        public float onSetValue;
        public bool invert;            // Range: shown as (min + max - value)
        public string gate;            // property that must be gateValue (up to gateMax, when that is higher) for this control to apply; otherwise gateNote is shown
        public int gateValue, gateMax;
        public bool hdr;               // Colour: drawn with HDR intensity and keeps its alpha (lilToon's lilHDR colours)
        public string gateNote;
    }

    /// <summary>
    /// The settings Quick material offers for Poiyomi and lilToon, with their exact property names. Poiyomi names come from its
    /// shader attributes; lilToon names were checked against the lilToon shader's property list. A name the material does not have
    /// is dropped, so a wrong or renamed property hides its control instead of failing.
    /// </summary>
    internal static class QuickMaterialProfiles
    {
        static QControl Col(string group, string id, string label, string tip, string prop, string toggle = null, bool hdr = false) =>
            new QControl { group = group, id = id, label = label, tip = tip, prop = prop, toggle = toggle, kind = hdr ? QKind.ColourHdr : QKind.Colour };

        static QControl ColHdrAlpha(string group, string id, string label, string tip, string prop, string toggle = null) =>
            new QControl { group = group, id = id, label = label, tip = tip, prop = prop, toggle = toggle, kind = QKind.Colour, hdr = true };

        static QControl Rng(string group, string id, string label, string tip, string prop, float min, float max, string toggle = null, bool invert = false) =>
            new QControl { group = group, id = id, label = label, tip = tip, prop = prop, toggle = toggle, kind = QKind.Range, min = min, max = max, invert = invert };

        static QControl Tog(string group, string id, string label, string tip, string prop, string onSet = null, float onSetValue = 0f) =>
            new QControl { group = group, id = id, label = label, tip = tip, prop = prop, kind = QKind.Toggle, onSet = onSet, onSetValue = onSetValue };

        /// <summary>The controls this material has, in display order (empty for other shaders).</summary>
        internal static List<QControl> For(Material m)
        {
            var list = new List<QControl>();
            if (m == null || m.shader == null) return list;
            var family = ShaderProfiles.Detect(m.shader);
            if (family == ShaderFamily.Poiyomi) AddPoiyomi(list);
            else if (family == ShaderFamily.LilToon) AddLilToon(list, ShaderProfiles.IsLilLite(m.shader), MaterialWriter.SourceShader(m.shader).name.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0);
            list.RemoveAll(c => !m.HasProperty(c.prop));
            foreach (var c in list)
                if (c.kind == QKind.Range) UseShaderRange(m, c);
            return list;
        }

        /// <summary>Number of feature groups (tint, glow, rim…), for the fold's summary.</summary>
        internal static int GroupCount(List<QControl> controls) => controls.Select(c => c.group).Distinct().Count();

        static void AddPoiyomi(List<QControl> l)
        {
            l.Add(Col("tint", "tint", Loc.T("Tint"), Loc.T("Multiplies the main texture colour."), "_Color"));
            l.Add(Tog("glow", "emission", Loc.T("Glow"), Loc.T("Makes this material glow."), "_EnableEmission", "_EmissionStrength", 1f));
            l.Add(Col("glow", "emissionColour", Loc.T("Glow colour"), Loc.T("The colour of the glow."), "_EmissionColor", "_EnableEmission", true));
            l.Add(Rng("glow", "emissionStrength", Loc.T("Glow strength"), Loc.T("How bright the glow is."), "_EmissionStrength", 0f, 20f, "_EnableEmission"));
            l.Add(Tog("rim", "rim", Loc.T("Rim light"), Loc.T("A light edge around the shape."), "_EnableRimLighting"));
            var rimColour = Col("rim", "rimColour", Loc.T("Rim colour"), Loc.T("The colour of the rim light."), "_RimLightColor", "_EnableRimLighting");
            rimColour.gate = "_RimStyle";
            rimColour.gateMax = 1; // the colour is used by the Poiyomi and UTS2 styles
            rimColour.gateNote = Loc.T("This rim style has its own colour: edit it in the material.");
            l.Add(rimColour);
            var rimWidth = Rng("rim", "rimWidth", Loc.T("Rim width"), Loc.T("How far the rim reaches in."), "_RimWidth", 0f, 1f, "_EnableRimLighting");
            var rimSoft = Rng("rim", "rimSoft", Loc.T("Rim softness"), Loc.T("How soft the rim edge is."), "_RimSharpness", 0f, 1f, "_EnableRimLighting", true);
            rimWidth.gate = rimSoft.gate = "_RimStyle";
            rimWidth.gateNote = rimSoft.gateNote = Loc.T("Rim style isn't Poiyomi: edit it in the material.");
            l.Add(rimWidth);
            l.Add(rimSoft);
            l.Add(Tog("matcap", "matcap", Loc.T("Matcap"), Loc.T("A shiny look painted on from a picture."), "_MatcapEnable"));
            l.Add(Col("matcap", "matcapColour", Loc.T("Matcap colour"), Loc.T("Tints the matcap."), "_MatcapColor", "_MatcapEnable"));
            l.Add(Rng("matcap", "matcapStrength", Loc.T("Matcap strength"), Loc.T("How much of the matcap shows."), "_MatcapIntensity", 0f, 5f, "_MatcapEnable"));
            l.Add(Col("shadow", "shadow", Loc.T("Shadow colour"), Loc.T("The colour of the shaded side."), "_LightingShadowColor"));
            l.Add(Tog("outline", "outline", Loc.T("Outline"), Loc.T("A line around the shape."), "_EnableOutlines"));
            l.Add(Col("outline", "outlineColour", Loc.T("Outline colour"), Loc.T("The colour of the outline."), "_LineColor", "_EnableOutlines"));
            l.Add(Rng("outline", "outlineWidth", Loc.T("Outline width"), Loc.T("How thick the outline is."), "_LineWidth", 0f, 10f, "_EnableOutlines")); // a plain Float in the shader, so 1 is not the top
            l.Add(Rng("metal", "metal", Loc.T("Metal"), Loc.T("How metallic the surface looks. Only shows when the Poiyomi PBR section is on."), "_MochieMetallicMultiplier", 0f, 1f));
            l.Add(Rng("metal", "smooth", Loc.T("Smoothness"), Loc.T("How glossy the surface is. Only shows when the Poiyomi PBR section is on."), "_MochieRoughnessMultiplier", 0f, 1f));
            l.Add(Tog("glitter", "glitter", Loc.T("Glitter"), Loc.T("Sparkling specks."), "_GlitterEnable"));
            l.Add(Col("glitter", "glitterColour", Loc.T("Glitter colour"), Loc.T("The colour of the sparkles."), "_GlitterColor", "_GlitterEnable", true));
        }

        static void AddLilToon(List<QControl> l, bool lite, bool outline)
        {
            l.Add(ColHdrAlpha("tint", "tint", Loc.T("Tint"), Loc.T("Multiplies the main texture colour."), "_Color"));
            l.Add(Tog("glow", "emission", Loc.T("Glow"), Loc.T("Makes this material glow."), "_UseEmission"));
            l.Add(Col("glow", "emissionColour", Loc.T("Glow colour"), Loc.T("The colour and brightness of the glow."), "_EmissionColor", "_UseEmission", true));
            l.Add(Tog("rim", "rim", Loc.T("Rim light"), Loc.T("A light edge around the shape."), "_UseRim"));
            l.Add(ColHdrAlpha("rim", "rimColour", Loc.T("Rim colour"), Loc.T("The colour of the rim light."), "_RimColor", "_UseRim"));
            l.Add(Rng("rim", "rimWidth", Loc.T("Rim width"), Loc.T("How far the rim reaches in."), "_RimBorder", 0f, 1f, "_UseRim", true));
            l.Add(Rng("rim", "rimSoft", Loc.T("Rim softness"), Loc.T("How soft the rim edge is."), "_RimBlur", 0f, 1f, "_UseRim"));
            l.Add(Tog("matcap", "matcap", Loc.T("Matcap"), Loc.T("A shiny look painted on from a picture."), "_UseMatCap"));
            l.Add(ColHdrAlpha("matcap", "matcapColour", Loc.T("Matcap colour"), Loc.T("Tints the matcap."), "_MatCapColor", "_UseMatCap"));
            l.Add(Rng("matcap", "matcapStrength", Loc.T("Matcap strength"), Loc.T("How much of the matcap shows."), "_MatCapBlend", 0f, 1f, "_UseMatCap"));
            l.Add(Tog("shadow", "shadowOn", Loc.T("Shadow"), Loc.T("Shades the side away from the light."), "_UseShadow"));
            l.Add(Col("shadow", "shadow", Loc.T("Shadow colour"), Loc.T("The colour of the shaded side."), "_ShadowColor", "_UseShadow"));
            if (!lite) l.Add(Col("shadow", "shadow2", Loc.T("2nd shadow colour"), Loc.T("The colour of the deepest shade."), "_Shadow2ndColor", "_UseShadow"));
            // Only the Outline shaders draw an outline: the plain lilToon ones declare the properties but have no pass for them.
            if (outline)
            {
                l.Add(ColHdrAlpha("outline", "outlineColour", Loc.T("Outline colour"), Loc.T("The colour of the outline."), "_OutlineColor"));
                l.Add(Rng("outline", "outlineWidth", Loc.T("Outline width"), Loc.T("How thick the outline is."), "_OutlineWidth", 0f, 1f));
            }
            if (lite) return;
            l.Add(Tog("metal", "reflection", Loc.T("Metal and gloss"), Loc.T("Turns on reflections."), "_UseReflection"));
            l.Add(Rng("metal", "metal", Loc.T("Metal"), Loc.T("How metallic the surface looks."), "_Metallic", 0f, 1f, "_UseReflection"));
            l.Add(Rng("metal", "smooth", Loc.T("Smoothness"), Loc.T("How glossy the surface is."), "_Smoothness", 0f, 1f, "_UseReflection"));
            l.Add(Tog("glitter", "glitter", Loc.T("Glitter"), Loc.T("Sparkling specks."), "_UseGlitter"));
            l.Add(ColHdrAlpha("glitter", "glitterColour", Loc.T("Glitter colour"), Loc.T("The colour of the sparkles."), "_GlitterColor", "_UseGlitter"));
        }

        /// <summary>A slider follows the range the shader itself declares, when it declares one.</summary>
        static void UseShaderRange(Material m, QControl c)
        {
            int i = m.shader.FindPropertyIndex(c.prop);
            if (i < 0 || m.shader.GetPropertyType(i) != ShaderPropertyType.Range) return;
            var r = m.shader.GetPropertyRangeLimits(i);
            c.min = r.x;
            c.max = r.y;
        }

        /// <summary>True when the control's switch is on, or it has none.</summary>
        internal static bool IsOn(Material m, QControl c) => c.toggle == null || !m.HasProperty(c.toggle) || m.GetFloat(c.toggle) > 0.5f;

        /// <summary>False when the control only applies to another style (for example a Poiyomi rim style).</summary>
        internal static bool Applies(Material m, QControl c) => c.gate == null || !m.HasProperty(c.gate) || InGate(Mathf.RoundToInt(m.GetFloat(c.gate)), c);

        static bool InGate(int v, QControl c) => v >= c.gateValue && v <= Mathf.Max(c.gateValue, c.gateMax);

        /// <summary>A locked Poiyomi material keeps only the settings marked Animated.</summary>
        internal static bool IsAnimated(Material m, QControl c) => m.GetTag(c.prop + "Animated", false, "") is string t && t != "" && t != "0";

        /// <summary>The value as shown: a Color, a bool (switch) or a float (slider, inverted for softness-style controls).</summary>
        internal static object Read(Material m, QControl c)
        {
            switch (c.kind)
            {
                case QKind.Toggle: return m.GetFloat(c.prop) > 0.5f;
                case QKind.Range:
                    float v = m.GetFloat(c.prop);
                    return c.invert ? c.min + c.max - v : v;
                default: return m.GetColor(c.prop);
            }
        }
    }
}
