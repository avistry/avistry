using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Flatten decals (1 · Pick): turns a decal the shader draws on top of the main texture into an Image layer with
    /// the same placement, so it becomes part of the texture. The decal is switched off on the material when you save.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _foldDecals;
        [SerializeField] bool _foldAudit;
        List<DecalReader.Decal> _decals;
        Material _decalsFor;
        int _decalsStamp;

        bool EditingMainTexture => CurrentSlot != null && CurrentSlot.properties.Contains("_MainTex");

        List<DecalReader.Decal> Decals()
        {
            // Re-read when the material or its settings change (cheap: a handful of property reads).
            // The CRC is only recomputed on Layout events (so Layout and Repaint see the same list); other events reuse it.
            bool refresh = Event.current == null || Event.current.type == EventType.Layout;
            if (_decals != null && _decalsFor == _material && !refresh) return _decals;
            int stamp = _material != null ? _material.ComputeCRC() : 0;
            if (_decals != null && _decalsFor == _material && _decalsStamp == stamp) return _decals;
            _decalsFor = _material;
            _decalsStamp = stamp;
            _decals = DecalReader.Read(_material);
            return _decals;
        }

        void DrawDecalsGroup()
        {
            if (_material == null || Event.current == null) return;
            var decals = Decals();
            if (decals.Count == 0) return;
            StudioGUI.Count("maps.decalsFoldout");
            _foldDecals = EditorGUILayout.Foldout(_foldDecals, new GUIContent(Loc.F("Flatten decals ({0})", decals.Count),
                Loc.T("Decals are pictures the shader draws on top of the texture. Flattening makes one part of the texture itself.")), true);
            if (!_foldDecals) return;

            StudioGUI.BeginGroup(Loc.T("Decals on this material"), infoId: "texture");
            StudioGUI.Count("maps.decalFlatten");
            GUILayout.Label(Loc.T("Flattening adds the decal as an Image layer, placed exactly where the shader draws it. When you save, the decal is switched off on the saved material, " +
                            "so it's drawn once, from the texture. Good for fewer shader features, or to paint over a decal."), StudioGUI.Hint);
            bool locked = MaterialWriter.IsPoiyomiLocked(_material);
            if (!EditingMainTexture)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Pick the main colour texture above to flatten decals (they're drawn onto it)."));
            else if (locked)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This material is locked (optimised). Unlock it in the Poiyomi inspector first, otherwise the decal can't be switched off after saving and would show twice."));

            foreach (var d in decals)
            {
                bool already = d.layer != null && _recipe.layers.OfType<ImageLayer>().Any(l => l.flattenedFrom == d.layer.flattenedFrom);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent(d.shown, d.blocked != null ? Loc.F("Can't be flattened: {0}", d.blocked) : null),
                        d.blocked != null ? StudioGUI.RowLabelDisabled : StudioGUI.RowLabel, GUILayout.MinWidth(40));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(d.layer == null || already || locked || !EditingMainTexture || _comp == null))
                    {
                        var decal = d;
                        if (GUILayout.Button(new GUIContent(already ? Loc.T("Added") : Loc.T("Flatten"), Loc.T("Add this decal to the layers as an Image layer")), EditorStyles.miniButton, GUILayout.Width(64)))
                            EditorApplication.delayCall += () => FlattenDecal(decal);
                    }
                }
                if (d.blocked != null) GUILayout.Label(Loc.F("Can't be flattened: {0}.", d.blocked), StudioGUI.Hint);
                else foreach (var n in d.notes) StudioGUI.CheckRow(StudioGUI.Check.Note, n);
            }
            StudioGUI.EndGroup();
        }

        void FlattenDecal(DecalReader.Decal d)
        {
            if (d?.layer == null || _comp == null) return;
            // A fresh copy, so the list entry stays unchanged and can be compared with the layers later.
            var layer = JsonUtility.FromJson<ImageLayer>(JsonUtility.ToJson(d.layer));
            layer.id = System.Guid.NewGuid().ToString("N");
            layer.image = d.layer.image;
            layer.mask = d.layer.mask;
            var keepSelection = new HashSet<int>(_selection);
            _selection.Clear();           // a flattened decal isn't limited to the selected islands
            InsertLayer(layer, "Flatten decal");
            foreach (int i in keepSelection) _selection.Add(i);
            SetTab(Tab.Paint);
            Toast(Loc.F("Added “{0}” as an Image layer. Until you save, the avatar shows the decal twice (the shader's and this layer); " +
                        "saving switches the shader's decal off on the saved material.", layer.name) + (d.notes.Count > 0 ? Loc.T(" See the notes in 1 · Pick for what may look different.") : ""),
                d.notes.Count > 0 ? MessageType.Warning : MessageType.Info, 10f);
        }
    }
}
