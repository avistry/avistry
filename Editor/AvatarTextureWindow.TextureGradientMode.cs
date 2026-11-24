using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _textureGradientUseDefaults;

        DrawnGradientLayer SelectedTextureGradient => SelectedGradient is { sceneProjection: false, enabled: true } layer ? layer : null;
        DrawnGradientLayer TextureGradientForHandles => _textureGradientUseDefaults ? null : SelectedTextureGradient;

        void DrawTextureGradientMode()
        {
            int mode = _textureGradientUseDefaults ? 0 : 1;
            int next;
            using (new EditorGUI.DisabledScope(_needsRebuild || _gradDrag != null))
                next = WrapToolbar(mode, new[]
                {
                    new GUIContent(Loc.T("New gradient"), Loc.T("Start a new gradient, even over an existing handle.")),
                    new GUIContent(Loc.T("Selected layer"), Loc.T("Use the Gradient tool to drag the endpoints or middle handle on the texture.")),
                }, EditorStyles.miniButton, 20f, PanelContentWidth, navigate: true);
            StudioGUI.Count("paint.textureGradientMode", GUILayoutUtility.GetLastRect());
            if (next == mode) return;

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, "Choose gradient settings");
            _textureGradientUseDefaults = next == 0;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Repaint();
        }
    }
}
