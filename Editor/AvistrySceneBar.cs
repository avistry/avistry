using UnityEditor;
using UnityEditor.Overlays;

namespace Avistry.Textures
{
    /// <summary>
    /// The Avistry bar at the bottom left of the Scene view. It shows only while an Avistry window has a target. The
    /// content is drawn by the window (AvatarTextureWindow.DrawSceneBar), so this class only hosts it.
    /// The id "avistry-scene-bar" is saved in users' Scene view layouts: never rename it.
    /// </summary>
    [Overlay(typeof(SceneView), "avistry-scene-bar", "Avistry", defaultDisplay = true,
             defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom, defaultLayout = Layout.Panel)]
    internal sealed class AvistrySceneBar : IMGUIOverlay, ITransientOverlay
    {
        public bool visible
        {
            get
            {
                var owner = AvatarTextureWindow.SceneBarWindow();
                return owner != null && owner.SceneBarVisible;
            }
        }

        public override void OnGUI()
        {
            var owner = AvatarTextureWindow.SceneBarWindow();
            if (owner != null) owner.DrawSceneBar();
        }
    }
}
