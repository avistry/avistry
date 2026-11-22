using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Make maps (1 · Pick): bake ambient occlusion from the model into a Multiply layer, and make a normal map from
    /// the texture's light and dark details. See MapBaker for the maths.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _foldMaps;
        [SerializeField] MapBaker.AoSettings _ao = new MapBaker.AoSettings();
        [SerializeField] MapBaker.NormalSettings _nrm = new MapBaker.NormalSettings();
        [SerializeField] Texture2D _madeNormal;
        [SerializeField] FaceShadow.Settings _face = new FaceShadow.Settings();
        [SerializeField] Texture2D _madeFaceShadow;

        static readonly int[] AoSampleCounts = { 64, 128, 256 };
        static string[] AoSampleNames() => new[] { Loc.T("Fast (64 directions)"), Loc.T("Good (128 directions)"), Loc.T("Best (256 directions)") };
        static readonly int[] AoSizes = { 0, 1024, 2048, 4096 };

        string[] _aoSizeNames;
        int _aoSizeNamesW = -1;
        string _aoSizeNamesLang;
        bool _nrmCacheValid;
        Material _nrmCacheMat;
        Texture _nrmCacheTex;
        Texture _nrmCacheBase;
        bool _nrmCacheIsOurs;

        static bool IsAoLayer(Layer l) => l is ImageLayer il && il.generated == MapBaker.AoLayerMark;

        void DrawMapsGroup()
        {
            var slot = CurrentSlot;
            if (slot == null || _comp == null || _uv == null) return;
            StudioGUI.Count("maps.foldout");
            _foldMaps = EditorGUILayout.Foldout(_foldMaps, new GUIContent(Loc.T("Make maps (shading, normal map, face shadow)"),
                Loc.T("Bake soft shading in creases from the model, make a normal map from the texture's details, or make a toon face shadow map")), true);
            if (!_foldMaps) return;

            if (slot.role != TextureRole.Color)
            {
                StudioGUI.BeginGroup(Loc.T("Make maps"));
                GUILayout.Label(Loc.T("Maps are made from a colour texture. Pick the main (colour) texture above."), StudioGUI.Hint);
                StudioGUI.EndGroup();
                return;
            }

            StudioGUI.BeginGroup(Loc.T("Ambient occlusion"), Loc.T("shading in creases"), infoId: "ambient-occlusion");
            GUILayout.Label(Loc.T("Soft shading where parts are close: creases, under the hair, between fingers. It's added as a " +
                            "Multiply layer you can fade with Layer opacity or a mask."), StudioGUI.Hint);
            int qi = Mathf.Max(0, Array.IndexOf(AoSampleCounts, _ao.samples));
            StudioGUI.Count("maps.aoQuality");
            int nqi = EditorGUILayout.Popup(new GUIContent(Loc.T("Quality"), Loc.T("How many directions the shading is measured from. More = smoother, a little slower")), qi, AoSampleNames());
            if (nqi != qi || Array.IndexOf(AoSampleCounts, _ao.samples) < 0) _ao.samples = AoSampleCounts[nqi];
            StudioGUI.Count("maps.aoSpread");
            _ao.spread = EditorGUILayout.Slider(new GUIContent(Loc.T("Spread"), Loc.T("How far the shading reaches. Low = only tight creases, high = broad soft shading")), _ao.spread, 0f, 1f);
            StudioGUI.Count("maps.aoStrength");
            _ao.strength = EditorGUILayout.Slider(new GUIContent(Loc.T("Strength"), Loc.T("How dark the shading gets")), _ao.strength, 0f, 2f);
            StudioGUI.Count("maps.aoOtherMeshes");
            _ao.otherMeshes = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Clothing and hair cast shading too"),
                Loc.T("The avatar's other parts also shade this one (a collar on the neck, hair on the forehead). Leave it off if those parts " +
                "can be toggled off in game, or their shading stays behind")), _ao.otherMeshes);
            int srcW = _comp.SourceWidth;
            if (_aoSizeNames == null || _aoSizeNamesW != srcW || _aoSizeNamesLang != Loc.Current)
            {
                _aoSizeNames = AoSizes.Select(s => s == 0 ? Loc.F("Same as the texture ({0} px)", srcW) : $"{s} px").ToArray();
                _aoSizeNamesW = srcW;
                _aoSizeNamesLang = Loc.Current;
            }
            var sizeNames = _aoSizeNames;
            int si = Mathf.Max(0, Array.IndexOf(AoSizes, _ao.size));
            StudioGUI.Count("maps.aoSize");
            _ao.size = AoSizes[EditorGUILayout.Popup(new GUIContent(Loc.T("Size"), Loc.T("Size of the baked shading image")), si, sizeNames)];
            GUILayout.Space(2);
            var existingAo = _recipe.layers.FirstOrDefault(IsAoLayer);
            StudioGUI.Count("maps.aoBake");
            if (StudioGUI.PrimaryButton(new GUIContent(existingAo != null ? Loc.T("Bake ambient occlusion again") : Loc.T("Bake ambient occlusion"),
                    Loc.F("Bakes from the model in its current pose and saves {0}_AO.png next to the texture", slot.texture.name) +
                    (existingAo != null ? Loc.F(". Updates the “{0}” layer", existingAo.name) : Loc.T(". Adds a Multiply layer"))), StudioGUI.InfoBlue, 26f))
                EditorApplication.delayCall += BakeAmbientOcclusion;
            StudioGUI.EndGroup();

            StudioGUI.BeginGroup(Loc.T("Normal map (bump image)"), Loc.T("bumps from the texture"), infoId: "normal-map");
            GUILayout.Label(Loc.F("Fake bumps made from the light and dark details (stitches, fabric, scales). Saved as " +
                                  "{0}_normal.png next to the texture.", slot.texture.name), StudioGUI.Hint);
            StudioGUI.Count("maps.nrmWithLayers");
            _nrm.withLayers = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Include my layers"), Loc.T("On: use the texture as you see it, with your layers (not the ambient occlusion layer). Off: the original texture")), _nrm.withLayers);
            StudioGUI.Count("maps.nrmStrength");
            _nrm.strength = EditorGUILayout.Slider(new GUIContent(Loc.T("Strength"), Loc.T("How deep the bumps look")), _nrm.strength, 0f, 5f);
            StudioGUI.Count("maps.nrmSmoothness");
            _nrm.smoothness = EditorGUILayout.Slider(new GUIContent(Loc.T("Smoothness"), Loc.T("Low keeps fine grain and noise, high keeps only broad shapes")), _nrm.smoothness, 0f, 1f);
            StudioGUI.Count("maps.nrmDarkRaised");
            _nrm.darkRaised = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Dark parts stick out"), Loc.T("Off: light parts stick out. On: dark parts do (for raised dark seams or outlines)")), _nrm.darkRaised);

            string prop = MapBaker.NormalProperty(_material);
            var current = prop != null ? _material.GetTexture(prop) : null;
            // Importer lookups are cached per (material, its normal texture); MakeNormalMap clears the cache (it rewrites the importer's userData).
            if (!_nrmCacheValid || _nrmCacheMat != _material || _nrmCacheTex != current)
            {
                _nrmCacheBase = NormalToCombineWith(current, out _nrmCacheIsOurs);
                _nrmCacheMat = _material;
                _nrmCacheTex = current;
                _nrmCacheValid = true;
            }
            var baseNormal = _nrmCacheBase;
            bool currentIsOurs = _nrmCacheIsOurs;
            if (baseNormal != null)
            {
                StudioGUI.Count("maps.nrmCombine");
                _nrm.combine = EditorGUILayout.ToggleLeft(new GUIContent(Loc.F("Combine with ‘{0}’", baseNormal.name),
                    Loc.T("Keep the bumps of the normal map the material already has and add these on top. Off: the new map replaces it")), _nrm.combine);
            }
            else if (current != null && !currentIsOurs)
                GUILayout.Label(Loc.F("The material's normal map ‘{0}’ can't be read, so the new one would replace it.", current.name), StudioGUI.Hint);

            StudioGUI.Count("maps.nrmMake");
            if (StudioGUI.PrimaryButton(new GUIContent(_madeNormalFor(slot) != null ? Loc.T("Make the normal map again") : Loc.T("Make normal map"),
                    Loc.F("Saves {0}_normal.png (set up as a normal map). The material doesn't change until you press Apply to material", slot.texture.name)), StudioGUI.InfoBlue, 26f))
                EditorApplication.delayCall += MakeNormalMap;

            var made = _madeNormalFor(slot);
            if (made != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(made, typeof(Texture2D), false);
                    bool inUse = current == made;
                    using (new EditorGUI.DisabledScope(inUse || prop == null))
                    {
                        StudioGUI.Count("maps.nrmUse");
                        if (GUILayout.Button(new GUIContent(inUse ? Loc.T("In use ✓") : Loc.T("Apply to material"),
                                prop == null ? Loc.T("This material's shader has no normal map slot") :
                                Loc.T("Puts it in the material's normal map slot and switches normal mapping on. Ctrl+Z undoes it")),
                                EditorStyles.miniButton, GUILayout.Width(70)))
                            EditorApplication.delayCall += UseMadeNormalMap;
                    }
                }
                if (prop == null) GUILayout.Label(Loc.T("This material's shader has no normal map slot."), StudioGUI.Hint);
                else if (MaterialWriter.IsPoiyomiLocked(_material) && current == null)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This Poiyomi material is locked. A new normal map may not show until you unlock it (in the material, or 2 · Recolour)."));
            }
            StudioGUI.EndGroup();

            DrawFaceShadowGroup();
        }

        /// <summary>The last normal map made, if it belongs to the texture being edited.</summary>
        Texture2D _madeNormalFor(TextureSlot slot) =>
            _madeNormal != null && slot?.texture != null && _madeNormal.name.StartsWith(slot.texture.name + "_normal") ? _madeNormal : null;

        /// <summary>
        /// Which normal map a new one would be combined with: the material's own, or (when that one was made by this tool)
        /// the map it was combined with back then, so making it again doesn't stack the old bumps.
        /// </summary>
        static Texture NormalToCombineWith(Texture current, out bool currentIsOurs)
        {
            currentIsOurs = MapBaker.IsMadeNormal(current, out var earlierBase);
            if (currentIsOurs) return earlierBase;
            if (current == null) return null;
            return current is Texture2D ? current : null;
        }

        void BakeAmbientOcclusion()
        {
            var slot = CurrentSlot;
            if (slot == null || _uv == null || _comp == null) { Toast(Loc.T("Pick a part and its texture first (1 · Pick)."), MessageType.Warning); return; }
            if (slot.role != TextureRole.Color) { Toast(Loc.T("Ambient occlusion is made for colour textures. Pick the main texture in 1 · Pick."), MessageType.Warning); return; }
            int w = _ao.size > 0 ? _ao.size : _comp.SourceWidth;
            const int AoMaxSize = 4096; // float render targets at 8192 px can run out of memory
            bool aoCapped = w > AoMaxSize;
            w = Mathf.Clamp(w, 64, AoMaxSize);
            int h = Mathf.Max(1, Mathf.RoundToInt(w * _comp.Height / (float)Mathf.Max(1, _comp.Width)));

            MapBaker.AoResult res;
            try { res = MapBaker.BakeAO(_uv, slot.texture, w, h, _ao); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast(Msg.M19SoftShadow(false, e.Message), MessageType.Warning, 8f, Msg.BtnTryAgain, BakeAmbientOcclusion);
                return;
            }
            if (res.texture == null) { Toast(Msg.M19SoftShadow(true, null), MessageType.Info, 3f, Msg.BtnTryAgain, BakeAmbientOcclusion); return; }
            _lastAo = res;
            Learn.Did(LearnEvents.MapsAo);

            var layer = _recipe.layers.OfType<ImageLayer>().FirstOrDefault(l => IsAoLayer(l) && l.image == res.texture);
            string casters = res.casters > 1 ? Loc.F(", {0} parts casting shading", res.casters) : "";
            if (aoCapped) casters += Loc.F(", capped at {0} px as the texture is larger", AoMaxSize);
            if (layer != null)
            {
                Undo.RecordObject(this, "Bake ambient occlusion");
                _activeLayerId = layer.id;
                MarkEdited();
                Toast(Loc.F("Updated “{0}” ({1} px, {2:0.0} s{3}).", layer.name, res.width, res.seconds, casters), MessageType.Info, 5f);
            }
            else
            {
                layer = new ImageLayer
                {
                    name = "Ambient occlusion",
                    image = res.texture,
                    position = new Vector2(0.5f, 0.5f),
                    size = Vector2.one,
                    blend = BlendMode.Multiply,
                    generated = MapBaker.AoLayerMark,
                };
                // Shading belongs on top of the colours and paint below it.
                if (_recipe.layers.Count > 0) _activeLayerId = _recipe.layers[_recipe.layers.Count - 1].id;
                InsertLayer(layer, "Add ambient occlusion");
                Toast(Loc.F("Added an “Ambient occlusion” layer (Multiply, {0} px, {1:0.0} s{2}). " +
                      "Lower its Layer opacity for lighter shading, or give it a mask. Ctrl+Z removes it.", res.width, res.seconds, casters), MessageType.Info, 7f);
            }
            Repaint();
        }

        MapBaker.AoResult _lastAo;

        /// <summary>Note and Bake again button for an ambient occlusion layer (in its settings).</summary>
        void DrawAoLayerNote()
        {
            GUILayout.Label(Loc.T("Baked from the model. Lower Layer opacity for lighter shading. Spread and Strength are in " +
                            "1 · Pick › Make maps."), StudioGUI.Hint);
            StudioGUI.Count("maps.aoBakeAgain");
            if (GUILayout.Button(new GUIContent(Loc.T("Bake again"), Loc.T("Bakes it again with the settings in 1 · Pick › Make maps, in the current pose")), EditorStyles.miniButton))
                EditorApplication.delayCall += BakeAmbientOcclusion;
        }

        void MakeNormalMap()
        {
            var slot = CurrentSlot;
            if (slot == null || _uv == null || _comp == null) { Toast(Loc.T("Pick a part and its texture first (1 · Pick)."), MessageType.Warning); return; }
            if (slot.role != TextureRole.Color) { Toast(Loc.T("A normal map is made from a colour texture. Pick the main texture in 1 · Pick."), MessageType.Warning); return; }
            string prop = MapBaker.NormalProperty(_material);
            var current = prop != null ? _material.GetTexture(prop) : null;
            var baseNormal = _nrm.combine ? NormalToCombineWith(current, out _) : null;

            var t0 = EditorApplication.timeSinceStartup;
            try
            {
                EditorUtility.DisplayProgressBar(Loc.T("Normal map"), Loc.T("Reading the texture…"), 0.1f);
                Color32[] px;
                int w, h;
                using (var comp = new Compositor(slot, 0)) // full resolution
                {
                    comp.Compose(_nrm.withLayers ? _recipe.layers.Where(l => !IsAoLayer(l)).ToList() : new List<Layer>(), _uv);
                    var tex = comp.ReadResult(false);
                    px = tex.GetPixels32();
                    w = tex.width; h = tex.height;
                    DestroyImmediate(tex);
                }
                EditorUtility.DisplayProgressBar(Loc.T("Normal map"), Loc.T("Finding the pieces…"), 0.3f);
                var ids = _uv.RasterIslandIds(w, h, 0); // not grown: the padding's colours mustn't make bumps at island edges
                Color32[] existing = null;
                if (baseNormal != null)
                {
                    EditorUtility.DisplayProgressBar(Loc.T("Normal map"), Loc.F("Reading ‘{0}’…", baseNormal.name), 0.4f);
                    existing = MapBaker.ReadNormalMap(baseNormal, w, h);
                }
                EditorUtility.DisplayProgressBar(Loc.T("Normal map"), Loc.T("Making the bumps…"), 0.5f);
                var normals = MapBaker.NormalsFromColour(px, w, h, ids, _nrm, existing,
                    f => EditorUtility.DisplayCancelableProgressBar(Loc.T("Normal map"), Loc.T("Making the bumps…"), 0.5f + 0.35f * f));
                if (normals == null) { Toast(Loc.T("Normal map cancelled. Nothing was changed."), MessageType.Info, 3f); return; }
                EditorUtility.DisplayProgressBar(Loc.T("Normal map"), Loc.T("Saving…"), 0.85f);
                _madeNormal = MapBaker.SaveNormalMap(slot.texture, normals, w, h, baseNormal, out string path);
                Learn.Did(LearnEvents.MapsNormal);
                double secs = EditorApplication.timeSinceStartup - t0;
                bool inUse = current != null && current == _madeNormal;
                Toast(inUse
                        ? Loc.F("Updated ‘{0}’, which the material already uses ({1:0.0} s).", _madeNormal.name, secs)
                        : Loc.F("Saved ‘{0}’ ({1} px, {2:0.0} s){3}. The material doesn't use it yet.", _madeNormal.name, w, secs, (baseNormal != null ? $", combined with ‘{baseNormal.name}’" : "")),
                    MessageType.Info, 7f, inUse || prop == null ? null : Loc.T("Apply to material"), inUse || prop == null ? null : (Action)UseMadeNormalMap);
                EditorGUIUtility.PingObject(_madeNormal);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast(Loc.F("Couldn't make the normal map: {0}", e.Message), MessageType.Warning, 8f);
            }
            finally { EditorUtility.ClearProgressBar(); _nrmCacheValid = false; }
            Repaint();
        }

        void UseMadeNormalMap()
        {
            var made = _madeNormalFor(CurrentSlot);
            if (made == null || _material == null) return;
            string prop = MapBaker.NormalProperty(_material);
            if (prop == null) { Toast(Loc.T("This material's shader has no normal map slot."), MessageType.Warning); return; }
            var current = _material.GetTexture(prop);
            MapBaker.IsMadeNormal(made, out var madeFrom);
            bool replaces = current != null && current != made && !MapBaker.IsMadeNormal(current, out _) && madeFrom != current;
            if (replaces && !StudioDialog.Ask(Loc.T("Use normal map"),
                    Loc.F("‘{0}’ already has the normal map ‘{1}’. Replace it with ‘{2}’?\n\n" +
                          "To keep both, tick Combine and make the normal map again. Ctrl+Z undoes the change.", _material.name, current.name, made.name), Loc.T("Replace"), Loc.T("Cancel")))
                return;
            MaterialWriter.EnsureEditable(new[] { _material });
            MapBaker.AssignNormalMap(_material, made);
            Toast(Loc.F("‘{0}’ now uses ‘{1}’ as its normal map. Ctrl+Z undoes it.", _material.name, made.name), MessageType.Info, 5f);
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
