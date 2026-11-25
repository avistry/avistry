using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        bool SceneIsIsolated => SceneVisibilityManager.instance.IsCurrentStageIsolated();
        bool SceneTargetOutsideCurrentStage => _renderer == null ? _uv != null :
            !StageUtility.GetCurrentStageHandle().Contains(_renderer.gameObject);

        void DiscardOffStageSceneInput(SceneView view)
        {
            if (_renderer == null) _needsRebuild = true;
            if (_sceneStrokeDraft != null)
            {
                _cancelledSceneStrokePreview = _comp;
                CancelStroke();
                RestoreCancelledSceneStrokePreview();
            }
            ReleaseSceneStrokeControl(view);
            RestoreAbandonedSceneGradient();
            ReleaseSceneGradientControl(view);
            _sceneGradientRecovery = null;
            if (_marqueeActive && _marqueeInScene) CancelMarquee();
            if (_matchPick != MatchPick.None) CancelMatchPick();
            ClearSceneTopologyFeedback();
            _sceneHitValid = false;
            _hoverIsland = -1;
            _hoverFaces = null;
        }

        void DrawSceneIsolation(float width, bool isolated, bool canIsolate, ref Action change)
        {
            var content = isolated
                ? new GUIContent(Loc.T("Restore Scene view"), Loc.T("Exit Unity’s isolation view and bring back the other visible objects."))
                : new GUIContent(Loc.T("Isolate edited parts"),
                    Loc.T("Hide clothing and other Scene objects while you paint the edited parts."));
            var rect = SceneBarRow(width);
            using (new EditorGUI.DisabledScope(!isolated && (!canIsolate || SceneTargetOutsideCurrentStage)))
                if (GUI.Button(rect, content, EditorStyles.miniButton)) change = () => SetSceneIsolation(!isolated);
            StudioGUI.Count("scene.paintIsolation", rect);
        }

        void SetSceneIsolation(bool isolate)
        {
            var visibility = SceneVisibilityManager.instance;
            if (isolate == visibility.IsCurrentStageIsolated()) return;
            if (!isolate && s_slotIsolationOwner != null && s_slotIsolationOwner != this)
            {
                s_slotIsolationOwner.SetSceneIsolation(false);
                return;
            }
            GameObject[] shown = null;
            SlotIsolation slots = null;
            if (isolate)
            {
                if (_needsRebuild || _uv == null) return;
                shown = VisibleEditedSceneObjects();
                if (shown.Length == 0)
                {
                    Toast(Loc.T("Show an edited part in the current Scene view before isolating it."), MessageType.Info);
                    return;
                }
                slots = PrepareSlotIsolation(shown);
            }

            EndStroke();
            CancelMarquee();
            CancelGradientDrag();
            CancelSceneGradient();
            _sceneHitValid = false;
            _hoverIsland = -1;
            _hoverFaces = null;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            if (isolate)
            {
                visibility.Isolate(shown, false);
                if (slots != null)
                {
                    if (s_slotIsolationOwner != null && s_slotIsolationOwner != this)
                        s_slotIsolationOwner.ForgetSlotIsolation(false);
                    Undo.RecordObject(this, Loc.T("Isolate edited parts"));
                    AttachSlotIsolation(slots);
                }
                else ForgetSlotIsolation(false);
            }
            else
            {
                if (_isolateMaterialSlots)
                {
                    Undo.RecordObject(this, Loc.T("Restore Scene view"));
                    _isolateMaterialSlots = false;
                    ClearSlotCameraState();
                }
                visibility.ExitIsolation();
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            SelectUiChanged();
        }

        IEnumerable<Renderer> EditedSceneRenderers()
        {
            if (_renderer != null) yield return _renderer;
            foreach (var slot in _users) if (slot.renderer != null) yield return slot.renderer;
            foreach (var user in _alsoUsers) if (user.slot.renderer != null) yield return user.slot.renderer;
        }

        GameObject[] VisibleEditedSceneObjects()
        {
            var stage = StageUtility.GetCurrentStageHandle();
            return EditedSceneRenderers().Where(r => r != null && IsDrawn(r) && stage.Contains(r.gameObject))
                .Select(r => r.gameObject).Distinct().ToArray();
        }

        string ScenePaintCoverHint(Renderer covered, out bool canIsolate)
        {
            bool sameObject = covered != null && EditedSceneRenderers().Any(renderer => renderer.gameObject == covered.gameObject);
            canIsolate = covered != null && !SceneIsIsolated && (!sameObject ||
                !_needsRebuild && _uv != null && PrepareSlotIsolation(VisibleEditedSceneObjects())?.parts.Any(p => p.renderer == covered) == true);
            if (sameObject)
                return canIsolate
                    ? Loc.T("A material on the same part covers this spot. Isolate edited parts to paint underneath.")
                    : Loc.T("A material on the same part covers this spot. Use the texture view to paint underneath.");
            return canIsolate
                ? Loc.F("“{0}” covers this spot. Isolate edited parts to paint underneath.", covered.name)
                : CoveredMessage(covered);
        }

        void ReportBlockedScenePaint(SceneView scene, Renderer covered)
        {
            var hint = ScenePaintCoverHint(covered, out bool canIsolate);
            if (canIsolate)
            {
                var target = _uv;
                Toast(hint, MessageType.Info, 5f, Loc.T("Isolate edited parts"), () =>
                {
                    if (_uv == target) SetSceneIsolation(true);
                });
            }
            else Toast(hint, MessageType.Info, 5f);
            scene.ShowNotification(new GUIContent(hint), 3.0);
        }
    }
}
