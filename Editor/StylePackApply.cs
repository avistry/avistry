using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Avistry.Textures
{
    internal static class StylePackApply
    {
        internal struct Receipt
        {
            internal int EntryIndex, IssueCount;
            internal string PngPath, MaterialPath, RecipePath, PaddingWarning;
        }

        internal sealed class Result
        {
            internal Receipt[] Receipts = Array.Empty<Receipt>();
            internal string Problem;
            internal bool Cancelled;
        }

        sealed class MaterialPlan
        {
            internal Renderer Renderer;
            internal int Index;
            internal Material Source, Copy;
            internal string Path;
            internal int CopyCrc;
            internal readonly HashSet<string> Properties = new HashSet<string>(StringComparer.Ordinal);

            internal bool CopyMatches() => Copy != null && Copy.ComputeCRC() == CopyCrc;
        }

        sealed class EntryPlan
        {
            internal int Index;
            internal StylePack.Entry Entry;
            internal StylePackTargets.Resolution Target;
            internal MaterialPlan Material;
            internal Hash128 TextureHash;
            internal uint TextureVersion;

            internal bool StillMatches() => Target.StillMatches() && Target.Slot.texture.updateCount == TextureVersion &&
                AssetDatabase.GetAssetDependencyHash(Target.Slot.assetPath) == TextureHash;
        }

        sealed class Output
        {
            internal string Path;
            internal Object Asset;
            internal byte[] PngDigest;

            internal bool Matches() =>
                (ReferenceEquals(Asset, null) ||
                 Asset != null && AssetDatabase.GetAssetPath(Asset) == Path && AssetDatabase.LoadMainAssetAtPath(Path) == Asset) &&
                (PngDigest == null || File.Exists(Path) && DigestFile(Path).SequenceEqual(PngDigest));

            internal void RollBack()
            {
                if (!Matches()) return;
                if (!AssetDatabase.DeleteAsset(Path) && PngDigest != null && Matches()) File.Delete(Path);
            }
        }

        internal static string Check(StylePackTargets.Resolution target)
        {
            if (target?.Problem != null) return target.Problem;
            if (target?.Renderer == null || target.Material == null || target.Slot?.texture == null)
                return Loc.T("Choose a matching part before applying this edit.");
            if (MaterialWriter.IsSectionLocked(target.Material) || MaterialWriter.IsPoiyomiLocked(target.Material))
                return Loc.T("Unlock the material before applying this style pack edit.");
            if (target.Material.shader == null || !target.Material.shader.isSupported)
                return Loc.T("This shader can't be used for style pack apply.");
            if (target.Slot.featureOff)
                return Loc.T("This texture is turned off in the recipient material.");
            if (target.Slot.mixedRoles)
                return Loc.T("This texture is shared by different shader channels.");
            return null;
        }

        internal static Result Apply(string path, StylePack.Manifest manifest, Transform root, IReadOnlyList<int> entryIndices,
                                     Func<string, float, bool> progress = null, Func<bool> stillReviewed = null)
        {
            if (root == null || manifest?.entries == null || entryIndices == null || entryIndices.Count == 0)
                return Problem(Loc.T("Choose at least one matching edit to apply."));
            var plans = new List<EntryPlan>();
            var materials = new Dictionary<(Renderer, int), MaterialPlan>();
            var outputs = new List<Output>();
            var receipts = new List<Receipt>();
            bool committed = false;
            try
            {
                if (stillReviewed?.Invoke() == false) return Changed();
                using var pack = File.OpenRead(path);
                foreach (int index in entryIndices)
                {
                    if (index < 0 || index >= manifest.entries.Length) return Changed();
                    var entry = manifest.entries[index];
                    var target = StylePackTargets.Resolve(root, entry);
                    var plan = new EntryPlan { Index = index, Entry = entry, Target = target };
                    plans.Add(plan);
                    string problem = Check(target);
                    if (problem != null) return Problem(problem);
                    plan.TextureVersion = target.Slot.texture.updateCount;
                    plan.TextureHash = AssetDatabase.GetAssetDependencyHash(target.Slot.assetPath);
                    var key = (target.Renderer, entry.materialIndex);
                    if (!materials.TryGetValue(key, out var material))
                    {
                        material = new MaterialPlan { Renderer = key.Item1, Index = key.Item2, Source = target.Material };
                        materials.Add(key, material);
                    }
                    if (target.Slot.properties.Any(property => !material.Properties.Add(property)))
                        return Problem(Loc.T("Two selected edits change the same texture on this part. Select one."));
                    plan.Material = material;
                }

                for (int i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i];
                    if (progress?.Invoke(Loc.F("Preparing edit {0} of {1}…", i + 1, plans.Count), .85f * i / plans.Count) == true)
                        return new Result { Cancelled = true };
                    if (!plan.StillMatches()) return Changed();
                    using var edit = StylePack.ReadEdit(path, manifest, plan.Entry, plan.Target.LayoutMatches);
                    if (!edit.Layers.Any(layer => layer.enabled && layer.opacity > 0f))
                        return Problem(Loc.T("Style pack apply needs at least one enabled layer on each selected part."));
                    var slot = plan.Target.Slot;
                    var texture = Exporter.RenderRecipe(slot, edit.Layers, plan.Target.Geometry, slot.role == TextureRole.Color,
                        Exporter.PadSeams, out string warning);
                    byte[] png;
                    try { png = texture.EncodeToPNG(); }
                    finally { Object.DestroyImmediate(texture); }
                    if (progress?.Invoke(Loc.F("Saving edit {0} of {1}…", i + 1, plans.Count), .85f * (i + .7f) / plans.Count) == true)
                        return new Result { Cancelled = true };
                    if (!plan.StillMatches()) return Changed();
                    Exporter.SaveFolder("Output");
                    var paths = Exporter.PredictPaths(slot, null, OutputMode.PngOnly);
                    var baked = SaveTexture(slot, paths.pngPath, png, outputs);
                    var material = plan.Material;
                    if (ReferenceEquals(material.Copy, null)) SaveMaterial(material, outputs);
                    if (!material.CopyMatches()) return Changed();
                    foreach (string property in slot.properties) material.Copy.SetTexture(property, baked);
                    material.CopyCrc = material.Copy.ComputeCRC();
                    SaveRecipe(root, plan, edit, baked, paths.recipePath, outputs);
                    receipts.Add(new Receipt
                    {
                        EntryIndex = plan.Index, IssueCount = edit.Issues.Count, PngPath = paths.pngPath,
                        MaterialPath = material.Path, RecipePath = paths.recipePath, PaddingWarning = warning,
                    });
                }
                foreach (var material in materials.Values)
                {
                    if (!material.CopyMatches()) return Changed();
                    EditorUtility.SetDirty(material.Copy);
                    AssetDatabase.SaveAssetIfDirty(material.Copy);
                }
                if (progress?.Invoke(Loc.T("Assigning to the avatar…"), .95f) == true) return new Result { Cancelled = true };
                if (stillReviewed?.Invoke() == false || plans.Any(plan => !plan.StillMatches()) ||
                    materials.Values.Any(material => !material.CopyMatches()) ||
                    outputs.Any(asset => !asset.Matches())) return Changed();
                AssignMaterials(materials.Values);
                committed = true;
                return new Result { Receipts = receipts.ToArray() };
            }
            catch (Exception failure)
            {
                Debug.LogWarning($"[Avistry] Could not apply a style pack ({failure.GetType().Name}).");
                return Problem(Loc.T("Couldn't apply this style pack. Your avatar is unchanged."));
            }
            finally
            {
                foreach (var plan in plans) plan.Target.Geometry?.ReleaseSurfaceMesh();
                if (!committed)
                    for (int i = outputs.Count - 1; i >= 0; i--)
                        try { outputs[i].RollBack(); }
                        catch (Exception failure)
                        {
                            Debug.LogWarning($"[Avistry] Could not remove a staged style pack asset ({failure.GetType().Name}).");
                        }
            }
        }

        static Result Problem(string message) => new Result { Problem = message };
        static Result Changed() => Problem(Loc.T("The recipient or pack changed. Review the selected edits again."));

        static byte[] DigestFile(string path)
        {
            using var file = File.OpenRead(path);
            using var hash = SHA256.Create();
            return hash.ComputeHash(file);
        }

        static Texture2D SaveTexture(TextureSlot slot, string path, byte[] png, List<Output> outputs)
        {
            using var hash = SHA256.Create();
            var output = new Output { Path = path, PngDigest = hash.ComputeHash(png) };
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                try { file.Write(png, 0, png.Length); }
                catch
                {
                    file.Dispose();
                    File.Delete(path);
                    throw;
                }
            }
            outputs.Add(output);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || !output.Matches()) throw new IOException("Could not import the staged texture.");
            output.Asset = texture;
            Exporter.CopyImporterSettings(slot.assetPath, path);
            if (!output.Matches()) throw new IOException("The staged texture changed during import.");
            return texture;
        }

        static void SaveMaterial(MaterialPlan plan, List<Output> outputs)
        {
            plan.Path = Exporter.FreeMaterialPath(plan.Source);
            var copy = new Material(plan.Source) { name = Path.GetFileNameWithoutExtension(plan.Path) };
            plan.CopyCrc = copy.ComputeCRC();
            SaveNewAsset(copy, plan.Path, outputs);
            plan.Copy = copy;
        }

        static void SaveRecipe(Transform root, EntryPlan plan, StylePack.Edit edit, Texture2D baked, string path, List<Output> outputs)
        {
            var recipe = ScriptableObject.CreateInstance<TextureRecipe>();
            recipe.name = Path.GetFileNameWithoutExtension(path);
            recipe.sourceTexture = plan.Target.Slot.texture;
            recipe.sourceMaterial = plan.Target.Material;
            recipe.bakedTexture = baked;
            recipe.bakedMaterial = plan.Material.Copy;
            recipe.rendererPath = string.Join("/", plan.Entry.rendererPath);
            recipe.materialIndex = plan.Entry.materialIndex;
            recipe.draftTarget = StylePackTargets.Describe(root, plan.Target.Renderer, plan.Entry.materialIndex,
                plan.Target.Slot, plan.Target.Geometry, plan.Entry.name);
            recipe.layers = edit.Layers;
            var artwork = recipe.layers.OfType<ImageLayer>().Select(layer => (layer, image: layer.image, mask: layer.mask)).ToArray();
            foreach (var (layer, _, _) in artwork) { layer.image = null; layer.mask = null; }
            SaveNewAsset(recipe, path, outputs);
            foreach (var texture in edit.Textures.Values)
            {
                if (texture == null) continue;
                texture.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(texture, recipe);
                if (AssetDatabase.GetAssetPath(texture) != path) throw new IOException("Could not save the staged artwork.");
            }
            foreach (var (layer, image, mask) in artwork) { layer.image = image; layer.mask = mask; }
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
        }

        static void SaveNewAsset(Object asset, string path, List<Output> outputs)
        {
            try
            {
                if (File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    throw new IOException("The staged output is no longer free.");
                AssetDatabase.CreateAsset(asset, path);
                if (AssetDatabase.GetAssetPath(asset) != path) throw new IOException("Could not create the staged asset.");
            }
            finally
            {
                if (!EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
                else outputs.Add(new Output { Asset = asset, Path = path });
            }
        }

        static void AssignMaterials(IEnumerable<MaterialPlan> plans)
        {
            const string undoName = "Apply style pack";
            var renderers = plans.GroupBy(plan => plan.Renderer).ToArray();
            var assignments = new List<(Renderer renderer, Material[] materials)>();
            foreach (var group in renderers)
            {
                var renderer = group.Key;
                var materials = renderer.sharedMaterials;
                foreach (var plan in group)
                {
                    if (plan.Copy == null || plan.Index >= materials.Length || materials[plan.Index] != plan.Source)
                        throw new InvalidOperationException("The selected material changed before assignment.");
                    materials[plan.Index] = plan.Copy;
                }
                assignments.Add((renderer, materials));
            }
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int groupId = Undo.GetCurrentGroup();
            try
            {
                Undo.SetCurrentGroupName(undoName);
                Undo.RecordObjects(assignments.Select(a => (Object)a.renderer).ToArray(), undoName);
                foreach (var (renderer, materials) in assignments)
                {
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(groupId);
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(groupId);
                throw;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }
    }
}
