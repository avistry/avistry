using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Avistry.Textures
{
    public enum BlendMode
    {
        [InspectorName("Normal")] Normal = 0,
        [InspectorName("Darken/Multiply")] Multiply = 1,
        [InspectorName("Lighten/Screen")] Screen = 2,
        [InspectorName("Contrast/Overlay")] Overlay = 3,
        [InspectorName("Contrast/Soft light")] SoftLight = 4,
        [InspectorName("Lighten/Add")] Add = 5,
        [InspectorName("Compare/Subtract")] Subtract = 6,
        [InspectorName("Colour/Color")] Color = 7,
        [InspectorName("Colour/Hue")] Hue = 8,
        [InspectorName("Colour/Saturation")] Saturation = 9,
        [InspectorName("Colour/Luminosity")] Luminosity = 10,
        [InspectorName("Darken/Darken")] Darken = 11,
        [InspectorName("Lighten/Lighten")] Lighten = 12,
        [InspectorName("Darken/Colour burn")] ColorBurn = 13,
        [InspectorName("Darken/Linear burn")] LinearBurn = 14,
        [InspectorName("Lighten/Colour dodge")] ColorDodge = 15,
        [InspectorName("Lighten/Glow dodge")] GlowDodge = 16,
        [InspectorName("Lighten/Add (Glow)")] AddGlow = 17,
        [InspectorName("Contrast/Hard light")] HardLight = 18,
        [InspectorName("Contrast/Vivid light")] VividLight = 19,
        [InspectorName("Contrast/Linear light")] LinearLight = 20,
        [InspectorName("Contrast/Pin light")] PinLight = 21,
        [InspectorName("Contrast/Hard mix")] HardMix = 22,
        [InspectorName("Compare/Difference")] Difference = 23,
        [InspectorName("Compare/Exclusion")] Exclusion = 24,
        [InspectorName("Compare/Divide")] Divide = 25,
        [InspectorName("Darken/Darker colour")] DarkerColor = 26,
        [InspectorName("Lighten/Lighter colour")] LighterColor = 27,
    }

    public enum MaskMode { WholeTexture = 0, SelectedIslands = 1, SelectedFaces = 2 }

    [Serializable]
    internal struct LayerBlendPass
    {
        public bool enabled;
        public BlendMode mode;
        [Range(0, 1)] public float strength;
    }

    [Serializable]
    public abstract class Layer
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name;
        public bool enabled = true;
        public bool expanded = true;
        [SerializeField, HideInInspector] internal string groupId;
        [SerializeField, HideInInspector] internal string groupName;
        [Range(0, 1)] public float opacity = 1f;
        public BlendMode blend = BlendMode.Normal;
        [SerializeField, HideInInspector] internal List<LayerBlendPass> additionalBlends = new List<LayerBlendPass>();
        public MaskMode maskMode = MaskMode.WholeTexture;
        public List<int> islands = new List<int>();
        public bool invertMask;
        [HideInInspector] public List<int> faces = new List<int>();
        [HideInInspector] public string faceLayout;

        [Tooltip("Clip to layer below: this layer only shows where the nearest unclipped layer below it shows")]
        public bool clipToBelow;
        [Tooltip("Which channels of the texture this layer writes (for masks that pack several things into R, G, B and A)")]
        public bool chR = true, chG = true, chB = true;
        [Tooltip("Also write the alpha channel (as the brightness of the paint)")]
        public bool chA;
        [Tooltip("A mask painted with the brush: hides parts of this layer without deleting anything")]
        public bool hasPaintMask;
        [Tooltip("The mask started as 'hide everything' (paint to reveal). Flipping this inverts the mask.")]
        public bool paintMaskStartsHidden;
        public List<PaintStroke> maskStrokes = new List<PaintStroke>();

        public abstract int Pass { get; }
        public abstract string TypeName { get; }
        public virtual bool NeedsGradientTexture => false;
        public virtual GradientMode GradientModeUsed => GradientMode.PerStrandUV;

        internal const int MaxAdditionalBlends = 4;

        internal Vector4 AdditionalBlendStrengths(out Vector4 modes)
        {
            modes = Vector4.zero;
            var strengths = Vector4.zero;
            int count = Mathf.Min(additionalBlends?.Count ?? 0, MaxAdditionalBlends);
            for (int index = 0; index < count; index++)
            {
                var step = additionalBlends[index];
                if (!step.enabled || !float.IsFinite(step.strength) || step.strength <= 0f) continue;
                modes[index] = (int)step.mode;
                strengths[index] = Mathf.Clamp01(step.strength);
            }
            return strengths;
        }

        internal bool HasActiveAdditionalBlends
        {
            get
            {
                var strength = AdditionalBlendStrengths(out _);
                return strength.x + strength.y + strength.z + strength.w > 0f;
            }
        }

        public abstract void Apply(Material mat, Func<Gradient, Texture2D> lut);

        protected static Vector4 Gamma(Color c) => new Vector4(c.r, c.g, c.b, c.a);
    }

    public enum ChromaMode { Scale = 0, Add = 1, Set = 2 }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class HslLayer : Layer
    {
        [Range(-180, 180)] public float hue;
        [Range(-1, 1)] public float saturation;
        [Range(-1, 1)] public float lightness;
        [Tooltip("Adjust = change the saturation you already have. Add colour / Set colour = work in a perceptual colour space, so white and grey areas can be given a colour too")]
        public ChromaMode chromaMode = ChromaMode.Scale;
        [Tooltip("How much colour to add (Add colour) or to give every pixel (Set colour)")]
        [Range(0, 0.3f)] public float chroma = 0.08f;
        [Tooltip("The colour (hue, in degrees) given to pixels that have no colour of their own, such as white or grey")]
        [Range(0, 360)] public float colourHue = 20f;
        [Tooltip("Use that hue for every pixel, not only the greys")]
        public bool replaceHue;

        public HslLayer() { name = "Hue / Saturation / Lightness"; }
        public override int Pass => 0;
        public override string TypeName => "Hue/Sat/Light";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetVector("_Params0", new Vector4(hue / 360f, saturation, lightness, 0));
            m.SetVector("_Params1", new Vector4((float)chromaMode, chroma, colourHue / 360f, replaceHue ? 1 : 0));
        }
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class LevelsLayer : Layer
    {
        [Range(0, 1)] public float inBlack = 0f;
        [Range(0, 1)] public float inWhite = 1f;
        [Range(0.1f, 5f)] public float gamma = 1f;
        [Range(0, 1)] public float outBlack = 0f;
        [Range(0, 1)] public float outWhite = 1f;

        public LevelsLayer() { name = "Levels"; }
        public override int Pass => 1;
        public override string TypeName => "Levels";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetVector("_Params0", new Vector4(inBlack, inWhite, gamma, 0));
            m.SetVector("_Params1", new Vector4(outBlack, outWhite, 0, 0));
        }
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class GradientMapLayer : Layer
    {
        public Gradient gradient = DefaultGradient();

        public GradientMapLayer() { name = "Gradient Map (recolour)"; }
        public override int Pass => 2;
        public override string TypeName => "Gradient Map";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetTexture("_LUT", lut(gradient));
        }

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.10f, 0.04f, 0.16f), 0f), new GradientColorKey(new Color(0.55f, 0.30f, 0.85f), 0.55f), new GradientColorKey(new Color(0.95f, 0.85f, 1f), 1f) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class SolidColorLayer : Layer
    {
        public Color color = new Color(1f, 0.55f, 0.75f, 1f);

        public SolidColorLayer() { name = "Solid Colour"; blend = BlendMode.Color; }
        public override int Pass => 3;
        public override string TypeName => "Solid Colour";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetVector("_Color0", Gamma(color));
        }
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class RootTipGradientLayer : Layer
    {
        public Gradient gradient = DefaultGradient();
        public GradientMode mode = GradientMode.PerStrandUV;
        public bool reverse;
        [Range(0, 1)] public float start = 0f;
        [Range(0, 1)] public float end = 1f;

        public RootTipGradientLayer() { name = "Ombré Gradient"; blend = BlendMode.Color; }
        public override int Pass => 4;
        public override string TypeName => "Ombré Gradient";
        public override bool NeedsGradientTexture => mode != GradientMode.UvV;
        public override GradientMode GradientModeUsed => mode;
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetTexture("_LUT", lut(gradient));
            m.SetVector("_Params0", new Vector4(start, end, reverse ? 1 : 0, mode == GradientMode.UvV ? 1 : 0));
        }

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.45f, 0.7f), 0f), new GradientColorKey(new Color(0.55f, 0.35f, 1f), 1f) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0, 0.35f), new GradientAlphaKey(1, 1) });
            return g;
        }
    }


    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class BrightnessContrastLayer : Layer
    {
        [Range(-1, 1)] public float brightness;
        [Range(-1, 1)] public float contrast;

        public BrightnessContrastLayer() { name = "Brightness / Contrast"; }
        public override int Pass => 6;
        public override string TypeName => "Brightness/Contrast";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut) =>
            m.SetVector("_Params0", new Vector4(brightness, contrast, 0, 0));
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class ColorBalanceLayer : Layer
    {
        [Tooltip("Cyan ← → Red, Magenta ← → Green, Yellow ← → Blue, for the dark tones")]
        public Vector3 shadows;
        [Tooltip("Cyan ← → Red, Magenta ← → Green, Yellow ← → Blue, for the middle tones")]
        public Vector3 midtones;
        [Tooltip("Cyan ← → Red, Magenta ← → Green, Yellow ← → Blue, for the bright tones")]
        public Vector3 highlights;
        [Tooltip("Keep brightness: only the colour shifts, not how light or dark it is")]
        public bool keepBrightness = true;

        public ColorBalanceLayer() { name = "Colour Balance"; }
        public override int Pass => 7;
        public override string TypeName => "Colour Balance";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetVector("_Params0", new Vector4(shadows.x, shadows.y, shadows.z, keepBrightness ? 1 : 0));
            m.SetVector("_Params1", new Vector4(midtones.x, midtones.y, midtones.z, 0));
            m.SetVector("_Color0", new Vector4(highlights.x, highlights.y, highlights.z, 0));
        }
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class PosterizeLayer : Layer
    {
        [Range(2, 20)] public int levels = 6;

        public PosterizeLayer() { name = "Posterization"; }
        public override int Pass => 8;
        public override string TypeName => "Posterization";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut) =>
            m.SetVector("_Params0", new Vector4(Mathf.Max(2, levels), 0, 0, 0));
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class InvertLayer : Layer
    {
        public InvertLayer() { name = "Invert colours"; }
        public override int Pass => 9;
        public override string TypeName => "Invert colours";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut) { }
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class ThresholdLayer : Layer
    {
        [Range(0, 1)] public float threshold = 0.5f;

        public ThresholdLayer() { name = "Binarization"; }
        public override int Pass => 10;
        public override string TypeName => "Binarization";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut) =>
            m.SetVector("_Params0", new Vector4(threshold, 0, 0, 0));
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class ToneCurveLayer : Layer
    {
        [Tooltip("RGB: input brightness (left→right) to output brightness (bottom→top)")]
        public AnimationCurve rgb = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve red = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve green = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve blue = AnimationCurve.Linear(0, 0, 1, 1);

        public ToneCurveLayer() { name = "Tone Curve"; }
        public override int Pass => 11;
        public override string TypeName => "Tone Curve";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut) { }

        public Color32[] BuildLut()
        {
            var px = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                float t = rgb.Evaluate(i / 255f);
                px[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(red.Evaluate(Mathf.Clamp01(t))) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(green.Evaluate(Mathf.Clamp01(t))) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(blue.Evaluate(Mathf.Clamp01(t))) * 255f),
                    255);
            }
            return px;
        }
    }


    public enum GradientShape { Linear = 0, Circle = 1, Ellipse = 2 }

    public enum GradientEdge
    {
        [InspectorName("Not repeat (extend the end colours)")] NotRepeat = 0,
        [InspectorName("Repeat")] Repeat = 1,
        [InspectorName("Reverse repeat (back and forth)")] ReverseRepeat = 2,
        [InspectorName("Don't draw past the ends")] DontDraw = 3,
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class DrawnGradientLayer : Layer
    {
        [Tooltip("Left = colour at the start point, right = colour at the end point. Transparent keys leave the texture untouched.")]
        public Gradient gradient = GradientToolSettings.FromColour(new Color(1f, 0.45f, 0.75f), GradientColours.BrushToTransparent);
        public GradientShape shape = GradientShape.Linear;
        [Tooltip("Edge process: what happens past the start and end points")]
        public GradientEdge edge = GradientEdge.NotRepeat;
        [Tooltip("Ellipse only: 1 = circle, lower = flatter (across the drag direction)")]
        [Range(0.05f, 1f)] public float flatness = 0.5f;
        [Tooltip("Adds invisible noise so smooth gradients don't show stepped bands after saving")]
        public bool dither = true;
        [Tooltip("Start point (texture position, 0..1). Easier: drag the dots on the texture with the Gradient tool.")]
        public Vector2 start = new Vector2(0.5f, 1f);
        [Tooltip("End point (texture position, 0..1)")]
        public Vector2 end = new Vector2(0.5f, 0f);

        [HideInInspector] public bool sceneProjection;
        [HideInInspector] public Vector3 sceneStart, sceneEnd, sceneNormal;
        [HideInInspector] public Vector3 sceneAxis, sceneAcross;

        public DrawnGradientLayer() { name = "Gradient"; }
        public override int Pass => 12;
        public override string TypeName => "Gradient";
        internal DrawnGradientLayer CopyPlacement() => (DrawnGradientLayer)MemberwiseClone();
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetTexture("_LUT", lut(gradient));
            m.SetVector("_Params0", new Vector4(start.x, start.y, end.x, end.y));
            m.SetVector("_Params1", new Vector4((int)shape, (int)edge, flatness, dither ? 1 : 0));
        }
    }


    public enum ImageCopy
    {
        [InspectorName("No copy")] None = 0,
        [InspectorName("Mirror onto the other half")] Mirror = 1,
        [InspectorName("Copy onto the other half")] Copy = 2,
        [InspectorName("Mirror onto the other half (flipped picture)")] MirrorFlipped = 3,
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class ImageLayer : Layer
    {
        [Tooltip("The picture to place (a PNG with transparency works best)")]
        public Texture2D image;
        [Tooltip("Multiplies the picture's colour; its alpha fades the picture")]
        public Color tint = Color.white;
        [Tooltip("Where the centre of the picture goes on the texture (0..1 from the left / bottom)")]
        public Vector2 position = new Vector2(0.5f, 0.5f);
        [Tooltip("Width and height as a share of the texture (1 = the whole texture). Negative flips it.")]
        public Vector2 size = new Vector2(0.25f, 0.25f);
        [Tooltip("Degrees")]
        [Range(-180f, 180f)] public float rotation;
        [Tooltip("Repeat the picture across the whole texture instead of showing it once")]
        public bool tiled;
        [Tooltip("Also show it on the other half of the texture (for mirrored layouts)")]
        public ImageCopy copy = ImageCopy.None;
        [Tooltip("Optional mask: the picture only shows where this texture is white")]
        public Texture2D mask;
        [Tooltip("Which channel of the mask to use (0 = R, 1 = G, 2 = B, 3 = A)")]
        [Range(0, 3)] public int maskChannel;

        [SerializeField, HideInInspector] internal string missingPackImage;
        [SerializeField, HideInInspector] internal string missingPackMask;
        internal bool NeedsPackMask => mask == null && !string.IsNullOrEmpty(missingPackMask);

        [HideInInspector] public List<string> offFloats = new List<string>();
        [HideInInspector] public List<string> offKeywords = new List<string>();
        [HideInInspector] public string flattenedFrom;
        [HideInInspector] public Vector4 imageST = new Vector4(1, 1, 0, 0);
        [HideInInspector] public string generated;

        public ImageLayer() { name = "Image"; }
        internal ImageLayer CopyPlacement() => (ImageLayer)MemberwiseClone();
        public override int Pass => 13;
        public override string TypeName => "Image";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetVector("_Params0", new Vector4(position.x, position.y, size.x, size.y));
            m.SetVector("_Params1", new Vector4(rotation * Mathf.Deg2Rad, tiled ? 1 : 0, (int)copy, mask != null ? maskChannel : -1));
            m.SetVector("_Color0", Gamma(tint));
            if (this is ImageLayer il) m.SetVector("_ImageST", il.imageST);
        }
    }


    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class ColourMatchLayer : Layer
    {
        [Tooltip("The material whose colours to match (only used to find the colours)")]
        public Material source;
        [Tooltip("Or a texture whose colours to match (any texture in the project)")]
        public Texture2D sourceTexture;
        [Tooltip("Match to the texture above instead of the material")]
        public bool useTexture;

        public UnityEngine.Object SourceObject => useTexture ? (UnityEngine.Object)sourceTexture : source;
        public bool HasSource => SourceObject != null;
        public string SourceName => HasSource ? SourceObject.name : "";
        [Tooltip("This texture's main colours (gamma)")]
        public List<Color> from = new List<Color>();
        [Tooltip("The colour each one becomes (gamma)")]
        public List<Color> to = new List<Color>();
        [Tooltip("How many main colours to look for on each texture")]
        [Range(2, 8)] public int colourCount = 4;
        [SerializeField, HideInInspector] internal ColourMatch.Pairing pairing;
        [Tooltip("How far each match reaches into similar colours. Higher = smoother, lower = more separate")]
        [Range(0.02f, 0.4f)] public float softness = 0.12f;
        [Tooltip("How much of the other material's brightness to take (0 = keep this texture's light and dark)")]
        [Range(0, 1)] public float brightness = 0.7f;
        [Tooltip("How much of the other material's colour to take")]
        [Range(0, 1.5f)] public float colourAmount = 1f;
        [Tooltip("Leave colours that are far from every matched colour (eyes, small details) as they are")]
        public bool keepOtherColours = true;
        [Tooltip("Pure white and pure black stay as they are; colours in between move. Keeps highlights and shadows crisp.")]
        public bool keepWhitesAndBlacks = true;

        static readonly Vector4[] s_from = new Vector4[8], s_to = new Vector4[8];

        public ColourMatchLayer() { name = "Colour Match"; }
        public override int Pass => 14;
        public override string TypeName => "Colour Match";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            int n = Mathf.Min(8, Mathf.Min(from.Count, to.Count));
            for (int i = 0; i < 8; i++)
            {
                s_from[i] = i < n ? (Vector4)Recolour.ToOklab(from[i]) : Vector4.zero;
                s_to[i] = i < n ? (Vector4)Recolour.ToOklab(to[i]) : Vector4.zero;
            }
            m.SetVectorArray("_MatchFrom", s_from);
            m.SetVectorArray("_MatchTo", s_to);
            m.SetVector("_Params0", new Vector4(n, softness, brightness, colourAmount));
            m.SetVector("_Params1", new Vector4(keepOtherColours ? 1 : 0, keepWhitesAndBlacks ? 1 : 0, 0, 0));
        }
    }


    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class RecolourPartLayer : Layer
    {
        [Tooltip("Turn every colour of this part around the colour wheel (degrees)")]
        [Range(-180, 180)] public float hue;
        [Tooltip("-1 grey · 0 unchanged · +1 twice as vivid")]
        [Range(-1, 1)] public float saturation;
        [Tooltip("-1 black · 0 unchanged · +1 twice as bright")]
        [Range(-1, 1)] public float brightness;
        [Tooltip("Turn the hue in OKLab (colours stay evenly bright, like Poiyomi) instead of HSV (like lilToon)")]
        public bool oklab;
        [HideInInspector] public Color swatch = new Color(0.6f, 0.6f, 0.6f, 1f);

        public enum PartMode { Shift, Tint }
        [Tooltip("Rotate colours: turn every colour around the colour wheel. Tint: give the part one colour, keeping its light and shadow (works on white and grey).")]
        public PartMode partMode;
        [Tooltip("The colour the part is given in Tint mode")]
        public Color tintColour = new Color(1f, 0.19f, 0.38f, 1f);
        [Tooltip("How strongly the tint colours the part (Tint mode)")]
        [Range(0, 1)] public float tintStrength = 1f;
        [HideInInspector] public bool modeChosen;
        [HideInInspector] public bool autoTinted;

        public RecolourPartLayer() { name = "Part"; }
        public override int Pass => 15;
        public override string TypeName => "Recolour part";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut)
        {
            m.SetVector("_Params0", new Vector4(hue / 360f, saturation, brightness, oklab ? 1 : 0));
            if (partMode == PartMode.Tint)
            {
                Recolour.TintParams(tintColour, tintStrength, out float chroma, out float hueTurns);
                m.SetVector("_Params1", new Vector4(1, chroma, hueTurns, 0));
            }
            else m.SetVector("_Params1", Vector4.zero);
        }

        public RecolourSettings Settings => new RecolourSettings { hue = hue, saturation = saturation, brightness = brightness, main = true };
        public bool IsTint => partMode == PartMode.Tint;
        public bool IsIdentity => IsTint ? (tintStrength < 0.001f && Mathf.Abs(brightness) < 0.001f) : Settings.IsIdentity;

        public Color Preview(Color c) => IsTint ? Recolour.Tint(c, tintColour, tintStrength, brightness) : Recolour.Shift(c, Settings, oklab);
    }
}
