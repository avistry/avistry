using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Built-in brushes plus the user's saved brushes (stored in EditorPrefs, not in the project).</summary>
    public static class BrushLibrary
    {
        const string PrefsKey = "AvatarTextureStudio.BrushPresets.v1";

        static BrushPreset P(string name, float size, BrushShape shape, float opacity = 1f, bool pSize = true, bool pOpacity = false) =>
            new BrushPreset { name = name, size = size, opacity = opacity, pressureSize = pSize, pressureOpacity = pOpacity, shape = shape };

        public static readonly BrushPreset[] BuiltIn =
        {
            P("Soft round", 64, new BrushShape { hardness = 0.15f, spacing = 0.1f }),
            P("Hard round", 48, new BrushShape { hardness = 0.95f, spacing = 0.1f }),
            P("Pencil", 6, new BrushShape { hardness = 1f, spacing = 0.15f, grain = 0.35f, grainSize = 2f }, 1f, false, true),
            P("Airbrush", 160, new BrushShape { hardness = 0f, spacing = 0.08f, buildUp = true, flow = 0.06f }, 1f, false, true),
            P("Marker", 40, new BrushShape { tip = BrushTip.Square, hardness = 0.85f, spacing = 0.08f, roundness = 0.35f, followStroke = true, angle = 90f }, 0.85f, false),
            P("Calligraphy", 36, new BrushShape { hardness = 0.95f, spacing = 0.06f, roundness = 0.18f, angle = 45f }),
            P("Bristle", 80, new BrushShape { tip = BrushTip.Bristle, hardness = 0.6f, spacing = 0.06f, followStroke = true, density = 12 }),
            P("Spray", 120, new BrushShape { tip = BrushTip.Spray, hardness = 1f, spacing = 0.5f, density = 16 }, 1f, false),
            P("Chalk", 48, new BrushShape { hardness = 0.8f, spacing = 0.1f, grain = 0.9f, grainSize = 2.5f }, 1f, true, true),
            P("Splatter", 60, new BrushShape { hardness = 0.9f, spacing = 1.2f, scatter = 1.6f, sizeJitter = 0.85f, opacityJitter = 0.3f }, 1f, false),
            P("Smooth blend", 200, new BrushShape { hardness = 0f, spacing = 0.1f }, 0.25f, false, true),
        };

        [Serializable]
        class Store { public List<BrushPreset> presets = new List<BrushPreset>(); }

        static List<BrushPreset> _user;

        public static List<BrushPreset> User
        {
            get
            {
                if (_user != null) return _user;
                _user = new List<BrushPreset>();
                try
                {
                    var json = EditorPrefs.GetString(PrefsKey, "");
                    if (!string.IsNullOrEmpty(json)) _user = JsonUtility.FromJson<Store>(json)?.presets ?? new List<BrushPreset>();
                }
                catch (Exception e) { Debug.LogWarning("[Avistry] Could not read saved brushes: " + e.Message); }
                return _user;
            }
        }

        static void SaveUser() => EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Store { presets = User }));

        public static void Save(BrushPreset p)
        {
            User.RemoveAll(x => x.name == p.name);
            User.Add(p);
            SaveUser();
        }

        public static void Delete(string name)
        {
            User.RemoveAll(x => x.name == name);
            SaveUser();
        }
    }

    /// <summary>Layer-stack presets: built-in looks, plus recipes the user saved to the Presets folder.</summary>
    public static class LayerPresets
    {
        // In Assets, not inside the package: an installed package folder is replaced on every update.
        public const string UserFolder = "Assets/AvatarTextureStudio Presets";

        static Gradient G(params (float t, Color c, float a)[] keys)
        {
            var g = new Gradient();
            g.SetKeys(keys.Select(k => new GradientColorKey(k.c, k.t)).ToArray(),
                      keys.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }

        static T Named<T>(T layer, string name, BlendMode blend, float opacity = 1f) where T : Layer
        {
            layer.name = name;
            layer.blend = blend;
            layer.opacity = opacity;
            return layer;
        }

        /// <summary>Menu path ("Category/Name") and a factory for the layers, bottom first.</summary>
        public static readonly (string path, Func<List<Layer>> make)[] BuiltIn =
        {
            ("Colour/Recolour (keeps shading)", () => new List<Layer> { Named(new GradientMapLayer(), "Recolour", BlendMode.Normal) }),
            ("Colour/Desaturate", () => new List<Layer> { Named(new HslLayer { saturation = -1f }, "Desaturate", BlendMode.Normal) }),
            ("Colour/Pastel", () => new List<Layer> { Named(new HslLayer { saturation = -0.45f, lightness = 0.3f }, "Pastel", BlendMode.Normal) }),
            ("Colour/Vivid", () => new List<Layer> { Named(new HslLayer { saturation = 0.45f }, "Vivid", BlendMode.Normal) }),
            ("Colour/Warm tint", () => new List<Layer> { Named(new SolidColorLayer { color = new Color(1f, 0.62f, 0.32f) }, "Warm tint", BlendMode.SoftLight, 0.45f) }),
            ("Colour/Cool tint", () => new List<Layer> { Named(new SolidColorLayer { color = new Color(0.35f, 0.6f, 1f) }, "Cool tint", BlendMode.SoftLight, 0.45f) }),
            ("Colour/Hue shift +60°", () => new List<Layer> { Named(new HslLayer { hue = 60f }, "Hue shift", BlendMode.Normal) }),

            ("Tone/Brighten", () => new List<Layer> { Named(new LevelsLayer { gamma = 1.4f }, "Brighten", BlendMode.Normal) }),
            ("Tone/Darken", () => new List<Layer> { Named(new LevelsLayer { gamma = 0.7f }, "Darken", BlendMode.Normal) }),
            ("Tone/More contrast", () => new List<Layer> { Named(new LevelsLayer { inBlack = 0.08f, inWhite = 0.92f }, "Contrast", BlendMode.Normal) }),
            ("Tone/Faded (lift blacks)", () => new List<Layer> { Named(new LevelsLayer { outBlack = 0.14f, outWhite = 0.96f }, "Faded", BlendMode.Normal) }),

            ("Mask textures/Invert", () => new List<Layer> { Named(new LevelsLayer { outBlack = 1f, outWhite = 0f }, "Invert", BlendMode.Normal) }),
            ("Mask textures/Stronger", () => new List<Layer> { Named(new LevelsLayer { inWhite = 0.6f }, "Stronger", BlendMode.Normal) }),
            ("Mask textures/Weaker", () => new List<Layer> { Named(new LevelsLayer { outWhite = 0.55f }, "Weaker", BlendMode.Normal) }),
            ("Mask textures/Softer edges", () => new List<Layer> { Named(new LevelsLayer { gamma = 1.6f }, "Softer", BlendMode.Normal) }),

            ("Hair/Ombré tips", () => new List<Layer>
            {
                Named(new RootTipGradientLayer
                {
                    gradient = G((0f, new Color(1f, 0.45f, 0.7f), 0f), (0.4f, new Color(1f, 0.45f, 0.7f), 0f), (1f, new Color(0.55f, 0.35f, 1f), 1f)),
                }, "Ombré tips", BlendMode.Color),
            }),
            ("Hair/Dark roots", () => new List<Layer>
            {
                Named(new RootTipGradientLayer
                {
                    gradient = G((0f, new Color(0.25f, 0.2f, 0.25f), 1f), (0.3f, new Color(0.25f, 0.2f, 0.25f), 0f), (1f, Color.white, 0f)),
                }, "Dark roots", BlendMode.Multiply),
            }),
            ("Hair/Bleached tips", () => new List<Layer>
            {
                Named(new RootTipGradientLayer
                {
                    gradient = G((0f, new Color(1f, 0.95f, 0.85f), 0f), (0.55f, new Color(1f, 0.95f, 0.85f), 0f), (1f, new Color(1f, 0.95f, 0.85f), 0.85f)),
                }, "Bleached tips", BlendMode.Screen),
            }),
            ("Hair/Two-tone (split colour)", () => new List<Layer>
            {
                Named(new RootTipGradientLayer
                {
                    gradient = G((0f, new Color(0.2f, 0.25f, 0.6f), 1f), (0.49f, new Color(0.2f, 0.25f, 0.6f), 1f), (0.51f, new Color(0.95f, 0.5f, 0.8f), 1f), (1f, new Color(0.95f, 0.5f, 0.8f), 1f)),
                }, "Two-tone", BlendMode.Color),
            }),
            ("Hair/Galaxy", () => new List<Layer>
            {
                Named(new GradientMapLayer
                {
                    gradient = G((0f, new Color(0.04f, 0.03f, 0.12f), 1f), (0.5f, new Color(0.25f, 0.2f, 0.7f), 1f), (1f, new Color(0.85f, 0.8f, 1f), 1f)),
                }, "Galaxy base", BlendMode.Normal),
                Named(new RootTipGradientLayer
                {
                    mode = GradientMode.WholeHairHeight,
                    gradient = G((0f, new Color(0.3f, 0.4f, 1f), 0f), (0.45f, new Color(0.65f, 0.3f, 1f), 0.6f), (1f, new Color(1f, 0.4f, 0.75f), 1f)),
                }, "Galaxy glow", BlendMode.Color, 0.8f),
            }),
            ("Hair/Pastel recolour", () => new List<Layer>
            {
                Named(new GradientMapLayer
                {
                    gradient = G((0f, new Color(0.45f, 0.35f, 0.55f), 1f), (0.6f, new Color(0.8f, 0.7f, 0.95f), 1f), (1f, new Color(1f, 0.97f, 1f), 1f)),
                }, "Pastel recolour", BlendMode.Normal),
            }),
        };

        /// <summary>Recipes saved by the user into the Presets folder.</summary>
        public static List<TextureRecipe> UserPresets()
        {
            if (!AssetDatabase.IsValidFolder(UserFolder)) return new List<TextureRecipe>();
            return AssetDatabase.FindAssets("t:TextureRecipe", new[] { UserFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<TextureRecipe>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null)
                .OrderBy(r => r.name)
                .ToList();
        }

        /// <summary>Copies of a saved preset's layers (fresh ids).</summary>
        public static List<Layer> LayersOf(TextureRecipe preset)
        {
            var copy = UnityEngine.Object.Instantiate(preset);
            var layers = copy.layers.Where(l => l != null).ToList();
            UnityEngine.Object.DestroyImmediate(copy);
            foreach (var l in layers) l.id = Guid.NewGuid().ToString("N");
            return layers;
        }

        /// <summary>
        /// Saves the given layers as a preset asset (asks for a file name). Island masks and paint strokes are
        /// dropped, because they only make sense on the mesh they were made for.
        /// </summary>
        public static string SaveAsPreset(TextureRecipe working)
        {
            if (!AssetDatabase.IsValidFolder(UserFolder)) AssetDatabase.CreateFolder("Assets", "AvatarTextureStudio Presets");
            string path = StudioDialog.SaveFilePanelInProject(Loc.T("Save layer preset"), "My preset", "asset",
                Loc.T("Layer settings are saved. Piece masks and paint strokes are not, since they belong to one part."), UserFolder);
            if (string.IsNullOrEmpty(path)) return null;

            var preset = UnityEngine.Object.Instantiate(working);
            preset.hideFlags = HideFlags.None;
            preset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            preset.sourceTexture = null;
            preset.sourceMaterial = null;
            preset.rendererPath = null;
            preset.bakedTexture = null;
            preset.materialIndex = 0;
            preset.layers = preset.layers.Where(l => l != null && !(l is PaintLayer)).ToList();
            foreach (var l in preset.layers)
            {
                l.maskMode = MaskMode.WholeTexture;
                l.islands = new List<int>();
                l.invertMask = false;
                l.hasPaintMask = false; // painted masks belong to one mesh's UVs, like island masks
                l.paintMaskStartsHidden = false;
                l.maskStrokes = new List<PaintStroke>();
            }
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            return path;
        }
    }
}
