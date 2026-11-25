using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        bool CaptureIncludedLayout(out TextureRecipe.IncludedLayout layout)
        {
            layout = null;
            if (_alsoUsers.Count == 0) return true;
            var slot = CurrentSlot;
            var root = TargetResolver.AvatarRoot(_renderer);
            if (_needsRebuild || _uv == null || slot == null || root == null || _material == null ||
                Assigned(_renderer, _materialIndex) != _material || !MaterialMatches(_material, slot.texture, slot.properties, slot.uvChannel, true) ||
                _uv.parts.Any(part => part.renderer == null || MeshOf(part.renderer) != part.mesh)) return false;
            bool needsBindings = _alsoUsers.Exists(user =>
                TryGetIncludedUvChannel(user.slot.renderer, user.slot.materialIndex, slot.texture, out int channel) &&
                (channel != slot.uvChannel || _uv.SlotMissingUv(user.slot.renderer, user.slot.materialIndex)));
            var primary = StylePackTargets.CaptureBinding(root, _renderer, _materialIndex);
            var primarySlots = _users.Select(user => StylePackTargets.CaptureBinding(root, user.renderer, user.materialIndex)).ToArray();
            if (primary == null || primarySlots.Length == 0 || primarySlots.Any(binding => binding == null)) return !needsBindings;

            var included = new TextureRecipe.IncludedPart[_alsoUsers.Count];
            for (int i = 0; i < included.Length; i++)
            {
                var user = _alsoUsers[i];
                var binding = StylePackTargets.CaptureBinding(root, user.slot.renderer, user.slot.materialIndex);
                if (binding == null) return !needsBindings;
                if (user.material == null || user.properties.Count == 0 ||
                    Assigned(user.slot.renderer, user.slot.materialIndex) != user.material ||
                    !TargetResolver.TextureProperties(user.material, slot.texture).SequenceEqual(user.properties)) return false;
                int? actualChannel = TargetResolver.CommonUvChannel(user.material, user.properties);
                if (actualChannel != user.slot.uvChannel) return false;
                included[i] = new TextureRecipe.IncludedPart
                {
                    binding = binding, sourceMaterial = EditorUtility.IsPersistent(user.material) ? user.material : null,
                    properties = user.properties.ToArray(), uvChannel = actualChannel ?? -1,
                    channel = TryGetIncludedUvChannel(user.slot.renderer, user.slot.materialIndex, slot.texture, out int channel) ? channel : -1,
                };
            }
            layout = new TextureRecipe.IncludedLayout
            {
                fingerprint = _uv.CombinedLayout, primary = primary, primarySlots = primarySlots,
                primaryProperties = slot.properties.ToArray(), primaryChannel = slot.uvChannel, included = included,
            };
            return true;
        }

        void ShowIncludedEditMismatch() => Toast(Loc.T("This edit’s saved parts or UV layout could not be matched. Restore its parts and source texture, then open it again."),
            MessageType.Warning, 7f);

        void ShowIncludedSaveProblem() => Toast(Loc.T("The included parts changed or could not be identified. Check the parts in Pick, then save again."),
            MessageType.Warning, 7f);

        bool FindIncludedEditSource(TextureRecipe recipe, out (Renderer part, int index, Material material) found)
        {
            found = default;
            var binding = recipe.includedLayout?.primary;
            if (binding == null) return false;
            foreach (var root in OpenDropParts().Select(TargetResolver.AvatarRoot).Where(root => root != null).Distinct())
            {
                var part = StylePackTargets.FindRenderer(root, binding.rendererPath, binding.rendererKind, out _);
                if (part == null || !TryPlanIncludedEdit(recipe, part, binding.materialIndex, out _, out _)) continue;
                if (found.part != null) return false;
                found = (part, binding.materialIndex, Assigned(part, binding.materialIndex));
            }
            return found.part != null;
        }

        readonly struct SavedPart
        {
            internal readonly TargetResolver.SlotRef slot;
            internal readonly Material current, original;
            internal readonly string[] restoreProperties;

            internal SavedPart(TargetResolver.SlotRef slot, Material current, Material original, string[] restoreProperties)
            {
                this.slot = slot;
                this.current = current;
                this.original = original;
                this.restoreProperties = restoreProperties;
            }

            internal bool NeedsRestore => current != original || restoreProperties != null;
        }

        static bool TryPlanIncludedEdit(TextureRecipe recipe, Renderer primary, int primaryIndex,
            out List<SavedPart> parts, out int primaryCount)
        {
            parts = null;
            primaryCount = 0;
            var saved = recipe.includedLayout;
            var root = TargetResolver.AvatarRoot(primary);
            if (!StylePackTargets.InCurrentStage(root) || recipe.sourceTexture == null || saved?.primary == null || saved.primarySlots == null ||
                saved.included == null || saved.included.Length == 0 || string.IsNullOrEmpty(saved.fingerprint) ||
                (uint)saved.primaryChannel > 3 || saved.primary.materialIndex != primaryIndex ||
                ResolveRenderer(saved.primary) != primary) return false;

            var ordered = root.GetComponentsInChildren<Renderer>(true).Select((renderer, order) => (renderer, order))
                .ToDictionary(pair => pair.renderer, pair => pair.order);
            var seen = new HashSet<(Renderer, int)>();
            var resolved = new List<SavedPart>(saved.primarySlots.Length + saved.included.Length);
            var primaryMaterial = Assigned(primary, primaryIndex);
            if (primaryMaterial == null || recipe.sourceMaterial == null && primaryMaterial == recipe.bakedMaterial &&
                !MaterialMatches(primaryMaterial, recipe.sourceTexture, saved.primaryProperties, saved.primaryChannel, true)) return false;
            var sourceMaterial = recipe.sourceMaterial != null ? recipe.sourceMaterial : primaryMaterial;
            var primaryUsers = TargetResolver.FindMaterialUsers(root, sourceMaterial);
            if (recipe.bakedMaterial != null && recipe.bakedMaterial != sourceMaterial)
                primaryUsers.AddRange(TargetResolver.FindMaterialUsers(root, recipe.bakedMaterial));
            if (primaryUsers.Count != saved.primarySlots.Length) return false;

            foreach (var binding in saved.primarySlots)
                if (!TryPart(binding, sourceMaterial, recipe.bakedMaterial, saved.primaryProperties, saved.primaryChannel, -1, true)) return false;
            if (!seen.Contains((primary, primaryIndex)) || !InHierarchyOrder(resolved)) return false;
            primaryCount = resolved.Count;

            foreach (var entry in saved.included)
                if (entry == null || !TryPart(entry.binding, entry.sourceMaterial, entry.bakedMaterial, entry.properties,
                    entry.uvChannel, entry.channel, false)) return false;
            if (!InHierarchyOrder(resolved.Skip(primaryCount))) return false;
            parts = resolved;
            return true;

            Renderer ResolveRenderer(StylePack.Binding binding)
            {
                var renderer = StylePackTargets.FindRenderer(root, binding.rendererPath, binding.rendererKind, out _);
                return renderer != null && TargetResolver.AvatarRoot(renderer) == root ? renderer : null;
            }

            bool TryPart(StylePack.Binding binding, Material original, Material baked, string[] properties, int actualChannel,
                int channel, bool isPrimary)
            {
                if (binding == null || properties == null || properties.Length == 0 || properties.Any(string.IsNullOrEmpty) ||
                    properties.Distinct(StringComparer.Ordinal).Count() != properties.Length || channel < -1 || channel > 3 ||
                    actualChannel < -1 || actualChannel > 3) return false;
                var renderer = ResolveRenderer(binding);
                if (renderer == null || MeshOf(renderer) == null || !seen.Add((renderer, binding.materialIndex))) return false;
                var current = Assigned(renderer, binding.materialIndex);
                if (current == null) return false;
                if (original == null)
                {
                    if (baked != null) return false;
                    original = current;
                }
                if (!isPrimary && original == sourceMaterial) return false;

                bool currentSource = current == original && MaterialMatches(current, recipe.sourceTexture, properties, actualChannel, isPrimary);
                string[] restoreProperties = null;
                if (!currentSource)
                {
                    if (recipe.bakedTexture == null || current != baked ||
                        !MaterialMatches(current, recipe.bakedTexture, properties, actualChannel, isPrimary)) return false;
                    if (current == original) restoreProperties = properties;
                    else if (!MaterialMatches(original, recipe.sourceTexture, properties, actualChannel, isPrimary)) return false;
                }
                resolved.Add(new SavedPart(new TargetResolver.SlotRef
                {
                    renderer = renderer, materialIndex = binding.materialIndex, uvChannel = channel < 0 ? null : (int?)channel,
                }, current, original, restoreProperties));
                return true;
            }

            bool InHierarchyOrder(IEnumerable<SavedPart> range) => range.Select(part => part.slot)
                .SequenceEqual(range.OrderBy(part => ordered[part.slot.renderer]).ThenBy(part => part.slot.materialIndex).Select(part => part.slot));
        }

        static Material Assigned(Renderer renderer, int index)
        {
            var materials = renderer.sharedMaterials;
            return (uint)index < (uint)materials.Length ? materials[index] : null;
        }

        static bool MaterialMatches(Material material, Texture2D texture, IReadOnlyList<string> properties, int channel, bool isPrimary)
        {
            if (material == null || material.shader == null || properties == null) return false;
            if (isPrimary)
                return TargetResolver.GetTextureSlots(material).Any(slot => slot.texture == texture && slot.role != TextureRole.Normal &&
                    slot.uvChannel == channel && slot.properties.SequenceEqual(properties));
            return TargetResolver.TextureProperties(material, texture).SequenceEqual(properties) &&
                (TargetResolver.CommonUvChannel(material, properties) ?? -1) == channel;
        }

        void OpenIncludedEdit(TextureRecipe source, Renderer primary, int index, Func<bool> contextMatches)
        {
            if (!contextMatches() || !TryPlanIncludedEdit(source, primary, index, out var parts, out _)) { ShowIncludedEditMismatch(); return; }
            bool restores = parts.Exists(part => part.NeedsRestore);
            if (restores)
            {
                string message = Loc.T("Restore the original texture and materials on the saved parts, then open the edit?");
                if (parts.Exists(part => part.restoreProperties != null))
                    message += "\n\n" + Loc.T("Other avatars using these materials will also switch back to the original texture.");
                if (!StudioDialog.Ask(Loc.T("Open saved edit"), message, Msg.BtnGoBack, Msg.BtnCancel)) return;
            }
            if (!ConfirmReplaceSavedEdit(source, contextMatches)) return;

            WithTargetUndo("Load saved edit", () =>
            {
                if (!contextMatches() || !TryPlanIncludedEdit(source, primary, index, out var live, out int primaryCount) ||
                    !parts.SequenceEqual(live)) { ShowIncludedEditMismatch(); return; }
                var saved = source.includedLayout;
                var geometry = MeshUV.Build(live.Select(part => part.slot).ToList(), saved.primaryChannel);
                if (geometry == null || geometry.CombinedLayout != saved.fingerprint)
                {
                    geometry?.ReleaseSurfaceMesh();
                    ShowIncludedEditMismatch();
                    return;
                }

                int group = Undo.GetCurrentGroup();
                try
                {
                    Undo.RecordObjects(new UnityEngine.Object[] { this, _recipe }, "Load saved edit");
                    RestoreIncludedMaterials(live, source.sourceTexture);
                    SetRenderer(primary);
                    _materialIndex = index;
                    _selection.Clear();
                    ClearFaceSelection();
                    _alsoRenderers.Clear();
                    _alsoSlots.Clear();
                    _alsoUvs.Clear();
                    foreach (var part in live.Skip(primaryCount))
                    {
                        _alsoRenderers.Add(part.slot.renderer);
                        _alsoSlots.Add(part.slot.materialIndex);
                        if (part.slot.uvChannel is int channel)
                            BindIncludedUv(part.slot.renderer, part.slot.materialIndex, source.sourceTexture, channel);
                    }
                    _pendingTexture = source.sourceTexture;
                    RebuildUsing(geometry);
                    if (_comp != null && _uv == geometry && CurrentSlot?.texture == source.sourceTexture &&
                        LiveIncludedEditMatches(live, primaryCount))
                    {
                        LoadRecipeLayers(source, asCopy: false, verifiedIncludedLayout: true);
                        return;
                    }
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
                if (_uv != geometry) geometry.ReleaseSurfaceMesh();
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                Rebuild();
                ShowIncludedEditMismatch();
            });
        }

        bool LiveIncludedEditMatches(List<SavedPart> planned, int primaryCount) =>
            _users.Select(user => (user.renderer, user.materialIndex)).SequenceEqual(
                planned.Take(primaryCount).Select(part => (part.slot.renderer, part.slot.materialIndex))) &&
            _alsoUsers.Select(user => (user.slot.renderer, user.slot.materialIndex, user.material)).SequenceEqual(
                planned.Skip(primaryCount).Select(part => (part.slot.renderer, part.slot.materialIndex, part.original)));

        static void RestoreIncludedMaterials(List<SavedPart> parts, Texture2D source)
        {
            foreach (var group in parts.Where(part => part.current != part.original).GroupBy(part => part.slot.renderer))
            {
                var renderer = group.Key;
                Undo.RecordObject(renderer, "Restore original material");
                var materials = renderer.sharedMaterials;
                foreach (var part in group) materials[part.slot.materialIndex] = part.original;
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (var group in parts.Where(part => part.restoreProperties != null).GroupBy(part => part.original))
            {
                var material = group.Key;
                Undo.RecordObject(material, "Restore original texture");
                foreach (var property in group.SelectMany(part => part.restoreProperties).Distinct()) material.SetTexture(property, source);
                EditorUtility.SetDirty(material);
            }
        }
    }
}
