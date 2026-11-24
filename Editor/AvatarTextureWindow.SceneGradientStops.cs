using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        SceneGradientStopWindow _sceneGradientStopWindow;

        void DrawSceneGradientStopsButton(SceneView scene, float width, ref Action change)
        {
            var layer = EditingPlacedSceneGradient ? SelectedGradient : null;
            var button = new GUIContent(Loc.T("Edit stops"),
                layer == null ? Loc.T("Edit the colours and opacity of the next gradient.")
                    : Loc.T("Edit the colours and opacity stops of the selected layer."));
            using (new EditorGUI.DisabledScope(_needsRebuild || _sceneGradientDraft != null))
                if (SceneSelectionButton(width, layer == null ? "scene.gradientStops" : "scene.placedGradientStops", button))
                    change = () => OpenSceneGradientStopWindow(scene, layer);
        }

        void OpenSceneGradientStopWindow(SceneView scene, DrawnGradientLayer layer)
        {
            if (scene == null || _recipe == null || CurrentSlot == null || !SceneGradientStopTarget(layer)) return;

            if (_sceneGradientStopWindow != null)
            {
                if (_sceneGradientStopWindow.IsFor(layer))
                {
                    _sceneGradientStopWindow.Focus();
                    return;
                }
                _sceneGradientStopWindow.Close();
            }
            var original = layer == null ? NewGradientRamp(_grad.colours) : CopyRamp(layer.gradient);
            var source = _grad.colours;
            var targetMatches = CaptureSavedEditContext(_recipe, CurrentSlot);
            _sceneGradientStopWindow = CreateInstance<SceneGradientStopWindow>();
            _sceneGradientStopWindow.Open(this, scene, layer, original, () =>
                scene != null && SceneGradientStopTarget(layer) && targetMatches() &&
                (layer != null ? SameRamp(original, layer.gradient)
                    : _grad.colours == source && SameRamp(original, NewGradientRamp(source))));
        }

        bool SceneGradientStopTarget(DrawnGradientLayer layer)
        {
            if (!SceneGradientActive || _needsRebuild || _sceneGradientDraft != null || _gradDrag != null ||
                !SceneBarVisible || SceneBarWindow() != this || !CanPaint(out _)) return false;
            var root = _uv.AvatarRoot();
            if (root == null || !StageUtility.GetCurrentStageHandle().Contains(root.gameObject)) return false;
            return layer == null ? !EditingPlacedSceneGradient : EditingPlacedSceneGradient && SelectedGradient == layer;
        }

        static bool SameRamp(Gradient left, Gradient right) => right != null &&
            left.mode == right.mode && left.colorSpace == right.colorSpace &&
            left.colorKeys.SequenceEqual(right.colorKeys) && left.alphaKeys.SequenceEqual(right.alphaKeys);

        bool ApplySceneGradientStops(DrawnGradientLayer layer, Gradient ramp)
        {
            bool applied = true;
            Undo.IncrementCurrentGroup();
            if (layer == null)
            {
                Undo.RecordObject(this, "Edit new gradient settings");
                StoreCustomGradientRamp(ramp);
                SelectUiChanged();
            }
            else applied = UpdatePlacedSceneGradient(layer, ramp, layer.shape, layer.edge, layer.flatness, layer.opacity, layer.blend);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            return applied;
        }

        sealed class SceneGradientStopWindow : EditorWindow
        {
            AvatarTextureWindow _owner;
            DrawnGradientLayer _layer;
            Gradient _original, _ramp;
            Func<bool> _targetMatches;
            GradientStopEditor _selection;
            Vector2 _scroll;
            string _warning;
            bool _stale;

            internal bool IsFor(DrawnGradientLayer layer) => _layer == layer && !_stale && _targetMatches?.Invoke() == true;

            internal void Open(AvatarTextureWindow owner, SceneView scene, DrawnGradientLayer layer, Gradient original,
                Func<bool> targetMatches)
            {
                _owner = owner;
                _layer = layer;
                _original = original;
                _ramp = CopyRamp(original);
                _targetMatches = targetMatches;
                titleContent = new GUIContent(layer == null ? Loc.T("New gradient") : Loc.T("Selected gradient"));
                var size = new Vector2(334f, 318f);
                minSize = maxSize = size;
                var view = scene.position;
                ShowUtility();
                position = new Rect(view.x + Mathf.Max(0f, view.width - size.x - 8f),
                    view.y + Mathf.Clamp(view.height - size.y, 0f, 40f), size.x, size.y);
            }

            void OnDisable()
            {
                if (_owner != null && _owner._sceneGradientStopWindow == this) _owner._sceneGradientStopWindow = null;
            }

            void OnGUI()
            {
                var input = Event.current;
                if (_owner == null || _targetMatches == null || input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
                {
                    Close();
                    GUIUtility.ExitGUI();
                    return;
                }

                bool apply, cancel;
                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(_stale))
                        {
                            var button = new GUIContent(Loc.T("Apply"));
                            var rect = GUILayoutUtility.GetRect(button, EditorStyles.miniButton, GUILayout.ExpandWidth(true));
                            if (input.type == EventType.MouseDown && input.button == 0 && rect.Contains(input.mousePosition))
                                GUIUtility.keyboardControl = 0;
                            apply = GUI.Button(rect, button, EditorStyles.miniButton);
                        }
                        cancel = GUILayout.Button(Loc.T("Cancel"), EditorStyles.miniButton);
                    }

                    using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
                    {
                        _scroll = scroll.scrollPosition;
                        if (_layer != null) GUILayout.Label(new GUIContent(_layer.name, _layer.name), EditorStyles.miniBoldLabel);
                        using (new EditorGUI.DisabledScope(_stale))
                        {
                            _ramp = EditorGUILayout.GradientField(_ramp);
                            string error = null;
                            if (_owner.DrawGradientStopControls(_ramp, ref _selection, position.width - 30f, message => error = message))
                                _warning = null;
                            if (error != null) _warning = error;
                        }
                    }
                    if (_warning != null) EditorGUILayout.HelpBox(_warning, MessageType.Info);
                }

                if (apply)
                {
                    Undo.FlushUndoRecordObjects();
                    if (!_targetMatches())
                    {
                        _stale = true;
                        _warning = Loc.T("The edit changed. Reopen the stop editor.");
                        Repaint();
                        return;
                    }
                    if (!SameRamp(_original, _ramp) && !_owner.ApplySceneGradientStops(_layer, _ramp))
                    {
                        _warning = _owner._toastText;
                        Repaint();
                        return;
                    }
                }
                if (!apply && !cancel) return;
                Close();
                GUIUtility.ExitGUI();
            }
        }
    }
}
