using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _openLayerCombine;
        string _layerSettingsToReveal;
        bool _revealLayerCombine;
        MixedMergePreview _revealedMixedMerge;

        void DrawLayerCommands(ref Action pending)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool add = GUILayout.Button(StudioGUI.Icon("Toolbar Plus", Loc.T(" Add layer"), Loc.T("Add a layer above the selected one")),
                    EditorStyles.toolbarDropDown, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                StudioGUI.Count("layers.toolbar", GUILayoutUtility.GetLastRect());
                if (add) ShowAddLayerMenu();
                DrawTemplatesButton();
            }

            int index = ActiveIndex;
            if (index < 0) return;
            var settings = new GUIContent(Loc.T("Layer settings"), Loc.T("Open settings for the selected layer."));
            var combine = new GUIContent(Loc.T("Combine layers"), Loc.T("Group, merge paint, or preview a rendered merge."));
            bool hasCombine = index > 0 || _mixedMergePreview != null;
            float labelledWidth = EditorStyles.miniButton.CalcSize(settings).x;
            if (hasCombine) labelledWidth += EditorStyles.boldLabel.CalcSize(combine).x + 32f;
            bool wrap = labelledWidth + 110f > PanelContentWidth;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool showSettings = GUILayout.Button(settings, EditorStyles.miniButton, GUILayout.MinWidth(0));
                StudioGUI.Count("layers.settingsJump", GUILayoutUtility.GetLastRect());
                if (showSettings)
                {
                    _openLayerSettings = true;
                    _layerSettingsToReveal = _activeLayerId;
                    Repaint();
                }
                if (hasCombine)
                {
                    bool wasOpen = _openLayerCombine;
                    using (new EditorGUI.DisabledScope(_mixedMergePreview != null))
                    {
                        var disclosure = EditorGUILayout.GetControlRect(false, 22f, GUILayout.MinWidth(0f));
                        _openLayerCombine = StudioGUI.Foldout(disclosure, _openLayerCombine || _mixedMergePreview != null, combine);
                    }
                    if (!wasOpen && _openLayerCombine) _revealLayerCombine = true;
                    StudioGUI.Count("layers.combine", GUILayoutUtility.GetLastRect());
                }
                if (!wrap) DrawLayerOrderCommands(index, ref pending);
            }
            if (wrap)
                using (new EditorGUILayout.HorizontalScope()) DrawLayerOrderCommands(index, ref pending);
        }

        void DrawLayerOrderCommands(int index, ref Action pending)
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!CanMoveLayer(index, 1)))
                if (GUILayout.Button(new GUIContent("▲", Loc.T("Move up (in front)")), EditorStyles.toolbarButton, GUILayout.Width(22)))
                    pending = () => MoveLayer(index, 1);
            using (new EditorGUI.DisabledScope(!CanMoveLayer(index, -1)))
                if (GUILayout.Button(new GUIContent("▼", Loc.T("Move down (behind)")), EditorStyles.toolbarButton, GUILayout.Width(22)))
                    pending = () => MoveLayer(index, -1);
            GUILayout.Space(3);
            using (new EditorGUI.DisabledScope(ActiveLayer is MixedMergeLayer))
                if (GUILayout.Button(StudioGUI.Icon("TreeEditor.Duplicate", "", Loc.T("Duplicate layer")), EditorStyles.toolbarButton, GUILayout.Width(24)))
                    pending = () => DuplicateLayer(index);
            GUILayout.Space(5);
            if (StudioGUI.DangerButton(StudioGUI.Icon("TreeEditor.Trash", "", Loc.T("Delete layer (Ctrl+Z restores it)")), EditorStyles.toolbarButton, GUILayout.Width(24)))
                pending = () => RemoveLayerConfirmed(index);
        }

        void DrawCombineLayerActions(ref Action pending)
        {
            int index = ActiveIndex;
            var draft = _mixedMergePreview;
            if (draft == null) _revealedMixedMerge = null;
            if (draft == null && (!_openLayerCombine || index < 1)) return;
            if (draft?.layer != null && draft != _revealedMixedMerge)
            {
                _revealedMixedMerge = draft;
                _revealLayerCombine = true;
            }

            var rect = EditorGUILayout.BeginVertical();
            if (draft == null)
            {
                if (ActiveLayer is PaintLayer && _recipe.layers[index - 1] is PaintLayer)
                {
                    var merge = new GUIContent(Loc.T("Merge down…"), Loc.T("Combine the selected Paint layer with the Paint layer directly below."));
                    bool mergePressed = GUILayout.Button(merge, EditorStyles.miniButton);
                    StudioGUI.Count("layers.mergeDown", GUILayoutUtility.GetLastRect());
                    if (mergePressed)
                    {
                        string id = _activeLayerId;
                        pending = () => MergePaintDown(id);
                    }
                }
                DrawGroupBelowAction(ref pending);
            }
            DrawMixedMergeAction(ref pending);
            EditorGUILayout.EndVertical();

            if (!_revealLayerCombine || Event.current.type != EventType.Repaint) return;
            _revealLayerCombine = false;
            float needed = rect.yMax + 8f - _learnViewportHeight;
            if (needed <= _panelScroll.y) return;
            _panelScroll.y = needed;
            Repaint();
        }
    }
}
