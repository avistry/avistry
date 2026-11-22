using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Avistry.Textures
{
    internal static partial class AgentApi
    {
        internal enum FieldType { Float, Int, Bool, Colour, Enum }

        /// <summary>One layer setting the helper may change. Anything not listed here cannot be set through set_layer.</summary>
        internal class FieldSpec
        {
            public readonly string kind;          // the layer kind word
            public readonly Type layerType;
            public readonly string wire;          // the name the helper uses
            public readonly string field;         // the serialised field on the layer class
            public readonly FieldType type;
            public readonly float min, max;
            public readonly string[] choices;     // Enum: accepted words, in the enum's order

            internal FieldSpec(string kind, Type layerType, string wire, string field, FieldType type, float min = 0, float max = 0, string[] choices = null)
            {
                this.kind = kind; this.layerType = layerType; this.wire = wire; this.field = field;
                this.type = type; this.min = min; this.max = max; this.choices = choices;
            }

            internal FieldInfo Info => layerType.GetField(field, BindingFlags.Public | BindingFlags.Instance);
        }

        internal static readonly string[] ChromaWords = { "adjust", "add_colour", "set_colour" };

        /// <summary>Settable layer fields, per layer kind. The tests check every row against the class by reflection.</summary>
        internal static readonly IReadOnlyList<FieldSpec> Fields = new[]
        {
            new FieldSpec("hsl", typeof(HslLayer), "hue", nameof(HslLayer.hue), FieldType.Float, -180, 180),
            new FieldSpec("hsl", typeof(HslLayer), "saturation", nameof(HslLayer.saturation), FieldType.Float, -1, 1),
            new FieldSpec("hsl", typeof(HslLayer), "lightness", nameof(HslLayer.lightness), FieldType.Float, -1, 1),
            new FieldSpec("hsl", typeof(HslLayer), "chroma_mode", nameof(HslLayer.chromaMode), FieldType.Enum, choices: ChromaWords),
            new FieldSpec("hsl", typeof(HslLayer), "chroma", nameof(HslLayer.chroma), FieldType.Float, 0, 0.3f),
            new FieldSpec("hsl", typeof(HslLayer), "colour_hue", nameof(HslLayer.colourHue), FieldType.Float, 0, 360),
            new FieldSpec("hsl", typeof(HslLayer), "replace_hue", nameof(HslLayer.replaceHue), FieldType.Bool),
            new FieldSpec("levels", typeof(LevelsLayer), "in_black", nameof(LevelsLayer.inBlack), FieldType.Float, 0, 1),
            new FieldSpec("levels", typeof(LevelsLayer), "in_white", nameof(LevelsLayer.inWhite), FieldType.Float, 0, 1),
            new FieldSpec("levels", typeof(LevelsLayer), "gamma", nameof(LevelsLayer.gamma), FieldType.Float, 0.1f, 5),
            new FieldSpec("levels", typeof(LevelsLayer), "out_black", nameof(LevelsLayer.outBlack), FieldType.Float, 0, 1),
            new FieldSpec("levels", typeof(LevelsLayer), "out_white", nameof(LevelsLayer.outWhite), FieldType.Float, 0, 1),
            new FieldSpec("solid_colour", typeof(SolidColorLayer), "colour", nameof(SolidColorLayer.color), FieldType.Colour),
            new FieldSpec("brightness_contrast", typeof(BrightnessContrastLayer), "brightness", nameof(BrightnessContrastLayer.brightness), FieldType.Float, -1, 1),
            new FieldSpec("brightness_contrast", typeof(BrightnessContrastLayer), "contrast", nameof(BrightnessContrastLayer.contrast), FieldType.Float, -1, 1),
            new FieldSpec("colour_balance", typeof(ColorBalanceLayer), "keep_brightness", nameof(ColorBalanceLayer.keepBrightness), FieldType.Bool),
            new FieldSpec("posterize", typeof(PosterizeLayer), "levels", nameof(PosterizeLayer.levels), FieldType.Int, 2, 20),
            new FieldSpec("threshold", typeof(ThresholdLayer), "threshold", nameof(ThresholdLayer.threshold), FieldType.Float, 0, 1),
            new FieldSpec("root_tip_gradient", typeof(RootTipGradientLayer), "reverse", nameof(RootTipGradientLayer.reverse), FieldType.Bool),
            new FieldSpec("root_tip_gradient", typeof(RootTipGradientLayer), "start", nameof(RootTipGradientLayer.start), FieldType.Float, 0, 1),
            new FieldSpec("root_tip_gradient", typeof(RootTipGradientLayer), "end", nameof(RootTipGradientLayer.end), FieldType.Float, 0, 1),
        };

        /// <summary>Layer kinds the helper can add, with the class each one makes. Order is the order shown to the helper.</summary>
        static readonly (string kind, Type type)[] AddableKinds =
        {
            ("paint", typeof(PaintLayer)), ("hsl", typeof(HslLayer)), ("levels", typeof(LevelsLayer)),
            ("gradient_map", typeof(GradientMapLayer)), ("solid_colour", typeof(SolidColorLayer)),
            ("brightness_contrast", typeof(BrightnessContrastLayer)), ("colour_balance", typeof(ColorBalanceLayer)),
            ("posterize", typeof(PosterizeLayer)), ("invert", typeof(InvertLayer)), ("threshold", typeof(ThresholdLayer)),
            ("tone_curve", typeof(ToneCurveLayer)), ("root_tip_gradient", typeof(RootTipGradientLayer)),
        };

        /// <summary>Kinds the helper can read but not add.</summary>
        static readonly (string kind, Type type)[] ReadOnlyKinds =
        {
            ("gradient", typeof(DrawnGradientLayer)), ("image", typeof(ImageLayer)),
            ("colour_match", typeof(ColourMatchLayer)), ("recolour_part", typeof(RecolourPartLayer)),
        };

        internal static string KindWord(Layer l)
        {
            var t = l.GetType();
            foreach (var (kind, type) in AddableKinds) if (type == t) return kind;
            foreach (var (kind, type) in ReadOnlyKinds) if (type == t) return kind;
            return "other";
        }

        static string AddableKindList()
        {
            var words = new string[AddableKinds.Length];
            for (int i = 0; i < words.Length; i++) words[i] = AddableKinds[i].kind;
            return string.Join(", ", words);
        }

        static Layer NewLayer(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            foreach (var (k, type) in AddableKinds)
                if (string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)) return (Layer)Activator.CreateInstance(type);
            return null;
        }

        static string FieldWords(Layer l)
        {
            var words = new List<string>();
            foreach (var f in Fields) if (f.layerType == l.GetType()) words.Add(f.wire);
            return words.Count == 0 ? "none" : string.Join(", ", words);
        }

        static FieldSpec FindField(Layer l, string wire)
        {
            foreach (var f in Fields)
                if (f.layerType == l.GetType() && string.Equals(f.wire, wire, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /// <summary>The whitelisted fields of a layer as plain data.</summary>
        static Dictionary<string, object> FieldValues(Layer l)
        {
            var d = new Dictionary<string, object>();
            foreach (var f in Fields)
            {
                if (f.layerType != l.GetType()) continue;
                object v = f.Info.GetValue(l);
                switch (f.type)
                {
                    case FieldType.Colour: v = "#" + ColorUtility.ToHtmlStringRGB((Color)v); break;
                    case FieldType.Enum: v = f.choices[Mathf.Clamp((int)v, 0, f.choices.Length - 1)]; break;
                }
                d[f.wire] = v;
            }
            return d;
        }

        /// <summary>Checks one value against its spec. Returns the value to store, or null with a plain reason.</summary>
        static object ConvertField(FieldSpec f, object raw, out string problem)
        {
            problem = null;
            switch (f.type)
            {
                case FieldType.Bool:
                    if (raw is bool b) return b;
                    problem = $"“{f.wire}” is true or false.";
                    return null;
                case FieldType.Colour:
                    if (raw is string hex && TryColour(hex, out var c)) return c;
                    problem = $"“{f.wire}” is a colour like #RRGGBB.";
                    return null;
                case FieldType.Enum:
                    if (raw is string word)
                        for (int i = 0; i < f.choices.Length; i++)
                            if (string.Equals(f.choices[i], word, StringComparison.OrdinalIgnoreCase)) return i;
                    problem = $"“{f.wire}” is one of: {string.Join(", ", f.choices)}.";
                    return null;
                default:
                    if (!TryNumber(raw, out double n) || double.IsNaN(n) || n < f.min || n > f.max)
                    {
                        problem = $"“{f.wire}” is a number from {f.min.ToString(CultureInfo.InvariantCulture)} to {f.max.ToString(CultureInfo.InvariantCulture)}.";
                        return null;
                    }
                    return f.type == FieldType.Int ? (object)(int)Math.Round(n) : (float)n;
            }
        }

        static bool TryNumber(object raw, out double n)
        {
            switch (raw)
            {
                case int i: n = i; return true;
                case long l: n = l; return true;
                case float f: n = f; return true;
                case double d: n = d; return true;
                default: n = 0; return false;
            }
        }

        static readonly Regex s_hex = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        static bool TryColour(string hex, out Color c)
        {
            c = default;
            return hex != null && s_hex.IsMatch(hex) && ColorUtility.TryParseHtmlString(hex, out c);
        }

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static readonly string[] MaskWords = { "whole_texture", "selected_pieces" };

        static bool TryMaskMode(string word, out MaskMode mode)
        {
            mode = MaskMode.WholeTexture;
            if (word == null) return false;
            string w = word.Replace("_", "").Replace(" ", "").ToLowerInvariant();
            if (w == "wholetexture" || w == "whole") return true;
            if (w == "selectedislands" || w == "selectedpieces" || w == "pieces" || w == "selected") { mode = MaskMode.SelectedIslands; return true; }
            return false;
        }

        static Dictionary<string, BlendMode> s_blends;

        /// <summary>Accepts the enum name ("Multiply"), the menu name ("Multiply") or its full menu path ("Darken/Multiply"), any case.</summary>
        static bool TryBlend(string word, out BlendMode mode)
        {
            mode = BlendMode.Normal;
            if (string.IsNullOrEmpty(word)) return false;
            if (s_blends == null)
            {
                s_blends = new Dictionary<string, BlendMode>(StringComparer.OrdinalIgnoreCase);
                foreach (BlendMode m in Enum.GetValues(typeof(BlendMode)))
                {
                    s_blends[m.ToString()] = m;
                    var attr = typeof(BlendMode).GetField(m.ToString()).GetCustomAttribute<InspectorNameAttribute>();
                    if (attr == null) continue;
                    s_blends[attr.displayName] = m;
                    int slash = attr.displayName.LastIndexOf('/');
                    if (slash >= 0) s_blends[attr.displayName.Substring(slash + 1)] = m;
                }
            }
            return s_blends.TryGetValue(word.Trim(), out mode);
        }
    }
}
