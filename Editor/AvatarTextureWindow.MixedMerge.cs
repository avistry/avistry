using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        sealed class MixedMergePreview
        {
            public TextureRecipe recipe;
            public Compositor compositor;
            public TextureSlot slot;
            public MeshUV mesh;
            public Func<bool> targetMatches;
            public int revision;
            public int count;
            public string selected;
            public MixedMergeLayer layer;
            public bool showPixels = true;
            public bool showDetails = true;
        }

        MixedMergePreview _mixedMergePreview;

        bool MixedMergeContextMatches(MixedMergePreview draft) => draft != null && CanChangeLayerGroups() &&
            _recipe == draft.recipe && _comp == draft.compositor && CurrentSlot == draft.slot && _uv == draft.mesh &&
            _historyRevision == draft.revision && _activeLayerId == draft.selected &&
            (_tab == Tab.Paint || _tab == Tab.Layers);

        string MixedMergeProblem(int index)
        {
            if (!CanChangeLayerGroups() || !CanPaint(out _))
                return Loc.T("Finish the current edit before previewing a merge.");
            var layers = _recipe.layers;
            if (index < 1 || index >= layers.Count)
                return Loc.T("Select a layer with at least one layer below it.");
            if ((long)_comp.SourceWidth * _comp.SourceHeight > 4096L * 4096L ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
                return Loc.T("This texture is too large or its format is unavailable for a rendered merge. Use a layer group to keep editing.");
            if (LayerGroupBounds(layers, index).last != index)
                return Loc.T("Select the top layer in this group to preview all of its layers together.");
            int next = layers.FindIndex(index + 1, layer => layer != null);
            if (next >= 0 && layers[next].clipToBelow)
                return Loc.T("The next layer clips to this range. Include the clipped layers in the merge or use a layer group.");
            for (int i = 0; i <= index; i++)
            {
                var layer = layers[i];
                if (layer == null) return Loc.T("Reopen the edit before merging layers.");
                if (layer is MixedMergeLayer) return Loc.T("Separate the earlier merged layer before merging again.");
                if (layer is DrawnGradientLayer gradient && gradient.sceneProjection ||
                    layer is RootTipGradientLayer rootTip && rootTip.GradientModeUsed != GradientMode.UvV)
                    return Loc.T("This range contains a gradient tied to the model's pose. Keep those layers in a group.");
                if (HasPoseStrokes(layer.maskStrokes) || layer is PaintLayer paint && HasPoseStrokes(paint.strokes))
                    return Loc.T("This range contains paint or masks tied to the model's pose. Keep those layers in a group.");
                if (layer is ImageLayer image && (!string.IsNullOrEmpty(image.flattenedFrom) ||
                    image.offFloats?.Count > 0 || image.offKeywords?.Count > 0))
                    return Loc.T("This range includes a shader decal with material settings. Keep those layers in a group.");
            }
            return null;
        }

        static bool HasPoseStrokes(List<PaintStroke> strokes) => strokes?.Exists(stroke => stroke?.surface == true ||
            stroke?.mirrorPaths?.Exists(path => path?.surfacePoints?.Count > 0) == true) == true;

        void DrawMixedMergeAction(ref Action pending)
        {
            var draft = _mixedMergePreview;
            if (draft != null && !MixedMergeContextMatches(draft))
            {
                CancelMixedMergePreview();
                draft = null;
            }
            if (draft == null)
            {
                if (ActiveIndex < 1) return;
                bool preview = GUILayout.Button(new GUIContent(Loc.T("Preview merge from base…"),
                    Loc.T("Render from the bottom layer through the selected layer. Layers above stay editable.")), EditorStyles.miniButton);
                StudioGUI.Count("layers.mixedMergePreview", GUILayoutUtility.GetLastRect());
                if (preview)
                {
                    string id = _activeLayerId;
                    pending = () => StartMixedMergePreview(id);
                }
                return;
            }

            bool ready = draft.layer != null;
            if (StudioGUI.BeginFoldGroup(ref draft.showDetails, Loc.T("Preview merged layer"), infoId: "layer"))
            {
                GUILayout.Label(Loc.F("{0} layers from ‘{1}’ through ‘{2}’.", draft.count, _recipe.layers[0].name,
                    _recipe.layers[draft.count - 1].name), StudioGUI.CheckLabel);
                if (ready)
                {
                    bool showPixels = EditorGUILayout.ToggleLeft(Loc.T("Show merged pixels"), draft.showPixels, GUILayout.MinWidth(0));
                    if (showPixels != draft.showPixels)
                    {
                        draft.showPixels = showPixels;
                        _dirty = true;
                        Repaint();
                    }
                    if (_comp.Width != _comp.SourceWidth || _comp.Height != _comp.SourceHeight)
                        GUILayout.Label(Loc.T("Reduced previews may sample the merged image differently. Full-resolution output keeps the exact result."), StudioGUI.Hint);
                }
            }
            StudioGUI.EndGroup();
            if (!ready) GUILayout.Label(Loc.T("Rendering at full resolution…"), StudioGUI.Hint);
            else
            {
                GUILayout.Label(Loc.T("Keeping the merge creates a project asset. Separate layers restores the original edits."), StudioGUI.Hint);
                bool keep = StudioGUI.PrimaryButton(new GUIContent(Loc.T("Keep merged layer")), StudioGUI.InfoBlue);
                StudioGUI.Count("layers.mixedMergeKeep", GUILayoutUtility.GetLastRect());
                if (keep) pending = () => KeepMixedMerge(draft);
            }
            bool cancel = GUILayout.Button(Loc.T("Keep original layers"), EditorStyles.miniButton);
            StudioGUI.Count("layers.mixedMergeCancel", GUILayoutUtility.GetLastRect());
            if (cancel) pending = CancelMixedMergePreview;
        }

        void StartMixedMergePreview(string layerId)
        {
            if (_mixedMergePreview != null || layerId != _activeLayerId) return;
            string problem = MixedMergeProblem(ActiveIndex);
            if (problem != null) { Toast(problem, MessageType.Info, 6f); return; }
            var draft = new MixedMergePreview
            {
                recipe = _recipe, compositor = _comp, slot = CurrentSlot, mesh = _uv,
                selected = layerId, count = ActiveIndex + 1, revision = _historyRevision,
                targetMatches = CaptureSavedEditContext(_recipe, CurrentSlot),
            };
            _mixedMergePreview = draft;
            Repaint();
            EditorApplication.delayCall += () => { if (this != null && _mixedMergePreview == draft) RenderMixedMergePreview(draft); };
        }

        void RenderMixedMergePreview(MixedMergePreview draft)
        {
            if (draft.layer != null) return;
            if (!CheckMixedMergeTarget(draft)) return;
            try
            {
                var originals = _recipe.layers.GetRange(0, draft.count);
                using var render = new Compositor(draft.slot, 0);
                render.Compose(originals, draft.mesh);
                draft.layer = new MixedMergeLayer
                {
                    name = Loc.F("Merged through {0}", originals[originals.Count - 1].name),
                    source = draft.slot.texture,
                    uvLayout = draft.mesh?.FaceLayout, originals = originals, preview = render.TakeMixedMergePreview(),
                };
                _dirty = true;
                Repaint();
            }
            catch (Exception error)
            {
                CancelMixedMergePreview();
                Debug.LogException(error);
                Toast(Loc.T("The merge preview could not be rendered. Your layers are unchanged."), MessageType.Warning, 6f);
            }
        }

        IList<Layer> MixedMergePreviewLayers()
        {
            var draft = _mixedMergePreview;
            if (draft?.layer == null || !draft.showPixels || !MixedMergeContextMatches(draft)) return null;
            var layers = _recipe.layers.GetRange(draft.count, _recipe.layers.Count - draft.count);
            layers.Insert(0, draft.layer);
            return layers;
        }

        void CancelMixedMergePreview()
        {
            var draft = _mixedMergePreview;
            if (draft == null) return;
            _mixedMergePreview = null;
            draft.layer?.ReleasePreview();
            if (draft.layer?.pixels != null && !EditorUtility.IsPersistent(draft.layer.pixels)) DestroyImmediate(draft.layer.pixels);
            _dirty = true;
            Repaint();
        }

        bool CheckMixedMergeTarget(MixedMergePreview draft)
        {
            if (MixedMergeContextMatches(draft) && draft.targetMatches()) return true;
            if (_mixedMergePreview == draft)
            {
                CancelMixedMergePreview();
                Toast(Loc.T("The texture or layers changed. Preview the merge again."), MessageType.Info, 5f);
            }
            return false;
        }

        void KeepMixedMerge(MixedMergePreview draft)
        {
            if (_mixedMergePreview != draft || draft?.layer?.preview == null || !CheckMixedMergeTarget(draft)) return;
            string problem = MixedMergeProblem(draft.count - 1);
            if (problem != null) { Toast(problem, MessageType.Info, 6f); return; }
            Texture2D texture = null;
            string path = null;
            bool kept = false;
            try
            {
                var folder = Exporter.WritableFolder(draft.slot.assetPath);
                var basename = Path.GetFileNameWithoutExtension(draft.slot.assetPath);
                if (string.IsNullOrEmpty(basename)) basename = "Avistry";
                path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{basename}_merged.asset");
                texture = Compositor.CaptureMixedMergePixels(draft.layer.preview);
                draft.layer.pixels = texture;
                draft.layer.ReleasePreview();
                texture.name = basename + " merged";
                texture.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(texture, path);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != texture) throw new IOException();
                AssetDatabase.SaveAssetIfDirty(texture);
                if (!CheckMixedMergeTarget(draft)) return;

                var layers = _recipe.layers.GetRange(draft.count, _recipe.layers.Count - draft.count);
                layers.Insert(0, draft.layer);
                ChangeMixedMerge(Loc.T("Keep merged layer"), () =>
                {
                    _recipe.layers = layers;
                    kept = true;
                    _activeLayerId = draft.layer.id;
                    _maskEditLayerId = null;
                    _openLayerSettings = true;
                    _mixedMergePreview = null;
                });
                Learn.Did(LearnEvents.LayersMerged);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Toast(Loc.T("The merged project asset could not be saved. Your layers are unchanged."), MessageType.Warning, 6f);
            }
            finally
            {
                if (!kept)
                {
                    if (texture != null && path != null && AssetDatabase.LoadAssetAtPath<Texture2D>(path) == texture) AssetDatabase.DeleteAsset(path);
                    CancelMixedMergePreview();
                }
            }
        }

        void DrawMixedMergeSettings(MixedMergeLayer layer, ref Action pending)
        {
            if (!StudioGUI.BeginFoldGroup(ref _openLayerSettings, Loc.T("Merged layer"), layer.name, infoId: "layer"))
            {
                StudioGUI.EndGroup();
                return;
            }
            bool hasOriginals = layer.originals?.Exists(original => original != null && !(original is MixedMergeLayer)) == true;
            if (!hasOriginals)
                GUILayout.Label(Loc.T("The editable source layers are missing from this edit."), StudioGUI.Hint);
            else if (!layer.Matches(CurrentSlot?.texture, _uv?.FaceLayout, _comp?.SourceWidth ?? 0, _comp?.SourceHeight ?? 0))
                GUILayout.Label(Loc.T("This target is showing the original layers. Separate the merge to edit them."), StudioGUI.Hint);
            else
                GUILayout.Label(Loc.F("Rendered pixels from {0} layers. Separate them to edit the original layers.", layer.originals.Count), StudioGUI.Hint);
            using (new EditorGUI.DisabledScope(!hasOriginals))
            {
                bool separate = GUILayout.Button(Loc.T("Separate layers"), EditorStyles.miniButton);
                StudioGUI.Count("layers.mixedMergeSeparate", GUILayoutUtility.GetLastRect());
                if (separate) pending = () => SeparateMixedMerge(layer);
            }
            StudioGUI.EndGroup();
        }

        void SeparateMixedMerge(MixedMergeLayer layer)
        {
            if (!CanChangeLayerGroups() || layer?.originals == null || !_recipe.layers.Contains(layer)) return;
            var originals = layer.originals.FindAll(original => original != null && !(original is MixedMergeLayer));
            if (originals.Count == 0) return;
            ChangeMixedMerge(Loc.T("Separate layers"), () =>
            {
                int index = _recipe.layers.IndexOf(layer);
                var used = new HashSet<string>();
                foreach (var item in _recipe.layers) if (item != null && item != layer) used.Add(item.id);
                foreach (var item in originals)
                    if (string.IsNullOrEmpty(item.id) || !used.Add(item.id))
                    {
                        item.id = Guid.NewGuid().ToString("N");
                        used.Add(item.id);
                    }
                _recipe.layers.RemoveAt(index);
                _recipe.layers.InsertRange(index, originals);
                _activeLayerId = originals[originals.Count - 1].id;
                _maskEditLayerId = null;
            });
        }

        void ChangeMixedMerge(string action, Action change)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RecordObjects(new UnityEngine.Object[] { _recipe, this }, action);
            change();
            MarkEdited();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }
    }
}
