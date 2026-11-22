using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Colours, text styles, rounded boxes and state glyphs shared by the info card and the skill tree.</summary>
    static partial class StudioGUI
    {
        // Each token is a light / dark pair, chosen for 4.5:1 contrast against CardBg in both skins.
        static Color Pair(int light, int dark, float a = 1f) => Pro ? Hex(dark, a) : Hex(light, a);

        internal static Color CardBg => Pair(0xF4F5F7, 0x2B2D31);
        internal static Color CardInset => Pair(0xFFFFFF, 0x34373D);
        internal static Color CardBorder => Pair(0xD5D9DF, 0x464A52);
        internal static Color CardDivider => Pair(0xD5D9DF, 0x3C4047);
        internal static Color CardText => Pair(0x1F2328, 0xE6E8EB);
        internal static Color CardTextDim => Pair(0x3F4752, 0xA3ABB5);
        internal static Color CardAccent => Pair(0x8A4300, 0xFF9926);
        internal static Color CardTryFill => Pair(0xECE5DE, 0x443A30);
        internal static Color CardGood => Pair(0x2E7D46, 0x73D98C);
        internal static Color CardDanger => Pair(0xC0392B, 0xFF6B61);
        internal static Color CardBarWhat => Pair(0x245FC2, 0x6AA2FF);
        internal static Color CardBarWhen => Pair(0x8A4300, 0xFF9926);
        internal static Color CardBarExample => CardGood;
        internal static Color MedalGold => Pair(0x8A6400, 0xFFD24D);

        /// <summary>The colour of a tier (1 to 6): its stripe, kicker and medal.</summary>
        internal static Color TierColour(int tier)
        {
            switch (tier)
            {
                case 1: return Pair(0x2E7D46, 0x5CC97B);
                case 2: return Pair(0x245FC2, 0x6AA2FF);
                case 3: return Pair(0x6A3FC4, 0xB48CFF);
                case 4: return Pair(0x00796B, 0x4DD0C1);
                case 5: return Pair(0x9A4F00, 0xFFB050);
                default: return Pair(0xB42850, 0xFF7A98);
            }
        }

        // Styles are rebuilt when the editor skin changes, because their text colours are baked in.
        static GUIStyle s_cardKicker, s_cardTitle, s_cardTitleDetail, s_cardTitleRow, s_cardAlso, s_cardLede, s_cardHeading, s_cardBody, s_cardMeta;
        static bool s_cardStylesPro;

        static GUIStyle MakeCardStyle(GUIStyle basis, int size, Color colour, bool wrap)
        {
            var s = new GUIStyle(basis)
            {
                fontSize = size,
                wordWrap = wrap,
                richText = false,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
            };
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = colour;
            return s;
        }

        static void EnsureCardStyles()
        {
            if (s_cardKicker != null && s_cardStylesPro == Pro) return;
            s_cardStylesPro = Pro;
            var text = CardText;
            var dim = CardTextDim;
            s_cardKicker = MakeCardStyle(EditorStyles.miniBoldLabel, 10, text, false);
            s_cardTitle = MakeCardStyle(EditorStyles.boldLabel, 18, text, true);
            s_cardTitleDetail = MakeCardStyle(EditorStyles.boldLabel, 20, text, true);
            s_cardTitleRow = MakeCardStyle(EditorStyles.boldLabel, 16, text, true);
            s_cardAlso = MakeCardStyle(EditorStyles.wordWrappedMiniLabel, 11, dim, true);
            s_cardLede = MakeCardStyle(EditorStyles.wordWrappedLabel, 13, text, true);
            s_cardHeading = MakeCardStyle(EditorStyles.miniBoldLabel, 11, text, false);
            s_cardBody = MakeCardStyle(EditorStyles.wordWrappedLabel, 12, text, true);
            s_cardMeta = MakeCardStyle(EditorStyles.wordWrappedMiniLabel, 11, dim, true);
        }

        /// <summary>The small tier line above the title. The colour is set per call, so use it right away.</summary>
        internal static GUIStyle CardKicker(int tier)
        {
            EnsureCardStyles();
            s_cardKicker.normal.textColor = TierColour(tier);
            return s_cardKicker;
        }

        internal static GUIStyle CardTitle { get { EnsureCardStyles(); return s_cardTitle; } }
        internal static GUIStyle CardTitleDetail { get { EnsureCardStyles(); return s_cardTitleDetail; } }
        internal static GUIStyle CardTitleRow { get { EnsureCardStyles(); return s_cardTitleRow; } }
        internal static GUIStyle CardAlso { get { EnsureCardStyles(); return s_cardAlso; } }
        internal static GUIStyle CardLede { get { EnsureCardStyles(); return s_cardLede; } }
        internal static GUIStyle CardHeading { get { EnsureCardStyles(); return s_cardHeading; } }
        internal static GUIStyle CardBody { get { EnsureCardStyles(); return s_cardBody; } }
        internal static GUIStyle CardMeta { get { EnsureCardStyles(); return s_cardMeta; } }

        /// <summary>A filled rounded rectangle. Draws on Repaint only.</summary>
        internal static void DrawRounded(Rect r, Color fill, float radius)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, 0f, radius);
        }

        /// <summary>The outline of a rounded rectangle. Draws on Repaint only.</summary>
        internal static void DrawRoundedBorder(Rect r, Color border, float radius, float width = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, border, width, radius);
        }

        /// <summary>A rounded box with a 1 px outline, like the example box and the tree nodes.</summary>
        internal static void DrawRoundedBox(Rect r, Color fill, Color border, float radius)
        {
            DrawRounded(r, fill, radius);
            DrawRoundedBorder(r, border, radius, 1f);
        }

        // White glyphs: the colour comes from GUI.color, so one texture serves every tier and both skins.
        // They are drawn from pixels because the editor font may not have the half-filled circle.
        const int GlyphSize = 24;
        static Texture2D s_glyphNew, s_glyphRead, s_glyphDone, s_glyphMedal;

        static Texture2D Glyph(ref Texture2D cache, System.Func<float, float, float> alphaAt)
        {
            if (cache != null) return cache;
            var px = new Color32[GlyphSize * GlyphSize];
            for (int y = 0; y < GlyphSize; y++)
                for (int x = 0; x < GlyphSize; x++)
                    px[y * GlyphSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(x, y)) * 255f));
            var t = new Texture2D(GlyphSize, GlyphSize, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            t.SetPixels32(px);
            t.Apply(false, true);
            return cache = t;
        }

        const float GlyphCentre = (GlyphSize - 1) * 0.5f;
        static float GlyphDist(float x, float y, float cx, float cy) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        static float Disc(float d, float r) => r - d + 0.5f;

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            return GlyphDist(px, py, ax + t * dx, ay + t * dy);
        }

        static float TickDist(float x, float y) => Mathf.Min(SegDist(x, y, 6.5f, 12.5f, 10f, 8.5f), SegDist(x, y, 10f, 8.5f, 17.5f, 16f));

        /// <summary>New is a ring, Read is a ring with the left half filled, Done is a filled disc with a tick.</summary>
        internal static Texture2D StateGlyph(Learn.State state)
        {
            switch (state)
            {
                case Learn.State.Done:
                    return Glyph(ref s_glyphDone, (x, y) => Mathf.Min(Disc(GlyphDist(x, y, GlyphCentre, GlyphCentre), 10.5f), 1f) * (1f - Mathf.Clamp01(1.4f - TickDist(x, y) + 0.5f)));
                case Learn.State.Read:
                    return Glyph(ref s_glyphRead, (x, y) =>
                    {
                        float d = GlyphDist(x, y, GlyphCentre, GlyphCentre);
                        float ring = Mathf.Clamp01(Disc(d, 10.5f)) - Mathf.Clamp01(Disc(d, 8.5f));
                        float half = x < GlyphCentre ? Mathf.Clamp01(Disc(d, 8.5f)) : 0f;
                        return Mathf.Max(ring, half);
                    });
                default:
                    return Glyph(ref s_glyphNew, (x, y) =>
                    {
                        float d = GlyphDist(x, y, GlyphCentre, GlyphCentre);
                        return Mathf.Clamp01(Disc(d, 10.5f)) - Mathf.Clamp01(Disc(d, 8.5f));
                    });
            }
        }

        /// <summary>A coin on two ribbon tails, for a finished tier.</summary>
        internal static Texture2D MedalGlyph()
        {
            return Glyph(ref s_glyphMedal, (x, y) =>
            {
                float d = GlyphDist(x, y, GlyphCentre, 14.5f);
                float coin = Mathf.Clamp01(Disc(d, 8f)) * (1f - Mathf.Clamp01(1.2f - Mathf.Abs(d - 5.2f) + 0.5f) * 0.9f);
                bool tail = y <= 8f && y >= 1f && ((x >= 7.5f && x <= 10.5f) || (x >= 12.5f && x <= 15.5f));
                return Mathf.Max(coin, tail ? 1f : 0f);
            });
        }

        static Texture2D s_glyphPlay, s_glyphPage, s_glyphExternal;

        // Texture rows run bottom to top, so y grows upwards in these shapes.
        static float Line(float x, float y, float ax, float ay, float bx, float by, float half) => Mathf.Clamp01(half - SegDist(x, y, ax, ay, bx, by) + 0.5f);

        /// <summary>A play triangle (a page with a video) or a page with text lines (a page to read).</summary>
        internal static Texture2D WebGlyph(bool video)
        {
            if (video)
                return Glyph(ref s_glyphPlay, (x, y) =>
                {
                    // Edges of the triangle (7,4) (7,20) (20,12), as signed distances; inside is positive on all three.
                    float left = x - 7f;
                    float top = ((20f - 7f) * (y - 20f) - (12f - 20f) * (x - 7f)) / Mathf.Sqrt(13f * 13f + 8f * 8f);
                    float bottom = -((20f - 7f) * (y - 4f) - (12f - 4f) * (x - 7f)) / Mathf.Sqrt(13f * 13f + 8f * 8f);
                    return Mathf.Clamp01(Mathf.Min(left, Mathf.Min(-top, -bottom)) + 0.5f);
                });
            return Glyph(ref s_glyphPage, (x, y) =>
            {
                float frame = Mathf.Max(Mathf.Max(Line(x, y, 5f, 2f, 5f, 21f, 0.9f), Line(x, y, 18f, 2f, 18f, 21f, 0.9f)),
                                        Mathf.Max(Line(x, y, 5f, 2f, 18f, 2f, 0.9f), Line(x, y, 5f, 21f, 18f, 21f, 0.9f)));
                float text = Mathf.Max(Line(x, y, 8.5f, 16f, 14.5f, 16f, 0.9f), Mathf.Max(Line(x, y, 8.5f, 11.5f, 14.5f, 11.5f, 0.9f), Line(x, y, 8.5f, 7f, 12.5f, 7f, 0.9f)));
                return Mathf.Max(frame, text);
            });
        }

        /// <summary>An arrow pointing up and out: the link opens the browser.</summary>
        internal static Texture2D ExternalGlyph()
        {
            return Glyph(ref s_glyphExternal, (x, y) =>
                Mathf.Max(Line(x, y, 5f, 5f, 18.5f, 18.5f, 1.3f), Mathf.Max(Line(x, y, 9f, 18.5f, 18.5f, 18.5f, 1.3f), Line(x, y, 18.5f, 9f, 18.5f, 18.5f, 1.3f))));
        }

        internal static void DrawWebGlyph(Rect r, bool video, Color tint) => DrawTinted(r, WebGlyph(video), tint);
        internal static void DrawExternalGlyph(Rect r, Color tint) => DrawTinted(r, ExternalGlyph(), tint);

        static void DrawTinted(Rect r, Texture2D glyph, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, glyph, ScaleMode.ScaleToFit);
            GUI.color = old;
        }

        /// <summary>Draws a state glyph in <paramref name="tint"/> inside <paramref name="r"/>. Repaint only.</summary>
        internal static void DrawStateGlyph(Rect r, Learn.State state, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, StateGlyph(state), ScaleMode.ScaleToFit);
            GUI.color = old;
        }

        internal static void DrawMedalGlyph(Rect r, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, MedalGlyph(), ScaleMode.ScaleToFit);
            GUI.color = old;
        }
    }
}
