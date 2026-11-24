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
        void DrawTemplatesButton()
        {
            bool open = GUILayout.Button(StudioGUI.Icon("Favorite", " " + Loc.T("Templates"),
                Loc.T("Choose a ready-made template or save your reusable layers.")), EditorStyles.toolbarButton, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
            var anchor = GUILayoutUtility.GetLastRect();
            StudioGUI.Count("paint.templates", anchor);
            if (open) PopupWindow.Show(anchor, new TemplateChooser(this));
        }

        void ShowLookTargetChanged() => Toast(Loc.T("The target changed. Open Templates again."), MessageType.Info);

        void ApplySavedLayerPreset(TextureRecipe preset)
        {
            if (this == null) return;
            if (preset == null) Toast(Loc.T("That template is no longer available. Open Templates again."), MessageType.Info);
            else ApplyLayerPreset(LayerPresets.LayersOf(preset), preset.name);
        }

        sealed class TemplatePlacement
        {
            public List<Layer> layers;
            public int insertAt, faces, pieces, released, omitted;
        }

        TemplatePlacement PrepareTemplatePlacement(List<Layer> layers, int released, int omitted)
        {
            var pieces = _selection.Where(IsCurrentPiece).Distinct().ToList();
            foreach (var layer in layers)
            {
                if (CaptureFaceSelection(layer) || pieces.Count == 0) continue;
                layer.maskMode = MaskMode.SelectedIslands;
                layer.islands = new List<int>(pieces);
                layer.invertMask = false;
            }
            return new TemplatePlacement
            {
                layers = layers,
                insertAt = TemplateInsertionIndex(layers),
                faces = HasFaceSelection ? _selectedFaces.Count : 0,
                pieces = pieces.Count,
                released = released,
                omitted = omitted,
            };
        }

        int TemplateInsertionIndex(List<Layer> layers)
        {
            int active = ActiveIndex;
            if (active < 0) return _recipe.layers.Count;
            return layers.Any(layer => !string.IsNullOrEmpty(layer.groupId))
                ? LayerGroupBounds(_recipe.layers, active).last + 1 : active + 1;
        }

        void AddPreparedTemplate(TemplatePlacement placement, string name)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            string undoName = "Add preset " + name;
            Undo.SetCurrentGroupName(undoName);
            int index = placement.insertAt;
            foreach (var layer in placement.layers)
                InsertLayer(layer, undoName, captureSelection: false, insertionIndex: index++);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int count = placement.layers.Count;
            if (count > 0) Learn.Did(LearnEvents.TemplateApplied);
            string message = Loc.F("Added template “{0}” ({1} layer{2})", name, count, count == 1 ? "" : "s") +
                (placement.faces > 0 ? Loc.F(", limited to the {0} selected face(s).", placement.faces) :
                    placement.pieces > 0 ? Loc.F(", limited to the {0} selected piece(s).", placement.pieces) :
                    Loc.T(". Tweak it in the layer settings below."));
            if (placement.released > 0) message += " " + Loc.T("Clipping was released where the look had no base layer.");
            if (placement.omitted > 0) message += " " + Loc.T("Layers tied to the original edit were skipped.");
            Toast(message, MessageType.Info, 5f);
        }

        sealed class TemplateChooser : PopupWindowContent
        {
            const string SearchControl = "AvistryTemplateSearch";
            enum TemplateAction { None, Preview, Add }

            readonly AvatarTextureWindow _owner;
            readonly Compositor _preview;
            readonly Renderer _part;
            readonly Material _material;
            readonly int _materialIndex;
            readonly Tab _tab;
            readonly TextureSlot _slot;
            List<TextureRecipe> _project;
            readonly List<(string path, TextureRecipe recipe)> _readingProject = new();
            IEnumerator<(string path, TextureRecipe recipe)> _projectScan;
            Vector2 _scroll;
            string _search = "", _name = "", _status;
            int _page;
            bool _restoreSearchFocus;
            TemplateComparison _comparison;

            public TemplateChooser(AvatarTextureWindow owner)
            {
                _owner = owner;
                _preview = owner._comp;
                _part = owner._renderer;
                _material = owner._material;
                _materialIndex = owner._materialIndex;
                _slot = owner.CurrentSlot;
                _tab = owner._tab;
            }

            public override Vector2 GetWindowSize() => new Vector2(380f, 460f);
            public override void OnOpen() => EditorApplication.projectChanged += InvalidateProjectTemplates;

            public override void OnClose()
            {
                EditorApplication.projectChanged -= InvalidateProjectTemplates;
                StopProjectScan();
                ClearComparison();
            }

            void StopProjectScan()
            {
                EditorApplication.update -= ReadProjectTemplates;
                _projectScan?.Dispose();
                _projectScan = null;
                _readingProject.Clear();
            }

            void InvalidateProjectTemplates()
            {
                StopProjectScan();
                _project = null;
                editorWindow?.Repaint();
            }

            void ReadProjectTemplates()
            {
                if (_projectScan == null) return;
                if (!CanUseTarget() || _page == 0 || editorWindow == null)
                {
                    StopProjectScan();
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

                double deadline = EditorApplication.timeSinceStartup + .004;
                for (int i = 0; i < 48 && EditorApplication.timeSinceStartup < deadline; i++)
                {
                    if (!_projectScan.MoveNext())
                    {
                        _project = LayerPresets.OrderUserPresets(_readingProject);
                        StopProjectScan();
                        editorWindow.Repaint();
                        break;
                    }

                    var entry = _projectScan.Current;
                    if (entry.recipe != null) _readingProject.Add(entry);
                }
            }

            bool CanUseTarget() => _owner != null && !_owner._needsRebuild && _owner._comp == _preview &&
                _owner._renderer == _part && _owner._material == _material && _owner._materialIndex == _materialIndex &&
                _owner.CurrentSlot == _slot && _owner._tab == _tab;

            bool CheckLiveTarget()
            {
                if (CanUseTarget())
                {
                    if (_part != null && _material != null && _slot != null && MeshOf(_part) == _owner._builtTargetMesh &&
                        SlotUsesMaterial(_part, _materialIndex, _material) && SavedEditSlotStillAssigned(_material, _slot)) return true;
                    _owner._needsRebuild = true;
                    _owner.Repaint();
                }
                _status = Loc.T("The target changed. Open Templates again.");
                if (_owner != null) _owner.ShowLookTargetChanged();
                return false;
            }

            public override void OnGUI(Rect rect)
            {
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    Event.current.Use();
                    if (_comparison != null && CanUseTarget()) ReturnToTemplates();
                    else editorWindow.Close();
                    return;
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    GUILayout.Label(Loc.T("Templates"), EditorStyles.boldLabel);
                    GUILayout.Label(Loc.T("Adds layers above the selected layer. Your current piece or face selection limits the template."), StudioGUI.Hint);
                    if (!CanUseTarget())
                    {
                        GUILayout.Label(Loc.T("The target changed. Open Templates again."), StudioGUI.Hint);
                        return;
                    }
                    if (_comparison != null)
                    {
                        DrawComparison();
                        return;
                    }
                    int page = DrawTemplatePages();
                    GUI.SetNextControlName(SearchControl);
                    string search = EditorGUILayout.TextField(Loc.T("Find templates"), _search);
                    if (_restoreSearchFocus && Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.FocusTextInControl(SearchControl);
                        _restoreSearchFocus = false;
                    }
                    if (page != _page || search != _search) _scroll = Vector2.zero;
                    if (page == 0 && _page != 0) StopProjectScan();
                    _page = page;
                    _search = search;

                    using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.ExpandHeight(true)))
                    {
                        _scroll = scroll.scrollPosition;
                        if (_page == 0) DrawReadyMade();
                        else DrawProject();
                    }

                    GUILayout.Space(5f);
                    DrawSaveForm();
                    if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, StudioGUI.Hint);
                }
            }

            int DrawTemplatePages()
            {
                var rect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                StudioGUI.Count("templates.navPages", rect);
                var choices = new[] { new GUIContent(Loc.T("Ready-made")), new GUIContent(Loc.T("My templates")) };
                int page = DrawStepSelectionGrid(rect, _page, choices, choices.Length, EditorStyles.miniButton);
                if (!EditorGUIUtility.editingTextField) return page;

                int direction = ChoiceInput(rect, false, Event.current);
                if (direction == 0) return page;
                Event.current.Use();
                return Mathf.Clamp(page + direction, 0, choices.Length - 1);
            }

            bool Matches(string text) => string.IsNullOrWhiteSpace(_search) ||
                text.IndexOf(_search.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;

            void DrawReadyMade()
            {
                string group = null;
                foreach (var (path, make) in LayerPresets.BuiltIn)
                {
                    string translated = LayerPresetLabel(path);
                    if (!Matches(translated)) continue;
                    int split = translated.LastIndexOf('/');
                    string category = translated.Substring(0, split);
                    string label = translated.Substring(split + 1);
                    if (category != group)
                    {
                        group = category;
                        GUILayout.Label(category, EditorStyles.miniBoldLabel);
                    }
                    var action = DrawTemplateRow(label, true);
                    if (action == TemplateAction.Preview) CompareTemplate(label, make);
                    if (action == TemplateAction.Add && CheckLiveTarget())
                    {
                        _owner.ApplyLayerPreset(make(), label);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                }
                if (group == null) GUILayout.Label(Loc.T("No templates match this search."), StudioGUI.Hint);
            }

            void DrawProject()
            {
                if (_project == null)
                {
                    if (_projectScan == null)
                    {
                        _projectScan = LayerPresets.ReadUserPresets().GetEnumerator();
                        EditorApplication.update += ReadProjectTemplates;
                    }
                    GUILayout.Label(Loc.T("Loading saved templates…"), StudioGUI.Hint);
                    return;
                }

                bool shown = false;
                foreach (var template in _project)
                {
                    if (template == null || !Matches(template.name)) continue;
                    shown = true;
                    string path = AssetDatabase.GetAssetPath(template);
                    bool reusable = template.layers.Any(LayerPresets.IsReusable);
                    var action = DrawTemplateRow(template.name, reusable, path);
                    if (action == TemplateAction.Preview) CompareTemplate(template.name, () => LayerPresets.LayersOf(template), template);
                    if (action == TemplateAction.Add && CheckLiveTarget())
                    {
                        _owner.ApplySavedLayerPreset(template);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                }
                if (!shown) GUILayout.Label(_project.Count == 0 ? Loc.T("No saved templates yet.") :
                    Loc.T("No templates match this search."), StudioGUI.Hint);
            }

            static TemplateAction DrawTemplateRow(string label, bool enabled, string path = null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent(label, path), EditorStyles.label, GUILayout.MinWidth(0));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!enabled))
                    {
                        bool preview = GUILayout.Button(Loc.T("Preview"), EditorStyles.miniButton);
                        bool add = GUILayout.Button(new GUIContent(Loc.T("Add"),
                            enabled ? null : Loc.T("This template has no reusable layers.")), EditorStyles.miniButton);
                        return preview ? TemplateAction.Preview : add ? TemplateAction.Add : TemplateAction.None;
                    }
                }
            }

            void CompareTemplate(string label, Func<List<Layer>> make, TextureRecipe saved = null)
            {
                if (!CheckLiveTarget()) return;
                if (!_owner.CanPaint(out _status)) return;
                var additions = make();
                int total = additions.Count;
                int released = LayerPresets.PrepareForApply(additions);
                if (additions.Count == 0)
                {
                    _status = Loc.T("This template has no reusable layers.");
                    return;
                }
                if (additions.Any(layer => layer is ColourMatchLayer) && !_owner.CanAddColourMatchLayer(out _status)) return;
                if (_owner._liveStroke != null)
                {
                    _status = Loc.T("Finish painting before previewing a template.");
                    return;
                }

                ClearComparison();
                try
                {
                    var uv = _owner._uv;
                    if (uv?.PoseChanged() == true) uv.RefreshPose();
                    var placement = _owner.PrepareTemplatePlacement(additions, released, total - additions.Count);
                    var before = new List<Layer>(_owner._recipe.layers);
                    var after = new List<Layer>(before);
                    after.InsertRange(placement.insertAt, placement.layers);
                    _comparison = new TemplateComparison(label, saved, placement, _slot, uv, before, after,
                        CaptureComparisonContext());
                    _status = null;
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"[Avistry] Template preview could not be rendered ({error.GetType().Name}).");
                    _status = Loc.T("The template preview could not be rendered.");
                }
                editorWindow.Repaint();
            }

            Func<bool> CaptureComparisonContext()
            {
                var sameTarget = _owner.CaptureSavedEditContext(_owner._recipe, _slot);
                var uv = _owner._uv;
                int pose = uv?.PoseVersion ?? 0;
                string active = _owner._activeLayerId;
                string faceLayout = _owner._selectedFaceLayout;
                var pieces = _owner._selection.ToArray();
                var faces = _owner._selectedFaces.ToArray();
                return () => CanUseTarget() && sameTarget() && _owner._liveStroke == null &&
                    _owner._uv == uv && (uv == null || uv.PoseVersion == pose && !uv.PoseChanged()) &&
                    _owner._activeLayerId == active && _owner._selectedFaceLayout == faceLayout &&
                    _owner._selection.SequenceEqual(pieces) && _owner._selectedFaces.SequenceEqual(faces);
            }

            void DrawComparison()
            {
                var view = _comparison;
                GUILayout.Label(view.label, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawComparisonTexture(Loc.T("Current edit"), view.before);
                    DrawComparisonTexture(Loc.T("With template"), view.compositor.Preview);
                }
                var placement = view.placement;
                GUILayout.Label(Loc.F("Reusable layers ({0})", placement.layers.Count), StudioGUI.Hint);
                if (placement.faces > 0)
                    GUILayout.Label(Loc.F("Selected faces ({0})", placement.faces), StudioGUI.Hint);
                else if (placement.pieces > 0)
                    GUILayout.Label(Loc.F("Selected pieces ({0})", placement.pieces), StudioGUI.Hint);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Loc.T("Back to templates")))
                    {
                        ReturnToTemplates();
                        return;
                    }
                    if (GUILayout.Button(Loc.T("Add template")))
                    {
                        if (!view.StillMatches())
                        {
                            _status = Loc.T("The edit, selection or template changed. Preview it again.");
                        }
                        else
                        {
                            _owner.AddPreparedTemplate(view.placement, view.label);
                            editorWindow.Close();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, StudioGUI.Hint);
            }

            void ReturnToTemplates()
            {
                ClearComparison();
                _status = null;
                _restoreSearchFocus = true;
                editorWindow.Repaint();
            }

            static void DrawComparisonTexture(string label, Texture texture)
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Label(label, EditorStyles.miniBoldLabel);
                    var rect = GUILayoutUtility.GetRect(0f, 166f, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint)
                        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                }
            }

            void ClearComparison()
            {
                _comparison?.Dispose();
                _comparison = null;
            }

            sealed class TemplateComparison : IDisposable
            {
                public readonly string label;
                public readonly TemplatePlacement placement;
                public Compositor compositor;
                public RenderTexture before;
                readonly TextureRecipe _saved;
                readonly string _path;
                readonly Hash128 _version;
                readonly Func<bool> _sameContext;

                public TemplateComparison(string label, TextureRecipe saved, TemplatePlacement placement,
                    TextureSlot slot, MeshUV uv, List<Layer> current, List<Layer> next, Func<bool> sameContext)
                {
                    this.label = label;
                    this.placement = placement;
                    _sameContext = sameContext;
                    _saved = saved;
                    _path = saved != null ? AssetDatabase.GetAssetPath(saved) : null;
                    _version = _path != null ? AssetDatabase.GetAssetDependencyHash(_path) : default;
                    try
                    {
                        compositor = new Compositor(slot, 256);
                        compositor.Compose(current, uv);
                        before = new RenderTexture(compositor.Preview.descriptor) { hideFlags = HideFlags.HideAndDontSave };
                        before.Create();
                        Graphics.CopyTexture(compositor.Preview, before);
                        compositor.Compose(next, uv);
                    }
                    catch
                    {
                        Dispose();
                        throw;
                    }
                }

                public bool StillMatches() => _sameContext() && (_path == null || _saved != null &&
                    AssetDatabase.GetAssetPath(_saved) == _path && AssetDatabase.GetAssetDependencyHash(_path) == _version);

                public void Dispose()
                {
                    compositor?.Dispose();
                    if (before == null) return;
                    before.Release();
                    DestroyImmediate(before);
                }
            }

            void DrawSaveForm()
            {
                var layers = _owner._recipe.layers;
                int reusable = layers.Count(LayerPresets.IsReusable);
                GUILayout.Label(Loc.T("Save reusable layers as a template"), EditorStyles.miniBoldLabel);
                GUILayout.Label(Loc.T("Drafts keep the full edit. Templates use the new target's selection and leave out painted masks."), StudioGUI.Hint);
                if (reusable == 0 || layers.Count > reusable)
                    GUILayout.Label(Loc.T("Paint, Scene gradients, generated maps and images without an imported picture stay with the edit."), StudioGUI.Hint);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(reusable == 0))
                    {
                        _name = EditorGUILayout.TextField(Loc.T("Template name"), _name);
                        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_name)))
                            if (GUILayout.Button(Loc.T("Save template"), GUILayout.Width(106f)) && CheckLiveTarget()) SaveTemplate();
                    }
                }
            }

            void SaveTemplate()
            {
                if (!LayerPresets.ValidTemplateName(_name))
                {
                    _status = Loc.T("Use a template name without slashes or other file name symbols.");
                    return;
                }
                var path = LayerPresets.SaveNamedTemplate(_owner._recipe, _name);
                if (path == null)
                {
                    _status = Loc.T("The template couldn't be saved. Check the Console and try again.");
                    return;
                }
                InvalidateProjectTemplates();
                _page = 1;
                _search = "";
                _scroll = Vector2.zero;
                _name = "";
                _status = Loc.F("Saved template “{0}”.", Path.GetFileNameWithoutExtension(path));
                _owner.Toast(_status, MessageType.Info);
                editorWindow.Repaint();
            }
        }
    }
}
