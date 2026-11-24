using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [NonSerialized] readonly HashSet<string> _collapsedLayerGroups = new HashSet<string>();

        static bool SameLayerGroup(Layer a, Layer b) => !string.IsNullOrEmpty(a?.groupId) && a.groupId == b?.groupId;

        static void CopyLayerGroup(Layer from, Layer to)
        {
            to.groupId = from?.groupId;
            to.groupName = from?.groupName;
        }

        static (int first, int last) LayerGroupBounds(List<Layer> layers, int index)
        {
            int first = index, last = index;
            var layer = layers[index];
            while (first > 0 && SameLayerGroup(layer, layers[first - 1])) first--;
            while (last + 1 < layers.Count && SameLayerGroup(layer, layers[last + 1])) last++;
            return (first, last);
        }

        void DrawGroupedLayerRows(ref Action pending)
        {
            var layers = _recipe.layers;
            int visualIndex = 0;
            for (int i = layers.Count - 1; i >= 0;)
            {
                if (string.IsNullOrEmpty(layers[i]?.groupId))
                {
                    DrawLayerRow(i--, visualIndex++, ref pending);
                    continue;
                }
                var (first, last) = LayerGroupBounds(layers, i);
                if (DrawLayerGroupHeader(first, last, ref pending))
                    for (int child = last; child >= first; child--)
                        DrawLayerRow(child, visualIndex++, ref pending, 12f);
                i = first - 1;
            }
        }

        bool DrawLayerGroupHeader(int first, int last, ref Action pending)
        {
            var layer = _recipe.layers[last];
            string id = layer.groupId;
            bool open = !_collapsedLayerGroups.Contains(id);
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = EditorGUILayout.GetControlRect(false, 23f, GUILayout.MinWidth(0f));
                string title = Loc.F("{0} ({1})", layer.groupName, last - first + 1);
                bool expand = StudioGUI.Foldout(rect, open, new GUIContent(title,
                    Loc.T("Layer group. Open to select and edit its individual layers.")));
                if (!open && ActiveIndex >= first && ActiveIndex <= last && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 2f, rect.height - 4f), StudioGUI.InfoBlue);
                StudioGUI.Count("layers.groupHeader", rect);
                if (expand != open)
                {
                    if (expand) _collapsedLayerGroups.Remove(id);
                    else _collapsedLayerGroups.Add(id);
                    Repaint();
                }
                bool ungroup = GUILayout.Button(new GUIContent(Loc.T("Ungroup"), Loc.T("Show these layers separately in the list. Their appearance and order stay the same.")),
                    EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                StudioGUI.Count("layers.ungroup", GUILayoutUtility.GetLastRect());
                if (ungroup) pending = () => UngroupLayers(layer.id, id);
            }
            return open;
        }

        void DrawGroupBelowAction(ref Action pending)
        {
            int index = ActiveIndex;
            if (index <= 0) return;
            var layers = _recipe.layers;
            var (first, last) = LayerGroupBounds(layers, index);
            if (first <= 0 || layers[first - 1] == null) return;
            int count = last - LayerGroupBounds(layers, first - 1).first + 1;
            string label = count == 2 ? Loc.T("Group with layer below") : Loc.F("Group {0} neighbouring layers", count);
            if (GUILayout.Button(new GUIContent(label,
                    Loc.T("Keep these layers together in the list. Each layer keeps its settings, mask and blend.")), EditorStyles.miniButton))
            {
                string layerId = _activeLayerId;
                pending = () => GroupWithLayerBelow(layerId);
            }
            StudioGUI.Count("layers.groupDown", GUILayoutUtility.GetLastRect());
        }

        bool CanChangeLayerGroups() => !_needsRebuild && _comp != null && _recipe != null &&
            _liveStroke == null && _gradDrag == null && _imgDrag == null && _sceneGradientDraft == null;

        void GroupWithLayerBelow(string layerId)
        {
            if (!CanChangeLayerGroups()) return;
            var layers = _recipe.layers;
            int index = layers.FindIndex(layer => layer?.id == layerId);
            if (index <= 0) return;
            var (first, last) = LayerGroupBounds(layers, index);
            if (first <= 0 || layers[first - 1] == null) return;
            var (baseFirst, _) = LayerGroupBounds(layers, first - 1);
            var upper = layers[index];
            var lower = layers[first - 1];
            var existing = !string.IsNullOrEmpty(upper.groupId) ? upper : lower;
            string id = string.IsNullOrEmpty(existing.groupId) ? Guid.NewGuid().ToString("N") : existing.groupId;
            string name = existing.groupName ?? Loc.F("Group: {0}", upper.name);
            ChangeLayerGroups(Loc.T("Group layers"), () =>
            {
                _collapsedLayerGroups.Remove(id);
                for (int i = baseFirst; i <= last; i++)
                {
                    _collapsedLayerGroups.Remove(layers[i].groupId);
                    layers[i].groupId = id;
                    layers[i].groupName = name;
                }
            });
        }

        void UngroupLayers(string layerId, string groupId)
        {
            if (!CanChangeLayerGroups() || string.IsNullOrEmpty(groupId)) return;
            var layers = _recipe.layers;
            int index = layers.FindIndex(layer => layer?.id == layerId && layer.groupId == groupId);
            if (index < 0) return;
            var (first, last) = LayerGroupBounds(layers, index);
            ChangeLayerGroups(Loc.T("Ungroup layers"), () =>
            {
                for (int i = first; i <= last; i++) CopyLayerGroup(null, layers[i]);
                _collapsedLayerGroups.Remove(groupId);
            });
        }

        void ChangeLayerGroups(string action, Action change)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RecordObjects(new UnityEngine.Object[] { _recipe, this }, action);
            change();
            RefreshEditedRecipe(affectsImage: false);
            RecordHistory();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }
    }
}
