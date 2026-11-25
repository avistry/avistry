using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly string[] SceneToolIds = Array.ConvertAll((SelectGlyph[])Enum.GetValues(typeof(SelectGlyph)), glyph => "SceneTool." + glyph);
        static readonly string[] PanelToolIds = Array.ConvertAll((SelectGlyph[])Enum.GetValues(typeof(SelectGlyph)), glyph => "PanelTool." + glyph);
        [NonSerialized] string _sceneToolName, _sceneToolDescription, _sceneToolTip;

        void ShowSceneToolTip(GUIContent title)
        {
            if (_sceneToolName != title.text || _sceneToolDescription != title.tooltip)
            {
                _sceneToolName = title.text;
                _sceneToolDescription = title.tooltip;
                _sceneToolTip = string.IsNullOrEmpty(title.tooltip) ? title.text : title.text + "\n" + title.tooltip;
            }
            SceneSelectionHoverTip = _sceneToolTip;
        }

        bool SceneToolCell(Rect rect, SelectGlyph glyph, GUIContent title, bool selected = false)
        {
            if (rect.Contains(Event.current.mousePosition)) ShowSceneToolTip(title);
            return StudioGUI.SelectionCell(rect, glyph, title, selected);
        }

        int ToolPalette(int selected, SelectGlyph[] glyphs, GUIContent[] titles, float width, bool scene,
            bool navigate = true, int keyboard = 0)
        {
            const float gap = 3f, height = 26f;
            width = Mathf.Max(1f, width);
            Span<Rect> cells = stackalloc Rect[glyphs.Length];
            for (int i = 0; i < cells.Length; ++i)
                cells[i].width = Mathf.Min(width, scene ? height : StudioGUI.SelectionCellWidth(titles[i]));
            float y = 0f;
            for (int first = 0; first < cells.Length; y += height + gap)
            {
                int end = first + 1;
                float used = cells[first].width;
                while (end < cells.Length && used + gap + cells[end].width <= width)
                    used += gap + cells[end++].width;
                float extra = (width - used) / (end - first);
                float x = 0f;
                for (; first < end; first++)
                {
                    float w = cells[first].width + extra;
                    cells[first] = new Rect(x, y, w, height);
                    x += w + gap;
                }
            }
            var bounds = GUILayoutUtility.GetRect(0f, y - gap, GUILayout.ExpandWidth(true));
            bounds.width = Mathf.Min(bounds.width, width);
            if (keyboard == 0) keyboard = GUIUtility.GetControlID(FocusType.Keyboard);
            var e = Event.current;
            bool focused = GUIUtility.keyboardControl == keyboard;
            if (navigate && GUI.enabled && !EditorGUIUtility.editingTextField)
            {
                int step = ChoiceInput(bounds, focused, e);
                if (step != 0)
                {
                    selected = Mathf.Clamp(selected + step, 0, glyphs.Length - 1);
                    e.Use();
                }
            }
            if (GUI.enabled && focused && selected < 0 && e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                selected = 0;
                e.Use();
            }
            var ids = scene ? SceneToolIds : PanelToolIds;
            for (int i = 0; i < glyphs.Length; i++)
            {
                var cell = cells[i];
                float scale = bounds.width / width;
                cell.x = bounds.x + cell.x * scale;
                cell.y += bounds.y;
                cell.width *= scale;
                bool hover = cell.Contains(e.mousePosition);
                if (scene && hover) ShowSceneToolTip(titles[i]);
                bool press = GUI.enabled && hover && e.type == EventType.MouseDown && e.button == 0;
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) selected = i;
                if (press) GUIUtility.keyboardControl = keyboard;
                StudioGUI.DrawToolCell(cell, glyphs[i], titles[i], i == selected, !scene,
                    GUI.enabled && hover, GUI.enabled && focused && i == Mathf.Max(0, selected));
                if (GUI.enabled && e.type == EventType.Repaint) EditorGUIUtility.AddCursorRect(cell, MouseCursor.Link);
                StudioGUI.Track(ids[(int)glyphs[i]], cell);
            }
            return selected;
        }
    }
}
