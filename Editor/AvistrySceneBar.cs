using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Avistry.Textures
{
    [Overlay(typeof(SceneView), "avistry-scene-bar", "Avistry", defaultDisplay = true,
             defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom, defaultLayout = Layout.Panel)]
    internal sealed class AvistrySceneBar : Overlay, ITransientOverlay
    {
        AvatarTextureWindow _scrollOwner;
        IMGUIContainer _panel;
        (int view, Vector2 offset) _details;
        float _detailsTop;

        internal bool PanelIsVisible
        {
            get
            {
                if (_panel?.panel == null) return false;
                for (VisualElement element = _panel; element != null; element = element.parent)
                    if (element.resolvedStyle.display == DisplayStyle.None) return false;
                return true;
            }
        }

        public bool visible
        {
            get
            {
                var owner = AvatarTextureWindow.SceneBarWindow();
                return owner != null && owner.SceneBarVisible;
            }
        }

        public override VisualElement CreatePanelContent()
        {
            var panel = _panel = new IMGUIContainer();
            string pointerTip = null;
            panel.RegisterCallback<PointerMoveEvent>(_ => panel.MarkDirtyRepaint(), TrickleDown.TrickleDown);
            panel.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                pointerTip = null;
                panel.tooltip = "";
                panel.MarkDirtyRepaint();
            });
            panel.onGUIHandler = () =>
            {
                if (!displayed) return;
                var owner = AvatarTextureWindow.SceneBarWindow();
                if (_scrollOwner != owner)
                {
                    _scrollOwner = owner;
                    _details = default;
                    _detailsTop = 0f;
                }
                if (owner != null && containerWindow is SceneView scene)
                {
                    var bounds = panel.worldBound;
                    float space = bounds.height > 1f && panel.panel != null
                        ? panel.panel.visualTree.worldBound.yMax - bounds.yMin : float.PositiveInfinity;
                    if (!floating && float.IsFinite(space) && (scene.position.height < 650f || bounds.width < 190f))
                        space = DockedPanelSpace(panel, space);
                    float previousTop = _detailsTop;
                    owner.DrawSceneBar(scene, panel.contentRect.width, scene.position.height, space, ref _details, ref _detailsTop);
                    if (previousTop != _detailsTop) panel.MarkDirtyRepaint();
                    var input = Event.current.rawType;
                    if (input == EventType.MouseMove || input == EventType.MouseDrag)
                        pointerTip = owner.SceneSelectionHoverTip;
                    else if (input == EventType.MouseDown || input == EventType.KeyDown || input == EventType.ScrollWheel)
                        pointerTip = null;
                    if (input == EventType.Repaint || input == EventType.MouseMove)
                    {
                        string tip = pointerTip ?? GUI.tooltip;
                        if (panel.tooltip != tip) panel.tooltip = tip;
                    }
                }
                if (Event.current.isMouse) Event.current.Use();
            };
            return panel;
        }

        static float DockedPanelSpace(VisualElement panel, float spaceBelowTop)
        {
            for (var element = panel; element.parent != null; element = element.parent)
            {
                if (!element.ClassListContains("unity-overlay")) continue;
                var siblings = element.parent;
                for (int i = 0; i < siblings.childCount && siblings[i] != element; i++)
                    if (siblings[i].ClassListContains("overlay-container__spacer"))
                        return Mathf.Min(spaceBelowTop, panel.worldBound.height) + siblings[i].worldBound.height;
            }
            return spaceBelowTop;
        }
    }
}
