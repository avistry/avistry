using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        void Bake() => RunBake(false);

        void RunBake(bool background)
        {
            if (_saving || _pixelJob != null || _aoSteps != null) return;
            EndStroke();
            var slot = CurrentSlot;
            var material = _material;
            if (slot?.texture == null || material == null) return;
            var preview = _comp;
            var source = slot.texture;
            var sourceHash = source.imageContentsHash;
            var renderer = _renderer;
            var materialIndex = _materialIndex;
            var shader = material.shader;
            var recipe = _recipe;
            int revision = _historyRevision;
            var uv = _uv;
            var mode = _outputMode;
            var dither = _dither;
            var users = new List<TargetResolver.SlotRef>(_users);
            var also = new List<TargetResolver.TextureUser>(_alsoUsers);
            var recipients = also.Select(user => user.material).Append(material).Distinct().Where(m => m != null)
                .Select(m => (material: m, crc: m.ComputeCRC())).ToArray();
            var enrolledMeshes = users.Select(user => user.renderer)
                .Concat(also.Select(user => user.slot.renderer)).Distinct().Where(part => part != null)
                .Select(part => (part, mesh: MeshOf(part))).ToArray();

            bool UsesSource(Material candidate, IEnumerable<string> properties) =>
                candidate != null && properties.All(p => candidate.HasProperty(p) && candidate.GetTexture(p) == slot.texture);

            bool SameLiveEdit() =>
                this != null && !_needsRebuild && _comp != null && _comp == preview && recipe != null && recipe == _recipe &&
                _historyRevision == revision && _liveStroke == null && _sceneStrokeDraft == null && _gradDrag == null &&
                _sceneGradientDraft == null && _imgDrag == null && renderer != null && renderer == _renderer &&
                materialIndex == _materialIndex && ReferenceEquals(slot, CurrentSlot) && ReferenceEquals(uv, _uv) &&
                mode == _outputMode && dither == _dither && source != null && slot.texture == source && source.imageContentsHash == sourceHash;

            bool SaveContextStillMatches()
            {
                if (!SameLiveEdit() || material == null || material.shader != shader ||
                    recipients.Any(entry => entry.material == null || entry.material.ComputeCRC() != entry.crc) ||
                    slot.properties.Count == 0 || !UsesSource(material, slot.properties) ||
                    AssetDatabase.GetAssetPath(slot.texture) != slot.assetPath ||
                    TargetResolver.ReadUvChannel(material, slot.properties[0]) != slot.uvChannel ||
                    !also.SequenceEqual(_alsoUsers) ||
                    also.Count > 0 && !MaterialMatches(material, slot.texture, slot.properties, slot.uvChannel, true) ||
                    enrolledMeshes.Any(entry => entry.part == null || MeshOf(entry.part) != entry.mesh))
                    return false;

                var materials = renderer.sharedMaterials;
                if (materialIndex < 0 || materialIndex >= materials.Length || materials[materialIndex] != material ||
                    !users.SequenceEqual(TargetResolver.FindMaterialUsers(TargetResolver.AvatarRoot(renderer), material)))
                    return false;

                return also.All(user =>
                {
                    if (user.slot.renderer == null || !UsesSource(user.material, user.properties) ||
                        !TargetResolver.TextureProperties(user.material, slot.texture).SequenceEqual(user.properties) ||
                        TargetResolver.CommonUvChannel(user.material, user.properties) != user.slot.uvChannel) return false;
                    var attached = user.slot.renderer.sharedMaterials;
                    int index = user.slot.materialIndex;
                    return index >= 0 && index < attached.Length && attached[index] == user.material;
                });
            }

            void ShowChangedSaveContext(string pngPath = null)
            {
                if (this == null || _comp == null) return;
                if (!background) { _needsRebuild = true; SetTab(Tab.Target); }
                if (pngPath == null)
                    Toast(Loc.T("The target or layers changed. Check your edit before saving again."), MessageType.Warning, 5f);
                else
                    Toast(Loc.T("The PNG and saved edit are ready. Avistry kept your current edit because the target or layers changed."),
                        MessageType.Warning, 6f, Msg.BtnShowFile, () => ShowFileAndLearn(pngPath));
            }

            if (recipe == null || slot.role == TextureRole.Normal) return;
            if (!SaveContextStillMatches()) { ShowChangedSaveContext(); return; }
            bool plain = _outputMode == OutputMode.DuplicateMaterial && _alsoUsers.Count == 0 && SharedMaterialNote() == null && (_comp?.SourceWidth ?? 0) == 2048;
            string body = plain ? Msg.M3BakeConfirm(System.IO.Path.GetFileNameWithoutExtension(Exporter.PredictPaths(slot, _material, _outputMode).pngPath)) : BakeSummary(slot);
            if (!StudioDialog.Ask(Loc.T("Save your edit"), body, Msg.BtnSave, Msg.BtnCancel)) return;
            if (!SaveContextStillMatches()) { ShowChangedSaveContext(); return; }
            if (!CaptureIncludedLayout(out var includedLayout))
            {
                ShowIncludedSaveProblem();
                return;
            }

            _preview.Clear();
            _saving = true;
            bool appliesToAvatar = mode != OutputMode.PngOnly;
            int undoGroup = -1;
            void BeginSaveUndo()
            {
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
            }

            void FinishSaveUndo()
            {
                if (undoGroup < 0) return;
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                Undo.IncrementCurrentGroup();
                undoGroup = -1;
            }
            TextureRecipe snapshot = null;
            bool handedOff = false, finished = false;
            void Finished()
            {
                if (finished) return;
                finished = true;
                Exporter.ReleaseSnapshot(snapshot);
                FinishSaveUndo();
                _saving = false;
                if (background) _pixelJob = null;
                else EditorUtility.ClearProgressBar();
                if (this != null) Repaint();
            }
            void Failed(Exception error)
            {
                _lastBakePng = null;
                if (!background) EditorUtility.ClearProgressBar();
                ShowErrorDialog(Msg.M2SaveFailed(), error);
            }
            try
            {
                string progressTitle = Loc.T("Avistry");
                string textureName = slot.texture.name;
                bool ReportProgress(string step, float fraction)
                {
                    if (background) return false;
                    string label = $"{step} ({textureName})";
                    if (fraction < Exporter.WritingProgress)
                        return EditorUtility.DisplayCancelableProgressBar(progressTitle, label, fraction);
                    EditorUtility.DisplayProgressBar(progressTitle, label, fraction);
                    return false;
                }
                var savedTarget = StylePackTargets.Describe(TargetResolver.AvatarRoot(renderer), renderer, materialIndex, slot, uv, null);
                if (background)
                {
                    snapshot = Exporter.SnapshotRecipe(recipe, savedTarget, includedLayout);
                    Exporter.BakePixels pixels = null;
                    _pixelJob = new EditorPixelJob(Loc.T("Save your edit"),
                        token => Exporter.CapturePixelsAsync(slot, snapshot, uv, dither, token, out pixels),
                        (rgba, token, progress) => pixels.EncodePixels(rgba, token, progress), png =>
                        Complete(Exporter.WriteBake(slot, material, users, snapshot, mode, also, png, pixels.Warning,
                            null, SaveContextStillMatches, BeginSaveUndo)),
                        Failed, () => Toast(Loc.T("Save cancelled. Nothing was written."), MessageType.Info, 4f), Finished, Repaint);
                    handedOff = true;
                }
                else
                    Complete(Exporter.Bake(slot, material, users, recipe, uv, mode, dither, also, ReportProgress,
                        SaveContextStillMatches, BeginSaveUndo, savedTarget, includedLayout));
            }
            catch (Exception e) when (!(e is ExitGUIException)) { Failed(e); }
            finally { if (!handedOff) Finished(); }

            void Complete(Exporter.Result res)
            {
                if (res.editChanged || (res.pngPath != null && !SameLiveEdit()))
                {
                    if (res.pngPath != null) AssetDatabase.SaveAssets();
                    ShowChangedSaveContext(res.pngPath);
                    return;
                }
                if (res.pngPath == null)
                {
                    Toast(Loc.T("Save cancelled. Nothing was written."), MessageType.Info, 4f);
                    return;
                }
                if (appliesToAvatar)
                {
                    Undo.SetCurrentGroupName("Save Avistry edit");
                    Undo.RecordObjects(new UnityEngine.Object[] { this, recipe }, "Save Avistry edit");
                }
                RememberLastBakeFiles(res);
                _lastBakeClearedLayers = appliesToAvatar;
                _unsaved = false;
                _savedBlockVisible = true;
                Learn.Did(LearnEvents.SaveDone);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(res.pngPath));
                if (_lastBakeClearedLayers)
                {
                    var bakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(res.pngPath);
                    if (bakedTexture != null)
                        foreach (var user in also)
                            if (TryGetIncludedUvChannel(user.slot.renderer, user.slot.materialIndex, slot.texture, out int channel))
                                BindIncludedUv(user.slot.renderer, user.slot.materialIndex, bakedTexture, channel);
                    recipe.layers.Clear();
                    _activeLayerId = null;
                    ClearHistory();
                }
                _tab = Tab.Save;
                FinishSaveUndo();
                AssetDatabase.SaveAssets();
                string saved = Msg.M1Saved(System.IO.Path.GetFileNameWithoutExtension(res.pngPath), !appliesToAvatar);
                if (res.paddingWarning != null) saved += "\n" + res.paddingWarning;
                string pngPath = res.pngPath;
                string pngGuid = _lastBakePngGuid;
                Toast(saved, MessageType.Info, 5f, Msg.BtnShowFile, () => RevealLastBakeFile<Texture2D>(pngPath, pngGuid));
                _needsRebuild = true;
            }
        }
    }
}
