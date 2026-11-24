using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        sealed class SceneStrokeDraft
        {
            internal TextureRecipe recipe;
            internal Compositor compositor;
            internal TextureSlot slot;
            internal Shader shader;
            internal int revision;
            internal List<Layer> previewLayers;
            internal int insertionIndex;
            internal bool newBlendLayer;
        }

        SceneStrokeDraft _sceneStrokeDraft;
        Compositor _cancelledSceneStrokePreview;
        bool DisplayingOriginal => _showOriginal && _sceneStrokeDraft == null;

        void StartStrokeEdit(bool fromScene, bool fill)
        {
            if (fromScene && !fill)
                _sceneStrokeDraft = new SceneStrokeDraft
                {
                    recipe = _recipe, compositor = _comp, slot = CurrentSlot,
                    shader = _material != null ? _material.shader : null,
                    revision = _historyRevision
                };
            else
            {
                if (fill) Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                _strokeUndoGroup = Undo.GetCurrentGroup();
                ShowEditsIfBeforeOn();
            }
        }

        bool SceneStrokeTargetCurrent(SceneStrokeDraft draft) => !_needsRebuild && _recipe == draft.recipe &&
            _comp == draft.compositor && CurrentSlot == draft.slot && draft.slot?.texture != null &&
            _historyRevision == draft.revision;

        void StageScenePaintLayer(PaintLayer layer, bool captureSelection, bool blending)
        {
            var draft = _sceneStrokeDraft;
            if (captureSelection) CaptureInsertedLayerSelection(layer);
            draft.insertionIndex = LayerInsertionIndex(null);
            draft.newBlendLayer = blending;
            InheritLayerGroupAtInsertion(layer, draft.insertionIndex);
            draft.previewLayers = new List<Layer>(_recipe.layers);
            draft.previewLayers.Insert(draft.insertionIndex, layer);
        }

        void ComposeSceneStrokePreview(Layer layer)
        {
            var layers = _sceneStrokeDraft.previewLayers ?? _recipe.layers;
            bool enabled = layer.enabled;
            layer.enabled = true;
            try { _comp.ComposeLivePreview(layers, _uv, layer, _envStamp); }
            finally { layer.enabled = enabled; }
        }

        bool CanCommitSceneStroke(SceneStrokeDraft draft, Layer layer)
        {
            if (_sceneStrokeDraft != draft || !SceneStrokeTargetCurrent(draft) || layer == null ||
                _liveStroke == null || _liveStroke.points.Count == 0 ||
                (draft.previewLayers == null && !_recipe.layers.Contains(layer)))
                return false;
            if (_renderer == null || _material == null || _material.shader != draft.shader ||
                !SlotUsesMaterial(_renderer, _materialIndex, _material) ||
                MeshOf(_renderer) != _builtTargetMesh || !SavedEditSlotStillAssigned(_material, draft.slot))
            {
                _needsRebuild = true;
                _preview.Clear();
                Repaint();
                return false;
            }
            return true;
        }

        bool BeginSceneStrokeCommit(SceneStrokeDraft draft, Layer layer)
        {
            if (!CanCommitSceneStroke(draft, layer)) return false;

            Undo.FlushUndoRecordObjects();
            if (!CanCommitSceneStroke(draft, layer))
            {
                if (_sceneStrokeDraft == draft)
                {
                    string reason = Loc.T("Edit changed. Paint the stroke again.");
                    Toast(reason, MessageType.Warning);
                    if (_sceneStrokeView != null)
                        _sceneStrokeView.ShowNotification(new GUIContent(reason), 3.0);
                }
                return false;
            }
            Undo.IncrementCurrentGroup();
            _strokeUndoGroup = Undo.GetCurrentGroup();
            ShowEditsIfBeforeOn();
            if (draft.previewLayers != null)
            {
                InsertLayer(layer, draft.newBlendLayer ? "Add blending layer" : "Add paint layer",
                    captureSelection: false, insertionIndex: draft.insertionIndex);
                if (draft.newBlendLayer) ExplainNewBlendLayer();
            }
            if (!layer.enabled)
            {
                Undo.RecordObject(_recipe, "Show layer");
                layer.enabled = true;
            }
            _sceneStrokeDraft = null;
            return true;
        }

        void CheckSceneStrokeCapture(SceneView view)
        {
            if (_sceneStrokeControl == 0 || _sceneStrokeView != view) return;
            if (ScenePainting && _liveStroke != null && GUIUtility.hotControl == _sceneStrokeControl) return;
            ReleaseSceneStrokeControl(view);
            _sceneHitValid = false;
            EndStroke();
        }

        void FinishClosedSceneStroke()
        {
            if (_sceneStrokeControl == 0 || _sceneStrokeView != null) return;
            ClearSceneStrokeControl();
            _sceneHitValid = false;
            EndStroke();
            SceneView.RepaintAll();
        }

        void RestoreCancelledSceneStrokePreview()
        {
            var compositor = _cancelledSceneStrokePreview;
            if (compositor == null) return;
            _cancelledSceneStrokePreview = null;
            if (_comp != compositor || !_dirty) return;
            if (_needsRebuild)
                _preview.Clear();
            else
                ComposeNow();
            SceneView.RepaintAll();
        }
    }
}
