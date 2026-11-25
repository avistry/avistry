using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        void HandleBrushColourCode(Rect swatch, bool main)
        {
            var input = Event.current;
            if (input.type != EventType.ContextClick || !swatch.Contains(input.mousePosition)) return;
            input.Use();
            PopupWindow.Show(swatch, new BrushColourCodePopup(this, main));
        }

        sealed class BrushColourCodePopup : PopupWindowContent
        {
            const string CodeControl = "AvistryBrushColourCode";
            readonly AvatarTextureWindow _owner;
            readonly bool _main;
            string _text;
            bool _focus = true;
            bool _error;

            internal BrushColourCodePopup(AvatarTextureWindow owner, bool main)
            {
                _owner = owner;
                _main = main;
                _text = CurrentCode;
            }

            Color Swatch => _main ? _owner._brush.color : _owner._brush.subColor;
            string CurrentCode => "#" + ColorUtility.ToHtmlStringRGB(Swatch);

            public override Vector2 GetWindowSize() => new Vector2(270f, 108f);

            public override void OnGUI(Rect rect)
            {
                var input = Event.current;
                bool key = input.type == EventType.KeyDown;
                if (_owner == null || key && input.keyCode == KeyCode.Escape)
                {
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                    return;
                }
                bool enter = key && (input.keyCode == KeyCode.Return || input.keyCode == KeyCode.KeypadEnter) &&
                    GUI.GetNameOfFocusedControl() == CodeControl;
                bool apply, paste;
                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    GUILayout.Label(Loc.T("Colour code"), EditorStyles.boldLabel);
                    GUI.SetNextControlName(CodeControl);
                    string next = EditorGUILayout.TextField(_text);
                    if (next != _text) _error = false;
                    _text = next;
                    if (_focus && input.type == EventType.Repaint)
                    {
                        EditorGUI.FocusTextInControl(CodeControl);
                        _focus = false;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(Loc.T("Copy"), EditorStyles.miniButtonLeft))
                            EditorGUIUtility.systemCopyBuffer = CurrentCode;
                        paste = GUILayout.Button(Loc.T("Paste"), EditorStyles.miniButtonMid);
                        apply = GUILayout.Button(Loc.T("Apply"), EditorStyles.miniButtonRight);
                    }

                    GUILayout.Label(_error ? Loc.T("That colour code couldn't be read. Try hex, RGB, HSL or HSV.") :
                        Loc.T("Hex, RGB, HSL or HSV"), StudioGUI.Hint);
                }

                if (!apply && !enter && !paste) return;
                _error = !TryApplyCode(paste ? EditorGUIUtility.systemCopyBuffer : _text);
                if (_error) return;
                editorWindow.Close();
                GUIUtility.ExitGUI();
            }

            bool TryApplyCode(string code)
            {
                if (_owner == null || !ColourCode.TryParse(code, out var colour, out _)) return false;
                var old = Swatch;
                colour.a = old.a;
                if (colour == old) return true;
                Undo.RecordObject(_owner, "Change brush colour");
                if (_main) _owner._brush.color = colour;
                else _owner._brush.subColor = colour;
                _owner.Repaint();
                SceneView.RepaintAll();
                return true;
            }
        }
    }
}
