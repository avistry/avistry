using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static partial class AgentApi
    {
        const string PictureFolder = "Temp/Avistry";
        const string PictureFile = "agent-picture.png";
        const string SaveDialogTitle = "Save your edit";

        class PendingWork
        {
            public string kind;
            public bool use;
            public int size;
            public string detail;
            public AgentAccess.Request request;
        }

        static PendingWork s_pending;

        internal static string OpenRequestDetail => s_pending?.detail;

        static AgentResult RequestAction(string action, AgentArgs a)
        {
            switch (action)
            {
                case "status": return RequestStatus(a);
                case "cancel":
                    AgentAccess.CloseRequest();
                    s_pending = null;
                    return Done(action, D(), null);
                case "save_png": case "bake_ao": case "make_normal_map": case "make_face_shadow": case "picture":
                    break;
                default:
                    return null;
            }
            var w = Window();
            if (w == null) return NoWindow(action);
            var busy = BusyCheck(action, w);
            if (busy != null) return busy;
            if (w.AutoCompositor == null || w.AutoCurrentSlot == null) return NoTarget(action);

            int size = AgentArgs.DefaultPictureSize;
            if (action == "picture")
            {
                if (a.size.HasValue) size = Mathf.RoundToInt(a.size.Value);
                if (size < AgentArgs.MinPictureSize || size > AgentArgs.MaxPictureSize)
                    return Invalid(action, $"size is {AgentArgs.MinPictureSize} to {AgentArgs.MaxPictureSize} pixels.");
            }
            else if (action == "save_png")
            {
                if (w.AutoRecipe == null || w.AutoRecipe.layers.Count == 0) return Invalid(action, "There is nothing to save yet: no layers.", "Add or paint a layer first.");
                if (w.AutoOutputMode == OutputMode.UpdateOriginalMaterial)
                    return Fail(action, AgentResult.Codes.Unsupported, "The person's save setting would change the original material.", "Ask them to choose a copy of the material in the Save tab.");
            }
            else if ((action == "bake_ao" || action == "make_normal_map") && w.AutoCurrentSlot.role != TextureRole.Color)
                return Fail(action, AgentResult.Codes.Unsupported, "This map is made from a colour texture, and another kind is open.", "Use set_target with the main colour texture first.");

            var work = new PendingWork { kind = action, use = a.use ?? false, size = size };
            string texture = w.AutoCurrentSlot.texture != null ? w.AutoCurrentSlot.texture.name : "the texture";
            string summary = Summary(w, work, texture, out work.detail);

            var open = AgentAccess.OpenRequest;
            if (open != null && open.state == AgentAccess.RequestState.Waiting)
                return Fail(action, AgentResult.Codes.RequestOpen, "Another request is still waiting for the person.", "Wait for it, or use avistry_request cancel.");

            if (action == "picture" ? AgentAccess.AllowPictures : AgentAccess.AllowSaves)
                return Execute(w, work);

            work.request = AgentAccess.BeginRequest(action, summary);
            s_pending = work;
            return AgentResult.Pending(action, "Waiting for the person to click Allow in Unity.", D(("kind", action)));
        }

        static string Summary(AvatarTextureWindow w, PendingWork work, string texture, out string detail)
        {
            switch (work.kind)
            {
                case "save_png":
                    bool copy = w.AutoOutputMode == OutputMode.DuplicateMaterial;
                    detail = SaveDialogText(w);
                    return $"Your AI helper asks to save a new PNG{(copy ? " and material copy" : "")} of {texture}. Your original stays as it is.";
                case "bake_ao":
                    detail = "Bakes ambient occlusion (soft shading from the model) into a new image next to the texture and adds it on top as a Multiply layer. Ctrl+Z removes the layer. The new image stays as a new file.";
                    return $"Your AI helper asks to bake ambient occlusion for {texture}: a new image and a layer.";
                case "make_normal_map":
                    detail = $"Makes a new normal map image next to {texture}." + (work.use ? " It is then put in the material's normal map slot. Ctrl+Z undoes that." : " The material is not changed.");
                    return $"Your AI helper asks to make a new normal map from {texture}{(work.use ? " and use it on the material" : "")}.";
                case "make_face_shadow":
                    detail = $"Makes a new face shadow map image next to {texture} from the selected pieces." + (work.use ? " It is then put in the material and its face shading is switched to SDF. Ctrl+Z undoes that." : " The material is not changed.");
                    return $"Your AI helper asks to make a new face shadow map from {texture}{(work.use ? " and use it on the material" : "")}.";
                default:
                    detail = $"Draws {texture} with the current layers at {work.size} pixels and keeps it as a temporary file in this project's Temp folder. The picture is sent to your AI service.";
                    return "Your AI helper asks to see a picture of your texture. It will be sent to your AI service.";
            }
        }

        static string SaveDialogText(AvatarTextureWindow w)
        {
            string text = null;
            var previous = StudioDialog.Override;
            StudioDialog.Override = q => { if (q.title == Loc.T(SaveDialogTitle)) text = q.message; return 1; };
            try { w.AutoBake(); }
            finally { StudioDialog.Override = previous; }
            return text;
        }

        static AgentResult RequestStatus(AgentArgs a)
        {
            var r = AgentAccess.OpenRequest;
            if (r == null)
                return Fail(a.action, AgentResult.Codes.Expired, "No request is open. It may have been dropped when Unity reloaded its scripts.", "Ask again.");
            switch (r.state)
            {
                case AgentAccess.RequestState.Waiting:
                    return AgentResult.Pending(a.action, "Waiting for the person to click Allow in Unity.", D(("kind", r.kind)));
                case AgentAccess.RequestState.Allowed when r.result == null:
                    return AgentResult.Pending(a.action, "The person allowed it. Working on it now.", D(("kind", r.kind)));
                case AgentAccess.RequestState.Allowed:
                {
                    var result = r.result;
                    AgentAccess.CloseRequest();
                    s_pending = null;
                    return result;
                }
                case AgentAccess.RequestState.Denied:
                    AgentAccess.CloseRequest();
                    s_pending = null;
                    return Fail(r.kind, AgentResult.Codes.Denied, "The person clicked Deny.", "Don't ask again unless they say so.");
                default:
                    AgentAccess.CloseRequest();
                    s_pending = null;
                    return Fail(r.kind, AgentResult.Codes.Expired, r.state == AgentAccess.RequestState.Expired ? "The person did not answer within 5 minutes." : "The request was cancelled.", "Ask again if it is still needed.");
            }
        }

        internal static bool Answer(bool allow, bool forSession = false, bool defer = false, bool fireLearn = true)
        {
            var req = AgentAccess.OpenRequest;
            if (req == null || req.state != AgentAccess.RequestState.Waiting) return false;
            var work = s_pending != null && s_pending.request == req ? s_pending : new PendingWork { kind = req.kind, size = AgentArgs.DefaultPictureSize, request = req };
            if (!allow)
            {
                AgentAccess.AnswerRequest(AgentAccess.RequestState.Denied);
                s_pending = null;
                return true;
            }
            if (fireLearn) Learn.Did(LearnEvents.AgentApproved);
            if (forSession)
            {
                if (req.kind == "picture") AgentAccess.AllowPictures = true; else AgentAccess.AllowSaves = true;
            }
            AgentAccess.AnswerRequest(AgentAccess.RequestState.Allowed);
            if (defer) EditorApplication.delayCall += () => Finish(req, work);
            else Finish(req, work);
            return true;
        }

        static void Finish(AgentAccess.Request req, PendingWork work)
        {
            AgentResult res;
            try
            {
                var w = Window();
                if (w == null) res = NoWindow(req.kind);
                else if (AgentAccess.Current < AgentAccess.Level.LookAndEdit) res = Fail(req.kind, AgentResult.Codes.ReadOnly, "The AI helper may only look now.", AgentResult.ReadOnlyHint);
                else res = Execute(w, work);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Avistry] The approved AI helper request failed: " + ex.GetType().Name + " " + Scrub(ex.Message));
                res = Fail(req.kind, AgentResult.Codes.Unsupported, "Something went wrong while running this request.", "Ask the person to look at the Console in Unity.");
            }
            req.result = Clean(res);
        }

        static AgentResult Execute(AvatarTextureWindow w, PendingWork work)
        {
            switch (work.kind)
            {
                case "save_png":
                    return InScope(w, "Save PNG", q => q.title == Loc.T(SaveDialogTitle) ? 0 : ShowToPerson(q), () =>
                    {
                        var before = w.AutoLastSave;
                        w.AutoBake();
                        var after = w.AutoLastSave;
                        if (after.png == null || after.png == before.png)
                            return Fail(work.kind, AgentResult.Codes.Unsupported, w.AutoLastToast() ?? "The save did not finish. Nothing was written.");
                        return Done(work.kind, D(("png", after.png), ("material", after.material), ("recipe", after.recipe)), "Saved " + Path.GetFileName(after.png));
                    });
                case "bake_ao":
                    return InScope(w, "Bake ambient occlusion", null, () =>
                    {
                        w.AutoClearToast();
                        w.AutoBakeAO();
                        string path = w.AutoLastAoPath;
                        if (path == null) return Fail(work.kind, AgentResult.Codes.Unsupported, w.AutoLastToast() ?? "The ambient occlusion was not made.");
                        return Done(work.kind, D(("asset", path)), "Baked ambient occlusion");
                    });
                case "make_normal_map":
                    return InScope(w, "Make normal map", null, () =>
                    {
                        w.AutoClearToast();
                        string path = AssetDatabase.GetAssetPath(w.AutoMakeNormalMap(work.use));
                        if (string.IsNullOrEmpty(path)) return Fail(work.kind, AgentResult.Codes.Unsupported, w.AutoLastToast() ?? "The normal map was not made.");
                        return Done(work.kind, D(("asset", path)), "Made a normal map");
                    });
                case "make_face_shadow":
                    return InScope(w, "Make face shadow map", null, () =>
                    {
                        w.AutoClearToast();
                        string path = AssetDatabase.GetAssetPath(w.AutoMakeFaceShadow(work.use, out _));
                        if (string.IsNullOrEmpty(path)) return Fail(work.kind, AgentResult.Codes.Unsupported, w.AutoLastToast() ?? "The face shadow map was not made.");
                        return Done(work.kind, D(("asset", path)), "Made a face shadow map");
                    });
                default:
                    return InScope(w, null, null, () => MakePicture(w, work.size));
            }
        }

        static AgentResult MakePicture(AvatarTextureWindow w, int size)
        {
            var slot = w.AutoCurrentSlot;
            if (slot == null || w.AutoRecipe == null) return NoTarget("picture");
            Texture2D tex;
            using (var comp = new Compositor(slot, size))
            {
                comp.Compose(w.AutoRecipe.layers, w.AutoMeshUV);
                tex = comp.ReadResult(false);
            }
            byte[] png = tex.EncodeToPNG();
            int width = tex.width, height = tex.height;
            UnityEngine.Object.DestroyImmediate(tex);
            Directory.CreateDirectory(PictureFolder);
            File.WriteAllBytes(PictureFolder + "/" + PictureFile, png);
            var data = D(("path", PictureFolder + "/" + PictureFile), ("width", width), ("height", height));
            if (size <= AgentArgs.MaxInlinePictureSize) data["base64"] = Convert.ToBase64String(png);
            AgentAccess.Log("Showed a picture of the texture");
            return Done("picture", data, "Showed a picture of the texture");
        }
    }
}
