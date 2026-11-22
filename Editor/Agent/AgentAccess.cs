using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// What the AI helper may do, who allowed it and what it did. Only the person changes the level, in the AI helper
    /// window or with the Stop button, so no tool can reach the setter. The level is a preference like the Advanced
    /// toggle, so it uses no Undo. Tests switch <see cref="Sandbox"/> on to keep everything in memory.
    /// </summary>
    internal static class AgentAccess
    {
        internal enum Level { Off = 0, LookOnly = 1, LookAndEdit = 2 }
        internal enum RequestState { Waiting, Allowed, Denied, Expired, Cancelled }

        /// <summary>One thing the helper asked the person to approve.</summary>
        internal class Request
        {
            public string kind;       // save_png, bake_ao, make_normal_map, make_face_shadow or picture
            public string summary;    // the text the banner shows
            public double since;      // EditorApplication.timeSinceStartup
            public RequestState state;
            public AgentResult result; // set when the approved work has finished
        }

        /// <summary>One thing the helper did, for the strip and the AI helper window. Memory only.</summary>
        internal class Activity
        {
            public string text;
            public string undoName;   // the Undo group name, or null
            public int undoGroup;     // Undo.GetCurrentGroup() when it was made
            public double time;
        }

        class Mem
        {
            public Level level;
            public bool allowSaves, allowPictures;
            public Request request;
            public readonly List<Activity> log = new List<Activity>();
        }

        internal const string LevelKey = "Avistry.Agent.Access";
        const string SavesKey = "Avistry.Agent.AllowSaves";
        const string PicturesKey = "Avistry.Agent.AllowPictures";
        internal const int ActivityMax = 50;
        internal const double RequestTimeoutSeconds = 300;

        static readonly Mem s_real = new Mem();
        static Mem s_sandbox;
        static Level? s_levelCache;

        internal static event Action Changed;

        static Mem M => s_sandbox ?? s_real;

        /// <summary>Keeps level, allowances, request and log in memory only, starting from Off. For tests.</summary>
        internal static bool Sandbox
        {
            get => s_sandbox != null;
            set
            {
                if (value == (s_sandbox != null)) return;
                s_sandbox = value ? new Mem() : null;
                Changed?.Invoke();
            }
        }

        /// <summary>Off by default.</summary>
        internal static Level Current
        {
            get
            {
                if (s_sandbox != null) return s_sandbox.level;
                return s_levelCache ??= (Level)Mathf.Clamp(EditorPrefs.GetInt(LevelKey, 0), 0, 2);
            }
            set
            {
                if (Current == value) return;
                if (s_sandbox != null) s_sandbox.level = value;
                else
                {
                    s_levelCache = value;
                    EditorPrefs.SetInt(LevelKey, (int)value);
                }
                if (value == Level.Off) { CloseRequest(); }
                Changed?.Invoke();
            }
        }

        /// <summary>"Allow saves until Unity closes". Unchecked by default and never stored.</summary>
        internal static bool AllowSaves
        {
            get => s_sandbox != null ? s_sandbox.allowSaves : SessionState.GetBool(SavesKey, false);
            set { if (s_sandbox != null) s_sandbox.allowSaves = value; else SessionState.SetBool(SavesKey, value); Changed?.Invoke(); }
        }

        /// <summary>"Allow pictures until Unity closes". Unchecked by default and never stored.</summary>
        internal static bool AllowPictures
        {
            get => s_sandbox != null ? s_sandbox.allowPictures : SessionState.GetBool(PicturesKey, false);
            set { if (s_sandbox != null) s_sandbox.allowPictures = value; else SessionState.SetBool(PicturesKey, value); Changed?.Invoke(); }
        }

        /// <summary>Null when the call may go ahead; otherwise the access_off or read_only answer.</summary>
        internal static AgentResult Gate(string action, bool edits)
        {
            var level = Current;
            if (level == Level.Off)
                return AgentResult.Fail(action, AgentResult.Codes.AccessOff, "The AI helper is switched off.", AgentResult.AccessOffHint);
            if (edits && level < Level.LookAndEdit)
                return AgentResult.Fail(action, AgentResult.Codes.ReadOnly, "The AI helper may only look.", AgentResult.ReadOnlyHint);
            return null;
        }

        /// <summary>The request that is waiting or was just answered, or null. A waiting one older than five minutes becomes Expired.</summary>
        internal static Request OpenRequest
        {
            get
            {
                var r = M.request;
                if (r != null && r.state == RequestState.Waiting && EditorApplication.timeSinceStartup - r.since > RequestTimeoutSeconds)
                {
                    r.state = RequestState.Expired;
                    Changed?.Invoke();
                }
                return r;
            }
        }

        /// <summary>Opens a request. Returns null when another one is still waiting (the caller answers request_open).</summary>
        internal static Request BeginRequest(string kind, string summary)
        {
            var open = OpenRequest;
            if (open != null && open.state == RequestState.Waiting) return null;
            var r = new Request { kind = kind, summary = summary, since = EditorApplication.timeSinceStartup, state = RequestState.Waiting };
            M.request = r;
            Changed?.Invoke();
            return r;
        }

        /// <summary>Records the person's answer. Only a waiting request can be answered.</summary>
        internal static void AnswerRequest(RequestState answer)
        {
            var r = OpenRequest;
            if (r == null || r.state != RequestState.Waiting) return;
            r.state = answer;
            Changed?.Invoke();
        }

        /// <summary>Forgets the request once its answer was delivered.</summary>
        internal static void CloseRequest()
        {
            if (M.request == null) return;
            if (M.request.state == RequestState.Waiting) M.request.state = RequestState.Cancelled;
            M.request = null;
            Changed?.Invoke();
        }

        internal static IReadOnlyList<Activity> Recent => M.log;

        internal static void Log(string text, string undoName = null, int undoGroup = -1)
        {
            var log = M.log;
            log.Add(new Activity { text = text, undoName = undoName, undoGroup = undoGroup, time = EditorApplication.timeSinceStartup });
            if (log.Count > ActivityMax) log.RemoveAt(0);
            Changed?.Invoke();
        }

        internal static void ClearActivity()
        {
            M.log.Clear();
            Changed?.Invoke();
        }
    }
}
