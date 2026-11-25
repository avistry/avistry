using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Avistry.Textures
{
    [MovedFrom(false, sourceNamespace: "AvatarTextureStudio", sourceAssembly: "AvatarTextureStudio.Editor")]
    public partial class AvatarTextureWindow : EditorWindow
    {
        enum Tool { Select = 0, Paint = 1 }

        const float MinPanelWidth = 260f;
        const float MinCanvasWidth = 280f;
        static readonly int[] WorkSizes = { 512, 1024, 2048, 4096 };

        [SerializeField] Renderer _renderer;
        [SerializeField] string _rendererId;
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
        [SerializeField] bool _foldHelp;
        [SerializeField] bool _foldBrushAdvanced;
        [SerializeField] bool _focusWhilePainting = true;
        [SerializeField] float _panelWidth = 300f;
        [SerializeField] float _canvasHeightFraction = 0.45f;
        [SerializeField] string _newBrushName = "";

        Material _material;
        Renderer _builtRenderer;
        Mesh _builtTargetMesh;
        int _builtMaterialIndex, _builtSlotIndex;
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

        enum Tab { Target = 0, Select = 1, Paint = 2, Layers = 3, Save = 4, Colour = 5 }
        [SerializeField] Tab _tab = Tab.Target;
        [SerializeField] bool _unsaved;
        [SerializeField] bool _foldMoreBrushes, _foldOutputOptions;
        [SerializeField] string _lastBakePng, _lastBakeMaterial, _lastBakeRecipe, _lastSavedEdit;
        [SerializeField] bool _lastBakeClearedLayers;
        Texture _pendingTexture;
        TextureRecipe _editThatMadeTexture;
        string _transparencyNote;
        bool _eyedropperArmed;
        bool _scenePickArmed;

        PaintStroke _liveStroke;
        PaintLayer _liveLayer;
        Layer _liveMaskLayer;
        int _strokeUndoGroup;

        [SerializeField] string _maskEditLayerId;
        [SerializeField] bool _maskFillHides = true;
        [SerializeField] bool _showMaskOverlay = true;

        bool MaskSelectedForEditing => !string.IsNullOrEmpty(_maskEditLayerId) && _maskEditLayerId == _activeLayerId &&
            ActiveLayer != null && ActiveLayer.hasPaintMask;
        bool MaskEditing => _tool == Tool.Paint && MaskSelectedForEditing;

        TextureSlot CurrentSlot => _slots != null && _slotIndex >= 0 && _slotIndex < _slots.Count ? _slots[_slotIndex] : null;
        int ActiveIndex => _recipe == null ? -1 : _recipe.layers.FindIndex(l => l != null && l.id == _activeLayerId);
        Layer ActiveLayer { get { int i = ActiveIndex; return i >= 0 ? _recipe.layers[i] : null; } }

        [MenuItem("Tools/Avistry/Textures")]
        public static void Open() => Open(true);

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

        Renderer SuggestedRenderer
        {
            get
            {
                var r = Selection.activeGameObject != null ? ResolveRenderer(Selection.activeGameObject) : null;
                return r != _renderer ? r : null;
            }
        }

        void OnSelectionChange() => Repaint();

        internal static string Version => UpdateCheck.InstalledVersion();

        void OnEnable()
        {
            RefreshWindowTitle();
            SupportSession.Open(this);
            wantsMouseMove = true;
            HookUpdateCheck();
            saveChangesMessage = Loc.T("Avistry has layers you haven't saved yet.\n\n" +
                                 "Save keeps them as a draft you can reopen from Browse drafts.");
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
            ClearHistory();
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
            ObserveEditorActivity();
            EditorApplication.update += ObserveEditorActivity;
        }

        void OnDisable()
        {
            SupportSession.Close(this);
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            UpdateCheck.Changed -= Repaint;
            SceneView.duringSceneGui -= OnSceneGUI;
            ReleaseSceneBar();
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoEvent -= OnUndoRedoEvent;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload -= Teardown;
            EditorApplication.update -= ObserveEditorActivity;
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
            _pixelJob?.Dispose();
            StopOcclusionBake();
            ClearPreparedInteraction();
            ForgetSlotIsolation(true);
            CancelMixedMergePreview();
            CancelGradientDrag();
            CancelSceneGradient();
            CancelImageDrag();
            ClearSceneStrokeControl();
            CancelStroke();
            _cancelledSceneStrokePreview = null;
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
            _recallSelection = false;
            bool interruptedSceneStroke = _sceneStrokeDraft != null;
            if (interruptedSceneStroke) CancelStroke();
            _comp?.InvalidateLivePrefix();
            _comp?.InvalidateLiveStrokeCoverage();
            CancelMixedMergePreview();
            CancelGradientDrag();
            CancelSceneGradient();
            CancelImageDrag();
            if (AlsoSignature() != _alsoSignature) _needsRebuild = true;
            if (_renderer != _builtRenderer || _materialIndex != _builtMaterialIndex || _slotIndex != _builtSlotIndex)
                _needsRebuild = true;
            if (PreviewSizeChanged()) _needsRebuild = true;
            if (_renderer != null && !_needsRebuild)
            {
                var slotMats = _renderer.sharedMaterials;
                var material = _materialIndex >= 0 && _materialIndex < slotMats.Length ? slotMats[_materialIndex] : null;
                var slot = CurrentSlot;
                if (material != _material || (material != null && slot != null &&
                    slot.properties.Any(prop => !material.HasProperty(prop) || material.GetTexture(prop) != slot.texture)))
                    _needsRebuild = true;
            }
            _dirty = true;
            _savedBlockVisible = false;
            _emptyMasks = null;
            _selectionVisualsDirty = true;
            _animatedSettings = null;
            if (_renderer != null) Learn.Did(LearnEvents.UndoUsed);
            if (interruptedSceneStroke) _cancelledSceneStrokePreview = _comp;
            Repaint();
        }

        public override void SaveChanges()
        {
            EndStroke();
            if (_recipe == null || _recipe.layers.Count == 0 || (SaveEditAsset() != null && !_unsaved)) base.SaveChanges();
        }

        void MarkEdited(int? historyGroup = null)
        {
            RefreshEditedRecipe();
            RecordHistory(historyGroup);
        }

        void RefreshEditedRecipe(bool affectsImage = true)
        {
            _unsaved = true;
            _savedBlockVisible = false;
            if (affectsImage)
            {
                _comp?.InvalidateLivePrefix();
                ShowEditsIfBeforeOn();
                _dirty = true;
            }
            Repaint();
        }

        bool ConfirmTargetChange(string what)
        {
            if (!PlanTargetChange(what, out var apply)) return false;
            apply();
            return true;
        }

        bool PlanTargetChange(string what, out Action apply)
        {
            apply = () => _savedBlockVisible = false;
            if (_recipe == null || _recipe.layers.Count == 0) return true;
            int n = _recipe.layers.Count;
            string tex = CurrentSlot != null ? CurrentSlot.texture.name : Loc.T("this texture");
            int c = StudioDialog.AskComplex(Loc.F("Switch {0}?", what), Msg.M4TargetChange(n, tex, _unsaved),
                Msg.BtnMoveLayers, Msg.BtnCancel, Msg.BtnStartEmpty);
            if (c == 1) return false;
            if (c == 2)
                apply = () =>
                {
                    Undo.RecordObject(_recipe, "Start fresh");
                    Undo.RecordObject(this, "Start fresh");
                    _savedBlockVisible = false;
                    _recipe.layers.Clear();
                    _activeLayerId = null;
                    _unsaved = false;
                };
            return true;
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
            RememberSelectionBeforeSwitch();
            Undo.RecordObject(this, "Change target");
            _renderer = r;
            _rendererId = r != null ? GlobalObjectId.GetGlobalObjectIdSlow(r).ToString() : null;
            _materialIndex = 0;
            _slotIndex = 0;
            _slotChosenByUser = false;
            _savedBlockVisible = false;
            _selection.Clear();
            ClearFaceSelection();
            _alsoRenderers.Clear();
            _alsoSlots.Clear();
            _alsoUvs.Clear();
            _needsRebuild = true;
            if (r != null) LearnTargetPicked(r);
        }

        void Rebuild() => RebuildUsing(null);

        void RebuildUsing(MeshUV prepared)
        {
            _pixelJob?.Cancel();
            ClearPreparedInteraction();
            ReleaseSceneSelectionCaches();
            ForgetSlotIsolation(true);
            CancelMixedMergePreview();
            _pickForPaint = false;
            CancelGradientDrag();
            CancelSceneGradient();
            CancelImageDrag();
            var oldRenderer = _builtRenderer;
            var oldMesh = _builtTargetMesh;
            var oldMaterial = _material;
            var oldSlot = _builtSlotIndex >= 0 && _builtSlotIndex < _slots.Count ? _slots[_builtSlotIndex] : null;
            _needsRebuild = false;
            CancelStroke();
            _preview.Clear();
            _comp?.Dispose();
            _comp = null;
            _uv?.ReleaseSurfaceMesh();
            _uv = null;
            _builtRenderer = _renderer;
            _builtTargetMesh = _renderer != null ? MeshOf(_renderer) : null;
            _builtMaterialIndex = _materialIndex;
            _builtSlotIndex = _slotIndex;
            _material = null;
            _users.Clear();
            _alsoUsers.Clear();
            _editThatMadeTexture = null;
            _slots = new List<TextureSlot>();
            _status = "";
            _statusFix = StatusFix.None;
            try
            {
                if (_renderer == null) return;
                var mats = _renderer.sharedMaterials;
                _materialIndex = Mathf.Clamp(_materialIndex, 0, Mathf.Max(0, mats.Length - 1));
                _builtMaterialIndex = _materialIndex;
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
                if (!_slotChosenByUser)
                {
                    int ci = _slots.FindIndex(s => s.role == TextureRole.Color);
                    if (ci >= 0) _slotIndex = ci;
                }
                _slotIndex = Mathf.Clamp(_slotIndex, 0, _slots.Count - 1);
                _builtSlotIndex = _slotIndex;
                var slot = CurrentSlot;
                _editThatMadeTexture = FindEditThatMade(slot.texture);
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
                var includedSlots = ResolveIncludedMeshes(root, slot);
                _uv = prepared ?? MeshUV.Build(includedSlots, slot.uvChannel);
                if (_uv == null) { _status = Msg.M25SlotStatus(true); _statusFix = StatusFix.PickAnotherMesh; }
                else ClaimSceneBar();
                RecallSelectionAfterSwitch(slot);
                _selection.RemoveAll(i => _uv == null || i >= _uv.islandCount);

                bool prepareBrush = CanPreparePickedBrush(slot) && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
                _comp = new Compositor(slot, _workSize, prepareBrush ? _uv : null, _preparationPacing.Busy);
                LearnTextureLoaded();
                _recipe.sourceTexture = slot.texture;
                _recipe.sourceMaterial = _material;
                _recipe.materialIndex = _materialIndex;
                _recipe.rendererPath = root != null ? AnimationUtility.CalculateTransformPath(_renderer.transform, root) : _renderer.name;
                _zoom = 0;
                if (slot.role == TextureRole.Normal && _tool == Tool.Paint) _tool = Tool.Select;
                if (slot.role == TextureRole.Normal && (_tab == Tab.Paint || _tab == Tab.Layers)) _tab = Tab.Target;
                RebuildSelectionVisuals();
                _dirty = true;
            }
            catch (Compositor.PreviewShaderException e)
            {
                _statusFix = StatusFix.RetryPreview;
                _status = Loc.T("The texture preview shader didn't load. Check the Console for shader errors. If Avistry files are missing, reinstall Avistry.");
                Debug.LogWarning(e.Message);
            }
            catch (Exception e)
            {
                _status = Loc.F("Error: {0}", e.Message);
                Debug.LogException(e);
                _comp?.Dispose();
                _comp = null;
            }
            finally
            {
                if (prepared != null && prepared != _uv) prepared.ReleaseSurfaceMesh();
                if (!ReferenceEquals(oldRenderer, _renderer) || oldMesh != _builtTargetMesh || oldMaterial != _material ||
                    oldSlot?.texture != CurrentSlot?.texture)
                    ClearHistory();
            }
        }

        static Dictionary<(string guid, long localId), string> s_bakedReceipts;
        static int s_bakedReceiptStamp;

        static TextureRecipe FindEditThatMade(Texture tex)
        {
            if (tex == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex, out string guid, out long localId)) return null;
            string path = AssetDatabase.GetAssetPath(tex);
            TextureRecipe nearby = null;
            if (!string.IsNullOrEmpty(path))
            {
                string guess = $"{System.IO.Path.GetDirectoryName(path).Replace('\\', '/')}/{tex.name}_recipe.asset";
                nearby = AssetDatabase.LoadAssetAtPath<TextureRecipe>(guess);
                if (nearby != null && nearby.bakedTexture == tex) return nearby;
            }

            if (s_bakedReceipts == null || s_bakedReceiptStamp != _envStamp) IndexBakedReceipts();
            var key = (guid, localId);
            if (s_bakedReceipts.TryGetValue(key, out string receiptGuid))
            {
                var receipt = AssetDatabase.LoadAssetAtPath<TextureRecipe>(AssetDatabase.GUIDToAssetPath(receiptGuid));
                if (receipt != null && receipt.bakedTexture == tex) return receipt;
                s_bakedReceipts.Remove(key);
            }
            return nearby != null && nearby.bakedTexture == null &&
                System.Text.RegularExpressions.Regex.IsMatch(tex.name, @"_ats(_\d+)?$") ? nearby : null;
        }

        static void IndexBakedReceipts()
        {
            var index = new Dictionary<(string guid, long localId), string>();
            foreach (var receiptGuid in AssetDatabase.FindAssets("t:TextureRecipe"))
            {
                var receipt = AssetDatabase.LoadAssetAtPath<TextureRecipe>(AssetDatabase.GUIDToAssetPath(receiptGuid));
                if (receipt == null || receipt.bakedTexture == null) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(receipt.bakedTexture, out string guid, out long localId))
                    index.TryAdd((guid, localId), receiptGuid);
            }
            s_bakedReceipts = index;
            s_bakedReceiptStamp = _envStamp;
        }

        void OpenSavedEdit(TextureRecipe r)
        {
            if (r == null) return;
            var slot = CurrentSlot;
            if (slot == null || _comp == null)
            {
                Toast(Loc.T("Pick the part first, then open the edit."), MessageType.Info);
                return;
            }

            EndStroke();
            var stillMatches = CaptureSavedEditContext(r, slot);
            if (!CheckSavedEditContext(stillMatches)) return;
            if (r.includedLayout != null)
            {
                OpenIncludedEdit(r, _renderer, _materialIndex, stillMatches);
                return;
            }
            bool canOpenHere = slot.role != TextureRole.Normal;
            if (r.sourceTexture == null || slot.texture == r.sourceTexture)
            {
                if (!canOpenHere) ReopenNeedsEditableTexture();
                else if (r.sourceTexture == null && IsProjectDraft(r, AssetDatabase.GetAssetPath(r)))
                    OpenDraftWithReplacement(r, slot, stillMatches);
                else if (ConfirmReplaceSavedEdit(r, stillMatches)) LoadRecipeOnCurrentTarget(r, asCopy: r.sourceTexture == null);
                return;
            }

            bool isBake = r.bakedTexture == slot.texture || r == _editThatMadeTexture;
            bool CanRestoreMaterial() => r.sourceMaterial == null || r.sourceMaterial == _material ||
                TargetResolver.GetTextureSlots(r.sourceMaterial).Any(s => s.texture == r.sourceTexture);
            if (isBake && IsMaterialFromBake(r) && CanRestoreMaterial())
            {
                string body = Msg.M7OpenSavedEdit(r.sourceTexture.name, slot.texture.name);
                string back = $"{Msg.BtnGoBack} \"{r.sourceTexture.name}\"";
                int c = canOpenHere ? StudioDialog.AskComplex(Loc.T("Open saved edit"), body, back, Msg.BtnCancel, Msg.BtnOpenHere)
                    : StudioDialog.Ask(Loc.T("Open saved edit"), body, back, Msg.BtnCancel) ? 0 : 1;
                bool RestoreContextStillMatches()
                {
                    if (!stillMatches()) return false;
                    if (c == 2) return true;
                    if (!_users.SequenceEqual(TargetResolver.FindMaterialUsers(TargetResolver.AvatarRoot(_renderer), _material)))
                    {
                        _needsRebuild = true;
                        return false;
                    }
                    return CanRestoreMaterial();
                }
                if (c == 1 || !ConfirmReplaceSavedEdit(r, RestoreContextStillMatches)) return;
                if (c == 2) LoadRecipeOnCurrentTarget(r, asCopy: true);
                else RestoreOriginalAndOpen(r);
                return;
            }

            int sourceIndex = _slots.FindIndex(s => s.texture == r.sourceTexture && s.role != TextureRole.Normal);
            if (sourceIndex >= 0) OpenSavedEditOnSource(r, slot, sourceIndex, stillMatches);
            else if (!canOpenHere) ReopenNeedsEditableTexture();
            else if (StudioDialog.Ask(Loc.T("Open saved edit"),
                Loc.F("This edit was made for ‘{0}’. Apply its layers to ‘{1}’?", r.sourceTexture.name, slot.texture.name),
                Msg.BtnOpenHere, Msg.BtnCancel) && ConfirmReplaceSavedEdit(r, stillMatches)) LoadRecipeOnCurrentTarget(r, asCopy: true);
        }

        bool IsMaterialFromBake(TextureRecipe recipe)
        {
            if (_material == null) return false;
            if (_material == recipe.sourceMaterial || _material == recipe.bakedMaterial) return true;
            if (recipe.bakedMaterial != null || recipe.sourceMaterial == null || !Exporter.IsToolCopy(_material)) return false;

            string sourceDir = Exporter.WritableFolder(AssetDatabase.GetAssetPath(recipe.sourceMaterial), false);
            string materialPath = AssetDatabase.GetAssetPath(_material);
            return System.IO.Path.GetDirectoryName(materialPath)?.Replace('\\', '/') == sourceDir &&
                Exporter.StripAts(_material.name) == Exporter.StripAts(recipe.sourceMaterial.name);
        }

        void RestoreOriginalAndOpen(TextureRecipe r) => WithTargetUndo("Switch back to original texture", () =>
        {
            Undo.RecordObject(this, "Switch back to original texture");
            var originalMaterial = _material;
            if (r.sourceMaterial != null && r.sourceMaterial != _material)
            {
                originalMaterial = r.sourceMaterial;
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
                Undo.RecordObject(_material, "Restore original texture");
                foreach (var p in CurrentSlot.properties) _material.SetTexture(p, r.sourceTexture);
                EditorUtility.SetDirty(_material);
            }
            var originalSlots = TargetResolver.GetTextureSlots(originalMaterial);
            int sourceIndex = originalSlots.FindIndex(s => s.texture == r.sourceTexture);
            _selection.Clear();
            ClearFaceSelection();
            LoadRecipe(r);
            if (sourceIndex >= 0) { _slotIndex = sourceIndex; _slotChosenByUser = true; }
            _needsRebuild = true;
        });

        void ComposeNow()
        {
            if (_sceneStrokeDraft != null && !SceneStrokeTargetCurrent(_sceneStrokeDraft)) CancelStroke();
            _dirty = false;
            _pickerCircleKey = null;
            if (_comp == null) return;
            var slot = CurrentSlot;
            if (slot.role == TextureRole.Normal) _comp.Compose(Array.Empty<Layer>(), _uv);
            else
            {
                var layers = MixedMergePreviewLayers() ?? LayersForPreview();
                var liveLayer = _liveMaskLayer ?? (Layer)_liveLayer;
                if (_sceneStrokeDraft != null && ReferenceEquals(layers, _recipe.layers))
                    ComposeSceneStrokePreview(liveLayer);
                else if (_liveStroke != null && ReferenceEquals(layers, _recipe.layers))
                    _comp.ComposeLivePreview(layers, _uv, liveLayer, _envStamp);
                else _comp.Compose(layers, _uv);
            }
            _sceneGradientPose = _uv?.PoseVersion ?? 0;

            if (_previewOnAvatar && _users.Count + _alsoUsers.Count > 0)
                _preview.Apply(PreviewSlots(slot), slot.texture, DisplayingOriginal ? _comp.SourcePreview : _comp.Preview);
            else _preview.Clear();
            Repaint();
        }

        void OnLostFocus()
        {
            _panning = false;
            CancelImageDrag();
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.Layout) _drawEditorActivity = _editorActivity;
            if (e.type == EventType.Layout && ((_renderer == null && _material != null) ||
                (CurrentSlot != null && CurrentSlot.texture == null))) _needsRebuild = true;
            if (_needsRebuild && e.type == EventType.Layout && !WaitingForPreview) Rebuild();
            if (_imgDrag != null && (SelectedImage != _imgOriginal || GUIUtility.hotControl != _imgDragControl)) CancelImageDrag();
            if (_selectionVisualsDirty && e.type == EventType.Layout) { _selectionVisualsDirty = false; RebuildSelectionVisuals(); }
            if (_dirty && e.type == EventType.Layout) ComposeNow();
            HandleShortcuts(e);
            ReleaseGradientDragControl();
            ReleaseImageDragControl();
            LearnToastInput(e);
            LearnPulse.Latch();

            const float toolbarH = 21f;
            var toolbar = new Rect(0, 0, position.width, toolbarH);
            float bodyH = position.height - toolbarH;
            Rect canvas, panel, splitter;
            bool sideBySide = position.width >= MinPanelWidth + MinCanvasWidth;
            if (sideBySide)
            {
                float pw = Mathf.Clamp(_panelWidth, MinPanelWidth, position.width - MinCanvasWidth);
                canvas = new Rect(0, toolbarH, position.width - pw, bodyH);
                panel = new Rect(canvas.xMax + 1, toolbarH, pw - 1, bodyH);
                splitter = new Rect(canvas.xMax - 3, toolbarH, 7, bodyH);
            }
            else
            {
                float h = Mathf.Clamp(bodyH * _canvasHeightFraction, 140f, Mathf.Max(140f, bodyH - 160f));
                canvas = new Rect(0, toolbarH, position.width, h);
                panel = new Rect(0, canvas.yMax + 1, position.width, bodyH - h - 1);
                splitter = new Rect(0, canvas.yMax - 3, position.width, 7);
            }

            DrawToolbar(toolbar);
            HandleSplitter(splitter, sideBySide, toolbarH, bodyH);
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

        static bool CanUseShortcuts(Event e) =>
            e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField && !e.control && !e.command && !e.alt;

        void HandleShortcuts(Event e)
        {
            if (!CanUseShortcuts(e) || HandlePaintToolShortcut(e)) return;
            switch (e.keyCode)
            {
                case KeyCode.Escape when PickingForPaint && !_marqueeActive: SetPickForPaint(false); e.Use(); break;
                case KeyCode.S: SetTool(Tool.Select); e.Use(); break;
                case KeyCode.X: SwapColours(); e.Use(); break;
                case KeyCode.LeftBracket: ResizeBrush(1f / 1.2f); e.Use(); break;
                case KeyCode.RightBracket: ResizeBrush(1.2f); e.Use(); break;
                case KeyCode.I when CanvasPainting:
                    _eyedropperArmed = !_eyedropperArmed;
                    if (_eyedropperArmed) Toast(Loc.T("Eyedropper: click the texture or the model to pick a colour."), MessageType.Info, 3f);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        bool HandlePaintToolShortcut(Event e, SceneView scene = null)
        {
            int index = e.keyCode switch
            {
                KeyCode.B => 0,
                KeyCode.E => 1,
                KeyCode.G => e.shift ? 3 : 2,
                KeyCode.J => 4,
                _ => -1,
            };
            if (index < 0) return false;
            bool texturePlacement = _gradDrag != null || _imgDrag != null;
            if (_liveStroke != null || _marqueeActive || (scene != null && (texturePlacement || _sceneGradientDraft != null)))
            {
                e.Use();
                return true;
            }
            if (GUIUtility.hotControl != 0 && !texturePlacement) return false;

            CanPaint(out string problem);
            if (problem == null && index >= 3 && MaskSelectedForEditing)
                problem = Loc.T("Finish editing the mask before using Gradient or Blend.");
            if (problem != null)
            {
                Toast(problem, MessageType.Info, 4f);
                if (scene != null) scene.ShowNotification(new GUIContent(problem), 3.0);
            }
            else if (_tab != Tab.Paint || _tool != Tool.Paint || PaintToolIndex != index || texturePlacement || PickingForPaint)
            {
                SetTool(Tool.Paint);
                SetPaintTool(index);
            }
            e.Use();
            return true;
        }

        void SetTab(Tab t)
        {
            if (!TabAvailable(t, out var why)) { Toast(why, MessageType.Warning); return; }
            if (_tab == Tab.Paint && t != Tab.Paint)
            {
                EndStroke();
                _eyedropperArmed = false;
                SetPickForPaint(false);
            }
            if (t != _tab)
            {
                if (t == Tab.Colour)
                    _paintLayerBeforeRecolour = ActiveLayer is PaintLayer paint ? paint.id : null;
                else if (_tab == Tab.Colour)
                    RestorePaintLayerAfterRecolour();
                CancelGradientDrag();
                CancelImageDrag();
                CancelMixedMergePreview();
            }
            if (t != Tab.Paint) CancelSceneGradient();
            _tab = t;
            if (t == Tab.Select) SetTool(Tool.Select);
            else if (t == Tab.Paint) SetTool(Tool.Paint);
            else if (t == Tab.Colour && ByPart && _tool == Tool.Paint) SetTool(Tool.Select);
            if (_highlightedColourPart != VisibleColourPart) RebuildSelectionVisuals();
            GUI.FocusControl(null);
            _panelScroll = Vector2.zero;
            Repaint();
        }

        bool TabAvailable(Tab t, out string why)
        {
            why = null;
            if (t == Tab.Target) return true;
            if (WaitingForPreview) { why = PreviewWaitText; return false; }
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
            if (t == Tool.Select) SetPickForPaint(false);
            if (_tab == Tab.Colour && t == Tool.Paint) { SetTab(Tab.Paint); return; }
            if (t == Tool.Paint) _tab = Tab.Paint;
            else if (_tab == Tab.Paint) _tab = Tab.Select;
            if (_tool == t) { Repaint(); return; }
            ClearSceneTopologyFeedback();
            CancelSceneGradient();
            EndStroke();
            CancelGradientDrag();
            CancelImageDrag();
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
            ClearFaceSelection();
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
            ClearFaceSelection();
            _selection = islands.Distinct().ToList();
            RebuildSelectionVisuals();
            if (_selection.Count > 0) Learn.Did(LearnEvents.PiecesSelected);
            Repaint();
        }

        void RebuildSelectionVisuals()
        {
            _highlightedColourPart = VisibleColourPart;
            _focusKey = null;
            _canvasSelectionPixelsDirty = _canvasSelectionWireDirty = true;
            if (_comp == null || _uv == null || HighlightElements.Count == 0)
            {
                _selSegments = Array.Empty<Vector3>();
                if (_selOverlay != null) DestroyImmediate(_selOverlay);
                _selOverlay = null;
                if (_uv == null || _comp == null) return;
            }
            if (_uv.PoseChanged()) _uv.RefreshPose();
            BuildSelectionHighlight();
            SceneView.RepaintAll();
        }

        bool _canvasSelectionPixelsDirty, _canvasSelectionWireDirty;

        void PrepareCanvasSelection(bool wire)
        {
            if (!_canvasSelectionPixelsDirty && (!wire || !_canvasSelectionWireDirty)) return;
            bool faces = HighlightUsesFaces;
            var set = new HashSet<int>(HighlightElements);
            if (wire && _canvasSelectionWireDirty)
            {
                _selSegments = faces ? _uv.SegmentsForTriangles(set.Contains) : _uv.SegmentsForIslands(set);
                _canvasSelectionWireDirty = false;
            }
            if (!_canvasSelectionPixelsDirty) return;
            int ow = Mathf.Min(1024, _comp.Width);
            int oh = Mathf.Max(1, Mathf.RoundToInt(_comp.Height * (ow / (float)_comp.Width)));
            if (_selOverlay == null || _selOverlay.width != ow || _selOverlay.height != oh)
            {
                if (_selOverlay != null) DestroyImmediate(_selOverlay);
                _selOverlay = new Texture2D(ow, oh, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            var color = new Color32(255, 140, 20, 90);
            _selOverlay.SetPixels32(faces ? _uv.FaceSelectionOverlay(ow, oh, set, color) : _uv.SelectionOverlay(ow, oh, set, color));
            _selOverlay.Apply(false);
            _canvasSelectionPixelsDirty = false;
        }

        static float Pressure(Event e) => BrushPointerInput.Pressure(e.pointerType, e.pressure);

        bool CanPaint(out string why)
        {
            why = null;
            if (WaitingForPreview) why = PreviewWaitText;
            else if (_comp == null) why = Loc.T("Pick a renderer and texture first.");
            else if (CurrentSlot?.role == TextureRole.Normal) why = Loc.T("Normal maps can't be painted.");
            return why == null;
        }

        static bool IsInsideTexture(Vector2 uv) => uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        string FillPlacementProblem(Vector2 uv, int hitPiece)
        {
            if (_uv == null) return Loc.T("Fill needs a UV map. Use the brush on this texture, or pick a part with a UV map.");
            if (!IsInsideTexture(uv)) return Loc.T("This point is outside the open texture. Click a piece inside the texture.");
            var pieces = _uv.IslandsAtUV(uv);
            if (hitPiece < 0 && pieces.Count == 0)
                return Loc.T("Click a piece of the texture to fill it. Empty space between pieces can't be filled.");

            int seedPiece = _comp.ScopedFillSeedPiece(_uv, uv);
            if (hitPiece >= 0 && seedPiece == hitPiece) return null;
            if (pieces.Contains(seedPiece))
                return hitPiece < 0 ? null : Loc.T("Two pieces share this spot. Fill the shared area from the texture view.");
            return Loc.T("No fill pixel on this piece. Click farther inside or raise Preview size in Advanced Pick settings.");
        }

        string BeginStroke(Vector2 uv, float pressure, int island, MeshUV.SurfaceHit? hit = null) =>
            BeginBrushStroke(uv, pressure, island, hit, fromScene: false);

        string BeginBrushStroke(Vector2 uv, float pressure, int island, MeshUV.SurfaceHit? hit, bool fromScene)
        {
            if (!CanPaint(out var why)) { Toast(why, MessageType.Warning); return why; }
            RequestPaintPreparation(fromScene);
            var active = ActiveLayer;
            if (ActiveFaceLimitNeedsUpdate(active))
            {
                why = Loc.T("This face limit does not match the current UV mesh. Pick new faces or choose Everything.");
                Toast(why, MessageType.Info, 6f, Loc.T("Pick faces"), () => PickFacesForLayer(active));
                return why;
            }
            PaintFillRegion fill = null;
            if (_brush.fillTool && PlanFill(uv, island, hit?.triangle ?? -1, out fill) is string problem)
            {
                bool pickSelection = FillScope == PaintFillScope.Selection && !HasFaceSelection && !_selection.Exists(IsCurrentPiece);
                Toast(problem, MessageType.Info, 6f, pickSelection ? FillSelectionAction : Msg.BtnUseBrush,
                    pickSelection ? (Action)PickFillSelection : () => SetPaintTool(0));
                return problem;
            }
            if (MaskEditing) { BeginMaskStroke(uv, pressure, island, hit, fill, fromScene); return null; }
            if (_brush.mode == BrushMode.Erase)
            {
                if (!(ActiveLayer is PaintLayer target))
                {
                    why = Msg.M14EraserNeedsPaint();
                    Toast(why, MessageType.Warning, 6f, Msg.BtnUseBrush, () => SetPaintTool(0));
                    return why;
                }
                if (target.strokes.Count == 0)
                {
                    why = Msg.M14EraserNeedsPaint();
                    Toast(why, MessageType.Info, 4f, Msg.BtnUseBrush, () => SetPaintTool(0));
                    return why;
                }
                if (!MatchesFaceSelection(target))
                {
                    why = Loc.T("Use the selected faces as this layer's limit before erasing.");
                    Toast(why, MessageType.Info, 5f, Loc.T("Use selection"), () => CaptureSelection(target));
                    return why;
                }
            }
            EndStroke();
            var paint = BlendToolActive ? null : PaintDestination();
            if (BrushOrFillAddsColour && EmptyLockedPaint(paint))
            {
                var recipe = _recipe;
                var compositor = _comp;
                why = EmptyPaintLockMessage;
                Toast(why, MessageType.Info, 7f, Loc.T("Allow new paint"), () => AllowNewPaint(recipe, compositor, paint));
                return why;
            }
            StartStrokeEdit(fromScene, fill != null);
            _liveLayer = BlendToolActive ? EnsureBlendLayer() : EnsurePaintLayer(paint);
            _liveStroke = _brush.NewStroke(_comp.SourceWidth);
            CapturePaintUvLimit(_liveStroke);
            _liveStroke.fillRegion = fill;
            _liveStroke.lockAlpha = _liveLayer.lockTransparent;
            _comp.LiveStroke = _liveStroke;
            _comp.LiveLayerId = _liveLayer.id;
            SetupSurfaceStroke(_liveStroke, hit);
            StartMirrorStroke();
            if (_liveStroke.isFill)
            {
                _liveStroke.points.Add(new Vector4(uv.x, uv.y, 1f, 0f));
                MirrorStrokePoint(uv, 1f, false, island);
                EndStroke();
                return null;
            }
            AddStrokePoint(uv, pressure, false, island, hit, false);
            return null;
        }

        void BeginMaskStroke(Vector2 uv, float pressure, int island, MeshUV.SurfaceHit? hit, PaintFillRegion fill, bool fromScene)
        {
            var layer = ActiveLayer;
            EndStroke();
            StartStrokeEdit(fromScene, fill != null);
            ShowStrokeLayerImmediately(layer);
            bool reveal = _brush.fillTool ? !_maskFillHides : _brush.mode == BrushMode.Paint;
            _liveMaskLayer = layer;
            _liveStroke = _brush.NewStroke(_comp.SourceWidth);
            CapturePaintUvLimit(_liveStroke);
            _liveStroke.fillRegion = fill;
            _liveStroke.mode = reveal == layer.paintMaskStartsHidden ? BrushMode.Paint : BrushMode.Erase;
            _liveStroke.tool = StrokeTool.Brush;
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

        void SetMaskEditing(Layer layer, bool on)
        {
            if (on && layer != null) BeginMaskEditing(layer, false);
            else if (_maskEditLayerId != null)
                ChangeMaskSession("Finish painting layer mask", () => _maskEditLayerId = null);
        }

        void BeginMaskEditing(Layer layer, bool startHidden)
        {
            if (layer is MixedMergeLayer) return;
            if (_needsRebuild || _recipe == null || !_recipe.layers.Contains(layer) || !CanPaint(out _)) return;
            if (MaskEditing && _maskEditLayerId == layer.id && _tab == Tab.Paint) return;
            SetPickForPaint(false);
            CancelGradientDrag();
            CancelSceneGradient();
            CancelImageDrag();
            var id = layer.id;
            ChangeMaskSession(layer.hasPaintMask ? "Edit layer mask" : "Add layer mask", () =>
            {
                SetTab(Tab.Paint);
                _activeLayerId = id;
                _maskEditLayerId = id;
                if (_brush.gradientTool || _brush.blendTool) SetPaintTool(0);
                if (!layer.hasPaintMask) AddPaintMask(layer, startHidden);
            });
            Toast(Msg.M12MaskPainting(layer.name), MessageType.Info, 5f, Msg.BtnDone, () =>
            {
                if (MaskEditing && _maskEditLayerId == id) SetMaskEditing(null, false);
            });
        }

        void ChangeMaskSession(string action, Action change)
        {
            EndStroke();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RecordObject(this, action);
            change();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Repaint();
            SceneView.RepaintAll();
        }

        void AddPaintMask(Layer layer, bool startHidden)
        {
            if (layer is MixedMergeLayer) return;
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
            layer.paintMaskStartsHidden = !layer.paintMaskStartsHidden;
            MarkEdited();
            Learn.Did(LearnEvents.MaskMade);
        }

        void DeletePaintMask(Layer layer)
        {
            if (_needsRebuild || layer == null || !layer.hasPaintMask || _recipe == null || !_recipe.layers.Contains(layer)) return;
            const string action = "Delete layer mask";
            ChangeMaskSession(action, () =>
            {
                Undo.RecordObject(_recipe, action);
                layer.hasPaintMask = false;
                layer.paintMaskStartsHidden = false;
                layer.maskStrokes = new List<PaintStroke>();
                if (_maskEditLayerId == layer.id) _maskEditLayerId = null;
                MarkEdited();
            });
        }

        void SetClipToBelow(Layer layer, bool on)
        {
            Undo.RecordObject(_recipe, on ? "Clip to layer below" : "Release clipping");
            layer.clipToBelow = on;
            MarkEdited();
            if (on) Learn.Did(LearnEvents.LayerClip);
        }

        void AddStrokePoint(Vector2 uv, float pressure, bool joined, int island, MeshUV.SurfaceHit? hit = null, bool surfJoined = true)
        {
            if (_liveStroke == null) return;
            if (_liveStroke.surface)
            {
                if (hit == null) return;
                var h = hit.Value;
                _liveStroke.surf.Add(new Vector4(h.triangle, h.bary.y, h.bary.z, (surfJoined ? 1 : 0) + (h.backFace ? 2 : 0)));
            }
            _liveStroke.points.Add(new Vector4(uv.x, uv.y, pressure, joined ? 1 : 0));
            RecordPenAngle(_liveStroke, hit);
            AddBrushClipPieces(_liveStroke, _liveStroke.clipIslands, uv, island, pressure,
                hit?.triangle ?? -1, hit?.bary ?? default);
            MirrorStrokePoint(uv, pressure, joined, island, surfJoined, hit.HasValue && hit.Value.backFace, hit);
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
            bool sceneDraft = _sceneStrokeDraft != null;
            if (sceneDraft && !BeginSceneStrokeCommit(_sceneStrokeDraft, _liveMaskLayer ?? (Layer)_liveLayer))
            {
                CancelStroke();
                if (_dirty && !_needsRebuild) ComposeNow();
                return;
            }
            var s = _liveStroke;
            var layer = _liveLayer;
            var maskLayer = _liveMaskLayer;
            var mirror = TakeMirrorStroke();
            CancelStroke();
            if (maskLayer != null)
            {
                if (s.points.Count > 0 && _recipe.layers.Contains(maskLayer))
                {
                    string undoName = s.isFill ? "Fill layer mask" : "Paint layer mask";
                    Undo.RecordObject(_recipe, undoName);
                    maskLayer.maskStrokes.Add(s);
                    AddMirror(maskLayer.maskStrokes, s, mirror);
                    Undo.SetCurrentGroupName(undoName);
                    RecordHistory(_strokeUndoGroup);
                    if (!sceneDraft) Undo.CollapseUndoOperations(_strokeUndoGroup);
                    _unsaved = true;
                    _savedBlockVisible = false;
                    Learn.Did(LearnEvents.MaskMade);
                    if (s.clip && s.clipIslands.Count == 0)
                        Toast(Msg.M13EmptySpaceStroke(), MessageType.Warning, 6f, Msg.BtnPaintAnywhere, PaintAnywhere);
                }
            }
            else if (layer != null && s.points.Count > 0 && _recipe.layers.Contains(layer))
            {
                string undoName = s.isFill ? "Fill" : s.mode == BrushMode.Erase ? "Erase stroke" : s.tool == StrokeTool.Blur ? "Blur stroke" : s.tool == StrokeTool.Smudge ? "Smudge stroke" : "Paint stroke";
                Undo.RecordObject(_recipe, undoName);
                layer.strokes.Add(s);
                AddMirror(layer.strokes, s, mirror);
                Undo.SetCurrentGroupName(undoName);
                RecordHistory(_strokeUndoGroup);
                if (!sceneDraft) Undo.CollapseUndoOperations(_strokeUndoGroup);
                if (s.mode == BrushMode.Paint && (!s.IsBlend || s.tool == StrokeTool.Mix)) RememberColour(s.color);
                _unsaved = true;
                _savedBlockVisible = false;
                Learn.Did(s.mode == BrushMode.Erase ? LearnEvents.LayerErase : LearnEvents.LayerStroke);
                if (s.IsSurface) Learn.Did(LearnEvents.SeamSmoothStroke);
                ExplainIfNothingVisible(s, layer);
            }
            if (sceneDraft)
            {
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            ComposeNow();
        }

        void ExplainIfNothingVisible(PaintStroke s, PaintLayer layer)
        {
            if (s.mode != BrushMode.Paint || _uv == null) return;
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
            if (s.isFill && s.fillRegion != null) return;
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
            if (_sceneStrokeDraft != null)
            {
                _sceneStrokeDraft = null;
                _dirty = true;
            }
            _liveStroke = null;
            _liveLayer = null;
            _liveMaskLayer = null;
            _liveMirror = null;
            _advancedMirrorRuns = null;
            if (_comp != null)
            {
                _comp.LiveStroke = null;
                _comp.LiveMirror = null;
                _comp.InvalidateLivePrefix();
                _comp.InvalidateLiveStrokeCoverage();
            }
        }

        PaintLayer EnsureBlendLayer()
        {
            if (ActiveLayer is PaintLayer p && p.blend == BlendMode.Normal && !p.HasActiveAdditionalBlends && MatchesFaceSelection(p))
            {
                ShowStrokeLayerImmediately(p);
                return p;
            }
            var layer = new PaintLayer { name = "Blending" };
            if (_sceneStrokeDraft != null) StageScenePaintLayer(layer, captureSelection: true, blending: true);
            else
            {
                InsertLayer(layer, "Add blending layer");
                ExplainNewBlendLayer();
            }
            return layer;
        }

        void ExplainNewBlendLayer() =>
            Toast(Loc.T("Added a “Blending” layer for blur and smudge, so the original stays untouched. Hide or delete it to undo the blending."), MessageType.Info, 5f);

        void ShowStrokeLayerImmediately(Layer layer)
        {
            if (_sceneStrokeDraft != null || layer.enabled) return;
            Undo.RecordObject(_recipe, "Show layer");
            layer.enabled = true;
        }

        PaintLayer PaintDestination()
        {
            if (_brush.fillTool) return FillDestination() as PaintLayer;
            return ActiveLayer is PaintLayer paint && MatchesFaceSelection(paint) ? paint : null;
        }

        bool BrushOrFillAddsColour => !MaskEditing && !BlendToolActive && !GradientToolActive && _brush.mode == BrushMode.Paint;

        static bool EmptyLockedPaint(PaintLayer paint) => paint?.lockTransparent == true &&
            !paint.strokes.Exists(stroke => stroke != null && stroke.mode == BrushMode.Paint && !stroke.lockAlpha);

        PaintLayer EmptyLockedPaintDestination => CanvasPainting && !_needsRebuild && CanPaint(out _) && BrushOrFillAddsColour &&
            PaintDestination() is PaintLayer paint && EmptyLockedPaint(paint) ? paint : null;

        static string EmptyPaintLockMessage => Loc.T("No paint on this layer to recolour.");

        void AllowNewPaint(TextureRecipe recipe, Compositor compositor, PaintLayer paint)
        {
            if (recipe != _recipe || compositor != _comp || paint == null || paint != EmptyLockedPaintDestination) return;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            string action = Loc.T("Allow new paint");
            Undo.SetCurrentGroupName(action);
            Undo.RecordObject(recipe, action);
            paint.lockTransparent = false;
            RefreshEditedRecipe(affectsImage: false);
            RecordHistory(group);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            if (_toastText == EmptyPaintLockMessage) _toastUntil = 0d;
            SceneView.RepaintAll();
        }

        PaintLayer EnsurePaintLayer(PaintLayer paint)
        {
            if (paint != null)
            {
                ShowStrokeLayerImmediately(paint);
                return paint;
            }
            var layer = new PaintLayer();
            bool captureSelection = !_brush.fillTool || FillScope != PaintFillScope.WholeTexture;
            if (_sceneStrokeDraft != null) StageScenePaintLayer(layer, captureSelection, blending: false);
            else InsertLayer(layer, "Add paint layer", captureSelection);
            return layer;
        }

        void RememberColour(Color c)
        {
            c.a = 1;
            _recentColors.RemoveAll(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
            _recentColors.Insert(0, c);
            if (_recentColors.Count > 10) _recentColors.RemoveRange(10, _recentColors.Count - 10);
        }

        void InsertLayer(Layer layer, string undoName = "Add layer", bool captureSelection = true, int? insertionIndex = null)
        {
            Undo.RecordObject(_recipe, undoName);
            Undo.RecordObject(this, undoName);
            if (captureSelection) CaptureInsertedLayerSelection(layer);
            int index = LayerInsertionIndex(insertionIndex);
            InheritLayerGroupAtInsertion(layer, index);
            _recipe.layers.Insert(index, layer);
            _activeLayerId = layer.id;
            if (!(layer is PaintLayer)) _openLayerSettings = true;
            MarkEdited();
            Learn.Did(LearnEvents.LayerAdded);
            Learn.Did(LearnEvents.LayerAddedOf(layer.GetType().Name));
            Repaint();
        }

        void CaptureInsertedLayerSelection(Layer layer)
        {
            if (CaptureFaceSelection(layer) || _selection.Count == 0) return;
            layer.maskMode = MaskMode.SelectedIslands;
            layer.islands = new List<int>(_selection);
        }

        int LayerInsertionIndex(int? insertionIndex)
        {
            int active = ActiveIndex;
            int index = insertionIndex ?? (active < 0 ? _recipe.layers.Count : active + 1);
            return index == 0 && _recipe.layers.Count > 0 && _recipe.layers[0] is MixedMergeLayer ? 1 : index;
        }

        void InheritLayerGroupAtInsertion(Layer layer, int index)
        {
            if (index > 0 && index < _recipe.layers.Count && SameLayerGroup(_recipe.layers[index - 1], _recipe.layers[index]))
                CopyLayerGroup(_recipe.layers[index - 1], layer);
        }

        bool CanMoveLayer(int i, int dir)
        {
            int j = i + dir;
            var layers = _recipe?.layers;
            if (layers == null || i < 0 || i >= layers.Count || j < 0 || j >= layers.Count || layers[i] == null || layers[i] is MixedMergeLayer)
                return false;
            return dir > 0 || !(layers[0] is MixedMergeLayer) || j != 0 && (SameLayerGroup(layers[i], layers[j]) || LayerGroupBounds(layers, j).first > 0);
        }

        void MoveLayer(int i, int dir)
        {
            if (!CanMoveLayer(i, dir)) return;
            int j = i + dir;
            var layers = _recipe.layers;
            Undo.RecordObject(_recipe, "Move layer");
            var layer = layers[i];
            var adjacent = layers[j];
            if (SameLayerGroup(layer, adjacent)) (layers[i], layers[j]) = (adjacent, layer);
            else
            {
                var (first, last) = LayerGroupBounds(layers, j);
                CopyLayerGroup(null, layer);
                layers.RemoveAt(i);
                layers.Insert(dir > 0 ? last : first, layer);
            }
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
            if (_recipe.layers[i] is MixedMergeLayer) return;
            var copy = Instantiate(_recipe);
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

        void ApplyLayerPreset(List<Layer> layers, string name)
        {
            if (layers == null || layers.Count == 0) return;
            if (_needsRebuild) { ShowLookTargetChanged(); return; }
            if (!CanPaint(out var problem)) { Toast(problem, MessageType.Info); return; }
            int omitted = layers.Count;
            int released = LayerPresets.PrepareForApply(layers);
            omitted -= layers.Count;
            if (layers.Count == 0) { Toast(Loc.T("This template has no reusable layers."), MessageType.Info); return; }
            if (layers.Any(l => l is ColourMatchLayer) && !CanAddColourMatchLayer(out var why)) { ShowColourMatchBlocked(why); return; }
            EndStroke();
            AddPreparedTemplate(PrepareTemplatePlacement(layers, released, omitted), name);
        }

        void CaptureSelection(Layer layer) => CaptureLayerLimitSelection(layer, HasFaceSelection,
            !HasFaceSelection && LayerLimitExcludesSelection(layer));

        void CaptureLayerLimitSelection(Layer layer, bool faces, bool invert)
        {
            if (layer is MixedMergeLayer) { Toast(Loc.T("Separate the merged layer to change its original settings."), MessageType.Info, 4f); return; }
            var mode = faces ? MaskMode.SelectedFaces : MaskMode.SelectedIslands;
            if (TryPlanLayerLimit(layer, mode, invert, true, out var apply))
                CommitLayerLimit(layer, apply, "Set layer mask");
        }

        void ClearPaint(PaintLayer layer)
        {
            Undo.RecordObject(_recipe, "Clear paint");
            layer.strokes.Clear();
            MarkEdited();
        }

        bool ConfirmReplaceLayers(TextureRecipe source)
        {
            if (_recipe == null || _recipe.layers.Count == 0 || !_unsaved) return true;
            int n = _recipe.layers.Count;
            return StudioDialog.AskComplex(Loc.T("Replace current layers?"), Msg.M8ReplaceLayers(source.name, n),
                Msg.BtnReplace, Msg.BtnCancel, null) == 0;
        }

        void LoadRecipe(TextureRecipe source, bool asCopy = false) => LoadRecipeLayers(source, asCopy);

        void LoadRecipeLayers(TextureRecipe source, bool asCopy, int? pausedDraftLayers = null, bool verifiedIncludedLayout = false)
        {
            if (source.includedLayout != null && !verifiedIncludedLayout)
            {
                ShowIncludedEditMismatch();
                return;
            }
            CancelGradientDrag();
            CancelImageDrag();
            CancelSceneGradient();
            EndStroke();
            Undo.RecordObject(this, "Load saved edit");
            Undo.RecordObject(_recipe, "Load saved edit");
            var copy = Instantiate(source);
            _recipe.layers = copy.layers;
            DestroyImmediate(copy);
            foreach (var layer in _recipe.layers)
            {
                if (layer == null) continue;
                layer.id = Guid.NewGuid().ToString("N");
                if (pausedDraftLayers.HasValue && NeedsVerifiedDraftLayout(layer)) layer.enabled = false;
            }
            var active = pausedDraftLayers.HasValue ? _recipe.layers.LastOrDefault(layer => layer?.enabled == true) :
                _recipe.layers.LastOrDefault();
            _activeLayerId = active?.id;
            MarkEdited();
            _unsaved = asCopy;
            string message = pausedDraftLayers > 0
                ? Loc.F("Opened draft “{0}”. Layers with unverified UV layouts are paused ({1}).",
                    ProjectDraftName(AssetDatabase.GetAssetPath(source)), pausedDraftLayers)
                : Loc.F("Opened saved edit “{0}” ({1} layer{2}).", source.name, _recipe.layers.Count, _recipe.layers.Count == 1 ? "" : "s");
            Toast(message, MessageType.Info, pausedDraftLayers > 0 ? 7f : 4f);
            if (_tab == Tab.Target) SetTab(Tab.Paint);
            Learn.Did(LearnEvents.EditReopened);
        }

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


    }
}
