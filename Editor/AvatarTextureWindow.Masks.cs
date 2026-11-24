using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _foldMaskBuilder;
        [SerializeField] string _maskProperty;
        List<MaskBuilder.EmptySlot> _emptyMasks;
        GUIContent[] _maskChoices;
        int[] _maskIndices;
        Material _emptyMasksFor;
        Shader _emptyMasksShader;
        int _emptyMasksStamp;
        string _maskLanguage;
        bool _maskFromSelection;

        List<MaskBuilder.EmptySlot> EmptyMasks()
        {
            bool same = _emptyMasks != null && _emptyMasksFor == _material && _material != null &&
                _emptyMasksShader == _material.shader && _maskLanguage == Loc.Current;
            if (same && Event.current != null && Event.current.type != EventType.Layout) return _emptyMasks;
            int stamp = _material != null ? _material.ComputeCRC() : 0;
            if (same && _emptyMasksStamp == stamp) return _emptyMasks;
            _emptyMasksStamp = stamp;
            _emptyMasksFor = _material;
            _emptyMasksShader = _material != null ? _material.shader : null;
            _maskLanguage = Loc.Current;
            _emptyMasks = MaskBuilder.EmptySlots(_material);
            _maskChoices = _emptyMasks.Select(slot => new GUIContent(
                slot.displayName + (slot.featureOff ? Loc.T(" (effect off)") : ""), slot.property)).ToArray();
            _maskIndices = Enumerable.Range(0, _maskChoices.Length).ToArray();
            return _emptyMasks;
        }

        void DrawMaskBuilderGroup()
        {
            if (_material == null) return;
            var all = EmptyMasks();
            if (all.Count == 0) return;
            bool open = StudioGUI.BeginFoldGroup(ref _foldMaskBuilder, Loc.T("Add a mask"), Loc.F("{0} available", all.Count),
                tooltip: Loc.T("Choose a shader effect to mask, then add a texture you can paint."), infoId: "mask");
            StudioGUI.Count("masks.foldout", GUILayoutUtility.GetLastRect());
            if (open)
            {
                if (_pixelJob != null || _aoSteps != null)
                {
                    EditorGUILayout.LabelField(_pixelJob?.Title ?? Loc.T("Ambient occlusion"), StudioGUI.Hint);
                    if (GUILayout.Button(Msg.BtnCancel, EditorStyles.miniButton))
                    {
                        if (_pixelJob != null) _pixelJob.Cancel(); else _aoCancel.Cancel();
                    }
                    StudioGUI.EndGroup();
                    return;
                }
                int index = Mathf.Max(0, all.FindIndex(slot => slot.property == _maskProperty));
                MaskBuilder.EmptySlot chosen;
                bool add;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = EditorGUILayout.GetControlRect(GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.Height(22f));
                    StudioGUI.Count("masks.rows", rect);
                    int picked = all.Count > 1
                        ? SteppedChoice(rect, index, _maskIndices, (field, value) => EditorGUI.Popup(field, value, _maskChoices))
                        : EditorGUI.Popup(rect, index, _maskChoices);
                    chosen = all[picked];
                    if (picked != index)
                    {
                        Undo.FlushUndoRecordObjects();
                        Undo.IncrementCurrentGroup();
                        Undo.RecordObject(this, "Choose mask slot");
                        _maskProperty = chosen.property;
                        Undo.FlushUndoRecordObjects();
                        Undo.IncrementCurrentGroup();
                    }
                    add = GUILayout.Button(new GUIContent(Loc.T("Add"), Loc.T("Create this mask and open it in Paint.")),
                        GUILayout.Height(22f), GUILayout.MinWidth(45f));
                    StudioGUI.Count("masks.add", GUILayoutUtility.GetLastRect());
                }

                if (!string.IsNullOrEmpty(chosen.channelInfo)) GUILayout.Label(chosen.channelInfo, StudioGUI.Hint);
                if (chosen.featureOff)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Switch on this effect to see the mask on the avatar."));
                int pieces = _uv != null ? _selection.Count(IsCurrentPiece) : 0;
                bool canUsePieces = pieces > 0 && _uv.uvChannel == chosen.uvChannel;
                if (pieces > 0)
                {
                    using (new EditorGUI.DisabledScope(!canUsePieces))
                        _maskFromSelection = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Start from selected pieces"),
                            Loc.T("Start white on the selected pieces and black everywhere else.")), _maskFromSelection && canUsePieces);
                    if (!canUsePieces)
                        GUILayout.Label(Loc.F("To start from pieces, select them on a texture that uses UV{0}.", chosen.uvChannel), StudioGUI.Hint);
                }
                if (add) QueueCreateMask(chosen, canUsePieces && _maskFromSelection);
            }
            StudioGUI.EndGroup();
        }

        void QueueCreateMask(MaskBuilder.EmptySlot slot, bool fromSelection)
        {
            var renderer = _renderer;
            var material = _material;
            if (material == null) return;
            int index = _materialIndex;
            int revision = _historyRevision;
            var uv = _uv;
            var shader = material.shader;
            var pieces = fromSelection ? _selection.Where(IsCurrentPiece).ToList() : null;
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                bool TargetMatches()
                {
                    if (renderer == null || renderer != _renderer || material == null || material != _material ||
                        index != _materialIndex || revision != _historyRevision || !ReferenceEquals(uv, _uv) || _needsRebuild) return false;
                    var assigned = renderer.sharedMaterials;
                    return index >= 0 && index < assigned.Length && assigned[index] == material &&
                        material.HasProperty(slot.property) && material.GetTexture(slot.property) == null &&
                        (pieces == null || TargetResolver.ReadUvChannel(material, slot.property) == uv.uvChannel);
                }
                if (TargetMatches() && material.shader == shader) CreateMask(slot, pieces, TargetMatches);
                else MaskTargetChanged();
            };
        }

        void MaskTargetChanged()
        {
            _emptyMasks = null;
            Toast(Loc.T("The mask slot or target changed. Select a mask and try again."), MessageType.Warning);
        }

        void CreateMask(MaskBuilder.EmptySlot s, List<int> pieces, System.Func<bool> targetMatches)
        {
            if (_pixelJob != null || _aoSteps != null) return;
            bool fromSelection = pieces != null && pieces.Count > 0;
            if (!PlanTargetChange("texture", out var switchLayers)) return;
            if (!targetMatches()) { MaskTargetChanged(); return; }
            if (MaterialWriter.IsPoiyomiLocked(_material))
            {
                if (!ConfirmUnlock(new[] { _material }, "mask")) return;
                EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Unlocking the material…"), 0.5f);
                try { MaterialWriter.UnlockPoiyomi(new[] { _material }); }
                finally { EditorUtility.ClearProgressBar(); }
                if (MaterialWriter.IsPoiyomiLocked(_material))
                {
                    Toast(Loc.T("The material couldn't be unlocked. Unlock it in the Poiyomi inspector, then try again."), MessageType.Warning, 6f);
                    return;
                }
            }
            if (!targetMatches()) { MaskTargetChanged(); return; }
            var material = _material;
            var shader = material.shader;
            var reference = material.mainTexture;
            if (reference == null) reference = CurrentSlot?.texture;
            var size = MaskBuilder.Dimensions(reference);
            var input = fromSelection ? _uv.CopyIslandRasterInput() : null;
            bool white = s.startsWhite;
            bool CanAssign() => targetMatches() && material.shader == shader && _liveStroke == null &&
                _sceneStrokeDraft == null && _gradDrag == null && _imgDrag == null && _sceneGradientDraft == null;
            void Complete(byte[] png)
            {
                if (!CanAssign()) { MaskTargetChanged(); return; }
                int undoGroup = -1;
                const string undoName = "Add mask texture";
                try
                {
                    var tex = MaskBuilder.ImportAndAssign(material, s, reference, size, png, CanAssign, () =>
                    {
                        Undo.FlushUndoRecordObjects();
                        Undo.IncrementCurrentGroup();
                        undoGroup = Undo.GetCurrentGroup();
                        Undo.RecordObjects(new Object[] { this, _recipe }, undoName);
                    }, out bool assigned);
                    if (tex == null) { Toast(Loc.T("The mask texture couldn't be created."), MessageType.Error, 6f); return; }
                    if (!assigned)
                    {
                        _emptyMasks = null;
                        string path = AssetDatabase.GetAssetPath(tex);
                        Toast(Loc.T("The mask file is ready. The target changed before it could be added."), MessageType.Warning, 6f,
                            Msg.BtnShowFile, () => ShowFileAndLearn(path));
                        return;
                    }
                    switchLayers();
                    OpenCreatedMask(s, fromSelection, tex);
                }
                finally
                {
                    if (undoGroup >= 0)
                    {
                        Undo.SetCurrentGroupName(undoName);
                        Undo.FlushUndoRecordObjects();
                        Undo.CollapseUndoOperations(undoGroup);
                        Undo.IncrementCurrentGroup();
                    }
                }
            }
            void Failure(System.Exception error)
            {
                Debug.LogException(error);
                Toast(Loc.T("The mask texture couldn't be created. See the Console for details."), MessageType.Warning, 6f);
            }
            try
            {
                _pixelJob = new EditorPixelJob(Loc.F("Creating the {0}…", s.displayName),
                    (token, progress) => MaskBuilder.EncodeMask(size.x, size.y, white, input, pieces, token),
                    Complete, Failure, () => { }, () => _pixelJob = null, Repaint);
            }
            catch (System.Exception error) { Failure(error); }
        }

        void OpenCreatedMask(MaskBuilder.EmptySlot s, bool fromSelection, Texture2D tex)
        {
            _emptyMasks = null;
            Learn.Did(LearnEvents.MaskMade);
            RememberSelectionBeforeSwitch();
            _pendingTexture = tex;
            _selection.Clear();
            ClearFaceSelection();
            Rebuild();
            bool paintBlack = s.startsWhite && !fromSelection;
            _brush.color = paintBlack ? Color.black : Color.white;
            _brush.subColor = paintBlack ? Color.white : Color.black;
            if (_comp != null) SetTab(Tab.Paint);
            string created = paintBlack ? Msg.M21MaskCreated(_material.name)
                : Loc.F("Created the {0} ({1}) and assigned it to “{2}”. ", s.displayName, tex.name, _material.name) +
                  (fromSelection ? Loc.T("It's white on the pieces you had selected and black elsewhere.")
                      : Loc.T("It's all black. Paint white where the effect should show (X swaps to black)."));
            string path = AssetDatabase.GetAssetPath(tex);
            var featureSlot = new TextureSlot { featureToggle = s.featureToggle };
            var toastMaterial = _material;
            if (s.featureOff)
                Toast(created, MessageType.Warning, 8f, Msg.BtnShowFile, () => ShowFileAndLearn(path), Msg.BtnSwitchEffectOn, () => SwitchFeatureOnFor(toastMaterial, featureSlot));
            else
                Toast(created, MessageType.Info, 8f, Msg.BtnShowFile, () => ShowFileAndLearn(path), null, null);
        }

        void DrawChannelSwitches(Layer layer, ref System.Action pending)
        {
            bool custom = !layer.chR || !layer.chG || !layer.chB || layer.chA;
            var slot = CurrentSlot;
            if (!custom && (slot == null || slot.role != TextureRole.Mask)) return;
            var l = layer;
            StudioGUI.Count("masks.channels");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(Loc.T("Channels"), Loc.T("Which channels of the texture this layer changes. Masks often pack different things into R, G, B and A.")),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                bool r = GUILayout.Toggle(layer.chR, "R", EditorStyles.miniButtonLeft);
                bool g = GUILayout.Toggle(layer.chG, "G", EditorStyles.miniButtonMid);
                bool b = GUILayout.Toggle(layer.chB, "B", EditorStyles.miniButtonMid);
                bool a = GUILayout.Toggle(layer.chA, new GUIContent("A", Loc.T("Alpha: written as the brightness of the paint")), EditorStyles.miniButtonRight);
                if (r != layer.chR || g != layer.chG || b != layer.chB || a != layer.chA)
                    pending = () =>
                    {
                        Undo.RecordObject(_recipe, "Layer channels");
                        l.chR = r; l.chG = g; l.chB = b; l.chA = a;
                        MarkEdited();
                        Learn.Did(LearnEvents.LayerChannels);
                    };
            }
            if (slot != null && !string.IsNullOrEmpty(slot.channelInfo)) GUILayout.Label(Loc.F("This texture: {0}", slot.channelInfo), StudioGUI.Hint);
            if (!layer.chR && !layer.chG && !layer.chB && !layer.chA)
            {
                StudioGUI.CheckRow(StudioGUI.Check.Problem, Msg.M22ChannelsOff());
                StudioGUI.Count("masks.turnAllOn");
                if (GUILayout.Button(Msg.BtnTurnAllOn, EditorStyles.miniButton))
                    pending = () =>
                    {
                        Undo.RecordObject(_recipe, "Turn all channels on");
                        l.chR = l.chG = l.chB = l.chA = true;
                        MarkEdited();
                    };
            }
        }
    }
}
