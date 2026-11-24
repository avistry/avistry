using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly int[] FaceSizes = { 512, 1024, 2048, 0 };
        const int FaceMaxSize = 8192;
        [SerializeField] Texture2D _madeFaceShadowSource;
        [SerializeField] MapTarget _madeFaceShadowTarget;

        Texture2D MadeFaceShadowFor(TextureSlot slot) =>
            _madeFaceShadowSource != null && _madeFaceShadowSource == slot?.texture && MatchesMapTarget(_madeFaceShadowTarget, slot)
                ? _madeFaceShadow : null;

        static bool FaceMapInUse(Material material, string property, Texture current, Texture2D made) =>
            made != null && current == made && (property == "_ShadowStrengthMask"
                ? Mathf.Approximately(material.GetFloat("_ShadowMaskType"), 2f) &&
                  (!material.HasProperty("_UseShadow") || material.GetFloat("_UseShadow") >= 0.5f)
                : Mathf.Approximately(material.GetFloat("_LightingMode"), 8f));

        void DrawFaceShadowGroup()
        {
            var slot = CurrentSlot;
            StudioGUI.BeginGroup(Loc.T("toon faces"), infoId: "sdf-face-shadow");
            GUILayout.Label(Loc.T("A shadow map for anime-style faces: the shadow sweeps cleanly across the face as the light turns to the side, " +
                            "without blotches from the nose or eyes. Made from this part, so edit the face part. Works with lilToon and Poiyomi."), StudioGUI.Hint);
            string prop = FaceShadow.Property(_material);
            if (prop == null) GUILayout.Label(Loc.T("You can save a PNG. This shader has no face shadow setting Avistry can apply to."), StudioGUI.Hint);
            var names = FaceSizes.Select(s => MapSizeLabel(s, FaceMaxSize)).ToArray();
            DrawMapSetting("maps.faceSize", new GUIContent(Loc.T("Size"), Loc.T("Face shadows are smooth, so 1024 px is plenty")),
                _face.size, FaceSizes, names, value => _face.size = value);
            StudioGUI.Count("maps.faceSwapSides");
            _face.swapSides = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Swap left and right"),
                Loc.T("Tick this if the shadow lands on the side the light comes from, then make the map again")), _face.swapSides);
            if (_selection.Count > 0)
                GUILayout.Label(ShaderProfiles.Detect(_material.shader) == ShaderFamily.LilToon
                    ? Loc.F("Uses the {0} selected piece{1} as the face; the rest keeps its normal shading.", _selection.Count, _selection.Count == 1 ? "" : "s")
                    : Loc.F("Uses the {0} selected piece{1} as the face.", _selection.Count, _selection.Count == 1 ? "" : "s"), StudioGUI.Hint);

            var made = MadeFaceShadowFor(slot);
            string makeTip = prop != null ? Loc.T("Saves a face shadow PNG. Use Apply to material to put it on your avatar.") : null;
            bool make = StudioGUI.PrimaryButton(new GUIContent(made != null ? Loc.T("Make the face shadow map again") : Loc.T("Make face shadow map"),
                    makeTip), StudioGUI.InfoBlue, 26f);
            StudioGUI.Count("maps.faceMake", GUILayoutUtility.GetLastRect());
            if (make) DelayMapAction(slot, () => RunFaceBake(true));

            if (made != null)
            {
                bool inUse = prop != null && FaceMapInUse(_material, prop, _material.GetTexture(prop), made);
                bool locked = !inUse && prop != null && MaterialWriter.IsPoiyomiLocked(_material);
                if (locked)
                {
                    GUILayout.Label(Loc.T("This Poiyomi material is locked. Unlock it in the material first, then press Apply to material again."), StudioGUI.Hint);
                    DrawOpenQuickMaterialInspector(_material);
                }
                DrawGeneratedMap(slot, made, prop != null && !locked, inUse, "maps.faceUse",
                    Loc.T("Puts it in the material and switches its face shading to SDF. Ctrl+Z undoes it"), () => UseFaceShadow(made));
            }
            else if (_madeFaceShadow != null && _madeFaceShadowSource == slot.texture) GUILayout.Label(PreviousMapTargetHint(), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        Texture2D MakeFaceShadow() => RunFaceBake(false);

        Texture2D RunFaceBake(bool background)
        {
            if (_pixelJob != null || _aoSteps != null) return null;
            var slot = CurrentSlot;
            if (slot == null || _uv == null || _comp == null) { Toast(Loc.T("Pick the face part and its texture first (1 · Pick)."), MessageType.Warning); return null; }
            if (slot.role != TextureRole.Color)
            {
                Toast(Loc.T("Maps are made from a colour texture. Pick the main (colour) texture above."), MessageType.Warning);
                return null;
            }
            var target = CaptureMapTarget(slot);
            var shader = target.material.shader;
            if (!CanRunMapAction(target, slot.texture, shader)) { MapTargetChanged(); return null; }
            var size = MapSize(_face.size, _comp.SourceWidth, _comp.SourceHeight, FaceMaxSize, out _);
            var t0 = EditorApplication.timeSinceStartup;
            try
            {
                if (_uv.PoseChanged()) _uv.RefreshPose();
                var preview = _comp;
                var selected = new HashSet<int>(_selection);
                var input = FaceShadow.Prepare(_uv, selected, _material, size.x, size.y, _face, background);
                FaceShadow.Result res = default;
                byte[] Calculate(System.Threading.CancellationToken token, Func<float, bool> progress)
                {
                    res = input(progress, token);
                    token.ThrowIfCancellationRequested();
                    if (res.pixels == null) return null;
                    var png = MapPixels.EncodeRgba(res.pixels, size.x, size.y);
                    res.pixels = null;
                    return png;
                }
                Texture2D Complete(byte[] png)
                {
                    if (_comp != preview || !selected.SetEquals(_selection) || !CanRunMapAction(target, slot.texture, shader))
                    { MapTargetChanged(); return null; }
                    var made = MapBaker.SaveEncodedMap(slot.texture, "FaceShadow", FaceShadow.Tag, png, size.x, size.y,
                        out _, targetKey: MapTargetKey(target));
                    if (made == null) throw new InvalidOperationException(Loc.T("Unity couldn't import the face shadow map."));
                    Undo.RecordObject(this, "Make face shadow map");
                    _madeFaceShadow = made;
                    _madeFaceShadowSource = slot.texture;
                    _madeFaceShadowTarget = target;
                    Learn.Did(LearnEvents.MapsFaceShadow);
                    double secs = EditorApplication.timeSinceStartup - t0;
                    string prop = FaceShadow.Property(target.material);
                    bool inUse = prop != null && FaceMapInUse(target.material, prop, target.material.GetTexture(prop), made);
                    string dimensions = $"{size.x}×{size.y}";
                    string msg = (res.headBone
                        ? Loc.F("Saved ‘{0}’ ({1} px, {2:0.0} s, centred on the head bone).", _madeFaceShadow.name, dimensions, secs)
                        : Loc.F("Saved ‘{0}’ ({1} px, {2:0.0} s).", _madeFaceShadow.name, dimensions, secs)) +
                                 (inUse ? Loc.T(" The material already uses it.") : prop != null ? Loc.T(" The material doesn't use it yet.") : "") +
                                 (res.note != null ? " " + res.note : "");
                    Toast(msg, res.note != null ? MessageType.Warning : MessageType.Info, res.note != null ? 10f : 7f,
                        inUse || prop == null ? null : Loc.T("Apply to material"),
                        inUse || prop == null ? null : BoundMapAction(target, slot.texture, shader, () => UseFaceShadow(made)));
                    _lastFaceShadowNote = res.note;
                    EditorGUIUtility.PingObject(made);
                    return made;
                }
                if (!background)
                {
                    var png = Calculate(default, f => EditorUtility.DisplayCancelableProgressBar(Loc.T("Face shadow"), Loc.T("Measuring the face…"), f));
                    if (png != null) return Complete(png);
                    Cancelled();
                    return null;
                }
                _pixelJob = new EditorPixelJob(Loc.T("Face shadow"), Calculate, png => Complete(png), Failed, Cancelled,
                    () => _pixelJob = null, Repaint);
            }
            catch (Exception e) { Failed(e); }
            finally { if (!background) EditorUtility.ClearProgressBar(); Repaint(); }
            void Cancelled() => Toast(Loc.T("Face shadow cancelled. Nothing was saved."), MessageType.Info);
            void Failed(Exception e)
            {
                Debug.LogException(e);
                Toast(Loc.F("Couldn't make the face shadow map: {0}", e.Message), MessageType.Warning, 8f);
            }
            return null;
        }

        string _lastFaceShadowNote;

        void UseFaceShadow(Texture2D made)
        {
            var slot = CurrentSlot;
            if (made == null || made != MadeFaceShadowFor(slot)) { MapTargetChanged(); return; }
            var material = _material;
            var shader = material.shader;
            var target = CaptureMapTarget(slot);
            string prop = FaceShadow.Property(material);
            if (prop == null) { Toast(Loc.T("This material's shader has no face shadow (SDF) setting this tool knows."), MessageType.Warning); return; }
            if (MaterialWriter.IsPoiyomiLocked(material))
            {
                Toast(Loc.T("This Poiyomi material is locked. Unlock it in the material first, then press Apply to material again."), MessageType.Warning, 6f);
                return;
            }
            bool lil = prop == "_ShadowStrengthMask";
            var current = material.GetTexture(prop);
            if (FaceMapInUse(material, prop, current, made)) return;
            string replaces = current != null && current != made && !FaceShadow.IsMade(current) ? "\n\n" + Loc.F("It replaces ‘{0}’ in that slot.", current.name) : "";
            if (!StudioDialog.Ask(Loc.T("Use face shadow map"),
                    (lil ? Loc.F("Puts ‘{0}’ in ‘{1}’ and switches its shadow mask type to SDF (Shadow settings).", made.name, material.name)
                         : Loc.F("Puts ‘{0}’ in ‘{1}’ and switches its lighting type to SDF (Shading settings).", made.name, material.name)) +
                    Loc.T(" Every part using this material gets face shading.") + replaces + "\n\n" + Loc.T("Ctrl+Z undoes it."), Loc.T("Apply to material"), Loc.T("Cancel")))
                return;
            if (!CanRunMapAction(target, slot.texture, shader) || made != MadeFaceShadowFor(CurrentSlot) || material.GetTexture(prop) != current)
            { MapTargetChanged(); return; }
            ApplyMapToMaterial(material, "Use face shadow map", () => FaceShadow.Assign(material, made));
            Toast(Loc.F("‘{0}’ now uses ‘{1}’ for face shading. Turn the Directional Light to check it; " +
                        "if the shadow is on the lit side, tick Swap left and right and make it again. Ctrl+Z undoes it.", material.name, made.name), MessageType.Info, 8f);
            SceneView.RepaintAll();
            Repaint();
        }

        internal Texture2D AutoMakeFaceShadow(bool use, out string note)
        {
            _lastFaceShadowNote = null;
            var made = MakeFaceShadow();
            if (use && made != null) UseFaceShadow(made);
            AutoSettle();
            note = _lastFaceShadowNote;
            return made;
        }
    }
}
