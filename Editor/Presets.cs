using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public static class BrushLibrary
    {
        const string PrefsKey = "AvatarTextureStudio.BrushPresets.v1";

        static BrushPreset P(string name, float size, BrushShape shape, float opacity = 1f, bool pSize = true, bool pOpacity = false) =>
            new BrushPreset { name = name, size = size, opacity = opacity, pressureSize = pSize, pressureOpacity = pOpacity, shape = shape };

        // These additional stroke recipes are adapted from TextureCraft; see Third Party Notices.md.
        static BrushPreset Stamp(string name, string alpha, float hardness = 0.5f, float flow = 1f, float spacing = 0.1f,
            bool follow = false, float angleJitter = 0f, float sizeJitter = 0f, float scatter = 0f) =>
            P(name, 64f, new BrushShape
            {
                tip = BrushTip.Stamp, alphaId = alpha, alphaVersion = 1, jitterVersion = 1,
                hardness = hardness, flow = flow, buildUp = true, spacing = spacing * 2f,
                followStroke = follow, angleJitter = angleJitter, sizeJitter = sizeJitter, scatter = scatter,
            });

        internal static string CategoryOf(BrushPreset preset) => preset.name switch
        {
            "Basic hard" or "Basic soft" or "Soft airbrush" or "Square" or "Dots" => "Basic",
            "Dry brush" or "Sponge chalk" or "Textured spray" or "Grunge" or "Ink splatter" or
                "Scratches" or "Chips" or "Cracks" or "Drips" => "Wear and grunge",
            "Stars" or "Stitches" or "Rivets" or "Fur" => "Details",
            _ => "Everyday",
        };

        public static readonly BrushPreset[] BuiltIn =
        {
            P("Soft round", 64, new BrushShape { hardness = 0.15f, spacing = 0.1f }),
            P("Hard round", 48, new BrushShape { hardness = 0.95f, spacing = 0.1f }),
            P("Pencil", 6, new BrushShape { hardness = 1f, spacing = 0.15f, grain = 0.35f, grainSize = 2f }, 1f, false, true),
            P("Airbrush", 160, new BrushShape { hardness = 0f, spacing = 0.08f, buildUp = true, flow = 0.06f }, 1f, false, true),
            P("Marker", 40, new BrushShape { tip = BrushTip.Square, hardness = 0.85f, spacing = 0.08f, roundness = 0.35f, followStroke = true, angle = 90f }, 0.85f, false),
            P("Calligraphy", 36, new BrushShape { hardness = 0.95f, spacing = 0.06f, roundness = 0.18f, angle = 45f }),
            P("Bristle", 80, new BrushShape { tip = BrushTip.Bristle, hardness = 0.6f, spacing = 0.06f, followStroke = true, density = 12 }),
            P("Spray", 120, new BrushShape { tip = BrushTip.Spray, hardness = 1f, spacing = 0.5f, density = 16, dotSize = BrushShape.SprayDotSize }, 1f, false),
            P("Chalk", 48, new BrushShape { hardness = 0.8f, spacing = 0.1f, grain = 0.9f, grainSize = 2.5f }, 1f, true, true),
            P("Splatter", 60, new BrushShape { hardness = 0.9f, spacing = 1.2f, scatter = 1.6f, sizeJitter = 0.85f, opacityJitter = 0.3f, dotSize = BrushShape.SplatterDotSize }, 1f, false),
            P("Smooth blend", 200, new BrushShape { hardness = 0f, spacing = 0.1f }, 0.25f, false, true),
            Stamp("Basic hard", "procedural_round", hardness: 0.95f),
            Stamp("Basic soft", "procedural_round", hardness: 0f, flow: 0.6f),
            Stamp("Soft airbrush", "procedural_round", hardness: 0f, flow: 0.12f, spacing: 0.04f),
            Stamp("Square", "procedural_square", hardness: 0.9f, follow: true),
            Stamp("Dots", "procedural_round", hardness: 0.9f, spacing: 1.6f),
            Stamp("Dry brush", "procedural_streaks", flow: 0.5f, spacing: 0.04f, follow: true),
            Stamp("Sponge chalk", "sponge", flow: 0.7f, spacing: 0.15f, angleJitter: 1f),
            Stamp("Textured spray", "procedural_spray", spacing: 0.3f, angleJitter: 1f, sizeJitter: 0.3f),
            Stamp("Grunge", "procedural_grunge", spacing: 0.4f, angleJitter: 1f, sizeJitter: 0.4f),
            Stamp("Ink splatter", "splatter", spacing: 1.2f, angleJitter: 1f, sizeJitter: 0.5f, scatter: 1.5f),
            Stamp("Scratches", "scratches", spacing: 0.5f, follow: true, angleJitter: 0.1f),
            Stamp("Chips", "chips", spacing: 0.8f, angleJitter: 1f, sizeJitter: 0.5f, scatter: 1f),
            Stamp("Cracks", "cracks", spacing: 0.9f, angleJitter: 1f),
            P("Drips", 64f, new BrushShape
                { tip = BrushTip.Drips, hardness = 0.7f, buildUp = true, flow = 0.6f, spacing = 0.2f, jitterVersion = 1 }),
            Stamp("Stars", "star_5", spacing: 1.5f, angleJitter: 1f, sizeJitter: 0.6f, scatter: 2f),
            Stamp("Stitches", "stitches", spacing: 0.9f, follow: true),
            Stamp("Rivets", "rivet", spacing: 2f),
            Stamp("Fur", "fur", spacing: 0.3f, follow: true, angleJitter: 0.15f),
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

    public static class LayerPresets
    {
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

        public static List<TextureRecipe> UserPresets() => OrderUserPresets(ReadUserPresets());

        internal static IEnumerable<(string path, TextureRecipe recipe)> ReadUserPresets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TextureRecipe", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextureRecipe>(path);
                bool preset = recipe != null && !recipe.savedAsDraft && recipe.bakedTexture == null &&
                    recipe.bakedMaterial == null && (recipe.savedAsTemplate || IsLegacyPreset(recipe, path));
                yield return (path, preset ? recipe : null);
            }
        }

        internal static List<TextureRecipe> OrderUserPresets(IEnumerable<(string path, TextureRecipe recipe)> entries) => entries
            .Where(entry => entry.recipe != null)
            .OrderBy(entry => entry.recipe.name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.path, StringComparer.Ordinal)
            .Select(entry => entry.recipe)
            .ToList();

        static bool IsLegacyPreset(TextureRecipe recipe, string path) => recipe.sourceTexture == null && recipe.sourceMaterial == null &&
            string.IsNullOrEmpty(recipe.rendererPath) && path.StartsWith(UserFolder + "/", StringComparison.Ordinal);

        internal static bool IsReusable(Layer layer) => layer != null && !(layer is PaintLayer) && !(layer is MixedMergeLayer) &&
            !(layer is DrawnGradientLayer gradient && gradient.sceneProjection) &&
            !(layer is ImageLayer image && (image.image == null || !EditorUtility.IsPersistent(image.image) || !string.IsNullOrEmpty(image.generated)));

        public static List<Layer> LayersOf(TextureRecipe preset)
        {
            if (preset == null) return new List<Layer>();
            var copy = UnityEngine.Object.Instantiate(preset);
            var layers = copy.layers.Where(l => l != null).ToList();
            UnityEngine.Object.DestroyImmediate(copy);
            foreach (var l in layers) l.id = Guid.NewGuid().ToString("N");
            return layers;
        }

        internal static int PrepareForApply(List<Layer> layers)
        {
            int released = ReleaseOrphanClipping(layers);
            layers.RemoveAll(layer => !IsReusable(layer));
            var groupIds = new Dictionary<string, string>();
            foreach (var layer in layers)
            {
                if (!string.IsNullOrEmpty(layer.groupId))
                {
                    if (!groupIds.TryGetValue(layer.groupId, out var newId))
                        groupIds[layer.groupId] = newId = Guid.NewGuid().ToString("N");
                    layer.groupId = newId;
                }
                ClearEditSpecificSettings(layer);
            }
            return released;
        }

        static int ReleaseOrphanClipping(List<Layer> layers)
        {
            int released = 0;
            bool baseIncluded = false;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                bool included = IsReusable(layer);
                if (!layer.clipToBelow) baseIncluded = included;
                else if (!baseIncluded && included)
                {
                    layer.clipToBelow = false;
                    released++;
                }
            }
            return released;
        }

        static void ClearEditSpecificSettings(Layer layer)
        {
            layer.maskMode = MaskMode.WholeTexture;
            layer.islands = new List<int>();
            layer.faces = new List<int>();
            layer.faceLayout = null;
            layer.invertMask = false;
            layer.hasPaintMask = false;
            layer.paintMaskStartsHidden = false;
            layer.maskStrokes = new List<PaintStroke>();
            if (layer is ImageLayer image)
            {
                image.offFloats = new List<string>();
                image.offKeywords = new List<string>();
                image.flattenedFrom = null;
                if (image.mask != null && !EditorUtility.IsPersistent(image.mask)) image.mask = null;
            }
            if (layer is ColourMatchLayer match)
            {
                match.source = null;
                match.sourceTexture = null;
            }
        }

        public static string SaveAsPreset(TextureRecipe working)
        {
            if (working == null || !working.layers.Any(IsReusable)) return null;
            EnsureUserFolder();
            string path = StudioDialog.SaveFilePanelInProject(Loc.T("Save layer preset"), "My preset", "asset",
                Loc.T("Looks keep reusable layer settings. Paint, painted masks and selection limits stay with the edit. Clipping stays when its base is included."), UserFolder);
            return string.IsNullOrEmpty(path) ? null : WriteTemplate(working, path);
        }

        static void EnsureUserFolder()
        {
            if (!AssetDatabase.IsValidFolder(UserFolder)) AssetDatabase.CreateFolder("Assets", "AvatarTextureStudio Presets");
        }

        internal static bool ValidTemplateName(string name)
        {
            const string invalid = "<>:\"/\\|?*";
            return !string.IsNullOrWhiteSpace(name) && name.Any(c => c != '.' && !char.IsWhiteSpace(c)) &&
                !name.Any(c => invalid.Contains(c) || char.IsControl(c));
        }

        internal static string SaveNamedTemplate(TextureRecipe working, string name)
        {
            if (working == null || !working.layers.Any(IsReusable) || !ValidTemplateName(name)) return null;
            EnsureUserFolder();
            return WriteTemplate(working, $"{UserFolder}/{name.Trim().TrimEnd('.').TrimEnd()}.asset");
        }

        static string WriteTemplate(TextureRecipe working, string path)
        {
            var preset = UnityEngine.Object.Instantiate(working);
            preset.hideFlags = HideFlags.None;
            preset.sourceTexture = null;
            preset.sourceMaterial = null;
            preset.rendererPath = null;
            preset.bakedTexture = null;
            preset.bakedMaterial = null;
            preset.savedAsDraft = false;
            preset.savedAsTemplate = true;
            preset.materialIndex = 0;
            PrepareForApply(preset.layers);
            try
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                preset.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(preset, path);
                if (AssetDatabase.GetAssetPath(preset) != path) return null;
                AssetDatabase.SaveAssetIfDirty(preset);
                return path;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Avistry couldn't save the template ({error.GetType().Name}).");
                return null;
            }
            finally
            {
                if (!EditorUtility.IsPersistent(preset)) UnityEngine.Object.DestroyImmediate(preset);
            }
        }
    }
}
