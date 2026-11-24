using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        Rect _settingsButtonRect;

        void ReloadAvistry()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || _saving)
            {
                Toast(Loc.T("Wait for Unity to finish, then reload Avistry."), MessageType.Info);
                return;
            }
            if (_liveStroke != null || _gradDrag != null || _imgDrag != null || _sceneGradientDraft != null ||
                _marqueeActive || _mixedMergePreview != null)
            {
                Toast(Loc.T("Finish the current edit or preview before reloading Avistry."), MessageType.Info);
                return;
            }
            if (_isolateMaterialSlots)
            {
                Toast(Loc.T("Restore the Scene view before reloading Avistry."), MessageType.Info);
                return;
            }

            var texture = CurrentSlot?.texture;
            float zoom = _zoom;
            bool picking = _pickForPaint;
            Glossary.Reload();
            _envStamp++;
            DestroyCanvasResources();
            DestroySceneResources();
            Rebuild();
            if (texture != null && CurrentSlot?.texture == texture)
            {
                _zoom = zoom;
                _pickForPaint = picking;
            }
            if (_comp != null) ComposeNow();
            if (string.IsNullOrEmpty(_status)) Toast(Loc.T("Avistry reloaded."), MessageType.Info);
            Repaint();
            SceneView.RepaintAll();
        }

        void DrawSettingsButton(GUIContent content, float width, GUIStyle style)
        {
            if (GUILayout.Button(content, style, GUILayout.Width(width))) BuildSettingsMenu().ShowAsContext();
            if (Event.current.type == EventType.Repaint) _settingsButtonRect = GUILayoutUtility.GetLastRect();
            if (!_showShortcutsNext || Event.current.type != EventType.Layout) return;
            _showShortcutsNext = false;
            PopupWindow.Show(_settingsButtonRect, new ShortcutsPopup());
        }
    }
}
