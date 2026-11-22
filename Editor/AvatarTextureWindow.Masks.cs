using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Mask builder UI (1 · Pick): lists the mask slots of the material that have no texture yet and creates one to
    /// paint. Also the per-layer channel switches for masks that pack several things into R, G, B and A.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _foldMaskBuilder;
        [SerializeField] bool _foldMaskOff;
        List<MaskBuilder.EmptySlot> _emptyMasks;
        Material _emptyMasksFor;
        Shader _emptyMasksShader;
        int _emptyMasksStamp;
        Vector2 _maskListScroll;

        List<MaskBuilder.EmptySlot> EmptyMasks()
        {
            int stamp = _material != null ? _material.ComputeCRC() : 0;
            if (_emptyMasks != null && _emptyMasksFor == _material && _material != null && _emptyMasksShader == _material.shader && _emptyMasksStamp == stamp) return _emptyMasks;
            _emptyMasksStamp = stamp;
            _emptyMasksFor = _material;
            _emptyMasksShader = _material != null ? _material.shader : null;
            _emptyMasks = MaskBuilder.EmptySlots(_material);
            return _emptyMasks;
        }

        void DrawMaskBuilderGroup()
        {
            if (_material == null) return;
            var all = EmptyMasks();
            if (all.Count == 0) return;
            var on = all.Where(s => !s.featureOff && s.known).ToList();
            var rest = all.Where(s => s.featureOff || !s.known).ToList();

            StudioGUI.Count("masks.foldout");
            _foldMaskBuilder = EditorGUILayout.Foldout(_foldMaskBuilder, new GUIContent(Loc.F("Add a mask ({0} available)", on.Count),
                Loc.T("Masks limit a shader effect (glow, rim light, shine…) to part of the model. Create one here, then paint it.")), true);
            if (!_foldMaskBuilder) return;

            StudioGUI.BeginGroup(Loc.T("Masks this material can use"), infoId: "mask");
            StudioGUI.Count("masks.rows");
            GUILayout.Label(Loc.T("A mask is a black and white texture: white = the effect shows, black = it doesn't. Create one, then paint it in 4 · Paint."), StudioGUI.Hint);
            if (on.Count == 0) GUILayout.Label(Loc.T("Every mask of the effects that are switched on already has a texture."), StudioGUI.CheckLabel);
            foreach (var s in on) DrawEmptyMaskRow(s);
            if (rest.Count > 0)
            {
                StudioGUI.Count("masks.more");
                _foldMaskOff = EditorGUILayout.Foldout(_foldMaskOff, new GUIContent(Loc.F("More masks ({0})", rest.Count), Loc.T("Masks of effects that are switched off on this material, and less common ones")), true);
                if (_foldMaskOff)
                {
                    _maskListScroll = EditorGUILayout.BeginScrollView(_maskListScroll, GUILayout.MaxHeight(180));
                    foreach (var s in rest) DrawEmptyMaskRow(s);
                    EditorGUILayout.EndScrollView();
                }
            }
            StudioGUI.EndGroup();
        }

        void DrawEmptyMaskRow(MaskBuilder.EmptySlot s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string note = s.featureOff ? Loc.T("  (effect is off)") : "";
                string tip = $"{s.property}" + (s.channelInfo != null ? $"\n{s.channelInfo}" : "") +
                             (s.featureOff ? "\n" + Loc.T("This effect is switched off on the material, so the mask won't show until you switch it on in the material's inspector.") : "");
                GUILayout.Label(new GUIContent(s.displayName + note, tip), s.featureOff ? StudioGUI.RowLabelDisabled : StudioGUI.RowLabel, GUILayout.MinWidth(40));
                GUILayout.FlexibleSpace();
                if (_selection.Count > 0 && _uv != null &&
                    GUILayout.Button(new GUIContent(Loc.T("From selection"), Loc.F("Create it white on the {0} selected piece(s) and black everywhere else", _selection.Count)), EditorStyles.miniButtonLeft, GUILayout.Width(96)))
                { var slot = s; EditorApplication.delayCall += () => { if (this == null) return; CreateMask(slot, true); }; }
                if (GUILayout.Button(new GUIContent(Loc.T("Create"), s.startsWhite
                        ? Loc.T("Create it all white (the effect stays everywhere, so nothing looks different yet). Then paint black where the effect should be off.")
                        : Loc.T("Create it all black (the shader's starting value). Then paint white where the effect should show.")),
                        _selection.Count > 0 && _uv != null ? EditorStyles.miniButtonRight : EditorStyles.miniButton, GUILayout.Width(58)))
                { var slot = s; EditorApplication.delayCall += () => { if (this == null) return; CreateMask(slot, false); }; }
            }
        }

        void CreateMask(MaskBuilder.EmptySlot s, bool fromSelection)
        {
            if (_material == null || (_material.HasProperty(s.property) && _material.GetTexture(s.property) != null)) { _emptyMasks = null; Repaint(); return; } // already filled (Undo / Inspector edit)
            if (!ConfirmTargetChange("texture")) return;
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
            var reference = _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;
            if (reference == null) reference = CurrentSlot?.texture;
            Texture2D tex;
            try
            {
                EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.F("Creating the {0}…", s.displayName), 0.5f);
                tex = MaskBuilder.Create(_material, s, reference, _uv, fromSelection ? new List<int>(_selection) : null);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                Toast(Loc.T("The mask texture couldn't be created. See the Console for details."), MessageType.Warning, 6f);
                return;
            }
            finally { EditorUtility.ClearProgressBar(); }
            if (tex == null) { Toast(Loc.T("The mask texture couldn't be created."), MessageType.Error, 6f); return; }

            _emptyMasks = null;
            Learn.Did(LearnEvents.MaskMade);
            _pendingTexture = tex;
            _selection.Clear();
            Rebuild();
            // Paint with the colour that changes something: black on a white mask, white on a black one.
            bool paintBlack = s.startsWhite && !fromSelection;
            _brush.color = paintBlack ? Color.black : Color.white;
            _brush.subColor = paintBlack ? Color.white : Color.black;
            if (_comp != null) SetTab(Tab.Paint);
            // M21 describes the all-white mask; the other two starting states keep their own lines.
            string created = paintBlack ? Msg.M21MaskCreated(_material.name)
                : Loc.F("Created the {0} ({1}) and assigned it to “{2}”. ", s.displayName, tex.name, _material.name) +
                  (fromSelection ? Loc.T("It's white on the pieces you had selected and black elsewhere.")
                      : Loc.T("It's all black. Paint white where the effect should show (X swaps to black)."));
            string path = AssetDatabase.GetAssetPath(tex);
            var featureSlot = new TextureSlot { featureToggle = s.featureToggle };
            var toastMaterial = _material; // the toast can outlive a change of target
            if (s.featureOff)
                Toast(created, MessageType.Warning, 8f, Msg.BtnShowFile, () => ShowFileAndLearn(path), Msg.BtnSwitchEffectOn, () => SwitchFeatureOnFor(toastMaterial, featureSlot));
            else
                Toast(created, MessageType.Info, 8f, Msg.BtnShowFile, () => ShowFileAndLearn(path), null, null);
        }

        /// <summary>Layer settings: which channels the layer writes. Shown for mask textures (or when already changed).</summary>
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
