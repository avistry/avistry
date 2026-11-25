using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class SupportSession
    {
        const string Prefix = "Avistry.Support.";
        const string Interrupted = "Editor session interrupted while Avistry was open";
        static readonly string Marker = Path.Combine("Library", "Avistry-open-session");
        static readonly List<AvatarTextureWindow> Windows = new List<AvatarTextureWindow>();
        static readonly Regex Frame = new Regex(@"^(?<type>[\w.<>+/]+)[.:](?<method>[\w<>]+)\s*\(");
        static readonly HashSet<string> ExceptionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "NullReferenceException", "InvalidOperationException", "ArgumentException", "ArgumentOutOfRangeException",
            "IndexOutOfRangeException", "KeyNotFoundException", "IOException", "UnauthorizedAccessException", "OutOfMemoryException",
            "NotSupportedException", "UnityException", "MissingReferenceException"
        };
        static bool s_logging, s_quitting;
        static string s_pending, s_launch;

        internal static string LastIssue => SessionState.GetString(Prefix + "Last", "");

        internal static void Open(AvatarTextureWindow window)
        {
            if (Application.isBatchMode || s_quitting || window.AutoSandbox || Windows.Contains(window)) return;
            Windows.Add(window);
            if (Windows.Count != 1) return;
            s_launch = SessionState.GetString(Prefix + "Launch", "");
            if (s_launch.Length == 0)
            {
                s_launch = Guid.NewGuid().ToString("N");
                SessionState.SetString(Prefix + "Launch", s_launch);
            }
            if (UpdateMarker(Marker, s_launch, true)) Queue(Interrupted);
            Application.logMessageReceived += OnLog;
            EditorApplication.quitting += OnQuit;
        }

        internal static void Close(AvatarTextureWindow window)
        {
            if (!Windows.Remove(window) || Windows.Count != 0) return;
            Application.logMessageReceived -= OnLog;
            EditorApplication.quitting -= OnQuit;
            EditorApplication.delayCall -= ShowPending;
            s_pending = null;
            UpdateMarker(Marker, s_launch, false);
        }

        static void OnQuit()
        {
            s_quitting = true;
            EditorApplication.delayCall -= ShowPending;
            UpdateMarker(Marker, s_launch, false);
        }

        internal static bool UpdateMarker(string path, string launch, bool open)
        {
            try
            {
                string previous = File.Exists(path) ? File.ReadAllText(path) : "";
                if (open)
                {
                    if (previous == launch) return false;
                    File.WriteAllText(path, launch);
                    return Guid.TryParseExact(previous, "N", out _);
                }
                if (previous == launch) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        internal static string Classify(string condition, string stack, LogType severity)
        {
            if (severity != LogType.Exception && severity != LogType.Error && severity != LogType.Assert) return null;
            if (string.IsNullOrEmpty(stack)) return null;
            string kind = (condition ?? "").Split(new[] { ':', '\r', '\n' }, 2)[0].Trim();
            if (kind.EndsWith("ExitGUIException", StringComparison.Ordinal) ||
                kind.EndsWith("OperationCanceledException", StringComparison.Ordinal) ||
                kind.EndsWith("TaskCanceledException", StringComparison.Ordinal)) return null;
            string exceptionName = kind.Substring(kind.LastIndexOf('.') + 1);
            string category = severity == LogType.Exception && ExceptionNames.Contains(exceptionName) ? exceptionName : severity.ToString();
            foreach (var line in stack.Split('\n'))
            {
                string frame = line.Trim();
                if (frame.StartsWith("at ", StringComparison.Ordinal)) frame = frame.Substring(3);
                if (frame.Length == 0 || frame.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                    frame.StartsWith("UnityEditor.", StringComparison.Ordinal) || frame.StartsWith("System.", StringComparison.Ordinal)) continue;
                var match = Frame.Match(frame);
                if (!match.Success) return null;
                string name = match.Groups["type"].Value.Replace('/', '+');
                if (!name.StartsWith("Avistry.Textures.", StringComparison.Ordinal)) return null;
                var type = typeof(AvatarTextureWindow).Assembly.GetType(name, false);
                if (type == null) return null;
                var owner = type;
                while (owner.DeclaringType != null) owner = owner.DeclaringType;
                if (owner == typeof(SupportSession) || owner == typeof(DiagnosticsWindow) || owner == typeof(DiagnosticsReport)) return null;
                string method = match.Groups["method"].Value;
                var members = type.GetMember(method, System.Reflection.MemberTypes.Method,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.DeclaredOnly);
                string verifiedMethod = members.Length > 0 && type == owner ? "." + members[0].Name : "";
                return category + " in " + owner.Name + verifiedMethod;
            }
            return null;
        }

        static void OnLog(string condition, string stack, LogType severity)
        {
            if (s_logging || s_quitting) return;
            string issue = Classify(condition, stack, severity);
            if (issue != null) Queue(issue);
        }

        internal static bool LogSaveFailure(Exception error)
        {
            s_logging = true;
            try { Debug.LogException(error); }
            finally { s_logging = false; }
            const string issue = "Could not save edit";
            SessionState.SetString(Prefix + "Last", issue);
            return FirstPrompt(issue);
        }

        static bool FirstPrompt(string issue)
        {
            string key = Prefix + "Shown." + issue;
            if (SessionState.GetBool(key, false)) return false;
            SessionState.SetBool(key, true);
            return true;
        }

        static void Queue(string issue)
        {
            if (s_pending != null || SessionState.GetBool(Prefix + "Shown." + issue, false)) return;
            s_pending = issue;
            SessionState.SetString(Prefix + "Last", issue);
            EditorApplication.delayCall += ShowPending;
        }

        static void ShowPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ShowPending;
                return;
            }
            string issue = s_pending;
            s_pending = null;
            if (s_quitting || Windows.Count == 0 || issue == null || !FirstPrompt(issue)) return;
            var window = Windows[Windows.Count - 1];
            if (window) window.OfferDiagnostics(issue == Interrupted
                ? Loc.T("Unity closed while Avistry was open. You can reopen a saved draft from Save. The cause is unknown.")
                : Loc.T("An Avistry tool reported an error. Open the Console for details or review a report to share with support."));
        }
    }
}
