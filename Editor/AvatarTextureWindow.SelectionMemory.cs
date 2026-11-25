using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        sealed class SelectionBookmark
        {
            public (int renderer, int mesh, int material, int slot, int texture, int channel, string property) target;
            public string layout;
            public bool faces;
            public int[] elements;
        }

        readonly List<SelectionBookmark> _recentSelections = new List<SelectionBookmark>();
        bool _recallSelection;

        (int, int, int, int, int, int, string) SelectionAddress(TextureSlot slot) =>
            (_builtRenderer.GetInstanceID(), _builtTargetMesh.GetInstanceID(), _material.GetInstanceID(), _builtMaterialIndex,
                slot.texture.GetInstanceID(), slot.uvChannel, slot.properties.Count > 0 ? slot.properties[0] : null);

        void RememberSelectionBeforeSwitch()
        {
            if (_recallSelection) return;
            _recallSelection = true;
            var slot = _builtSlotIndex >= 0 && _builtSlotIndex < _slots.Count ? _slots[_builtSlotIndex] : null;
            if (_uv == null || _builtRenderer == null || _builtTargetMesh == null || _material == null || slot?.texture == null) return;
            var key = SelectionAddress(slot);
            _recentSelections.RemoveAll(entry => entry.target.Equals(key));
            bool faces = HasFaceSelection;
            var elements = faces ? _selectedFaces : _selection;
            if (elements.Count == 0) return;
            _recentSelections.Add(new SelectionBookmark { target = key, layout = _uv.FaceLayout, faces = faces, elements = elements.ToArray() });
            int count = 0;
            for (int i = _recentSelections.Count - 1; i >= 0; i--)
            {
                count += _recentSelections[i].elements.Length;
                if (i < _recentSelections.Count - 12 || count > 1024 * 1024 && i != _recentSelections.Count - 1)
                {
                    _recentSelections.RemoveRange(0, i + 1);
                    break;
                }
            }
        }

        void RecallSelectionAfterSwitch(TextureSlot slot)
        {
            if (!_recallSelection) return;
            _recallSelection = false;
            _selection.Clear();
            ClearFaceSelection();
            if (_uv == null || _builtTargetMesh == null || slot?.texture == null) return;
            var key = SelectionAddress(slot);
            var entry = _recentSelections.Find(item => item.target.Equals(key));
            if (entry == null || entry.layout != _uv.FaceLayout) return;
            if (entry.faces)
            {
                _selectedFaces.AddRange(entry.elements);
                _selectedFaceLayout = entry.layout;
            }
            else _selection.AddRange(entry.elements);
        }
    }
}
