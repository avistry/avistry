using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _sceneGradientUseDefaults;
        [SerializeField] bool _scenePlacedGradientOptions;
        static string[] s_compactGradientBlendNames;
        bool EditingPlacedSceneGradient => !_sceneGradientUseDefaults && SelectedGradient?.sceneProjection == true;

        void DrawSceneGradientHint(DrawnGradientLayer selected)
        {
            var draft = _sceneGradientDraft;
            bool adjusting = draft != null ? _sceneGradientOriginal != null : selected != null;
            string mode = adjusting ? Loc.T("Selected gradient") : Loc.T("New gradient");
            string scope = SceneGradientScope(draft ?? selected);
            string gesture = draft != null && _sceneGradientReady ? Loc.T("Release to finish. Esc cancels.")
                : adjusting ? Loc.T("Drag dots to adjust, the square to move, or the model to add a gradient. Esc cancels.")
                : Loc.T("Drag from the model. Esc cancels.");
            DrawSceneOverlay($"<b>Avistry · {mode} · {scope}</b>   {gesture} · " +
                Loc.T("The line can extend past the model.") + " · " + Loc.T("Shared texture pixels have one colour."));
        }

        string SceneGradientScope(DrawnGradientLayer layer)
        {
            if (layer?.maskMode == MaskMode.WholeTexture) return Loc.T("All edited parts");
            bool faces = layer == null ? HasFaceSelection : layer.maskMode == MaskMode.SelectedFaces;
            bool inverted = layer?.invertMask == true;
            int count;
            if (faces)
            {
                if (layer != null && !HasCurrentFaceLimit(layer)) return Loc.T("Face limit needs updating");
                count = (layer?.faces ?? _selectedFaces).Count(IsCurrentFace);
                if (count == 0) return Loc.T("Face limit needs updating");
            }
            else
            {
                count = (layer?.islands ?? _selection).Count(IsCurrentPiece);
                if (count == 0) return layer == null || inverted ? Loc.T("All edited parts") : Loc.T("No matching pieces");
            }

            string plural = count == 1 ? "" : "s";
            string scope = faces ? Loc.F("{0} face{1}", count, plural) : Loc.F("{0} piece{1}", count, plural);
            return inverted ? Loc.F("Except {0}", scope) : scope;
        }

        void DrawSceneGradientMode(float width, ref Action change)
        {
            int mode = _sceneGradientUseDefaults ? 0 : 1;
            int next = WrapToolbar(mode, new[]
            {
                new GUIContent(Loc.T("New gradient"), GradientChoiceHint(Loc.T("Set up the next gradient you draw."))),
                new GUIContent(Loc.T("Selected layer"), GradientChoiceHint(Loc.T("Change the gradient already on the model."))),
            }, EditorStyles.miniButton, 20f, width, navigate: true);
            StudioGUI.Count("scene.gradientSettingsMode", GUILayoutUtility.GetLastRect());
            if (next != mode)
                change = () => ChangeSceneBar("Choose gradient settings", () => _sceneGradientUseDefaults = next == 0);
        }

        void DrawSceneGradientFrame(SceneView scene, float width, ref Action change)
        {
            var layer = SelectedGradient;
            if (!SceneGradientActive || !EditingPlacedSceneGradient || layer?.enabled != true) return;
            var button = new GUIContent(Loc.T("Frame gradient"), Loc.T("Fit this gradient’s start and end in this Scene view."));
            using (new EditorGUI.DisabledScope(_needsRebuild || _sceneGradientDraft != null))
                if (SceneSelectionButton(width, "scene.frameGradient", button))
                    change = () => FramePlacedSceneGradient(scene, layer);
        }

        void FramePlacedSceneGradient(SceneView scene, DrawnGradientLayer layer)
        {
            if (scene == null || _needsRebuild || !SceneGradientActive || !EditingPlacedSceneGradient ||
                _sceneGradientDraft != null || SelectedGradient != layer || !layer.enabled) return;
            var root = _uv.AvatarRoot();
            if (root == null || !StageUtility.GetCurrentStageHandle().Contains(root.gameObject)) return;

            var start = root.TransformPoint(layer.sceneStart);
            var end = root.TransformPoint(layer.sceneEnd);
            float span = Vector3.Distance(start, end);
            if (!float.IsFinite(span) || span <= 0f) return;
            var bounds = new Bounds(start, Vector3.zero);
            bounds.Encapsulate(end);
            bounds.Expand(span * 0.4f);
            scene.Frame(bounds);
        }

        void DrawPlacedSceneGradientControls(DrawnGradientLayer layer, float width, ref Action change)
        {
            GUILayout.Label(new GUIContent(layer.name, layer.name), EditorStyles.miniBoldLabel, GUILayout.MaxWidth(width));
            var rect = SceneBarRow(width);
            EditorGUI.BeginChangeCheck();
            var ramp = EditorGUI.GradientField(rect, new GUIContent(Loc.T("Colours"),
                Loc.T("Edit the colours and opacity stops of the selected layer.")), CopyRamp(layer.gradient));
            bool rampChanged = EditorGUI.EndChangeCheck();
            StudioGUI.Count("scene.placedGradientRamp", rect);

            var shape = (GradientShape)ToolPalette((int)layer.shape, GradientShapeGlyphs, PaintLabels.gradientShape, width, scene: true);
            StudioGUI.Count("scene.placedGradientShape", GUILayoutUtility.GetLastRect());
            if (!Advanced) DrawGradientDirection(width);
            if (TryPlacedSceneGradientAngle(layer, out _, out _, out _, out _, out float angle))
            {
                var angleRect = SceneBarRow(width);
                var label = new GUIContent(Loc.T("Angle (°)"),
                    Loc.T("Turn this gradient around its centre. 0° points right and 90° points up on the avatar."));
                EditorGUI.BeginChangeCheck();
                float next = EditorGUI.DelayedFloatField(angleRect, label, Mathf.Round(angle * 10f) * 0.1f);
                if (EditorGUI.EndChangeCheck()) change = () => SetPlacedSceneGradientAngle(layer, next);
                StudioGUI.Count("scene.placedGradientAngle", angleRect);
            }
            float flatness = shape == GradientShape.Ellipse
                ? DrawSceneSlider(new GUIContent(Loc.T("Flatness")), layer.flatness, .05f, 1f, width, "scene.placedGradientFlatness")
                : layer.flatness;
            float opacity = DrawSceneSlider(new GUIContent(Loc.T("Opacity"), Loc.T("Opacity of the selected gradient layer.")),
                layer.opacity, 0f, 1f, width, "scene.placedGradientOpacity");

            var edge = layer.edge;
            var blend = layer.blend;
            var options = SceneBarRow(width);
            bool open = StudioGUI.Foldout(options, _scenePlacedGradientOptions, new GUIContent(Loc.T("Gradient options")));
            StudioGUI.Count("scene.placedGradientOptions", options);
            if (open != _scenePlacedGradientOptions)
                change = () => ChangeSceneBar("Toggle gradient options", () => _scenePlacedGradientOptions = open);
            if (_scenePlacedGradientOptions)
            {
                blend = DrawSceneGradientBlendChoice(blend, "scene.placedGradientBlend", width);
                edge = DrawGradientEdgeChoice(edge, "scene.placedGradientEdge", width);
                if (Advanced) DrawGradientDirection(width);
            }

            if (rampChanged || shape != layer.shape || flatness != layer.flatness || opacity != layer.opacity ||
                edge != layer.edge || blend != layer.blend)
                change = () => UpdatePlacedSceneGradient(layer, rampChanged ? ramp : null, shape, edge, flatness, opacity, blend);
        }

        static BlendMode DrawSceneGradientBlendChoice(BlendMode blend, string controlId, float width)
        {
            var label = new GUIContent(Loc.T("Blend mode"), Loc.T("How the gradient mixes with the texture. Normal paints it on; Multiply shades; Color tints but keeps shading."));
            var next = SteppedChoice(label, blend, AllBlendModes,
                (rect, current) => width < StackedChoiceWidth ? CompactGradientBlendPopup(rect, current)
                    : (BlendMode)EditorGUI.EnumPopup(rect, current), width);
            StudioGUI.Count(controlId, GUILayoutUtility.GetLastRect());
            return next;
        }

        static BlendMode CompactGradientBlendPopup(Rect rect, BlendMode mode)
        {
            s_compactGradientBlendNames ??= AllBlendModes.Select(value =>
            {
                string name = value.ToString();
                string path = typeof(BlendMode).GetField(name).GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? name;
                return path.Substring(path.LastIndexOf('/') + 1);
            }).ToArray();
            int index = EditorGUI.Popup(rect, Array.IndexOf(AllBlendModes, mode), s_compactGradientBlendNames);
            return index >= 0 ? AllBlendModes[index] : mode;
        }

        bool UpdatePlacedSceneGradient(DrawnGradientLayer layer, Gradient ramp, GradientShape shape,
            GradientEdge edge, float flatness, float opacity, BlendMode blend)
        {
            if (_needsRebuild || _uv == null || _comp == null || SelectedGradient != layer || !layer.sceneProjection) return false;
            if (!EditGradientLayer(layer, candidate =>
            {
                if (ramp != null) candidate.gradient = CopyRamp(ramp);
                candidate.shape = shape;
                candidate.edge = edge;
                candidate.flatness = flatness;
                candidate.opacity = opacity;
                candidate.blend = blend;
            }, isolateUndo: shape != layer.shape)) return false;
            CancelSceneGradient(restoreComparison: false);
            if (_sceneGradientRecovery != null && _toastText == _sceneGradientRecovery.reason) _toastText = null;
            _sceneGradientRecovery = null;
            ComposeNow();
            return true;
        }

        void OpenPlacedSceneGradientInPaint()
        {
            SetTab(Tab.Paint);
            RevealSelectedGradient();
            Focus();
        }
    }
}
