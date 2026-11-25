using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public partial class AvatarTextureWindow
    {
        enum RecolourMaterialScope { SameTexture, AllSupported, Picked }

        [SerializeField] RecolourSettings _rc = new RecolourSettings();
        [SerializeField] List<RecolourEntry> _rcEntries = new List<RecolourEntry>();
        [SerializeField] Color _rcTarget = new Color(0.35f, 0.55f, 1f);
        [SerializeField] bool _rcMatchBrightness;
        [SerializeField] Vector2 _rcListScroll;
        [SerializeField] Material _rcSessionFor;
        [SerializeField] Color _rcMainTint = new Color(1f, 0.35f, 0.55f);
        [SerializeField] Color _rcMainTintChoice;
        [SerializeField] bool _rcMainTintOn;
        [SerializeField] bool _rcOthersOpen;
        [SerializeField] bool _openColourSources;
        [SerializeField] bool _openRecolourFollow;
        [SerializeField] bool _openRecolourControls = true;
        [SerializeField] bool _openRecolourMatch = true;
        [SerializeField] bool _openColourChanges = true;
        bool _rcNeedsFinalize;

        bool RecolourChanged => _rcEntries.Count > 0 && (!_rc.IsIdentity || _rcMainTintOn);

        List<Material> AvatarMaterials()
        {
            var root = TargetResolver.AvatarRoot(_renderer);
            if (root == null) return new List<Material>();
            double now = EditorApplication.timeSinceStartup;
            if (_avMatsCache != null && _avMatsRoot == root && now - _avMatsTime < 1.0) return _avMatsCache;
            _avMatsRoot = root;
            _avMatsTime = now;
            return _avMatsCache = root.GetComponentsInChildren<Renderer>(true)
                .Where(IsMeshRenderer)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToList();
        }

        List<Material> _avMatsCache;
        Transform _avMatsRoot;
        double _avMatsTime;
        static GUIStyle s_arrowStyle, s_hintCentered;
        static GUIStyle ArrowStyle => s_arrowStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        static GUIStyle HintCentered => s_hintCentered ??= new GUIStyle(StudioGUI.Hint) { alignment = TextAnchor.MiddleCenter };
        Texture _rcSampleTex;
        Hash128 _rcSampleHash;

        void StartRecolourSession()
        {
            _rcEntries.Clear();
            _rcSessionFor = _material;
            if (_material == null) return;
            var main = _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;
            foreach (var m in AvatarMaterials())
            {
                bool pick = m == _material || (main != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == main);
                if (pick && Recolour.Supports(m)) { MaterialWriter.EnsureEditable(new[] { m }); _rcEntries.Add(Recolour.Capture(m)); }
            }
            _rc.hue = _rc.saturation = _rc.brightness = 0;
            _rcMainTintOn = false;
        }

        void SetRecolourIncluded(Material material, bool include)
        {
            if (include)
            {
                _avMatsCache = null;
                if (!AvatarMaterials().Contains(material)) return;
            }
            ChangeRecolourIncluded(new[] { material }, _ => include);
        }

        void SetRecolourMaterialScope(RecolourMaterialScope scope)
        {
            if (_material == null) return;
            _avMatsCache = null;
            var materials = AvatarMaterials();
            var main = _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;
            var included = new HashSet<Material>(materials.Where(m => scope switch
            {
                RecolourMaterialScope.Picked => m == _material,
                RecolourMaterialScope.SameTexture => m == _material || (main != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == main),
                _ => true,
            }));
            ChangeRecolourIncluded(materials.Concat(_rcEntries.Select(e => e.material)), included.Contains);
        }

        void ChangeRecolourIncluded(IEnumerable<Material> materials, System.Func<Material, bool> include)
        {
            if (_material == null || _rcSessionFor != _material ||
                !QuickTargetStillMatches(_renderer, _materialIndex, _material, _material.shader)) return;
            var enrolled = new HashSet<Material>(_rcEntries.Select(e => e.material));
            var changes = materials.Where(m => m != null).Distinct()
                .Select(m => (material: m, include: include(m) && Recolour.Supports(m)))
                .Where(change => change.include != enrolled.Contains(change.material)).ToArray();
            if (changes.Length == 0 && !_rcEntries.Any(e => e.material == null)) return;

            var tint = AppliedMainTint;
            const string undo = "Recolour materials";
            WithTargetUndo(undo, () =>
            {
                Undo.RecordObjects(changes.Select(change => (Object)change.material).Append(this).ToArray(), undo);
                _rcEntries.RemoveAll(e => e.material == null);
                foreach (var (material, add) in changes)
                {
                    if (add)
                    {
                        MaterialWriter.EnsureEditable(new[] { material });
                        var entry = Recolour.Capture(material);
                        _rcEntries.Add(entry);
                        if (!MaterialWriter.IsPoiyomiLocked(material))
                        {
                            Recolour.Apply(entry, _rc, PartialMaskFor(entry));
                            if (_rcMainTintOn) Recolour.SetMainTint(entry, tint);
                        }
                    }
                    else
                    {
                        int index = _rcEntries.FindIndex(e => e.material == material);
                        var entry = _rcEntries[index];
                        if (CanRestoreRecolourScopeEntry(entry)) Recolour.Restore(entry);
                        _rcEntries.RemoveAt(index);
                    }
                }
            });
            _rcNeedsFinalize = true;
            Repaint();
            SceneView.RepaintAll();
        }

        static bool CanRestoreRecolourScopeEntry(RecolourEntry entry)
        {
            var shader = entry.material.shader;
            if (shader == null || ShaderProfiles.Detect(shader) != entry.family) return false;
            bool Matches(string name, ShaderPropertyType expected)
            {
                int index = shader.FindPropertyIndex(name);
                if (index < 0) return true;
                var actual = shader.GetPropertyType(index);
                return actual == expected || (expected == ShaderPropertyType.Float && actual == ShaderPropertyType.Range);
            }
            return entry.colorNames.All(name => Matches(name, ShaderPropertyType.Color)) &&
                entry.floatNames.All(name => Matches(name, ShaderPropertyType.Float)) &&
                entry.vectorNames.All(name => Matches(name, ShaderPropertyType.Vector)) &&
                entry.textureNames.All(name => Matches(name, ShaderPropertyType.Texture));
        }

        void ApplyRecolour()
        {
            var mats = _rcEntries.Where(e => e.material != null).Select(e => e.material).Cast<Object>().ToList();
            mats.Add(this);
            Undo.RecordObjects(mats.ToArray(), "Recolour");
            MaterialWriter.EnsureEditable(_rcEntries.Select(e => e.material));
            var tint = AppliedMainTint;
            foreach (var e in _rcEntries)
            {
                if (e.material == null || MaterialWriter.IsPoiyomiLocked(e.material)) continue;
                Recolour.Apply(e, _rc, PartialMaskFor(e));
                if (e.family == ShaderFamily.LilToon && e.material.HasProperty("_BaseColor") && e.material.HasProperty("_Color"))
                    e.material.SetColor("_BaseColor", e.material.GetColor("_Color"));
                if (_rcMainTintOn) Recolour.SetMainTint(e, tint);
                EditorUtility.SetDirty(e.material);
            }
            _rcNeedsFinalize = true;
            SceneView.RepaintAll();
        }

        void FinalizeRecolourIfIdle()
        {
            if (!_rcNeedsFinalize || GUIUtility.hotControl != 0) return;
            _rcNeedsFinalize = false;
            if (Mathf.Abs(_rc.hue) >= 0.01f) Learn.Did(LearnEvents.RecolourHue);
            if (_rcMainTintOn) Learn.Did(LearnEvents.RecolourCover);
            var mats = _rcEntries.Where(e => e.material != null && !MaterialWriter.IsPoiyomiLocked(e.material)).Select(e => e.material).ToList();
            MaterialWriter.FixPoiyomiKeywords(mats);
            MaterialWriter.AfterLilToonEdit(mats);
        }

        void ResetRecolour()
        {
            var mats = _rcEntries.Where(e => e.material != null).Select(e => e.material).Cast<Object>().ToList();
            mats.Add(this);
            Undo.RecordObjects(mats.ToArray(), "Reset recolour");
            foreach (var e in _rcEntries) Recolour.Restore(e);
            _rc.hue = _rc.saturation = _rc.brightness = 0;
            _rcMainTintOn = false;
            _rcNeedsFinalize = true;
            SceneView.RepaintAll();
            Toast(Loc.T("Colours are back to how they were when you started."), MessageType.Info, 3f);
        }

        void TintMainColour(Color colour)
        {
            var live = _rcEntries.Where(e => e.material != null && e.material.HasProperty("_Color") && !MaterialWriter.IsPoiyomiLocked(e.material)).ToList();
            if (live.Count == 0)
            {
                Toast(Loc.T("None of the included materials can be tinted: locked Poiyomi materials ignore colour changes until you unlock them."), MessageType.Warning, 5f);
                return;
            }
            var objs = live.Select(e => e.material).Cast<Object>().ToList();
            objs.Add(this);
            Undo.RecordObjects(objs.ToArray(), "Tint the material");
            MaterialWriter.EnsureEditable(live.Select(e => e.material));
            _rcMainTint = new Color(colour.r, colour.g, colour.b, 1f);
            _rcMainTintChoice = _rcMainTint;
            _rcMainTintOn = true;
            foreach (var e in live) { Recolour.SetMainTint(e, _rcMainTint); EditorUtility.SetDirty(e.material); }
            _rcNeedsFinalize = true;
            SceneView.RepaintAll();
            Toast(Loc.F("Main Color set on {0} material{1}. Reset puts it back.", live.Count, (live.Count == 1 ? "" : "s")), MessageType.Info, 3f);
        }

        string WholeChangeText()
        {
            var bits = new List<string>();
            if (Mathf.Abs(_rc.hue) >= 0.01f) bits.Add(Loc.F("hue {0:+0;-0;0}°", _rc.hue));
            if (Mathf.Abs(_rc.saturation) >= 0.001f) bits.Add(Loc.F("saturation {0:+0.##;-0.##;0}", _rc.saturation));
            if (Mathf.Abs(_rc.brightness) >= 0.001f) bits.Add(Loc.F("brightness {0:+0.##;-0.##;0}", _rc.brightness));
            if (_rcMainTintOn) bits.Add(Loc.F("Main Color #{0}", ColorUtility.ToHtmlStringRGB(AppliedMainTint)));
            return Loc.F("Everything (shader): {0}", string.Join(", ", bits));
        }

        void DrawColourChangesSummary()
        {
            var parts = Parts.Where(p => p.enabled && !p.IsIdentity).ToList();
            var matches = _recipe != null ? _recipe.layers.OfType<ColourMatchLayer>().ToList() : new List<ColourMatchLayer>();
            bool whole = RecolourChanged;
            int kinds = (whole ? 1 : 0) + (parts.Count > 0 ? 1 : 0) + (matches.Count > 0 ? 1 : 0);
            if (kinds < 2) return;

            StudioGUI.Count("colour.changes");
            string count = Loc.F("{0} kinds", kinds);
            if (!StudioGUI.BeginFoldGroup(ref _openColourChanges, Loc.T("Colour changes on this texture"), count, count))
            {
                StudioGUI.EndGroup();
                return;
            }
            System.Action pending = null;
            if (whole)
                SummaryRow(WholeChangeText(), Loc.T("Reset"), Loc.T("Put the material colours back to how they were when you started"), () => ResetRecolour());
            foreach (var p in parts.Take(4))
            {
                var pp = p;
                string what = p.IsTint ? Loc.F("tint #{0}", ColorUtility.ToHtmlStringRGB(p.tintColour)) : Loc.F("hue {0:+0;-0;0}°", p.hue);
                SummaryRow(Loc.F("Part “{0}”: {1}", p.name, what), Loc.T("Remove"), Loc.F("Delete the part “{0}” (Ctrl+Z brings it back)", p.name), () => pending = () => DeletePart(pp));
            }
            if (parts.Count > 4) GUILayout.Label(Loc.F("…and {0} more parts (see Pieces).", parts.Count - 4), StudioGUI.Hint);
            foreach (var m in matches)
            {
                var mm = m;
                SummaryRow(Loc.F("Colour Match “{0}”", m.name), Loc.T("Show"), Loc.T("Open this layer in 4 · Paint to change or remove it"), () => pending = () =>
                {
                    if (!TabAvailable(Tab.Paint, out var why)) { ShowColourMatchBlocked(why); return; }
                    if (!_recipe.layers.Contains(mm)) return;
                    Undo.RecordObject(this, "Select layer");
                    _activeLayerId = mm.id;
                    SetTab(Tab.Paint);
                });
            }
            StudioGUI.EndGroup();
            if (pending != null) EditorApplication.delayCall += () => { if (this != null) { pending(); Repaint(); } };
        }

        void SummaryRow(string text, string button, string tip, System.Action onClick)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(text, text), StudioGUI.RowLabel, GUILayout.MinWidth(40));
                if (GUILayout.Button(new GUIContent(button, tip), EditorStyles.miniButton, GUILayout.Width(62))) onClick();
            }
        }

        void FinishRecolour()
        {
            FinalizeRecolourIfIdle();
            var unlocked = _rcEntries.Where(e => e.unlockedByUs && e.material != null).Select(e => e.material).ToList();
            int n = _rcEntries.Count(e => e.material != null);
            Undo.RecordObject(this, "Finish recolour");
            _rcEntries.Clear();
            _rc.hue = _rc.saturation = _rc.brightness = 0;
            _rcMainTintOn = false;
            AssetDatabase.SaveAssets();
            Toast(Loc.F("Recolour kept on {0} material{1}.", n, (n == 1 ? "" : "s")) +
                  (unlocked.Count > 0 ? " " + Loc.T("Poiyomi will lock them again when you upload.") : ""), MessageType.Info, 5f);
            StartRecolourSession();
        }

        void KeepRecolourBeforeSwitch()
        {
            FinalizeRecolourIfIdle();
            int n = _rcEntries.Count(e => e.material != null);
            bool changed = _rc.hue != 0 || _rc.saturation != 0 || _rc.brightness != 0 || _rcMainTintOn;
            if (n == 0 || !changed) return;
            AssetDatabase.SaveAssets();
            Toast(Loc.F("Recolour kept on {0} material{1}.", n, (n == 1 ? "" : "s")), MessageType.Info, 4f);
        }

        void UnlockLockedForRecolour()
        {
            var locked = _rcEntries.Where(e => e.material != null && MaterialWriter.IsPoiyomiLocked(e.material)).ToList();
            if (locked.Count == 0) return;
            if (!ConfirmUnlock(locked.Select(e => e.material).ToList(), "recolour")) return;
            EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Unlocking Poiyomi materials…"), 0.5f);
            try { MaterialWriter.UnlockPoiyomi(locked.Select(e => e.material)); }
            finally { EditorUtility.ClearProgressBar(); }
            foreach (var e in locked)
            {
                if (MaterialWriter.IsPoiyomiLocked(e.material)) continue;
                int i = _rcEntries.IndexOf(e);
                var fresh = Recolour.Capture(e.material);
                fresh.unlockedByUs = true;
                _rcEntries[i] = fresh;
            }
            ApplyRecolour();
            int still = _rcEntries.Count(e => e.material != null && MaterialWriter.IsPoiyomiLocked(e.material));
            if (still < locked.Count) Learn.Did(LearnEvents.ColourUnlocked);
            Toast(still == 0 ? Loc.T("Unlocked. They'll be locked again on upload.") : Loc.F("{0} material(s) couldn't be unlocked; unlock them in the Poiyomi inspector.", still),
                still == 0 ? MessageType.Info : MessageType.Warning);
        }

        void DrawColourTab()
        {
            if (_rcSessionFor != _material && _material != null && Recolour.Supports(_material)) { KeepRecolourBeforeSwitch(); StartRecolourSession(); }
            _rcEntries.RemoveAll(e => e.material == null);
            FinalizeRecolourIfIdle();

            if (!Recolour.Supports(_material))
            {
                if (ByPart && CanRecolourPieces)
                {
                    if (DrawRecolourButton(EditorGUILayout.GetControlRect(false, 26f), SelectGlyph.Material,
                            new GUIContent(Loc.T("Material"), Loc.T("Back to material controls")), false)) LeaveByPart();
                    DrawPartChoice(false);
                    DrawRecolourControls();
                    DrawPartsFooter();
                    return;
                }
                StudioGUI.BeginGroup(Loc.T("Recolour"));
                if (CanRecolourPieces)
                {
                    bool recolour = DrawRecolourButton(EditorGUILayout.GetControlRect(false, 26f), SelectGlyph.Piece,
                        new GUIContent(Loc.T("Pieces"), Loc.T("Recolour pieces")), false);
                    StudioGUI.Count("colour.recolourPieces", GUILayoutUtility.GetLastRect());
                    if (recolour) EnterByPart();
                }
                if (CanAddColourMatchLayer(out _))
                    DrawAddColourMatchButton();
                GUILayout.Label(Loc.T("Quick material shows the settings this shader supports."), StudioGUI.Hint);
                StudioGUI.EndGroup();
                DrawQuickMaterial();
                return;
            }

            if (!ByPart) DrawUnlockBanner();
            DrawColourChangesSummary();
            DrawPartChoice();
            if (!ByPart) DrawRecolourMaterialList();
            DrawRecolourControls();
            if (ByPart)
            {
                DrawPartsFooter();
                DrawQuickMaterial();
                return;
            }
            StudioGUI.BeginGroup(null);
            GUILayout.Label(Loc.T("Changes go straight onto the materials, like editing them in the Inspector. No new texture is made. Ctrl+Z undoes; Reset puts back the starting colours."), StudioGUI.Hint);
            GUILayout.Label(Loc.T("Note: people whose VRChat Safety settings block custom shaders see a fallback that ignores these shader colour settings. " +
                            "To change the colour for them too, recolour the texture itself with layers (in 4 · Paint) and save a PNG."), StudioGUI.Hint);
            StudioGUI.Count("colour.reset");
            StudioGUI.Count("colour.keepChanges");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!RecolourChanged))
                {
                    if (StudioGUI.DangerButton(new GUIContent(Loc.T("Reset"), Loc.T("Put back the colours from when you started")), GUI.skin.button, GUILayout.Height(26))) ResetRecolour();
                    if (GUILayout.Button(new GUIContent(Loc.T("Keep changes"), Loc.T("Keep these colours and start fresh")), GUILayout.Height(26))) FinishRecolour();
                }
            }
            StudioGUI.EndGroup();
            if (Advanced)
            {
                DrawColourSources();
                DrawRecolourFollow();
            }

            if (StudioGUI.BeginFoldGroup(ref _openRecolourMatch, Loc.T("Match another material")))
            {
                if (CanAddColourMatchLayer(out _))
                    GUILayout.Label(Loc.T("Give this texture the colours of another part of the avatar (for example hair matched to the outfit). " +
                                    "It changes the texture itself, keeping its shading and detail, and saves as a new PNG."), StudioGUI.Hint);
                DrawAddColourMatchButton();
            }
            StudioGUI.EndGroup();
            DrawQuickMaterial();
        }

        void DrawRecolourMaterialList()
        {
            int included = _rcEntries.Count;
            StudioGUI.Count("colour.otherMaterials");
            if (StudioGUI.BeginFoldGroup(ref _rcOthersOpen, Loc.F("Materials included ({0})", included),
                tooltip: Loc.T("This material and the ones that share its texture are included automatically. Open this to include or leave out others.")))
                DrawMaterialPicker();
            StudioGUI.EndGroup();
            DrawLockNotes();
        }

        void DrawMaterialPicker()
        {
            var all = AvatarMaterials();
            var supported = all.Where(Recolour.Supports).ToList();
            int included = supported.Count(m => _rcEntries.Any(entry => entry.material == m));
            GUILayout.Label(Loc.F("{0} of {1} supported materials included", included, supported.Count), StudioGUI.Hint, GUILayout.MinWidth(0));
            if (included < _rcEntries.Count)
                GUILayout.Label(Loc.T("Some included materials are unavailable. Choose a scope to update the list."), StudioGUI.Hint, GUILayout.MinWidth(0));
            using (new EditorGUI.DisabledScope(_needsRebuild))
            {
                int scope = WrapToolbar(-1, new[]
                {
                    new GUIContent(Loc.T("Same texture"), Loc.T("This material and the avatar's materials that use the same main texture")),
                    new GUIContent(Loc.T("All supported"), Loc.T("Every Poiyomi / lilToon material on this avatar")),
                    new GUIContent(Loc.T("Only this"), Loc.T("Include only the material picked in Avistry")),
                }, EditorStyles.miniButton, 20f, PanelContentWidth);
                StudioGUI.Count("colour.materialScope", GUILayoutUtility.GetLastRect());
                if (scope >= 0) SetRecolourMaterialScope((RecolourMaterialScope)scope);
            }

            float h = Mathf.Min(150f, all.Count * 20f + 4f);
            _rcListScroll = EditorGUILayout.BeginScrollView(_rcListScroll, GUILayout.Height(h));
            foreach (var m in all)
            {
                bool editable = Recolour.Supports(m);
                bool on = _rcEntries.Any(e => e.material == m);
                bool locked = editable && MaterialWriter.IsPoiyomiLocked(m);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!editable || _needsRebuild))
                    {
                        bool now = EditorGUILayout.ToggleLeft(new GUIContent(m.name + (m == _material ? Loc.T("  (this)") : ""),
                            editable ? AssetDatabase.GetAssetPath(m) : Loc.T("Not a Poiyomi or lilToon material")), on, GUILayout.MinWidth(40));
                        if (now != on) SetRecolourIncluded(m, now);
                    }
                    string tag = !editable ? Loc.T("other") : (ShaderProfiles.Detect(m.shader) == ShaderFamily.Poiyomi ? "Poi" : "lil");
                    if (locked) tag = Loc.T("locked");
                    var tagContent = new GUIContent(tag, locked ? Loc.T("Locked (optimised) Poiyomi material. It ignores colour changes. It is not unlocked automatically: press Unlock below (Poiyomi locks it again when you upload).") : null);
                    GUILayout.Label(tagContent, StudioGUI.MiniRight, GUILayout.Width(StudioGUI.MiniRight.CalcSize(tagContent).x + 2f));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawUnlockBanner()
        {
            int lockedCount = _rcEntries.Count(e => e.material != null && MaterialWriter.IsPoiyomiLocked(e.material));
            if (lockedCount == 0) return;
            StudioGUI.CheckRow(StudioGUI.Check.Problem, Msg.M9LockedMaterial());
            StudioGUI.Count("colour.unlock");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!MaterialWriter.CanUnlockPoiyomi))
                    if (GUILayout.Button(new GUIContent(Msg.BtnUnlock, Loc.T("Poiyomi locks them again automatically when you upload")))) UnlockLockedForRecolour();
                StudioGUI.Info("shader-lock");
            }
        }

        void DrawLockNotes()
        {
            if (!Advanced) return;

            int unlockedByUs = _rcEntries.Count(e => e.unlockedByUs && e.material != null && !MaterialWriter.IsPoiyomiLocked(e.material));
            if (unlockedByUs > 0)
            {
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("{0} material(s) were unlocked for recolouring. Poiyomi relocks them at upload; if another optimizer stops that, " +
                                                         "you'll get \"Unlocked Shaders were found\". Lock them again when you're done.", unlockedByUs));
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent(Loc.F("Lock {0} again", unlockedByUs), Loc.T("Lock In Optimized Shader (colour changes then need unlocking again)")), EditorStyles.miniButton)) RelockUnlocked();
                }
            }
        }

        const int RecolourThumbSize = 96;
        Material _rcSampleFor;
        Color[] _rcPixels;
        List<Color> _rcPalette = new List<Color>();
        float _rcColourful;
        List<Recolour.ColourSource> _rcSources = new List<Recolour.ColourSource>();
        Texture2D _rcThumbBefore, _rcThumbAfter, _rcHueBar;
        string _rcThumbKey;
        [SerializeField] int _rcSourceSwatch;

        void DestroyColourResources()
        {
            foreach (var t in new[] { _rcThumbBefore, _rcThumbAfter, _rcHueBar }) if (t != null) DestroyImmediate(t);
            _rcThumbBefore = _rcThumbAfter = _rcHueBar = null;
            _rcSampleFor = null;
            _rcThumbKey = null;
        }

        static Texture2D NewThumb(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        void EnsureRecolourPreview()
        {
            var mainNow = _material != null && _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;
            var hashNow = mainNow != null ? mainNow.imageContentsHash : default;
            if (_rcSampleFor != _material || _rcThumbBefore == null || _rcSampleTex != mainNow || _rcSampleHash != hashNow)
            {
                _rcSampleFor = _material;
                _rcSampleTex = mainNow;
                _rcSampleHash = hashNow;
                _rcPixels = Recolour.SampleMainTexture(_material, RecolourThumbSize);
                _rcPalette = Recolour.Palette(_rcPixels, 5, out _rcColourful);
                _rcSources = Recolour.ColourSources(_material);
                _rcSourceSwatch = Mathf.Max(0, _rcPalette.FindIndex(c => { Color.RGBToHSV(c, out _, out float s, out float v); return s > 0.15f && v > 0.12f; }));
                if (_rcThumbBefore == null) _rcThumbBefore = NewThumb(RecolourThumbSize, RecolourThumbSize);
                if (_rcPixels != null) { _rcThumbBefore.SetPixels(_rcPixels); _rcThumbBefore.Apply(false); }
                _rcThumbKey = null;
            }
            string key = ByPart ? "parts:" + PartsKey()
                : $"{_rc.hue:0.0}|{_rc.saturation:0.000}|{_rc.brightness:0.000}|{_rc.main}|{PreviewOklab}|{(_rc.partial ? _rcMaskKey : "")}";
            if (_rcPixels != null && key != _rcThumbKey)
            {
                _rcThumbKey = key;
                if (_rcThumbAfter == null) _rcThumbAfter = NewThumb(RecolourThumbSize, RecolourThumbSize);
                _rcThumbAfter.SetPixels(PreviewAfterPixels());
                _rcThumbAfter.Apply(false);
            }
            if (_rcHueBar == null)
            {
                _rcHueBar = new Texture2D(128, 1, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var bar = new Color[128];
                for (int i = 0; i < 128; i++) bar[i] = Color.HSVToRGB(i / 128f, 0.85f, 1f);
                _rcHueBar.SetPixels(bar);
                _rcHueBar.Apply(false);
            }
        }

        void DrawRecolourControls()
        {
            var part = ByPart ? CurrentPart : null;
            var cur = ByPart ? (part != null ? part.Settings : new RecolourSettings()) : _rc;
            bool oklab = part != null ? part.oklab : PreviewOklab;
            if (!StudioGUI.BeginFoldGroup(ref _openRecolourControls, part != null ? Loc.F("Colour of “{0}”", part.name) : Loc.T("Colour")))
            {
                StudioGUI.EndGroup();
                return;
            }
            EnsureRecolourPreview();
            var palette = ByPart ? PartPalette(part) : _rcPalette;

            if (_rcPixels != null)
            {
                float avail = PanelContentWidth;
                float size = Mathf.Clamp((avail - 30f) / 2f, 60f, 130f);
                var row = GUILayoutUtility.GetRect(0, size, GUILayout.ExpandWidth(true));
                float x0 = row.x + (row.width - (size * 2 + 30f)) / 2f;
                var before = new Rect(x0, row.y, size, size);
                var after = new Rect(x0 + size + 30f, row.y, size, size);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(before, StudioGUI.CanvasBg);
                    EditorGUI.DrawRect(after, StudioGUI.CanvasBg);
                    GUI.DrawTexture(before, _rcThumbBefore, ScaleMode.ScaleToFit, true);
                    GUI.DrawTexture(after, _rcThumbAfter != null ? _rcThumbAfter : _rcThumbBefore, ScaleMode.ScaleToFit, true);
                    StudioGUI.DrawBorder(before, StudioGUI.HeaderLine, 1);
                    StudioGUI.DrawBorder(after, StudioGUI.HeaderLine, 1);
                }
                GUI.Label(new Rect(before.xMax, row.y, 30f, size), "→", ArrowStyle);

                if (palette.Count > 0)
                {
                    StudioGUI.Count("colour.swatches");
                    var sw = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                    float cell = Mathf.Min(20f, (size - 4f) / palette.Count);
                    for (int i = 0; i < palette.Count; i++)
                    {
                        var b = new Rect(before.x + i * (cell + 1), sw.y + 3, cell - 1, 16);
                        var a = new Rect(after.x + i * (cell + 1), sw.y + 3, cell - 1, 16);
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(b, palette[i]);
                            EditorGUI.DrawRect(a, part != null ? part.Preview(palette[i]) : cur.main ? Recolour.Shift(palette[i], cur, oklab) : palette[i]);
                            StudioGUI.DrawBorder(b, i == _rcSourceSwatch ? StudioGUI.Accent : StudioGUI.HeaderLine, i == _rcSourceSwatch ? 2 : 1);
                            StudioGUI.DrawBorder(a, StudioGUI.HeaderLine, 1);
                        }
                        EditorGUIUtility.AddCursorRect(b, MouseCursor.Link);
                        if (GUI.Button(b, new GUIContent("", ByPart ? Loc.T("Main colour in this part. Click to use it for “Shift to this colour”.") : Loc.T("Main colour in this texture. Click to use it for “Shift to this colour”.")), GUIStyle.none)) _rcSourceSwatch = i;
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Loc.T("Before"), HintCentered);
                    GUILayout.Label(Loc.T("After (preview)"), HintCentered);
                }

                if (_rcColourful < 0.25f && !ByPart)
                {
                    var elsewhere = _rcSources.Where(s => s.group != "main" && s.colourful >= 0.1f && s.colourful > _rcColourful + 0.05f).ToList();
                    if (elsewhere.Count > 0)
                    {
                        string sources = string.Join(Loc.T(" and "),
                            elsewhere.Select(s => s.label.Split('·')[0].Trim()).Distinct().Take(3));
                        StudioGUI.CheckRow(StudioGUI.Check.Note, Advanced
                            ? Loc.F("Hue barely changes this grey texture. To shift {0}, include it in “Where the colour comes from”.",
                                sources)
                            : Loc.F("Hue barely changes this grey texture. Switch to Advanced to shift the colour from {0}.", sources));
                    }
                    else
                        StudioGUI.CheckRow(StudioGUI.Check.Note,
                            Loc.F("Only {0:0}% of this texture has colour for Hue to shift. Choose a tint below to colour the grey areas.",
                                _rcColourful * 100f));
                }
            }

            if (!ByPart && _rcEntries.Count > 0) DrawMainTintAction();
            if (!ByPart && Advanced) DrawPoiyomiColourNotes();
            if (ByPart && part == null)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Click a part of the model first (or press New part), then colour it here."));
            using (new EditorGUI.DisabledScope(ByPart && part == null))
            {
                bool tintMode = part != null && part.IsTint;
                if (part != null)
                {
                    int pm = DrawRecolourChoice(false, tintMode ? 1 : 0, PanelContentWidth, false);
                    StudioGUI.Count("colour.cover", GUILayoutUtility.GetLastRect());
                    if (pm != (tintMode ? 1 : 0))
                    {
                        tintMode = pm == 1;
                        ChangeRecolourChoice(() => SetPartMode(part, tintMode ? RecolourPartLayer.PartMode.Tint : RecolourPartLayer.PartMode.Shift));
                        if (tintMode) LearnLater(LearnEvents.RecolourCover);
                    }
                    if (tintMode && part.autoTinted)
                        StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This part is white/grey, so it's tinted (hue shift can't colour grey)."));
                }
                if (tintMode) DrawPartTintControls(part);
                else
                {
                EditorGUI.BeginChangeCheck();
                var hueLabel = new GUIContent(Loc.T("Colour shift (hue)"), Loc.T("Rotate every colour around the colour wheel (degrees)"));
                bool hueAbove = EditorStyles.label.CalcSize(hueLabel).x > EditorGUIUtility.labelWidth - 5f;
                if (hueAbove) GUILayout.Label(hueLabel, StudioGUI.Hint, GUILayout.MinWidth(0f));
                float hue, sat, bri;
                Rect hueRow;
                using (new EditorGUILayout.HorizontalScope())
                {
                    StudioGUI.Count("colour.hue");
                    hue = EditorGUILayout.Slider(hueAbove ? GUIContent.none : hueLabel, cur.hue, -180f, 180f);
                    hueRow = GUILayoutUtility.GetLastRect();
                    StudioGUI.Info("hue");
                }
                DrawHueBars(cur.hue, !hueAbove, hueRow);
                StudioGUI.Count("colour.saturation");
                sat = EditorGUILayout.Slider(new GUIContent(Loc.T("Saturation"), Loc.T("−1 grey · 0 unchanged · +1 twice as vivid")), cur.saturation, -1f, 1f);
                StudioGUI.Count("colour.brightness");
                bri = EditorGUILayout.Slider(new GUIContent(Loc.T("Brightness"), Loc.T("−1 black · 0 unchanged · +1 twice as bright")), cur.brightness, -1f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    if (hue != cur.hue) LearnLater(LearnEvents.RecolourHue);
                    if (ByPart) SetPartValues(part, hue, sat, bri);
                    else
                    {
                        Undo.RecordObject(this, "Recolour");
                        _rc.hue = hue; _rc.saturation = sat; _rc.brightness = bri;
                        ApplyRecolour();
                    }
                }

                GUILayout.Space(4);
                GUILayout.Label(Loc.T("Or shift the selected colour to"), StudioGUI.Hint, GUILayout.MinWidth(0f));
                bool hasSource = palette.Count > 0 && _rcSourceSwatch < palette.Count;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (hasSource)
                    {
                        var src = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18));
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(src, palette[_rcSourceSwatch]);
                            StudioGUI.DrawBorder(src, StudioGUI.Accent, 1);
                        }
                        GUILayout.Label("→", GUILayout.Width(14));
                    }
                    StudioGUI.Count("colour.shiftTo");
                    _rcTarget = EditorGUILayout.ColorField(GUIContent.none, _rcTarget, true, false, false, GUILayout.MinWidth(35), GUILayout.ExpandWidth(true));
                    using (new EditorGUI.DisabledScope(!hasSource))
                    {
                        var shiftLabel = new GUIContent(Loc.T("Apply"), Loc.T("Sets hue and saturation so the selected colour (orange outline above) becomes this colour"));
                        if (GUILayout.Button(shiftLabel, GUILayout.Width(Mathf.Max(40f, GUI.skin.button.CalcSize(shiftLabel).x + 2f))))
                        {
                            if (ByPart)
                            {
                                ShiftPartToColour(part, palette, _rcSourceSwatch, _rcTarget, _rcMatchBrightness);
                            }
                            else
                            {
                                Undo.RecordObject(this, "Recolour");
                                Recolour.Match(_rcPalette[_rcSourceSwatch], _rcTarget, _rc, _rcMatchBrightness, PreviewOklab);
                                ApplyRecolour();
                            }
                        }
                    }
                }
                StudioGUI.Count("colour.matchBrightness");
                _rcMatchBrightness = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Match brightness too")), _rcMatchBrightness);
                }
            }
            StudioGUI.EndGroup();
        }

        void DrawPartTintControls(RecolourPartLayer part, float sceneWidth = 0f)
        {
            EditorGUI.BeginChangeCheck();
            StudioGUI.Count("colour.tintColour");
            var col = EditorGUILayout.ColorField(new GUIContent(Loc.T("Colour"), Loc.T("The colour this part is given. Its own light and shadow stay.")), part.tintColour, true, false, false);
            StudioGUI.Count("colour.tintStrength");
            var strength = new GUIContent(Loc.T("Strength"), Loc.T("0 = no tint · 1 = the full colour"));
            float str = sceneWidth > 0f
                ? DrawSceneSlider(strength, part.tintStrength, 0f, 1f, sceneWidth, "scene.recolourTintStrength")
                : EditorGUILayout.Slider(strength, part.tintStrength, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) { SetPartTint(part, col, str); LearnLater(LearnEvents.RecolourCover); }
            EditorGUI.BeginChangeCheck();
            StudioGUI.Count("colour.brightness");
            var brightness = new GUIContent(Loc.T("Brightness"), Loc.T("−1 darker · 0 as it is · +1 lighter"));
            float bri = sceneWidth > 0f
                ? DrawSceneSlider(brightness, part.brightness, -1f, 1f, sceneWidth, "scene.recolourBrightness")
                : EditorGUILayout.Slider(brightness, part.brightness, -1f, 1f);
            if (EditorGUI.EndChangeCheck()) SetPartValues(part, part.hue, part.saturation, bri);
        }

        Color MainTintChoice => _rcMainTintChoice.a == 0f ? _rcMainTint : _rcMainTintChoice;

        Color AppliedMainTint
        {
            get
            {
                if (!_rcMainTintOn || _rcMainTintChoice.a != 0f) return _rcMainTint;
                var entry = _rcEntries.FirstOrDefault(e => e.material != null && e.material.HasProperty("_Color") &&
                    e.colorNames.Contains("_Color") && !MaterialWriter.IsPoiyomiLocked(e.material));
                if (entry == null) return _rcMainTint;
                var colour = entry.material.GetColor("_Color");
                colour.a = 1f;
                return colour;
            }
        }

        void SetMainTintChoice(Color colour)
        {
            colour.a = 1f;
            if (colour == MainTintChoice) return;
            Undo.RecordObject(this, "Choose tint colour");
            _rcMainTint = AppliedMainTint;
            _rcMainTintChoice = colour;
        }

        void DrawMainTintAction(bool scene = false)
        {
            if (!scene) StudioGUI.Count("colour.mainTint");
            using (new EditorGUILayout.HorizontalScope())
            {
                SetMainTintChoice(EditorGUILayout.ColorField(new GUIContent("", Loc.T("Choose the colour to apply to the materials.")),
                    MainTintChoice, false, false, false, GUILayout.Width(54)));
                var apply = EditorGUILayout.GetControlRect(false, 26f, GUILayout.MinWidth(0f));
                if (DrawRecolourButton(apply, SelectGlyph.Tint, new GUIContent(Loc.T("Apply material tint"),
                        Loc.T("Sets the Main Color of every included material to this colour, so the grey texture shows in it. Reset puts it back, Ctrl+Z undoes.")), scene))
                    TintMainColour(MainTintChoice);
                if (scene) StudioGUI.Track("scene.applyMaterialTint", apply);
            }
        }

        static GUIStyle _recolourActionStyle;

        static bool DrawRecolourAction(GUIContent label, float height = 22f)
        {
            _recolourActionStyle ??= new GUIStyle(GUI.skin.button) { wordWrap = true, fixedHeight = 0f };
            return GUILayout.Button(label, _recolourActionStyle, GUILayout.MinWidth(0f), GUILayout.MinHeight(height), GUILayout.ExpandWidth(true));
        }

        void DrawHueBars(float hueShift, bool labelled, Rect slider)
        {
            var r = EditorGUILayout.GetControlRect(false, 12);
            r.xMin = slider.xMin + (labelled ? EditorGUIUtility.labelWidth + 2f : 2f);
            r.xMax = slider.xMax - EditorGUIUtility.fieldWidth - 5f;
            if (Event.current.type != EventType.Repaint || _rcHueBar == null || r.width < 10) return;
            var top = new Rect(r.x, r.y, r.width, 5);
            var bottom = new Rect(r.x, r.y + 6, r.width, 5);
            GUI.DrawTextureWithTexCoords(top, _rcHueBar, new Rect(0, 0, 1, 1));
            GUI.DrawTextureWithTexCoords(bottom, _rcHueBar, new Rect(hueShift / 360f, 0, 1, 1));
        }


        bool GroupFlag(string g) => g switch
        {
            "main" => _rc.main, "shadows" => _rc.shadows, "rim" => _rc.rim, "matcap" => _rc.matcap,
            "outline" => _rc.outline, "emission" => _rc.emission, _ => _rc.other,
        };

        void SetGroupFlag(string g, bool v)
        {
            switch (g)
            {
                case "main": _rc.main = v; break;
                case "shadows": _rc.shadows = v; break;
                case "rim": _rc.rim = v; break;
                case "matcap": _rc.matcap = v; break;
                case "outline": _rc.outline = v; break;
                case "emission": _rc.emission = v; break;
                default: _rc.other = v; break;
            }
        }

        void DrawColourSources()
        {
            if (!_openRecolourControls) EnsureRecolourPreview();
            if (_rcSources.Count == 0) return;
            if (!StudioGUI.BeginFoldGroup(ref _openColourSources, Loc.T("Where the colour comes from")))
            {
                StudioGUI.EndGroup();
                return;
            }
            bool stack = _panelContentWidth < 300f;
            foreach (var src in _rcSources.OrderByDescending(x => x.colourful))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var sw = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(18));
                        sw.y += 1; sw.height = 16;
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(sw, src.swatch);
                            StudioGUI.DrawBorder(sw, StudioGUI.HeaderLine, 1);
                        }
                        string tone = src.colourful < 0.1f ? Loc.T("grey") : src.colourful < 0.35f ? Loc.T("a little colour") : Loc.T("colourful");
                        var nameR = GUILayoutUtility.GetRect(40f, 18f, GUILayout.MinWidth(40f), GUILayout.ExpandWidth(true));
                        GUI.Label(nameR, new GUIContent(SourceRowText(src.label, tone, nameR.width), Loc.F("{0}  ({1:0}% of this texture has real colour)", src.label, src.colourful * 100f)), StudioGUI.RowLabel);
                        if (!stack) DrawSourceToggle(src, false);
                    }
                    if (stack)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(20f);
                            DrawSourceToggle(src, true);
                        }
                }
            }
            StudioGUI.EndGroup();
        }

        static string SourceRowText(string label, string tone, float width)
        {
            int dot = label.IndexOf(" · ", System.StringComparison.Ordinal);
            if (dot < 0) return StudioGUI.Ellipsize($"{label}  ({tone})", StudioGUI.RowLabel, width);
            string head = label.Substring(0, dot + 3), name = label.Substring(dot + 3), tail = $"  ({tone})";
            float room = width - StudioGUI.RowLabel.CalcSize(new GUIContent(head + tail)).x - 4f;
            if (room >= 40f) return head + StudioGUI.Ellipsize(name, StudioGUI.RowLabel, room) + tail;
            room = width - StudioGUI.RowLabel.CalcSize(new GUIContent(head)).x - 4f;
            return head + StudioGUI.Ellipsize(name, StudioGUI.RowLabel, Mathf.Max(room, 16f));
        }

        void DrawSourceToggle(Recolour.ColourSource src, bool stretch)
        {
            var opt = stretch ? GUILayout.ExpandWidth(true) : GUILayout.Width(84);
            if (src.canShift)
            {
                bool on = GroupFlag(src.group);
                bool now = GUILayout.Toggle(on, new GUIContent(on ? Loc.T("Included") : Loc.T("Not included"), on ? Loc.T("The recolour changes this") : Loc.T("Click to let the recolour change this too")),
                    EditorStyles.miniButton, opt);
                if (now != on)
                {
                    Undo.RecordObject(this, "Recolour");
                    SetGroupFlag(src.group, now);
                    ApplyRecolour();
                }
            }
            else
                GUILayout.Label(new GUIContent(Loc.T("use Layers"), Loc.T("This shader can't shift this texture's colour without baking. Recolour it in the layers in 4 · Paint instead.")),
                    stretch ? StudioGUI.Mini : StudioGUI.MiniRight, opt);
        }

        void DrawRecolourFollow()
        {
            if (ByPart) return;
            if (!StudioGUI.BeginFoldGroup(ref _openRecolourFollow, Loc.T("Also shift")))
            {
                StudioGUI.EndGroup();
                return;
            }
            EditorGUI.BeginChangeCheck();
            bool main = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Main texture"), Loc.T("Through the shader's colour adjustment (no new PNG)")), _rc.main);
            bool shadows = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Shadow colours")), _rc.shadows);
            bool rim = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Rim light")), _rc.rim);
            bool matcap = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Matcap tints")), _rc.matcap);
            bool outline = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Outline")), _rc.outline);
            bool emission = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Emission (glow)"), Loc.T("Off by default: glow colours are often meant to stay as they are")), _rc.emission);
            bool other = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Backlight, reflection, glitter")), _rc.other);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Recolour");
                _rc.main = main; _rc.shadows = shadows; _rc.rim = rim; _rc.matcap = matcap; _rc.outline = outline; _rc.emission = emission; _rc.other = other;
                ApplyRecolour();
            }

            var shadowNote = Recolour.ShadowNote(_material);
            if (shadowNote != null) StudioGUI.CheckRow(StudioGUI.Check.Note, shadowNote);
            GUILayout.Label(Loc.T("Tints (shadow, rim… colours) follow fully. Feature textures (emission, matcap…) follow in hue only."), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        bool PreviewOklab
        {
            get
            {
                var e = _rcEntries.FirstOrDefault(x => x.material == _material);
                if (e != null) return e.oklab;
                return _material != null && ShaderProfiles.Detect(_material.shader) == ShaderFamily.Poiyomi;
            }
        }

        void DrawPoiyomiColourNotes()
        {
            var m = _material;
            if (m == null || ShaderProfiles.Detect(m.shader) != ShaderFamily.Poiyomi) return;
            var e = _rcEntries.FirstOrDefault(x => x.material == m);
            float Base(string p, float fallback = 0) => e != null ? e.BaseFloat(p, fallback) : (m.HasProperty(p) ? m.GetFloat(p) : fallback);
            bool hueOn = Base("_MainColorAdjustToggle") > 0.5f && Base("_MainHueShiftToggle") > 0.5f;

            if (hueOn && m.HasProperty("_MainHueShiftSelectOrShift") && Base("_MainHueShiftSelectOrShift", 1) < 0.5f)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This material uses Hue Select (every colour set to one hue), so Hue moves that single hue instead of rotating the colours."));
            if (m.HasProperty("_MainHueShiftSpeed") && Mathf.Abs(m.GetFloat("_MainHueShiftSpeed")) > 1e-4f)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("The hue is animated (Hue Shift Speed isn't 0), so in VRChat the colours keep cycling from this starting point."));

            if (hueOn && Base("_MainHueShiftColorSpace") > 0.5f)
            {
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This material uses the older HSV colour space for hue shifting. Poiyomi recommends OKLab: colours stay evenly bright as the hue turns."));
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent(Loc.T("Switch to OKLab"), Loc.T("Sets Color Space to OKLab for the main hue shift and the feature hue shifts that are on (Ctrl+Z undoes)")), EditorStyles.miniButton))
                        SwitchToOklab(m);
                }
            }

            var themes = Recolour.LinkedThemes(m);
            if (themes.Count > 0)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("Some colours are linked to Global Theme Color {0}; those use the theme's colour, so the theme colour is shifted instead.", string.Join(", ", themes)));
            int al = Recolour.AudioLinkDrivenCount(m);
            if (al > 0)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("{0} colour(s) are driven by AudioLink themes and change with music; a recolour can't move them.", al));
        }

        void SwitchToOklab(Material m)
        {
            Undo.RecordObjects(new UnityEngine.Object[] { m, this }, "Switch to OKLab");
            int i = _rcEntries.FindIndex(x => x.material == m);
            if (i >= 0) Recolour.Restore(_rcEntries[i]);
            foreach (var p in new[] { "_MainHueShiftColorSpace", "_EmissionHueShiftColorSpace", "_EmissionHueShiftColorSpace1", "_EmissionHueShiftColorSpace2", "_EmissionHueShiftColorSpace3",
                                      "_MatcapHueShiftColorSpace", "_Matcap2HueShiftColorSpace", "_Matcap3HueShiftColorSpace", "_Matcap4HueShiftColorSpace",
                                      "_RimHueShiftColorSpace", "_Rim2HueShiftColorSpace", "_OutlineHueShiftColorSpace", "_GlitterHueShiftColorSpace",
                                      "_DecalHueShiftColorSpace", "_DecalHueShiftColorSpace1", "_DecalHueShiftColorSpace2", "_DecalHueShiftColorSpace3" })
                if (m.HasProperty(p)) m.SetFloat(p, 0);
            EditorUtility.SetDirty(m);
            if (i >= 0)
            {
                bool unlocked = _rcEntries[i].unlockedByUs;
                _rcEntries[i] = Recolour.Capture(m);
                _rcEntries[i].unlockedByUs = unlocked;
                Recolour.Apply(_rcEntries[i], _rc, PartialMaskFor(_rcEntries[i]));
            }
            _rcThumbKey = null;
            SceneView.RepaintAll();
            Toast(Loc.T("Hue shift now uses OKLab, Poiyomi's recommended colour space."), MessageType.Info, 3f);
        }

        void RelockUnlocked()
        {
            var list = _rcEntries.Where(x => x.unlockedByUs && x.material != null && !MaterialWriter.IsPoiyomiLocked(x.material)).ToList();
            if (list.Count == 0) return;
            FinalizeRecolourIfIdle();
            EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Locking Poiyomi materials…"), 0.5f);
            try { MaterialWriter.LockPoiyomi(list.Select(x => x.material)); }
            finally { EditorUtility.ClearProgressBar(); }
            int locked = list.Count(x => MaterialWriter.IsPoiyomiLocked(x.material));
            foreach (var x in list) if (MaterialWriter.IsPoiyomiLocked(x.material)) x.unlockedByUs = false;
            Toast(locked == list.Count ? Loc.F("Locked {0} material(s) again.", locked) : Loc.F("Locked {0} of {1}; lock the rest in the Poiyomi inspector.", locked, list.Count),
                locked == list.Count ? MessageType.Info : MessageType.Warning);
        }

        [SerializeField] Texture2D _rcMask;
        [SerializeField] string _rcMaskKey;
        float[] _rcThumbMask;
        string _rcThumbMaskKey;

        Texture CurrentMainTexture => _material != null && _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;

        Texture PartialMaskFor(RecolourEntry e)
        {
            if (!_rc.partial || _rcMask == null || e?.material == null) return null;
            var main = CurrentMainTexture;
            if (main == null || !e.material.HasProperty("_MainTex") || e.material.GetTexture("_MainTex") != main) return null;
            return Recolour.PartialBlocker(e) == null ? _rcMask : null;
        }

        void EnsurePartialMask()
        {
            if (!_rc.partial || Event.current.type != EventType.Layout || _uv == null) return;
            var pieces = _highlightedColourPart?.islands ?? _selection;
            if (pieces.Count == 0) return;
            var main = CurrentMainTexture;
            string key = RecolourMask.Key(main, pieces);
            if (key == _rcMaskKey && _rcMask != null) return;
            _rcMask = RecolourMask.Write(main, _uv, pieces);
            _rcMaskKey = key;
            _rcThumbKey = null;
            ApplyRecolour();
        }

        void ClearExistingAdjustment(RecolourEntry e)
        {
            var m = e.material;
            if (m == null) return;
            Undo.RecordObjects(new UnityEngine.Object[] { m, this }, "Clear colour adjustment");
            Recolour.Restore(e);
            if (e.family == ShaderFamily.Poiyomi)
            {
                foreach (var p in new[] { "_MainHueShift", "_Saturation", "_MainBrightness" }) if (m.HasProperty(p)) m.SetFloat(p, 0);
                if (m.HasProperty("_MainGamma")) m.SetFloat("_MainGamma", 1);
                if (m.HasProperty("_MainHueShiftToggle")) m.SetFloat("_MainHueShiftToggle", 0);
            }
            else if (e.family == ShaderFamily.LilToon)
            {
                if (m.HasProperty("_MainTexHSVG")) m.SetVector("_MainTexHSVG", new Vector4(0, 1, 1, 1));
                if (m.HasProperty("_MainGradationStrength")) m.SetFloat("_MainGradationStrength", 0);
            }
            EditorUtility.SetDirty(m);
            int i = _rcEntries.IndexOf(e);
            if (i >= 0)
            {
                bool unlocked = e.unlockedByUs;
                _rcEntries[i] = Recolour.Capture(m);
                _rcEntries[i].unlockedByUs = unlocked;
            }
            ApplyRecolour();
            Toast(Loc.F("Cleared {0}'s whole-texture colour adjustment. Ctrl+Z brings it back.", m.name), MessageType.Info, 4f);
        }

        Color[] PreviewAfterPixels()
        {
            if (ByPart) return PartsAfterPixels();
            var shifted = Recolour.ShiftPixels(_rcPixels, _rc, PreviewOklab);
            if (!_rc.partial) return shifted;
            if (_uv == null || _selection.Count == 0 || shifted == null) return _rcPixels;
            string key = RecolourMask.Key(CurrentMainTexture, _selection);
            if (_rcThumbMask == null || _rcThumbMaskKey != key)
            {
                var bytes = _uv.RasterIslandMask(RecolourThumbSize, RecolourThumbSize, new HashSet<int>(_selection), 1);
                _rcThumbMask = bytes.Select(b => b / 255f).ToArray();
                _rcThumbMaskKey = key;
            }
            var o = new Color[shifted.Length];
            for (int i = 0; i < o.Length; i++) o[i] = Color.Lerp(_rcPixels[i], shifted[i], _rcThumbMask[i]);
            return o;
        }
    }
}
