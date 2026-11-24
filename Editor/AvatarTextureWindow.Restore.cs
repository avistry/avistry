using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        void OnHierarchyChanged()
        {
            if (_renderer == null && !string.IsNullOrEmpty(_rendererId)) TryRestoreRenderer();
        }

        void TryRestoreRenderer()
        {
            RepairRecipe("restore");
            if (_renderer != null) { if (string.IsNullOrEmpty(_rendererId)) _rendererId = GlobalObjectId.GetGlobalObjectIdSlow(_renderer).ToString(); return; }
            if (string.IsNullOrEmpty(_rendererId) || !GlobalObjectId.TryParse(_rendererId, out var id)) return;
            var r = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Renderer;
            if (r == null) return;
            _renderer = r;
            _needsRebuild = true;
            Repaint();
        }

        void ReopenNeedsEditableTexture() => Toast(Loc.T("Choose a colour or mask texture before opening a saved edit."), MessageType.Info);

        Func<bool> CaptureSavedEditContext(TextureRecipe source, TextureSlot slot)
        {
            var part = _renderer;
            var material = _material;
            var shader = material != null ? material.shader : null;
            var preview = _comp;
            var recipe = _recipe;
            int materialIndex = _materialIndex;
            int revision = _historyRevision;
            var sourceTexture = source.sourceTexture;
            var sourceMaterial = source.sourceMaterial;
            var bakedTexture = source.bakedTexture;
            var bakedMaterial = source.bakedMaterial;
            return () =>
            {
                if (this == null || part == null || material == null || _needsRebuild || _renderer != part || _material != material ||
                    _materialIndex != materialIndex || CurrentSlot != slot || _comp != preview || _recipe != recipe || _historyRevision != revision ||
                    source == null || source.sourceTexture != sourceTexture || source.sourceMaterial != sourceMaterial ||
                    source.bakedTexture != bakedTexture || source.bakedMaterial != bakedMaterial) return false;

                var materials = part.sharedMaterials;
                bool liveTarget = materialIndex >= 0 && materialIndex < materials.Length && materials[materialIndex] == material &&
                    material.shader == shader && MeshOf(part) == _builtTargetMesh && SavedEditSlotStillAssigned(material, slot);
                if (!liveTarget) _needsRebuild = true;
                return liveTarget;
            };
        }

        static bool SavedEditSlotStillAssigned(Material material, TextureSlot slot) => slot.texture != null && slot.properties.Count > 0 &&
            slot.properties.All(p => material.HasProperty(p) && material.GetTexture(p) == slot.texture) &&
            TargetResolver.ReadUvChannel(material, slot.properties[0]) == slot.uvChannel;

        bool CheckSavedEditContext(Func<bool> stillMatches)
        {
            if (stillMatches()) return true;
            SavedEditContextChanged();
            return false;
        }

        void SavedEditContextChanged()
        {
            if (this != null) Toast(Loc.T("The target or layers changed while opening this edit. Try opening it again."), MessageType.Info);
        }

        bool ConfirmReplaceSavedEdit(TextureRecipe recipe, Func<bool> stillMatches) =>
            CheckSavedEditContext(stillMatches) && ConfirmReplaceLayers(recipe) && CheckSavedEditContext(stillMatches);

        void WithTargetUndo(string name, Action change)
        {
            EndStroke();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try { change(); }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.SetCurrentGroupName(name);
                Undo.CollapseUndoOperations(group);
                Undo.IncrementCurrentGroup();
            }
        }

        void LoadRecipeOnCurrentTarget(TextureRecipe recipe, bool asCopy) =>
            WithTargetUndo("Load saved edit", () => LoadRecipe(recipe, asCopy));

        static bool NeedsVerifiedDraftLayout(Layer layer) => layer is MixedMergeLayer || StylePack.NeedsLayout(layer);

        bool MatchesDraftLayout(TextureRecipe draft, TextureSlot slot)
        {
            var saved = draft.draftTarget;
            if (string.IsNullOrEmpty(saved?.uvLayout) || saved.uvLayout != _uv?.FaceLayout) return false;
            var current = StylePackTargets.Describe(TargetResolver.AvatarRoot(_renderer), _renderer, _materialIndex,
                slot, _uv, null);
            if (current == null || saved.uvLayout != current.uvLayout || saved.rendererKind != current.rendererKind ||
                saved.materialIndex != current.materialIndex || saved.textureRole != current.textureRole ||
                saved.uvChannel != current.uvChannel || saved.rendererPath == null ||
                !saved.rendererPath.SequenceEqual(current.rendererPath) || saved.properties == null ||
                !saved.properties.SequenceEqual(current.properties) || saved.geometry == null ||
                saved.geometry.Length != current.geometry.Length) return false;
            for (int i = 0; i < saved.geometry.Length; i++)
            {
                var a = saved.geometry[i];
                var b = current.geometry[i];
                if (a?.rendererPath == null || a.rendererKind != b.rendererKind || a.materialIndex != b.materialIndex ||
                    !a.rendererPath.SequenceEqual(b.rendererPath)) return false;
            }
            return true;
        }

        void OpenDraftWithReplacement(TextureRecipe draft, TextureSlot slot, Func<bool> stillMatches)
        {
            int? PausedLayers() => MatchesDraftLayout(draft, slot) ? null :
                draft.layers.Count(layer => layer?.enabled == true && NeedsVerifiedDraftLayout(layer));
            int? paused = PausedLayers();
            string message = Loc.F("This draft’s source texture is missing. Open its layers on ‘{0}’?", slot.texture.name);
            if (paused > 0) message += "\n\n" + Loc.F("Layers with unverified UV layouts will stay paused ({0}).", paused);
            bool RecoveryStillMatches() => stillMatches() && PausedLayers() == paused;
            if (!StudioDialog.Ask(Loc.T("Open saved edit"), message, Msg.BtnOpenHere, Msg.BtnCancel) ||
                !ConfirmReplaceSavedEdit(draft, RecoveryStillMatches)) return;
            WithTargetUndo("Load saved edit", () => LoadRecipeLayers(draft, asCopy: true, pausedDraftLayers: paused));
        }

        void OpenSavedEditOnSource(TextureRecipe recipe, TextureSlot current, int sourceIndex, Func<bool> stillMatches)
        {
            var source = _slots[sourceIndex];
            var title = Loc.T("Open saved edit");
            var body = Loc.F("This edit belongs to ‘{0}’, which is on this material.", source.texture.name);
            var openSource = Loc.T("Open on saved texture");
            int choice = current.role == TextureRole.Normal
                ? (StudioDialog.Ask(title, body, openSource, Msg.BtnCancel) ? 0 : 1)
                : StudioDialog.AskComplex(title, body, openSource, Msg.BtnCancel, Msg.BtnOpenHere);
            if (choice == 1) return;
            var destination = choice == 2 ? current : source;
            bool DestinationStillMatches()
            {
                if (!stillMatches()) return false;
                if (SavedEditSlotStillAssigned(_material, destination)) return true;
                _needsRebuild = true;
                return false;
            }
            if (!ConfirmReplaceSavedEdit(recipe, DestinationStillMatches)) return;
            if (choice == 2)
            {
                LoadRecipeOnCurrentTarget(recipe, asCopy: true);
                return;
            }

            WithTargetUndo("Load saved edit", () =>
            {
                Undo.RecordObject(this, "Load saved edit");
                _selection.Clear();
                ClearFaceSelection();
                ChooseSlot(sourceIndex);
                _pendingTexture = source.texture;
                LoadRecipe(recipe);
                Rebuild();
            });
        }
    }
}
