using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] List<Renderer> _alsoRenderers = new List<Renderer>();
        [SerializeField] List<int> _alsoSlots = new List<int>();
        [SerializeField] List<IncludedUv> _alsoUvs = new List<IncludedUv>();

        [Serializable]
        struct IncludedUv
        {
            public Renderer renderer;
            public int slot;
            public Texture2D texture;
            public int channel;
        }

        List<TargetResolver.TextureUser> _textureUsers = new List<TargetResolver.TextureUser>();
        List<TargetResolver.TextureUser> _alsoUsers = new List<TargetResolver.TextureUser>();
        readonly List<Material> _includedMaterials = new List<Material>();
        string _alsoSignature = "";

        bool IsAlsoIncluded(TargetResolver.TextureUser u)
        {
            for (int i = 0; i < _alsoRenderers.Count && i < _alsoSlots.Count; i++)
                if (_alsoRenderers[i] == u.slot.renderer && _alsoSlots[i] == u.slot.materialIndex) return true;
            return false;
        }

        string AlsoSignature() =>
            string.Join(",", _alsoRenderers.Select((r, i) => $"{(r != null ? r.GetInstanceID() : 0)}:{(i < _alsoSlots.Count ? _alsoSlots[i] : 0)}")) +
            "|" + string.Join(",", _alsoUvs.Select(u => $"{(u.renderer != null ? u.renderer.GetInstanceID() : 0)}:{u.slot}:" +
                $"{(u.texture != null ? u.texture.GetInstanceID() : 0)}:{u.channel}"));

        bool TryGetIncludedUvChannel(Renderer renderer, int materialIndex, Texture2D texture, out int channel)
        {
            foreach (var binding in _alsoUvs)
                if (binding.renderer == renderer && binding.slot == materialIndex && binding.texture == texture)
                {
                    channel = binding.channel;
                    return true;
                }
            channel = 0;
            return false;
        }

        void BindIncludedUv(Renderer renderer, int materialIndex, Texture2D texture, int channel)
        {
            _alsoUvs.RemoveAll(binding => binding.renderer == renderer && binding.slot == materialIndex && binding.texture == texture);
            _alsoUvs.Add(new IncludedUv { renderer = renderer, slot = materialIndex, texture = texture, channel = channel });
        }

        TargetResolver.SlotRef BoundIncludedSlot(TargetResolver.TextureUser user, Texture2D texture)
        {
            var slot = user.slot;
            slot.uvChannel = TryGetIncludedUvChannel(slot.renderer, slot.materialIndex, texture, out int channel) ? channel : null;
            return slot;
        }

        static string IncludedUvChangedHint =>
            Loc.T("An included part uses a different UV set. Paint on the texture here, or include it again in a new edit.");

        static string SharedPartsChangedHint => Loc.T("The parts sharing this texture changed. Check Pick and try again.");

        bool TextureUserStillAssigned(TargetResolver.TextureUser user, Texture2D texture)
        {
            var renderer = user.slot.renderer;
            int index = user.slot.materialIndex;
            if (renderer == null || user.material == null || texture == null || user.properties.Count == 0) return false;
            renderer.GetSharedMaterials(_includedMaterials);
            if ((uint)index >= (uint)_includedMaterials.Count || _includedMaterials[index] != user.material) return false;
            foreach (var property in user.properties)
                if (!user.material.HasProperty(property) || user.material.GetTexture(property) != texture) return false;
            return true;
        }

        List<TargetResolver.SlotRef> PrimaryMeshSlots() => _users.Count > 0
            ? new List<TargetResolver.SlotRef>(_users)
            : new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = _renderer, materialIndex = _materialIndex } };

        List<TargetResolver.SlotRef> ResolveIncludedMeshes(Transform root, TextureSlot slot)
        {
            _textureUsers = TargetResolver.FindTextureUsers(root, _material, slot.texture);
            _otherMaterials = _textureUsers.Select(u => u.material).Distinct().ToList();
            _alsoUsers = _textureUsers.Where(IsAlsoIncluded).ToList();
            _alsoSignature = AlsoSignature();
            var parts = PrimaryMeshSlots();
            foreach (var user in _alsoUsers) parts.Add(BoundIncludedSlot(user, slot.texture));
            return parts;
        }

        List<(TargetResolver.SlotRef slot, List<string> props)> PreviewSlots(TextureSlot slot)
        {
            var list = _users.Select(u => (u, slot.properties)).ToList();
            list.AddRange(_alsoUsers.Select(u => (u.slot, u.properties)));
            return list;
        }

        bool IsEditedSlot(Renderer r, int index)
        {
            if (r == null) return false;
            if (_users.Any(u => u.renderer == r && u.materialIndex == index)) return true;
            if (r == _renderer && index == _materialIndex) return true;
            return _alsoUsers.Any(u => u.slot.renderer == r && u.slot.materialIndex == index);
        }

        Renderer RendererOfHit(MeshUV.SurfaceHit hit) => _uv?.PartOfTriangle(hit.triangle)?.renderer ?? _renderer;

        void QueueMeshesIncluded(IEnumerable<TargetResolver.TextureUser> users, bool on)
        {
            if (_needsRebuild || _recipe == null || CurrentSlot == null) return;
            var stillMatches = CaptureSavedEditContext(_recipe, CurrentSlot);
            string signature = AlsoSignature();
            var requested = users.ToList();
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (!stillMatches() || signature != AlsoSignature())
                {
                    Toast(SharedPartsChangedHint, MessageType.Info, 4f);
                    return;
                }
                SetMeshesIncluded(requested, on);
            };
        }

        bool SetMeshesIncluded(IEnumerable<TargetResolver.TextureUser> users, bool on)
        {
            if (_needsRebuild || _recipe == null || CurrentSlot == null) return false;
            var list = users.Where(u => IsAlsoIncluded(u) != on).ToList();
            if (list.Count == 0) return true;
            EndStroke();
            var slot = CurrentSlot;
            if (slot == null || _recipe == null) return false;
            var stillMatches = CaptureSavedEditContext(_recipe, slot);
            string signature = AlsoSignature();
            Undo.FlushUndoRecordObjects();
            var root = TargetResolver.AvatarRoot(_renderer);
            bool Matches(TargetResolver.TextureUser user) => TextureUserStillAssigned(user, slot.texture) &&
                root != null && user.slot.renderer.transform.IsChildOf(root) &&
                user.properties.SequenceEqual(TargetResolver.TextureProperties(user.material, slot.texture)) &&
                TargetResolver.CommonUvChannel(user.material, user.properties) == user.slot.uvChannel;
            if (!stillMatches() || signature != AlsoSignature() || !list.TrueForAll(Matches) ||
                !_alsoUsers.TrueForAll(Matches) || !_users.SequenceEqual(TargetResolver.FindMaterialUsers(root, _material)))
            {
                _needsRebuild = true;
                Toast(SharedPartsChangedHint, MessageType.Info, 4f);
                return false;
            }

            var changes = new HashSet<(Renderer, int)>(list.Select(user => (user.slot.renderer, user.slot.materialIndex)));
            var plannedSlots = PrimaryMeshSlots();
            foreach (var user in _textureUsers)
            {
                var key = (user.slot.renderer, user.slot.materialIndex);
                if (changes.Contains(key))
                {
                    if (on) plannedSlots.Add(user.slot);
                }
                else if (IsAlsoIncluded(user)) plannedSlots.Add(BoundIncludedSlot(user, slot.texture));
            }
            var previous = _uv;
            var planned = MeshUV.Build(plannedSlots, slot.uvChannel);
            if (!TryPlanCombinedUvRemap(previous, planned, out var apply))
            {
                planned?.ReleaseSurfaceMesh();
                Toast(CombinedUvChangeBlockedHint, MessageType.Info, 5f);
                return false;
            }
            string what = on ? (list.Count == 1 ? $"Include {list[0].slot.renderer.name}" : "Include parts")
                             : (list.Count == 1 ? $"Leave out {list[0].slot.renderer.name}" : "Leave out parts");
            var renderer = _renderer;
            int materialIndex = _materialIndex;
            var material = _material;
            var recipe = _recipe;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(what);
            Undo.RecordObjects(new Object[] { this, _recipe }, what);
            foreach (var u in list)
            {
                if (on)
                {
                    _alsoRenderers.Add(u.slot.renderer);
                    _alsoSlots.Add(u.slot.materialIndex);
                    if (u.slot.uvChannel is int channel)
                        BindIncludedUv(u.slot.renderer, u.slot.materialIndex, slot.texture, channel);
                }
                else
                {
                    _alsoUvs.RemoveAll(binding => binding.renderer == u.slot.renderer && binding.slot == u.slot.materialIndex);
                    for (int i = _alsoRenderers.Count - 1; i >= 0; i--)
                        if (_alsoRenderers[i] == u.slot.renderer && i < _alsoSlots.Count && _alsoSlots[i] == u.slot.materialIndex)
                        { _alsoRenderers.RemoveAt(i); _alsoSlots.RemoveAt(i); }
                }
            }
            float zoom = _zoom; var pan = _pan;
            bool committed = false;
            try
            {
                RebuildUsing(planned);
                if (_comp != null && _uv == planned && _renderer == renderer && _materialIndex == materialIndex &&
                    _material == material && _recipe == recipe && CurrentSlot?.texture == slot.texture)
                {
                    apply();
                    committed = true;
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            if (!committed)
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                Rebuild();
                _zoom = zoom; _pan = pan;
                Toast(SharedPartsChangedHint, MessageType.Info, 4f);
                return false;
            }
            _zoom = zoom; _pan = pan;
            RebuildSelectionVisuals();
            _dirty = true;
            if (previous?.CombinedLayout != _uv?.CombinedLayout)
            {
                if (_recipe.layers.Count > 0) RefreshEditedRecipe(affectsImage: false);
                _historyRevision = ++_nextHistoryRevision;
                ClearHistory();
            }
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int n = _alsoUsers.Count;
            Toast(on ? Loc.F("Included {0}: its pieces are in the texture view now. Ctrl+Z undoes it.", string.Join(", ", list.Select(u => u.slot.renderer.name)))
                     : Loc.F("Left out {0}.", string.Join(", ", list.Select(u => u.slot.renderer.name))) + (n > 0 ? Loc.F(" {0} other part{1} still included.", n, n == 1 ? "" : "s") : ""),
                MessageType.Info, 4f);
            Repaint();
            SceneView.RepaintAll();
            return true;
        }

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
                string label = u.slot.renderer.name;
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(label + (inc ? Loc.T("  (shares this texture, edited too)") : Loc.T("  (shares this texture)")),
                    Loc.F("Its own material ‘{0}’ uses this texture ({1}). ", u.material.name, string.Join(", ", u.properties)) +
                    (inc ? Loc.T("Included: its pieces are in the texture view and saving gives its material the new texture. Untick to leave it out")
                         : Loc.T("Tick to edit it together with this part: its pieces join the texture view, and saving gives its material the new texture"))), inc);
                if (now != inc) { toggled = u; toggledOn = now; }
                if (!inc) continue;
                var boundChannel = BoundIncludedSlot(u, CurrentSlot.texture).uvChannel;
                int? actualChannel = TargetResolver.CommonUvChannel(u.material, u.properties);
                if (boundChannel.HasValue && boundChannel != actualChannel ||
                    actualChannel.HasValue && actualChannel.Value != (boundChannel ?? CurrentSlot.uvChannel))
                    GUILayout.Label(IncludedUvChangedHint, StudioGUI.Hint);
                else if (!actualChannel.HasValue)
                    GUILayout.Label(Loc.T("This part uses the texture on several UV sets. Scene tools use the UV set from the part you picked."), StudioGUI.Hint);
                else
                {
                    int channel = actualChannel.Value;
                    var mesh = TargetResolver.GetSharedMesh(u.slot.renderer);
                    if (mesh != null && !MeshUV.HasUvChannel(mesh, channel))
                        GUILayout.Label(Loc.F("This part is missing UV{0}. Edit its texture from the texture view.", channel), StudioGUI.Hint);
                }
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
                            QueueMeshesIncluded(_textureUsers, true);
                    StudioGUI.Count("pick.onlyThisOne");
                    using (new EditorGUI.DisabledScope(none))
                        if (GUILayout.Button(new GUIContent(Loc.T("Edit just this part"), Loc.T("Leave the other materials' parts out again")), EditorStyles.miniButtonRight))
                            QueueMeshesIncluded(_textureUsers, false);
                }
            }
            if (_textureUsers.Count > 0 && _alsoUsers.Count == 0)
                GUILayout.Label(Loc.T("Tick a part to edit it too: its pieces join the texture view, you can paint it in the Scene view, " +
                                "and saving gives its material the new texture."), StudioGUI.Hint);

            if (toggled != null) QueueMeshesIncluded(new[] { toggled }, toggledOn);
        }

        internal List<(Renderer renderer, int slot, string material, string state)> AutoMeshList()
        {
            var list = new List<(Renderer, int, string, string)>();
            foreach (var u in _users.Where(u => u.renderer != null))
                list.Add((u.renderer, u.materialIndex, _material != null ? _material.name : "", u.renderer == _renderer ? "editing" : "same material"));
            foreach (var u in _textureUsers.Where(u => u.slot.renderer != null))
                list.Add((u.slot.renderer, u.slot.materialIndex, u.material.name, IsAlsoIncluded(u) ? "included" : "off"));
            return list;
        }

        internal string AutoIncludeMeshes(IList<Renderer> renderers, bool on)
        {
            if (_needsRebuild) Rebuild();
            var pick = renderers == null ? _textureUsers.ToList() : _textureUsers.Where(u => renderers.Contains(u.slot.renderer)).ToList();
            if (pick.Count == 0) return _textureUsers.Count == 0 ? "No other material on this avatar uses this texture." : "None of those parts shows this texture with its own material.";
            if (!SetMeshesIncluded(pick, on)) return AutoLastToast() ?? SharedPartsChangedHint;
            AutoSettle();
            return null;
        }
    }
}
