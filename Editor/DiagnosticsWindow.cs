using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal class DiagnosticsWindow : EditorWindow
    {
        [SerializeField] string _text = "";
        bool _copied;
        Vector2 _scroll;

        internal static void Show(string text)
        {
            var window = GetWindow<DiagnosticsWindow>(true, Loc.T("Support report"));
            window.titleContent = new GUIContent(Loc.T("Support report"), DiagnosticsReport.Logo);
            window.minSize = new Vector2(420, 340);
            window._text = text;
            window._copied = false;
            window._scroll = Vector2.zero;
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(DiagnosticsReport.Logo, GUILayout.Width(32), GUILayout.Height(32));
                GUILayout.Label(Loc.T("Support report"), EditorStyles.boldLabel, GUILayout.Height(32));
            }
            EditorGUILayout.HelpBox(Loc.T("Review the report, then copy it to share on Discord or GitHub. You choose what to send."), MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.SelectableLabel(_text, EditorStyles.textArea,
                GUILayout.ExpandWidth(true), GUILayout.Height(EditorStyles.textArea.CalcHeight(new GUIContent(_text), Mathf.Max(200, position.width - 30)) + 8));
            EditorGUILayout.EndScrollView();
            if (StudioGUI.PrimaryButton(new GUIContent(_copied ? Loc.T("Copied") : Loc.T("Copy report")), StudioGUI.InfoBlue))
            {
                GUIUtility.systemCopyBuffer = _text;
                _copied = true;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Loc.T("Open Discord"))) OnlineGuide.OpenSupport(false);
                if (GUILayout.Button(Loc.T("Open GitHub issue"))) OnlineGuide.OpenSupport(true);
            }
            EditorGUILayout.Space();
        }
    }
}
