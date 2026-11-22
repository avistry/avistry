using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Several meshes at once (1 · Pick › Meshes on this texture). Every mesh using the edited material is always
    /// edited. Meshes whose OWN material shows the same texture (hair pieces with separate materials, for example) can be
    /// included: their UV islands join the texture view, painting and picking work on them in the Scene view, the
    /// preview shows on them, and the bake gives their material the new texture too.
    /// Included meshes' islands are numbered after the edited material's own, so those ids never change.
    /// </summary>
    partial class AvatarTextureWindow
    {
        // Included meshes (renderer + material slot), as two lists so old windows reload without them.
        [SerializeField] List<Renderer> _alsoRenderers = new List<Renderer>();
        [SerializeField] List<int> _alsoSlots = new List<int>();

        List<TargetResolver.TextureUser> _textureUsers = new List<TargetResolver.TextureUser>(); // could be included
        List<TargetResolver.TextureUser> _alsoUsers = new List<TargetResolver.TextureUser>();    // are included
        string _alsoSignature = "";

        bool IsAlsoIncluded(TargetResolver.TextureUser u)
        {
            for (int i = 0; i < _alsoRenderers.Count && i < _alsoSlots.Count; i++)
                if (_alsoRenderers[i] == u.slot.renderer && _alsoSlots[i] == u.slot.materialIndex) return true;
            return false;
        }

        string AlsoSignature() =>
            string.Join(",", _alsoRenderers.Select((r, i) => $"{(r != null ? r.GetInstanceID() : 0)}:{(i < _alsoSlots.Count ? _alsoSlots[i] : 0)}"));

        /// <summary>Called by Rebuild: finds the meshes that could be included and the slots the UVs are built from.</summary>
        List<TargetResolver.SlotRef> ResolveIncludedMeshes(Transform root, TextureSlot slot)
        {
            _textureUsers = TargetResolver.FindTextureUsers(root, _material, slot.texture);
            _otherMaterials = _textureUsers.Select(u => u.material).Distinct().ToList();
            _alsoUsers = _textureUsers.Where(IsAlsoIncluded).ToList();
            _alsoSignature = AlsoSignature();
            var parts = _users.Count > 0
                ? new List<TargetResolver.SlotRef>(_users)
                : new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = _renderer, materialIndex = _materialIndex } };
            parts.AddRange(_alsoUsers.Select(u => u.slot));
            return parts;
        }

        /// <summary>Every slot that shows the edited texture right now, with the properties to preview it in.</summary>
        List<(TargetResolver.SlotRef slot, List<string> props)> PreviewSlots(TextureSlot slot)
        {
            var list = _users.Select(u => (u, slot.properties)).ToList();
            list.AddRange(_alsoUsers.Select(u => (u.slot, u.properties)));
            return list;
        }

        /// <summary>True for a renderer slot whose texture is being edited (same material, or an included mesh).</summary>
        bool IsEditedSlot(Renderer r, int index)
        {
            if (r == null) return false;
            if (_users.Any(u => u.renderer == r && u.materialIndex == index)) return true;
            if (r == _renderer && index == _materialIndex) return true;
            return _alsoUsers.Any(u => u.slot.renderer == r && u.slot.materialIndex == index);
        }

        /// <summary>The renderer under a hit on the edited meshes (the target, or an included mesh).</summary>
        Renderer RendererOfHit(MeshUV.SurfaceHit hit) => _uv?.PartOfTriangle(hit.triangle)?.renderer ?? _renderer;

        /// <summary>Includes or leaves out meshes (undoable). Island ids of the other meshes are carried over.</summary>
        void SetMeshesIncluded(IEnumerable<TargetResolver.TextureUser> users, bool on)
        {
            var list = users.Where(u => u.slot.renderer != null && IsAlsoIncluded(u) != on).ToList();
            if (list.Count == 0) return;
            EndStroke();
            var oldKeys = _uv?.IslandKeys();
            var oldSelection = new List<int>(_selection);
            string what = on ? (list.Count == 1 ? $"Include {list[0].slot.renderer.name}" : "Include parts")
                             : (list.Count == 1 ? $"Leave out {list[0].slot.renderer.name}" : "Leave out parts");
            Undo.RecordObject(this, what);
            if (_recipe != null) Undo.RecordObject(_recipe, what);
            foreach (var u in list)
            {
                if (on) { _alsoRenderers.Add(u.slot.renderer); _alsoSlots.Add(u.slot.materialIndex); }
                else
                {
                    for (int i = _alsoRenderers.Count - 1; i >= 0; i--)
                        if (_alsoRenderers[i] == u.slot.renderer && i < _alsoSlots.Count && _alsoSlots[i] == u.slot.materialIndex)
                        { _alsoRenderers.RemoveAt(i); _alsoSlots.RemoveAt(i); }
                }
            }
            float zoom = _zoom; var pan = _pan;
            Rebuild();
            _zoom = zoom; _pan = pan; // same texture: keep the view where it was
            if (oldKeys != null && _uv != null) RemapIslands(oldKeys, oldSelection);
            RebuildSelectionVisuals();
            _dirty = true;
            int n = _alsoUsers.Count;
            Toast(on ? Loc.F("Included {0}: its pieces are in the texture view now. Ctrl+Z undoes it.", string.Join(", ", list.Select(u => u.slot.renderer.name)))
                     : Loc.F("Left out {0}.", string.Join(", ", list.Select(u => u.slot.renderer.name))) + (n > 0 ? Loc.F(" {0} other part{1} still included.", n, n == 1 ? "" : "s") : ""),
                MessageType.Info, 4f);
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>Points the selection and every layer's island lists at the same islands after the UVs were rebuilt.</summary>
        void RemapIslands((int, int, int)[] oldKeys, List<int> oldSelection)
        {
            var newKeys = _uv.IslandKeys();
            var lookup = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < newKeys.Length; i++) lookup[newKeys[i]] = i;
            int Map(int id) => id >= 0 && id < oldKeys.Length && lookup.TryGetValue(oldKeys[id], out int n) ? n : -1;

            // A list that only pointed at islands of a mesh that left keeps a -1 ("nothing"), not an empty list ("everywhere").
            void Remap(List<int> l)
            {
                if (l == null || l.Count == 0) return;
                var m = l.Select(Map).Where(i => i >= 0).Distinct().ToList();
                if (m.Count == 0) m.Add(-1);
                l.Clear();
                l.AddRange(m);
            }

            _selection = oldSelection.Select(Map).Where(i => i >= 0).Distinct().ToList();
            if (_recipe == null) return;
            foreach (var layer in _recipe.layers)
            {
                if (layer == null) continue;
                Remap(layer.islands);
                foreach (var s in layer.maskStrokes) Remap(s.clipIslands);
                if (layer is PaintLayer pl) foreach (var s in pl.strokes) Remap(s.clipIslands);
            }
        }

        /// <summary>1 · Pick: the meshes showing this texture, each editing / included / off.</summary>
        void DrawMeshList()
        {
            var same = _users.Where(u => u.renderer != null).Select(u => u.renderer).Distinct().ToList();
            if (_textureUsers.Count == 0 && same.Count <= 1) return;

            GUILayout.Space(4);
            GUILayout.Label(new GUIContent(Loc.T("Parts on this texture"), Loc.T("Every part of this avatar that shows this texture. Ticked parts are edited together: " +
                "their pieces are in the texture view, you can select and paint them, and saving gives them the new texture")), StudioGUI.GroupTitle);

            StudioGUI.Count("pick.sharedParts");
            foreach (var r in same)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ToggleLeft(new GUIContent(r == _renderer ? Loc.F("{0}  (the part you picked)", r.name) : Loc.F("{0}  (same material, edited too)", r.name),
                        r == _renderer ? Loc.T("The part you picked") : Loc.F("Uses ‘{0}’ too, so it always changes along with it", _material.name)), true);
            }

            TargetResolver.TextureUser toggled = null;
            bool toggledOn = false;
            foreach (var u in _textureUsers)
            {
                if (u.slot.renderer == null) continue;
                bool inc = IsAlsoIncluded(u);
                string label = u.slot.renderer.name; // its material is in the tooltip
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(label + (inc ? Loc.T("  (shares this texture, edited too)") : Loc.T("  (shares this texture)")),
                    Loc.F("Its own material ‘{0}’ uses this texture ({1}). ", u.material.name, string.Join(", ", u.properties)) +
                    (inc ? Loc.T("Included: its pieces are in the texture view and saving gives its material the new texture. Untick to leave it out")
                         : Loc.T("Tick to edit it together with this part: its pieces join the texture view, and saving gives its material the new texture"))), inc);
                if (now != inc) { toggled = u; toggledOn = now; }
            }

            if (_textureUsers.Count > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    bool all = _textureUsers.All(IsAlsoIncluded), none = !_textureUsers.Any(IsAlsoIncluded);
                    StudioGUI.Count("pick.includeAll");
                    using (new EditorGUI.DisabledScope(all))
                        if (GUILayout.Button(new GUIContent(Loc.T("Edit all sharing parts"), Loc.T("Edit every part that shows this texture together")), EditorStyles.miniButtonLeft))
                            EditorApplication.delayCall += () => SetMeshesIncluded(_textureUsers.ToList(), true);
                    StudioGUI.Count("pick.onlyThisOne");
                    using (new EditorGUI.DisabledScope(none))
                        if (GUILayout.Button(new GUIContent(Loc.T("Edit just this part"), Loc.T("Leave the other materials' parts out again")), EditorStyles.miniButtonRight))
                            EditorApplication.delayCall += () => SetMeshesIncluded(_textureUsers.ToList(), false);
                }
            }
            if (_textureUsers.Count > 0 && _alsoUsers.Count == 0)
                GUILayout.Label(Loc.T("Tick a part to edit it too: its pieces join the texture view, you can paint it in the Scene view, " +
                                "and saving gives its material the new texture."), StudioGUI.Hint);

            // Rebuilding changes what is drawn, so do it after this GUI pass.
            if (toggled != null) EditorApplication.delayCall += () => SetMeshesIncluded(new[] { toggled }, toggledOn);
        }

        /// <summary>The meshes on this texture: name, material, state (editing / same material / included / off).</summary>
        internal List<(Renderer renderer, int slot, string material, string state)> AutoMeshList()
        {
            var list = new List<(Renderer, int, string, string)>();
            foreach (var u in _users.Where(u => u.renderer != null))
                list.Add((u.renderer, u.materialIndex, _material != null ? _material.name : "", u.renderer == _renderer ? "editing" : "same material"));
            foreach (var u in _textureUsers.Where(u => u.slot.renderer != null))
                list.Add((u.slot.renderer, u.slot.materialIndex, u.material.name, IsAlsoIncluded(u) ? "included" : "off"));
            return list;
        }

        /// <summary>Includes or leaves out meshes by renderer (null = all of them), like ticking them in 1 · Pick.</summary>
        internal string AutoIncludeMeshes(IList<Renderer> renderers, bool on)
        {
            if (_needsRebuild) Rebuild();
            var pick = renderers == null ? _textureUsers.ToList() : _textureUsers.Where(u => renderers.Contains(u.slot.renderer)).ToList();
            if (pick.Count == 0) return _textureUsers.Count == 0 ? "No other material on this avatar uses this texture." : "None of those parts shows this texture with its own material.";
            SetMeshesIncluded(pick, on);
            AutoSettle();
            return null;
        }
    }
}
