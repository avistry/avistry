using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The editable layer stack. Lives in memory while editing and is saved next to a baked PNG
    /// so the edit can be reopened and tweaked later. (Own file: ScriptableObject assets need a matching file name.)
    /// </summary>
    public class TextureRecipe : ScriptableObject
    {
        public Texture2D sourceTexture;
        public Material sourceMaterial;
        [Tooltip("The PNG this edit was baked into (empty for edits saved without baking)")]
        public Texture2D bakedTexture;
        public string rendererPath;
        public int materialIndex;
        [SerializeReference] public List<Layer> layers = new List<Layer>();

        /// <summary>
        /// Removes empty entries from a layer list in place and warns once with the count. Unity leaves a null in a
        /// [SerializeReference] list when it restores a snapshot (undo, reload, loaded asset) that lacks the object.
        /// </summary>
        internal static int DropNullLayers(List<Layer> list, string where)
        {
            if (list == null) return 0;
            int n = list.RemoveAll(l => l == null);
            if (n > 0) Debug.LogWarning($"Avistry: dropped {n} empty layer{(n == 1 ? "" : "s")} from the edit ({where}).");
            return n;
        }

        public static readonly (string label, Func<Layer> create)[] LayerTypes =
        {
            ("Paint (brush strokes)", () => new PaintLayer()),
            ("Gradient Map (recolour, keeps shading)", () => new GradientMapLayer()),
            ("Colour Match (take another material's colours, keeps detail)", () => new ColourMatchLayer()),
            ("Hue / Saturation / Lightness", () => new HslLayer()),
            ("Recolour part (hue on some pieces, like 2 · Recolour › One piece at a time)", () => new RecolourPartLayer()),
            ("Ombré Gradient (top → bottom)", () => new RootTipGradientLayer()),
            ("Gradient (line, circle or ellipse; drag it with the Gradient tool)", () => new DrawnGradientLayer()),
            ("Image ∕ sticker (place a picture on the texture)", () => new ImageLayer()),
            ("Solid Colour (tint)", () => new SolidColorLayer()),
            ("Levels", () => new LevelsLayer()),
            ("Correction/Brightness ∕ Contrast", () => new BrightnessContrastLayer()),
            ("Correction/Colour Balance", () => new ColorBalanceLayer()),
            ("Correction/Tone Curve", () => new ToneCurveLayer()),
            ("Correction/Posterization", () => new PosterizeLayer()),
            ("Correction/Invert colours", () => new InvertLayer()),
            ("Correction/Binarization (threshold)", () => new ThresholdLayer()),
        };
    }
}
