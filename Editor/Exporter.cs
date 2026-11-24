using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public enum OutputMode
    {
        DuplicateMaterial = 0,
        UpdateOriginalMaterial = 1,
        PngOnly = 2,
    }

    public static class Exporter
    {
        const string FallbackFolder = "Assets/AvatarTextureStudio Output";
        const string ProjectFolder = "Assets/Avistry";
        static readonly string[] Platforms = { "Standalone", "Android", "iPhone", "WebGL" };
        internal const float WritingProgress = 0.8f;
        const float AssignmentProgress = 0.9f;

        internal static bool PadSeams = true;

        public struct Result
        {
            public string pngPath;
            public string materialPath;
            public string recipePath;
            public List<string> alsoMaterialPaths;
            public string paddingWarning;
            internal bool editChanged;
        }

        public static Result PredictPaths(TextureSlot slot, Material material, OutputMode mode)
        {
            var r = new Result();
            if (slot == null) return r;
            string dir = SaveFolder("Output", false);
            string baseName = TextureOutputName(slot);
            string tag = PickTag(baseName, (dir, ".png"), (dir, "_recipe.asset"));
            r.pngPath = $"{dir}/{baseName}{tag}.png";
            r.recipePath = $"{dir}/{baseName}{tag}_recipe.asset";
            if (mode == OutputMode.DuplicateMaterial && material != null)
                r.materialPath = FreeMaterialPath(material, false);
            return r;
        }

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
            var result = Bake(slot, material, users, recipe, uv, mode, dither, also, progress, null, null);
            if (result.pngPath != null) AssetDatabase.SaveAssets();
            return result;
        }

        internal static Result Bake(TextureSlot slot, Material material, List<TargetResolver.SlotRef> users,
                                    TextureRecipe recipe, MeshUV uv, OutputMode mode, bool dither, List<TargetResolver.TextureUser> also,
                                    System.Func<string, float, bool> progress, System.Func<bool> targetMatches, System.Action beginApply,
                                    StylePack.Entry savedTarget = null, TextureRecipe.IncludedLayout includedLayout = null)
        {
            bool Cancelled(string step, float f) => progress != null && progress(step, f);
            if (Cancelled(Loc.T("Rendering at full resolution…"), .1f)) return default;
            var copy = SnapshotRecipe(recipe, savedTarget, includedLayout);
            try
            {
                var pixels = CapturePixels(slot, copy, uv, dither);
                if (Cancelled(Loc.T("Encoding PNG…"), .5f)) return default;
                var png = pixels.Encode(default, null);
                if (Cancelled(Loc.T("Writing files…"), .7f)) return default;
                return WriteBake(slot, material, users, copy, mode, also, png, pixels.Warning, progress, targetMatches, beginApply);
            }
            finally { ReleaseSnapshot(copy); }
        }

        internal static TextureRecipe SnapshotRecipe(TextureRecipe recipe, StylePack.Entry savedTarget, TextureRecipe.IncludedLayout includedLayout)
        {
            var copy = Object.Instantiate(recipe);
            if (savedTarget != null) copy.draftTarget = StylePackTargets.Snapshot(savedTarget);
            if (includedLayout != null) copy.includedLayout = includedLayout;
            return copy;
        }

        internal static void ReleaseSnapshot(TextureRecipe copy)
        {
            if (copy != null && !EditorUtility.IsPersistent(copy)) Object.DestroyImmediate(copy);
        }

        internal static BakePixels CapturePixels(TextureSlot slot, TextureRecipe copy, MeshUV uv, bool dither)
        {
            var texture = RenderRecipe(slot, copy.layers, uv, dither, false, out _);
            try
            {
                return new BakePixels(texture.GetPixels32(), texture.width, texture.height, PaddingSnapshot(slot, uv));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static MeshUV PaddingSnapshot(TextureSlot slot, MeshUV uv) =>
            PadSeams && slot.role == TextureRole.Color && uv != null && FollowsPartUVs(slot) ? uv.CopyIslandRasterInput() : null;

        internal static Task<Color32[]> CapturePixelsAsync(TextureSlot slot, TextureRecipe copy, MeshUV uv, bool dither,
            CancellationToken cancel, out BakePixels bake)
        {
            using var comp = new Compositor(slot, 0);
            comp.Compose(copy.layers, uv);
            bake = new BakePixels(null, comp.Width, comp.Height, PaddingSnapshot(slot, uv));
            return comp.ReadResultPixelsAsync(dither, cancel);
        }

        internal sealed class BakePixels
        {
            readonly Color32[] _pixels;
            readonly MeshUV _raster;
            readonly int _width, _height;
            bool _closePieces;
            internal string Warning => _closePieces ? SeamPadding.CloseWarning : null;

            internal BakePixels(Color32[] pixels, int width, int height, MeshUV raster)
            {
                _pixels = pixels;
                _width = width;
                _height = height;
                _raster = raster;
            }

            internal byte[] Encode(CancellationToken cancel, System.Func<float, bool> progress) => EncodePixels(_pixels, cancel, progress);

            internal byte[] EncodePixels(Color32[] rgba, CancellationToken cancel, System.Func<float, bool> progress)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke(.1f);
                if (_raster != null) _closePieces = SeamPadding.ApplyPixels(rgba, _raster, _width, _height, cancel);
                progress?.Invoke(.75f);
                cancel.ThrowIfCancellationRequested();
                var png = MapPixels.EncodeRgba(rgba, _width, _height);
                cancel.ThrowIfCancellationRequested();
                if (png == null || png.Length == 0) throw new IOException("Could not encode the edited PNG.");
                return png;
            }
        }

        internal static Result WriteBake(TextureSlot slot, Material material, List<TargetResolver.SlotRef> users, TextureRecipe recipeCopy,
            OutputMode mode, List<TargetResolver.TextureUser> also, byte[] png, string warning, System.Func<string, float, bool> progress,
            System.Func<bool> targetMatches, System.Action beginApply)
        {
            bool TargetMatches() => targetMatches == null || targetMatches();
            var result = new Result { alsoMaterialPaths = new List<string>(), paddingWarning = warning };
            also ??= new List<TargetResolver.TextureUser>();
            if (targetMatches != null) progress?.Invoke(Loc.T("Writing files…"), WritingProgress);
            if (!TargetMatches()) return new Result { editChanged = true };

            string dir = SaveFolder("Output");
            if (!TargetMatches()) return new Result { editChanged = true };
            string baseName = TextureOutputName(slot);
            string tag = PickTag(baseName, (dir, ".png"), (dir, "_recipe.asset"));
            result.pngPath = $"{dir}/{baseName}{tag}.png";
            result.recipePath = $"{dir}/{baseName}{tag}_recipe.asset";
            File.WriteAllBytes(result.pngPath, png);
            AssetDatabase.ImportAsset(result.pngPath, ImportAssetOptions.ForceUpdate);
            CopyImporterSettings(slot.assetPath, result.pngPath);
            var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(result.pngPath);
            if (newTex == null) throw new IOException($"Unity could not import {result.pngPath}.");
            if (mode == OutputMode.PngOnly)
                progress?.Invoke(Loc.T("Saving the edit…"), AssignmentProgress);

            recipeCopy.hideFlags = HideFlags.None;
            recipeCopy.name = baseName + tag + "_recipe";
            recipeCopy.bakedTexture = newTex;
            recipeCopy.bakedMaterial = material;
            AssetDatabase.CreateAsset(recipeCopy, result.recipePath);
            if (!EditorUtility.IsPersistent(recipeCopy)) throw new IOException($"Unity could not create {result.recipePath}.");

            if (mode != OutputMode.PngOnly)
                progress?.Invoke(Loc.T("Assigning to the avatar…"), AssignmentProgress);
            if (!TargetMatches())
            {
                result.editChanged = true;
                return result;
            }

            var included = also.Where(a => a.material != null && a.material != material).GroupBy(a => a.material);
            if (mode == OutputMode.DuplicateMaterial)
            {
                string path = FreeMaterialPath(material);
                var copy = CreateMaterialCopy(material, path);
                SetBakedTexture(copy, slot.properties, recipeCopy.layers);
                var copies = new List<(Material original, Material copy, IEnumerable<TargetResolver.SlotRef> users)>();
                foreach (var group in included)
                {
                    string includedPath = FreeMaterialPath(group.Key);
                    var includedCopy = CreateMaterialCopy(group.Key, includedPath);
                    SetBakedTexture(includedCopy, group.SelectMany(a => a.properties).Distinct());
                    copies.Add((group.Key, includedCopy, group.Select(a => a.slot)));
                    result.alsoMaterialPaths.Add(includedPath);
                }
                beginApply?.Invoke();
                if (!TargetMatches())
                {
                    result.editChanged = true;
                    return result;
                }
                result.materialPath = path;
                recipeCopy.bakedMaterial = copy;
                recipeCopy.sourceMaterial = material;
                var assignments = users.Select(user => (user, copy))
                    .Concat(copies.SelectMany(entry => entry.users.Select(user => (user, entry.copy))));
                AssignMaterials(assignments, "Assign edited material");
                foreach (var entry in copies) RememberIncludedCopy(entry.original, entry.copy);
            }
            else if (mode == OutputMode.UpdateOriginalMaterial)
            {
                beginApply?.Invoke();
                if (!TargetMatches()) { result.editChanged = true; return result; }
                UpdateMaterial(material, slot.properties, recipeCopy.layers);
                result.materialPath = AssetDatabase.GetAssetPath(material);
                foreach (var group in included)
                {
                    RememberIncludedCopy(group.Key, group.Key);
                    UpdateMaterial(group.Key, group.SelectMany(a => a.properties).Distinct());
                    result.alsoMaterialPaths.Add(AssetDatabase.GetAssetPath(group.Key));
                }
            }

            void RememberIncludedCopy(Material original, Material baked)
            {
                if (recipeCopy.includedLayout == null) return;
                foreach (var part in recipeCopy.includedLayout.included)
                    if (part.sourceMaterial == original) part.bakedMaterial = baked;
            }

            void SetBakedTexture(Material target, IEnumerable<string> properties, List<Layer> layers = null)
            {
                foreach (var p in properties) target.SetTexture(p, newTex);
                if (layers != null) DecalReader.TurnOffFlattened(target, layers);
                EditorUtility.SetDirty(target);
            }

            void UpdateMaterial(Material target, IEnumerable<string> properties, List<Layer> layers = null)
            {
                Undo.RecordObject(target, "Point material at edited texture");
                MaterialWriter.EnsureEditable(new[] { target });
                SetBakedTexture(target, properties, layers);
            }

            var changedPaths = new List<string>(result.alsoMaterialPaths);
            if (!string.IsNullOrEmpty(result.materialPath)) changedPaths.Insert(0, result.materialPath);
            var changed = changedPaths.Select(AssetDatabase.LoadAssetAtPath<Material>).Where(x => x != null).ToArray();
            if (changed.Length > 0)
            {
                MaterialWriter.AfterLilToonEdit(changed);
                MaterialWriter.FixPoiyomiKeywords(changed);
            }

            EditorUtility.SetDirty(recipeCopy);
            return result;
        }

        static void AssignMaterials(IEnumerable<(TargetResolver.SlotRef slot, Material material)> assignments, string undoName)
        {
            foreach (var group in assignments.Where(a => a.slot.renderer != null).GroupBy(a => a.slot.renderer))
            {
                var renderer = group.Key;
                var mats = renderer.sharedMaterials;
                bool changed = false;
                foreach (var (slot, material) in group)
                {
                    int index = slot.materialIndex;
                    if (index < 0 || index >= mats.Length || mats[index] == material) continue;
                    mats[index] = material;
                    changed = true;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, undoName);
                renderer.sharedMaterials = mats;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        internal static string CopyMaterial(Material m, List<TargetResolver.SlotRef> users, System.Func<bool> canAssign,
                                            System.Action<Material> prepareCopy = null, System.Action afterAssign = null,
                                            System.Func<Material, bool> validatePreparedCopy = null)
        {
            if (m == null || !canAssign()) return null;
            string path;
            try { path = FreeMaterialPath(m); }
            catch (IOException) { return null; }
            int expectedCopy = 0;
            System.Action<Material> recordCopy = validatePreparedCopy == null ? null : staged => expectedCopy = staged.ComputeCRC();
            var copy = CreateMaterialCopy(m, path, recordCopy);
            bool keepCopy = false;
            try
            {
                bool SameCopy() => copy != null && AssetDatabase.LoadAssetAtPath<Material>(path) == copy && AssetDatabase.GetAssetPath(copy) == path;
                if (!SameCopy() || validatePreparedCopy != null && copy.ComputeCRC() != expectedCopy) return null;
                Undo.FlushUndoRecordObjects();
                if (!canAssign()) return null;
                if (prepareCopy != null)
                {
                    prepareCopy(copy);
                    Undo.FlushUndoRecordObjects();
                    if (!canAssign()) return null;
                }
                if (validatePreparedCopy != null)
                {
                    if (!validatePreparedCopy(copy)) return null;
                    expectedCopy = copy.ComputeCRC();
                    AssetDatabase.SaveAssetIfDirty(copy);
                    if (!SameCopy() || copy.ComputeCRC() != expectedCopy || !validatePreparedCopy(copy) || !canAssign()) return null;
                }
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                keepCopy = true;
                AssignMaterials(users.Select(user => (user, copy)), "Make editable material copy");
                afterAssign?.Invoke();
                Undo.SetCurrentGroupName("Make editable material copy");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Undo.IncrementCurrentGroup();
                if (validatePreparedCopy == null) AssetDatabase.SaveAssetIfDirty(copy);
                return path;
            }
            finally
            {
                if (!keepCopy && copy != null)
                {
                    if (!EditorUtility.IsPersistent(copy)) Object.DestroyImmediate(copy);
                    else if (AssetDatabase.LoadAssetAtPath<Material>(path) == copy && AssetDatabase.GetAssetPath(copy) == path)
                        AssetDatabase.DeleteAsset(path);
                }
            }
        }

        static Material CreateMaterialCopy(Material source, string path, System.Action<Material> beforeCreate = null)
        {
            var copy = new Material(source) { name = Path.GetFileNameWithoutExtension(path) };
            MaterialWriter.EnsureEditable(new[] { copy });
            beforeCreate?.Invoke(copy);
            AssetDatabase.CreateAsset(copy, path);
            if (copy != null && AssetDatabase.IsMainAsset(copy) && AssetDatabase.GetAssetPath(copy) == path &&
                ShaderProfiles.Detect(copy.shader) == ShaderFamily.Poiyomi)
                AssetDatabase.SetLabels(copy, new[] { PoiyomiCopyLabel });
            return copy;
        }

        const string PoiyomiCopyLabel = "AvistryMaterialCopy";

        internal static bool IsVerifiedPoiyomiCopy(Material material) => material != null &&
            AssetDatabase.IsMainAsset(material) && AssetDatabase.GetAssetPath(material).StartsWith("Assets/", System.StringComparison.Ordinal) &&
            AssetDatabase.GetLabels(material).Contains(PoiyomiCopyLabel);

        internal static bool IsToolCopy(Material m)
        {
            if (m == null) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/") && Regex.IsMatch(m.name, @"_ats(_\d+)?$");
        }

        internal static string StripAts(string name) => string.IsNullOrEmpty(name) ? name : Regex.Replace(name, @"_ats(_\d+)?$", "");

        static string TextureOutputName(TextureSlot slot)
        {
            string name = StripAts(Path.GetFileNameWithoutExtension(slot.assetPath));
            if (!string.IsNullOrEmpty(name)) return name;
            return SafeOutputName(slot.texture != null ? slot.texture.name : null, "Texture");
        }

        static string SafeOutputName(string rawName, string fallback)
        {
            string name = StripAts(rawName) ?? "";
            const string invalid = "<>:\"/\\|?*";
            name = string.Concat(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)).Trim(' ', '.');
            return name.Length != 0 ? name : fallback;
        }

        static bool PathTaken(string path) => File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));

        static string PickTag(string baseName, params (string dir, string ext)[] files)
        {
            for (int n = 1; n < 10000; n++)
            {
                string tag = n == 1 ? "_ats" : "_ats_" + n;
                if (files.All(f => !PathTaken($"{f.dir}/{baseName}{tag}{f.ext}"))) return tag;
            }
            return "_ats_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        internal static string FreeMaterialPath(Material m, bool createFolder = true)
        {
            string dir = SaveFolder("Output", createFolder);
            string baseName = SafeOutputName(m.name, "Material");
            return $"{dir}/{baseName}{PickTag(baseName, (dir, ".mat"))}.mat";
        }

        internal static string SaveFolder(string name, bool createFolder = true)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new IOException("Invalid Avistry save folder.");
            string folder = $"{ProjectFolder}/{name}";
            if (createFolder)
            {
                EnsureProjectFolder("Assets", "Avistry", ProjectFolder);
                EnsureProjectFolder(ProjectFolder, name, folder);
            }
            return folder;
        }

        static void EnsureProjectFolder(string parent, string name, string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            if (File.Exists(path) || Directory.Exists(path) ||
                AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(parent, name)) != path)
                throw new IOException("Unity could not create the Avistry save folder.");
        }

        internal static string WritableFolder(string assetPath, bool createFolder = true)
        {
            string dir = string.IsNullOrEmpty(assetPath) ? null : Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && dir.StartsWith("Assets")) return dir;
            if (createFolder && !AssetDatabase.IsValidFolder(FallbackFolder)) AssetDatabase.CreateFolder("Assets", "AvatarTextureStudio Output");
            return FallbackFolder;
        }

        internal static void CopyImporterSettings(string srcPath, string dstPath)
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
            if (changed) dst.SaveAndReimport();
        }

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
