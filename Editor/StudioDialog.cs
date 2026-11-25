using System;
using System.Collections.Generic;
using UnityEditor;

namespace Avistry.Textures
{
    internal static class StudioDialog
    {
        internal class Question
        {
            public string title, message, ok, cancel, alt;
            public int answer;
        }

        internal static Func<Question, int> Override;

        internal static readonly List<Question> Log = new List<Question>();

        internal static string NextSavePath;

        internal static bool Ask(string title, string message, string ok, string cancel = null)
        {
            if (Override == null) return cancel == null ? EditorUtility.DisplayDialog(title, message, ok) : EditorUtility.DisplayDialog(title, message, ok, cancel);
            return Answer(new Question { title = title, message = message, ok = ok, cancel = cancel }) == 0;
        }

        internal static bool Ask(string title, string message, string ok, string cancel, DialogOptOutDecisionType optOutType, string optOutKey)
        {
            if (Override == null) return EditorUtility.DisplayDialog(title, message, ok, cancel, optOutType, optOutKey);
            return Answer(new Question { title = title, message = message, ok = ok, cancel = cancel }) == 0;
        }

        internal static int AskComplex(string title, string message, string ok, string cancel, string alt)
        {
            if (Override == null) return EditorUtility.DisplayDialogComplex(title, message, ok, cancel, alt);
            return Answer(new Question { title = title, message = message, ok = ok, cancel = cancel, alt = alt });
        }

        internal static string SaveFilePanelInProject(string title, string defaultName, string extension, string message, string folder = null)
        {
            if (Override == null && NextSavePath == null)
                return folder == null ? EditorUtility.SaveFilePanelInProject(title, defaultName, extension, message)
                                      : EditorUtility.SaveFilePanelInProject(title, defaultName, extension, message, folder);
            string p = NextSavePath;
            NextSavePath = null;
            Answer(new Question { title = title, message = $"{message} (file panel, answered with: {p ?? "cancel"})", ok = "Save", cancel = "Cancel", answer = p != null ? 0 : 1 });
            return p ?? "";
        }

        static int Answer(Question q)
        {
            if (Override != null && q.message != null && !q.message.Contains("(file panel")) q.answer = Override(q);
            Log.Add(q);
            if (Log.Count > 50) Log.RemoveAt(0);
            return q.answer;
        }
    }
}
