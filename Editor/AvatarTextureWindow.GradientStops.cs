using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        const int MaxGradientStops = 8;
        const int MinGradientStops = 2;
        const float StopTimeTolerance = 0.00002f;

        struct GradientStopEditor
        {
            public bool folded, opacity;
            public int colourIndex, opacityIndex;
        }

        GradientStopEditor _newGradientStops, _savedGradientStops;
        string _gradientStopsLayerId;

        bool DrawGradientStops(Gradient ramp, bool forNew)
        {
            if (!forNew && _gradientStopsLayerId != ActiveLayer?.id)
            {
                _gradientStopsLayerId = ActiveLayer?.id;
                _savedGradientStops.colourIndex = _savedGradientStops.opacityIndex = 0;
            }
            ref var state = ref (forNew ? ref _newGradientStops : ref _savedGradientStops);
            bool open = !state.folded;
            open = StudioGUI.Foldout(open, new GUIContent(Loc.T("Edit stops"),
                Loc.T("Add, remove or move colour and opacity stops. Choose recent colours or paste colour codes.")));
            state.folded = !open;
            if (!open) return false;

            return DrawGradientStopControls(ramp, ref state);
        }

        bool DrawGradientStopControls(Gradient ramp, ref GradientStopEditor state, float width = -1f, Action<string> reportError = null)
        {
            state.opacity = WrapToolbar(state.opacity ? 1 : 0, new[]
            {
                new GUIContent(Loc.T("Colour")),
                new GUIContent(Loc.T("Opacity"))
            }, EditorStyles.miniButton, 20f, width, navigate: true) == 1;

            var colours = ramp.colorKeys;
            var opacity = ramp.alphaKeys;
            ref int selected = ref (state.opacity ? ref state.opacityIndex : ref state.colourIndex);
            var times = state.opacity ? Array.ConvertAll(opacity, key => key.time) : Array.ConvertAll(colours, key => key.time);
            string tip = Loc.T("Use arrow keys after clicking a stop, or hold Shift while scrolling here.");
            var labels = times.Select((time, index) => new GUIContent(Loc.F("{0} · {1:0.#}%", index + 1, time * 100f), tip)).ToArray();
            selected = Mathf.Clamp(selected, 0, times.Length - 1);
            selected = WrapToolbar(selected, labels, EditorStyles.miniButton, 19f, width, navigate: true);
            StudioGUI.Count("paint.gradientStop", GUILayoutUtility.GetLastRect());

            bool add, remove;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(times.Length >= MaxGradientStops))
                    add = GUILayout.Button(new GUIContent(Loc.T("Add stop"), Loc.T("Add a stop near the selected position. Each ramp supports up to eight of each type.")), EditorStyles.miniButton);
                StudioGUI.Count("paint.gradientAddStop", GUILayoutUtility.GetLastRect());
                using (new EditorGUI.DisabledScope(times.Length <= MinGradientStops))
                    remove = GUILayout.Button(new GUIContent(Loc.T("Remove stop"), Loc.T("Remove the selected stop. Keep at least two of each type.")), EditorStyles.miniButton);
                StudioGUI.Count("paint.gradientRemoveStop", GUILayoutUtility.GetLastRect());
            }
            float shownPosition = Mathf.Round(times[selected] * 1000f) / 10f;
            float position = EditorGUILayout.DelayedFloatField(new GUIContent(Loc.T("Position (%)"),
                Loc.T("0 is the start of the ramp. 100 is the end.")), shownPosition);
            float newTime = float.IsFinite(position) ? Mathf.Clamp01(position / 100f) : times[selected];
            bool positionChanged = position != shownPosition && Mathf.Abs(newTime - times[selected]) > StopTimeTolerance;
            if (add)
            {
                int neighbour = selected < times.Length - 1 ? selected + 1 : selected - 1;
                newTime = (times[selected] + times[neighbour]) * 0.5f;
                if (newTime == times[selected]) newTime = times[selected] < 0.5f
                    ? (1f + times[selected]) * 0.5f : times[selected] * 0.5f;
                var sample = ramp.Evaluate(newTime);
                if (state.opacity) opacity = opacity.Append(new GradientAlphaKey(sample.a, newTime)).ToArray();
                else colours = colours.Append(new GradientColorKey(new Color(sample.r, sample.g, sample.b), newTime)).ToArray();
            }
            else if (remove)
            {
                int old = selected;
                if (state.opacity) opacity = opacity.Where((_, index) => index != old).ToArray();
                else colours = colours.Where((_, index) => index != old).ToArray();
                selected = Mathf.Min(selected, times.Length - 2);
            }
            else if (positionChanged)
            {
                if (state.opacity) opacity[selected].time = newTime;
                else colours[selected].time = newTime;
            }

            if (add || remove || positionChanged)
            {
                ramp.SetKeys(colours, opacity);
                if (!remove)
                {
                    times = state.opacity ? Array.ConvertAll(ramp.alphaKeys, key => key.time) : Array.ConvertAll(ramp.colorKeys, key => key.time);
                    selected = Array.FindIndex(times, time => Mathf.Abs(time - newTime) <= StopTimeTolerance);
                    if (selected < 0) selected = 0;
                }
            }

            bool valueChanged = state.opacity ? DrawGradientOpacityStop(ramp, selected)
                : DrawGradientColourStop(ramp, selected, width, reportError);
            return add || remove || positionChanged || valueChanged;
        }

        static bool DrawGradientOpacityStop(Gradient ramp, int index)
        {
            var opacity = ramp.alphaKeys;
            float value = EditorGUILayout.Slider(Loc.T("Opacity"), opacity[index].alpha, 0f, 1f);
            if (value == opacity[index].alpha) return false;
            opacity[index].alpha = value;
            ramp.SetKeys(ramp.colorKeys, opacity);
            return true;
        }

        bool DrawGradientColourStop(Gradient ramp, int index, float width, Action<string> reportError)
        {
            var keys = ramp.colorKeys;
            var colour = EditorGUILayout.ColorField(new GUIContent(Loc.T("Stop colour")), keys[index].color,
                showEyedropper: true, showAlpha: false, hdr: false);
            bool changed = colour != keys[index].color && SetGradientStopColour(ramp, index, colour);

            string shown = GradientStopCode(ramp, index);
            string code = EditorGUILayout.DelayedTextField(new GUIContent(Loc.T("Colour code"),
                Loc.T("Enter hex, RGB, HSL or HSV. An alpha value sets opacity at this stop.")), shown);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(Loc.T("Copy"), Loc.T("Copy this stop's colour code")), EditorStyles.miniButtonLeft))
                    EditorGUIUtility.systemCopyBuffer = shown;
                if (GUILayout.Button(new GUIContent(Loc.T("Paste"), Loc.T("Paste a colour code into this stop")), EditorStyles.miniButtonRight))
                    code = EditorGUIUtility.systemCopyBuffer;
            }
            if (code != shown)
            {
                if (TrySetGradientStopCode(ramp, index, code, out var error)) changed = true;
                else if (error != null)
                {
                    if (reportError != null) reportError(error);
                    else Toast(error, MessageType.Info);
                }
            }

            if (_recentColors.Count > 0)
            {
                GUILayout.Label(Loc.T("Recent colours"), StudioGUI.Hint);
                int hit = TileGrid(_recentColors, 16, out bool rightClick,
                    _ => Loc.T("Use this recent colour for the selected stop"), width: width);
                StudioGUI.Count("paint.gradientRecent", GUILayoutUtility.GetLastRect());
                if (hit >= 0 && !rightClick)
                    changed |= SetGradientStopColour(ramp, index, _recentColors[hit]);
            }
            return changed;
        }

        static string GradientStopCode(Gradient ramp, int index)
        {
            var key = ramp.colorKeys[index];
            var colour = key.color;
            colour.a = ramp.Evaluate(key.time).a;
            return "#" + (Mathf.Approximately(colour.a, 1f) ? ColorUtility.ToHtmlStringRGB(colour) : ColorUtility.ToHtmlStringRGBA(colour));
        }

        static bool SetGradientStopColour(Gradient ramp, int index, Color colour)
        {
            var keys = ramp.colorKeys;
            colour.a = 1f;
            if (keys[index].color == colour) return false;
            keys[index].color = colour;
            ramp.SetKeys(keys, ramp.alphaKeys);
            return true;
        }

        static bool TrySetGradientStopCode(Gradient ramp, int index, string code, out string error)
        {
            error = null;
            if (!ColourCode.TryParse(code, out var colour, out bool hasAlpha))
            {
                error = Loc.T("That colour code couldn't be read. Try hex, RGB, HSL or HSV.");
                return false;
            }

            var keys = ramp.colorKeys;
            var opacity = ramp.alphaKeys;
            float time = keys[index].time;
            bool opacityChanged = hasAlpha && !Mathf.Approximately(ramp.Evaluate(time).a, colour.a);
            if (opacityChanged)
            {
                int match = Array.FindIndex(opacity, key => Mathf.Abs(key.time - time) <= StopTimeTolerance);
                if (match >= 0) opacity[match].alpha = colour.a;
                else if (opacity.Length < MaxGradientStops) opacity = opacity.Append(new GradientAlphaKey(colour.a, time)).ToArray();
                else
                {
                    error = Loc.T("This ramp has eight opacity stops. Remove one before pasting a colour with opacity here.");
                    return false;
                }
            }
            colour.a = 1f;
            if (!opacityChanged && keys[index].color == colour) return false;
            keys[index].color = colour;
            ramp.SetKeys(keys, opacity);
            return true;
        }

        void ApplyLayerGradientPreset(Layer layer, Gradient ramp)
        {
            if (_recipe == null) return;
            int index = _recipe.layers.IndexOf(layer);
            if (index < 0) return;
            using var selection = new SerializedObject(_recipe);
            var gradient = selection.FindProperty("layers").GetArrayElementAtIndex(index).FindPropertyRelative("gradient");
            if (gradient == null || gradient.propertyType != SerializedPropertyType.Gradient) return;
            gradient.gradientValue = CopyRamp(ramp);
            if (selection.ApplyModifiedProperties()) MarkEdited();
        }
    }
}
