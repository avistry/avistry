using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal enum SaveState { Idle = 0, NotSaved = 1, Saving = 2, Saved = 3 }

        [SerializeField] bool _savedBlockVisible;   // true only after a successful Bake; cleared by MarkEdited, OnUndoRedo, target change
        [System.NonSerialized] bool _saving;

        // LastBakePngExists() already exists in AvatarTextureWindow.Panel.cs.

        internal SaveState CurrentSaveState => _saving ? SaveState.Saving
            : (_savedBlockVisible && LastBakePngExists()) ? SaveState.Saved
            : (_unsaved && _recipe != null && _recipe.layers.Count > 0) ? SaveState.NotSaved : SaveState.Idle;

        internal string SaveStateLabel()
        {
            switch (CurrentSaveState)
            {
                case SaveState.NotSaved: return $"NotSaved({_recipe.layers.Count})";
                case SaveState.Saving: return "Saving";
                case SaveState.Saved: return "Saved";
                default: return "Idle";
            }
        }
    }
}
