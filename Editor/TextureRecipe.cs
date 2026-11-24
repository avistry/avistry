using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public class TextureRecipe : ScriptableObject
    {
        public Texture2D sourceTexture;
        public Material sourceMaterial;
        [Tooltip("The PNG this edit was baked into (empty for edits saved without baking)")]
        public Texture2D bakedTexture;
        [SerializeField] internal Material bakedMaterial;
        [SerializeField] internal bool savedAsDraft;
        [SerializeField] internal bool savedAsTemplate;
        [SerializeReference] internal StylePack.Entry draftTarget;
        [SerializeReference] internal IncludedLayout includedLayout;
        public string rendererPath;
        public int materialIndex;
        [SerializeReference] public List<Layer> layers = new List<Layer>();

        [Serializable]
        internal sealed class IncludedLayout
        {
            public string fingerprint;
            public StylePack.Binding primary;
            public StylePack.Binding[] primarySlots;
            public string[] primaryProperties;
            public int primaryChannel;
            public IncludedPart[] included;
        }

        [Serializable]
        internal sealed class IncludedPart
        {
            public StylePack.Binding binding;
            public Material sourceMaterial;
            public Material bakedMaterial;
            public string[] properties;
            public int channel;
            public int uvChannel;
        }

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
