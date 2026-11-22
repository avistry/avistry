using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal class DiagnosticsWindow : EditorWindow
    {
        string _text = "";
        Vector2 _scroll;

        internal static void Show(string text)
        {
            var w = GetWindow<DiagnosticsWindow>(true, Loc.T("Copy diagnostics"));
            w.minSize = new Vector2(420, 320);
            w._text = text;
            w.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(Loc.T("Check this before you share it. It has no file paths or user names. Nothing is copied until you press Copy."), MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.SelectableLabel(_text, EditorStyles.textArea, GUILayout.ExpandHeight(true), GUILayout.MinHeight(position.height - 90));
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Loc.T("Copy"), GUILayout.Width(90)))
                {
                    GUIUtility.systemCopyBuffer = _text;
                    Close();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(Loc.T("Close"), GUILayout.Width(90))) { Close(); GUIUtility.ExitGUI(); }
            }
        }
    }
}
