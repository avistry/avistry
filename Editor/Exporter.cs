using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public enum OutputMode
    {
        DuplicateMaterial = 0,   // new PNG + copy of the material using it, assigned to this avatar (default, safest)
        UpdateOriginalMaterial = 1, // new PNG, original material re-pointed to it
        PngOnly = 2,             // just write the PNG
    }

    public static class Exporter
    {
        const string FallbackFolder = "Assets/AvatarTextureStudio Output";
        static readonly string[] Platforms = { "Standalone", "Android", "iPhone", "WebGL" };

        /// <summary>Harness toggle (no UI): pad texture seams after rendering.</summary>
        internal static bool PadSeams = true;

        public struct Result
        {
            public string pngPath;
            public string materialPath;
            public string recipePath;
            public List<string> alsoMaterialPaths; // materials of included meshes (other materials showing the same texture)
            public string paddingWarning; // null, or one plain line
        }

        /// <summary>The file names Bake would write right now (nothing is created).</summary>
        public static Result PredictPaths(TextureSlot slot, Material material, OutputMode mode)
        {
            var r = new Result();
            if (slot == null) return r;
            string dir = WritableFolder(slot.assetPath, false);
            string baseName = StripAts(Path.GetFileNameWithoutExtension(slot.assetPath));
            if (string.IsNullOrEmpty(baseName)) baseName = slot.texture != null ? StripAts(slot.texture.name) : "Texture";
            string tag = PickTag(baseName, (dir, ".png"), (dir, "_recipe.asset"));
            r.pngPath = $"{dir}/{baseName}{tag}.png";
            r.recipePath = $"{dir}/{baseName}{tag}_recipe.asset";
            if (mode == OutputMode.DuplicateMaterial && material != null)
                r.materialPath = FreeMaterialPath(material, false);
            return r;
        }

        /// <summary>Renders the layer stack to a new texture (size 0 = source resolution); the caller destroys it.</summary>
        internal static Texture2D RenderRecipe(TextureSlot slot, IList<Layer> layers, MeshUV uv, bool dither,
                                               bool padSeams, out string paddingWarning, int size = 0)
        {
            Texture2D tex;
            using (var comp = new Compositor(slot, size))
            {
                comp.Compose(layers, uv);
                tex = comp.ReadResult(dither);
            }
            paddingWarning = null;
            if (padSeams && slot.role == TextureRole.Color && uv != null && FollowsPartUVs(slot)) paddingWarning = SeamPadding.Apply(tex, uv);
            return tex;
        }

        // Matcaps, panospheres and tiled, scrolling or world-mapped textures aren't laid out by the part's UVs: padding would paint over texels they show.
        static bool FollowsPartUVs(TextureSlot slot)
        {
            if (slot.uvProblems.Count > 0) return false;
            foreach (var p in slot.properties)
                if (p.IndexOf("matcap", System.StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("panosphere", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            return true;
        }

        public static Result Bake(TextureSlot slot, Material material, List<TargetResolver.SlotRef> users,
                                  TextureRecipe recipe, MeshUV uv, OutputMode mode, bool dither,
                                  List<TargetResolver.TextureUser> also = null, System.Func<string, float, bool> progress = null)
        {
            bool Cancelled(string step, float f) => progress != null && progress(step, f);
            var result = new Result { alsoMaterialPaths = new List<string>() };
            also ??= new List<TargetResolver.TextureUser>();
            // A material on Poiyomi's in-memory section shader must go back to its real shader first:
            // a copy saved with CreateAsset would otherwise point at a shader that doesn't exist on disk (pink).
            MaterialWriter.EnsureEditable(new[] { material }.Concat(also.Select(a => a.material)).Distinct().ToArray());
            if (Cancelled(Loc.T("Rendering at full resolution…"), 0.1f)) return default;
            byte[] png;
            var tex = RenderRecipe(slot, recipe.layers, uv, dither, PadSeams, out result.paddingWarning);
            if (Cancelled(Loc.T("Encoding PNG…"), 0.5f)) { Object.DestroyImmediate(tex); return default; }
            png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (Cancelled(Loc.T("Writing files…"), 0.7f)) return default;

            string dir = WritableFolder(slot.assetPath);
            string baseName = StripAts(Path.GetFileNameWithoutExtension(slot.assetPath));
            string tag = PickTag(baseName, (dir, ".png"), (dir, "_recipe.asset"));
            result.pngPath = $"{dir}/{baseName}{tag}.png";
            File.WriteAllBytes(result.pngPath, png);
            AssetDatabase.ImportAsset(result.pngPath, ImportAssetOptions.ForceUpdate);
            CopyImporterSettings(slot.assetPath, result.pngPath);
            var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(result.pngPath);

            progress?.Invoke(Loc.T("Assigning to the avatar…"), 0.9f); // files exist now: no more cancelling

            // Keep the edit re-editable.
            var recipeCopy = Object.Instantiate(recipe);
            recipeCopy.hideFlags = HideFlags.None;
            recipeCopy.name = baseName + tag + "_recipe";
            recipeCopy.bakedTexture = newTex; // lets the tool recognise this PNG as a baked copy later
            result.recipePath = $"{dir}/{baseName}{tag}_recipe.asset";
            AssetDatabase.CreateAsset(recipeCopy, result.recipePath);

            switch (mode)
            {
                case OutputMode.DuplicateMaterial:
                {
                    string newMatPath = FreeMaterialPath(material);
                    var newMat = new Material(material) { name = Path.GetFileNameWithoutExtension(newMatPath) };
                    result.materialPath = newMatPath;
                    AssetDatabase.CreateAsset(newMat, result.materialPath);
                    foreach (var p in slot.properties) newMat.SetTexture(p, newTex);
                    DecalReader.TurnOffFlattened(newMat, recipe.layers); // flattened decals are now in the texture
                    EditorUtility.SetDirty(newMat);

                    AssignMaterial(users, newMat, "Assign edited material");
                    recipeCopy.sourceMaterial = material;
                    break;
                }
                case OutputMode.UpdateOriginalMaterial:
                    Undo.RecordObject(material, "Point material at edited texture");
                    foreach (var p in slot.properties) material.SetTexture(p, newTex);
                    DecalReader.TurnOffFlattened(material, recipe.layers);
                    EditorUtility.SetDirty(material);
                    result.materialPath = AssetDatabase.GetAssetPath(material);
                    break;
            }

            // Included meshes with their own material: the same treatment, one material (copy) per material.
            if (mode != OutputMode.PngOnly)
            {
                foreach (var group in also.Where(a => a.material != null && a.material != material).GroupBy(a => a.material))
                {
                    var m = group.Key;
                    var props = group.SelectMany(a => a.properties).Distinct().ToList();
                    if (mode == OutputMode.DuplicateMaterial)
                    {
                        string path = FreeMaterialPath(m);
                        var copy = new Material(m) { name = Path.GetFileNameWithoutExtension(path) };
                        AssetDatabase.CreateAsset(copy, path);
                        foreach (var p in props) copy.SetTexture(p, newTex);
                        EditorUtility.SetDirty(copy);
                        AssignMaterial(group.Select(a => a.slot), copy, "Assign edited material");
                        result.alsoMaterialPaths.Add(path);
                    }
                    else
                    {
                        Undo.RecordObject(m, "Point material at edited texture");
                        foreach (var p in props) m.SetTexture(p, newTex);
                        EditorUtility.SetDirty(m);
                        result.alsoMaterialPaths.Add(AssetDatabase.GetAssetPath(m));
                    }
                }
            }

            // Follow each shader's own rules after a scripted change (lilToon mirrors/settings, Poiyomi keywords).
            var changedPaths = new List<string>(result.alsoMaterialPaths);
            if (!string.IsNullOrEmpty(result.materialPath)) changedPaths.Insert(0, result.materialPath);
            var changed = changedPaths.Select(AssetDatabase.LoadAssetAtPath<Material>).Where(x => x != null).ToArray();
            if (changed.Length > 0)
            {
                MaterialWriter.AfterLilToonEdit(changed);
                MaterialWriter.FixPoiyomiKeywords(changed);
            }

            EditorUtility.SetDirty(recipeCopy);
            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>Puts <paramref name="material"/> into each slot (one Undo record per renderer).</summary>
        static void AssignMaterial(IEnumerable<TargetResolver.SlotRef> slots, Material material, string undoName)
        {
            foreach (var u in slots)
            {
                if (u.renderer == null) continue;
                Undo.RecordObject(u.renderer, undoName);
                var mats = u.renderer.sharedMaterials;
                if (u.materialIndex < mats.Length) mats[u.materialIndex] = material;
                u.renderer.sharedMaterials = mats;
                PrefabUtility.RecordPrefabInstancePropertyModifications(u.renderer);
            }
        }

        /// <summary>
        /// Makes a copy of <paramref name="m"/> next to it (the same naming as a save: White_ats, White_ats_2…) and puts it on
        /// every slot in <paramref name="users"/>, as one Undo step. The original is untouched; the copy asset stays after Undo.
        /// Returns the copy's path, or null when nothing was made.
        /// </summary>
        internal static string CopyMaterial(Material m, List<TargetResolver.SlotRef> users)
        {
            if (m == null) return null;
            Undo.RecordObject(m, "Make editable material copy"); // so a shader swap on the original is undone too
            MaterialWriter.EnsureEditable(new[] { m }); // an in-memory section-locked shader would save as a pink copy
            string path = FreeMaterialPath(m);
            var copy = new Material(m) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(copy, path);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            AssignMaterial(users, copy, "Make editable material copy");
            Undo.SetCurrentGroupName("Make editable material copy");
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return path;
        }

        /// <summary>
        /// True for a material this tool saved (name ends in _ats or _ats_2…) under Assets/. Quick material only writes to these, so a
        /// user's own material is never changed. It goes by name only, so a hand-made material ending in _ats counts as a copy. The open edit's recipe always names the open material as its source, so that is no test.
        /// </summary>
        internal static bool IsToolCopy(Material m)
        {
            if (m == null) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/") && Regex.IsMatch(m.name, @"_ats(_\d+)?$");
        }

        /// <summary>"White_ats" and "White_ats_2" both give "White": a new save reuses the base name instead of stacking suffixes (White_ats_ats).</summary>
        internal static string StripAts(string name) => string.IsNullOrEmpty(name) ? name : Regex.Replace(name, @"_ats(_\d+)?$", "");

        static bool PathTaken(string path) => File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));

        /// <summary>"_ats" if every file this save writes is free, otherwise "_ats_2", "_ats_3"…</summary>
        static string PickTag(string baseName, params (string dir, string ext)[] files)
        {
            for (int n = 1; n < 10000; n++)
            {
                string tag = n == 1 ? "_ats" : "_ats_" + n;
                if (files.All(f => !PathTaken($"{f.dir}/{baseName}{tag}{f.ext}"))) return tag;
            }
            return "_ats_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Where the edited copy of a material goes: next to the original, named like the texture copy.</summary>
        internal static string FreeMaterialPath(Material m, bool createFolder = true)
        {
            string dir = WritableFolder(AssetDatabase.GetAssetPath(m), createFolder);
            string baseName = StripAts(m.name);
            return $"{dir}/{baseName}{PickTag(baseName, (dir, ".mat"))}.mat";
        }

        /// <summary>Same folder as the source when it's under Assets/, otherwise a fallback folder (Packages are read-only).</summary>
        internal static string WritableFolder(string assetPath, bool createFolder = true)
        {
            string dir = string.IsNullOrEmpty(assetPath) ? null : Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && dir.StartsWith("Assets")) return dir;
            if (createFolder && !AssetDatabase.IsValidFolder(FallbackFolder)) AssetDatabase.CreateFolder("Assets", "AvatarTextureStudio Output");
            return FallbackFolder;
        }

        /// <summary>Copies every import setting (sRGB, type, mips, streaming, size, compression, platform overrides).</summary>
        static void CopyImporterSettings(string srcPath, string dstPath)
        {
            var src = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(dstPath) as TextureImporter;
            if (src == null || dst == null) return;

            bool changed = false;
            var s = new TextureImporterSettings();
            src.ReadTextureSettings(s);
            var ds = new TextureImporterSettings();
            dst.ReadTextureSettings(ds);
            if (!SameProps(s, ds)) { dst.SetTextureSettings(s); changed = true; }
            if (dst.maxTextureSize != src.maxTextureSize) { dst.maxTextureSize = src.maxTextureSize; changed = true; }
            if (dst.textureCompression != src.textureCompression) { dst.textureCompression = src.textureCompression; changed = true; }
            if (dst.crunchedCompression != src.crunchedCompression) { dst.crunchedCompression = src.crunchedCompression; changed = true; }
            if (dst.compressionQuality != src.compressionQuality) { dst.compressionQuality = src.compressionQuality; changed = true; }
            foreach (var p in Platforms)
            {
                var ps = src.GetPlatformTextureSettings(p);
                ps.name = p;
                if (SameProps(ps, dst.GetPlatformTextureSettings(p))) continue;
                dst.SetPlatformTextureSettings(ps);
                changed = true;
            }
            if (changed) dst.SaveAndReimport(); // the PNG was already imported with default settings
        }

        /// <summary>True when every readable+writable public property of two importer settings objects is equal.</summary>
        static bool SameProps(object a, object b)
        {
            foreach (var pi in a.GetType().GetProperties())
            {
                if (!pi.CanRead || !pi.CanWrite || pi.GetIndexParameters().Length > 0) continue;
                if (!Equals(pi.GetValue(a), pi.GetValue(b))) return false;
            }
            return true;
        }
    }
}
