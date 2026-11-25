using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static GUIContent[] ChromaModeLabels => new[]
        {
            new GUIContent(Loc.T("Adjust"), Loc.T("Make the colours you already have stronger or weaker. White and grey stay grey")),
            new GUIContent(Loc.T("Add colour"), Loc.T("Add some colour on top of what is there. White and grey areas get the colour below")),
            new GUIContent(Loc.T("Set colour"), Loc.T("Give every pixel the same colour strength. White and grey areas get the colour below")),
        };

        void DrawHslSettings(HslLayer hl, ref Action pending)
        {
            var l = hl;
            EditorGUI.BeginChangeCheck();
            StudioGUI.Count("layers.hslHue");
            float hue = EditorGUILayout.Slider(new GUIContent(Loc.T("Colour shift (hue)"), Loc.T("Turns every colour around the colour wheel (-180 to 180 degrees)")), hl.hue, -180f, 180f);

            ChromaMode mode;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(Loc.T("Saturation"), Loc.T("How strong the colours are")), GUILayout.Width(EditorGUIUtility.labelWidth));
                StudioGUI.Count("layers.hslMode");
                mode = (ChromaMode)GUILayout.Toolbar((int)hl.chromaMode, ChromaModeLabels, EditorStyles.miniButton);
            }

            float sat = hl.saturation, amount = hl.chroma, colourHue = hl.colourHue;
            bool everywhere = hl.replaceHue;
            StudioGUI.Count("layers.hslAmount");
            if (mode == ChromaMode.Scale)
                sat = EditorGUILayout.Slider(new GUIContent(Loc.T("Saturation"), Loc.T("Less = greyer, more = more vivid. Grey and white areas stay grey")), hl.saturation, -1f, 1f);
            else
            {
                amount = EditorGUILayout.Slider(new GUIContent(Loc.T("Amount"), mode == ChromaMode.Add
                    ? Loc.T("How much colour to add. Small values are subtle")
                    : Loc.T("How much colour every pixel gets. Small values are subtle")), hl.chroma, 0f, 0.3f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    colourHue = EditorGUILayout.Slider(new GUIContent(Loc.T("Colour for greys"), Loc.T("The colour given to white and grey areas (hue in degrees)")), hl.colourHue, 0f, 360f);
                    var r = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f), GUILayout.Height(18f));
                    float hr = colourHue * Mathf.Deg2Rad;
                    EditorGUI.DrawRect(r, Recolour.FromOklab(new Vector3(0.72f, 0.12f * Mathf.Cos(hr), 0.12f * Mathf.Sin(hr)), 1f));
                    GUI.Label(r, new GUIContent("", Loc.T("A preview of the colour for greys")));
                }
                everywhere = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Use this hue everywhere"),
                    Loc.T("Give every pixel this hue, not only the white and grey areas")), hl.replaceHue);
            }

            StudioGUI.Count("layers.hslLightness");
            float light = EditorGUILayout.Slider(new GUIContent(Loc.T("Lightness"), Loc.T("Lighter or darker")), hl.lightness, -1f, 1f);
            if (EditorGUI.EndChangeCheck())
                pending = () =>
                {
                    Undo.RecordObject(_recipe, "Edit layer");
                    l.hue = hue; l.chromaMode = mode; l.saturation = sat; l.chroma = amount; l.colourHue = colourHue;
                    l.replaceHue = everywhere; l.lightness = light;
                    MarkEdited();
                };

            if (hl.chromaMode != ChromaMode.Scale)
                GUILayout.Label(Loc.T("Very bright whites can only hold a little colour. Lower Lightness a bit for stronger colour."), StudioGUI.Hint);
        }
    }
}
