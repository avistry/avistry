using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The content of the small Avistry bar at the bottom left of the Scene view (AvistrySceneBar). It holds no state of its
    /// own: every value it shows is a window field, and every button calls the same method as the panel.
    /// </summary>
    partial class AvatarTextureWindow
    {
        const float SceneBarWidth = 250f;

        /// <summary>The window the bar drives: the last Avistry window that got a target or the focus.</summary>
        internal static AvatarTextureWindow SceneBarOwner;

        /// <summary>The same gate as the Scene view picking: a target, and no colour pick waiting.</summary>
        internal bool SceneBarVisible => _renderer != null && _uv != null && _matchPick == MatchPick.None && !_scenePickArmed;

        /// <summary>The window the bar shows now: the owner, or when the owner has no target, another window that has one.</summary>
        internal static AvatarTextureWindow SceneBarWindow()
        {
            if (SceneBarOwner != null && SceneBarOwner._uv != null) return SceneBarOwner;
            foreach (var w in Resources.FindObjectsOfTypeAll<AvatarTextureWindow>())
                if (!w._automationSandbox && w._uv != null && w.SceneBarVisible) return w;
            return SceneBarOwner;
        }

        void ClaimSceneBar()
        {
            if (!_automationSandbox) SceneBarOwner = this;
        }

        void ReleaseSceneBar()
        {
            if (SceneBarOwner != this) return;
            SceneBarOwner = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<AvatarTextureWindow>())
                if (w != this && !w._automationSandbox) { SceneBarOwner = w; break; }
        }

        void OnFocus() => ClaimSceneBar();

        internal void DrawSceneBar()
        {
            if (_uv == null) return;
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 64f;
            try
            {
            using (new GUILayout.VerticalScope(GUILayout.Width(SceneBarWidth)))
            {
                if (_tool == Tool.Paint) DrawSceneBarPaint();
                else DrawSceneBarSelect();

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(Loc.F("{0} piece{1} selected", _selection.Count, (_selection.Count == 1 ? "" : "s")), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    var spotContent = new GUIContent(Loc.T("Spotlight"),
                        Loc.T("While painting, dim everything outside the selected pieces and hide the orange selection."));
                    // grows for longer translations ("スポットライト") instead of cutting the word
                    bool spot = GUILayout.Toggle(_focusWhilePainting, spotContent, EditorStyles.miniButton,
                        GUILayout.Width(Mathf.Max(66f, EditorStyles.miniButton.CalcSize(spotContent).x + 4f)));
                    if (spot != _focusWhilePainting) { _focusWhilePainting = spot; SelectUiChanged(); }
                }
            }
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; }
        }

        void DrawSceneBarSelect()
        {
            DrawSelectModeStrip(true);
            DrawSelectOpSwitch();
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(Loc.T("Everything"), Loc.T("Select every piece")), EditorStyles.miniButtonLeft)) { SelectEverything(); SelectUiChanged(); }
                if (GUILayout.Button(new GUIContent(Loc.T("Nothing"), Loc.T("Clear the selection")), EditorStyles.miniButtonMid)) { SelectNothing(); SelectUiChanged(); }
                if (GUILayout.Button(new GUIContent(Loc.T("Invert"), Loc.T("Select what is not selected")), EditorStyles.miniButtonRight)) { InvertSelection(); SelectUiChanged(); }
            }
            DrawSelectModeOptions();
        }

        // While painting the bar swaps the selection tools for the brush's size and strength, the same fields as the panel.
        void DrawSceneBarPaint()
        {
            if (GUILayout.Button(new GUIContent(Loc.T("Select"), Loc.T("Back to selecting pieces (S)")), EditorStyles.miniButton)) SetSelectMode(_selectMode, true);
            int toolIndex = PaintToolIndex;
            bool simpleTool = toolIndex == 2 || toolIndex == 3; // fill and gradient have no brush size
            EditorGUI.BeginChangeCheck();
            if (!simpleTool)
                _brush.size = StudioGUI.LogSlider(new GUIContent(Loc.T("Size"), Loc.T("Brush diameter in texture pixels. [ and ] change it.")), _brush.size, BrushSettings.MinSize, BrushSettings.MaxSize, null);
            _brush.opacity = EditorGUILayout.Slider(new GUIContent(Loc.T("Strength"), Loc.T("How strong one stroke (or fill) is")), _brush.opacity, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) MarkBrushEdited();
        }
    }
}
