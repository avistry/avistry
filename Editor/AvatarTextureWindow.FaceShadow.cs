using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>1 · Pick › Make maps › Face shadow: makes a toon face shadow (SDF) map from the face mesh. See FaceShadow.</summary>
    partial class AvatarTextureWindow
    {
        static readonly int[] FaceSizes = { 512, 1024, 2048, 0 };

        Texture2D MadeFaceShadowFor(TextureSlot slot) =>
            _madeFaceShadow != null && slot?.texture != null && _madeFaceShadow.name.StartsWith(slot.texture.name + "_FaceShadow") ? _madeFaceShadow : null;

        void DrawFaceShadowGroup()
        {
            var slot = CurrentSlot;
            StudioGUI.BeginGroup(Loc.T("Face shadow"), Loc.T("toon faces"), infoId: "sdf-face-shadow");
            GUILayout.Label(Loc.T("A shadow map for anime-style faces: the shadow sweeps cleanly across the face as the light turns to the side, " +
                            "without blotches from the nose or eyes. Made from this part, so edit the face part. Works with lilToon and Poiyomi."), StudioGUI.Hint);
            int srcW = _comp.SourceWidth;
            var names = FaceSizes.Select(s => s == 0 ? Loc.F("Same as the texture ({0} px)", srcW) : $"{s} px").ToArray();
            int si = Mathf.Max(0, Array.IndexOf(FaceSizes, _face.size));
            StudioGUI.Count("maps.faceSize");
            _face.size = FaceSizes[EditorGUILayout.Popup(new GUIContent(Loc.T("Size"), Loc.T("Face shadows are smooth, so 1024 px is plenty")), si, names)];
            StudioGUI.Count("maps.faceSwapSides");
            _face.swapSides = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Swap left and right"),
                Loc.T("Tick this if the shadow lands on the side the light comes from, then make the map again")), _face.swapSides);
            if (_selection.Count > 0)
                GUILayout.Label(ShaderProfiles.Detect(_material.shader) == ShaderFamily.LilToon
                    ? Loc.F("Uses the {0} selected piece{1} as the face; the rest keeps its normal shading.", _selection.Count, _selection.Count == 1 ? "" : "s")
                    : Loc.F("Uses the {0} selected piece{1} as the face.", _selection.Count, _selection.Count == 1 ? "" : "s"), StudioGUI.Hint);

            var made = MadeFaceShadowFor(slot);
            StudioGUI.Count("maps.faceMake");
            if (StudioGUI.PrimaryButton(new GUIContent(made != null ? Loc.T("Make the face shadow map again") : Loc.T("Make face shadow map"),
                    Loc.F("Saves {0}_FaceShadow.png next to the texture. The material doesn't change until you press Apply to material", slot.texture.name)), StudioGUI.InfoBlue, 26f))
                EditorApplication.delayCall += MakeFaceShadow;

            if (made != null)
            {
                string prop = FaceShadow.Property(_material);
                bool inUse = prop != null && _material.GetTexture(prop) == made;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(made, typeof(Texture2D), false);
                    StudioGUI.Count("maps.faceUse");
                    using (new EditorGUI.DisabledScope(inUse || prop == null))
                        if (GUILayout.Button(new GUIContent(inUse ? Loc.T("In use ✓") : Loc.T("Apply to material"),
                                prop == null ? Loc.T("This material's shader has no face shadow (SDF) setting this tool knows") :
                                Loc.T("Puts it in the material and switches its face shading to SDF. Ctrl+Z undoes it")), EditorStyles.miniButton, GUILayout.Width(70)))
                            EditorApplication.delayCall += UseFaceShadow;
                }
                if (prop == null)
                    GUILayout.Label(Loc.T("This material's shader has no face shadow setting this tool knows. In lilToon: Shadow › Mask type SDF, " +
                                    "map in the strength mask. In Poiyomi: Shading › Lighting type SDF."), StudioGUI.Hint);
            }
            StudioGUI.EndGroup();
        }

        void MakeFaceShadow()
        {
            var slot = CurrentSlot;
            if (slot == null || _uv == null || _comp == null) { Toast(Loc.T("Pick the face part and its texture first (1 · Pick)."), MessageType.Warning); return; }
            int w = _face.size > 0 ? _face.size : _comp.SourceWidth;
            w = Mathf.Clamp(w, 64, 8192);
            int h = Mathf.Max(1, Mathf.RoundToInt(w * _comp.Height / (float)Mathf.Max(1, _comp.Width)));
            var t0 = EditorApplication.timeSinceStartup;
            try
            {
                EditorUtility.DisplayProgressBar(Loc.T("Face shadow"), Loc.T("Measuring the face…"), 0.2f);
                if (_uv.PoseChanged()) _uv.RefreshPose();
                var res = FaceShadow.Make(_uv, new HashSet<int>(_selection), _material, w, h, _face,
                    p => EditorUtility.DisplayCancelableProgressBar(Loc.T("Face shadow"), Loc.T("Measuring the face…"), 0.2f + 0.6f * p));
                if (res.pixels == null) { Toast(Loc.T("Face shadow cancelled. Nothing was saved."), MessageType.Info); return; }
                EditorUtility.DisplayProgressBar(Loc.T("Face shadow"), Loc.T("Saving…"), 0.8f);
                _madeFaceShadow = FaceShadow.Save(slot.texture, res, out _);
                Learn.Did(LearnEvents.MapsFaceShadow);
                double secs = EditorApplication.timeSinceStartup - t0;
                string prop = FaceShadow.Property(_material);
                bool inUse = prop != null && _material.GetTexture(prop) == _madeFaceShadow;
                string msg = (res.headBone ? Loc.F("Saved ‘{0}’ ({1} px, {2:0.0} s, centred on the head bone).", _madeFaceShadow.name, w, secs)
                                           : Loc.F("Saved ‘{0}’ ({1} px, {2:0.0} s).", _madeFaceShadow.name, w, secs)) +
                             (inUse ? Loc.T(" The material already uses it.") : prop != null ? Loc.T(" The material doesn't use it yet.") : "") +
                             (res.note != null ? " " + res.note : "");
                Toast(msg, res.note != null ? MessageType.Warning : MessageType.Info, res.note != null ? 10f : 7f,
                    inUse || prop == null ? null : Loc.T("Apply to material"), inUse || prop == null ? null : (Action)UseFaceShadow);
                _lastFaceShadowNote = res.note;
                EditorGUIUtility.PingObject(_madeFaceShadow);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast(Loc.F("Couldn't make the face shadow map: {0}", e.Message), MessageType.Warning, 8f);
            }
            finally { EditorUtility.ClearProgressBar(); }
            Repaint();
        }

        string _lastFaceShadowNote;

        void UseFaceShadow()
        {
            var made = MadeFaceShadowFor(CurrentSlot);
            if (made == null || _material == null) return;
            string prop = FaceShadow.Property(_material);
            if (prop == null) { Toast(Loc.T("This material's shader has no face shadow (SDF) setting this tool knows."), MessageType.Warning); return; }
            if (MaterialWriter.IsPoiyomiLocked(_material))
            {
                Toast(Loc.T("This Poiyomi material is locked. Unlock it in the material first, then press Apply to material again."), MessageType.Warning, 6f);
                return;
            }
            bool lil = prop == "_ShadowStrengthMask";
            var current = _material.GetTexture(prop);
            string replaces = current != null && current != made && !FaceShadow.IsMade(current) ? "\n\n" + Loc.F("It replaces ‘{0}’ in that slot.", current.name) : "";
            if (!StudioDialog.Ask(Loc.T("Use face shadow map"),
                    (lil ? Loc.F("Puts ‘{0}’ in ‘{1}’ and switches its shadow mask type to SDF (Shadow settings).", made.name, _material.name)
                         : Loc.F("Puts ‘{0}’ in ‘{1}’ and switches its lighting type to SDF (Shading settings).", made.name, _material.name)) +
                    Loc.T(" Every part using this material gets face shading.") + replaces + "\n\n" + Loc.T("Ctrl+Z undoes it."), Loc.T("Apply to material"), Loc.T("Cancel")))
                return;
            MaterialWriter.EnsureEditable(new[] { _material });
            FaceShadow.Assign(_material, made);
            Toast(Loc.F("‘{0}’ now uses ‘{1}’ for face shading. Turn the Directional Light to check it; " +
                        "if the shadow is on the lit side, tick Swap left and right and make it again. Ctrl+Z undoes it.", _material.name, made.name), MessageType.Info, 8f);
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>1 · Pick › Make maps › Make face shadow map (settings: face.*); use = also press Apply to material.</summary>
        internal Texture2D AutoMakeFaceShadow(bool use, out string note)
        {
            _lastFaceShadowNote = null;
            MakeFaceShadow();
            if (use) UseFaceShadow();
            AutoSettle();
            note = _lastFaceShadowNote;
            return MadeFaceShadowFor(CurrentSlot);
        }
    }
}
