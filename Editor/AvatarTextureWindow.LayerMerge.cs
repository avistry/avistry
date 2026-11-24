using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        string PaintMergeProblem(int index)
        {
            if (_needsRebuild || _comp == null)
                return Loc.T("Wait for the texture to finish loading, then try merging again.");
            if (_liveStroke != null || _gradDrag != null || _imgDrag != null || _sceneGradientDraft != null)
                return Loc.T("Finish the current tool action before merging layers.");
            if (index <= 0 || index >= _recipe.layers.Count)
                return Loc.T("Choose a layer with another layer directly below it.");
            if (!(_recipe.layers[index] is PaintLayer upper) || !(_recipe.layers[index - 1] is PaintLayer lower))
                return Loc.T("Choose two adjacent Paint layers to merge. Adjustment and Image layers keep their own editing controls.");
            if ((!string.IsNullOrEmpty(upper.groupId) || !string.IsNullOrEmpty(lower.groupId)) && !SameLayerGroup(upper, lower))
                return Loc.T("Group these layers first to merge paint across groups.");
            if (!upper.enabled || !lower.enabled || upper.opacity != 1f || lower.opacity != 1f ||
                upper.blend != BlendMode.Normal || lower.blend != BlendMode.Normal)
                return Loc.T("Both Paint layers need to be visible, at 100% opacity, with Normal blend mode to merge.");
            if (upper.additionalBlends?.Count > 0 || lower.additionalBlends?.Count > 0)
                return Loc.T("Keep layers with additional blends separate to preserve their blend settings.");
            if (upper.maskMode != MaskMode.WholeTexture || lower.maskMode != MaskMode.WholeTexture ||
                upper.hasPaintMask || lower.hasPaintMask || upper.clipToBelow || lower.clipToBelow)
                return Loc.T("This merge needs two Paint layers set to Everything, with layer masks and clipping off.");
            if (upper.chR != lower.chR || upper.chG != lower.chG || upper.chB != lower.chB || upper.chA != lower.chA)
                return Loc.T("Both Paint layers need the same channel settings to merge.");
            int next = _recipe.layers.FindIndex(index + 1, layer => layer != null);
            if (next >= 0 && _recipe.layers[next].clipToBelow)
                return Loc.T("A layer above clips to this paint. Keep these layers separate to preserve its clipping.");
            if (upper.strokes == null || lower.strokes == null)
                return Loc.T("These layers have unreadable paint strokes. Reopen the edit before merging.");
            if (upper.strokes.Exists(stroke => stroke != null && (stroke.mode != BrushMode.Paint || stroke.lockAlpha || stroke.IsBlend)))
                return Loc.T("The upper layer has erasing, locked paint or Blend strokes. Keep the layers separate to preserve those strokes.");
            return null;
        }

        void MergePaintDown(string layerId)
        {
            var layers = _recipe?.layers;
            int index = layers?.FindIndex(layer => layer?.id == layerId) ?? -1;
            string problem = PaintMergeProblem(index);
            if (problem != null) { Toast(problem, MessageType.Info, 6f); return; }

            var preview = _comp;
            int revision = _historyRevision;
            var upper = (PaintLayer)layers[index];
            var lower = (PaintLayer)layers[index - 1];
            string title = Loc.T("Merge Paint layers");
            int choice = StudioDialog.AskComplex(title,
                Loc.F("Merge “{0}” into “{1}”? Both layers' paint stays editable. Choose which name and “Only recolour existing paint” setting to keep. " +
                    "Ctrl+Z restores the separate layers.", upper.name, lower.name),
                Loc.T("Keep lower settings"), Msg.BtnCancel, Loc.T("Keep upper settings"));
            if (choice != 0 && choice != 2) return;

            if (this == null) return;
            if (_historyRevision != revision || !ReferenceEquals(_recipe?.layers, layers) || _comp != preview || index >= layers.Count ||
                !ReferenceEquals(upper, layers[index]) || !ReferenceEquals(lower, layers[index - 1]))
            {
                Toast(Loc.T("The texture or layers changed. Choose Merge down again."), MessageType.Info, 5f);
                return;
            }
            problem = PaintMergeProblem(index);
            if (problem != null) { Toast(problem, MessageType.Info, 6f); return; }

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(title);
            Undo.RecordObjects(new UnityEngine.Object[] { _recipe, this }, title);
            lower.strokes.AddRange(upper.strokes);
            if (choice == 2)
            {
                lower.name = upper.name;
                lower.lockTransparent = upper.lockTransparent;
            }
            layers.RemoveAt(index);
            _activeLayerId = lower.id;
            MarkEdited();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Toast(Loc.F("Merged paint into “{0}”.", lower.name), MessageType.Info, 4f);
            Learn.Did(LearnEvents.LayersMerged);
        }
    }
}
