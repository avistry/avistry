using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly Regex DraftSuffix = new Regex(@"_edit( \d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        const string InvalidDraftNameCharacters = "<>:\"/\\|?*";
        [SerializeField] bool _openSaveDrafts = true;
        string _draftName;

        string DefaultDraftName() => DefaultDraftName(_recipe?.sourceTexture);

        static string DefaultDraftName(Texture2D texture)
        {
            var name = texture != null ? texture.name : "Texture";
            return string.Concat(name.Select(c => InvalidDraftNameCharacters.Contains(c) || char.IsControl(c) ? '_' : c)).Trim(' ', '.');
        }

        string SaveEditAsset() => SaveProjectDraft(null);

        string SaveProjectDraft(string requestedName)
        {
            EndStroke();
            var recipe = _recipe;
            if (recipe == null || recipe.layers.Count == 0) return null;
            var part = _renderer;
            var texture = recipe.sourceTexture;
            var material = recipe.sourceMaterial;
            int materialIndex = _materialIndex;
            int revision = _historyRevision;
            var target = !_needsRebuild ? StylePackTargets.Describe(TargetResolver.AvatarRoot(part), part, materialIndex,
                CurrentSlot, _uv, requestedName) : null;
            if (!CaptureIncludedLayout(out var includedLayout))
            {
                ShowIncludedSaveProblem();
                return null;
            }
            string path = SaveDraftSnapshot(recipe, requestedName, out string error, target, includedLayout);
            if (path == null)
            {
                Toast(error, MessageType.Warning);
                return null;
            }

            _lastSavedEdit = path;
            bool sameEdit = _recipe == recipe && _historyRevision == revision && _renderer == part && _materialIndex == materialIndex &&
                recipe.sourceTexture == texture && recipe.sourceMaterial == material;
            if (sameEdit)
            {
                _unsaved = false;
                hasUnsavedChanges = false;
            }
            string saved = ProjectDraftName(path);
            bool temporaryMaterial = material != null && !EditorUtility.IsPersistent(material);
            string message = !sameEdit ? Loc.F("Draft “{0}” saved. Your newer changes are still open.", saved) : temporaryMaterial
                ? Loc.F("Draft “{0}” saved. Pick its part to continue it from Browse drafts.", saved)
                : Loc.F("Draft “{0}” saved. Find it in Browse drafts.", saved);
            Toast(message, MessageType.Info);
            Learn.Did(LearnEvents.EditSaved);
            return path;
        }

        internal static string SaveDraftSnapshot(TextureRecipe recipe, string requestedName, out string error, StylePack.Entry target = null,
            TextureRecipe.IncludedLayout includedLayout = null)
        {
            error = null;
            var texture = recipe != null ? recipe.sourceTexture : null;
            if (texture == null || !EditorUtility.IsPersistent(texture))
            {
                error = Loc.T("Save the source texture as a project asset before saving a draft.");
                return null;
            }
            var name = string.IsNullOrWhiteSpace(requestedName) ? DefaultDraftName(texture) : requestedName.Trim();
            if (name.Length == 0) name = "Texture";
            if (name.All(c => c == '.') || name.Any(c => InvalidDraftNameCharacters.Contains(c) || char.IsControl(c)))
            {
                error = Loc.T("Use a draft name without slashes or other file name symbols.");
                return null;
            }

            var copy = Instantiate(recipe);
            copy.hideFlags = HideFlags.None;
            copy.bakedTexture = null;
            copy.bakedMaterial = null;
            copy.savedAsDraft = true;
            copy.savedAsTemplate = false;
            copy.draftTarget = StylePackTargets.Snapshot(target ?? recipe.draftTarget);
            if (includedLayout != null) copy.includedLayout = includedLayout;
            if (copy.sourceMaterial != null && !EditorUtility.IsPersistent(copy.sourceMaterial)) copy.sourceMaterial = null;

            string path = null;
            var artworkCopies = new Dictionary<Texture2D, Texture2D>();
            try
            {
                if (!TryDraftArtwork(copy, out var artwork, out error))
                {
                    DestroyImmediate(copy);
                    return null;
                }
                string dir = Exporter.SaveFolder("Drafts");
                path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{name}_edit.asset");
                copy.name = Path.GetFileNameWithoutExtension(path);
                if (artwork.Count > 0)
                {
                    using var stagedEdit = new SerializedObject(copy);
                    foreach (var (property, _) in artwork)
                        stagedEdit.FindProperty(property).objectReferenceValue = null;
                    stagedEdit.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.CreateAsset(copy, path);
                if (AssetDatabase.GetAssetPath(copy) != path) throw new IOException("Unity did not create the draft asset.");
                if (artwork.Count > 0)
                {
                    foreach (var original in artwork.Select(item => item.source).Distinct())
                    {
                        var asset = Instantiate(original);
                        asset.name = original.name;
                        asset.hideFlags = HideFlags.None;
                        artworkCopies.Add(original, asset);
                        AssetDatabase.AddObjectToAsset(asset, copy);
                        if (AssetDatabase.GetAssetPath(asset) != path || !EditorUtility.IsPersistent(asset))
                            throw new IOException("Unity did not save the draft artwork.");
                    }
                    using var savedEdit = new SerializedObject(copy);
                    foreach (var (property, original) in artwork)
                    {
                        var reference = savedEdit.FindProperty(property);
                        if (reference == null || reference.objectReferenceValue != null)
                            throw new IOException("The draft artwork changed while saving.");
                        reference.objectReferenceValue = artworkCopies[original];
                    }
                    savedEdit.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssetIfDirty(copy);
            }
            catch (Exception failure)
            {
                if (copy != null && !EditorUtility.IsPersistent(copy)) DestroyImmediate(copy);
                else if (copy != null && path != null && AssetDatabase.GetAssetPath(copy) == path &&
                    AssetDatabase.LoadAssetAtPath<TextureRecipe>(path) == copy) AssetDatabase.DeleteAsset(path);
                foreach (var asset in artworkCopies.Values)
                    if (asset != null && !EditorUtility.IsPersistent(asset)) DestroyImmediate(asset);
                Debug.LogWarning($"Avistry couldn't save the draft ({failure.GetType().Name}).");
                error = Loc.T("The draft couldn't be saved. Check the Console and try again.");
                return null;
            }
            return path;
        }

        static bool TryDraftArtwork(TextureRecipe copy, out List<(string property, Texture2D source)> artwork, out string reason)
        {
            artwork = new List<(string, Texture2D)>();
            reason = null;
            using var serialized = new SerializedObject(copy);
            foreach (var value in DraftLayerAssetReferences(serialized))
            {
                var asset = value.objectReferenceValue;
                if (asset == null && value.objectReferenceInstanceIDValue != 0)
                {
                    reason = Loc.T("A layer uses an asset Unity can't find. Replace it before saving the draft.");
                    return false;
                }
                if (asset == null || EditorUtility.IsPersistent(asset)) continue;
                if (asset is Texture2D texture) artwork.Add((value.propertyPath, texture));
                else
                {
                    reason = Loc.T("A layer uses a temporary asset. Save it in the project, then try again.");
                    return false;
                }
            }
            return true;
        }

        static IEnumerable<SerializedProperty> DraftLayerAssetReferences(SerializedObject serialized)
        {
            var value = serialized.FindProperty("layers");
            var end = value.GetEndProperty();
            bool enter = true;
            while (value.Next(enter) && !SerializedProperty.EqualContents(value, end))
            {
                enter = !value.isArray || value.name == "originals" || value.propertyPath == "layers.Array" ||
                    value.propertyPath.EndsWith(".originals.Array", StringComparison.Ordinal);
                if (value.propertyType == SerializedPropertyType.ObjectReference) yield return value;
            }
        }

        static int CountDraftArtworkGaps(TextureRecipe edit)
        {
            if (edit.layers == null || !edit.layers.Any(layer => layer is ImageLayer || layer is MixedMergeLayer)) return 0;
            using var serialized = new SerializedObject(edit);
            HashSet<string> layers = null;
            foreach (var value in DraftLayerAssetReferences(serialized))
            {
                bool image = value.name == nameof(ImageLayer.image);
                bool mask = value.name == nameof(ImageLayer.mask);
                if ((!image && !mask) || value.objectReferenceValue != null) continue;
                var path = value.propertyPath;
                var layerPath = path.Substring(0, path.LastIndexOf('.'));
                if (mask && value.objectReferenceInstanceIDValue == 0 &&
                    string.IsNullOrEmpty(serialized.FindProperty(layerPath + "." + nameof(ImageLayer.missingPackMask))?.stringValue)) continue;
                (layers ??= new HashSet<string>()).Add(layerPath);
            }
            return layers?.Count ?? 0;
        }

        static bool IsProjectDraft(TextureRecipe edit, string path) => edit != null && !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
            !edit.savedAsTemplate && edit.bakedTexture == null && edit.bakedMaterial == null &&
            (edit.savedAsDraft || DraftSuffix.IsMatch(Path.GetFileNameWithoutExtension(path)));

        static string ProjectDraftName(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            return DraftSuffix.Replace(file, "$1");
        }

        static string[] FindDraftCandidates() => AssetDatabase.FindAssets("t:TextureRecipe", new[] { "Assets" });

        static DraftItem ReadProjectDraft(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            var edit = AssetDatabase.LoadAssetAtPath<TextureRecipe>(path);
            return IsProjectDraft(edit, path) ? new DraftItem(edit, path) : null;
        }

        static int CompareDrafts(DraftItem a, DraftItem b)
        {
            int byName = StringComparer.CurrentCultureIgnoreCase.Compare(a.name, b.name);
            return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.path, b.path);
        }

        static List<DraftItem> ProjectDrafts()
        {
            var drafts = FindDraftCandidates().Select(ReadProjectDraft).Where(item => item != null).ToList();
            drafts.Sort(CompareDrafts);
            return drafts;
        }

        sealed class DraftItem
        {
            public readonly TextureRecipe edit;
            public readonly string path, name, texture, folder;
            public readonly bool missingSource;
            public readonly int artworkGaps;

            public DraftItem(TextureRecipe edit, string path)
            {
                this.edit = edit;
                this.path = path;
                name = ProjectDraftName(path);
                missingSource = edit.sourceTexture == null;
                texture = missingSource ? Loc.T("Source texture unavailable") : edit.sourceTexture.name;
                folder = path.Substring(0, path.LastIndexOf('/'));
                artworkGaps = CountDraftArtworkGaps(edit);
            }

            public bool Matches(string text) => name.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                texture.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                folder.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        void OpenProjectDraft(TextureRecipe edit)
        {
            if (this == null) return;
            if (edit == null || !IsProjectDraft(edit, AssetDatabase.GetAssetPath(edit)))
            {
                Toast(Loc.T("This draft is no longer in the project."), MessageType.Info);
                return;
            }

            bool canOpenHere = edit.sourceTexture == null
                ? CurrentSlot is TextureSlot current && current.role != TextureRole.Normal
                : (edit.sourceMaterial == null || _material == edit.sourceMaterial) &&
                    _slots.Any(slot => slot.texture == edit.sourceTexture && slot.role != TextureRole.Normal);
            if (_comp != null && !_needsRebuild && canOpenHere)
            {
                OpenSavedEdit(edit);
                return;
            }
            OpenSavedEditOnLoadedSource(edit);
        }

        void DrawBrowseDraftsButton()
        {
            bool pressed = GUILayout.Button(Loc.T("Browse drafts…"));
            var button = GUILayoutUtility.GetLastRect();
            StudioGUI.Count("drafts.browse", button);
            if (pressed) PopupWindow.Show(button, new DraftChooser(this));
        }

        sealed class DraftChooser : PopupWindowContent
        {
            const string SearchControl = "AvistryDraftSearch";
            const float RowLine = 16f;
            const float RowGap = 5f;
            readonly AvatarTextureWindow _owner;
            readonly List<DraftItem> _reading = new();
            readonly List<DraftItem> _matches = new();
            readonly List<float> _rowOffsets = new();
            List<DraftItem> _drafts;
            string[] _draftGuids;
            int _readIndex;
            int _chosenRow = -1;
            Vector2 _scroll;
            Rect _rowViewport;
            string _search = "";
            bool _focusSearch = true;
            GUIStyle _statusStyle;
            string _layoutQuery, _layoutLanguage;
            float _layoutWidth;

            public DraftChooser(AvatarTextureWindow owner) => _owner = owner;
            public override Vector2 GetWindowSize() => new Vector2(360f, 390f);
            public override void OnOpen()
            {
                EditorApplication.projectChanged += StartDraftScan;
                StartDraftScan();
            }

            public override void OnClose()
            {
                EditorApplication.projectChanged -= StartDraftScan;
                EditorApplication.update -= ReadDrafts;
                _reading.Clear();
                _matches.Clear();
                _rowOffsets.Clear();
                _drafts = null;
                _draftGuids = null;
            }

            void StartDraftScan()
            {
                EditorApplication.update -= ReadDrafts;
                _drafts = null;
                _draftGuids = null;
                _readIndex = 0;
                _reading.Clear();
                _matches.Clear();
                _rowOffsets.Clear();
                _layoutQuery = null;
                _chosenRow = -1;
                EditorApplication.update += ReadDrafts;
                editorWindow?.Repaint();
            }

            void ReadDrafts()
            {
                if (_owner == null || editorWindow == null)
                {
                    OnClose();
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

                double deadline = EditorApplication.timeSinceStartup + .004;
                _draftGuids ??= FindDraftCandidates();
                int end = Math.Min(_readIndex + 48, _draftGuids.Length);
                while (_readIndex < end && EditorApplication.timeSinceStartup < deadline)
                {
                    var draft = ReadProjectDraft(_draftGuids[_readIndex++]);
                    if (draft != null) _reading.Add(draft);
                }
                if (_readIndex == _draftGuids.Length)
                {
                    _reading.Sort(CompareDrafts);
                    _drafts = new List<DraftItem>(_reading);
                    _reading.Clear();
                    _draftGuids = null;
                    EditorApplication.update -= ReadDrafts;
                }
                editorWindow.Repaint();
            }

            public override void OnGUI(Rect rect)
            {
                if (_owner == null || Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    Event.current.Use();
                    editorWindow.Close();
                    return;
                }
                int direction = HandleDraftKeys();

                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    GUILayout.Label(Loc.T("Drafts"), EditorStyles.boldLabel);
                    GUI.SetNextControlName(SearchControl);
                    string search = EditorGUILayout.TextField(Loc.T("Find drafts"), _search);
                    StudioGUI.Count("drafts.search", GUILayoutUtility.GetLastRect());
                    if (_focusSearch && Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.FocusTextInControl(SearchControl);
                        _focusSearch = false;
                    }
                    if (search != _search)
                    {
                        _scroll = Vector2.zero;
                        _chosenRow = -1;
                    }
                    _search = search;

                    if (_drafts == null)
                    {
                        string status = _draftGuids == null ? Loc.T("Finding drafts…") :
                            Loc.F("Reading drafts… {0}/{1}", _readIndex, _draftGuids.Length);
                        GUILayout.Label(status, StudioGUI.Hint);
                        return;
                    }

                    var viewport = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    if (Event.current.type == EventType.Repaint) _rowViewport = viewport;
                    if (Event.current.type != EventType.Layout)
                        DrawDraftRows(direction == 0 ? viewport : _rowViewport, search.Trim(), direction);
                }
            }

            int HandleDraftKeys()
            {
                var e = Event.current;
                if (e.type != EventType.KeyDown || e.alt || e.command || e.control || _matches.Count == 0 ||
                    _layoutLanguage != Loc.Current || GUI.GetNameOfFocusedControl() != SearchControl) return 0;

                if (e.keyCode is KeyCode.UpArrow or KeyCode.DownArrow)
                {
                    int direction = ChoiceInput(default, true, e);
                    e.Use();
                    return direction;
                }
                if ((uint)_chosenRow < (uint)_matches.Count && e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    e.Use();
                    OpenDraft(_matches[_chosenRow].edit);
                }
                return 0;
            }

            void DrawDraftRows(Rect viewport, string query, int direction)
            {
                var open = new GUIContent(Loc.T("Open"));
                float buttonWidth = Mathf.Max(48f, EditorStyles.miniButton.CalcSize(open).x);
                float rowWidth = Mathf.Max(1f, viewport.width - GUI.skin.verticalScrollbar.fixedWidth - 4f);
                float textWidth = Mathf.Max(1f, rowWidth - buttonWidth - 8f);
                if (query != _layoutQuery || textWidth != _layoutWidth || Loc.Current != _layoutLanguage)
                    MeasureDraftRows(query, textWidth);
                if (_matches.Count == 0)
                {
                    GUI.Label(viewport, _drafts.Count == 0 ? Loc.T("No saved drafts in this project") :
                        Loc.T("No drafts match this search."), StudioGUI.Hint);
                    return;
                }

                if (direction == 0)
                {
                    direction = ChoiceInput(viewport, false, Event.current);
                    if (direction != 0)
                    {
                        Event.current.Use();
                        GUI.FocusControl(SearchControl);
                    }
                }
                if (direction != 0) StepDraftRow(direction, viewport.height);
                TextureRecipe selected = null;
                using (var scroll = new GUI.ScrollViewScope(viewport, _scroll, new Rect(0f, 0f, rowWidth, _rowOffsets[^1])))
                {
                    _scroll = scroll.scrollPosition;
                    int index = DraftRowAt(_scroll.y);
                    float bottom = _scroll.y + viewport.height;
                    for (; index < _matches.Count && _rowOffsets[index] < bottom; index++)
                    {
                        var item = _matches[index];
                        if (index == _chosenRow) DrawChosenDraftRow(index, rowWidth);
                        if (item.edit != null && DrawDraft(item, _rowOffsets[index], rowWidth, textWidth, buttonWidth, open))
                            selected = item.edit;
                    }
                }
                if (selected != null) OpenDraft(selected);
            }

            void OpenDraft(TextureRecipe selected)
            {
                editorWindow.Close();
                _owner.OpenProjectDraft(selected);
                GUIUtility.ExitGUI();
            }

            int DraftRowAt(float y)
            {
                int index = _rowOffsets.BinarySearch(y);
                return index < 0 ? Math.Max(0, ~index - 1) : index;
            }

            void StepDraftRow(int direction, float viewportHeight)
            {
                if (_chosenRow < 0)
                {
                    float start = direction > 0 ? _scroll.y : _scroll.y + viewportHeight - 1f;
                    _chosenRow = Mathf.Min(DraftRowAt(start), _matches.Count - 1);
                }
                else _chosenRow = Mathf.Clamp(_chosenRow + direction, 0, _matches.Count - 1);

                float top = _rowOffsets[_chosenRow];
                float bottom = _rowOffsets[_chosenRow + 1] - RowGap;
                _scroll.y = Mathf.Clamp(_scroll.y, Mathf.Min(top, bottom - viewportHeight), top);
            }

            void DrawChosenDraftRow(int index, float width)
            {
                if (Event.current.type != EventType.Repaint) return;
                float y = _rowOffsets[index];
                var row = new Rect(0f, y, width, _rowOffsets[index + 1] - y - RowGap);
                var tint = StudioGUI.Accent;
                tint.a = .19f;
                EditorGUI.DrawRect(row, tint);
                StudioGUI.Count("drafts.highlight", row);
            }

            void MeasureDraftRows(string query, float textWidth)
            {
                var chosen = _layoutLanguage == Loc.Current && (uint)_chosenRow < (uint)_matches.Count
                    ? _matches[_chosenRow].edit : null;
                bool restoreChoice = chosen != null;
                _chosenRow = -1;
                _layoutQuery = query;
                _layoutLanguage = Loc.Current;
                _layoutWidth = textWidth;
                _matches.Clear();
                _rowOffsets.Clear();
                _rowOffsets.Add(0f);
                _statusStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                {
                    normal = { textColor = EditorGUIUtility.isProSkin ? StudioGUI.Accent : new Color(.55f, .26f, .02f) },
                };
                foreach (var item in _drafts)
                {
                    if (item.edit == null || !item.Matches(query)) continue;
                    if (restoreChoice && item.edit == chosen) _chosenRow = _matches.Count;
                    _matches.Add(item);
                    float height = RowLine * 2f + RowGap;
                    if (item.missingSource) height += StatusHeight(new GUIContent(item.texture), textWidth);
                    if (item.artworkGaps > 0) height += StatusHeight(new GUIContent(ArtworkGapLabel(item)), textWidth);
                    _rowOffsets.Add(_rowOffsets[^1] + height);
                }
            }

            float StatusHeight(GUIContent content, float width) => Mathf.Max(RowLine, _statusStyle.CalcHeight(content, width));

            static string ArtworkGapLabel(DraftItem item) => item.artworkGaps == 1 ? Loc.T("1 image layer needs artwork") :
                Loc.F("{0} image layers need artwork", item.artworkGaps);

            bool DrawDraft(DraftItem item, float y, float width, float textWidth, float buttonWidth, GUIContent open)
            {
                GUI.Label(new Rect(0f, y, textWidth, RowLine), new GUIContent(item.name, item.name), EditorStyles.miniBoldLabel);
                var location = item.missingSource ? item.folder : Loc.F("{0} · {1}", item.texture, item.folder);
                GUI.Label(new Rect(0f, y + RowLine, textWidth, RowLine), new GUIContent(location, location), EditorStyles.miniLabel);
                float statusY = y + 2f * RowLine;
                if (item.missingSource) statusY += DrawStatus(item.texture, statusY, textWidth);
                if (item.artworkGaps > 0) DrawStatus(ArtworkGapLabel(item), statusY, textWidth);
                var button = new Rect(width - buttonWidth, y, buttonWidth, RowLine);
                open.tooltip = item.name;
                bool pressed = GUI.Button(button, open, EditorStyles.miniButton);
                StudioGUI.Count("drafts.open", button);
                return pressed;
            }

            float DrawStatus(string message, float y, float width)
            {
                var content = new GUIContent(message);
                float height = StatusHeight(content, width);
                GUI.Label(new Rect(0f, y, width, height), content, _statusStyle);
                return height;
            }
        }

        void DrawSaveDraftGroup(bool hasLayers)
        {
            if (!StudioGUI.BeginFoldGroup(ref _openSaveDrafts, Loc.T("Drafts"), infoId: "asset"))
            {
                StudioGUI.EndGroup();
                return;
            }
            GUILayout.Label(Loc.T("Keep your layers in this project so you can continue later."), StudioGUI.Hint);
            using (new EditorGUI.DisabledScope(!hasLayers))
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField(Loc.T("Draft name"), _draftName ?? DefaultDraftName());
                if (EditorGUI.EndChangeCheck()) _draftName = name;
                bool save = GUILayout.Button(Loc.T("Save draft"));
                StudioGUI.Count("save.saveLater", GUILayoutUtility.GetLastRect());
                if (save && SaveProjectDraft(_draftName) != null) _draftName = null;
            }
            DrawBrowseDraftsButton();
            if (!string.IsNullOrEmpty(_lastSavedEdit) && !_unsaved)
                GUILayout.Label(Loc.F("Last saved: {0}", ProjectDraftName(_lastSavedEdit)), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }
    }
}
