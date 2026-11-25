using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace Avistry.Textures
{
    public enum BrushMode { Paint = 0, Erase = 1 }

    [Serializable]
    internal sealed class MirroredPaintPath
    {
        public float radius;
        public List<Vector4> points = new List<Vector4>();
        public List<Vector4> surfacePoints = new List<Vector4>();
        public List<int> pieces = new List<int>();
        public List<Vector2> tipAngles;
    }

    internal enum PaintFillScope { Legacy = 0, ClickedPiece = 1, Selection = 2, WholeTexture = 3 }

    [Serializable]
    internal sealed class PaintFillRegion
    {
        public PaintFillScope scope;
        public string layout;
        public bool limitPieces, invertPieces, limitFaces, invertFaces;
        public List<int> pieces = new List<int>();
        public List<int> faces = new List<int>();

        public bool UsesGeometry => limitPieces || limitFaces;
        public bool AllowsPiece(int piece) => !limitPieces || pieces.Contains(piece) != invertPieces;
        public bool AllowsFace(int face) => !limitFaces || faces.Contains(face) != invertFaces;
    }

    public enum StrokeTool
    {
        Brush = 0,
        [InspectorName("Blur (soften)")] Blur = 1,
        [InspectorName("Smudge (finger tip)")] Smudge = 2,
        [InspectorName("Colour mixing brush")] Mix = 3,
    }

    public enum BrushTip
    {
        Round = 0,
        Square = 1,
        [InspectorName("Bristle (streaks)")] Bristle = 2,
        [InspectorName("Spray (particles)")] Spray = 3,
        [InspectorName("Stamp (alpha shape)")] Stamp = 4,
        [InspectorName("Drips (gravity)")] Drips = 5,
    }

    public enum BristleLoad { Natural = 0, Dry = 1, Loaded = 2 }

    [Serializable]
    public class BrushShape
    {
        public BrushTip tip = BrushTip.Round;
        public string alphaId;
        public int alphaVersion;
        [Range(0, 1)] public float angleJitter;
        public int jitterVersion;
        [Range(0, 1)] public float hardness = 0.5f;
        [Tooltip("Distance between dabs as a fraction of the brush radius")]
        [Range(0.02f, 4f)] public float spacing = 0.12f;
        [Tooltip("Airbrush: paint builds up while you keep brushing over the same spot")]
        public bool buildUp;
        [Range(0.01f, 1f)] public float flow = 0.15f;
        [Tooltip("1 = round, lower = flatter (calligraphy / marker)")]
        [Range(0.05f, 1f)] public float roundness = 1f;
        [Range(-180f, 180f)] public float angle;
        [Tooltip("Rotate the tip with the stroke direction")]
        public bool followStroke;
        [Tooltip("Paper-like texture in the paint (chalk, crayon)")]
        [Range(0, 1)] public float grain;
        [Tooltip("Grain size in source-texture pixels")]
        [Range(1f, 64f)] public float grainSize = 6f;
        [Tooltip("Random offset of each dab, as a fraction of the radius")]
        [Range(0, 3)] public float scatter;
        [Range(0, 1)] public float sizeJitter;
        [Range(0, 1)] public float opacityJitter;
        [Tooltip("Bristles across the tip, or particles per dab for Spray")]
        [Range(1, 48)] public int density = 10;
        public BristleLoad bristleLoad;
        [Tooltip("Dot diameter in source-texture pixels. Zero scales dots with the brush.")]
        [Range(0, MaxDotSize)] public float dotSize;

        internal const float MaxDotSize = 256f;
        internal const float SprayDotSize = 8f, SplatterDotSize = 24f;
        internal bool HasDots => tip == BrushTip.Spray || (tip == BrushTip.Round || tip == BrushTip.Square) && scatter > 0f;

        [Tooltip("Colour variation: each stroke gets a slightly different colour (natural variation for hair strands, fabric, skin)")]
        public bool colorJitter;
        [Tooltip("How far the hue may move, in degrees either way")]
        [Range(0, 180)] public float jitterHue = 8f;
        [Tooltip("How much the saturation may change (either way)")]
        [Range(0, 1)] public float jitterSaturation = 0.1f;
        [Tooltip("How much the brightness may change (either way)")]
        [Range(0, 1)] public float jitterLuminosity = 0.1f;
        [Tooltip("Blend with second colour: up to this much of the second colour is mixed in, at random")]
        [Range(0, 1)] public float jitterSubBlend;

        public BrushShape Clone() => (BrushShape)MemberwiseClone();

        public bool SurfaceCapable => tip == BrushTip.Stamp ||
            (tip == BrushTip.Round || tip == BrushTip.Drips) && roundness > 0.95f && scatter <= 0f;
    }

    [Serializable]
    public class PaintStroke
    {
        public BrushMode mode;
        public Color color = Color.white;
        [Range(0, 1)] public float opacity = 1f;
        public float radius = 0.01f;
        public float fixedDotRadius;
        public bool pressureSize;
        public bool pressureOpacity;
        public int pressureModel;
        public BrushPenSettings pen;
        public BrushShape shape = new BrushShape();
        public int seed;
        [SerializeField] internal int bristleModel;
        public bool clip;
        public List<int> clipIslands = new List<int>();
        public List<Vector4> points = new List<Vector4>();
        public List<Vector2> tipAngles;
        [SerializeField] internal List<MirroredPaintPath> mirrorPaths;

        internal PaintStroke ForMirror(MirroredPaintPath path)
        {
            var copy = (PaintStroke)MemberwiseClone();
            copy.radius = path.radius;
            copy.points = path.points;
            copy.tipAngles = path.tipAngles;
            copy.pen = pen?.Clone();
            copy.surf = path.surfacePoints;
            copy.clipIslands = path.pieces;
            copy.mirrorPaths = null;
            return copy;
        }

        public bool lockAlpha;
        public bool isFill;
        public bool fillWholeIsland = true;
        [Range(0, 1)] public float fillTolerance = 0.15f;
        public float fillAreaScaling = 2f;
        [SerializeField] internal bool fillAllMatches;
        [SerializeField] internal int fillGap;
        [SerializeField] internal PaintFillRegion fillRegion, fillMirrorRegion;

        public StrokeTool tool = StrokeTool.Brush;
        public float blendSize = 12f;
        public bool blendKeepIsland = true;
        [Range(0, 1)] public float mixPaint = 0.5f;

        public bool IsBlend => tool != StrokeTool.Brush;

        public bool surface;
        public List<Vector4> surf = new List<Vector4>();
        public float surfRadius;
        public bool surfTwoSided;

        public bool IsSurface => surface && surf != null && surf.Count > 0 && surf.Count == points.Count;

        [SerializeField] internal bool limitPaintUV;
        [SerializeField] internal Vector4 paintUVBounds;
        public bool HasUvPaintLimit => limitPaintUV;
        public bool IncludesPaintUV(Vector2 uv) => !limitPaintUV ||
            uv.x >= paintUVBounds.x && uv.y >= paintUVBounds.y && uv.x <= paintUVBounds.z && uv.y <= paintUVBounds.w;
    }

    [Serializable, MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public class PaintLayer : Layer
    {
        public List<PaintStroke> strokes = new List<PaintStroke>();
        [Tooltip("Lock transparent pixels: new strokes only recolour what's already painted on this layer")]
        public bool lockTransparent;

        public PaintLayer() { name = "Paint"; }
        public override int Pass => 5;
        public override string TypeName => "Paint";
        public override void Apply(Material m, Func<Gradient, Texture2D> lut) { }
    }

    [Serializable]
    public class BrushSettings
    {
        internal const int MaxFillGap = 8;

        public BrushMode mode = BrushMode.Paint;
        public Color color = new Color(1f, 0.45f, 0.75f, 1f);
        [Tooltip("Second colour: X swaps it with the main colour")]
        public Color subColor = new Color(0.35f, 0.2f, 0.45f, 1f);
        public float size = 64f;
        [Range(0, 1)] public float opacity = 1f;
        public bool pressureSize = true;
        public bool pressureOpacity;
        public BrushPenSettings pen = new BrushPenSettings();
        [FormerlySerializedAs("stayInsideStrands")] public bool stayInsideIslands = true;
        [Tooltip("Smooth across joins: supported tips keep their size on the model and cross UV seams")]
        public bool surfaceBrush = true;
        public BrushShape shape = new BrushShape();
        public string presetName = "Soft round";
        public bool presetIsUser;
        public bool edited;

        public bool fillTool;
        public bool gradientTool;

        public bool blendTool;
        public StrokeTool blendKind = StrokeTool.Blur;

        [Tooltip("The brush mixes with the colours already there, like wet paint")]
        public bool colorMixing;
        [Tooltip("Amount of paint: 1 = only the brush colour, lower = more of the colour underneath")]
        [Range(0.05f, 1f)] public float amountOfPaint = 0.5f;
        [Tooltip("Colour stretch: how far the colours underneath are pulled together, as a share of the brush size")]
        [Range(0.1f, 2f)] public float colorStretch = 0.5f;
        [Tooltip("Blur: how soft, as a share of the brush size. Smudge: how far colour is dragged, as a share of the brush size.")]
        [Range(0.1f, 3f)] public float blendAmount = 0.6f;
        public bool blendKeepIsland = true;
        public bool fillWholeIsland = true;
        [SerializeField] internal PaintFillScope fillScope = PaintFillScope.ClickedPiece;
        [SerializeField] internal bool fillAllMatches;
        [SerializeField] internal int fillGap;
        [Range(0, 1)] public float fillTolerance = 0.15f;
        [Range(0, 16)] public float fillAreaScaling = 2f;

        public const float MinSize = 1f, MaxSize = 1024f;

        public PaintStroke NewStroke(int sourceWidth)
        {
            var s = MakeStroke(sourceWidth);
            if (mode != BrushMode.Paint || fillTool) return s;
            if (blendTool)
            {
                s.tool = blendKind == StrokeTool.Brush ? StrokeTool.Blur : blendKind;
                s.blendSize = Mathf.Max(1f, blendAmount * size);
                s.blendKeepIsland = blendKeepIsland;
                s.color = Color.white;
            }
            else
            {
                if (shape != null && shape.colorJitter) s.color = Jitter(s.color, subColor, shape, s.seed);
                if (colorMixing)
                {
                    s.tool = StrokeTool.Mix;
                    s.mixPaint = amountOfPaint;
                    s.blendSize = Mathf.Max(1f, colorStretch * size);
                    s.blendKeepIsland = true;
                }
            }
            return s;
        }

        public static Color Jitter(Color main, Color sub, BrushShape sh, int seed)
        {
            var rng = new BrushRng(seed, 911);
            var c = Color.Lerp(main, sub, rng.Next() * sh.jitterSubBlend);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + (rng.Next() * 2f - 1f) * sh.jitterHue / 360f, 1f);
            s = Mathf.Clamp01(s + (rng.Next() * 2f - 1f) * sh.jitterSaturation);
            v = Mathf.Clamp01(v + (rng.Next() * 2f - 1f) * sh.jitterLuminosity);
            var o = Color.HSVToRGB(h, s, v);
            o.a = 1f;
            return o;
        }

        PaintStroke MakeStroke(int sourceWidth) => new PaintStroke
        {
            mode = mode,
            color = new Color(color.r, color.g, color.b, 1f),
            opacity = opacity,
            radius = size * 0.5f / Mathf.Max(1, sourceWidth),
            fixedDotRadius = shape?.HasDots == true ? Mathf.Clamp(shape.dotSize, 0f, BrushShape.MaxDotSize) * 0.5f / Mathf.Max(1, sourceWidth) : 0f,
            pressureSize = pressureSize,
            pressureOpacity = pressureOpacity,
            pressureModel = 1,
            pen = (pen ?? new BrushPenSettings()).Clone(),
            tipAngles = !fillTool && pen != null && pen.direction != BrushPenDirection.Brush ? new List<Vector2>() : null,
            shape = (shape ?? new BrushShape()).Clone(),
            seed = Guid.NewGuid().GetHashCode(),
            bristleModel = !fillTool && shape?.tip == BrushTip.Bristle ? BrushRenderer.CoherentBristleModel : 0,
            clip = stayInsideIslands && !fillTool,
            isFill = fillTool,
            fillWholeIsland = fillWholeIsland,
            fillAllMatches = fillAllMatches,
            fillGap = fillTool && !fillWholeIsland && !fillAllMatches ? fillGap : 0,
            fillTolerance = fillTolerance,
            fillAreaScaling = fillAreaScaling,
        };
    }

    public enum GradientColours
    {
        [InspectorName("Brush colour → transparent")] BrushToTransparent = 0,
        [InspectorName("Brush colour → white")] BrushToWhite = 1,
        [InspectorName("Brush colour → black")] BrushToBlack = 2,
        [InspectorName("Custom gradient")] Custom = 3,
        [InspectorName("Main colour → second colour")] MainToSub = 4,
    }

    [Serializable]
    public class GradientToolSettings
    {
        public GradientColours colours = GradientColours.BrushToTransparent;
        public Gradient custom = Sets[2].make();
        public GradientShape shape = GradientShape.Linear;
        public GradientEdge edge = GradientEdge.NotRepeat;
        [Range(0.05f, 1f)] public float flatness = 0.5f;
        public bool dither = true;
        public BlendMode blend = BlendMode.Normal;

        public Gradient Build(Color main, Color sub) => colours == GradientColours.Custom ? Clone(custom) : FromColour(main, colours, sub);

        public static Gradient FromColour(Color c, GradientColours kind, Color sub = default)
        {
            c.a = 1;
            sub.a = 1;
            Color to = kind == GradientColours.BrushToWhite ? Color.white : kind == GradientColours.BrushToBlack ? Color.black
                     : kind == GradientColours.MainToSub ? sub : c;
            float endAlpha = kind == GradientColours.BrushToTransparent ? 0f : 1f;
            return Make(new[] { (c, 0f), (to, 1f) }, new[] { (1f, 0f), (endAlpha, 1f) });
        }

        public static Gradient Clone(Gradient g)
        {
            var n = new Gradient();
            if (g == null) return n;
            n.mode = g.mode;
            n.SetKeys(g.colorKeys, g.alphaKeys);
            return n;
        }

        static Gradient Make((Color c, float t)[] colours, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(Array.ConvertAll(colours, k => new GradientColorKey(k.c, k.t)), Array.ConvertAll(alphas, k => new GradientAlphaKey(k.a, k.t)));
            return g;
        }

        static (float, float)[] Opaque => new[] { (1f, 0f), (1f, 1f) };
        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

        public static readonly (string name, Func<Gradient> make)[] Sets =
        {
            ("Shading/Shadow (black → transparent)", () => Make(new[] { (Color.black, 0f), (Color.black, 1f) }, new[] { (0.85f, 0f), (0f, 1f) })),
            ("Shading/Highlight (white → transparent)", () => Make(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0f, 1f) })),
            ("Colour/Sunset", () => Make(new[] { (Hex("#2B1055"), 0f), (Hex("#7A2A8C"), 0.3f), (Hex("#E8516A"), 0.6f), (Hex("#FFB25B"), 0.85f), (Hex("#FFE7A8"), 1f) }, Opaque)),
            ("Colour/Ocean", () => Make(new[] { (Hex("#04163A"), 0f), (Hex("#0B4F8A"), 0.4f), (Hex("#2BB3C0"), 0.75f), (Hex("#B8F3EE"), 1f) }, Opaque)),
            ("Colour/Fire", () => Make(new[] { (Hex("#3A0A00"), 0f), (Hex("#C21E05"), 0.35f), (Hex("#FF7A00"), 0.65f), (Hex("#FFE45C"), 1f) }, Opaque)),
            ("Colour/Night sky", () => Make(new[] { (Hex("#05040F"), 0f), (Hex("#1B1747"), 0.5f), (Hex("#4B3C8F"), 0.8f), (Hex("#9B7BD8"), 1f) }, Opaque)),
            ("Colour/Rainbow", () => Make(new[] { (Hex("#FF3B3B"), 0f), (Hex("#FF9F1C"), 0.17f), (Hex("#FFE14D"), 0.33f), (Hex("#4CD964"), 0.5f), (Hex("#2FA8FF"), 0.67f), (Hex("#5B5BFF"), 0.83f), (Hex("#C04CFF"), 1f) }, Opaque)),
            ("Colour/Pastel rainbow", () => Make(new[] { (Hex("#FFB3BA"), 0f), (Hex("#FFDFBA"), 0.2f), (Hex("#FFFFBA"), 0.4f), (Hex("#BAFFC9"), 0.6f), (Hex("#BAE1FF"), 0.8f), (Hex("#D9BAFF"), 1f) }, Opaque)),
            ("Hair/Pink tips (transparent → pink)", () => Make(new[] { (Hex("#FF8FC8"), 0f), (Hex("#FF5FA8"), 1f) }, new[] { (0f, 0f), (0f, 0.3f), (1f, 1f) })),
            ("Hair/Blue tips (transparent → blue)", () => Make(new[] { (Hex("#7FB8FF"), 0f), (Hex("#3D7BFF"), 1f) }, new[] { (0f, 0f), (0f, 0.3f), (1f, 1f) })),
            ("Hair/Dark roots (dark → transparent)", () => Make(new[] { (Hex("#1A1420"), 0f), (Hex("#1A1420"), 1f) }, new[] { (0.9f, 0f), (0f, 1f) })),
        };
    }

    [Serializable]
    public class BrushPreset
    {
        public string name;
        public float size = 64f;
        public float opacity = 1f;
        public bool pressureSize = true;
        public bool pressureOpacity;
        public BrushPenSettings pen = new BrushPenSettings();
        public BrushShape shape = new BrushShape();
        public bool colorMixing;
        public float amountOfPaint = 0.5f;
        public float colorStretch = 0.5f;

        public void ApplyTo(BrushSettings b)
        {
            b.size = size;
            b.opacity = opacity;
            b.pressureSize = pressureSize;
            b.pressureOpacity = pressureOpacity;
            b.pen = (pen ?? new BrushPenSettings()).Clone();
            b.shape = (shape ?? new BrushShape()).Clone();
            b.colorMixing = colorMixing;
            b.amountOfPaint = amountOfPaint > 0f ? Mathf.Clamp(amountOfPaint, 0.05f, 1f) : 0.5f;
            b.colorStretch = colorStretch > 0f ? Mathf.Clamp(colorStretch, 0.1f, 2f) : 0.5f;
            b.presetName = name;
            b.edited = false;
        }

        public static BrushPreset From(BrushSettings b, string name) => new BrushPreset
        {
            name = name, size = b.size, opacity = b.opacity, pressureSize = b.pressureSize,
            pressureOpacity = b.pressureOpacity, shape = (b.shape ?? new BrushShape()).Clone(),
            pen = (b.pen ?? new BrushPenSettings()).Clone(),
            colorMixing = b.colorMixing, amountOfPaint = b.amountOfPaint, colorStretch = b.colorStretch,
        };
    }

    struct BrushRng
    {
        uint _s;

        public BrushRng(int seed, int index)
        {
            _s = Mix(unchecked((uint)seed * 0x9E3779B9u ^ (uint)index * 0x85EBCA6Bu)) | 1u;
        }

        static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352du;
                x ^= x >> 15; x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }

        public float Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s & 0xFFFFFF) / 16777216f;
        }

        public Vector2 InDisc()
        {
            float a = Next() * Mathf.PI * 2f, r = Mathf.Sqrt(Next());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
    }
}
