using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// What an AI helper may do in the open Avistry Textures window. Three entry points split by risk: <see cref="Read"/>
    /// looks, <see cref="Edit"/> makes undoable edits, <see cref="Request"/> waits for the person to click Allow before it
    /// writes files or shares a picture. Every edit goes through the same window code as the UI and is one Undo step named
    /// "AI: ...". Nothing here opens a socket; the optional bridge (Editor/McpBridge) only translates tool calls to these.
    /// </summary>
    internal static partial class AgentApi
    {
        enum Kind { Read, Edit, Request }

        static readonly Queue<double> s_writes = new Queue<double>();

        internal static AgentResult Read(string action, AgentArgs a = null) => Run(Kind.Read, action, a);
        internal static AgentResult Edit(string action, AgentArgs a = null) => Run(Kind.Edit, action, a);
        internal static AgentResult Request(string action, AgentArgs a = null) => Run(Kind.Request, action, a);

        static AgentResult Run(Kind kind, string action, AgentArgs a)
        {
            action = (action ?? "").Trim();
            a ??= new AgentArgs();
            a.action = action;
            AgentResult res;
            try
            {
                bool edits = kind == Kind.Edit || (kind == Kind.Request && action != "status" && action != "cancel");
                res = AgentAccess.Gate(action, edits);
                if (res == null)
                {
                    res = kind == Kind.Read ? ReadAction(action, a) : kind == Kind.Edit ? EditAction(action, a) : RequestAction(action, a);
                    res ??= Fail(action, AgentResult.Codes.Unsupported, "This tool has no action called that.", "Check the action word.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Avistry] The AI helper call failed: " + ex.GetType().Name + " " + Scrub(ex.Message));
                res = Fail(action, AgentResult.Codes.Unsupported, "Something went wrong while running this action.", "Ask the person to look at the Console in Unity.");
            }
            return Clean(res);
        }

        static AgentResult Fail(string action, string code, string message, string hint = null) => AgentResult.Fail(action, code, message, hint);

        static AgentResult NoWindow(string action) =>
            Fail(action, AgentResult.Codes.NoWindow, "The Avistry Textures window is not open.", "Use avistry_edit open_window, or ask the person to open Tools › Avistry › Textures.");

        static AgentResult NoTarget(string action) =>
            Fail(action, AgentResult.Codes.NoTarget, "No part is picked yet.", "Use avistry_edit set_target first.");

        static AvatarTextureWindow Window() => AvatarTextureWindow.AutoFind(false);

        static Dictionary<string, object> D(params (string key, object value)[] items)
        {
            var d = new Dictionary<string, object>();
            foreach (var (k, v) in items) d[k] = v;
            return d;
        }

        static readonly Regex s_path = new Regex(@"(?:[A-Za-z]:[\\/]|\\\\|/(?:Users|home)/)[^\s""'’”]*", RegexOptions.Compiled);

        /// <summary>Messages may name assets, never a drive or home folder.</summary>
        static string Scrub(string s) => string.IsNullOrEmpty(s) ? s : s_path.Replace(s, "<path>");

        static AgentResult Clean(AgentResult r)
        {
            r.message = Scrub(r.message);
            r.hint = Scrub(r.hint);
            r.toast = Scrub(r.toast);
            r.undo = Scrub(r.undo);
            if (r.changed != null) for (int i = 0; i < r.changed.Length; i++) r.changed[i] = Scrub(r.changed[i]);
            return r;
        }

        /// <summary>Null when an edit may go ahead. A person who is dragging or saving, or a test that set its own dialog answer, is never interrupted.</summary>
        static AgentResult BusyCheck(string action, AvatarTextureWindow w)
        {
            string why = null;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) why = "Unity is compiling or importing.";
            else if (EditorApplication.isPlayingOrWillChangePlaymode) why = "Unity is in play mode.";
            else if (GUIUtility.hotControl != 0) why = "The person is dragging something.";
            else if (StudioDialog.Override != null) why = "Another automatic answer to the tool's dialogs is active.";
            else if (w != null && w.AutoBusyReason() is string window) why = window;
            if (why != null) return Fail(action, AgentResult.Codes.Busy, why, "Wait a moment and try again.");
            return null;
        }

        /// <summary>Counts one write. False once more than 20 came in the last 10 seconds.</summary>
        static bool TakeWriteSlot()
        {
            double now = EditorApplication.timeSinceStartup;
            while (s_writes.Count > 0 && now - s_writes.Peek() > AgentArgs.WriteWindowSeconds) s_writes.Dequeue();
            if (s_writes.Count >= AgentArgs.MaxWritesPerWindow) return false;
            s_writes.Enqueue(now);
            return true;
        }

        /// <summary>Forgets the write counter and the open request's details. For tests.</summary>
        internal static void Reset()
        {
            s_writes.Clear();
            s_pending = null;
        }

        /// <summary>
        /// Runs <paramref name="body"/> the way the person's own click would: skill progress stays untouched, the tool's
        /// dialogs are answered by <paramref name="answer"/>, and (when <paramref name="label"/> is given) every Undo step
        /// it makes becomes one step named "AI: label".
        /// </summary>
        static AgentResult InScope(AvatarTextureWindow w, string label, Func<StudioDialog.Question, int> answer, Func<AgentResult> body)
        {
            using (Learn.Suppress())
            {
                var previous = StudioDialog.Override;
                StudioDialog.Override = answer;
                try
                {
                    if (label == null) return body();
                    string name = "AI: " + label;
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName(name);
                    double toast = w.AutoToastStamp;
                    AgentResult res;
                    // A throwing hook must not leave a half-open group that swallows the person's next steps.
                    try { res = body(); }
                    finally
                    {
                        Undo.SetCurrentGroupName(name);
                        Undo.CollapseUndoOperations(group);
                        Undo.SetCurrentGroupName(name);
                    }
                    w.AutoSettle();
                    Undo.IncrementCurrentGroup();
                    if (w.AutoToastStamp != toast) res.toast = w.AutoLastToast();
                    if ((res.ok || res.partial) && !res.pending)
                    {
                        res.undo = name;
                        AgentAccess.Log(LogText(res, label), name, group);
                    }
                    return res;
                }
                finally { StudioDialog.Override = previous; }
            }
        }

        static string LogText(AgentResult r, string label) =>
            r.changed != null && r.changed.Length > 0 ? r.changed[0] : label;

        /// <summary>Questions during a helper edit: a target change follows on_layers, anything else is answered Cancel.</summary>
        // Matched on its "Move layers" button: the title is translated and has no fixed English prefix.
        static Func<StudioDialog.Question, int> EditAnswers(string onLayers) =>
            q => q.ok == Msg.BtnMoveLayers && onLayers == "keep" ? 0 : 1;

        /// <summary>The same question the person would see, shown the normal way.</summary>
        static int ShowToPerson(StudioDialog.Question q)
        {
            if (q.alt != null) return EditorUtility.DisplayDialogComplex(q.title, q.message, q.ok, q.cancel, q.alt);
            if (string.IsNullOrEmpty(q.cancel)) { EditorUtility.DisplayDialog(q.title, q.message, q.ok); return 0; }
            return EditorUtility.DisplayDialog(q.title, q.message, q.ok, q.cancel) ? 0 : 1;
        }

        static string AccessWord() =>
            AgentAccess.Current == AgentAccess.Level.LookAndEdit ? "edit" : AgentAccess.Current == AgentAccess.Level.LookOnly ? "look" : "off";

        static string PartId(Renderer r) => GlobalObjectId.GetGlobalObjectIdSlow(r).ToString();

        static string FamilyWord(Material m)
        {
            if (m == null) return "other";
            switch (ShaderProfiles.Detect(m.shader))
            {
                case ShaderFamily.Poiyomi: return "poiyomi";
                case ShaderFamily.LilToon: return "liltoon";
                default: return "other";
            }
        }

        static string RoleWord(TextureRole r) => r == TextureRole.Color ? "colour" : r == TextureRole.Mask ? "mask" : "normal map";

        static bool IsMeshRenderer(Renderer r) => r is SkinnedMeshRenderer || r is MeshRenderer;

        /// <summary>The mesh renderers with a material under an avatar root, in Hierarchy order.</summary>
        static List<Renderer> PartsUnder(Transform root)
        {
            var list = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !IsMeshRenderer(r) || !r.gameObject.scene.IsValid()) continue;
                bool any = false;
                foreach (var m in r.sharedMaterials) if (m != null) { any = true; break; }
                if (any) list.Add(r);
            }
            return list;
        }

        static string HierarchyPath(Renderer r, Transform root)
        {
            string rel = r.transform == root ? "" : AnimationUtility.CalculateTransformPath(r.transform, root);
            return rel.Length == 0 ? root.name : root.name + "/" + rel;
        }
    }
}
