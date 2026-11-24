using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Avistry.Textures
{
    internal static class BrushStampLibrary
    {
        internal const int Version = 1;
        const uint Seed = 0x7C0A;
        const float Pi = MathF.PI, Tau = Pi * 2, Aa = 3f / 512;
        internal sealed class Definition
        {
            internal readonly string Id, Label, Category;
            internal readonly int ProceduralKind;
            internal Definition(string id, string label, string category, int procedural = 0)
            { Id = id; Label = label; Category = category; ProceduralKind = procedural; }
        }
        static readonly string[] Glyphs =
        {
            "060110304146 0343", "06003041423303 3344453606", "4130100105163645", "00304145360600",
            "40000646 0333", "400006 0333", "41301001051636454323", "0006 4046 0343", "1030 2026 1636",
            "4045361605", "0006 400346", "000646", "0600234046", "06004640", "103041453616050110",
            "06003041423303", "103041453616050110 3346", "06003041423303 2346", "413010010213334445361605",
            "0040 2026", "000516364540", "002640", "0016233640", "0046 4006", "002340 2326", "00400646",
            "103041453616050110 0541", "112026 1636", "01103041420646", "01103041423313 334445361605",
            "300343 3036", "400002324345361605", "413010010516364543321203", "00404116",
            "130201103041423313 130405163645443313", "4313020110304145361605"
        };
        static readonly Definition[] Definitions = BuildDefinitions();
        static readonly Dictionary<string, Definition> ById = Index();
        internal static IReadOnlyList<Definition> All { get; } = Array.AsReadOnly(Definitions);

        static Definition[] BuildDefinitions()
        {
            var items = new List<Definition>();
            string[] procedural = { "round", "square", "spray", "grunge", "cloud", "star", "streaks" };
            string[] labels = { "Round", "Square", "Textured spray", "Grunge", "Cloud", "Star", "Dry streaks" };
            for (int i = 0; i < procedural.Length; i++) items.Add(new Definition("procedural_" + procedural[i], labels[i], "Procedural", i + 1));
            void Add(string id, string label, string category) => items.Add(new Definition(id, label, category));
            Add("round_soft", "Soft round", "Basic"); Add("round_hard", "Hard round", "Basic"); Add("square", "Square", "Basic");
            Add("rectangle", "Rectangle", "Basic"); Add("triangle", "Triangle", "Basic"); Add("hexagon", "Hexagon", "Basic");
            Add("ring", "Ring", "Basic"); Add("cross", "Cross", "Basic"); Add("star_5", "Star", "Basic");
            Add("star_4", "Sparkle", "Basic"); Add("star_6", "Star 6", "Basic"); Add("arrow", "Arrow", "Basic");
            Add("dots_grid", "Dots grid", "Basic"); Add("scratches", "Scratches", "Grunge"); Add("chips", "Chips", "Grunge");
            Add("splatter", "Splatter", "Grunge"); Add("sponge", "Sponge", "Grunge"); Add("grunge_spots", "Grunge spots", "Grunge");
            Add("cracks", "Cracks", "Grunge"); Add("speckle", "Speckle", "Grunge"); Add("fur", "Fur strands", "Organic");
            Add("leaf", "Leaf", "Organic"); Add("wood_knot", "Wood knot", "Organic"); Add("rivet", "Rivet", "Hard surface");
            Add("bolt_head", "Bolt head", "Hard surface"); Add("screw_slot", "Screw slot", "Hard surface");
            Add("screw_cross", "Screw cross", "Hard surface"); Add("stitches", "Stitches", "Hard surface");
            Add("vent", "Vent slots", "Hard surface"); Add("fabric", "Fabric stamp", "Hard surface"); Add("bricks", "Bricks stamp", "Hard surface");
            const string characters = "abcdefghijklmnopqrstuvwxyz0123456789";
            foreach (char c in characters) Add("letter_" + c, "Stencil " + char.ToUpperInvariant(c), "Stencil letters");
            return items.ToArray();
        }

        static Dictionary<string, Definition> Index()
        {
            var result = new Dictionary<string, Definition>(StringComparer.Ordinal);
            foreach (var item in Definitions) result.Add(item.Id, item);
            return result;
        }

        internal static bool TryFind(string id, out Definition definition)
        {
            definition = null;
            return id != null && ById.TryGetValue(id, out definition);
        }

        internal static bool IsValid(string id, int version) => version == Version && TryFind(id, out _);

        // Alpha shapes, stencil data and noise formulas are adapted from TextureCraft (MIT); see Third Party Notices.md.
        internal static byte[] Generate(string id, int size, bool parallel = true)
            => GenerateCore(id, size, parallel, default);

        internal static byte[] GenerateForWarm(string id, int size, CancellationToken cancellation)
            => GenerateCore(id, size, false, cancellation);

        static byte[] GenerateCore(string id, int size, bool parallel, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (size < 1 || size > 512 || !TryFind(id, out var definition) || definition.ProceduralKind != 0)
                throw new ArgumentException("Unknown generated brush alpha or unsupported resolution.");
            var sample = Make(id);
            var pixels = new byte[size * size];
            void Row(int y)
            {
                cancellation.ThrowIfCancellationRequested();
                float sy = (y + .5f) / size * 2 - 1;
                for (int x = 0; x < size; x++)
                {
                    float value = sample((x + .5f) / size * 2 - 1, sy);
                    pixels[y * size + x] = float.IsFinite(value) ? (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255) : (byte)0;
                }
            }
            if (parallel && size >= 256)
                Parallel.For(0, size, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, Environment.ProcessorCount / 2)) }, Row);
            else for (int y = 0; y < size; y++) Row(y);
            return pixels;
        }

        static float Fill(float d) => Math.Clamp(.5f - d / Aa, 0f, 1f);
        static float Len(float x, float y) => MathF.Sqrt(x * x + y * y);
        static float Smooth(float a, float b, float value)
        { float t = Math.Clamp((value - a) / MathF.Max(b - a, 1e-6f), 0f, 1f); return t * t * (3 - 2 * t); }
        static float Fade(float x, float y) => 1 - Smooth(.55f, .97f, Len(x, y));
        static float Mod(float value, float divisor) => value - MathF.Floor(value / divisor) * divisor;
        static float Rand(int item, int channel) => BrushStampNoise.Hash(item, channel, 0, Seed);
        static float Fbm(float x, float y, float z, int octaves) => BrushStampNoise.Fbm(x, y, z, octaves, Seed);
        static (float d, float t) Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / MathF.Max(dx * dx + dy * dy, 1e-12f), 0f, 1f);
            return (Len(x - ax - dx * t, y - ay - dy * t), t);
        }
        static float Ngon(float x, float y, int count, float apothem, float phase)
        {
            float best = float.MinValue;
            for (int k = 0; k < count; k++)
            { float t = phase + k * Tau / count; best = MathF.Max(best, x * MathF.Cos(t) + y * MathF.Sin(t)); }
            return best - apothem;
        }
        static float Star(float x, float y, int count, float inner, float outer, float curve)
        {
            float t = MathF.Abs(Mod(MathF.Atan2(x, y) / (Tau / count), 1) - .5f) * 2;
            return Fill(Len(x, y) - (inner + (outer - inner) * MathF.Pow(t, curve)));
        }
        static float Dome(float r, float radius)
        { float q = r / radius; return .35f * Fill(r - radius) + .65f * MathF.Sqrt(MathF.Max(1 - q * q, 0)); }

        static Func<float, float, float> Make(string id)
        {
            switch (id)
            {
                case "round_soft": return (x, y) => MathF.Pow(MathF.Max(1 - x * x - y * y, 0), 2);
                case "round_hard": return (x, y) => Fill(Len(x, y) - .95f);
                case "square": return (x, y) => Fill(MathF.Max(MathF.Abs(x), MathF.Abs(y)) - .9f);
                case "rectangle": return (x, y) => Fill(MathF.Max(MathF.Abs(x) - .95f, MathF.Abs(y) - .45f));
                case "triangle": return (x, y) => Fill(Ngon(x, y + .24f, 3, .475f, -Pi / 2));
                case "hexagon": return (x, y) => Fill(Ngon(x, y, 6, .82f, 0));
                case "ring": return (x, y) => Fill(MathF.Abs(Len(x, y) - .72f) - .2f);
                case "cross": return (x, y) => Fill(MathF.Min(MathF.Max(MathF.Abs(x) - .22f, MathF.Abs(y) - .92f),
                    MathF.Max(MathF.Abs(x) - .92f, MathF.Abs(y) - .22f)));
                case "star_5": return (x, y) => Star(x, y + .06f, 5, .4f, .97f, 1);
                case "star_4": return (x, y) => Star(x, y, 4, .1f, .97f, 3);
                case "star_6": return (x, y) => Star(x, y, 6, .55f, .95f, 1);
                case "arrow": return (x, y) => Fill(MathF.Min(MathF.Max(MathF.Max(MathF.Abs(x) - .16f, y - .15f), -.9f - y),
                    Ngon(x, y - .35f, 3, .25f, -Pi / 2)));
                case "dots_grid": return (x, y) => Fill(Len(x - (Math.Clamp(MathF.Floor((x + .9f) / .45f), 0, 3) * .45f - .675f),
                    y - (Math.Clamp(MathF.Floor((y + .9f) / .45f), 0, 3) * .45f - .675f)) - .14f);
                case "scratches": return Scratches();
                case "chips": return (x, y) =>
                {
                    float w = Fbm(x * 3, y * 3, 1.7f, 3) - .5f;
                    var cell = BrushStampNoise.Cellular(x * 3.5f + w * .8f, y * 3.5f + w * .8f, .5f, Seed);
                    if (cell.id >= .45f) return 0;
                    float rough = BrushStampNoise.Fbm(x * 14, y * 14, 2.3f, 3, Seed + 1, true);
                    return Fill(.1f + .12f * rough - (cell.second - cell.first)) * Fade(x, y);
                };
                case "splatter": return Splatter();
                case "sponge": return (x, y) =>
                {
                    float holes = BrushStampNoise.Cellular(x * 7, y * 7, .3f, Seed).first;
                    float fine = BrushStampNoise.Cellular(x * 15, y * 15, 1.1f, Seed + 3).first;
                    float body = Fill(Len(x, y) - .75f - .4f * (Fbm(x * 2, y * 2, .8f, 3) - .5f));
                    return Math.Clamp((holes - .28f) * 5, 0f, 1f) * Math.Clamp((fine - .2f) * 4, 0f, 1f) * body;
                };
                case "grunge_spots": return (x, y) => MathF.Min(Math.Clamp((Fbm(x * 2.6f, y * 2.6f, 3.1f, 5) - .55f) * 7, 0f, 1f) +
                    Math.Clamp((BrushStampNoise.Value(x * 30, y * 30, .5f, Seed) - .86f) * 8, 0f, .6f), 1) * Fade(x, y);
                case "cracks": return (x, y) =>
                {
                    float w = Fbm(x * 2, y * 2, 5, 3) - .5f;
                    var cell = BrushStampNoise.Cellular(x * 2.6f + w, y * 2.6f + w * .7f, .2f, Seed + 5);
                    float breakup = Math.Clamp((BrushStampNoise.Fbm(x * 4, y * 4, 7, 3, Seed + 2, true) - .35f) * 4, 0f, 1f);
                    return (1 - Smooth(.015f, .045f, cell.second - cell.first)) * breakup * Fade(x, y);
                };
                case "speckle": return (x, y) => Math.Clamp((BrushStampNoise.Value(x * 40, y * 40, 0, Seed) - .78f) * 10, 0f, 1f) * Fade(x, y);
                case "fur": return Fur();
                case "leaf": return Leaf();
                case "wood_knot": return (x, y) =>
                {
                    float d = Len(x / .55f, y) + (Fbm(x * 2.5f, y * 2.5f, 9, 3) - .5f) * .25f;
                    return MathF.Max((.5f + .5f * MathF.Cos(d * 30)) * .7f, 1 - Smooth(.12f, .22f, d)) * (1 - Smooth(.7f, .95f, d));
                };
                case "rivet": return (x, y) => Dome(Len(x, y), .7f);
                case "bolt_head": return (x, y) => MathF.Max(MathF.Max(Fill(Len(x, y) - .95f) * .4f,
                    Fill(Ngon(x, y, 6, .7f, 0)) * .75f), Fill(Len(x, y) - .6f));
                case "screw_slot": return (x, y) => Dome(Len(x, y), .9f) * (1 - Fill(MathF.Max(MathF.Abs(y) - .08f, MathF.Abs(x) - .7f)));
                case "screw_cross": return (x, y) => Dome(Len(x, y), .9f) * (1 - Fill(MathF.Min(
                    MathF.Max(MathF.Abs(y) - .08f, MathF.Abs(x) - .5f), MathF.Max(MathF.Abs(x) - .08f, MathF.Abs(y) - .5f))));
                case "stitches": return (x, y) =>
                {
                    float best = 0;
                    for (int i = 0; i < 6; i++) { float cx = -.75f + i * .3f; best = MathF.Max(best, Fill(Segment(x, y, cx - .07f, -.2f, cx + .07f, .2f).d - .05f)); }
                    return best;
                };
                case "vent": return (x, y) =>
                {
                    float best = 0;
                    for (int i = 0; i < 5; i++) { float cy = -.6f + i * .3f; best = MathF.Max(best, Fill(Segment(x, y, -.7f, cy, .7f, cy).d - .07f)); }
                    return best;
                };
                case "fabric": return (x, y) =>
                {
                    float u = x * 7, v = y * 7;
                    bool over = (((int)MathF.Floor(u) + (int)MathF.Floor(v)) & 1) == 0;
                    return MathF.Sin(Pi * Mod(over ? v : u, 1)) * Fill(MathF.Max(MathF.Abs(x), MathF.Abs(y)) - .92f);
                };
                case "bricks": return (x, y) =>
                {
                    float v = y * 3.2f, row = MathF.Floor(v), u = x * 1.6f + (((int)row & 1) == 0 ? .5f : 0);
                    float d = MathF.Max((MathF.Abs(Mod(u, 1) - .5f) - .47f) / 1.6f, (MathF.Abs(Mod(v, 1) - .5f) - .44f) / 3.2f);
                    return Fill(d) * (.7f + .3f * BrushStampNoise.Hash((int)MathF.Floor(u), (int)row, 0, Seed)) *
                        Fill(MathF.Max(MathF.Abs(x), MathF.Abs(y)) - .92f);
                };
            }
            if (id.StartsWith("letter_", StringComparison.Ordinal) && id.Length == 8)
            {
                int index = "abcdefghijklmnopqrstuvwxyz0123456789".IndexOf(id[7]);
                if (index >= 0) return Glyph(Glyphs[index]);
            }
            throw new ArgumentException("Unknown generated brush alpha.");
        }

        static Func<float, float, float> Scratches()
        {
            var lines = new (float ax, float ay, float bx, float by, float width, float strength)[16];
            for (int i = 0; i < lines.Length; i++)
            {
                float cx = Rand(i, 0) * 1.2f - .6f, cy = Rand(i, 1) * 1.2f - .6f, angle = .5f + (Rand(i, 2) - .5f) * .6f;
                float half = .15f + Rand(i, 3) * .4f, dx = MathF.Cos(angle) * half, dy = MathF.Sin(angle) * half;
                lines[i] = (cx - dx, cy - dy, cx + dx, cy + dy, .004f + Rand(i, 4) * .012f, .5f + Rand(i, 5) * .5f);
            }
            return (x, y) =>
            {
                float best = 0;
                foreach (var line in lines)
                {
                    var segment = Segment(x, y, line.ax, line.ay, line.bx, line.by);
                    float taper = segment.t * 2 - 1;
                    best = MathF.Max(best, Fill(segment.d - line.width * MathF.Sqrt(MathF.Max(0, 1 - taper * taper))) * line.strength);
                }
                return best * Fade(x, y);
            };
        }

        static Func<float, float, float> Splatter()
        {
            var drops = new (float x, float y, float radius)[28];
            for (int i = 0; i < drops.Length; i++)
            {
                float angle = Rand(i, 0) * Tau, distance = .42f + MathF.Pow(Rand(i, 1), .7f) * .45f;
                drops[i] = (MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, .015f + MathF.Pow(Rand(i, 2), 2) * .07f);
            }
            return (x, y) =>
            {
                float angle = MathF.Atan2(y, x), edge = .36f + .3f * (Fbm(MathF.Cos(angle) * 1.5f + 4, MathF.Sin(angle) * 1.5f, 0, 3) - .5f);
                float best = Fill(Len(x, y) - edge);
                foreach (var drop in drops) best = MathF.Max(best, Fill(Len(x - drop.x, y - drop.y) - drop.radius));
                return best;
            };
        }

        static Func<float, float, float> Fur()
        {
            var points = new (float x, float y)[40, 4];
            var widths = new float[40];
            for (int i = 0; i < 40; i++)
            {
                float x = Rand(i, 0) * 1.4f - .7f, y = -.85f + Rand(i, 1) * .3f;
                float length = .8f + Rand(i, 2) * .7f, bend = (Rand(i, 3) - .5f) * .9f;
                widths[i] = .008f + Rand(i, 4) * .01f;
                for (int k = 0; k < 4; k++) { float t = k / 3f; points[i, k] = (x + bend * t * t * length, MathF.Min(y + t * length, .95f)); }
            }
            return (x, y) =>
            {
                float best = 0;
                for (int i = 0; i < 40; i++)
                for (int k = 0; k < 3; k++)
                {
                    var a = points[i, k]; var b = points[i, k + 1];
                    if (x < MathF.Min(a.x, b.x) - widths[i] - Aa || x > MathF.Max(a.x, b.x) + widths[i] + Aa ||
                        y < MathF.Min(a.y, b.y) - widths[i] - Aa || y > MathF.Max(a.y, b.y) + widths[i] + Aa) continue;
                    var segment = Segment(x, y, a.x, a.y, b.x, b.y);
                    best = MathF.Max(best, Fill(segment.d - widths[i] * (1 - (k + segment.t) / 3)));
                }
                return best;
            };
        }

        static Func<float, float, float> Leaf() => (x, y) =>
        {
            float s = MathF.Sin(Pi / 6), c = MathF.Cos(Pi / 6), u = x * c - y * s, v = x * s + y * c;
            float stem = Fill(Segment(u, v, 0, -.97f, 0, -.8f).d - .015f), t = (v + .85f) / 1.7f;
            if (t < 0 || t > 1) return stem;
            float half = .38f * MathF.Pow(MathF.Max(MathF.Sin(Pi * t), 0), .8f) * (1 - .3f * t);
            float q = (v - MathF.Abs(u) * .8f) * 6;
            float vein = MathF.Max(Fill(MathF.Abs(u) - .012f), Fill(MathF.Abs(q - MathF.Round(q, MidpointRounding.AwayFromZero)) / 6 - .006f));
            return MathF.Max(Fill(MathF.Abs(u) - half) * (1 - .45f * vein), stem);
        };

        static Func<float, float, float> Glyph(string strokes)
        {
            var segments = new List<(float ax, float ay, float bx, float by)>();
            foreach (string run in strokes.Split(' '))
                for (int i = 0; i + 3 < run.Length; i += 2)
                    segments.Add(((run[i] - '0' - 2) * .27f, (3 - (run[i + 1] - '0')) * .27f,
                        (run[i + 2] - '0' - 2) * .27f, (3 - (run[i + 3] - '0')) * .27f));
            return (x, y) =>
            {
                float nearest = float.MaxValue;
                foreach (var segment in segments) nearest = MathF.Min(nearest, Segment(x, y, segment.ax, segment.ay, segment.bx, segment.by).d);
                return Fill(nearest - .11f);
            };
        }

        internal static float Procedural(int kind, float x, float y, float hardness, uint seed)
        {
            float spatialSeed = (seed ^ (seed >> 16)) & 4095u;
            float r = Len(x, y), fall = 1 - Smooth(MathF.Min(hardness, .999f), 1, r);
            switch (kind)
            {
                case 1: return fall;
                case 2: return 1 - Smooth(MathF.Min(hardness, .999f), 1, MathF.Max(MathF.Abs(x), MathF.Abs(y)));
                case 3: return fall * (BrushStampNoise.Value(x * 14, y * 14, spatialSeed * .37f, seed) > .72f ? 1 : 0);
                case 4: return fall * Math.Clamp((BrushStampNoise.Fbm(x * 2.5f + spatialSeed * .13f, y * 2.5f, .5f, 5, seed) - .42f) * 5, 0f, 1f);
                case 5: return fall * BrushStampNoise.Fbm(x * 1.8f, y * 1.8f, spatialSeed * .21f, 4, seed, true);
                case 6:
                    float k = MathF.Abs(MathF.Cos(MathF.Atan2(y, x) * 2.5f));
                    return 1 - Smooth(MathF.Min(hardness, .999f), 1, r / (.45f + .55f * k * k));
                case 7: return fall * Math.Clamp((BrushStampNoise.Value(x * .8f, y * 18, spatialSeed * .7f, seed) - .35f) * 3, 0f, 1f);
                default: return 0;
            }
        }
    }
}
