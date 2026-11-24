using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _openStylePack = true;
        bool _showStylePackDrafts;
        Transform _stylePackDraftRoot;
        List<DraftItem> _stylePackDrafts;
        readonly HashSet<TextureRecipe> _stylePackDraftSelection = new();

        bool HasPackReceipt => CurrentSaveState == SaveState.Saved && LastBakeFiles.edit != null;

        void DrawStylePackControls()
        {
            var root = _renderer != null ? TargetResolver.AvatarRoot(_renderer) : null;
            if (_stylePackDraftRoot != root)
            {
                _stylePackDraftRoot = root;
                _stylePackDrafts = null;
                _stylePackDraftSelection.Clear();
            }
            if (!StudioGUI.BeginFoldGroup(ref _openStylePack, Loc.T("Pack and Share"), infoId: "asset"))
            {
                StudioGUI.EndGroup();
                return;
            }
            GUILayout.Label(Loc.T("Share your editable layers and added artwork. The other avatar supplies the original textures and mesh."),
                StudioGUI.Hint);
            using (new EditorGUI.DisabledScope(root == null || _needsRebuild || _recipe == null || CurrentSlot == null ||
                _recipe.layers.Count == 0 && _stylePackDraftSelection.Count == 0 && !HasPackReceipt || EditorApplication.isCompiling))
            {
                bool export = GUILayout.Button(Loc.T("Export style pack…"));
                StudioGUI.Count("save.exportStylePack", GUILayoutUtility.GetLastRect());
                if (export) AskToExportStylePack();
            }
            DrawOpenStylePack();

            bool show = StudioGUI.Foldout(_showStylePackDrafts, new GUIContent(Loc.T("Include saved drafts")));
            if (show && _stylePackDrafts == null) GatherPackDrafts(root);
            _showStylePackDrafts = show;
            if (show && _stylePackDrafts != null)
            {
                if (_stylePackDrafts.Count == 0) GUILayout.Label(Loc.T("No drafts match this avatar."), StudioGUI.Hint);
                foreach (var draft in _stylePackDrafts)
                {
                    if (draft.edit == null) continue;
                    bool has = _stylePackDraftSelection.Contains(draft.edit);
                    bool next = EditorGUILayout.ToggleLeft(new GUIContent(draft.name, draft.folder), has);
                    if (next && !has) _stylePackDraftSelection.Add(draft.edit);
                    else if (has && !next) _stylePackDraftSelection.Remove(draft.edit);
                }
            }
            StudioGUI.EndGroup();
        }

        void GatherPackDrafts(Transform root)
        {
            _stylePackDraftRoot = root;
            _stylePackDraftSelection.Clear();
            _stylePackDrafts = new List<DraftItem>();
            if (root == null) return;
            foreach (var item in ProjectDrafts())
                if (item.edit.layers.Count > 0 && StylePackTargets.TryDescribeDraft(root, item.edit, out _))
                    _stylePackDrafts.Add(item);
        }

        void AskToExportStylePack()
        {
            if (_renderer == null || _needsRebuild || _recipe == null || CurrentSlot == null) return;
            var renderer = _renderer;
            var material = _material;
            var slot = CurrentSlot;
            var recipe = _recipe;
            int revision = _historyRevision;
            var path = EditorUtility.SaveFilePanel(Loc.T("Pack and Share"), Application.dataPath, DefaultDraftName(), StylePack.Extension);
            if (string.IsNullOrEmpty(path)) return;
            if (this == null || _renderer != renderer || _material != material || _recipe != recipe || revision != _historyRevision ||
                CurrentSlot != slot || !SavedEditSlotStillAssigned(material, slot))
            {
                if (this != null) SavedEditContextChanged();
                return;
            }
            EditorApplication.delayCall += () =>
            {
                if (this == null || _renderer != renderer || _material != material || CurrentSlot != slot ||
                    _recipe != recipe || revision != _historyRevision || !SavedEditSlotStillAssigned(material, slot)) return;
                ExportStylePack(path);
            };
        }

        void ExportStylePack(string path)
        {
            if (_renderer == null || _needsRebuild || _recipe == null || CurrentSlot == null) return;
            EndStroke();
            var root = TargetResolver.AvatarRoot(_renderer);
            if (root == null) return;
            var sources = new List<(StylePack.Entry entry, TextureRecipe recipe)>();
            if (_recipe.layers.Count > 0)
            {
                var current = StylePackTargets.Describe(root, _renderer, _materialIndex, CurrentSlot, _uv, DefaultDraftName());
                if (current == null) { Toast(Loc.T("Pick a part with a unique name on this avatar."), MessageType.Warning); return; }
                sources.Add((current, _recipe));
            }
            else if (CurrentSaveState == SaveState.Saved)
            {
                var receipt = ResolveLastBakeFile<TextureRecipe>(_lastBakeRecipe, _lastBakeRecipeGuid);
                if (receipt != null && receipt.layers.Count > 0 && receipt.draftTarget?.rendererPath != null)
                    sources.Add((StylePackTargets.Snapshot(receipt.draftTarget), receipt));
                else if (receipt != null && _stylePackDraftSelection.Count == 0)
                {
                    Toast(Loc.T("Open the saved layers before packing this older edit."), MessageType.Info);
                    return;
                }
            }
            if (_stylePackDraftRoot == root)
            {
                foreach (var draft in _stylePackDrafts ?? Enumerable.Empty<DraftItem>())
                {
                    if (draft.edit == null || !_stylePackDraftSelection.Contains(draft.edit)) continue;
                    if (!StylePackTargets.TryDescribeDraft(root, draft.edit, out var target))
                    {
                        _stylePackDrafts = null;
                        _stylePackDraftSelection.Clear();
                        Toast(Loc.T("A selected draft changed. Reopen Include saved drafts."), MessageType.Warning);
                        return;
                    }
                    sources.Add((target, draft.edit));
                }
            }
            if (sources.Count == 0) return;

            var protectedTextures = new HashSet<Texture>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    StylePack.AddMaterialTextures(material, protectedTextures);
            var snapshots = new List<(StylePack.Entry entry, TextureRecipe recipe)>();
            try
            {
                foreach (var source in sources) snapshots.Add((source.entry, Instantiate(source.recipe)));
                float shownProgress = 0f;
                var manifest = StylePack.Write(path, Path.GetFileNameWithoutExtension(path), snapshots, protectedTextures,
                    (step, total) =>
                    {
                        bool collecting = total == sources.Count;
                        shownProgress = Mathf.Max(shownProgress, collecting ? step / (float)total : .4f);
                        return EditorUtility.DisplayCancelableProgressBar(Loc.T("Pack and Share"),
                            collecting ? Loc.T("Collecting editable layers…") : Loc.T("Packing added artwork…"), shownProgress);
                    });
                int excluded = manifest.entries.SelectMany(entry => entry.layers).Sum(layer =>
                    (layer.image != null && layer.image.state != StylePack.ArtworkState.Included ? 1 : 0) +
                    (layer.mask != null && layer.mask.state != StylePack.ArtworkState.None && layer.mask.state != StylePack.ArtworkState.Included ? 1 : 0));
                Toast(excluded > 0 ? Loc.F("Style pack saved with {0} artwork references left to resolve.", excluded) :
                    Loc.T("Style pack saved."), MessageType.Info, 6f);
                Learn.Did(LearnEvents.PackExported);
            }
            catch (OperationCanceledException) { Toast(Loc.T("Style pack export cancelled."), MessageType.Info); }
            catch (Exception error)
            {
                Debug.LogWarning($"[Avistry] Could not save the style pack ({error.GetType().Name}).");
                Toast(Loc.T("Couldn't save the style pack. Choose a new file and try again."), MessageType.Warning);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                foreach (var entry in snapshots) DestroyImmediate(entry.recipe);
            }
        }

        void DrawOpenStylePack()
        {
            bool open = GUILayout.Button(Loc.T("Open style pack…"));
            StudioGUI.Count("save.openStylePack", GUILayoutUtility.GetLastRect());
            if (!open) return;
            var path = EditorUtility.OpenFilePanel(Loc.T("Open style pack"), Application.dataPath, StylePack.Extension);
            if (!string.IsNullOrEmpty(path))
                EditorApplication.delayCall += () => { if (this != null) OpenStylePack(path); };
        }

        void OpenStylePack(string path)
        {
            var review = CreateInstance<StylePackReview>();
            review.titleContent = new GUIContent(Loc.T("Style pack preview"));
            review.minSize = new Vector2(320, 420);
            review.position = new Rect(position.x + 45f, position.y + 55f, 625f, 665f);
            review._owner = this;
            review._path = path;
            review._root = _renderer != null ? TargetResolver.AvatarRoot(_renderer) : null;
            review.QueueReview();
            review.ShowUtility();
        }

        sealed partial class StylePackReview : EditorWindow
        {
            enum PreviewPhase { Idle, ReadFile, FindTarget, ReadLayers, Render }

            [SerializeField] internal AvatarTextureWindow _owner;
            [SerializeField] internal string _path;
            [SerializeField] internal Transform _root;
            [SerializeField] int _entry;
            [SerializeField] bool _cancelled;
            [NonSerialized] StylePack.Manifest _manifest;
            [NonSerialized] PreviewPhase _phase;
            [NonSerialized] string _status;
            [NonSerialized] MessageType _statusType;
            [NonSerialized] Vector2 _scroll;
            [NonSerialized] StylePackTargets.Resolution _target;
            [NonSerialized] StylePack.Edit _edit;
            [NonSerialized] IEnumerator<int> _readLayers;
            [NonSerialized] int _layersRead;
            [NonSerialized] Compositor _preview;

            static bool EditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;

            void OnEnable()
            {
                if (_owner == null || string.IsNullOrEmpty(_path)) return;
                if (_showBatchReview)
                {
                    if (_batchCancelled) CancelBatchReview();
                    else BeginBatchReview();
                }
                else if (_cancelled) CancelReview();
                else QueueReview();
            }

            void OnDisable()
            {
                EditorApplication.delayCall -= ApplyCheckedBatch;
                StopBatchReview();
                ClearReview();
            }

            void StopPreparing()
            {
                EditorApplication.update -= PrepareReview;
                _phase = PreviewPhase.Idle;
                _readLayers?.Dispose();
                _readLayers = null;
            }

            void ClearReview()
            {
                StopPreparing();
                _preview?.Dispose();
                _preview = null;
                _edit?.Dispose();
                _edit = null;
                _target?.Geometry?.ReleaseSurfaceMesh();
                _target = null;
            }

            internal void QueueReview()
            {
                _status = null;
                _statusType = MessageType.Info;
                _cancelled = false;
                _layersRead = 0;
                ClearReview();
                if (_manifest == null) _phase = PreviewPhase.ReadFile;
                else if (_root != null) _phase = PreviewPhase.FindTarget;
                if (_phase != PreviewPhase.Idle) EditorApplication.update += PrepareReview;
                Repaint();
            }

            void CancelReview()
            {
                ClearReview();
                _cancelled = true;
                _status = Loc.T("Style pack preview cancelled.");
                _statusType = MessageType.Info;
                Repaint();
            }

            void PrepareReview()
            {
                if (this == null || _owner == null) { ClearReview(); return; }
                if (EditorBusy) { Repaint(); return; }
                try
                {
                    if (_root == null && _phase != PreviewPhase.ReadFile)
                    {
                        ClearReview();
                        return;
                    }
                    switch (_phase)
                    {
                        case PreviewPhase.ReadFile:
                            _manifest = StylePack.Read(_path);
                            _entry = Mathf.Clamp(_entry, 0, _manifest.entries.Length - 1);
                            if (_root == null) StopPreparing();
                            else _phase = PreviewPhase.FindTarget;
                            break;
                        case PreviewPhase.FindTarget:
                            var entry = _manifest.entries[_entry];
                            _target = StylePackTargets.Resolve(_root, entry);
                            if (_target.Problem != null) { StopPreparing(); break; }
                            _edit = new StylePack.Edit();
                            _readLayers = StylePack.ReadEditSteps(_path, _manifest, entry, _target.LayoutMatches, _edit).GetEnumerator();
                            _phase = PreviewPhase.ReadLayers;
                            break;
                        case PreviewPhase.ReadLayers:
                            ReadLayersForFrame();
                            break;
                        case PreviewPhase.Render:
                            if (!_target.StillMatches())
                            {
                                ClearReview();
                                _status = Loc.T("The target changed during preparation. Check the avatar root and retry the preview.");
                                _statusType = MessageType.Warning;
                                break;
                            }
                            _preview = new Compositor(_target.Slot, 512);
                            _preview.Compose(_edit.Layers, _target.Geometry);
                            StopPreparing();
                            break;
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"[Avistry] Could not preview a style pack ({error.GetType().Name}).");
                    bool readingFile = _phase == PreviewPhase.ReadFile || _phase == PreviewPhase.ReadLayers;
                    ClearReview();
                    if (readingFile) _manifest = null;
                    _status = readingFile ? error switch
                    {
                        FileNotFoundException or DirectoryNotFoundException =>
                            Loc.T("The style pack file is missing. Open it again from its new location."),
                        InvalidDataException or ArgumentException =>
                            Loc.T("This file is damaged or uses an unsupported style pack format."),
                        _ => Loc.T("Couldn't read this style pack. Check that the file is accessible and retry the preview."),
                    } : Loc.T("Couldn't render this style pack preview.");
                    _statusType = MessageType.Warning;
                }
                finally { Repaint(); }
            }

            void ReadLayersForFrame()
            {
                double until = EditorApplication.timeSinceStartup + .01;
                do
                {
                    if (!_readLayers.MoveNext())
                    {
                        _readLayers.Dispose();
                        _readLayers = null;
                        _phase = PreviewPhase.Render;
                        return;
                    }
                    _layersRead = _readLayers.Current;
                }
                while (EditorApplication.timeSinceStartup < until);
            }

            void OnGUI()
            {
                if (_owner == null || string.IsNullOrEmpty(_path))
                {
                    EditorGUILayout.HelpBox(Loc.T("Open a style pack from Avistry to preview it here."), MessageType.Info);
                    return;
                }
                if (_showBatchReview) { DrawBatchReview(); return; }
                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
                {
                    _scroll = scroll.scrollPosition;
                    GUILayout.Label(_manifest?.name ?? Loc.T("Style pack preview"), EditorStyles.boldLabel);
                    if (_manifest != null) DrawTargetChoices();
                    if (_phase != PreviewPhase.Idle)
                    {
                        GUILayout.Label(PreparationLabel(), StudioGUI.Hint);
                        if (GUILayout.Button(Loc.T("Cancel preview"))) CancelReview();
                        return;
                    }
                    if (_manifest != null && _root == null)
                        EditorGUILayout.HelpBox(Loc.T("Choose the avatar root to find this edit's target."), MessageType.Info);
                    if (_manifest?.entries.Length > 1 && _root != null)
                    {
                        bool review = GUILayout.Button(Loc.T("Review all edits…"));
                        StudioGUI.Count("pack.reviewAllEdits", GUILayoutUtility.GetLastRect());
                        if (review)
                        {
                            BeginBatchReview();
                            return;
                        }
                    }
                    if (_target?.Problem != null) EditorGUILayout.HelpBox(_target.Problem, MessageType.Warning);
                    if (_preview != null && _root != null)
                    {
                        float previewHeight = Mathf.Clamp((position.width - 18f) * .5f, 120f, 200f);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DrawPackTexture(Loc.T("Current texture"), _preview.SourcePreview, previewHeight);
                            DrawPackTexture(Loc.T("With style pack"), _preview.Preview, previewHeight);
                        }
                        foreach (var (layer, issue, art) in _edit.Issues)
                            EditorGUILayout.HelpBox(PackIssueText(layer, issue, art), MessageType.Info);
                    }
                    else if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
                    if (_preview == null && (_status != null || _target?.Problem != null))
                        using (new EditorGUI.DisabledScope(EditorBusy))
                            if (GUILayout.Button(Loc.T("Retry preview"))) QueueReview();
                }
                if (_preview != null && _root != null) DrawReviewedDraftAction();
            }

            void DrawReviewedDraftAction()
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Loc.F("Enabled layers: {0}", _edit.Layers.Count(layer => layer.enabled)), EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();
                        if (_edit.Issues.Count > 0)
                            GUILayout.Label(Loc.F("Layer issues ({0})", _edit.Issues.Count), EditorStyles.miniLabel);
                    }
                    bool open;
                    using (new EditorGUI.DisabledScope(_edit.Layers.Count == 0 || EditorBusy))
                        open = GUILayout.Button(Loc.T("Open as draft"));
                    if (open) OpenReviewedDraft();
                }
            }

            string PreparationLabel()
            {
                if (EditorApplication.isCompiling) return Loc.T("Waiting for Unity to finish compiling…");
                if (EditorApplication.isUpdating) return Loc.T("Unity is importing assets…");
                return _phase switch
                {
                    PreviewPhase.ReadFile => Loc.T("Reading the style pack…"),
                    PreviewPhase.FindTarget => Loc.T("Finding the saved part on this avatar…"),
                    PreviewPhase.ReadLayers =>
                        Loc.F("Reading layers and artwork ({0} of {1})…", _layersRead, _manifest.entries[_entry].layers.Length),
                    _ => Loc.T("Rendering the texture preview…"),
                };
            }

            void DrawTargetChoices()
            {
                var root = (Transform)EditorGUILayout.ObjectField(Loc.T("Avatar root"), _root, typeof(Transform), true);
                if (root != _root) { _root = root; QueueReview(); }
                var choices = Enumerable.Range(0, _manifest.entries.Length).ToArray();
                var labels = _manifest.entries.Select(entry => entry.name ?? entry.sourceName ?? Loc.T("Part")).ToArray();
                int next = SteppedChoice(new GUIContent(Loc.T("Edit")), _entry, choices,
                    (rect, selected) => EditorGUI.Popup(rect, selected, labels), position.width - 30f);
                if (next != _entry) { _entry = next; QueueReview(); }
            }

            static void DrawPackTexture(string label, Texture texture, float height)
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Label(label, EditorStyles.miniBoldLabel);
                    var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                }
            }

            static string PackIssueText(string name, StylePack.LayerIssue issue, StylePack.Artwork art) => issue switch
            {
                StylePack.LayerIssue.Unreadable => Loc.F("Avistry could not read layer “{0}”.", name),
                StylePack.LayerIssue.LayoutChanged => Loc.F("“{0}” is paused because this avatar uses a different UV layout.", name),
                StylePack.LayerIssue.MaskUnavailable => Loc.F("“{0}” needs its mask “{1}”. {2}", name, ArtworkName(art), ArtworkReason(art)),
                _ => Loc.F("“{0}” needs its image “{1}”. {2}", name, ArtworkName(art), ArtworkReason(art)),
            };

            static string ArtworkName(StylePack.Artwork art) => string.IsNullOrEmpty(art?.name) ? Loc.T("Unknown") : art.name;

            static string ArtworkReason(StylePack.Artwork art) => art?.state switch
            {
                StylePack.ArtworkState.AvatarAsset => Loc.T("Choose the image from your own avatar files."),
                StylePack.ArtworkState.Generated => Loc.T("Generate this image from your avatar."),
                _ => Loc.T("Choose the missing artwork to enable this layer."),
            };

            void OpenReviewedDraft()
            {
                if (_target == null || !_target.StillMatches()) { QueueReview(); return; }
                var destination = _target;
                var entry = _manifest.entries[_entry];
                var ownerRecipe = _owner._recipe;
                int ownerRevision = _owner._historyRevision;
                var recipe = CreateInstance<TextureRecipe>();
                try
                {
                    recipe.sourceTexture = destination.Slot.texture;
                    recipe.sourceMaterial = destination.Material;
                    recipe.rendererPath = string.Join("/", entry.rendererPath ?? Array.Empty<string>());
                    recipe.materialIndex = entry.materialIndex;
                    recipe.layers = _edit.Layers;
                    if (!_owner.ConfirmReplaceLayers(recipe)) return;
                    if (_owner == null || _owner._recipe != ownerRecipe || _owner._historyRevision != ownerRevision ||
                        !destination.StillMatches()) { QueueReview(); return; }

                    string label = string.Concat((entry.name ?? "Style pack").Select(c =>
                        InvalidDraftNameCharacters.Contains(c) || char.IsControl(c) ? '_' : c)).Trim(' ', '.');
                    string path = SaveDraftSnapshot(recipe, label, out string error, StylePackTargets.Describe(_root,
                        destination.Renderer, entry.materialIndex, destination.Slot, destination.Geometry, label));
                    if (path == null) { _status = error; return; }
                    var saved = AssetDatabase.LoadAssetAtPath<TextureRecipe>(path);
                    if (saved == null || _owner == null || _owner._recipe != ownerRecipe || _owner._historyRevision != ownerRevision ||
                        !destination.StillMatches())
                    {
                        _status = Loc.T("The draft was saved. Find it in Browse drafts after picking the part again.");
                        return;
                    }

                    _owner.WithTargetUndo("Open style pack", () =>
                    {
                        Undo.RecordObject(_owner, "Open style pack");
                        _owner.SetRenderer(destination.Renderer);
                        _owner._materialIndex = entry.materialIndex;
                        _owner._pendingTexture = destination.Slot.texture;
                        _owner.SetTab(Tab.Target);
                        _owner.Rebuild();
                        if (_owner.CurrentSlot?.texture == destination.Slot.texture) _owner.LoadRecipe(saved);
                        else _owner.QueueSavedEdit(saved);
                    });
                    Close();
                }
                finally { DestroyImmediate(recipe); }
            }
        }
    }
}
