using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly BlendMode[] AllBlendModes = (BlendMode[])Enum.GetValues(typeof(BlendMode));
        static readonly BrushTip[] AllBrushTips = (BrushTip[])Enum.GetValues(typeof(BrushTip));
        const float StackedChoiceWidth = 175f;

        static T SteppedChoice<T>(GUIContent label, T current, T[] choices, Func<Rect, T, T> popup, float width = -1f)
        {
            bool hasLabel = !string.IsNullOrEmpty(label?.text);
            if (hasLabel && width > 0f && width < StackedChoiceWidth)
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.MaxWidth(width));
                hasLabel = false;
            }
            var rect = EditorGUILayout.GetControlRect(hasLabel);
            if (width > 0f) rect.width = Mathf.Min(rect.width, width);
            if (hasLabel) rect = EditorGUI.PrefixLabel(rect, label);
            return SteppedChoice(rect, current, choices, popup);
        }

        static T SteppedChoice<T>(Rect rect, T current, T[] choices, Func<Rect, T, T> popup)
        {
            string focus = "AvistryChoice" + GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            int index = Array.IndexOf(choices, current);
            bool click = e.type == EventType.MouseUp;
            int direction = GUI.enabled ? ChoiceInput(rect, e.type == EventType.KeyDown &&
                GUI.GetNameOfFocusedControl() == focus, e) : 0;
            if (direction != 0) e.Use();

            const float arrowWidth = 18f;
            var field = new Rect(rect.x + arrowWidth, rect.y, Mathf.Max(0f, rect.width - 2f * arrowWidth), rect.height);
            GUI.SetNextControlName(focus);
            T picked = popup(field, current);
            using (new EditorGUI.DisabledScope(index <= 0))
                if (GUI.Button(new Rect(rect.x, rect.y, arrowWidth, rect.height), new GUIContent("‹",
                        Loc.T("Previous choice. Focus this field and press an arrow key, or hold Shift while scrolling here.")),
                        EditorStyles.miniButtonLeft)) direction = -1;
            using (new EditorGUI.DisabledScope(index >= choices.Length - 1))
                if (GUI.Button(new Rect(rect.xMax - arrowWidth, rect.y, arrowWidth, rect.height), new GUIContent("›",
                        Loc.T("Next choice. Focus this field and press an arrow key, or hold Shift while scrolling here.")),
                        EditorStyles.miniButtonRight)) direction = 1;
            if (direction == 0) return picked;
            if (click) GUI.FocusControl(focus);
            int next = Mathf.Clamp(index + direction, 0, choices.Length - 1);
            if (next == index) return current;
            GUI.changed = true;
            return choices[next];
        }

        static int DrawStepSelectionGrid(Rect rect, int selected, GUIContent[] choices, int columns, GUIStyle style, int keyboard = 0)
        {
            var e = Event.current;
            if (keyboard == 0) keyboard = GUIUtility.GetControlID(FocusType.Keyboard, rect);
            bool canNavigate = GUI.enabled && !EditorGUIUtility.editingTextField;
            int direction = canNavigate ? ChoiceInput(rect, GUIUtility.keyboardControl == keyboard, e) : 0;
            if (direction != 0)
            {
                e.Use();
                int next = Mathf.Clamp(selected + direction, 0, choices.Length - 1);
                if (next != selected) GUI.changed = true;
                selected = next;
            }

            bool press = GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition);
            selected = GUI.SelectionGrid(rect, selected, choices, columns, style);
            if (press && e.type == EventType.Used) GUIUtility.keyboardControl = keyboard;
            if (e.type == EventType.Repaint && canNavigate && GUIUtility.keyboardControl == keyboard)
                StudioGUI.DrawRoundedBorder(rect, StudioGUI.Accent, 3f);
            return selected;
        }

        static int ChoiceInput(Rect rect, bool hasFocus, Event e)
        {
            if (e.alt || e.command || e.control) return 0;
            if (e.type == EventType.ScrollWheel && e.shift && rect.Contains(e.mousePosition))
            {
                float wheel = e.delta.y != 0f ? e.delta.y : e.delta.x;
                return wheel > 0f ? 1 : wheel < 0f ? -1 : 0;
            }
            if (e.type != EventType.KeyDown || !hasFocus) return 0;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.UpArrow: return -1;
                case KeyCode.RightArrow:
                case KeyCode.DownArrow: return 1;
                default: return 0;
            }
        }
    }
}
