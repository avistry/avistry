using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>The recolour controls (relative to each material's starting values).</summary>
    [Serializable]
    public class RecolourSettings
    {
        [Range(-180, 180)] public float hue;          // degrees
        [Range(-1, 1)] public float saturation;       // -1 = grey, 0 = unchanged, +1 = double
        [Range(-1, 1)] public float brightness;       // -1 = black, 0 = unchanged, +1 = double
        public bool main = true, shadows = true, rim = true, matcap = true, outline = true, emission, other = true;
        [Tooltip("Only recolour the selected pieces, through the shader's colour-adjust mask")]
        public bool partial;

        public bool IsIdentity => Mathf.Abs(hue) < 0.01f && Mathf.Abs(saturation) < 0.001f && Mathf.Abs(brightness) < 0.001f;
    }

    /// <summary>One material's starting values, so the recolour is always applied relative to them and Reset is exact.</summary>
    [Serializable]
    public class RecolourEntry
    {
        public Material material;
        public ShaderFamily family;
        public List<string> colorNames = new List<string>();
        public List<Color> colorValues = new List<Color>();
        public List<string> floatNames = new List<string>();
        public List<float> floatValues = new List<float>();
        public List<string> vectorNames = new List<string>();
        public List<Vector4> vectorValues = new List<Vector4>();
        public bool colorGradingKeyword;
        public bool unlockedByUs;
        public bool oklab;   // which colour space this material's hue shift uses (Poiyomi: OKLab by default; lilToon: HSV)
        public List<string> textureNames = new List<string>();   // e.g. the colour-adjust mask (partial recolour replaces it)
        public List<Texture> textureValues = new List<Texture>();

        public Color BaseColor(string n) => colorValues[colorNames.IndexOf(n)];
        public float BaseFloat(string n, float fallback = 0) { int i = floatNames.IndexOf(n); return i >= 0 ? floatValues[i] : fallback; }
        public Vector4 BaseVector(string n, Vector4 fallback) { int i = vectorNames.IndexOf(n); return i >= 0 ? vectorValues[i] : fallback; }
    }

    /// <summary>
    /// Harmonised recolour: the main texture is shifted through the shader's own colour adjustment (lossless, no new PNG),
    /// and every tint colour (shadows, rim, matcap, outline, emission…) is shifted by the same amount so the look stays consistent.
    /// </summary>
    public static class Recolour
    {
        // Tint colours per group. Only properties that exist on the material are touched.
        static readonly Dictionary<string, string[]> PoiTints = new Dictionary<string, string[]>
        {
            { "shadows", new[] { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor", "_1st_ShadeColor", "_2nd_ShadeColor", "_LightingShadowColor", "_ShadowBorderColor" } },
            { "rim", new[] { "_RimLightColor", "_RimColor", "_RimIndirColor", "_Ap_RimLightColor", "_Rim2LightColor", "_Rim2Color", "_Rim2IndirColor", "_Ap_Rim2LightColor" } },
            { "matcap", new[] { "_MatcapColor", "_Matcap2Color", "_Matcap3Color", "_Matcap4Color" } },
            { "outline", new[] { "_LineColor" } },
            { "emission", new[] { "_EmissionColor", "_EmissionColor1", "_EmissionColor2", "_EmissionColor3", "_OutlineEmissionColor" } },
            { "other", new[] { "_BacklightColor", "_GlitterColor" } },
        };

        static readonly Dictionary<string, string[]> LilTints = new Dictionary<string, string[]>
        {
            // lilToon applies _Color after its HSVG adjustment, so the main tints must follow too.
            { "main", new[] { "_Color", "_Color2nd", "_Color3rd" } },
            { "shadows", new[] { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor", "_ShadowBorderColor" } },
            { "rim", new[] { "_RimColor", "_RimShadeColor" } },
            { "matcap", new[] { "_MatCapColor", "_MatCap2ndColor" } },
            { "outline", new[] { "_OutlineColor" } },
            { "emission", new[] { "_EmissionColor", "_Emission2ndColor" } },
            { "other", new[] { "_BacklightColor", "_ReflectionColor", "_GlitterColor" } },
        };

        static readonly string[] PoiMainFloats =
            { "_MainColorAdjustToggle", "_MainHueShiftToggle", "_MainHueShift", "_MainHueShiftColorSpace", "_MainHueShiftSelectOrShift", "_Saturation", "_MainBrightness" };

        /// <summary>
        /// Poiyomi's per-feature hue shift: shifts the colour coming from that feature's *texture* (emission map, matcap,
        /// rim, outline, glitter, decals) without baking. Tint colours alone can't do this when they're white.
        /// </summary>
        struct HueKnob
        {
            public string group, enabled, space, shift, selectOrShift;
            public HueKnob(string group, string enabled, string space, string shift, string selectOrShift = null)
            { this.group = group; this.enabled = enabled; this.space = space; this.shift = shift; this.selectOrShift = selectOrShift; }
        }

        static readonly HueKnob[] PoiHueKnobs = BuildPoiHueKnobs();

        static HueKnob[] BuildPoiHueKnobs()
        {
            var k = new List<HueKnob>();
            foreach (var n in new[] { "", "1", "2", "3" })
                k.Add(new HueKnob("emission", "_EmissionHueShiftEnabled" + n, "_EmissionHueShiftColorSpace" + n, "_EmissionHueShift" + n));
            k.Add(new HueKnob("matcap", "_MatcapHueShiftEnabled", "_MatcapHueShiftColorSpace", "_MatcapHueShift"));
            foreach (var n in new[] { "2", "3", "4" })
                k.Add(new HueKnob("matcap", $"_Matcap{n}HueShiftEnabled", $"_Matcap{n}HueShiftColorSpace", $"_Matcap{n}HueShift"));
            k.Add(new HueKnob("rim", "_RimHueShiftEnabled", "_RimHueShiftColorSpace", "_RimHueShift"));
            k.Add(new HueKnob("rim", "_Rim2HueShiftEnabled", "_Rim2HueShiftColorSpace", "_Rim2HueShift"));
            k.Add(new HueKnob("outline", "_OutlineHueShift", "_OutlineHueShiftColorSpace", "_OutlineHue"));
            k.Add(new HueKnob("other", "_GlitterHueShiftEnabled", "_GlitterHueShiftColorSpace", "_GlitterHueShift", "_GlitterHueSelectOrShift"));
            foreach (var n in new[] { "", "1", "2", "3" })
                k.Add(new HueKnob("main", "_DecalHueShiftEnabled" + n, "_DecalHueShiftColorSpace" + n, "_DecalHueShift" + n, "_DecalHueShiftSelectOrShift" + n));
            return k.ToArray();
        }

        /// <summary>True if this Poiyomi material can hue-shift the given group's textures losslessly.</summary>
        public static bool CanHueShiftGroup(Material m, string group) =>
            m != null && ShaderProfiles.Detect(m.shader) == ShaderFamily.Poiyomi && PoiHueKnobs.Any(k => k.group == group && m.HasProperty(k.shift) && m.HasProperty(k.enabled));

        const string PoiKeyword = "COLOR_GRADING_HDR";
        static readonly Vector4 DefaultHSVG = new Vector4(0, 1, 1, 1);

        static Dictionary<string, string[]> TintsFor(ShaderFamily f) =>
            f == ShaderFamily.Poiyomi ? PoiTints : f == ShaderFamily.LilToon ? LilTints : new Dictionary<string, string[]> { { "main", new[] { "_Color" } } };

        public static bool Supports(Material m) => m != null && m.shader != null && ShaderProfiles.Detect(m.shader) != ShaderFamily.Other;


        public static RecolourEntry Capture(Material m)
        {
            var e = new RecolourEntry { material = m, family = ShaderProfiles.Detect(m.shader) };
            foreach (var group in TintsFor(e.family).Values)
                foreach (var p in group)
                    if (m.HasProperty(p) && !e.colorNames.Contains(p)) { e.colorNames.Add(p); e.colorValues.Add(m.GetColor(p)); }
            if (e.family == ShaderFamily.Poiyomi)
            {
                foreach (var p in PoiMainFloats)
                    if (m.HasProperty(p)) { e.floatNames.Add(p); e.floatValues.Add(m.GetFloat(p)); }
                foreach (var k in PoiHueKnobs)
                    foreach (var p in new[] { k.enabled, k.space, k.shift, k.selectOrShift })
                        if (p != null && m.HasProperty(p) && !e.floatNames.Contains(p)) { e.floatNames.Add(p); e.floatValues.Add(m.GetFloat(p)); }
                // Global Theme colours: tints linked to a theme ignore their own value, so the theme gets shifted instead.
                for (int t = 0; t < 4; t++)
                {
                    string tp = "_GlobalThemeColor" + t;
                    if (m.HasProperty(tp) && !e.colorNames.Contains(tp)) { e.colorNames.Add(tp); e.colorValues.Add(m.GetColor(tp)); }
                }
                e.colorGradingKeyword = m.IsKeywordEnabled(PoiKeyword);
                // Poiyomi's hue shift defaults to (and recommends) OKLab; keep a material's own choice if hue shift is already in use.
                bool hueInUse = e.BaseFloat("_MainColorAdjustToggle") > 0.5f && e.BaseFloat("_MainHueShiftToggle") > 0.5f;
                e.oklab = !hueInUse || e.BaseFloat("_MainHueShiftColorSpace") < 0.5f;
            }
            else if (e.family == ShaderFamily.LilToon)
            {
                foreach (var p in new[] { "_MainTexHSVG", "_OutlineTexHSVG" })
                    if (m.HasProperty(p)) { e.vectorNames.Add(p); e.vectorValues.Add(m.GetVector(p)); }
            }

            // Partial recolour swaps in our island mask: remember the material's own mask (and its UV/replace settings).
            string maskProp = RecolourMask.MaskProperty(m);
            if (maskProp != null) { e.textureNames.Add(maskProp); e.textureValues.Add(m.GetTexture(maskProp)); }
            foreach (var p in new[] { "_MainColorAdjustTextureUV", "_MainHueShiftReplace" })
                if (m.HasProperty(p) && !e.floatNames.Contains(p)) { e.floatNames.Add(p); e.floatValues.Add(m.GetFloat(p)); }
            return e;
        }

        public static void Restore(RecolourEntry e)
        {
            var m = e.material;
            if (m == null) return;
            for (int i = 0; i < e.colorNames.Count; i++) if (m.HasProperty(e.colorNames[i])) m.SetColor(e.colorNames[i], e.colorValues[i]);
            for (int i = 0; i < e.floatNames.Count; i++) if (m.HasProperty(e.floatNames[i])) m.SetFloat(e.floatNames[i], e.floatValues[i]);
            for (int i = 0; i < e.vectorNames.Count; i++) if (m.HasProperty(e.vectorNames[i])) m.SetVector(e.vectorNames[i], e.vectorValues[i]);
            for (int i = 0; i < e.textureNames.Count; i++) if (m.HasProperty(e.textureNames[i])) m.SetTexture(e.textureNames[i], e.textureValues[i]);
            if (e.family == ShaderFamily.Poiyomi)
            {
                if (e.colorGradingKeyword) m.EnableKeyword(PoiKeyword); else m.DisableKeyword(PoiKeyword);
            }
        }


        /// <summary>
        /// Starting values + the recolour. Always computed from the captured baseline, so it's repeatable.
        /// With <paramref name="partialMask"/> (partial mode), only the main texture changes, inside the mask: tints and
        /// feature hue shifts would change everywhere, so they're left alone.
        /// </summary>
        public static void Apply(RecolourEntry e, RecolourSettings s, Texture partialMask = null)
        {
            var m = e.material;
            if (m == null) return;
            Restore(e);
            if (s.IsIdentity) return;

            if (s.partial)
            {
                string maskProp = RecolourMask.MaskProperty(m);
                if (partialMask == null || maskProp == null) return; // not eligible: leave this material unchanged
                m.SetTexture(maskProp, partialMask);
                if (e.family == ShaderFamily.Poiyomi)
                {
                    if (m.HasProperty("_MainColorAdjustTextureUV"))
                        m.SetFloat("_MainColorAdjustTextureUV", m.HasProperty("_MainTexUV") ? m.GetFloat("_MainTexUV") : 0); // same UVs as the main texture
                    if (m.HasProperty("_MainHueShiftReplace")) m.SetFloat("_MainHueShiftReplace", 1); // hue only where the mask is
                }
                ApplyMain(e, m, s);
                return;
            }

            var tints = TintsFor(e.family);
            var themes = new HashSet<int>();
            foreach (var kv in tints)
            {
                if (!GroupOn(kv.Key, s)) continue;
                foreach (var p in kv.Value)
                {
                    if (e.colorNames.Contains(p)) m.SetColor(p, Shift(e.BaseColor(p), s, e.oklab));
                    if (e.family == ShaderFamily.Poiyomi && ThemeOf(m, p) is int t && t >= 0) themes.Add(t);
                }
            }

            if (e.family == ShaderFamily.Poiyomi)
            {
                // A tint linked to a Global Theme shows the theme colour, not its own: shift the theme instead.
                // (Not the theme the main colour uses: the main Color Adjust already shifts that.)
                if (s.main && ThemeOf(m, "_Color") is int mainTheme && mainTheme >= 0) themes.Remove(mainTheme);
                foreach (int t in themes)
                {
                    string tp = "_GlobalThemeColor" + t;
                    if (e.colorNames.Contains(tp)) m.SetColor(tp, Shift(e.BaseColor(tp), s, e.oklab));
                }

                // Also turn the hue of each feature's own texture (emission map, matcap…), not just its tint.
                if (Mathf.Abs(s.hue) > 0.01f)
                {
                    foreach (var k in PoiHueKnobs)
                    {
                        if (!GroupOn(k.group, s) || !m.HasProperty(k.shift) || !m.HasProperty(k.enabled)) continue;
                        bool wasOn = e.BaseFloat(k.enabled) > 0.5f;
                        m.SetFloat(k.enabled, 1);
                        if (!wasOn)
                        {
                            // Turning it on fresh: OKLab (Poiyomi's recommended space) and "Shift" mode.
                            // If it was already on, keep the user's colour space and Hue Select/Shift choice.
                            if (k.space != null && m.HasProperty(k.space)) m.SetFloat(k.space, e.oklab ? 0 : 1);
                            if (k.selectOrShift != null && m.HasProperty(k.selectOrShift)) m.SetFloat(k.selectOrShift, 1);
                        }
                        float baseShift = wasOn ? e.BaseFloat(k.shift) : 0f;
                        m.SetFloat(k.shift, Mathf.Repeat(baseShift + s.hue / 360f, 1f));
                    }
                }
            }

            if (e.family == ShaderFamily.LilToon && s.outline) ShiftHSVG(e, m, "_OutlineTexHSVG", s);
            if (!s.main) return;
            ApplyMain(e, m, s);
        }

        /// <summary>The main texture through the shader's own colour adjustment (Poiyomi Color Adjust / lilToon HSVG).</summary>
        static void ApplyMain(RecolourEntry e, Material m, RecolourSettings s)
        {
            if (e.family == ShaderFamily.Poiyomi && m.HasProperty("_MainColorAdjustToggle"))
            {
                bool hueWasOn = e.BaseFloat("_MainColorAdjustToggle") > 0.5f && e.BaseFloat("_MainHueShiftToggle") > 0.5f;
                m.SetFloat("_MainColorAdjustToggle", 1);
                m.EnableKeyword(PoiKeyword);
                if (Mathf.Abs(s.hue) > 0.01f && m.HasProperty("_MainHueShift"))
                {
                    m.SetFloat("_MainHueShiftToggle", 1);
                    if (!hueWasOn)
                    {
                        // Fresh: Shift mode in OKLab (Poiyomi's default/recommendation). Already in use: keep the
                        // material's own colour space and Hue Select/Shift mode, so its look isn't changed under it.
                        if (m.HasProperty("_MainHueShiftSelectOrShift")) m.SetFloat("_MainHueShiftSelectOrShift", 1);
                        if (m.HasProperty("_MainHueShiftColorSpace")) m.SetFloat("_MainHueShiftColorSpace", e.oklab ? 0 : 1);
                    }
                    float baseHue = hueWasOn ? e.BaseFloat("_MainHueShift") : 0f;
                    m.SetFloat("_MainHueShift", Mathf.Repeat(baseHue + s.hue / 360f, 1f));
                }
                if (m.HasProperty("_Saturation")) m.SetFloat("_Saturation", Mathf.Clamp(e.BaseFloat("_Saturation") + s.saturation, -1f, 10f));
                if (m.HasProperty("_MainBrightness")) m.SetFloat("_MainBrightness", Mathf.Clamp(e.BaseFloat("_MainBrightness") + s.brightness, -1f, 2f));
            }
            else if (e.family == ShaderFamily.LilToon)
            {
                ShiftHSVG(e, m, "_MainTexHSVG", s);

            }
        }

        /// <summary>
        /// Why partial (masked) recolour can't be used on this material, or null if it can. A colour-adjust mask limits the
        /// material's *whole* colour adjustment, so an adjustment that's already on would disappear outside the selection.
        /// </summary>
        public static string PartialBlocker(RecolourEntry e)
        {
            var m = e?.material;
            if (m == null) return "Material missing.";
            if (RecolourMask.MaskProperty(m) == null) return "this shader version has no colour-adjust mask.";
            if (e.family == ShaderFamily.Poiyomi)
            {
                if (e.BaseFloat("_MainColorAdjustToggle") < 0.5f) return null;
                float hue = e.BaseFloat("_MainHueShift");
                bool hueOn = e.BaseFloat("_MainHueShiftToggle") > 0.5f && hue > 1e-3f && hue < 0.999f;
                bool other = Mathf.Abs(e.BaseFloat("_Saturation")) > 1e-3f || Mathf.Abs(e.BaseFloat("_MainBrightness")) > 1e-3f ||
                             (m.HasProperty("_MainGamma") && Mathf.Abs(m.GetFloat("_MainGamma") - 1f) > 1e-3f);
                if (hueOn || other)
                    return "it already has a whole-texture Color Adjust (hue/saturation/brightness). A selection mask would remove that outside the selection, so it's left as it is.";
            }
            else if (e.family == ShaderFamily.LilToon)
            {
                var v = e.BaseVector("_MainTexHSVG", DefaultHSVG);
                if ((v - DefaultHSVG).sqrMagnitude > 1e-6f)
                    return "it already has a whole-texture colour correction (Hue/Saturation/Value/Gamma). A selection mask would remove that outside the selection, so it's left as it is.";
                if (m.HasProperty("_MainGradationStrength") && m.GetFloat("_MainGradationStrength") > 0)
                    return "it uses a Gradation Map, which a selection mask would remove outside the selection, so it's left as it is.";
            }
            return null;
        }

        static void ShiftHSVG(RecolourEntry e, Material m, string prop, RecolourSettings s)
        {
            if (!m.HasProperty(prop)) return;
            var b = e.BaseVector(prop, DefaultHSVG);
            float h = b.x + s.hue / 360f;
            h = Mathf.Repeat(h + 0.5f, 1f) - 0.5f;                      // lilToon hue range is -0.5..0.5
            m.SetVector(prop, new Vector4(h,
                Mathf.Clamp(b.y * (1f + s.saturation), 0f, 2f),
                Mathf.Clamp(b.z * (1f + s.brightness), 0f, 2f),
                b.w));
        }

        static bool GroupOn(string group, RecolourSettings s) => group switch
        {
            "main" => s.main,
            "shadows" => s.shadows,
            "rim" => s.rim,
            "matcap" => s.matcap,
            "outline" => s.outline,
            "emission" => s.emission,
            _ => s.other,
        };

        /// <summary>Global Theme (0–3) a Poiyomi colour property is linked to, or -1 (unlinked, or an AudioLink/ColorChord theme).</summary>
        public static int ThemeOf(Material m, string colorProp)
        {
            string p = colorProp + "ThemeIndex";
            if (m == null || !m.HasProperty(p)) return -1;
            int v = Mathf.RoundToInt(m.GetFloat(p));
            return v >= 1 && v <= 4 ? v - 1 : -1;
        }

        /// <summary>Colour properties driven by AudioLink / ColorChord themes (ThemeIndex 5–12): a recolour can't change them.</summary>
        public static int AudioLinkDrivenCount(Material m)
        {
            int n = 0;
            if (m == null || ShaderProfiles.Detect(m.shader) != ShaderFamily.Poiyomi) return 0;
            foreach (var group in PoiTints.Values)
                foreach (var p in group)
                    if (m.HasProperty(p + "ThemeIndex") && Mathf.RoundToInt(m.GetFloat(p + "ThemeIndex")) >= 5) n++;
            return n;
        }

        /// <summary>Theme colours (0–3) that tints on this material are linked to.</summary>
        public static List<int> LinkedThemes(Material m)
        {
            var set = new SortedSet<int>();
            if (m == null || ShaderProfiles.Detect(m.shader) != ShaderFamily.Poiyomi) return set.ToList();
            foreach (var group in PoiTints.Values)
                foreach (var p in group)
                    if (ThemeOf(m, p) is int t && t >= 0) set.Add(t);
            return set.ToList();
        }

        /// <summary>Hue rotate / saturation / brightness, in OKLab (Poiyomi's recommended space) or HSV, keeping alpha and HDR.</summary>
        public static Color Shift(Color c, RecolourSettings s, bool oklab) => oklab ? ShiftOklab(c, s) : Shift(c, s);

        /// <summary>Hue rotate / saturation / brightness in HSV, keeping alpha and HDR intensity.</summary>
        public static Color Shift(Color c, RecolourSettings s)
        {
            float a = c.a;
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            h = Mathf.Repeat(h + s.hue / 360f, 1f);
            sat = Mathf.Clamp01(sat * (1f + s.saturation));
            v = Mathf.Max(0f, v * (1f + s.brightness));
            var r = Color.HSVToRGB(h, sat, v, true);
            r.a = a;
            return r;
        }

        // OKLab: perceptual hue, so brightness stays even while rotating.

        static float ToLinear(float c) { c = Mathf.Max(0f, c); return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f); }
        static float ToGamma(float c) { c = Mathf.Max(0f, c); return c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f; }
        static float Cbrt(float x) => x < 0 ? -Mathf.Pow(-x, 1f / 3f) : Mathf.Pow(x, 1f / 3f);

        /// <summary>sRGB (gamma) colour -> OKLab (L, a, b).</summary>
        public static Vector3 ToOklab(Color c)
        {
            float r = ToLinear(c.r), g = ToLinear(c.g), b = ToLinear(c.b);
            float l = Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        /// <summary>OKLab -> sRGB (gamma) colour (out-of-gamut values clamped at 0, HDR allowed above 1).</summary>
        public static Color FromOklab(Vector3 lab, float alpha)
        {
            float l = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            float r = 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            float g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            float b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;
            return new Color(ToGamma(r), ToGamma(g), ToGamma(b), alpha);
        }

        /// <summary>Hue rotation and chroma scale in OKLCh (like Poiyomi's OKLab hue shift); brightness scales the result.</summary>
        public static Color ShiftOklab(Color c, RecolourSettings s)
        {
            var lab = ToOklab(c);
            float ang = s.hue * Mathf.Deg2Rad, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float chroma = Mathf.Max(0f, 1f + s.saturation);
            float a2 = (lab.y * ca - lab.z * sa) * chroma;
            float b2 = (lab.y * sa + lab.z * ca) * chroma;
            var o = FromOklab(new Vector3(lab.x, a2, b2), c.a);
            float k = Mathf.Max(0f, 1f + s.brightness);
            return new Color(o.r * k, o.g * k, o.b * k, c.a);
        }

        // Tint colours white or grey and keeps the shading.

        /// <summary>The tint colour's OKLCh chroma (times strength) and hue (0..1 turns), as the "Recolour part" shader pass takes them.</summary>
        public static void TintParams(Color tint, float strength, out float chroma, out float hueTurns)
        {
            var lab = ToOklab(tint);
            chroma = Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z) * Mathf.Clamp01(strength);
            hueTurns = Mathf.Repeat(Mathf.Atan2(lab.z, lab.y) / (2f * Mathf.PI), 1f);
        }

        static bool InGamut(Vector3 lab)
        {
            float l = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            float r = 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            float g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            float b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;
            return r >= 0 && r <= 1 && g >= 0 && g <= 1 && b >= 0 && b <= 1;
        }

        /// <summary>
        /// Gives a colour the tint's hue and chroma (times strength) while keeping its own lightness (+ brightness), like the
        /// shader pass. Chroma fades out near black and white so shadows and highlights stay natural; the result is kept
        /// inside the sRGB gamut by lowering the chroma (same search as the HSL layer's Add / Set colour).
        /// </summary>
        public static Color Tint(Color c, Color tint, float strength, float brightness)
        {
            TintParams(tint, strength, out float chroma, out float hueTurns);
            float L = Mathf.Clamp01(ToOklab(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b))).x);
            float a = Mathf.Clamp(brightness, -1f, 1f);
            L = a >= 0 ? L + (1 - L) * a : L * (1 + a);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.2f, L)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.93f, 1f, L)));
            float ang = hueTurns * 2f * Mathf.PI, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float lo = 0, hi = Mathf.Max(chroma * fade, 0f);
            for (int k = 0; k < 10; k++)
            {
                if (InGamut(new Vector3(L, ca * hi, sa * hi))) break;
                float mid = (lo + hi) * 0.5f;
                if (InGamut(new Vector3(L, ca * mid, sa * mid))) lo = mid; else hi = mid;
            }
            if (!InGamut(new Vector3(L, ca * hi, sa * hi))) hi = lo;
            var o = FromOklab(new Vector3(L, ca * hi, sa * hi), c.a);
            return new Color(Mathf.Clamp01(o.r), Mathf.Clamp01(o.g), Mathf.Clamp01(o.b), c.a);
        }

        /// <summary>Average OKLCh chroma of some colours (0 = grey; real colours are roughly 0.05 and up).</summary>
        public static float AverageChroma(IList<Color> px)
        {
            if (px == null || px.Count == 0) return 0f;
            double sum = 0;
            foreach (var c in px) { var lab = ToOklab(c); sum += Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z); }
            return (float)(sum / px.Count);
        }

        /// <summary>
        /// Sets a material's main tint (lilToon / Poiyomi `_Color`) for a recolour session. `_Color` joins the captured
        /// starting values the first time (Poiyomi's isn't shifted by the hue sliders), so Restore / Reset put it back.
        /// </summary>
        public static bool SetMainTint(RecolourEntry e, Color tint)
        {
            var m = e.material;
            if (m == null || !m.HasProperty("_Color")) return false;
            if (!e.colorNames.Contains("_Color")) { e.colorNames.Add("_Color"); e.colorValues.Add(m.GetColor("_Color")); }
            if (e.family == ShaderFamily.LilToon && m.HasProperty("_BaseColor") && !e.colorNames.Contains("_BaseColor"))
            { e.colorNames.Add("_BaseColor"); e.colorValues.Add(m.GetColor("_BaseColor")); } // so Reset puts it back too
            var baseCol = e.BaseColor("_Color");
            m.SetColor("_Color", new Color(tint.r, tint.g, tint.b, baseCol.a));
            if (e.family == ShaderFamily.LilToon && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_Color")); // lilToon's own rule, live
            return true;
        }


        /// <summary>Plain-language note on where this material's shadow colour comes from, and whether recolour moves it.</summary>
        public static string ShadowNote(Material m)
        {
            if (m == null) return null;
            var f = ShaderProfiles.Detect(m.shader);
            if (f == ShaderFamily.LilToon)
            {
                bool tex = m.HasProperty("_ShadowColorTex") && m.GetTexture("_ShadowColorTex") != null;
                return tex ? Loc.T("lilToon shadows are the main colour times the shadow colours, so they follow the recolour; the shadow colour texture itself doesn't change.")
                           : Loc.T("lilToon shadows are the main colour times the shadow colours, so they follow the recolour.");
            }
            if (f != ShaderFamily.Poiyomi || !m.HasProperty("_LightingMode")) return null;
            switch (Mathf.RoundToInt(m.GetFloat("_LightingMode")))
            {
                case 0: return Loc.T("Lighting Type: Texture Ramp. Shadow colour mostly comes from the ramp texture, which doesn't change; only the Shadow Tint follows.");
                case 1:
                {
                    bool tex = m.HasProperty("_ShadowColorTex") && m.GetTexture("_ShadowColorTex") != null;
                    return tex ? Loc.T("Lighting Type: Multilayer Math. Shadow colours 1–3 and the border colour follow; the Shadow Color texture doesn't.")
                               : Loc.T("Lighting Type: Multilayer Math. Shadow colours 1–3 and the border colour follow.");
                }
                case 4:
                {
                    bool maps = (m.HasProperty("_1st_ShadeMap") && m.GetTexture("_1st_ShadeMap") != null) || (m.HasProperty("_2nd_ShadeMap") && m.GetTexture("_2nd_ShadeMap") != null);
                    return maps ? Loc.T("Lighting Type: ShadeMap. 1st/2nd ShadeColor follow; the ShadeMap textures don't.")
                                : Loc.T("Lighting Type: ShadeMap. 1st/2nd ShadeColor follow.");
                }
                case 3: return Loc.T("Lighting Type: Skin. The Shadow Tint follows; the skin colour comes from a lookup texture that doesn't change.");
                case 2: case 6: case 8: return Loc.T("Shadows use the Shadow Tint, which follows the recolour.");
                default: return Loc.T("This Lighting Type (Flat / Cloth / PBR) has no separate shadow colour.");
            }
        }


        /// <summary>
        /// The main texture's dominant colour (saturation-weighted average hue, times the main tint), for the
        /// before/after swatch and for "shift towards a colour".
        /// </summary>
        public static bool DominantColour(Material m, out Color dominant)
        {
            dominant = Color.gray;
            if (m == null || !m.HasProperty("_MainTex")) return false;
            var tex = m.GetTexture("_MainTex");
            if (tex == null) return false;
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(64, 64, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false);
                read.Apply(false);
                var px = read.GetPixels32();
                UnityEngine.Object.DestroyImmediate(read);

                double sx = 0, sy = 0, ws = 0, ssum = 0, vsum = 0, wsum = 0;
                foreach (var p in px)
                {
                    float alpha = p.a / 255f;
                    if (alpha < 0.1f) continue;
                    Color.RGBToHSV(p, out float h, out float s, out float v);
                    float w = alpha * (s * v + 0.02f);
                    sx += Math.Cos(h * Math.PI * 2) * w;
                    sy += Math.Sin(h * Math.PI * 2) * w;
                    ws += w;
                    ssum += s * alpha; vsum += v * alpha; wsum += alpha;
                }
                if (wsum <= 0) return false;
                float hue = (float)(Math.Atan2(sy, sx) / (Math.PI * 2));
                if (hue < 0) hue += 1;
                dominant = Color.HSVToRGB(hue, (float)(ssum / wsum), (float)(vsum / wsum));
                if (m.HasProperty("_Color")) dominant *= m.GetColor("_Color");
                dominant.a = 1;
                return true;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>The main texture (times the main tint) downscaled to size×size, as displayable gamma colours.</summary>
        public static Color[] SampleMainTexture(Material m, int size)
        {
            if (m == null || !m.HasProperty("_MainTex")) return null;
            return SampleTexture(m.GetTexture("_MainTex"), size, m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
        }

        /// <summary>Any texture downscaled to size×size (gamma colours), multiplied by a tint.</summary>
        public static Color[] SampleTexture(Texture tex, int size, Color tint)
        {
            if (tex == null) return null;
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                read.Apply(false);
                var px = read.GetPixels();
                UnityEngine.Object.DestroyImmediate(read);
                if (tint != Color.white)
                    for (int i = 0; i < px.Length; i++) { var a = px[i].a; px[i] *= tint; px[i].a = a; }
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// The texture's main colours (up to <paramref name="max"/>), weighted so small vivid areas (e.g. coloured tips)
        /// still show next to large dark/grey areas. <paramref name="colourful"/> = share of visible pixels with real colour.
        /// </summary>
        public static List<Color> Palette(Color[] px, int max, out float colourful)
        {
            colourful = 0;
            var result = new List<Color>();
            if (px == null || px.Length == 0) return result;
            const int bins = 12;
            var w = new float[bins + 3];          // 12 hue bins + dark / mid / light neutrals
            var sum = new Color[bins + 3];
            float visible = 0, coloured = 0;
            foreach (var c in px)
            {
                if (c.a < 0.1f) continue;
                visible += c.a;
                Color.RGBToHSV(c, out float h, out float s, out float v);
                int bin;
                float weight;
                if (s > 0.15f && v > 0.12f)
                {
                    bin = Mathf.Min(bins - 1, (int)(h * bins));
                    weight = c.a * (0.3f + s * v * 2f); // vivid colours count more than their area
                    coloured += c.a;
                }
                else
                {
                    bin = bins + (v < 0.35f ? 0 : v < 0.7f ? 1 : 2);
                    weight = c.a;
                }
                w[bin] += weight;
                sum[bin] += c * weight;
            }
            if (visible <= 0) return result;
            colourful = coloured / visible;
            float total = w.Sum();
            foreach (int i in Enumerable.Range(0, w.Length).OrderByDescending(i => w[i]))
            {
                if (w[i] < total * 0.02f || result.Count >= max) break;
                var avg = sum[i] / w[i];
                avg.a = 1;
                result.Add(avg);
            }
            return result;
        }

        /// <summary>Applies the recolour to sampled pixels (approximate: HSV, like the tint colours) for the preview.</summary>
        public static Color[] ShiftPixels(Color[] px, RecolourSettings s, bool oklab)
        {
            if (px == null) return null;
            var o = new Color[px.Length];
            for (int i = 0; i < px.Length; i++) o[i] = s.main ? Shift(px[i], s, oklab) : px[i];
            return o;
        }

        /// <summary>Hue/saturation needed to turn <paramref name="from"/> into <paramref name="to"/>.</summary>
        public static void Match(Color from, Color to, RecolourSettings s, bool matchBrightness, bool oklab)
        {
            if (oklab)
            {
                // Match in OKLCh, the space the shader rotates in.
                var a = ToOklab(from);
                var b = ToOklab(to);
                float c0 = Mathf.Sqrt(a.y * a.y + a.z * a.z), c1 = Mathf.Sqrt(b.y * b.y + b.z * b.z);
                float dLab = (Mathf.Atan2(b.z, b.y) - Mathf.Atan2(a.z, a.y)) * Mathf.Rad2Deg;
                s.hue = Mathf.Repeat(dLab + 180f, 360f) - 180f;
                s.saturation = c0 > 0.01f ? Mathf.Clamp(c1 / c0 - 1f, -1f, 1f) : 0f;
                if (matchBrightness)
                {
                    Color.RGBToHSV(from, out _, out _, out float va);
                    Color.RGBToHSV(to, out _, out _, out float vb);
                    s.brightness = va > 0.02f ? Mathf.Clamp(vb / va - 1f, -1f, 1f) : 0f;
                }
                return;
            }
            Color.RGBToHSV(from, out float h0, out float s0, out float v0);
            Color.RGBToHSV(to, out float h1, out float s1, out float v1);
            float dh = (h1 - h0) * 360f;
            dh = Mathf.Repeat(dh + 180f, 360f) - 180f;
            s.hue = dh;
            s.saturation = s0 > 0.02f ? Mathf.Clamp(s1 / s0 - 1f, -1f, 1f) : 0f;
            if (matchBrightness) s.brightness = v0 > 0.02f ? Mathf.Clamp(v1 / v0 - 1f, -1f, 1f) : 0f;
        }

        /// <summary>One place a material's visible colour comes from (a colour texture of an active feature).</summary>
        public class ColourSource
        {
            public string label;      // "Emission · White_Hair_ats"
            public string group;      // recolour group that controls it ("main", "emission", …)
            public Color swatch;      // its most colourful main colour
            public float colourful;   // share of its pixels with real colour
            public bool canShift;     // a recolour moves it (main adjust, or a Poiyomi per-feature hue shift)
        }

        public static string GroupOf(string prop)
        {
            if (prop == "_MainTex" || prop.StartsWith("_Main2nd") || prop.StartsWith("_Main3rd") || prop.StartsWith("_Decal")) return "main";
            if (prop.StartsWith("_Emission")) return "emission";
            if (prop.StartsWith("_Matcap") || prop.StartsWith("_MatCap")) return "matcap";
            if (prop.StartsWith("_Rim")) return "rim";
            if (prop.StartsWith("_Outline")) return "outline";
            if (prop.StartsWith("_Shadow") || prop.Contains("_ShadeMap")) return "shadows";
            return "other";
        }

        /// <summary>
        /// Where the material's colour actually comes from: every colour texture of an active feature, how colourful it is,
        /// and whether recolour can shift it. Explains "I moved Hue and nothing happened" (grey main texture, colour in emission).
        /// </summary>
        public static List<ColourSource> ColourSources(Material m)
        {
            var list = new List<ColourSource>();
            if (m == null) return list;
            var family = ShaderProfiles.Detect(m.shader);
            foreach (var slot in TargetResolver.GetTextureSlots(m))
            {
                if (slot.role != TextureRole.Color || slot.featureOff) continue;
                string prop = slot.properties[0];
                string group = slot.properties.Contains("_MainTex") ? "main" : GroupOf(prop);
                var px = SampleTexture(slot.texture, 48, Color.white);
                var pal = Palette(px, 5, out float colourful);
                Color swatch = pal.Count > 0 ? pal[0] : Color.gray;
                foreach (var c in pal)
                {
                    Color.RGBToHSV(c, out _, out float sat, out float v);
                    if (sat > 0.15f && v > 0.12f) { swatch = c; break; }
                }
                bool canShift = group == "main" ? family != ShaderFamily.Other : CanHueShiftGroup(m, group);
                list.Add(new ColourSource { label = $"{slot.displayName} · {slot.texture.name}", group = group, swatch = swatch, colourful = colourful, canShift = canShift });
            }
            return list;
        }
    }
}
