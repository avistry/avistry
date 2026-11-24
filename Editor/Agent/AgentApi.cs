using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static partial class AgentApi
    {
        enum Kind { Read, Edit, Request }

        static readonly Queue<double> s_writes = new Queue<double>();
        static int s_undoSerial;

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

        static bool TakeWriteSlot()
        {
            double now = EditorApplication.timeSinceStartup;
            while (s_writes.Count > 0 && now - s_writes.Peek() > AgentArgs.WriteWindowSeconds) s_writes.Dequeue();
            if (s_writes.Count >= AgentArgs.MaxWritesPerWindow) return false;
            s_writes.Enqueue(now);
            return true;
        }

        internal static void Reset()
        {
            s_writes.Clear();
            s_pending = null;
            ClearHelperUndo();
        }

        static AgentResult InScope(AvatarTextureWindow w, string label, Func<StudioDialog.Question, int> answer, Func<AgentResult> body)
        {
            using (Learn.Suppress())
            {
                var previous = StudioDialog.Override;
                StudioDialog.Override = answer;
                try
                {
                    if (label == null) return body();
                    string name = "AI: " + label + " #" + ++s_undoSerial;
                    Undo.FlushUndoRecordObjects();
                    var undoBefore = ReadUndoTimeline() ? s_appliedUndo.ToArray() : null;
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    double toast = w.AutoToastStamp;
                    AgentResult res;
                    try
                    {
                        res = body();
                        w.AutoSettle();
                    }
                    finally
                    {
                        Undo.FlushUndoRecordObjects();
                        if (undoBefore == null || !ReadUndoTimeline() || !s_appliedUndo.SequenceEqual(undoBefore))
                        {
                            Undo.SetCurrentGroupName(name);
                            Undo.CollapseUndoOperations(group);
                        }
                        Undo.IncrementCurrentGroup();
                    }
                    if (w.AutoToastStamp != toast) res.toast = w.AutoLastToast();
                    if ((res.ok || res.partial) && !res.pending)
                    {
                        if (undoBefore != null && ReadUndoTimeline() && s_appliedUndo.Count > 0 &&
                            s_appliedUndo[s_appliedUndo.Count - 1] == name && !s_appliedUndo.SequenceEqual(undoBefore))
                        {
                            TrackHelperUndo(w, name);
                            res.undo = name;
                        }
                        AgentAccess.Log(LogText(res, label));
                    }
                    return res;
                }
                finally { StudioDialog.Override = previous; }
            }
        }

        static string LogText(AgentResult r, string label) =>
            r.changed != null && r.changed.Length > 0 ? r.changed[0] : label;

        static Func<StudioDialog.Question, int> EditAnswers(string onLayers) =>
            q => q.ok == Msg.BtnMoveLayers && onLayers == "keep" ? 0 : 1;

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
