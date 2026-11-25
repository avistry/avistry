using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _pickForPaint;

        bool PickingForPaint => _pickForPaint && _tab == Tab.Paint && _tool == Tool.Paint && _uv != null && !MaskEditing;
        bool CanPaintCurrentSelection => !_needsRebuild && CanPaint(out _) && !MaskSelectedForEditing &&
            PendingLayerLimit == null && HasSelectionForLayerLimit(HasFaceSelection);
        bool CanGradientCurrentSelection => CanPaintCurrentSelection && string.IsNullOrEmpty(_layerLimitTargetId) &&
            _matchPick == MatchPick.None && !_scenePickArmed;

        Layer PendingLayerLimit => _tab == Tab.Select && !string.IsNullOrEmpty(_layerLimitTargetId) && _recipe != null
            ? _recipe.layers.Find(layer => layer != null && layer.id == _layerLimitTargetId) : null;

        bool HasSelectionForLayerLimit(bool faces) => faces ? HasFaceSelection && _selectedFaces.TrueForAll(IsCurrentFace) :
            _selection.Exists(IsCurrentPiece);

        bool CanApplyLayerLimit(Layer layer) => layer != null && layer is not MixedMergeLayer && !_needsRebuild &&
            CanPaint(out _) && _recipe != null && _recipe.layers.Contains(layer) &&
            HasSelectionForLayerLimit(PendingLayerLimit == layer ? _layerLimitUsesFaces : HasFaceSelection);

        static bool LayerLimitExcludesSelection(Layer layer) => layer != null && layer.maskMode != MaskMode.WholeTexture && layer.invertMask;

        static string ExcludeSelectionLabel(bool faces) => faces ? Loc.T("Exclude selected faces") : Loc.T("Exclude selected pieces");

        string LayerLimitLabel => LayerLimitExcludesSelection(PendingLayerLimit) ? ExcludeSelectionLabel(_layerLimitUsesFaces) :
            _layerLimitUsesFaces ? Loc.T("Apply face limit") : Loc.T("Apply piece limit");

        string LayerLimitPrompt(Layer layer) => _layerLimitUsesFaces ? Loc.F("Pick the faces for layer ‘{0}’.", layer.name) :
            Loc.F("Pick the pieces for layer ‘{0}’.", layer.name);

        bool TryPlanLayerLimit(Layer layer, MaskMode mode, bool invert, bool useSelection, out Action<Layer> apply,
            SceneView scene = null)
        {
            apply = null;
            if (_needsRebuild || layer == null || _recipe == null || !_recipe.layers.Contains(layer)) return false;
            bool faces = mode == MaskMode.SelectedFaces;
            if (useSelection && !HasSelectionForLayerLimit(faces)) return false;

            var pieceIds = useSelection && !faces ? _selection.Where(IsCurrentPiece).ToList() : layer.islands;
            var faceIds = useSelection && faces ? new List<int>(_selectedFaces) : layer.faces;
            var faceLayout = useSelection && faces ? _selectedFaceLayout : layer.faceLayout;
            void UpdateLimit(Layer target)
            {
                target.maskMode = mode;
                target.invertMask = invert;
                target.islands = pieceIds;
                target.faces = faceIds;
                target.faceLayout = faceLayout;
            }

            if (layer is DrawnGradientLayer placed && placed.sceneProjection && _uv != null && _comp != null)
            {
                var candidate = placed.CopyPlacement();
                UpdateLimit(candidate);
                if (_uv.PoseChanged()) _uv.RefreshPose();
                var reason = SceneGradientOverlapProblem(candidate, _uv.AvatarRoot());
                if (reason != null)
                {
                    Toast(reason, MessageType.Info, 5f);
                    if (scene != null)
                        scene.ShowNotification(new GUIContent(Loc.T("Shared pixels prevent this limit.")), 4d);
                    return false;
                }
            }
            apply = UpdateLimit;
            return true;
        }

        void CommitLayerLimit(Layer layer, Action<Layer> apply, string undoName)
        {
            Undo.RecordObject(_recipe, undoName);
            apply(layer);
            MarkEdited();
        }

        void ApplyPendingLayerLimit(Layer layer) => ApplyPendingLayerLimitFromView(layer, null);

        void ApplyPendingLayerLimitFromView(Layer layer, SceneView scene)
        {
            if (!CanApplyLayerLimit(layer) || PendingLayerLimit != layer) return;
            bool faces = _layerLimitUsesFaces;
            var mode = faces ? MaskMode.SelectedFaces : MaskMode.SelectedIslands;
            var targetMatches = layer is DrawnGradientLayer { sceneProjection: true } && CurrentSlot is TextureSlot slot
                ? CaptureSavedEditContext(_recipe, slot) : null;
            if (targetMatches?.Invoke() == false) return;
            if (!TryPlanLayerLimit(layer, mode, LayerLimitExcludesSelection(layer), true, out var apply, scene)) return;
            var selection = targetMatches == null ? null : (faces ? _selectedFaces : _selection).ToArray();
            ChangeSceneBar(LayerLimitLabel, () =>
            {
                if (targetMatches != null && (!targetMatches() || PendingLayerLimit != layer || _layerLimitUsesFaces != faces ||
                    !HasSelectionForLayerLimit(faces) || !selection.SequenceEqual(faces ? _selectedFaces : _selection))) return;
                CommitLayerLimit(layer, apply, "Set layer mask");
                _layerLimitTargetId = null;
                _layerLimitUsesFaces = false;
                OpenPaintLayers();
            });
        }

        void SetPickForPaint(bool pick)
        {
            if (_pickForPaint == pick) return;
            if (pick && (_needsRebuild || _tab != Tab.Paint || _uv == null || MaskSelectedForEditing || !CanPaint(out _))) return;
            EndStroke();
            CancelGradientDrag();
            CancelSceneGradient();
            CancelImageDrag();
            CancelMarquee();
            _eyedropperArmed = false;
            if (pick)
            {
                if (_scenePickArmed || _matchPick != MatchPick.None) _toastUntil = 0d;
                _scenePickArmed = false;
                if (_matchPick != MatchPick.None) CancelMatchPick();
            }
            _pickForPaint = pick;
            _hoverIsland = -1;
            _hoverFaces = null;
            SelectUiChanged();
        }

        void PaintCurrentSelection()
        {
            if (!CanPaintCurrentSelection) return;
            var pieces = HasFaceSelection ? null : _selection.Where(IsCurrentPiece).Distinct().ToList();
            SetPickForPaint(false);
            SetTab(Tab.Paint);
            if (_tab != Tab.Paint) return;
            SetPaintTool(0);
            if (ActiveLayer is PaintLayer layer && layer.enabled && layer.opacity > 0f && !layer.invertMask &&
                !EmptyLockedPaint(layer) && !(layer.hasPaintMask && layer.paintMaskStartsHidden && layer.maskStrokes.Count == 0) &&
                BlockedClipBase(layer, ActiveIndex) == null &&
                (pieces == null ? MatchesFaceSelection(layer) : layer.maskMode == MaskMode.SelectedIslands &&
                    layer.islands != null && new HashSet<int>(layer.islands).SetEquals(pieces))) return;

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            InsertLayer(new PaintLayer(), "Paint selection");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        static GUIContent PaintSelectionContent() => new GUIContent(Loc.T("Paint selected"),
            Loc.T("Paint on a layer limited to the selected pieces or UV faces."));

        GUIContent PickForPaintContent(bool picking) => picking
            ? new GUIContent(Loc.T("Back to paint"), Glossary.Control("scene.backToPaint")?.tip + "\n" + PaintLabels.paint[PaintToolIndex].text)
            : new GUIContent(Loc.T("Pick for paint"), Loc.T("Choose pieces or faces without leaving Paint."));

        void PaintCurrentSceneSelection()
        {
            if (!CanPaintCurrentSelection) return;
            _paintInScene = true;
            PaintCurrentSelection();
        }

        static GUIContent GradientSelectionContent() => new GUIContent(Loc.T("Gradient on selected"),
            Loc.T("Draw a gradient on the model within the selected faces or pieces."));

        void GradientCurrentSceneSelection()
        {
            if (!CanGradientCurrentSelection) return;
            ChangeSceneBar(Loc.T("Gradient on selected"), () =>
            {
                SetTab(Tab.Paint);
                SetPaintTool(3);
                _paintInScene = true;
                _sceneGradientUseDefaults = true;
            });
        }

        void DrawPaintSelectionBar(bool picking)
        {
            if (_uv == null || MaskEditing) return;
            var pickLabel = PickForPaintContent(picking);
            var paintLabel = PaintSelectionContent();
            var (pickRect, paintRect) = PanelSelectionActionRow(pickLabel, paintLabel);
            bool toggle = StudioGUI.SelectionCell(pickRect, picking ? SelectGlyph.BackToPaint : SelectGlyph.Click, pickLabel, picking, true);
            StudioGUI.Count("paint.pickForPaint", pickRect);
            bool paint;
            using (new EditorGUI.DisabledScope(!CanPaintCurrentSelection))
                paint = StudioGUI.SelectionCell(paintRect, SelectGlyph.PaintSelected, paintLabel, false, true);
            StudioGUI.Count("paint.paintSelected", paintRect);

            if (picking)
            {
                DrawSceneBarSelect(PanelContentWidth, false);
                GUILayout.Label(SelectModeHint(), StudioGUI.Hint, GUILayout.MaxWidth(PanelContentWidth));
            }
            if (toggle)
            {
                Undo.RecordObject(this, picking ? "Back to paint" : "Pick for paint");
                SetPickForPaint(!picking);
            }
            if (paint) PaintCurrentSelection();
        }

        void ToggleScenePaintPicking(bool pick) => ChangeSceneBar(pick ? "Pick for paint" : "Back to paint", () =>
        {
            _paintInScene = true;
            SetPickForPaint(pick);
        });

        void DrawQuickScenePaintSelection(float width, ref System.Action change)
        {
            bool picking = PickingForPaint;
            var row = SceneBarRow(width, 26f);
            var cell = new Rect(row.x, row.y, 28f, row.height);
            if (SceneToolCell(cell, picking ? SelectGlyph.BackToPaint : SelectGlyph.Click, PickForPaintContent(picking), picking))
                change = () => ToggleScenePaintPicking(!picking);
            StudioGUI.Count(picking ? "scene.backToPaint" : "scene.pickForPaint", cell);
            cell.x += 31f;
            using (new EditorGUI.DisabledScope(!CanPaintCurrentSelection))
                if (SceneToolCell(cell, SelectGlyph.PaintSelected, PaintSelectionContent())) change = PaintCurrentSceneSelection;
            StudioGUI.Count("scene.paintSelected", cell);
            if (picking && CanGradientCurrentSelection)
            {
                cell.x += 31f;
                if (SceneToolCell(cell, SelectGlyph.Gradient, GradientSelectionContent())) change = GradientCurrentSceneSelection;
                StudioGUI.Count("scene.gradientSelected", cell);
            }
            int count = SelectedElementCount;
            float right = row.xMax;
            if (!picking)
            {
                var spot = new Rect(row.xMax - 26f, row.y, 26f, row.height);
                var wire = new Rect(spot.x - 29f, row.y, 26f, row.height);
                DrawSceneViewGuides(wire, spot);
                right = wire.x - 3f;
            }
            var summary = Rect.MinMaxRect(cell.xMax + 5f, row.y, right, row.yMax);
            var title = new GUIContent(SceneSelectionSummary(count, HasFaceSelection || count == 0 && FaceSelectionTool));
            if (EditorStyles.miniLabel.CalcSize(title).x > summary.width)
            {
                title.tooltip = title.text;
                title.text = count.ToString();
            }
            GUI.Label(summary, title, EditorStyles.miniLabel);
            StudioGUI.Count("scene.selectionCount", summary);
        }
    }
}
