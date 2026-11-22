using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    // 2 · Colour: harmonised recolour of whole materials through the shaders' own colour settings (no new PNG).
    public partial class AvatarTextureWindow
    {
        [SerializeField] RecolourSettings _rc = new RecolourSettings();
        [SerializeField] List<RecolourEntry> _rcEntries = new List<RecolourEntry>();
        [SerializeField] Color _rcTarget = new Color(0.35f, 0.55f, 1f);
        [SerializeField] bool _rcMatchBrightness;
        [SerializeField] Vector2 _rcListScroll;
        [SerializeField] Material _rcSessionFor; // the material the current session started from
        [SerializeField] Color _rcMainTint = new Color(1f, 0.35f, 0.55f); // colour for "Tint the material (Main Color)"
        [SerializeField] bool _rcMainTintOn;         // the materials' Main Color is currently set by it
        [SerializeField] bool _rcOthersOpen;         // "Also recolour other materials" foldout
        bool _rcNeedsFinalize;

        bool RecolourChanged => _rcEntries.Count > 0 && (!_rc.IsIdentity || _rcMainTintOn);

        /// <summary>Every mesh material on this avatar (distinct, in hierarchy order).</summary>
        List<Material> AvatarMaterials()
        {
            var root = TargetResolver.AvatarRoot(_renderer);
            if (root == null) return new List<Material>();
            // Cached for up to a second (OnGUI calls this every repaint).
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

        /// <summary>Default selection: the current material plus the avatar's materials that share its main texture (e.g. outline/transparent variants).</summary>
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

        void SetRecolourIncluded(Material m, bool include)
        {
            Undo.RecordObject(this, "Recolour materials");
            int i = _rcEntries.FindIndex(e => e.material == m);
            if (include && i < 0)
            {
                Undo.RecordObject(m, "Recolour materials");
                MaterialWriter.EnsureEditable(new[] { m }); // off Poiyomi's temporary section shader, so toggles can change
                var e = Recolour.Capture(m);
                _rcEntries.Add(e);
                if (!MaterialWriter.IsPoiyomiLocked(m)) { Recolour.Apply(e, _rc, PartialMaskFor(e)); if (_rcMainTintOn) Recolour.SetMainTint(e, _rcMainTint); }
            }
            else if (!include && i >= 0)
            {
                Undo.RecordObject(m, "Recolour materials");
                Recolour.Restore(_rcEntries[i]);
                _rcEntries.RemoveAt(i);
            }
            _rcNeedsFinalize = true;
            SceneView.RepaintAll();
        }

        void ApplyRecolour()
        {
            var mats = _rcEntries.Where(e => e.material != null).Select(e => e.material).Cast<Object>().ToList();
            mats.Add(this);
            Undo.RecordObjects(mats.ToArray(), "Recolour");
            MaterialWriter.EnsureEditable(_rcEntries.Select(e => e.material)); // the Poiyomi inspector may have section-locked one again
            foreach (var e in _rcEntries)
            {
                if (e.material == null || MaterialWriter.IsPoiyomiLocked(e.material)) continue; // locked: values would be ignored
                Recolour.Apply(e, _rc, PartialMaskFor(e));
                if (e.family == ShaderFamily.LilToon && e.material.HasProperty("_BaseColor") && e.material.HasProperty("_Color"))
                    e.material.SetColor("_BaseColor", e.material.GetColor("_Color")); // cheap part of lilToon's rules, live
                if (_rcMainTintOn) Recolour.SetMainTint(e, _rcMainTint);
                EditorUtility.SetDirty(e.material);
            }
            _rcNeedsFinalize = true;
            SceneView.RepaintAll();
        }

        /// <summary>The heavier shader bookkeeping, once the user lets go of a slider (not on every drag step).</summary>
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

        /// <summary>
        /// Sets the Main Color (lilToon / Poiyomi `_Color`) of every included material to this colour: for a grey or white
        /// main texture, where a hue shift has nothing to turn. Reset puts the starting colour back.
        /// </summary>
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
            _rcMainTintOn = true;
            foreach (var e in live) { Recolour.SetMainTint(e, _rcMainTint); EditorUtility.SetDirty(e.material); }
            _rcNeedsFinalize = true;
            SceneView.RepaintAll();
            Toast(Loc.F("Main Color set on {0} material{1}. Reset puts it back.", live.Count, (live.Count == 1 ? "" : "s")), MessageType.Info, 3f);
        }

        /// <summary>One line describing the whole-material shader change that is on.</summary>
        string WholeChangeText()
        {
            var bits = new List<string>();
            if (Mathf.Abs(_rc.hue) >= 0.01f) bits.Add(Loc.F("hue {0:+0;-0;0}°", _rc.hue));
            if (Mathf.Abs(_rc.saturation) >= 0.001f) bits.Add(Loc.F("saturation {0:+0.##;-0.##;0}", _rc.saturation));
            if (Mathf.Abs(_rc.brightness) >= 0.001f) bits.Add(Loc.F("brightness {0:+0.##;-0.##;0}", _rc.brightness));
            if (_rcMainTintOn) bits.Add(Loc.F("Main Color #{0}", ColorUtility.ToHtmlStringRGB(_rcMainTint)));
            return Loc.F("Everything (shader): {0}", string.Join(", ", bits));
        }

        /// <summary>
        /// When more than one kind of colour change is on (a whole-material shader change, colour parts, Colour Match
        /// layers), lists them all at the top, each with a way to take it off, so nothing stacks up unseen.
        /// </summary>
        void DrawColourChangesSummary()
        {
            var parts = Parts.Where(p => p.enabled && !p.IsIdentity).ToList();
            var matches = _recipe != null ? _recipe.layers.OfType<ColourMatchLayer>().ToList() : new List<ColourMatchLayer>();
            bool whole = RecolourChanged;
            int kinds = (whole ? 1 : 0) + (parts.Count > 0 ? 1 : 0) + (matches.Count > 0 ? 1 : 0);
            if (kinds < 2) return;

            StudioGUI.Count("colour.changes");
            StudioGUI.BeginGroup(Loc.T("Colour changes on this texture"), Loc.F("{0} kinds", kinds));
            System.Action pending = null;
            if (whole)
                SummaryRow(WholeChangeText(), Loc.T("Reset"), Loc.T("Put the material colours back to how they were when you started"), () => ResetRecolour());
            foreach (var p in parts.Take(4))
            {
                var pp = p;
                string what = p.IsTint ? Loc.F("tint #{0}", ColorUtility.ToHtmlStringRGB(p.tintColour)) : Loc.F("hue {0:+0;-0;0}°", p.hue);
                SummaryRow(Loc.F("Part “{0}”: {1}", p.name, what), Loc.T("Remove"), Loc.F("Delete the part “{0}” (Ctrl+Z brings it back)", p.name), () => pending = () => DeletePart(pp));
            }
            if (parts.Count > 4) GUILayout.Label(Loc.F("…and {0} more parts (see One piece at a time).", parts.Count - 4), StudioGUI.Hint);
            foreach (var m in matches)
            {
                var mm = m;
                SummaryRow(Loc.F("Colour Match “{0}”", m.name), Loc.T("Show"), Loc.T("Open this layer in 4 · Paint to change or remove it"), () => pending = () =>
                {
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

        /// <summary>Switching material: keep what the user did on the old session instead of silently dropping it.</summary>
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
                var fresh = Recolour.Capture(e.material); // the shader changed: take the starting values again
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
                StudioGUI.BeginGroup(Loc.T("Recolour"));
                GUILayout.Label(Loc.F("Quick recolour works with Poiyomi and lilToon materials. ‘{0}’ uses {1}.", _material?.name, _material?.shader?.name), StudioGUI.CheckLabel);
                GUILayout.Label(Loc.T("You can still recolour its texture with layers (in 4 · Paint)."), StudioGUI.Hint);
                if (GUILayout.Button(new GUIContent(Loc.T("Match colours to another material…"), Loc.T("Adds a Colour Match layer in 4 · Paint")))) EditorApplication.delayCall += AddColourMatchLayer;
                StudioGUI.EndGroup();
                DrawQuickMaterial();
                return;
            }

            if (!ByPart) DrawUnlockBanner();
            DrawColourChangesSummary();
            if (!ByPart) DrawRecolourMaterialList(); // shader recolour only; By part changes the texture
            DrawPartChoice();
            DrawRecolourControls();
            if (ByPart)
            {
                DrawPartsFooter();
                DrawQuickMaterial();
                return;
            }
            if (Advanced)
            {
                DrawColourSources();
                DrawRecolourFollow();
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
            StudioGUI.BeginGroup(Loc.T("Match another material"));
            GUILayout.Label(Loc.T("Give this texture the colours of another part of the avatar (for example hair matched to the outfit). " +
                            "It changes the texture itself, keeping its shading and detail, and saves as a new PNG."), StudioGUI.Hint);
            StudioGUI.Count("colour.copyFrom");
            if (GUILayout.Button(new GUIContent(Loc.T("  Match colours to another material…"), StudioGUI.Icon("PreTextureRGB", "").image,
                    Loc.T("Adds a Colour Match layer in 4 · Paint")), GUILayout.Height(26)))
                EditorApplication.delayCall += AddColourMatchLayer;
            StudioGUI.EndGroup();
            DrawQuickMaterial();
        }

        void DrawRecolourMaterialList()
        {
            int included = _rcEntries.Count;
            // The current material and the ones sharing its texture are included automatically, so most people never open this.
            StudioGUI.Count("colour.otherMaterials");
            _rcOthersOpen = EditorGUILayout.Foldout(_rcOthersOpen,
                new GUIContent(Loc.F("Also recolour other materials ({0} included)", included),
                    Loc.T("This material and the ones that share its texture are included automatically. Open this to include or leave out others.")), true, StudioGUI.HeaderFoldout);
            if (_rcOthersOpen) DrawMaterialPicker();
            DrawLockNotes();
        }

        void DrawMaterialPicker()
        {
            var all = AvatarMaterials();
            int included = _rcEntries.Count;
            StudioGUI.BeginGroup(Loc.T("Materials to recolour"), Loc.F("{0} of {1}", included, all.Count));

            using (new EditorGUILayout.HorizontalScope())
            {
                var main = _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;
                if (GUILayout.Button(new GUIContent(Loc.T("Same texture"), Loc.T("This material and the avatar's materials that use the same main texture")), EditorStyles.miniButtonLeft))
                    foreach (var m in all) SetRecolourIncluded(m, Recolour.Supports(m) && (m == _material || (main != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == main)));
                if (GUILayout.Button(new GUIContent(Loc.T("All"), Loc.T("Every Poiyomi / lilToon material on this avatar")), EditorStyles.miniButtonMid))
                    foreach (var m in all) SetRecolourIncluded(m, Recolour.Supports(m));
                if (GUILayout.Button(Loc.T("Only this"), EditorStyles.miniButtonRight))
                    foreach (var m in all) SetRecolourIncluded(m, m == _material);
            }

            float h = Mathf.Min(150f, all.Count * 20f + 4f);
            _rcListScroll = EditorGUILayout.BeginScrollView(_rcListScroll, GUILayout.Height(h));
            foreach (var m in all)
            {
                bool supported = Recolour.Supports(m);
                bool on = _rcEntries.Any(e => e.material == m);
                bool locked = supported && MaterialWriter.IsPoiyomiLocked(m);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!supported))
                    {
                        bool now = EditorGUILayout.ToggleLeft(new GUIContent(m.name + (m == _material ? Loc.T("  (this)") : ""),
                            supported ? AssetDatabase.GetAssetPath(m) : Loc.T("Not a Poiyomi or lilToon material")), on, GUILayout.MinWidth(40));
                        if (now != on) SetRecolourIncluded(m, now);
                    }
                    string tag = !supported ? Loc.T("other") : (ShaderProfiles.Detect(m.shader) == ShaderFamily.Poiyomi ? "Poi" : "lil");
                    if (locked) tag = Loc.T("locked");
                    var tagContent = new GUIContent(tag, locked ? Loc.T("Locked (optimised) Poiyomi material. It ignores colour changes. It is not unlocked automatically: press Unlock below (Poiyomi locks it again when you upload).") : null);
                    GUILayout.Label(tagContent, StudioGUI.MiniRight, GUILayout.Width(StudioGUI.MiniRight.CalcSize(tagContent).x + 2f));
                }
            }
            EditorGUILayout.EndScrollView();
            StudioGUI.EndGroup();
        }

        /// <summary>Locked Poiyomi materials and ones we unlocked: shown even when the material list is folded away.</summary>
        /// <summary>One plain line plus Unlock, shown first when the material is a locked Poiyomi material (it ignores colour changes).</summary>
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

            // Poiyomi relocks at upload, but its FAQ notes that can fail alongside other optimizers: offer to relock now.
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

        /// <summary>Samples the main texture once per material; rebuilds the "after" thumbnail when the sliders move.</summary>
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
                // Default colour to match: the most prominent one that actually has colour (not black/grey).
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
            // By part: the sliders edit the current part's layer; otherwise the material-wide shader recolour (_rc).
            var part = ByPart ? CurrentPart : null;
            var cur = ByPart ? (part != null ? part.Settings : new RecolourSettings()) : _rc;
            bool oklab = part != null ? part.oklab : PreviewOklab;
            StudioGUI.BeginGroup(part != null ? Loc.F("Colour of “{0}”", part.name) : Loc.T("Colour"));
            EnsureRecolourPreview();
            var palette = ByPart ? PartPalette(part) : _rcPalette;

            if (_rcPixels != null)
            {
                // Before / after thumbnails of the real texture (the model in the Scene view shows the exact result).
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

                // Main colours as swatches: before (click to choose the colour "Shift to" matches) and after.
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

                // Hue rotation can't change black, grey or white: say so when that's most of the texture, and
                // point at where this material's colour really comes from (e.g. a coloured emission map).
                if (_rcColourful < 0.25f && !ByPart)
                {
                    var elsewhere = _rcSources.Where(s => s.group != "main" && s.colourful >= 0.1f && s.colourful > _rcColourful + 0.05f).ToList();
                    if (elsewhere.Count > 0)
                        StudioGUI.CheckRow(StudioGUI.Check.Note,
                            Loc.F("The main texture is grey (only {0:0}% has colour), so Hue barely changes it. " +
                            "This material's colour comes from {1}: " +
                            "tick them in “Where the colour comes from” below, or tint the material:", _rcColourful * 100f, string.Join(Loc.T(" and "), elsewhere.Select(s => s.label.Split('·')[0].Trim()).Distinct().Take(3))));
                    else
                        StudioGUI.CheckRow(StudioGUI.Check.Note,
                            Loc.F("This texture is mostly dark or grey (only {0:0}% has real colour). Hue only moves the coloured parts; " +
                            "Saturation and Brightness change everything. To tint dark areas, use the layers in 4 · Paint › Solid Colour or a Gradient Map.", _rcColourful * 100f));
                    DrawMainTintAction();
                }
            }

            if (!ByPart && Advanced) DrawPoiyomiColourNotes();
            if (ByPart && part == null)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Click a part of the model first (or press + New part), then colour it here."));
            using (new EditorGUI.DisabledScope(ByPart && part == null))
            {
                bool tintMode = part != null && part.IsTint;
                if (part != null)
                {
                    int pm = WrapToolbar(tintMode ? 1 : 0, new[]
                    {
                        new GUIContent(Loc.T("Rotate colours"), Loc.T("Turn the part's colours around the colour wheel. Keeps every colour's own look, but can't colour white or grey.")),
                        new GUIContent(Loc.T("Cover with one colour"), Loc.T("Give the part one colour and keep its light and shadow. Works on white and grey.")),
                    });
                    if (pm != (tintMode ? 1 : 0)) { SetPartMode(part, pm == 1 ? RecolourPartLayer.PartMode.Tint : RecolourPartLayer.PartMode.Shift); tintMode = pm == 1; if (tintMode) LearnLater(LearnEvents.RecolourCover); }
                    if (tintMode && part.autoTinted)
                        StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This part is white/grey, so it's tinted (hue shift can't colour grey)."));
                }
                if (tintMode) DrawPartTintControls(part);
                else
                {
                EditorGUI.BeginChangeCheck();
                float hue, sat, bri;
                using (new EditorGUILayout.HorizontalScope())
                {
                    StudioGUI.Count("colour.hue");
                    hue = EditorGUILayout.Slider(new GUIContent(Loc.T("Colour shift (hue)"), Loc.T("Rotate every colour around the colour wheel (degrees)")), cur.hue, -180f, 180f);
                    StudioGUI.Info("hue");
                }
                DrawHueBars(cur.hue);
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
                GUILayout.Label(Loc.T("Or shift the selected colour to"), StudioGUI.GroupTitle);
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
                    _rcTarget = EditorGUILayout.ColorField(GUIContent.none, _rcTarget, true, false, false, GUILayout.MinWidth(30));
                    using (new EditorGUI.DisabledScope(!hasSource))
                    {
                        var shiftLabel = new GUIContent(Loc.T("Shift"), Loc.T("Sets hue and saturation so the selected colour (orange outline above) becomes this colour"));
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

        /// <summary>Tint mode of a part: its colour, strength and brightness.</summary>
        void DrawPartTintControls(RecolourPartLayer part)
        {
            EditorGUI.BeginChangeCheck();
            StudioGUI.Count("colour.tintColour");
            var col = EditorGUILayout.ColorField(new GUIContent(Loc.T("Colour"), Loc.T("The colour this part is given. Its own light and shadow stay.")), part.tintColour, true, false, false);
            StudioGUI.Count("colour.tintStrength");
            float str = EditorGUILayout.Slider(new GUIContent(Loc.T("Strength"), Loc.T("0 = no tint · 1 = the full colour")), part.tintStrength, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) { SetPartTint(part, col, str); LearnLater(LearnEvents.RecolourCover); }
            EditorGUI.BeginChangeCheck();
            StudioGUI.Count("colour.brightness");
            float bri = EditorGUILayout.Slider(new GUIContent(Loc.T("Brightness"), Loc.T("−1 darker · 0 as it is · +1 lighter")), part.brightness, -1f, 1f);
            if (EditorGUI.EndChangeCheck()) SetPartValues(part, part.hue, part.saturation, bri);
        }

        /// <summary>For a grey main texture: pick a colour and set the materials' Main Color to it (a hue shift can't colour grey).</summary>
        void DrawMainTintAction()
        {
            StudioGUI.Count("colour.mainTint");
            using (new EditorGUILayout.HorizontalScope())
            {
                _rcMainTint = EditorGUILayout.ColorField(new GUIContent("", Loc.T("The colour the materials get. It multiplies the grey texture, so light and shadow stay.")),
                    _rcMainTint, false, false, false, GUILayout.Width(70));
                if (GUILayout.Button(new GUIContent(Loc.T("Tint the material (Main Color)"),
                        Loc.T("Sets the Main Color of every included material to this colour, so the grey texture shows in it. Reset puts it back, Ctrl+Z undoes.")), GUILayout.Height(20)))
                    TintMainColour(_rcMainTint);
            }
        }

        /// <summary>Photoshop-style hue bars under the slider: top = original colours, bottom = what each becomes.</summary>
        void DrawHueBars() => DrawHueBars(_rc.hue);

        void DrawHueBars(float hueShift)
        {
            var r = EditorGUILayout.GetControlRect(false, 12);
            r.xMin += EditorGUIUtility.labelWidth + 2f;
            r.xMax -= EditorGUIUtility.fieldWidth + 5f;
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

        /// <summary>
        /// Every colour texture that's actually visible on this material, how colourful it is, and whether the recolour
        /// includes it, so it's obvious which part of the look the sliders move (and which they can't).
        /// </summary>
        void DrawColourSources()
        {
            if (_rcSources.Count == 0) return;
            StudioGUI.BeginGroup(Loc.T("Where the colour comes from"));
            // Narrow panel: the Included button moves under the name instead of squeezing it.
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

        /// <summary>"Main colour · texture  (tone)" for a row of the given width. Only the texture name is shortened (with …); the slot name and the tone stay.</summary>
        static string SourceRowText(string label, string tone, float width)
        {
            int dot = label.IndexOf(" · ", System.StringComparison.Ordinal);
            if (dot < 0) return StudioGUI.Ellipsize($"{label}  ({tone})", StudioGUI.RowLabel, width);
            string head = label.Substring(0, dot + 3), name = label.Substring(dot + 3), tail = $"  ({tone})";
            float room = width - StudioGUI.RowLabel.CalcSize(new GUIContent(head + tail)).x - 4f;
            if (room >= 40f) return head + StudioGUI.Ellipsize(name, StudioGUI.RowLabel, room) + tail;
            room = width - StudioGUI.RowLabel.CalcSize(new GUIContent(head)).x - 4f; // too tight for the tone: drop it
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
            if (ByPart) return; // By part changes the texture only (tints would change everywhere)
            StudioGUI.BeginGroup(Loc.T("Also shift"));
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

        /// <summary>Colour space the current material's hue shift uses (preview and "Shift" match it).</summary>
        bool PreviewOklab
        {
            get
            {
                var e = _rcEntries.FirstOrDefault(x => x.material == _material);
                if (e != null) return e.oklab;
                return _material != null && ShaderProfiles.Detect(_material.shader) == ShaderFamily.Poiyomi;
            }
        }

        /// <summary>
        /// Things the Poiyomi docs warn about that change what a recolour does: Hue Select mode, animated hue,
        /// the old HSV colour space, colours driven by Global Themes or AudioLink.
        /// </summary>
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
                _rcEntries[i] = Recolour.Capture(m); // new starting point: OKLab
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

        /// <summary>The mask for this material in partial mode, or null (whole mode, not eligible, or it would change the look outside).</summary>
        Texture PartialMaskFor(RecolourEntry e)
        {
            if (!_rc.partial || _rcMask == null || e?.material == null) return null;
            var main = CurrentMainTexture;
            // The mask is made from this mesh's UV islands: only materials showing the same main texture share that layout.
            if (main == null || !e.material.HasProperty("_MainTex") || e.material.GetTexture("_MainTex") != main) return null;
            return Recolour.PartialBlocker(e) == null ? _rcMask : null;
        }

        /// <summary>(Re)builds the mask PNG when the selection changed. Runs on Layout so the GUI never changes mid-frame.</summary>
        void EnsurePartialMask()
        {
            if (!_rc.partial || Event.current.type != EventType.Layout || _uv == null || _selection.Count == 0) return;
            var main = CurrentMainTexture;
            string key = RecolourMask.Key(main, _selection);
            if (key == _rcMaskKey && _rcMask != null) return;
            _rcMask = RecolourMask.Write(main, _uv, _selection);
            _rcMaskKey = key;
            _rcThumbKey = null;
            ApplyRecolour();
        }

        /// <summary>
        /// Neutralises a material's whole-texture colour adjustment (Poiyomi Color Adjust values / lilToon HSVG and gradation)
        /// so partial recolour can be used on it, then takes that as the new starting point.
        /// </summary>
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

        /// <summary>The "after" thumbnail: shifted everywhere, or only inside the selected islands in partial mode.</summary>
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
