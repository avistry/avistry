using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly SelectGlyph[] PaintGlyphs =
            { SelectGlyph.Paint, SelectGlyph.Erase, SelectGlyph.Fill, SelectGlyph.Gradient, SelectGlyph.Blend };
        static readonly SelectGlyph[] MaskPaintGlyphs = { SelectGlyph.Reveal, SelectGlyph.Hide, SelectGlyph.Fill };
        static readonly SelectGlyph[] MaskFillGlyphs = { SelectGlyph.Reveal, SelectGlyph.Hide };
        static readonly SelectGlyph[] BlendGlyphs = { SelectGlyph.Blur, SelectGlyph.Smudge };
        static readonly SelectGlyph[] GradientShapeGlyphs = { SelectGlyph.Linear, SelectGlyph.Circle, SelectGlyph.Ellipse };

        sealed class PaintPaletteLabels
        {
            internal readonly string language = Loc.Current;
            internal readonly GUIContent[] paint, paintWhilePicking, maskPaint, maskFill, blend, gradientShape;

            internal PaintPaletteLabels()
            {
                paintWhilePicking = new[]
                {
                    new GUIContent(Loc.T("Paint"), Loc.T("Paint colour (B)")),
                    new GUIContent(Loc.T("Erase"), Loc.T("Erase paint on the selected Paint layer (E)")),
                    new GUIContent(Loc.T("Fill"), Loc.T("Fill with the main colour using the chosen scope (G).")),
                    new GUIContent(Loc.T("Gradient"), Loc.T("Drag to draw a gradient: linear, circle or ellipse (Shift+G). Each one is an editable layer.")),
                    new GUIContent(Loc.T("Blend"), Loc.T("Blur or smudge what's already there (J). Works on a Paint layer, so it can be erased or undone.")),
                };
                paint = System.Array.ConvertAll(paintWhilePicking, item => Choice(item.text, item.tooltip));
                maskPaint = new[]
                {
                    Choice(Loc.T("Show"), Loc.T("Paint to show this part of the layer again (B)")),
                    Choice(Loc.T("Hide"), Loc.T("Paint to hide this part of the layer (E)")),
                    Choice(Loc.T("Fill"), Loc.T("Fill the layer mask within the chosen scope (G).")),
                };
                maskFill = new[]
                {
                    Choice(Loc.T("Show"), Loc.T("Show reveals the layer. Hide covers it.")),
                    Choice(Loc.T("Hide"), Loc.T("Show reveals the layer. Hide covers it.")),
                };
                blend = new[]
                {
                    Choice(Loc.T("Blur"), Loc.T("Soften: blends colours under the brush with their surroundings")),
                    Choice(Loc.T("Smudge"), Loc.T("Finger tip: drags colour along the direction you move the brush")),
                };
                gradientShape = new[]
                {
                    Choice(Loc.T("Linear"), Loc.T("Blend along the direction you drag.")),
                    Choice(Loc.T("Circle"), Loc.T("Spread out evenly from the start point.")),
                    Choice(Loc.T("Ellipse"), Loc.T("Spread out from the start point in an oval.")),
                };
            }

            static GUIContent Choice(string name, string tip) =>
                new GUIContent(name, tip + "\n" + Loc.T("Click a choice, then use arrow keys or Shift+scroll to switch."));
        }

        static PaintPaletteLabels s_paintLabels;
        static PaintPaletteLabels PaintLabels =>
            s_paintLabels?.language == Loc.Current ? s_paintLabels : s_paintLabels = new PaintPaletteLabels();

        int PaintToolPalette(int current, bool mask, float width, bool scene, bool picking = false, int keyboard = 0)
        {
            var labels = PaintLabels;
            var tools = mask ? labels.maskPaint : picking ? labels.paintWhilePicking : labels.paint;
            return ToolPalette(current, mask ? MaskPaintGlyphs : PaintGlyphs, tools, width, scene, !picking, keyboard);
        }
    }
}
