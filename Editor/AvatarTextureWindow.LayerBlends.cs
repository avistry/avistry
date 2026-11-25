using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _foldAdditionalBlends;

        void DrawAdditionalBlends(Layer layer)
        {
            int count = layer.additionalBlends?.Count ?? 0;
            string heading = count == 0 ? Loc.T("Additional blends") : Loc.F("Additional blends ({0})", count);
            _foldAdditionalBlends = StudioGUI.Foldout(_foldAdditionalBlends, new GUIContent(heading));
            if (!_foldAdditionalBlends) return;

            GUILayout.Label(Loc.T("Blend above applies first. Additional blends run from top to bottom. Layer opacity and masks apply once."),
                StudioGUI.Hint);
            if (!layer.chR && !layer.chG && !layer.chB)
                GUILayout.Label(Loc.T("Enable an RGB channel to see these blends."), StudioGUI.Hint);
            Action pending = null;
            for (int index = 0; index < count; index++) DrawAdditionalBlend(layer, index, count, ref pending);
            using (new EditorGUI.DisabledScope(count >= Layer.MaxAdditionalBlends))
            {
                if (GUILayout.Button(Loc.T("Add blend"), EditorStyles.miniButton))
                    pending = () => EditAdditionalBlends(layer, Loc.T("Add blend"), passes =>
                        passes.Add(new LayerBlendPass { enabled = true, mode = BlendMode.Multiply, strength = 0.5f }));
            }
            pending?.Invoke();
        }

        void DrawAdditionalBlend(Layer layer, int index, int count, ref Action pending)
        {
            var step = layer.additionalBlends[index];
            int move = 0;
            bool remove;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    var eye = StudioGUI.Icon(step.enabled ? "scenevis_visible_hover" : "scenevis_hidden_hover", "",
                        step.enabled ? Loc.T("Disable this blend") : Loc.T("Enable this blend"));
                    step.enabled = GUILayout.Toggle(step.enabled, eye, EditorStyles.miniButton, GUILayout.Width(23));
                    step.mode = BlendPopup(GUIContent.none, step.mode);
                    using (new EditorGUI.DisabledScope(index == 0))
                        if (GUILayout.Button(new GUIContent("▲", Loc.T("Apply this blend earlier")), EditorStyles.miniButtonLeft,
                                GUILayout.Width(23))) move = -1;
                    using (new EditorGUI.DisabledScope(index == count - 1))
                        if (GUILayout.Button(new GUIContent("▼", Loc.T("Apply this blend later")), EditorStyles.miniButtonMid,
                                GUILayout.Width(23))) move = 1;
                    remove = GUILayout.Button(StudioGUI.Icon("TreeEditor.Trash", "", Loc.T("Remove blend")),
                        EditorStyles.miniButtonRight, GUILayout.Width(23));
                }
                step.strength = EditorGUILayout.Slider(Loc.T("Strength (%)"), step.strength * 100f, 0f, 100f) / 100f;
                if (EditorGUI.EndChangeCheck())
                    pending = () => EditAdditionalBlends(layer, Loc.T("Edit blend"), passes => passes[index] = step);
            }
            if (remove) pending = () => EditAdditionalBlends(layer, Loc.T("Remove blend"), passes => passes.RemoveAt(index));
            else if (move != 0)
                pending = () => EditAdditionalBlends(layer, Loc.T("Move blend"), passes =>
                    (passes[index], passes[index + move]) = (passes[index + move], passes[index]));
        }

        void EditAdditionalBlends(Layer layer, string undoName, Action<List<LayerBlendPass>> edit)
        {
            Undo.RecordObject(_recipe, undoName);
            edit(layer.additionalBlends ??= new List<LayerBlendPass>());
            MarkEdited();
            if (layer.additionalBlends.Count > 0) LearnLater(LearnEvents.AdditionalBlend);
        }
    }
}
