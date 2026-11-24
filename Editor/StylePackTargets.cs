using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class StylePackTargets
    {
        internal sealed class Resolution
        {
            internal Renderer Renderer;
            internal Material Material;
            internal TextureSlot Slot;
            internal MeshUV Geometry;
            internal bool LayoutMatches;
            internal string Problem;
            internal Func<bool> StillMatches = () => false;
        }

        internal static bool InCurrentStage(Transform part) => part != null && part.gameObject.scene.IsValid() &&
            part.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(part) &&
            StageUtility.GetCurrentStageHandle().Contains(part.gameObject);

        static string Kind(Renderer renderer) => renderer switch
        {
            SkinnedMeshRenderer => nameof(SkinnedMeshRenderer),
            MeshRenderer => nameof(MeshRenderer),
            _ => null,
        };

        static Transform UniqueChild(Transform parent, string name, out bool ambiguous)
        {
            Transform found = null;
            ambiguous = false;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!string.Equals(child.name, name, StringComparison.Ordinal)) continue;
                if (found != null) { ambiguous = true; return null; }
                found = child;
            }
            return found;
        }

        internal static Renderer FindRenderer(Transform root, string[] path, string kind, out bool ambiguous)
        {
            ambiguous = false;
            if (path == null || string.IsNullOrEmpty(kind)) return null;
            var node = root;
            foreach (string segment in path)
            {
                node = UniqueChild(node, segment, out ambiguous);
                if (node == null) return null;
            }
            Renderer found = null;
            foreach (var candidate in node.GetComponents<Renderer>())
            {
                if (Kind(candidate) != kind) continue;
                if (found != null) { ambiguous = true; return null; }
                found = candidate;
            }
            return found;
        }

        internal static StylePack.Binding CaptureBinding(Transform root, Renderer renderer, int index)
        {
            if (renderer == null) return null;
            var kind = Kind(renderer);
            if (kind == null || !InCurrentStage(renderer.transform)) return null;
            var path = new List<string>();
            for (var child = renderer.transform; child != root; child = child.parent)
            {
                if (child == null || child.parent == null || UniqueChild(child.parent, child.name, out _) != child) return null;
                path.Add(child.name);
            }
            path.Reverse();
            var segments = path.ToArray();
            return FindRenderer(root, segments, kind, out _) == renderer
                ? new StylePack.Binding { rendererPath = segments, rendererKind = kind, materialIndex = index }
                : null;
        }

        static Material AssignedMaterial(Renderer renderer, int index)
        {
            if (renderer == null || index < 0) return null;
            var materials = renderer.sharedMaterials;
            return index < materials.Length ? materials[index] : null;
        }

        static int MeshBindingIndex(MeshUV.Part part, Renderer primary, int materialIndex) => part.renderer == primary &&
            Mathf.Clamp(materialIndex, 0, part.mesh.subMeshCount - 1) == part.submesh ? materialIndex : part.submesh;

        internal static StylePack.Entry Describe(Transform root, Renderer renderer, int materialIndex,
                                                 TextureSlot slot, MeshUV layout, string name)
        {
            if (!InCurrentStage(root) || renderer == null || TargetResolver.AvatarRoot(renderer) != root ||
                slot?.texture == null || slot.role == TextureRole.Normal || slot.properties.Count == 0) return null;
            var material = AssignedMaterial(renderer, materialIndex);
            if (material == null || !slot.properties.All(p => material.HasProperty(p) && material.GetTexture(p) == slot.texture) ||
                TargetResolver.ReadUvChannel(material, slot.properties[0]) != slot.uvChannel) return null;
            var binding = CaptureBinding(root, renderer, materialIndex);
            if (binding == null) return null;

            var geometry = layout != null && layout.uvChannel == slot.uvChannel && layout.AvatarRoot() == root
                ? layout.parts.Select(part => part.mesh == null || part.mesh.subMeshCount == 0 ? null :
                    CaptureBinding(root, part.renderer, MeshBindingIndex(part, renderer, materialIndex))).ToArray()
                : Array.Empty<StylePack.Binding>();
            bool layoutKnown = geometry.Length > 0 && geometry.All(part => part != null);
            return new StylePack.Entry
            {
                name = name, rendererPath = binding.rendererPath, rendererKind = binding.rendererKind,
                materialIndex = binding.materialIndex, sourceName = slot.texture.name, sourceWidth = slot.texture.width,
                sourceHeight = slot.texture.height, properties = slot.properties.ToArray(), textureRole = (int)slot.role,
                uvChannel = slot.uvChannel, uvLayout = layoutKnown ? layout.FaceLayout : null,
                geometry = layoutKnown ? geometry : Array.Empty<StylePack.Binding>(),
            };
        }

        internal static StylePack.Entry Snapshot(StylePack.Entry entry)
        {
            if (entry == null) return null;
            return new StylePack.Entry
            {
                name = entry.name, rendererPath = entry.rendererPath?.ToArray(), rendererKind = entry.rendererKind,
                materialIndex = entry.materialIndex, sourceName = entry.sourceName, sourceWidth = entry.sourceWidth,
                sourceHeight = entry.sourceHeight, properties = entry.properties?.ToArray(), textureRole = entry.textureRole,
                uvChannel = entry.uvChannel, uvLayout = entry.uvLayout,
                geometry = entry.geometry?.Select(part => part == null ? null : new StylePack.Binding
                {
                    rendererPath = part.rendererPath?.ToArray(), rendererKind = part.rendererKind, materialIndex = part.materialIndex,
                }).ToArray(),
            };
        }

        internal static Resolution Resolve(Transform root, StylePack.Entry entry)
        {
            Resolution Fail(string problem) => new Resolution { Problem = problem };
            if (!InCurrentStage(root)) return Fail(Loc.T("Pick an avatar in the current scene."));
            if (entry == null) return Fail(Loc.T("This edit is missing its saved target."));
            var renderer = FindRenderer(root, entry.rendererPath, entry.rendererKind, out bool ambiguous);
            if (ambiguous) return Fail(Loc.T("Several parts have the same saved path on this avatar."));
            if (renderer == null || !InCurrentStage(renderer.transform) || TargetResolver.AvatarRoot(renderer) != root)
                return Fail(Loc.T("This avatar is missing the saved part."));
            var mesh = TargetResolver.GetSharedMesh(renderer);
            if (mesh == null) return Fail(Loc.T("This avatar is missing the saved part."));
            var material = AssignedMaterial(renderer, entry.materialIndex);
            if (material == null || material.shader == null) return Fail(Loc.T("The saved material slot is missing on this part."));
            if (entry.properties == null || entry.properties.Length == 0)
                return Fail(Loc.T("This edit is missing its saved texture slot."));

            var properties = new HashSet<string>(entry.properties, StringComparer.Ordinal);
            if (properties.Count != entry.properties.Length) return Fail(Loc.T("This edit is missing its saved texture slot."));
            var slot = TargetResolver.GetTextureSlots(material).FirstOrDefault(candidate => candidate.role != TextureRole.Normal &&
                (int)candidate.role == entry.textureRole && candidate.uvChannel == entry.uvChannel && properties.SetEquals(candidate.properties));
            if (slot == null) return Fail(Loc.T("The saved texture properties or UV channel don't match this material."));
            if (!EditorUtility.IsPersistent(slot.texture))
                return Fail(Loc.T("Save this part's texture as a project asset before importing its edit."));

            var geometry = BuildDefaultGeometry(root, renderer, entry.materialIndex, material, slot.uvChannel);
            bool layoutMatches = geometry != null && geometry.AvatarRoot() == root && !string.IsNullOrEmpty(entry.uvLayout) &&
                entry.uvLayout == geometry.FaceLayout && GeometryBindingsMatch(root, entry.geometry, geometry, renderer, entry.materialIndex);
            var reviewedLayout = geometry?.CombinedLayout;
            var reviewedParts = geometry?.parts.Select(part => (part.renderer, part.materialSlot, part.mesh)).ToArray();
            var shader = material.shader;
            var signature = material.ComputeCRC();
            var claimedTarget = Snapshot(entry);
            return new Resolution
            {
                Renderer = renderer, Material = material, Slot = slot, Geometry = geometry, LayoutMatches = layoutMatches,
                StillMatches = () =>
                {
                    var current = Resolve(root, claimedTarget);
                    try
                    {
                        return current.Renderer == renderer && current.Material == material && material.shader == shader &&
                            material.ComputeCRC() == signature && current.Slot?.texture == slot.texture &&
                            TargetResolver.GetSharedMesh(renderer) == mesh && current.LayoutMatches == layoutMatches &&
                            current.Geometry?.CombinedLayout == reviewedLayout &&
                            (current.Geometry == null || current.Geometry.parts
                                .Select(part => (part.renderer, part.materialSlot, part.mesh)).SequenceEqual(reviewedParts));
                    }
                    finally { current.Geometry?.ReleaseSurfaceMesh(); }
                },
            };
        }

        static MeshUV BuildDefaultGeometry(Transform root, Renderer renderer, int index, Material material, int uvChannel)
        {
            var users = TargetResolver.FindMaterialUsers(root, material);
            if (users.Count == 0) users.Add(new TargetResolver.SlotRef { renderer = renderer, materialIndex = index });
            try { return MeshUV.Build(users, uvChannel); }
            catch (UnityException) { return null; }
        }

        static bool GeometryBindingsMatch(Transform root, StylePack.Binding[] bindings, MeshUV geometry, Renderer primary, int materialIndex)
        {
            if (bindings == null || bindings.Length != geometry.parts.Count) return false;
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                var part = geometry.parts[i];
                if (binding == null || binding.materialIndex != MeshBindingIndex(part, primary, materialIndex) ||
                    FindRenderer(root, binding.rendererPath, binding.rendererKind, out _) != part.renderer) return false;
            }
            return true;
        }

        internal static bool TryDescribeDraft(Transform root, TextureRecipe draft, out StylePack.Entry entry)
        {
            entry = null;
            if (!InCurrentStage(root) || draft?.sourceTexture == null || draft.materialIndex < 0) return false;
            if (draft.draftTarget != null && draft.draftTarget.rendererPath != null)
            {
                var stored = draft.draftTarget;
                var home = Resolve(root, stored);
                if (home.Problem != null || !UsesSavedTexture(draft, home.Slot.texture) || !UsesSavedMaterial(draft, home.Material)) return false;
                entry = Snapshot(stored);
                entry.name = draft.name;
                return true;
            }

            foreach (var part in root.GetComponentsInChildren<Renderer>(true))
            {
                if (Kind(part) == null || TargetResolver.AvatarRoot(part) != root ||
                    draft.rendererPath != null && AnimationUtility.CalculateTransformPath(part.transform, root) != draft.rendererPath) continue;
                var material = AssignedMaterial(part, draft.materialIndex);
                if (material == null || !UsesSavedMaterial(draft, material)) continue;
                var slots = TargetResolver.GetTextureSlots(material).Where(candidate => candidate.role != TextureRole.Normal && UsesSavedTexture(draft, candidate.texture)).ToList();
                if (slots.Count != 1) continue;
                var slot = slots[0];
                var candidate = Describe(root, part, draft.materialIndex, slot, null, draft.name);
                if (candidate == null) continue;
                if (entry != null) { entry = null; return false; }
                entry = candidate;
            }
            return entry != null;
        }

        static bool UsesSavedTexture(TextureRecipe draft, Texture texture) => texture == draft.sourceTexture || texture == draft.bakedTexture;

        static bool UsesSavedMaterial(TextureRecipe draft, Material material) =>
            draft.sourceMaterial == null || material == draft.sourceMaterial || material == draft.bakedMaterial;
    }
}
