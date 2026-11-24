using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        enum MapTool { Occlusion, Normal, FaceShadow }

        [SerializeField] bool _foldMaps;
        [SerializeField] MapTool _mapTool;
        [SerializeField] MapBaker.AoSettings _ao = new MapBaker.AoSettings();
        [SerializeField] MapBaker.NormalSettings _nrm = new MapBaker.NormalSettings();
        [SerializeField] Texture2D _madeNormal;
        [SerializeField] Texture2D _madeNormalSource;
        [SerializeField] MapTarget _madeNormalTarget;
        [SerializeField] FaceShadow.Settings _face = new FaceShadow.Settings();
        [SerializeField] Texture2D _madeFaceShadow;

        static readonly int[] AoSampleCounts = { 64, 128, 256 };
        static string[] AoSampleNames() => new[] { Loc.T("Fast (64 directions)"), Loc.T("Good (128 directions)"), Loc.T("Best (256 directions)") };
        static readonly int[] AoSizes = { 0, 1024, 2048, 4096 };
        const int AoMaxSize = 4096;

        string[] _aoSizeNames;
        Vector2Int _aoSizeSource;
        string _aoSizeNamesLang;
        bool _nrmCacheValid;
        Material _nrmCacheMat;
        Texture _nrmCacheTex;
        Texture _nrmCacheBase;
        bool _nrmCacheIsOurs;

        static bool IsAoLayer(Layer l) => l is ImageLayer il && il.generated == MapBaker.AoLayerMark;

        [Serializable]
        struct MapTarget
        {
            public Renderer part;
            public Material material;
            public int materialIndex, uv;
            public string property;
            public ShaderFamily family;
        }

        MapTarget CaptureMapTarget(TextureSlot slot) => new MapTarget
        {
            part = _renderer,
            material = _material,
            materialIndex = _materialIndex,
            uv = slot.uvChannel,
            property = slot.properties.FirstOrDefault(),
            family = slot.family,
        };

        static string MapTargetKey(MapTarget target)
        {
            var part = MapObjectKey(target.part);
            var material = MapObjectKey(target.material);
            string scope = FormattableString.Invariant($"{part}/{material}/{target.materialIndex}/{target.property}/{target.uv}/{(int)target.family}");
            return Hash128.Compute(scope).ToString();
        }

        static string MapObjectKey(UnityEngine.Object obj)
        {
            bool partInUnsavedScene = obj is Renderer part && !EditorUtility.IsPersistent(part) && string.IsNullOrEmpty(part.gameObject.scene.path);
            if (!partInUnsavedScene)
            {
                var id = GlobalObjectId.GetGlobalObjectIdSlow(obj);
                if (id.targetObjectId != 0 && !id.assetGUID.Empty()) return id.ToString();
            }

            const string SessionKey = "Avistry.MapTargetSession";
            string session = SessionState.GetString(SessionKey, "");
            if (session.Length == 0) SessionState.SetString(SessionKey, session = Guid.NewGuid().ToString("N"));
            return FormattableString.Invariant($"{session}:{obj.GetInstanceID()}");
        }

        bool MatchesMapTarget(MapTarget target, TextureSlot slot) =>
            !_needsRebuild && _comp != null && _uv != null && slot?.role == TextureRole.Color &&
            target.part != null && target.part == _renderer && target.material != null && target.material == _material &&
            target.materialIndex == _materialIndex && target.uv == slot.uvChannel && target.family == slot.family &&
            target.property == slot.properties.FirstOrDefault();

        bool CanRunMapAction(MapTarget target, Texture source, Shader shader)
        {
            var slot = CurrentSlot;
            if (!MatchesMapTarget(target, slot) || source == null || slot.texture != source) return false;
            var materials = _renderer.sharedMaterials;
            bool current = _material.shader == shader && ShaderProfiles.Detect(shader) == target.family &&
                           TargetResolver.ReadUvChannel(_material, target.property) == target.uv &&
                           _materialIndex >= 0 && _materialIndex < materials.Length && materials[_materialIndex] == _material &&
                           slot.properties.All(property => _material.HasProperty(property) && _material.GetTexture(property) == source);
            if (!current) _needsRebuild = true;
            return current;
        }

        void MapTargetChanged() => Toast(Loc.T("The map target changed. Open Make maps for the current part and try again."), MessageType.Info, 5f);

        Action OnMapTarget(TextureSlot slot, Action action) => BoundMapAction(CaptureMapTarget(slot), slot.texture, _material.shader, action);

        Action BoundMapAction(MapTarget target, Texture source, Shader shader, Action action) => () =>
            {
                if (this == null) return;
                if (CanRunMapAction(target, source, shader)) action();
                else MapTargetChanged();
            };

        void DelayMapAction(TextureSlot slot, Action action) => EditorApplication.delayCall += OnMapTarget(slot, action).Invoke;

        static string PreviousMapTargetHint() => Loc.T("Make a map for this part to enable Apply to material.");

        void DrawMapsGroup()
        {
            var slot = CurrentSlot;
            if (slot == null || _comp == null || _uv == null) return;
            bool open = StudioGUI.Section(ref _foldMaps, Loc.T("Make maps"),
                tooltip: Loc.T("Bake soft shading in creases from the model, make a normal map from the texture's details, or make a toon face shadow map"));
            StudioGUI.Count("maps.foldout", GUILayoutUtility.GetLastRect());
            if (!open) return;

            if (_aoSteps != null || _pixelJob != null)
            {
                bool ao = _aoSteps != null;
                EditorGUILayout.LabelField(ao ? Loc.T("Ambient occlusion") : _pixelJob.Title,
                    (ao ? Mathf.RoundToInt(_aoSteps.Current * 100) : _pixelJob.Percent) + "%");
                if (GUILayout.Button(Msg.BtnCancel, EditorStyles.miniButton))
                {
                    if (ao) _aoCancel.Cancel(); else _pixelJob.Cancel();
                }
                return;
            }

            if (slot.role != TextureRole.Color)
            {
                DrawMapSourceRecovery(slot);
                return;
            }

            _mapTool = (MapTool)WrapToolbar((int)_mapTool, new[]
            {
                new GUIContent(Loc.T("Ambient occlusion"), Loc.T("Bake shading from the model into a Multiply layer")),
                new GUIContent(Loc.T("Normal map"), Loc.T("Make a PNG for bumps and fine surface detail")),
                new GUIContent(Loc.T("Face shadow"), Loc.T("Make a PNG for toon face shading")),
            }, EditorStyles.miniButton);
            StudioGUI.Count("maps.type", GUILayoutUtility.GetLastRect());
            switch (_mapTool)
            {
                case MapTool.Occlusion: DrawOcclusionMap(slot); break;
                case MapTool.Normal: DrawNormalMap(slot); break;
                case MapTool.FaceShadow: DrawFaceShadowGroup(); break;
            }
        }

        (int sourceIndex, bool hasChoice) MapColourSources()
        {
            var main = MaterialMainTexture;
            int sourceIndex = _slots.FindIndex(s => s.role == TextureRole.Color && s.texture == main);
            int colourCount = _slots.Count(s => s.role == TextureRole.Color);
            if (sourceIndex < 0 && colourCount == 1)
                sourceIndex = _slots.FindIndex(s => s.role == TextureRole.Color);
            return (sourceIndex, colourCount > 0);
        }

        void DrawMapSourceRecovery(TextureSlot slot)
        {
            StudioGUI.BeginGroup(null);
            var (sourceIndex, hasChoice) = MapColourSources();
            if (sourceIndex >= 0)
            {
                GUILayout.Label(Loc.T("Maps are made from a colour texture."), StudioGUI.Hint);
                GUILayout.Label(Loc.F("Colour source: {0}", _slots[sourceIndex].texture.name), StudioGUI.Hint);
                using (new EditorGUI.DisabledScope(_needsRebuild))
                {
                    bool open = GUILayout.Button(Loc.T("Use colour source"), GUILayout.Height(24f));
                    StudioGUI.Count("maps.useColourTexture", GUILayoutUtility.GetLastRect());
                    if (open) UseMapColourSource(slot, sourceIndex);
                }
            }
            else if (hasChoice)
            {
                GUILayout.Label(Loc.T("Maps are made from a colour texture."), StudioGUI.Hint);
                bool pick = GUILayout.Button(Loc.T("Choose a colour texture"), GUILayout.Height(24f));
                StudioGUI.Count("maps.chooseColourTexture", GUILayoutUtility.GetLastRect());
                if (pick) RevealMapControl("pick.image");
            }
            else GUILayout.Label(Loc.T("This material has no colour texture. Choose another material or part."), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        void UseMapColourSource(TextureSlot slot, int sourceIndex)
        {
            var part = _renderer;
            var material = _material;
            var shader = material.shader;
            var source = _slots[sourceIndex];
            int materialIndex = _materialIndex;
            int revision = _historyRevision;
            if (!PlanTargetChange("texture", out var apply)) return;
            if (this == null || part == null || material == null || _renderer != part || _material != material ||
                _materialIndex != materialIndex || _historyRevision != revision || CurrentSlot != slot || _needsRebuild || _tab != Tab.Target)
                return;
            var live = part.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= live.Length || live[materialIndex] != material || material.shader != shader ||
                !SavedEditSlotStillAssigned(material, slot) || !SavedEditSlotStillAssigned(material, source))
            {
                _needsRebuild = true;
                MapTargetChanged();
                return;
            }
            WithTargetUndo("Change texture", () =>
            {
                Undo.RecordObject(this, "Change texture");
                apply();
                ChooseSlot(sourceIndex);
            });
            Learn.Did(LearnEvents.TexturePicked);
            RevealMapControl("maps.type");
        }

        void RevealMapControl(string id)
        {
            _revealLearnControl = id;
            LearnPulse.Start(id);
            Repaint();
        }

        string RevealMapTool(string control)
        {
            if (control.StartsWith("maps.ao", StringComparison.Ordinal)) _mapTool = MapTool.Occlusion;
            else if (control.StartsWith("maps.nrm", StringComparison.Ordinal)) _mapTool = MapTool.Normal;
            else if (control.StartsWith("maps.face", StringComparison.Ordinal)) _mapTool = MapTool.FaceShadow;
            else if (control != "maps.foldout") return control;
            _foldMaps = true;
            if (CurrentSlot == null || CurrentSlot.role == TextureRole.Color || control == "maps.foldout") return control;
            var (sourceIndex, hasChoice) = MapColourSources();
            return sourceIndex >= 0 ? "maps.useColourTexture" : hasChoice ? "maps.chooseColourTexture" : "maps.foldout";
        }

        void DrawMapSetting(string id, GUIContent label, int current, int[] values, string[] names, Action<int> apply)
        {
            int index = Array.IndexOf(values, current);
            int next = PickChoice(label, index, names.Select(name => new GUIContent(name)).ToArray());
            StudioGUI.Count(id, GUILayoutUtility.GetLastRect());
            if (next == index) return;
            if (index < 0 && next == 0) { apply(values[next]); return; }

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, Loc.T("Change map settings"));
            apply(values[next]);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        void DrawOcclusionMap(TextureSlot slot)
        {
            StudioGUI.BeginGroup(Loc.T("shading in creases"), infoId: "ambient-occlusion");
            GUILayout.Label(Loc.T("Soft shading where parts are close: creases, under the hair, between fingers. It's added as a " +
                            "Multiply layer you can fade with Layer opacity or a mask."), StudioGUI.Hint);
            DrawMapSetting("maps.aoQuality", new GUIContent(Loc.T("Quality"), Loc.T("How many directions the shading is measured from. More = smoother, a little slower")),
                _ao.samples, AoSampleCounts, AoSampleNames(), value => _ao.samples = value);
            StudioGUI.Count("maps.aoSpread");
            _ao.spread = EditorGUILayout.Slider(new GUIContent(Loc.T("Spread"), Loc.T("How far the shading reaches. Low = only tight creases, high = broad soft shading")), _ao.spread, 0f, 1f);
            StudioGUI.Count("maps.aoStrength");
            _ao.strength = EditorGUILayout.Slider(new GUIContent(Loc.T("Strength"), Loc.T("How dark the shading gets")), _ao.strength, 0f, 2f);
            StudioGUI.Count("maps.aoOtherMeshes");
            _ao.otherMeshes = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Clothing and hair cast shading too"),
                Loc.T("The avatar's other parts also shade this one (a collar on the neck, hair on the forehead). Leave it off if those parts " +
                "can be toggled off in game, or their shading stays behind")), _ao.otherMeshes);
            var sourceSize = new Vector2Int(_comp.SourceWidth, _comp.SourceHeight);
            if (_aoSizeNames == null || _aoSizeSource != sourceSize || _aoSizeNamesLang != Loc.Current)
            {
                _aoSizeNames = AoSizes.Select(s => MapSizeLabel(s, AoMaxSize)).ToArray();
                _aoSizeSource = sourceSize;
                _aoSizeNamesLang = Loc.Current;
            }
            DrawMapSetting("maps.aoSize", new GUIContent(Loc.T("Size"), Loc.T("Size of the baked shading image")),
                _ao.size, AoSizes, _aoSizeNames, value => _ao.size = value);
            GUILayout.Space(2);
            var existingAo = _recipe.layers.FirstOrDefault(IsAoLayer);
            bool bake = StudioGUI.PrimaryButton(new GUIContent(existingAo != null ? Loc.T("Bake ambient occlusion again") : Loc.T("Bake ambient occlusion"),
                    Loc.T("Makes a shading PNG from the model in its current pose") +
                    (existingAo != null ? Loc.F(". Updates the “{0}” layer", existingAo.name) : Loc.T(". Adds a Multiply layer"))), StudioGUI.InfoBlue, 26f);
            StudioGUI.Count("maps.aoBake", GUILayoutUtility.GetLastRect());
            if (bake) DelayMapAction(slot, BeginOcclusionBake);
            StudioGUI.EndGroup();
        }

        void DrawNormalMap(TextureSlot slot)
        {
            StudioGUI.BeginGroup(Loc.T("bumps from the texture"), infoId: "normal-map");
            GUILayout.Label(Loc.T("Make a normal map from light and dark details such as stitches, fabric or scales."), StudioGUI.Hint);
            string prop = MapBaker.NormalProperty(_material);
            if (prop == null) GUILayout.Label(Loc.T("You can save a PNG. This shader has no normal map slot Avistry can apply to."), StudioGUI.Hint);
            StudioGUI.Count("maps.nrmWithLayers");
            _nrm.withLayers = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Include my layers"),
                Loc.T("On: use your edited texture without ambient occlusion. Off: use the original texture.")), _nrm.withLayers);
            StudioGUI.Count("maps.nrmStrength");
            _nrm.strength = EditorGUILayout.Slider(new GUIContent(Loc.T("Strength"), Loc.T("How deep the bumps look")), _nrm.strength, 0f, 5f);
            StudioGUI.Count("maps.nrmSmoothness");
            _nrm.smoothness = EditorGUILayout.Slider(new GUIContent(Loc.T("Smoothness"), Loc.T("Low keeps fine grain and noise, high keeps only broad shapes")), _nrm.smoothness, 0f, 1f);
            StudioGUI.Count("maps.nrmDarkRaised");
            _nrm.darkRaised = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Dark parts stick out"), Loc.T("Off: light parts stick out. On: dark parts do (for raised dark seams or outlines)")), _nrm.darkRaised);

            var current = prop != null ? _material.GetTexture(prop) : null;
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

            var made = _madeNormalFor(slot);
            string makeTip = prop != null ? Loc.T("Saves a normal map PNG. Use Apply to material to put it on your avatar.") : null;
            bool make = StudioGUI.PrimaryButton(new GUIContent(made != null ? Loc.T("Make the normal map again") : Loc.T("Make normal map"),
                    makeTip), StudioGUI.InfoBlue, 26f);
            StudioGUI.Count("maps.nrmMake", GUILayoutUtility.GetLastRect());
            if (make) DelayMapAction(slot, () => RunNormalBake(true));

            if (made != null)
            {
                bool inUse = NormalMapInUse(_material, current, made, slot.uvChannel);
                DrawGeneratedMap(slot, made, prop != null, inUse, "maps.nrmUse",
                    ReenablesStandardNormal(_material, current, made)
                        ? Loc.T("Enable the assigned normal map. Unity may keep it enabled after Undo.")
                        : Loc.T("Puts it in the material's normal map slot and switches normal mapping on. Ctrl+Z undoes it"), () => UseMadeNormalMap(made));
                if (prop != null && MaterialWriter.IsPoiyomiLocked(_material) && current == null)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This Poiyomi material is locked. A new normal map may not show until you unlock it (in the material, or 2 · Recolour)."));
            }
            else if (_madeNormal != null && _madeNormalSource == slot.texture) GUILayout.Label(PreviousMapTargetHint(), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        void DrawGeneratedMap(TextureSlot slot, Texture2D made, bool canApply, bool inUse, string applyId, string tooltip, Action apply)
        {
            GUILayout.Space(3f);
            GUILayout.Label(Loc.F("Saved PNG: {0}", made.name), StudioGUI.Hint);
            if (inUse) StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.T("In use ✓"));
            else if (canApply)
            {
                bool use = GUILayout.Button(new GUIContent(Loc.T("Apply to material"), tooltip), GUILayout.Height(24f));
                StudioGUI.Count(applyId, GUILayoutUtility.GetLastRect());
                if (use) DelayMapAction(slot, apply);
            }
            string path = AssetDatabase.GetAssetPath(made);
            bool locate = GUILayout.Button(new GUIContent(Loc.T("Show PNG in Project"), path), EditorStyles.miniButton);
            StudioGUI.Count("maps.locatePng", GUILayoutUtility.GetLastRect());
            if (locate)
            {
                EditorUtility.FocusProjectWindow();
                ShowFile(path);
            }
        }

        Texture2D _madeNormalFor(TextureSlot slot) =>
            _madeNormalSource != null && _madeNormalSource == slot?.texture && MatchesMapTarget(_madeNormalTarget, slot) ? _madeNormal : null;

        static bool ReenablesStandardNormal(Material material, Texture current, Texture2D made) =>
            current == made && MapBaker.UsesStandardNormalKeyword(material) && !material.IsKeywordEnabled(MapBaker.StandardNormalKeyword);

        static bool NormalMapInUse(Material material, Texture current, Texture2D made, int uvChannel) =>
            made != null && current == made && (!material.HasProperty("_UseBumpMap") || material.GetFloat("_UseBumpMap") >= 0.5f) &&
            (!MapBaker.UsesStandardNormalKeyword(material) || material.IsKeywordEnabled(MapBaker.StandardNormalKeyword)) &&
            (!material.HasProperty("_BumpScale") || material.GetFloat("_BumpScale") > 0f) &&
            (!material.HasProperty("_BumpMapUV") || TargetResolver.ReadUvChannel(material, "_BumpMap") == uvChannel);

        static Texture NormalToCombineWith(Texture current, out bool currentIsOurs)
        {
            currentIsOurs = MapBaker.IsMadeNormal(current, out var earlierBase);
            if (currentIsOurs) return earlierBase;
            if (current == null) return null;
            return current is Texture2D ? current : null;
        }

        static Vector2Int MapSize(int requestedWidth, int sourceWidth, int sourceHeight, int maxSize, out bool capped)
        {
            float width = Mathf.Max(64, requestedWidth > 0 ? requestedWidth : sourceWidth);
            float height = width * sourceHeight / sourceWidth;
            float longest = Mathf.Max(width, height);
            capped = longest > maxSize;
            float scale = capped ? maxSize / longest : 1f;
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * scale)), Mathf.Max(1, Mathf.RoundToInt(height * scale)));
        }

        string MapSizeLabel(int requestedWidth, int maxSize)
        {
            var size = MapSize(requestedWidth, _comp.SourceWidth, _comp.SourceHeight, maxSize, out _);
            string dimensions = $"{size.x}×{size.y}";
            return requestedWidth == 0 && size.x == _comp.SourceWidth && size.y == _comp.SourceHeight
                ? Loc.F("Source ({0} px)", dimensions) : $"{dimensions} px";
        }

        void BakeAmbientOcclusion()
        {
            var slot = CurrentSlot;
            if (slot == null || _uv == null || _comp == null) { Toast(Loc.T("Pick a part and its texture first (1 · Pick)."), MessageType.Warning); return; }
            if (slot.role != TextureRole.Color) { Toast(Loc.T("Ambient occlusion is made for colour textures. Pick the main texture in 1 · Pick."), MessageType.Warning); return; }
            var size = MapSize(_ao.size, _comp.SourceWidth, _comp.SourceHeight, AoMaxSize, out bool aoCapped);
            string dimensions = $"{size.x}×{size.y}";

            MapBaker.AoResult res;
            try { res = MapBaker.BakeAO(_uv, slot.texture, size.x, size.y, _ao); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast(Msg.M19SoftShadow(false, e.Message), MessageType.Warning, 8f, Msg.BtnTryAgain, OnMapTarget(slot, BeginOcclusionBake));
                return;
            }
            if (res.texture == null)
            {
                Toast(Msg.M19SoftShadow(true, null), MessageType.Info, 3f, Msg.BtnTryAgain, OnMapTarget(slot, BeginOcclusionBake));
                return;
            }
            ApplyOcclusionResult(res, dimensions, aoCapped);
        }

        void ApplyOcclusionResult(MapBaker.AoResult res, string dimensions, bool aoCapped)
        {
            _lastAo = res;
            Learn.Did(LearnEvents.MapsAo);

            var aoLayers = _recipe.layers.OfType<ImageLayer>().Where(IsAoLayer);
            var layer = aoLayers.FirstOrDefault(l => l.image == res.texture) ?? aoLayers.FirstOrDefault();
            string casters = res.casters > 1 ? Loc.F(", {0} parts casting shading", res.casters) : "";
            if (aoCapped) casters += Loc.F(", capped at {0} px as the texture is larger", AoMaxSize);
            if (layer != null)
            {
                Undo.RecordObject(this, "Bake ambient occlusion");
                if (layer.image != res.texture)
                {
                    Undo.RecordObject(_recipe, "Bake ambient occlusion");
                    layer.image = res.texture;
                }
                _activeLayerId = layer.id;
                MarkEdited();
                Toast(Loc.F("Updated “{0}” ({1} px, {2:0.0} s{3}).", layer.name, dimensions, res.seconds, casters), MessageType.Info, 5f);
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
                if (_recipe.layers.Count > 0) _activeLayerId = _recipe.layers[_recipe.layers.Count - 1].id;
                InsertLayer(layer, "Add ambient occlusion");
                Toast(Loc.F("Added an “Ambient occlusion” layer (Multiply, {0} px, {1:0.0} s{2}). " +
                      "Lower its Layer opacity for lighter shading, or give it a mask. Ctrl+Z removes it.",
                      dimensions, res.seconds, casters), MessageType.Info, 7f);
            }
            Repaint();
        }

        MapBaker.AoResult _lastAo;

        void DrawAoLayerNote()
        {
            GUILayout.Label(Loc.T("Baked from the model. Lower Layer opacity for lighter shading. Spread and Strength are in " +
                            "1 · Pick › Make maps."), StudioGUI.Hint);
            StudioGUI.Count("maps.aoBakeAgain");
            if (GUILayout.Button(new GUIContent(Loc.T("Bake again"), Loc.T("Bakes it again with the settings in 1 · Pick › Make maps, in the current pose")), EditorStyles.miniButton) && CurrentSlot is TextureSlot slot)
                DelayMapAction(slot, BeginOcclusionBake);
        }

        void UseMadeNormalMap(Texture2D made)
        {
            var slot = CurrentSlot;
            if (made == null || made != _madeNormalFor(slot)) { MapTargetChanged(); return; }
            var material = _material;
            var shader = material.shader;
            var target = CaptureMapTarget(slot);
            string prop = MapBaker.NormalProperty(material);
            if (prop == null) { Toast(Loc.T("This material's shader has no normal map slot."), MessageType.Warning); return; }
            var current = material.GetTexture(prop);
            if (NormalMapInUse(material, current, made, slot.uvChannel)) return;
            MapBaker.IsMadeNormal(made, out var madeFrom);
            bool replaces = current != null && current != made && !MapBaker.IsMadeNormal(current, out _) && madeFrom != current;
            if (replaces && !StudioDialog.Ask(Loc.T("Use normal map"),
                    Loc.F("‘{0}’ already has the normal map ‘{1}’. Replace it with ‘{2}’?\n\n" +
                          "To keep both, tick Combine and make the normal map again. Ctrl+Z undoes the change.", material.name, current.name, made.name), Loc.T("Replace"), Loc.T("Cancel")))
                return;
            if (!CanRunMapAction(target, slot.texture, shader) || made != _madeNormalFor(CurrentSlot) || material.GetTexture(prop) != current)
            { MapTargetChanged(); return; }
            bool reenables = ReenablesStandardNormal(material, current, made);
            ApplyMapToMaterial(material, "Use normal map", () =>
            {
                if (material.HasProperty(prop + "UV")) material.SetFloat(prop + "UV", slot.uvChannel);
                MapBaker.AssignNormalMap(material, made);
            });
            Toast(reenables ? Loc.T("Normal mapping is back on. Unity may keep it enabled after Undo.")
                : Loc.F("‘{0}’ now uses ‘{1}’ as its normal map. Ctrl+Z undoes it.", material.name, made.name), MessageType.Info, 5f);
            SceneView.RepaintAll();
            Repaint();
        }

        static void ApplyMapToMaterial(Material material, string undoName, Action assign)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObject(material, undoName);
            MaterialWriter.EnsureEditable(new[] { material });
            assign();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }
    }
}
