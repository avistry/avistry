using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Keeps the edited mesh across Unity restarts and scene reloads: the renderer reference is lost then, so the window
    /// also remembers the mesh's GlobalObjectId and finds it again once the scene is open.
    /// </summary>
    partial class AvatarTextureWindow
    {
        void OnHierarchyChanged()
        {
            // Cheap: only does work while the mesh is missing (scene just reopened, avatar re-enabled…).
            if (_renderer == null && !string.IsNullOrEmpty(_rendererId)) TryRestoreRenderer();
        }

        void TryRestoreRenderer()
        {
            RepairRecipe("restore"); // runs on every window enable, so after a script reload too
            if (_renderer != null) { if (string.IsNullOrEmpty(_rendererId)) _rendererId = GlobalObjectId.GetGlobalObjectIdSlow(_renderer).ToString(); return; }
            if (string.IsNullOrEmpty(_rendererId) || !GlobalObjectId.TryParse(_rendererId, out var id)) return;
            var r = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Renderer;
            if (r == null) return;
            _renderer = r;          // keeps the material / texture choice and the selection, unlike SetRenderer
            _needsRebuild = true;
            Repaint();
        }
    }
}
