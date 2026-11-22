using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Avistry.Textures
{
    /// <summary>
    /// Avistry window. Split over several files:
    ///   AvatarTextureWindow.cs        state, lifecycle, compositing, painting and layer operations
    ///   AvatarTextureWindow.Canvas.cs toolbar, texture canvas, status bar, canvas input
    ///   AvatarTextureWindow.Panel.cs  side panel sections
    ///   AvatarTextureWindow.Scene.cs  Scene view highlight, picking and painting
    /// </summary>
    [MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public partial class AvatarTextureWindow : EditorWindow
    {
        enum Tool { Select = 0, Paint = 1 }

        const float MinPanelWidth = 260f;  // the narrowest panel; rows wrap or shorten to fit down to here
        const float MinCanvasWidth = 280f;
        static readonly int[] WorkSizes = { 512, 1024, 2048, 4096 };

        [SerializeField] Renderer _renderer;
        [SerializeField] string _rendererId; // GlobalObjectId: finds the mesh again after Unity restarts or the scene reopens
        [SerializeField] int _materialIndex;
        [SerializeField] int _slotIndex;
        [SerializeField] TextureRecipe _recipe;
        [SerializeField] List<int> _selection = new List<int>();
        [SerializeField] string _activeLayerId;
        [SerializeField] Tool _tool = Tool.Select;
        [SerializeField] BrushSettings _brush = new BrushSettings();
        [SerializeField] List<Color> _recentColors = new List<Color>();
        [SerializeField] int _workSize = 2048;
        [SerializeField] bool _previewOnAvatar = true;
        [SerializeField] bool _showWire = true;
        [SerializeField] bool _showSelection = true;
        [SerializeField] bool _alphaBlend = true;
        [SerializeField] bool _showOriginal;
        [SerializeField] bool _pickInScene;
        [SerializeField] bool _highlightInScene = true;
        [SerializeField] bool _paintInScene = true;
        [SerializeField] OutputMode _outputMode = OutputMode.DuplicateMaterial;
        [SerializeField] bool _dither = true;
        [SerializeField] bool _foldHelp; // "Advanced" output options in 5 · Save
        [SerializeField] bool _foldBrushAdvanced;
        [SerializeField] bool _focusWhilePainting = true;
        [SerializeField] float _panelWidth = 300f;
        [SerializeField] float _canvasHeightFraction = 0.45f;
        [SerializeField] string _newBrushName = "";

        Material _material;
        List<TextureSlot> _slots = new List<TextureSlot>();
        MeshUV _uv;
        Compositor _comp;
        readonly AvatarPreview _preview = new AvatarPreview();
        List<TargetResolver.SlotRef> _users = new List<TargetResolver.SlotRef>();
        List<Material> _otherMaterials = new List<Material>();
        bool _dirty, _needsRebuild = true, _selectionVisualsDirty, _strokeFlushQueued;
        string _status = "";
        TextureRecipe _recipeToLoad;

        BrushPreview _brushPreview;
        float _panelContentWidth = 300f;
        int _splitterId;

        // guided workflow (tabs = steps), unsaved-work tracking, last bake result
        enum Tab { Target = 0, Select = 1, Paint = 2, Layers = 3, Save = 4, Colour = 5 } // values are saved; display order is TabDefs
        [SerializeField] Tab _tab = Tab.Target;
        [SerializeField] bool _unsaved;
        [SerializeField] bool _foldMoreBrushes, _foldOutputOptions;
        [SerializeField] string _lastBakePng, _lastBakeMaterial, _lastBakeRecipe, _lastSavedEdit;
        [SerializeField] bool _lastBakeClearedLayers;
        TextureRecipe _pendingRecipe;
        Texture _pendingTexture;
        TextureRecipe _editThatMadeTexture; // set when the current texture is itself a bake output
        string _transparencyNote;            // main texture transparency vs the material render mode
        bool _eyedropperArmed;
        bool _scenePickArmed; // next click in the Scene view picks a colour (texture colour on this mesh, else what you see)

        // live brush stroke (not yet committed to its layer)
        PaintStroke _liveStroke;
        PaintLayer _liveLayer;
        Layer _liveMaskLayer;       // set instead of _liveLayer while painting a layer mask
        int _strokeUndoGroup;

        // Layer mask editing: brush strokes go to the mask of this layer (only while it is the active layer).
        [SerializeField] string _maskEditLayerId;
        [SerializeField] bool _maskFillHides = true;
        [SerializeField] bool _showMaskOverlay = true;

        bool MaskEditing => _tool == Tool.Paint && !string.IsNullOrEmpty(_maskEditLayerId) && _maskEditLayerId == _activeLayerId &&
                            ActiveLayer != null && ActiveLayer.hasPaintMask;

        TextureSlot CurrentSlot => _slots != null && _slotIndex >= 0 && _slotIndex < _slots.Count ? _slots[_slotIndex] : null;
        int ActiveIndex => _recipe == null ? -1 : _recipe.layers.FindIndex(l => l != null && l.id == _activeLayerId);
        Layer ActiveLayer { get { int i = ActiveIndex; return i >= 0 ? _recipe.layers[i] : null; } }

        [MenuItem("Tools/Avistry/Textures")]
        public static void Open() => Open(true);

        /// <param name="pickSelection">False lets the AI helper pick the Hierarchy selection itself, inside its own Undo step.</param>
        internal static void Open(bool pickSelection)
        {
            var inspector = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            var w = inspector != null
                ? GetWindow<AvatarTextureWindow>("Avistry Textures", true, inspector)
                : GetWindow<AvatarTextureWindow>("Avistry Textures", true);
            w.minSize = new Vector2(340, 420);
            if (pickSelection) w.PickSelectionIfEmpty();
        }

        internal void PickSelectionIfEmpty()
        {
            if (_renderer != null || Selection.activeGameObject == null) return;
            var r = ResolveRenderer(Selection.activeGameObject);
            if (r != null) SetRenderer(r);
        }

        /// <summary>
        /// Accepts whatever the user points at (Postel's law): the renderer itself, its GameObject, a parent
        /// (e.g. the avatar root: first mesh with materials below it) or a child. Only mesh renderers qualify.
        /// </summary>
        static Renderer ResolveRenderer(UnityEngine.Object o)
        {
            if (o is Renderer r0) return IsMeshRenderer(r0) ? r0 : null;
            var go = o as GameObject ?? (o as Component)?.gameObject;
            if (go == null) return null;
            var own = go.GetComponent<Renderer>();
            if (own != null && IsMeshRenderer(own)) return own;
            var child = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => IsMeshRenderer(x) && x.sharedMaterials.Any(m => m != null));
            if (child != null) return child;
            var parent = go.GetComponentInParent<Renderer>();
            return parent != null && IsMeshRenderer(parent) ? parent : null;
        }

        static bool IsMeshRenderer(Renderer r) => r is SkinnedMeshRenderer || r is MeshRenderer;

        /// <summary>The mesh the user has selected in the Hierarchy, if it isn't the current target.</summary>
        Renderer SuggestedRenderer
        {
            get
            {
                var r = Selection.activeGameObject != null ? ResolveRenderer(Selection.activeGameObject) : null;
                return r != _renderer ? r : null;
            }
        }

        void OnSelectionChange() => Repaint();

        static string s_version;
        /// <summary>The installed package version (from package.json), shown so testers can say which build they have.</summary>
        internal static string Version => s_version ??=
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AvatarTextureWindow).Assembly)?.version ?? "dev";

        void OnEnable()
        {
            titleContent = StudioGUI.Icon("Grid.PaintTool", _automationSandbox ? Loc.T("Avistry Textures (test)") : Loc.T("Avistry Textures"), Loc.F("Avistry {0}", Version));
            wantsMouseMove = true;
            HookUpdateCheck();
            saveChangesMessage = Loc.T("Avistry has edits that aren't baked yet.\n\n" +
                                 "Save keeps them as a saved edit (a small file next to the texture) that you can open again later. " +
                                 "It does not bake or change your texture.");
            if (_brush == null) _brush = new BrushSettings();
            if (_brush.shape == null) _brush.shape = new BrushShape();
            if (_recipe == null)
            {
                _recipe = CreateInstance<TextureRecipe>();
                _recipe.hideFlags = HideFlags.DontSave;
                _recipe.name = "working edit";
            }
            _needsRebuild = true;
            TryRestoreRenderer();
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            ClaimSceneBar();
            Undo.undoRedoPerformed += OnUndoRedo;
            Undo.undoRedoEvent += OnUndoRedoEvent;
            Learn.ConceptDone -= OnLearnConceptDone;
            Learn.ConceptDone += OnLearnConceptDone;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += Teardown;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            UpdateCheck.Changed -= Repaint;
            SceneView.duringSceneGui -= OnSceneGUI;
            ReleaseSceneBar();
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoEvent -= OnUndoRedoEvent;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload -= Teardown;
            EditorApplication.update -= ToastTick;
            Learn.ConceptDone -= OnLearnConceptDone;
            EditorApplication.update -= LearnToastTick;
            Teardown();
        }

        void OnDestroy()
        {
            if (_recipe != null && !EditorUtility.IsPersistent(_recipe)) DestroyImmediate(_recipe);
        }

        void Teardown()
        {
            CancelStroke();
            _preview.Clear();
            _comp?.Dispose();
            _comp = null;
            DestroyCanvasResources();
            DestroySceneResources();
            _needsRebuild = true;
            SceneView.RepaintAll();
        }

        void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode) { Teardown(); ClearHistory(); }
            if (s == PlayModeStateChange.EnteredEditMode) { _needsRebuild = true; Repaint(); }
        }

        void OnUndoRedo()
        {
            if (AlsoSignature() != _alsoSignature) _needsRebuild = true; // a mesh was included or left out
            if (_renderer != null) // Undo of "Make an editable copy" puts the original material back on the part
            {
                var slotMats = _renderer.sharedMaterials;
                if (_materialIndex < slotMats.Length && slotMats[_materialIndex] != _material) _needsRebuild = true;
            }
            _dirty = true;
            _savedBlockVisible = false;
            _emptyMasks = null;
            _selectionVisualsDirty = true; // GoToHistory fires this many times in a row: rebuild once on the next Layout
            if (_renderer != null) Learn.Did(LearnEvents.UndoUsed); // other windows' undos reach here too
            Repaint();
        }

        /// <summary>Unity's own "unsaved changes" prompt (window close / editor quit) calls this when the user picks Save.</summary>
        public override void SaveChanges()
        {
            EndStroke();
            SaveEditAsset();
            base.SaveChanges();
        }

        /// <summary>Marks the layer stack as changed: recomposite, and remember there is unbaked work.</summary>
        void MarkEdited()
        {
            _unsaved = true;
            _savedBlockVisible = false;
            ShowEditsIfBeforeOn();
            _dirty = true;
            RecordHistory();
            Repaint();
        }

        /// <summary>Saves the working layers as a "saved edit" asset next to the texture (no bake).</summary>
        string SaveEditAsset()
        {
            var slot = CurrentSlot;
            if (_recipe == null || _recipe.layers.Count == 0) return null;
            string dir = Exporter.WritableFolder(slot?.assetPath);
            string baseName = slot != null ? slot.texture.name : "Texture";
            var copy = Instantiate(_recipe);
            copy.hideFlags = HideFlags.None;
            copy.bakedTexture = null;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}_edit.asset");
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            _unsaved = false;
            _lastSavedEdit = path;
            hasUnsavedChanges = false;
            Toast(Loc.F("Saved edit: {0}. Open it again from 1 · Pick.", System.IO.Path.GetFileName(path)), MessageType.Info);
            Learn.Did(LearnEvents.EditSaved);
            return path;
        }

        /// <summary>
        /// Before switching mesh/material/texture with layers in the stack: ask whether to keep them (applied to the
        /// new texture) or start fresh. Returns false if the user cancelled.
        /// </summary>
        bool ConfirmTargetChange(string what)
        {
            if (_recipe == null || _recipe.layers.Count == 0) { NoteTargetChange(what); return true; }
            int n = _recipe.layers.Count;
            string tex = CurrentSlot != null ? CurrentSlot.texture.name : Loc.T("this texture");
            int c = StudioDialog.AskComplex(Loc.F("Switch {0}?", what), Msg.M4TargetChange(n, tex, _unsaved),
                Msg.BtnMoveLayers, Msg.BtnCancel, Msg.BtnStartEmpty);
            if (c == 1) return false;
            NoteTargetChange(what);
            ClearHistory(); // a different texture: the old steps no longer belong to this edit
            if (c == 2)
            {
                Undo.RecordObject(_recipe, "Start fresh");
                Undo.RecordObject(this, "Start fresh");
                _recipe.layers.Clear();
                _activeLayerId = null;
                _unsaved = false;
            }
            return true;
        }

        /// <summary>A confirmed target change: the "Saved" state no longer describes what is shown, and a texture picked by hand must survive the next Rebuild's auto-pick.</summary>
        void NoteTargetChange(string what)
        {
            _savedBlockVisible = false;
            if (what == "texture") _slotChosenByUser = true;
            else if (what == "material") _slotChosenByUser = false;
        }

        void SetRendererChecked(Renderer r)
        {
            if (r == null || r == _renderer) return;
            if (!ConfirmTargetChange("part")) return;
            SetRenderer(r);
        }

        void SetRenderer(Renderer r)
        {
            if (r == _renderer) return;
            Undo.RecordObject(this, "Change target");
            _renderer = r;
            _rendererId = r != null ? GlobalObjectId.GetGlobalObjectIdSlow(r).ToString() : null;
            _materialIndex = 0;
            _slotIndex = 0;
            _slotChosenByUser = false;
            _savedBlockVisible = false;
            _selection.Clear();
            _alsoRenderers.Clear(); // included meshes belong to the old texture
            _alsoSlots.Clear();
            _needsRebuild = true;
            if (r != null) LearnTargetPicked(r);
        }

        /// <summary>Resolves material, textures, UVs and builds the compositor for the current target.</summary>
        void Rebuild()
        {
            _needsRebuild = false;
            CancelStroke();
            _preview.Clear();
            _comp?.Dispose();
            _comp = null;
            _uv?.ReleaseSurfaceMesh();
            _uv = null;
            _slots = new List<TextureSlot>();
            _status = "";
            _statusFix = StatusFix.None;
            if (_renderer == null) return;

            try
            {
                var mats = _renderer.sharedMaterials;
                _materialIndex = Mathf.Clamp(_materialIndex, 0, Mathf.Max(0, mats.Length - 1));
                _material = mats.Length > 0 ? mats[_materialIndex] : null;
                if (_material == null) { _status = Msg.M25SlotStatus(false); _statusFix = StatusFix.PickAnotherMesh; return; }

                _slots = TargetResolver.GetTextureSlots(_material);
                if (_slots.Count == 0) { _status = Loc.T("This material has no 2D textures assigned."); return; }
                if (_pendingTexture != null)
                {
                    int pi = _slots.FindIndex(s => s.texture == _pendingTexture);
                    if (pi >= 0) { _slotIndex = pi; _slotChosenByUser = true; }
                    _pendingTexture = null;
                }
                // The main colour texture is almost always the one to paint: don't start on a mask or normal map.
                if (!_slotChosenByUser)
                {
                    int ci = _slots.FindIndex(s => s.role == TextureRole.Color);
                    if (ci >= 0) _slotIndex = ci;
                }
                _slotIndex = Mathf.Clamp(_slotIndex, 0, _slots.Count - 1);
                var slot = CurrentSlot;
                _editThatMadeTexture = FindEditThatMade(slot.texture);
                // Main texture transparency vs render mode (checked from the pixels once per rebuild, not every frame).
                _transparencyNote = null;
                var mainTex = _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;
                if (mainTex != null)
                {
                    var px = Recolour.SampleTexture(mainTex, 64, Color.white);
                    bool seeThrough = px != null && px.Any(p => p.a < 0.96f);
                    _transparencyNote = MaterialWriter.TransparencyIssue(_material, seeThrough);
                }

                var root = TargetResolver.AvatarRoot(_renderer);
                _users = TargetResolver.FindMaterialUsers(root, _material);
                // Every mesh using this material contributes UVs (one material is often split over several meshes), then
                // the meshes included from other materials that show the same texture (1 · Pick › Meshes on this texture).
                _uv = MeshUV.Build(ResolveIncludedMeshes(root, slot), slot.uvChannel);
                if (_uv == null) { _status = Msg.M25SlotStatus(true); _statusFix = StatusFix.PickAnotherMesh; }
                else ClaimSceneBar();
                _selection.RemoveAll(i => _uv == null || i >= _uv.islandCount);

                _comp = new Compositor(slot, _workSize);
                LearnTextureLoaded();
                _recipe.sourceTexture = slot.texture;
                _recipe.sourceMaterial = _material;
                _recipe.materialIndex = _materialIndex;
                _recipe.rendererPath = root != null ? AnimationUtility.CalculateTransformPath(_renderer.transform, root) : _renderer.name;
                _zoom = 0; // frame on next draw
                if (slot.role == TextureRole.Normal && _tool == Tool.Paint) _tool = Tool.Select;
                if (slot.role == TextureRole.Normal && (_tab == Tab.Paint || _tab == Tab.Layers)) _tab = Tab.Target;
                RebuildSelectionVisuals();
                _dirty = true;

                if (_pendingRecipe != null)
                {
                    var r = _pendingRecipe;
                    _pendingRecipe = null;
                    LoadRecipe(r);
                }
            }
            catch (Exception e)
            {
                _status = Loc.F("Error: {0}", e.Message);
                Debug.LogException(e);
                _comp?.Dispose();
                _comp = null;
            }
        }

        /// <summary>The saved edit (recipe) whose bake produced this texture, or null if it's an original.</summary>
        static TextureRecipe FindEditThatMade(Texture tex)
        {
            if (tex == null) return null;
            // Cheap path first: "<name>_ats.png" sits next to "<name>_ats_recipe.asset".
            string path = AssetDatabase.GetAssetPath(tex);
            string guess = null;
            if (!string.IsNullOrEmpty(path))
            {
                guess = $"{System.IO.Path.GetDirectoryName(path).Replace('\\', '/')}/{tex.name}_recipe.asset";
                var g = AssetDatabase.LoadAssetAtPath<TextureRecipe>(guess);
                if (g != null && g.bakedTexture == tex) return g;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:TextureRecipe"))
            {
                var r = AssetDatabase.LoadAssetAtPath<TextureRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null && r.bakedTexture == tex) return r;
            }
            // Bakes from before the link existed (no bakedTexture): trust the name guess.
            if (guess == null) return null;
            return System.Text.RegularExpressions.Regex.IsMatch(tex.name, @"_ats(_\d+)?$") ? AssetDatabase.LoadAssetAtPath<TextureRecipe>(guess) : null;
        }

        /// <summary>
        /// Opens a saved edit. If it was made for a different texture (typically: you're on the baked copy and the
        /// edit belongs to the original), offer to put the original back first so the edit isn't applied twice.
        /// </summary>
        void OpenSavedEdit(TextureRecipe r)
        {
            if (r == null) return;
            var slot = CurrentSlot;
            if (r.sourceTexture == null || slot == null || slot.texture == r.sourceTexture) { if (ConfirmReplaceLayers(r)) LoadRecipe(r); return; }

            int c = StudioDialog.AskComplex(Loc.T("Open saved edit"), Msg.M7OpenSavedEdit(r.sourceTexture.name, slot.texture.name),
                $"{Msg.BtnGoBack} \"{r.sourceTexture.name}\"", Msg.BtnCancel, Msg.BtnOpenHere);
            if (c == 1) return;
            if (c == 2) { if (ConfirmReplaceLayers(r)) LoadRecipe(r); return; }
            RestoreOriginalAndOpen(r);
        }

        void RestoreOriginalAndOpen(TextureRecipe r)
        {
            EndStroke();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            if (r.sourceMaterial != null && r.sourceMaterial != _material)
            {
                // "New PNG + duplicate material" bake: put the original material back on every slot using the copy.
                foreach (var u in _users)
                {
                    if (u.renderer == null) continue;
                    Undo.RecordObject(u.renderer, "Restore original material");
                    var mats = u.renderer.sharedMaterials;
                    if (u.materialIndex < mats.Length) mats[u.materialIndex] = r.sourceMaterial;
                    u.renderer.sharedMaterials = mats;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(u.renderer);
                }
            }
            else if (_material != null && CurrentSlot != null)
            {
                // "Update this material" bake: point the material back at the original texture.
                Undo.RecordObject(_material, "Restore original texture");
                foreach (var p in CurrentSlot.properties) _material.SetTexture(p, r.sourceTexture);
                EditorUtility.SetDirty(_material);
            }
            Undo.SetCurrentGroupName("Switch back to original texture");
            Undo.CollapseUndoOperations(group);
            _pendingTexture = r.sourceTexture;
            _pendingRecipe = r;
            _needsRebuild = true;
            Repaint();
        }

        void ComposeNow()
        {
            _dirty = false;
            if (_comp == null) return;
            var slot = CurrentSlot;
            if (slot.role == TextureRole.Normal) _comp.Compose(Array.Empty<Layer>(), _uv); // normal maps: view only
            else _comp.Compose(_recipe.layers, _uv);

            if (_previewOnAvatar && _users.Count + _alsoUsers.Count > 0)
                _preview.Apply(PreviewSlots(slot), slot.texture, _showOriginal ? _comp.SourcePreview : _comp.Preview);
            else _preview.Clear();
            Repaint();
        }

        void OnGUI()
        {
            var e = Event.current;
            // The texture being edited was deleted (or the asset replaced): look at the material again instead of drawing a dead reference.
            if (e.type == EventType.Layout && CurrentSlot != null && CurrentSlot.texture == null) _needsRebuild = true;
            if (_needsRebuild && e.type == EventType.Layout) Rebuild();
            if (_selectionVisualsDirty && e.type == EventType.Layout) { _selectionVisualsDirty = false; RebuildSelectionVisuals(); }
            if (_dirty && e.type == EventType.Layout) ComposeNow();
            HandleShortcuts(e);
            LearnToastInput(e);
            LearnPulse.Latch();

            const float toolbarH = 21f;
            var toolbar = new Rect(0, 0, position.width, toolbarH);
            float bodyH = position.height - toolbarH;
            Rect canvas, panel, splitter;
            bool sideBySide = position.width >= MinPanelWidth + MinCanvasWidth;
            if (sideBySide)
            {
                // Canvas left, panel right. Drag the divider to resize the panel.
                float pw = Mathf.Clamp(_panelWidth, MinPanelWidth, position.width - MinCanvasWidth);
                canvas = new Rect(0, toolbarH, position.width - pw, bodyH);
                panel = new Rect(canvas.xMax + 1, toolbarH, pw - 1, bodyH);
                splitter = new Rect(canvas.xMax - 3, toolbarH, 7, bodyH);
            }
            else
            {
                // Narrow (e.g. docked beside the Inspector): canvas on top, panel below. Drag the divider to resize.
                float h = Mathf.Clamp(bodyH * _canvasHeightFraction, 140f, Mathf.Max(140f, bodyH - 160f));
                canvas = new Rect(0, toolbarH, position.width, h);
                panel = new Rect(0, canvas.yMax + 1, position.width, bodyH - h - 1);
                splitter = new Rect(0, canvas.yMax - 3, position.width, 7);
            }

            DrawToolbar(toolbar);
            HandleSplitter(splitter, sideBySide, toolbarH, bodyH); // before the canvas, so the canvas doesn't grab the drag
            DrawCanvas(canvas);
            if (e.type == EventType.Repaint)
            {
                var line = sideBySide ? new Rect(canvas.xMax, toolbarH, 1, bodyH) : new Rect(0, canvas.yMax, position.width, 1);
                EditorGUI.DrawRect(line, GUIUtility.hotControl == _splitterId && _splitterId != 0 ? StudioGUI.Accent : StudioGUI.HeaderLine);
            }

            DrawPanelArea(panel);
            DrawLearnToast(canvas);
            LearnPoll();
            hasUnsavedChanges = _unsaved && _recipe != null && _recipe.layers.Count > 0;
        }

        /// <summary>The splitter's rule: at least MinPanelWidth, and always leave MinCanvasWidth for the texture view.</summary>
        void SetPanelWidth(float w) => _panelWidth = Mathf.Clamp(w, MinPanelWidth, Mathf.Max(MinPanelWidth, position.width - MinCanvasWidth));

        void HandleSplitter(Rect r, bool sideBySide, float top, float bodyH)
        {
            var e = Event.current;
            _splitterId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.AddCursorRect(r, sideBySide ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            switch (e.GetTypeForControl(_splitterId))
            {
                case EventType.MouseDown when e.button == 0 && r.Contains(e.mousePosition):
                    GUIUtility.hotControl = _splitterId;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == _splitterId:
                    if (sideBySide) SetPanelWidth(position.width - e.mousePosition.x);
                    else _canvasHeightFraction = Mathf.Clamp((e.mousePosition.y - top) / Mathf.Max(1f, bodyH), 0.15f, 0.85f);
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == _splitterId:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    Repaint();
                    break;
            }
        }

        void HandleShortcuts(Event e)
        {
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField || e.control || e.command || e.alt) return;
            switch (e.keyCode)
            {
                case KeyCode.S: SetTool(Tool.Select); e.Use(); break;
                case KeyCode.B: SetTool(Tool.Paint); SetPaintTool(0); e.Use(); break;
                case KeyCode.G: SetTool(Tool.Paint); SetPaintTool(e.shift ? 3 : 2); e.Use(); break; // Shift+G = gradient
                case KeyCode.E: SetTool(Tool.Paint); SetPaintTool(1); e.Use(); break;
                case KeyCode.J: SetTool(Tool.Paint); SetPaintTool(4); e.Use(); break; // Blend (blur / smudge), CSP's shortcut
                case KeyCode.X: SwapColours(); e.Use(); break; // main ⇄ sub colour
                case KeyCode.LeftBracket: ResizeBrush(1f / 1.2f); e.Use(); break;
                case KeyCode.RightBracket: ResizeBrush(1.2f); e.Use(); break;
                case KeyCode.I when _tool == Tool.Paint:
                    _eyedropperArmed = !_eyedropperArmed;
                    if (_eyedropperArmed) Toast(Loc.T("Eyedropper: click the texture or the model to pick a colour."), MessageType.Info, 3f);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        /// <summary>Tabs are the workflow steps. Select and Paint also switch the canvas tool.</summary>
        void SetTab(Tab t)
        {
            if (!TabAvailable(t, out var why)) { Toast(why, MessageType.Warning); return; }
            _tab = t;
            if (t == Tab.Select) SetTool(Tool.Select);
            else if (t == Tab.Paint) SetTool(Tool.Paint);
            else if (t == Tab.Colour && ByPart && _tool == Tool.Paint) SetTool(Tool.Select); // clicks pick parts there, not paint
            GUI.FocusControl(null);
            _panelScroll = Vector2.zero;
            Repaint();
        }

        bool TabAvailable(Tab t, out string why)
        {
            why = null;
            if (t == Tab.Target) return true;
            if (t == Tab.Colour)
            {
                if (_material == null) { why = Loc.T("Pick a part in 1 · Pick first."); return false; }
                return true;
            }
            if (_comp == null) { why = Loc.T("Pick a part and a texture in 1 · Pick first."); return false; }
            if (t != Tab.Select && CurrentSlot?.role == TextureRole.Normal)
            {
                why = Msg.M20NormalMap();
                return false;
            }
            return true;
        }

        void SetTool(Tool t)
        {
            if (t == Tool.Paint && CurrentSlot?.role == TextureRole.Normal)
            {
                Toast(Loc.T("Normal maps can't be painted. Pick a colour or mask texture in 1 · Pick."), MessageType.Warning);
                return;
            }
            // Keep the panel tab in step with the tool (the toolbar buttons and B/S/E keys switch tabs too).
            if (t == Tool.Paint) _tab = Tab.Paint;
            else if (_tab == Tab.Paint) _tab = Tab.Select;
            if (_tool == t) { Repaint(); return; }
            EndStroke();
            EndGradientDrag();
            CancelMarquee();
            _tool = t;
            _hoverIsland = -1;
            _eyedropperArmed = false;
            Repaint();
            SceneView.RepaintAll();
        }

        void ResizeBrush(float factor)
        {
            _brush.size = Mathf.Clamp(Mathf.Round(_brush.size * factor), BrushSettings.MinSize, BrushSettings.MaxSize);
            Repaint();
            SceneView.RepaintAll();
        }

        void ApplySelection(List<int> islands, bool add, bool remove)
        {
            Undo.RecordObject(this, "Select pieces");
            if (!add && !remove) _selection.Clear();
            if (remove) { var gone = new HashSet<int>(islands); _selection.RemoveAll(gone.Contains); }
            else
            {
                var have = new HashSet<int>(_selection);
                foreach (var i in islands) if (have.Add(i)) _selection.Add(i);
            }
            RebuildSelectionVisuals();
            if (_selection.Count > 0) Learn.Did(LearnEvents.PiecesSelected);
            Repaint();
        }

        void SetSelection(IEnumerable<int> islands)
        {
            Undo.RecordObject(this, "Select pieces");
            _selection = islands.Distinct().ToList();
            RebuildSelectionVisuals();
            if (_selection.Count > 0) Learn.Did(LearnEvents.PiecesSelected);
            Repaint();
        }

        void RebuildSelectionVisuals()
        {
            _focusKey = null; // islands may have changed: rebuild the focus overlay on next draw
            if (_uv == null || _comp == null) { _selSegments = Array.Empty<Vector3>(); return; }
            var set = new HashSet<int>(_selection);
            _selSegments = _uv.SegmentsForIslands(set);

            int ow = Mathf.Min(1024, _comp.Width);
            int oh = Mathf.Max(1, Mathf.RoundToInt(_comp.Height * (ow / (float)_comp.Width)));
            if (_selOverlay == null || _selOverlay.width != ow || _selOverlay.height != oh)
            {
                if (_selOverlay != null) DestroyImmediate(_selOverlay);
                _selOverlay = new Texture2D(ow, oh, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            _selOverlay.SetPixels32(_uv.SelectionOverlay(ow, oh, set, new Color32(255, 140, 20, 90)));
            _selOverlay.Apply(false);

            if (_uv.PoseChanged()) _uv.RefreshPose(); // re-bake only if the pose moved, so the highlight matches
            BuildSelectionHighlight();
            SceneView.RepaintAll();
        }

        static float Pressure(Event e) => e.pointerType == PointerType.Pen && e.pressure > 0 ? e.pressure : 1f;

        bool CanPaint(out string why)
        {
            why = null;
            if (_comp == null) why = Loc.T("Pick a renderer and texture first.");
            else if (CurrentSlot?.role == TextureRole.Normal) why = Loc.T("Normal maps can't be painted.");
            return why == null;
        }

        /// <summary>Starts a stroke on the active Paint layer (one is created if needed).</summary>
        void BeginStroke(Vector2 uv, float pressure, int island, MeshUV.SurfaceHit? hit = null)
        {
            if (!CanPaint(out var why)) { Toast(why, MessageType.Warning); return; }
            if (MaskEditing) { BeginMaskStroke(uv, pressure, island, hit); return; }
            if (_brush.mode == BrushMode.Erase)
            {
                // The eraser only removes paint strokes: don't silently create an empty layer to "erase" on.
                if (!(ActiveLayer is PaintLayer target))
                {
                    Toast(Msg.M14EraserNeedsPaint(), MessageType.Warning, 6f, Msg.BtnUseBrush, () => SetPaintTool(0));
                    return;
                }
                if (target.strokes.Count == 0)
                {
                    Toast(Msg.M14EraserNeedsPaint(), MessageType.Info, 4f, Msg.BtnUseBrush, () => SetPaintTool(0));
                    return;
                }
            }
            EndStroke();
            Undo.IncrementCurrentGroup();
            _strokeUndoGroup = Undo.GetCurrentGroup();
            ShowEditsIfBeforeOn(); // painting while "Before" is on would look like nothing happens
            _liveLayer = BlendToolActive ? EnsureBlendLayer() : EnsurePaintLayer();
            _liveStroke = _brush.NewStroke(_comp.SourceWidth);
            _liveStroke.lockAlpha = _liveLayer.lockTransparent;
            _comp.LiveStroke = _liveStroke;
            _comp.LiveLayerId = _liveLayer.id;
            SetupSurfaceStroke(_liveStroke, hit);
            StartMirrorStroke();
            if (_liveStroke.isFill)
            {
                // Fill: one click, applied straight away (CSP Fill tool).
                _liveStroke.points.Add(new Vector4(uv.x, uv.y, 1f, 0f));
                MirrorStrokePoint(uv, 1f, false, island);
                EndStroke();
                return;
            }
            AddStrokePoint(uv, pressure, false, island, hit, false);
        }

        /// <summary>
        /// A stroke on the active layer's mask. Brush = reveal, Eraser = hide, Fill = hide or reveal (its setting).
        /// Mask strokes store the change from the mask's starting state, so the stroke's mode depends on that state.
        /// </summary>
        void BeginMaskStroke(Vector2 uv, float pressure, int island, MeshUV.SurfaceHit? hit = null)
        {
            var layer = ActiveLayer;
            EndStroke();
            Undo.IncrementCurrentGroup();
            _strokeUndoGroup = Undo.GetCurrentGroup();
            ShowEditsIfBeforeOn();
            if (!layer.enabled) { Undo.RecordObject(_recipe, "Show layer"); layer.enabled = true; }
            bool reveal = _brush.fillTool ? !_maskFillHides : _brush.mode == BrushMode.Paint;
            _liveMaskLayer = layer;
            _liveStroke = _brush.NewStroke(_comp.SourceWidth);
            _liveStroke.mode = reveal == layer.paintMaskStartsHidden ? BrushMode.Paint : BrushMode.Erase;
            _liveStroke.tool = StrokeTool.Brush; // masks are painted, never blurred
            _liveStroke.color = Color.white;
            _liveStroke.lockAlpha = false;
            _comp.LiveStroke = _liveStroke;
            _comp.LiveLayerId = Compositor.MaskKey(layer.id);
            SetupSurfaceStroke(_liveStroke, hit);
            StartMirrorStroke();
            if (_liveStroke.isFill)
            {
                _liveStroke.points.Add(new Vector4(uv.x, uv.y, 1f, 0f));
                MirrorStrokePoint(uv, 1f, false, island);
                EndStroke();
                return;
            }
            AddStrokePoint(uv, pressure, false, island, hit, false);
        }

        /// <summary>Starts or stops painting on a layer's mask (adds a mask first if it has none).</summary>
        void SetMaskEditing(Layer layer, bool on)
        {
            EndStroke();
            if (!on || layer == null) { _maskEditLayerId = null; Repaint(); SceneView.RepaintAll(); return; }
            if (!layer.hasPaintMask) AddPaintMask(layer, false);
            _activeLayerId = layer.id;
            _maskEditLayerId = layer.id;
            if (_brush.gradientTool) SetPaintTool(0); // gradients aren't drawn on masks
            if (_tool != Tool.Paint) SetTab(Tab.Paint);
            Toast(Msg.M12MaskPainting(layer.name), MessageType.Info, 5f, Msg.BtnDone, () => SetMaskEditing(null, false));
            Repaint();
            SceneView.RepaintAll();
        }

        void AddPaintMask(Layer layer, bool startHidden)
        {
            Undo.RecordObject(_recipe, "Add layer mask");
            layer.hasPaintMask = true;
            layer.paintMaskStartsHidden = startHidden;
            layer.maskStrokes = new List<PaintStroke>();
            MarkEdited();
            Learn.Did(LearnEvents.MaskMade);
        }

        void InvertPaintMask(Layer layer)
        {
            Undo.RecordObject(_recipe, "Invert layer mask");
            layer.paintMaskStartsHidden = !layer.paintMaskStartsHidden; // stored strokes are relative, so this inverts it
            MarkEdited();
            Learn.Did(LearnEvents.MaskMade);
        }

        void DeletePaintMask(Layer layer)
        {
            EndStroke();
            Undo.RecordObject(_recipe, "Delete layer mask");
            layer.hasPaintMask = false;
            layer.paintMaskStartsHidden = false;
            layer.maskStrokes = new List<PaintStroke>();
            if (_maskEditLayerId == layer.id) _maskEditLayerId = null;
            MarkEdited();
        }

        void SetClipToBelow(Layer layer, bool on)
        {
            Undo.RecordObject(_recipe, on ? "Clip to layer below" : "Release clipping");
            layer.clipToBelow = on;
            MarkEdited();
            if (on) Learn.Did(LearnEvents.LayerClip);
        }

        /// <param name="joined">False starts a new run (no dabs between this and the previous point).</param>
        void AddStrokePoint(Vector2 uv, float pressure, bool joined, int island, MeshUV.SurfaceHit? hit = null, bool surfJoined = true)
        {
            if (_liveStroke == null) return;
            if (_liveStroke.surface)
            {
                if (hit == null) return; // a 3D stroke only takes points from the model
                var h = hit.Value;
                _liveStroke.surf.Add(new Vector4(h.triangle, h.bary.y, h.bary.z, (surfJoined ? 1 : 0) + (h.backFace ? 2 : 0)));
            }
            _liveStroke.points.Add(new Vector4(uv.x, uv.y, pressure, joined ? 1 : 0));
            if (_liveStroke.clip && _uv != null)
            {
                // Islands touched by the brush centre (plus anything overlapping them in UV, e.g. mirrored parts).
                if (island >= 0 && !_liveStroke.clipIslands.Contains(island)) _liveStroke.clipIslands.Add(island);
                foreach (int i in _uv.IslandsAtUV(uv))
                    if (!_liveStroke.clipIslands.Contains(i)) _liveStroke.clipIslands.Add(i);
            }
            MirrorStrokePoint(uv, pressure, joined, island, surfJoined, hit.HasValue && hit.Value.backFace);
            // Scene-view strokes may not get an OnGUI: coalesce to one compose per editor update (EndStroke composes the final state).
            _dirty = true;
            if (!_strokeFlushQueued)
            {
                _strokeFlushQueued = true;
                EditorApplication.delayCall += () =>
                {
                    _strokeFlushQueued = false;
                    if (this == null) return;
                    if (_dirty) ComposeNow();
                };
            }
        }

        void EndStroke()
        {
            if (_liveStroke == null) return;
            var s = _liveStroke;
            var layer = _liveLayer;
            var maskLayer = _liveMaskLayer;
            var mirror = TakeMirrorStroke();
            CancelStroke();
            if (maskLayer != null)
            {
                if (s.points.Count > 0 && _recipe.layers.Contains(maskLayer))
                {
                    Undo.RecordObject(_recipe, "Paint layer mask");
                    maskLayer.maskStrokes.Add(s);
                    AddMirror(maskLayer.maskStrokes, s, mirror);
                    Undo.SetCurrentGroupName("Paint layer mask");
                    Undo.CollapseUndoOperations(_strokeUndoGroup);
                    RecordHistory();
                    _unsaved = true;
                    _savedBlockVisible = false;
                    Learn.Did(LearnEvents.MaskMade);
                    if (s.clip && s.clipIslands.Count == 0)
                        Toast(Msg.M13EmptySpaceStroke(), MessageType.Warning, 6f, Msg.BtnPaintAnywhere, PaintAnywhere);
                }
                ComposeNow();
                return;
            }
            if (layer != null && s.points.Count > 0 && _recipe.layers.Contains(layer))
            {
                string undoName = s.mode == BrushMode.Erase ? "Erase stroke" : s.tool == StrokeTool.Blur ? "Blur stroke" : s.tool == StrokeTool.Smudge ? "Smudge stroke" : "Paint stroke"; // (the colour mixing brush counts as painting)
                Undo.RecordObject(_recipe, undoName);
                layer.strokes.Add(s);
                AddMirror(layer.strokes, s, mirror);
                Undo.SetCurrentGroupName(undoName);
                Undo.CollapseUndoOperations(_strokeUndoGroup);
                RecordHistory();
                if (s.mode == BrushMode.Paint && (!s.IsBlend || s.tool == StrokeTool.Mix)) RememberColour(s.color);
                _unsaved = true;
                _savedBlockVisible = false;
                Learn.Did(s.mode == BrushMode.Erase ? LearnEvents.LayerErase : LearnEvents.LayerStroke);
                if (s.IsSurface) Learn.Did(LearnEvents.SeamSmoothStroke);
                ExplainIfNothingVisible(s, layer);
            }
            ComposeNow();
        }

        /// <summary>
        /// Dead-end prevention: if a stroke can't have changed anything visible, say why and offer the fix
        /// instead of leaving the user wondering why nothing happened.
        /// </summary>
        void ExplainIfNothingVisible(PaintStroke s, PaintLayer layer)
        {
            if (s.mode != BrushMode.Paint || _uv == null) return;
            if (s.isFill && s.points.Count > 0 && _uv.IslandsAtUV(new Vector2(s.points[0].x, s.points[0].y)).Count == 0)
            {
                Toast(Msg.M13EmptySpaceStroke(), MessageType.Warning, 6f);
                return;
            }
            if (s.clip && s.clipIslands.Count == 0)
            {
                Toast(Msg.M13EmptySpaceStroke(), MessageType.Warning, 7f, Msg.BtnPaintAnywhere, PaintAnywhere);
                return;
            }
            if (layer.opacity <= 0.001f)
            {
                Toast(Msg.M15ZeroOpacity(layer.name), MessageType.Warning, 7f, Msg.BtnSet100, () => SetOpacity100(layer));
                return;
            }
            if (layer.maskMode == MaskMode.SelectedIslands)
            {
                var touched = s.clip ? new HashSet<int>(s.clipIslands) : IslandsUnderStroke(s);
                var mask = new HashSet<int>(layer.islands);
                bool visible = layer.invertMask ? touched.Any(i => !mask.Contains(i)) : touched.Overlaps(mask);
                if (!visible)
                {
                    Toast(Msg.M16MaskedTo(layer.name, layer.islands.Count), MessageType.Warning, 7f,
                        Msg.BtnAffectEverything, () => AffectEverything(layer));
                    return;
                }
            }
            var focus = FocusIslands();
            if (focus != null && !s.clip && !IslandsUnderStroke(s).Overlaps(focus))
                Toast(Msg.M17FocusDimming(), MessageType.Info, 6f, Msg.BtnShowAllParts, ShowAllParts);
        }

        void PaintAnywhere()
        {
            _brush.stayInsideIslands = false;
            Repaint();
        }

        HashSet<int> IslandsUnderStroke(PaintStroke s)
        {
            var set = new HashSet<int>();
            int step = Mathf.Max(1, s.points.Count / 24);
            for (int i = 0; i < s.points.Count; i += step)
                foreach (var isl in _uv.IslandsAtUV(new Vector2(s.points[i].x, s.points[i].y))) set.Add(isl);
            return set;
        }

        void CancelStroke()
        {
            _liveStroke = null;
            _liveLayer = null;
            _liveMaskLayer = null;
            _liveMirror = null;
            if (_comp != null) { _comp.LiveStroke = null; _comp.LiveMirror = null; }
        }

        /// <summary>
        /// Blur / Smudge need a Normal Paint layer (their result is "what you see, softened", laid on top). Use the selected
        /// one if it fits, otherwise add a "Blending" layer above it.
        /// </summary>
        PaintLayer EnsureBlendLayer()
        {
            if (ActiveLayer is PaintLayer p && p.blend == BlendMode.Normal)
            {
                if (!p.enabled) { Undo.RecordObject(_recipe, "Show layer"); p.enabled = true; }
                return p;
            }
            var layer = new PaintLayer { name = "Blending" };
            InsertLayer(layer, "Add blending layer");
            Toast(Loc.T("Added a “Blending” layer for blur and smudge, so the original stays untouched. Hide or delete it to undo the blending."), MessageType.Info, 5f);
            return layer;
        }

        PaintLayer EnsurePaintLayer()
        {
            if (ActiveLayer is PaintLayer p)
            {
                if (!p.enabled) { Undo.RecordObject(_recipe, "Show layer"); p.enabled = true; }
                return p;
            }
            var layer = new PaintLayer();
            InsertLayer(layer, "Add paint layer");
            return layer;
        }

        void RememberColour(Color c)
        {
            c.a = 1;
            _recentColors.RemoveAll(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
            _recentColors.Insert(0, c);
            if (_recentColors.Count > 10) _recentColors.RemoveRange(10, _recentColors.Count - 10);
        }

        /// <summary>Adds a layer above the active one (or on top). It takes the current island selection as its mask.</summary>
        void InsertLayer(Layer layer, string undoName = "Add layer")
        {
            Undo.RecordObject(_recipe, undoName);
            Undo.RecordObject(this, undoName);
            if (_selection.Count > 0)
            {
                layer.maskMode = MaskMode.SelectedIslands;
                layer.islands = new List<int>(_selection);
            }
            int idx = ActiveIndex;
            if (idx < 0) _recipe.layers.Add(layer);
            else _recipe.layers.Insert(idx + 1, layer);
            _activeLayerId = layer.id;
            if (!(layer is PaintLayer)) _openLayerSettings = true; // a new adjustment layer shows its controls
            MarkEdited();
            Learn.Did(LearnEvents.LayerAdded);
            Learn.Did(LearnEvents.LayerAddedOf(layer.GetType().Name));
            Repaint(); // may be called from a GenericMenu callback, outside OnGUI
        }

        void MoveLayer(int i, int dir)
        {
            int j = i + dir;
            if (i < 0 || j < 0 || j >= _recipe.layers.Count) return;
            Undo.RecordObject(_recipe, "Move layer");
            (_recipe.layers[i], _recipe.layers[j]) = (_recipe.layers[j], _recipe.layers[i]);
            MarkEdited();
        }

        void RemoveLayer(int i)
        {
            if (i < 0 || i >= _recipe.layers.Count) return;
            EndStroke();
            Undo.RecordObject(_recipe, "Delete layer");
            Undo.RecordObject(this, "Delete layer");
            _recipe.layers.RemoveAt(i);
            var next = _recipe.layers.Count == 0 ? null : _recipe.layers[Mathf.Clamp(i - 1, 0, _recipe.layers.Count - 1)];
            _activeLayerId = next?.id;
            MarkEdited();
        }

        void DuplicateLayer(int i)
        {
            if (i < 0 || i >= _recipe.layers.Count) return;
            var copy = Instantiate(_recipe); // deep copy through Unity serialisation (keeps gradients and strokes)
            var dup = copy.layers[i];
            DestroyImmediate(copy);
            dup.id = Guid.NewGuid().ToString("N");
            dup.name += " copy";
            Undo.RecordObject(_recipe, "Duplicate layer");
            Undo.RecordObject(this, "Duplicate layer");
            _recipe.layers.Insert(i + 1, dup);
            _activeLayerId = dup.id;
            MarkEdited();
        }

        /// <summary>Adds a preset's layers (bottom first) above the selected layer as one undo step.</summary>
        void ApplyLayerPreset(List<Layer> layers, string name)
        {
            if (layers == null || layers.Count == 0) return;
            EndStroke();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (var l in layers) InsertLayer(l, "Add preset");
            Undo.SetCurrentGroupName("Add preset " + name);
            Undo.CollapseUndoOperations(group);
            Toast(Loc.F("Added the “{0}” look ({1} layer{2})", name, layers.Count, layers.Count == 1 ? "" : "s") +
                  (_selection.Count > 0 ? Loc.F(", limited to the {0} selected piece(s).", _selection.Count) : Loc.T(". Tweak it in the layer settings below.")),
                MessageType.Info, 4f);
        }

        void CaptureSelection(Layer layer)
        {
            Undo.RecordObject(_recipe, "Set layer mask");
            layer.maskMode = MaskMode.SelectedIslands;
            layer.islands = new List<int>(_selection);
            MarkEdited();
        }

        void ClearPaint(PaintLayer layer)
        {
            Undo.RecordObject(_recipe, "Clear paint");
            layer.strokes.Clear();
            MarkEdited();
        }

        /// <summary>Opening a saved edit replaces the layer stack: ask first if there are unbaked layers.</summary>
        bool ConfirmReplaceLayers(TextureRecipe source)
        {
            if (_recipe == null || _recipe.layers.Count == 0 || !_unsaved) return true;
            int n = _recipe.layers.Count;
            return StudioDialog.AskComplex(Loc.T("Replace current layers?"), Msg.M8ReplaceLayers(source.name, n),
                Msg.BtnReplace, Msg.BtnCancel, null) == 0;
        }

        void LoadRecipe(TextureRecipe source)
        {
            EndStroke();
            Undo.RecordObject(_recipe, "Load saved edit");
            var copy = Instantiate(source);
            _recipe.layers = copy.layers;
            DestroyImmediate(copy);
            foreach (var l in _recipe.layers) if (l != null) l.id = Guid.NewGuid().ToString("N");
            _activeLayerId = _recipe.layers.LastOrDefault()?.id;
            MarkEdited();
            _unsaved = false; // it's already saved on disk
            Toast(Loc.F("Opened saved edit “{0}” ({1} layer{2}).", source.name, _recipe.layers.Count, (_recipe.layers.Count == 1 ? "" : "s")), MessageType.Info, 4f);
            if (_tab == Tab.Target) SetTab(Tab.Paint); // where the layers are
            Learn.Did(LearnEvents.EditReopened);
        }

        /// <summary>Plain-language description of what Save/Bake will write, with the predicted file names.</summary>
        string BakeSummary(TextureSlot slot)
        {
            var p = Exporter.PredictPaths(slot, _material, _outputMode);
            string s = Loc.F("Save your edit? You'll get: a new image ({0}, {1} px)", System.IO.Path.GetFileName(p.pngPath), _comp?.SourceWidth ?? 0);
            switch (_outputMode)
            {
                case OutputMode.DuplicateMaterial:
                    s += Loc.F(", a copy of the material ({0}) that your avatar will use on {1} slot(s)", System.IO.Path.GetFileName(p.materialPath), _users.Count);
                    break;
                case OutputMode.UpdateOriginalMaterial:
                    s += Loc.F(", \"{0}\" changed to use it (other avatars using this material change too)", _material.name);
                    break;
                default:
                    s += Loc.T(" (your avatar won't change)");
                    break;
            }
            s += Loc.F(", and a saved edit ({0}) so you can continue later.", System.IO.Path.GetFileName(p.recipePath));
            if (_alsoUsers.Count > 0 && _outputMode != OutputMode.PngOnly)
            {
                var mats = _alsoUsers.Select(u => u.material).Distinct().ToList();
                string names = string.Join(", ", mats.Take(3).Select(m => $"\"{m.name}\""));
                if (mats.Count > 3) names = Loc.F("{0} and {1} more", names, mats.Count - 3);
                bool one = mats.Count == 1;
                s += _outputMode == OutputMode.DuplicateMaterial
                    ? (one ? Loc.F("\n\nThe included parts get the new texture too: {0} is copied the same way.", names)
                           : Loc.F("\n\nThe included parts get the new texture too: {0} are copied the same way.", names))
                    : (one ? Loc.F("\n\nThe included parts get the new texture too: {0} is changed to use it.", names)
                           : Loc.F("\n\nThe included parts get the new texture too: {0} are changed to use it.", names));
            }
            string shared = SharedMaterialNote();
            if (shared != null) s += "\n\n" + System.Text.RegularExpressions.Regex.Replace(shared, "</?b>", "");
            return s + (_outputMode == OutputMode.PngOnly ? Loc.T("\n\nYour original texture is not changed.")
                                                          : Loc.T("\n\nYour original texture is not changed. Ctrl+Z puts the material back."));
        }

        void Bake()
        {
            EndStroke();
            var slot = CurrentSlot;
            if (slot == null || _material == null) return;
            // M3 is the standard wording; it only fits the default copy-the-material save at 2048 px with nothing else to say.
            bool plain = _outputMode == OutputMode.DuplicateMaterial && _alsoUsers.Count == 0 && SharedMaterialNote() == null && (_comp?.SourceWidth ?? 0) == 2048;
            string body = plain ? Msg.M3BakeConfirm(System.IO.Path.GetFileNameWithoutExtension(Exporter.PredictPaths(slot, _material, _outputMode).pngPath)) : BakeSummary(slot);
            if (!StudioDialog.Ask(Loc.T("Save your edit"), body, Msg.BtnSave, Msg.BtnCancel)) return;

            _preview.Clear();
            _saving = true;
            try
            {
                var res = Exporter.Bake(slot, _material, _users, _recipe, _uv, _outputMode, _dither, _alsoUsers,
                    (step, f) => EditorUtility.DisplayCancelableProgressBar(Loc.T("Avistry"), $"{step} ({slot.texture.name})", f));
                if (res.pngPath == null)
                {
                    Toast(Loc.T("Save cancelled. Nothing was written."), MessageType.Info, 4f);
                    return;
                }
                _lastBakePng = res.pngPath;
                _lastBakeMaterial = res.materialPath;
                _lastBakeRecipe = res.recipePath;
                _lastBakeClearedLayers = _outputMode != OutputMode.PngOnly;
                _unsaved = false;
                _savedBlockVisible = true;
                Learn.Did(LearnEvents.SaveDone);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(res.pngPath));
                if (_lastBakeClearedLayers)
                {
                    // The edit now lives in the new texture: start a fresh stack on top of it.
                    Undo.RecordObject(_recipe, "Bake");
                    ClearHistory(); // the edit now lives in the new texture
                    _recipe.layers.Clear();
                    _activeLayerId = null;
                }
                _tab = Tab.Save;
                string saved = Msg.M1Saved(System.IO.Path.GetFileNameWithoutExtension(res.pngPath), _outputMode == OutputMode.PngOnly);
                if (res.paddingWarning != null) saved += "\n" + res.paddingWarning;
                string pngPath = res.pngPath;
                Toast(saved, MessageType.Info, 5f, Msg.BtnShowFile, () => ShowFileAndLearn(pngPath));
            }
            catch (Exception e)
            {
                _lastBakePng = null;
                EditorUtility.ClearProgressBar(); // not behind the error dialog
                ShowErrorDialog(Msg.M2SaveFailed(), e);
            }
            finally
            {
                _saving = false;
                EditorUtility.ClearProgressBar();
                _needsRebuild = true;
                Repaint();
            }
        }
    }
}
