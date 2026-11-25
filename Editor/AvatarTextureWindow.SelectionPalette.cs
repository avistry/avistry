using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly SelectGlyph[] PieceGlyphs = { SelectGlyph.Click, SelectGlyph.Box, SelectGlyph.Lasso, SelectGlyph.Wand, SelectGlyph.Part, SelectGlyph.Tile };
        static readonly SelectGlyph[] MeshGlyphs = { SelectGlyph.Face, SelectGlyph.BoundaryLoop, SelectGlyph.FaceLoop, SelectGlyph.Vertex, SelectGlyph.Edge };
        static readonly SelectGlyph[] OperationGlyphs = { SelectGlyph.Replace, SelectGlyph.Add, SelectGlyph.Subtract };
        static readonly SelectGlyph[] MaskActionGlyphs = { SelectGlyph.Reveal, SelectGlyph.Hide };
        static readonly SelectGlyph[] SetGlyphs = { SelectGlyph.All, SelectGlyph.None, SelectGlyph.Invert };
        static readonly string[] SetControlIds = { "limit.everything", "limit.nothing", "limit.invert" };

        static readonly GUIContent[] s_selectionLabels = new GUIContent[Enum.GetValues(typeof(SelectGlyph)).Length];
        static readonly string[] s_selectionNames = Array.ConvertAll((SelectGlyph[])Enum.GetValues(typeof(SelectGlyph)),
            glyph => "Avistry.Select." + glyph);
        static string s_selectionLanguage;
        [NonSerialized] string _focusSelectionName;
        [NonSerialized] bool _focusSelectionInScene;
        [NonSerialized] GUIContent _hoveredSelection, _focusedSelection;

        static GUIContent SelectionLabel(SelectGlyph glyph)
        {
            if (s_selectionLanguage != Loc.Current)
            {
                Array.Clear(s_selectionLabels, 0, s_selectionLabels.Length);
                s_selectionLanguage = Loc.Current;
            }
            int i = (int)glyph;
            if (s_selectionLabels[i] != null) return s_selectionLabels[i];
            GUIContent content = glyph switch
            {
                SelectGlyph.Wire => new GUIContent(Loc.T("UV wire"), Loc.T("UV wireframe on the model")),
                SelectGlyph.Spotlight => new GUIContent(Loc.T("Spotlight"), Loc.T("While painting, dim everything outside the selected pieces and hide the orange selection.")),
                SelectGlyph.Replace => new GUIContent(Loc.T("Replace"), Loc.T("A new selection replaces the old one.")),
                SelectGlyph.Add => new GUIContent(Loc.T("Add"), Loc.T("Add to the selection (the same as holding Shift).")),
                SelectGlyph.Subtract => new GUIContent(Loc.T("Subtract"), Loc.T("Take away from the selection (the same as holding Ctrl).")),
                SelectGlyph.Reveal => new GUIContent(Loc.T("Show"), Loc.T("Show the colour area on the active layer. Shift-click also reveals.")),
                SelectGlyph.Hide => new GUIContent(Loc.T("Hide"), Loc.T("Hide the colour area on the active layer. Ctrl-click also hides.")),
                SelectGlyph.All => new GUIContent(Loc.T("Everything"), Loc.T("Select every piece")),
                SelectGlyph.None => new GUIContent(Loc.T("Nothing"), Loc.T("Clear the selection")),
                SelectGlyph.Invert => new GUIContent(Loc.T("Invert"), Loc.T("Select what is not selected")),
                _ => SelectModeContent((SelectMode)i)
            };
            if ((int)glyph <= (int)SelectGlyph.Subtract || glyph == SelectGlyph.Reveal || glyph == SelectGlyph.Hide)
                content.tooltip += "\n" + Loc.T("Click a choice, then use arrow keys or Shift+scroll to switch.");
            return s_selectionLabels[i] = content;
        }

        SelectGlyph? DrawSelectionRow(SelectGlyph[] choices, SelectGlyph? current, bool scene, float width,
            int count = -1, int offset = 0, bool commands = false)
        {
            const float gap = 3f, cellHeight = 26f;
            if (count < 0) count = choices.Length - offset;
            float minWidth = 26f;
            if (!scene)
                for (int k = offset; k < offset + count; k++)
                    minWidth = Mathf.Max(minWidth, StudioGUI.SelectionCellWidth(SelectionLabel(choices[k])));
            int columns = Mathf.Clamp(Mathf.FloorToInt((width + gap) / (minWidth + gap)), 1, count);
            int rows = (count + columns - 1) / columns;
            columns = (count + rows - 1) / rows;
            var bounds = EditorGUILayout.GetControlRect(false, (cellHeight + gap) * rows - gap, GUILayout.MinWidth(0f));
            bounds.width = Mathf.Min(bounds.width, width);
            float cellWidth = Mathf.Max(1f, (bounds.width - gap * (columns - 1)) / columns);
            if (scene && choices == MeshGlyphs) cellWidth = Mathf.Min(cellWidth, Mathf.Max(minWidth, (width - 5f * gap) / 6f));
            var ev = Event.current;
            string focus = GUI.GetNameOfFocusedControl();
            int focused = -1;
            SelectGlyph? picked = null;
            for (int k = 0; k < count; k++)
            {
                var glyph = choices[offset + k];
                string name = s_selectionNames[(int)glyph];
                var cell = new Rect(bounds.x + (k % columns) * (cellWidth + gap), bounds.y + (k / columns) * (cellHeight + gap), cellWidth, cellHeight);
                GUI.SetNextControlName(name);
                var label = SelectionLabel(glyph);
                if (name == focus) { focused = k; _focusedSelection = label; }
                if (StudioGUI.SelectionCell(cell, glyph, label, current == glyph, !scene))
                {
                    picked = glyph;
                    _focusSelectionName = name;
                    _focusSelectionInScene = scene;
                }
                if (_focusSelectionName == name && _focusSelectionInScene == scene && ev.type == EventType.Repaint)
                {
                    GUI.FocusControl(name);
                    _focusSelectionName = null;
                }
                if (cell.Contains(ev.mousePosition) && GUI.enabled) _hoveredSelection = label;
                StudioGUI.Track(name, cell);
                if (commands && !scene) StudioGUI.Count(SetControlIds[k], cell);
            }

            if (!GUI.enabled || EditorGUIUtility.editingTextField || commands && ev.type != EventType.KeyDown) return picked;
            int step = ChoiceInput(bounds, focused >= 0, ev);
            if (step == 0) return picked;
            bool fromKeyboard = ev.type == EventType.KeyDown;
            ev.Use();
            int active = current.HasValue ? Array.IndexOf(choices, current.Value, offset, count) - offset : -1;
            int start = fromKeyboard ? focused : active;
            var dest = choices[offset + Mathf.Clamp(start + step, 0, count - 1)];
            if ((int)choices[0] <= (int)SelectGlyph.Edge && start >= 0)
            {
                int max = Advanced ? (int)SelectMode.Edge : (int)SelectMode.BoundaryLoop;
                int next = Mathf.Clamp((int)choices[offset + start] + step, 0, max);
                if (!Advanced && (int)_selectMode > max && step > 0 && next == max && (int)choices[offset + start] >= max)
                    next = (int)_selectMode;
                dest = (SelectGlyph)next;
            }
            if (fromKeyboard)
            {
                _focusSelectionName = s_selectionNames[(int)dest];
                _focusSelectionInScene = scene;
            }
            return commands ? picked : dest;
        }

        void DrawSelectModeStrip(bool bar, float width = -1f)
        {
            if (width <= 0f) width = PanelContentWidth;
            _hoveredSelection = _focusedSelection = null;
            SelectGlyph? current = bar && !ScenePicking ? null : (SelectGlyph)_selectMode;
            var piece = DrawSelectionRow(PieceGlyphs, current, bar, width);
            GUILayout.Space(2f);
            var mesh = DrawSelectionRow(MeshGlyphs, current, bar, width, Advanced ? MeshGlyphs.Length : 2);
            if (!Advanced && (int)_selectMode >= (int)SelectMode.FaceLoop)
                mesh = DrawSelectionRow(MeshGlyphs, current, bar, width, 1, (int)_selectMode - (int)SelectMode.Face) ?? mesh;
            var picked = mesh ?? piece;
            if (!picked.HasValue || picked == current) return;
            var mode = (SelectMode)picked.Value;
            if (bar) ChangeSceneBar("Choose selection tool", () => SetSelectMode(mode, true));
            else SetSelectMode(mode);
        }

        void DrawSelectOpSwitch(float width = -1f, bool scene = false)
        {
            if (width <= 0f) width = PanelContentWidth;
            bool mask = WandMasksLayer;
            var choices = mask ? MaskActionGlyphs : OperationGlyphs;
            var current = choices[mask ? (WandHides ? 1 : 0) : (int)_selectOp];
            var picked = DrawSelectionRow(choices, current, scene, width);
            StudioGUI.Count(mask ? "limit.wandMaskAction" : "limit.operation", GUILayoutUtility.GetLastRect());
            if (!picked.HasValue || picked == current) return;
            if (mask)
                ChangeSceneBar("Set Wand mask action", () => _wandMaskAction = picked == SelectGlyph.Hide ? WandMaskAction.Hide : WandMaskAction.Reveal);
            else
                ChangeSceneBar("Set selection operation", () => _selectOp = (SelectOp)Array.IndexOf(OperationGlyphs, picked.Value));
        }

        void DrawSelectionSetActions(float width, bool scene)
        {
            var picked = DrawSelectionRow(SetGlyphs, null, scene, width, commands: true);
            if (picked == SelectGlyph.All) SelectEverything();
            if (picked == SelectGlyph.None) SelectNothing();
            if (picked == SelectGlyph.Invert) InvertSelection();
        }
    }
}
