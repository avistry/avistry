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
        enum CanvasDrop { None, Part, Mesh, Material, Edit, Unsupported, Multiple, NoMesh }
        CanvasDrop _canvasDrop;
        GameObject _dropHoverObject;
        int _dropHoverChildCount;
        int _dropHoverStamp;
        Renderer _dropHoverPart;
        readonly List<Renderer> _dropRenderers = new();
        readonly List<Material> _dropMaterials = new();

        static bool InOpenScene(GameObject part) => part != null && part.scene.IsValid() && part.scene.isLoaded;

        static Mesh DroppableMesh(Renderer part)
        {
            if (part == null || !IsMeshRenderer(part) || !InOpenScene(part.gameObject)) return null;
            var mesh = MeshOf(part);
            return mesh != null && mesh.subMeshCount > 0 ? mesh : null;
        }

        CanvasDrop ClassifyDrop(Object[] items, bool hover, out Renderer pickedPart)
        {
            pickedPart = null;
            if (items.Length > 1) return CanvasDrop.Multiple;
            if (items.Length == 0 || items[0] == null) return CanvasDrop.Unsupported;
            switch (items[0])
            {
                case TextureRecipe: return CanvasDrop.Edit;
                case Material: return CanvasDrop.Material;
                case Mesh: return CanvasDrop.Mesh;
                case Renderer renderer:
                    if (!IsMeshRenderer(renderer) || !InOpenScene(renderer.gameObject)) return CanvasDrop.Unsupported;
                    pickedPart = DroppableMesh(renderer) != null ? renderer : null;
                    break;
                case GameObject part when InOpenScene(part):
                    pickedPart = ResolveDroppedPart(part, hover);
                    break;
                case Component component when InOpenScene(component.gameObject):
                    pickedPart = ResolveDroppedPart(component.gameObject, hover);
                    break;
                default: return CanvasDrop.Unsupported;
            }
            return pickedPart != null ? CanvasDrop.Part : CanvasDrop.NoMesh;
        }

        Renderer ResolveDroppedPart(GameObject part, bool hover)
        {
            if (!InOpenScene(part)) return null;
            var own = part.GetComponent<Renderer>();
            if (DroppableMesh(own) != null) return own;
            var root = part.transform;
            if (hover && _dropHoverObject == part && _dropHoverStamp == _envStamp && _dropHoverChildCount == root.childCount &&
                (ReferenceEquals(_dropHoverPart, null) || DroppableMesh(_dropHoverPart) != null &&
                    (_dropHoverPart.transform.IsChildOf(root) || root.IsChildOf(_dropHoverPart.transform)))) return _dropHoverPart;

            var picked = FindDroppedDescendant(part);
            for (var parent = root.parent; picked == null && parent != null; parent = parent.parent)
            {
                var candidate = parent.GetComponent<Renderer>();
                if (DroppableMesh(candidate) != null) picked = candidate;
            }
            if (hover)
            {
                _dropHoverObject = part;
                _dropHoverChildCount = root.childCount;
                _dropHoverStamp = _envStamp;
                _dropHoverPart = picked;
            }
            return picked;
        }

        Renderer FindDroppedDescendant(GameObject part)
        {
            if (part.transform.childCount == 0) return null;
            part.GetComponentsInChildren(true, _dropRenderers);
            Renderer first = null;
            foreach (var candidate in _dropRenderers)
            {
                if (DroppableMesh(candidate) == null || candidate.gameObject == part) continue;
                first ??= candidate;
                candidate.GetSharedMaterials(_dropMaterials);
                if (_dropMaterials.Exists(material => material != null)) return candidate;
            }
            return first;
        }

        static string DropHint(CanvasDrop kind) => kind switch
        {
            CanvasDrop.Part => Loc.T("Drop to pick this part"),
            CanvasDrop.Mesh => Loc.T("Drop to find a part that uses this mesh"),
            CanvasDrop.Material => Loc.T("Drop to pick a part that uses this material"),
            CanvasDrop.Edit => Loc.T("Drop to continue this saved edit"),
            CanvasDrop.Multiple => Loc.T("Drop one part, mesh, material or saved edit at a time."),
            CanvasDrop.NoMesh => Loc.T("This object has no part with a mesh to edit."),
            _ => Loc.T("This item can't be opened here. Drop a part, mesh, material or saved edit."),
        };

        static bool RejectedDrop(CanvasDrop kind) => kind == CanvasDrop.Unsupported || kind == CanvasDrop.Multiple || kind == CanvasDrop.NoMesh;

        void HandleCanvasDrop(Rect canvas)
        {
            var e = Event.current;
            if (e.type == EventType.DragExited)
            {
                SetCanvasDrop(CanvasDrop.None);
                return;
            }
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!canvas.Contains(e.mousePosition)) { SetCanvasDrop(CanvasDrop.None); return; }

            var items = DragAndDrop.objectReferences;
            bool hover = e.type == EventType.DragUpdated;
            var kind = ClassifyDrop(items, hover, out var pickedPart);
            bool supported = !RejectedDrop(kind);
            DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (hover)
            {
                SetCanvasDrop(kind);
                e.Use();
                return;
            }

            SetCanvasDrop(CanvasDrop.None);
            if (supported) DragAndDrop.AcceptDrag();
            e.Use();
            if (!supported) { Toast(DropHint(kind), MessageType.Info); return; }

            var item = items[0];
            if (kind == CanvasDrop.Edit) OpenDroppedEdit((TextureRecipe)item);
            else if (kind == CanvasDrop.Part) PickDroppedPart(pickedPart, -1);
            else PickPartUsingAsset(item);
        }

        void SetCanvasDrop(CanvasDrop kind)
        {
            if (kind == CanvasDrop.None)
            {
                _dropHoverObject = null;
                _dropHoverPart = null;
                _dropRenderers.Clear();
                _dropMaterials.Clear();
            }
            if (_canvasDrop == kind) return;
            _canvasDrop = kind;
            Repaint();
        }

        void DrawCanvasDropHint(Rect canvas)
        {
            if (_canvasDrop == CanvasDrop.None || Event.current.type != EventType.Repaint) return;
            var accent = RejectedDrop(_canvasDrop) ? StudioGUI.Danger : StudioGUI.Accent;
            StudioGUI.DrawBorder(canvas, accent, 2f);
            var content = new GUIContent(DropHint(_canvasDrop));
            var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 12 };
            float width = Mathf.Min(360f, canvas.width - 24f);
            if (width < 24f) return;
            float height = Mathf.Max(36f, style.CalcHeight(content, width - 16f) + 16f);
            var box = new Rect(canvas.center.x - width * 0.5f, canvas.center.y - height * 0.5f, width, height);
            EditorGUI.DrawRect(box, StudioGUI.CanvasBg);
            StudioGUI.DrawBorder(box, accent, 1f);
            GUI.Label(new Rect(box.x + 8f, box.y + 4f, width - 16f, height - 8f), content, style);
        }

        static IEnumerable<Renderer> OpenDropParts() => FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(r => DroppableMesh(r) != null && !EditorUtility.IsPersistent(r));

        static int AssetSlot(Renderer part, Object asset, int preferIndex = -1)
        {
            var assignedMesh = DroppableMesh(part);
            if (assignedMesh == null || asset is Mesh mesh && assignedMesh != mesh) return -1;
            var materials = part.sharedMaterials;
            bool Matches(Material candidate) => asset is Material material ? candidate == material : candidate != null;
            if (preferIndex >= 0 && preferIndex < materials.Length && Matches(materials[preferIndex])) return preferIndex;
            return Array.FindIndex(materials, Matches);
        }

        void PickPartUsingAsset(Object asset)
        {
            var preferred = _renderer;
            int index = AssetSlot(preferred, asset, _materialIndex);
            if (index < 0)
            {
                preferred = ResolveDroppedPart(Selection.activeGameObject, false);
                index = AssetSlot(preferred, asset);
            }
            if (index >= 0) { PickDroppedPart(preferred, index, asset); return; }

            var matches = OpenDropParts().Select(r => (part: r, slot: AssetSlot(r, asset))).Where(x => x.slot >= 0).ToList();
            if (matches.Count == 0) { Toast(Loc.F("No part in the open scenes uses ‘{0}’.", asset.name), MessageType.Info); return; }
            if (matches.Count == 1) { PickDroppedPart(matches[0].part, matches[0].slot, asset); return; }

            var menu = new GenericMenu { allowDuplicateNames = true };
            foreach (var match in matches)
            {
                var root = TargetResolver.AvatarRoot(match.part);
                var path = AnimationUtility.CalculateTransformPath(match.part.transform, root);
                string label = root != null ? $"{match.part.gameObject.scene.name} · {root.name} · {path}" : match.part.name;
                if (match.part.transform == root) label = $"{match.part.gameObject.scene.name} · {match.part.name}";
                menu.AddItem(new GUIContent(NoSubmenu(label)), false, () => PickDroppedPart(match.part, match.slot, asset));
            }
            menu.ShowAsContext();
        }

        void PickDroppedPart(Renderer part, int index, Object asset = null)
        {
            bool samePart = part == _renderer;
            int requestedIndex = index >= 0 ? index : samePart ? _materialIndex : 0;
            Material LiveMaterial()
            {
                var materials = part.sharedMaterials;
                return materials.Length == 0 ? null : materials[Mathf.Clamp(requestedIndex, 0, materials.Length - 1)];
            }
            var mesh = DroppableMesh(part);
            var material = part != null ? LiveMaterial() : null;
            bool CandidateIsValid() => this != null && mesh != null && DroppableMesh(part) == mesh &&
                LiveMaterial() == material && (ReferenceEquals(asset, null) || (asset != null && AssetSlot(part, asset, index) == index));
            if (!CandidateIsValid()) { CanvasDropTargetChanged(); return; }
            bool sameSlot = samePart && requestedIndex == _materialIndex;
            if (sameSlot && _builtRenderer == part && _builtMaterialIndex == requestedIndex &&
                _builtTargetMesh == mesh && _material == material) { SetTab(Tab.Target); return; }
            bool meshChanged = samePart && _builtTargetMesh != mesh;
            bool materialChanged = samePart && _material != material;
            var previousPart = _renderer;
            var previousPreview = _comp;
            var previousRecipe = _recipe;
            int previousMaterialIndex = _materialIndex;
            int previousRevision = _historyRevision;
            if (!PlanTargetChange(samePart ? "material" : "part", out var applyLayers)) return;
            if (!CandidateIsValid() || _renderer != previousPart || _comp != previousPreview || _recipe != previousRecipe ||
                _materialIndex != previousMaterialIndex || _historyRevision != previousRevision)
            { CanvasDropTargetChanged(); return; }

            WithTargetUndo(samePart ? "Change material" : "Change target", () =>
            {
                applyLayers();
                Undo.RecordObject(this, samePart ? "Change material" : "Change target");
                RememberSelectionBeforeSwitch();
                if (!samePart) SetRenderer(part);
                _pendingTexture = null;
                bool resetTextureSlot = (index >= 0 && !sameSlot) || materialChanged;
                if (resetTextureSlot)
                {
                    _materialIndex = requestedIndex;
                    _slotIndex = 0;
                    _slotChosenByUser = false;
                    Learn.Did(LearnEvents.MaterialSwitched);
                }
                if (samePart && (resetTextureSlot || meshChanged))
                {
                    _selection.Clear();
                    ClearFaceSelection();
                }
                _needsRebuild = true;
                SetTab(Tab.Target);
            });
        }

        bool FindDroppedEditSource(TextureRecipe recipe, out (Renderer part, int index, Material material) found)
        {
            if (recipe.includedLayout != null) return FindIncludedEditSource(recipe, out found);
            found = default;
            if (recipe.sourceTexture == null) return false;
            var matchingMaterials = new Dictionary<Material, bool>();
            var matches = new List<(Renderer part, int index, Material material)>();
            foreach (var part in OpenDropParts())
            {
                var materials = part.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || (recipe.sourceMaterial != null && recipe.sourceMaterial != material)) continue;
                    if (!matchingMaterials.TryGetValue(material, out bool eligible))
                    {
                        eligible = TargetResolver.GetTextureSlots(material).Any(s => s.texture == recipe.sourceTexture && s.role != TextureRole.Normal);
                        matchingMaterials.Add(material, eligible);
                    }
                    if (eligible) matches.Add((part, i, material));
                }
            }
            if (matches.Count > 1 && recipe.rendererPath != null)
                matches = matches.Where(m => m.index == recipe.materialIndex &&
                    AnimationUtility.CalculateTransformPath(m.part.transform, TargetResolver.AvatarRoot(m.part)) == recipe.rendererPath).ToList();
            if (matches.Count != 1) return false;
            found = matches[0];
            return true;
        }

        void OpenDroppedEdit(TextureRecipe recipe)
        {
            if (_comp != null && !_needsRebuild) { OpenSavedEdit(recipe); return; }
            OpenSavedEditOnLoadedSource(recipe);
        }

        void OpenSavedEditOnLoadedSource(TextureRecipe recipe)
        {
            if (!FindDroppedEditSource(recipe, out var source)) { QueueSavedEdit(recipe); return; }
            var sourceMesh = DroppableMesh(source.part);
            var previousPart = _renderer;
            var previousPreview = _comp;
            var previousRecipe = _recipe;
            int previousMaterialIndex = _materialIndex;
            var texture = recipe.sourceTexture;
            var material = recipe.sourceMaterial;
            int previousRevision = _historyRevision;
            if (recipe.includedLayout != null)
            {
                bool DropContextMatches() => this != null && recipe != null && _renderer == previousPart && _comp == previousPreview &&
                    _recipe == previousRecipe && _materialIndex == previousMaterialIndex && _historyRevision == previousRevision &&
                    recipe.sourceTexture == texture && recipe.sourceMaterial == material && sourceMesh != null && DroppableMesh(source.part) == sourceMesh;
                OpenIncludedEdit(recipe, source.part, source.index, DropContextMatches);
                return;
            }
            if (!ConfirmReplaceLayers(recipe)) return;
            if (this == null || recipe == null || _renderer != previousPart || _comp != previousPreview || _recipe != previousRecipe ||
                _materialIndex != previousMaterialIndex || _historyRevision != previousRevision ||
                recipe.sourceTexture != texture || recipe.sourceMaterial != material)
            { SavedEditContextChanged(); return; }
            var materials = source.part != null ? source.part.sharedMaterials : Array.Empty<Material>();
            if (sourceMesh == null || DroppableMesh(source.part) != sourceMesh || source.index >= materials.Length ||
                materials[source.index] != source.material || texture == null ||
                !TargetResolver.GetTextureSlots(source.material).Any(s => s.texture == texture && s.role != TextureRole.Normal))
            { QueueSavedEdit(recipe); return; }

            WithTargetUndo("Load saved edit", () =>
            {
                Undo.RecordObject(this, "Load saved edit");
                bool changingSlot = _renderer == source.part && (_materialIndex != source.index || CurrentSlot?.texture != texture ||
                    MeshOf(source.part) != _builtTargetMesh);
                if (changingSlot)
                {
                    _selection.Clear();
                    ClearFaceSelection();
                }
                SetRenderer(source.part);
                _materialIndex = source.index;
                _pendingTexture = texture;
                SetTab(Tab.Target);
                Rebuild();
                if (_comp != null && CurrentSlot?.texture == texture) LoadRecipe(recipe);
                else QueueSavedEdit(recipe);
            });
        }

        void QueueSavedEdit(TextureRecipe recipe)
        {
            _recipeToLoad = recipe;
            SetTab(Tab.Target);
            Toast(Loc.T("Saved edit selected. Pick a part, then press Open under Continue a saved edit."), MessageType.Info);
        }

        void CanvasDropTargetChanged()
        {
            if (this != null) Toast(Loc.T("The target changed. Drop the item again."), MessageType.Info);
        }

    }
}
