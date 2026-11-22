using System;
using System.Collections.Generic;
using UnityEditor;

namespace Avistry.Textures
{
    /// <summary>
    /// Every confirmation dialog of the tool goes through here. Normally it is just EditorUtility's dialog; an
    /// automation harness can set <see cref="Override"/> to answer them without a modal window (and see what was asked).
    /// </summary>
    internal static class StudioDialog
    {
        /// <summary>A question the tool asked: title, message and the button labels (ok, cancel, alt; alt may be null).</summary>
        internal class Question
        {
            public string title, message, ok, cancel, alt;
            public int answer; // 0 = ok, 1 = cancel, 2 = alt
        }

        /// <summary>When set, returns the answer (0 = ok, 1 = cancel, 2 = alt) instead of showing a dialog.</summary>
        internal static Func<Question, int> Override;

        /// <summary>Questions answered by <see cref="Override"/> (most recent last, at most 50).</summary>
        internal static readonly List<Question> Log = new List<Question>();

        /// <summary>When set, SaveFilePanelInProject returns this path instead of opening a file panel (then clears it).</summary>
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
