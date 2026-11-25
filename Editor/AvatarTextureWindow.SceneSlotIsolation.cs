using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        sealed class IsolatedSlots
        {
            public Renderer renderer;
            public Mesh source, baked;
            public Material[] materials;
            public int[] visible;
            public int bakedPose = -1;
            public Matrix4x4 bakedToWorld;
            public bool mirrored;
        }

        sealed class SlotIsolation
        {
            public MeshUV geometry;
            public Renderer target;
            public TextureSlot texture;
            public StageHandle stage;
            public readonly List<IsolatedSlots> parts = new List<IsolatedSlots>();
            public readonly List<(TargetResolver.SlotRef slot, Material material, Shader shader, List<string> properties)> assignments = new();
        }

        static readonly PropertyInfo SceneVisibilitySwitch = typeof(SceneView).GetProperty("sceneVisActive",
            BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly EventInfo NativeIsolationTransition = typeof(SceneVisibilityManager).GetEvent("currentStageIsIsolated",
            BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo AddIsolationTransition = NativeIsolationTransition?.GetAddMethod(true);
        static readonly MethodInfo RemoveIsolationTransition = NativeIsolationTransition?.GetRemoveMethod(true);
        static readonly Matrix4x4 SlotReflection = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
        static readonly int WorldTransformParams = Shader.PropertyToID("unity_WorldTransformParams");
        static AvatarTextureWindow s_slotIsolationOwner;
        [SerializeField] bool _isolateMaterialSlots;
        SlotIsolation _slotIsolation;
        readonly List<Material> _isolationMaterials = new List<Material>();
        readonly List<Renderer> _isolationRenderers = new();
        readonly Stack<(Camera camera, bool isolate)> _slotRenderCameras = new();
        readonly List<Renderer> _mutedSlotRenderers = new();
        readonly List<Vector3> _slotMeshVectors = new();
        readonly List<Vector4> _slotMeshTangents = new();
        readonly List<int> _slotMeshIndices = new();
        Camera _slotRenderQueued;
        bool _slotCameraFailed;
        MaterialPropertyBlock _isolatedSlotBlock;

        static bool CanDrawIsolatedSlots()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || GraphicsSettings.currentRenderPipeline != null ||
                !StageUtility.GetMainStageHandle().Equals(StageUtility.GetCurrentStageHandle()) ||
                SceneView.sceneViews.Count == 0 || SceneVisibilitySwitch?.PropertyType != typeof(bool) ||
                AddIsolationTransition == null || RemoveIsolationTransition == null) return false;
            try
            {
                foreach (SceneView view in SceneView.sceneViews)
                    if (view == null || view.camera == null || view.cameraMode.drawMode != DrawCameraMode.Textured ||
                        !(bool)SceneVisibilitySwitch.GetValue(view)) return false;
            }
            catch { return false; }
            return true;
        }

        bool TrySplitEditedSlots(Renderer renderer, out IsolatedSlots result)
        {
            result = null;
            var materials = renderer.sharedMaterials;
            var edited = new List<int>();
            bool mixed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                if (IsEditedSlot(renderer, i)) edited.Add(i);
                else mixed = true;
            }
            if (!mixed || edited.Count == 0) return true;
            var mesh = MeshOf(renderer);
            if (mesh == null || mesh.subMeshCount != materials.Length || !SupportsIsolatedSlotRenderer(renderer)) return false;
            foreach (int index in edited)
                if (materials[index].shader == null || !materials[index].shader.isSupported ||
                    !_uv.parts.Any(part => part.renderer == renderer && part.mesh == mesh && part.submesh == index)) return false;
            result = new IsolatedSlots { renderer = renderer, source = mesh, materials = materials, visible = edited.ToArray() };
            return true;
        }

        SlotIsolation PrepareSlotIsolation(GameObject[] shown)
        {
            if (!CanDrawIsolatedSlots() || _material == null || CurrentSlot == null ||
                !SavedEditSlotStillAssigned(_material, CurrentSlot)) return null;
            var scope = new SlotIsolation { geometry = _uv, target = _renderer, texture = CurrentSlot,
                stage = StageUtility.GetCurrentStageHandle() };
            foreach (var renderer in EditedSceneRenderers().Distinct())
            {
                if (renderer == null || !shown.Contains(renderer.gameObject)) continue;
                if (!TrySplitEditedSlots(renderer, out var changed)) return null;
                if (changed != null) scope.parts.Add(changed);
            }
            var main = new TargetResolver.SlotRef { renderer = _renderer, materialIndex = _materialIndex };
            scope.assignments.Add((main, _material, _material.shader, scope.texture.properties));
            foreach (var user in _users)
                if (!user.Equals(main)) scope.assignments.Add((user, _material, _material.shader, scope.texture.properties));
            foreach (var user in _alsoUsers)
                if (user.material != null) scope.assignments.Add((user.slot, user.material, user.material.shader, user.properties));
            return scope.parts.Count > 0 ? scope : null;
        }

        bool SupportsIsolatedSlotRenderer(Renderer renderer)
        {
            renderer.GetComponents(_isolationRenderers);
            return _isolationRenderers.Count == 1 && renderer.lightmapIndex < 0 && renderer.realtimeLightmapIndex < 0 &&
                renderer.sortingLayerID == 0 && renderer.sortingOrder == 0 && renderer.probeAnchor == null &&
                renderer.lightProbeProxyVolumeOverride == null && renderer.lightProbeUsage != LightProbeUsage.UseProxyVolume &&
                !renderer.isPartOfStaticBatch && renderer.GetComponentInParent<LODGroup>() == null &&
                renderer.GetComponentInParent<SortingGroup>() == null &&
                (!(renderer is SkinnedMeshRenderer) || CanDrawIsolatedSkin(renderer));
        }

        static bool CanDrawIsolatedSkin(Renderer renderer)
        {
            float determinant = renderer.localToWorldMatrix.determinant;
            return determinant != 0f && float.IsFinite(determinant);
        }

        void AttachSlotIsolation(SlotIsolation scope)
        {
            ForgetSlotIsolation(false);
            try { AddIsolationTransition.Invoke(null, new object[] { (System.Action<bool>)OnNativeSlotIsolationChanged }); }
            catch { return; }
            _slotIsolation = scope;
            s_slotIsolationOwner = this;
            _isolateMaterialSlots = true;
            _slotCameraFailed = false;
            SceneView.beforeSceneGui += DrawIsolatedMaterialSlots;
            SceneView.duringSceneGui += FinishIsolatedSlotView;
            Camera.onPreCull += BeginIsolatedSlotCamera;
            Camera.onPostRender += FinishIsolatedSlotCamera;
            EditorApplication.update += ClearSlotCameraState;
        }

        void ForgetSlotIsolation(bool restore)
        {
            var scope = _slotIsolation;
            if (scope == null) return;
            _slotIsolation = null;
            bool active = _isolateMaterialSlots && s_slotIsolationOwner == this && SceneIsIsolated;
            _isolateMaterialSlots = false;
            if (s_slotIsolationOwner == this) s_slotIsolationOwner = null;
            SceneView.beforeSceneGui -= DrawIsolatedMaterialSlots;
            SceneView.duringSceneGui -= FinishIsolatedSlotView;
            Camera.onPreCull -= BeginIsolatedSlotCamera;
            Camera.onPostRender -= FinishIsolatedSlotCamera;
            EditorApplication.update -= ClearSlotCameraState;
            ClearSlotCameraState();
            try { RemoveIsolationTransition.Invoke(null, new object[] { (System.Action<bool>)OnNativeSlotIsolationChanged }); }
            catch { }
            foreach (var part in scope.parts)
                if (part.baked != null) DestroyImmediate(part.baked);
            if (!restore || !active || !scope.stage.Equals(StageUtility.GetCurrentStageHandle())) return;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            SceneVisibilityManager.instance.ExitIsolation();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            SceneView.RepaintAll();
        }

        void OnNativeSlotIsolationChanged(bool isolated)
        {
            if (!isolated && _isolateMaterialSlots) ForgetSlotIsolation(false);
        }

        bool SlotPreviewMatches()
        {
            var scope = _slotIsolation;
            if (!_isolateMaterialSlots || scope == null || s_slotIsolationOwner != this || _needsRebuild ||
                scope.geometry != _uv || scope.target != _renderer || scope.texture != CurrentSlot ||
                _slotCameraFailed || scope.target == null || MeshOf(scope.target) != _builtTargetMesh ||
                _material == null || !SavedEditSlotStillAssigned(_material, scope.texture) ||
                !scope.stage.Equals(StageUtility.GetCurrentStageHandle()) || !SceneIsIsolated || !CanDrawIsolatedSlots()) return false;
            foreach (var (slot, material, shader, properties) in scope.assignments)
            {
                if (slot.renderer == null || material == null || shader == null || shader != material.shader || !shader.isSupported)
                    return false;
                slot.renderer.GetSharedMaterials(_isolationMaterials);
                if (slot.materialIndex < 0 || slot.materialIndex >= _isolationMaterials.Count ||
                    _isolationMaterials[slot.materialIndex] != material ||
                    properties.Any(prop => !material.HasProperty(prop) || material.GetTexture(prop) != scope.texture.texture)) return false;
            }
            foreach (var part in scope.parts)
            {
                var renderer = part.renderer;
                if (renderer == null || !IsDrawn(renderer) || MeshOf(renderer) != part.source || !SupportsIsolatedSlotRenderer(renderer)) return false;
                renderer.GetSharedMaterials(_isolationMaterials);
                if (!_isolationMaterials.SequenceEqual(part.materials)) return false;
            }
            return true;
        }

        void DrawIsolatedMaterialSlots(SceneView view)
        {
            if (!_isolateMaterialSlots || Event.current.type != EventType.Repaint) return;
            if (!SceneIsIsolated) { ForgetSlotIsolation(false); return; }
            if (!SlotPreviewMatches()) { ForgetSlotIsolation(true); return; }
            _isolatedSlotBlock ??= new MaterialPropertyBlock();
            try
            {
                if (_uv.PoseChanged()) _uv.RefreshPose();
                foreach (var part in _slotIsolation.parts)
                {
                    var renderer = part.renderer;
                    var mesh = part.source;
                    var transform = renderer.localToWorldMatrix;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        part.baked ??= new Mesh { name = "Avistry isolated slot pose", hideFlags = HideFlags.HideAndDontSave };
                        if (part.bakedPose != _uv.PoseVersion)
                        {
                            part.bakedToWorld = MeshUV.BakeSkinPose(skin, part.baked);
                            part.mirrored = renderer.localToWorldMatrix.determinant < 0f;
                            if (part.mirrored)
                            {
                                ReflectIsolatedSkin(part);
                                part.bakedToWorld *= SlotReflection;
                            }
                            part.baked.RecalculateBounds();
                            part.bakedPose = _uv.PoseVersion;
                        }
                        mesh = part.baked;
                        transform = part.bakedToWorld;
                    }
                    var bounds = WorldSlotBounds(mesh.bounds, transform);
                    foreach (int index in part.visible)
                    {
                        renderer.GetPropertyBlock(_isolatedSlotBlock, index);
                        if (_isolatedSlotBlock.isEmpty) renderer.GetPropertyBlock(_isolatedSlotBlock);
                        if (part.mirrored) _isolatedSlotBlock.SetVector(WorldTransformParams, new Vector4(0f, 0f, 0f, -1f));
                        var draw = new RenderParams(part.materials[index]) { camera = view.camera, worldBounds = bounds,
                            layer = renderer.gameObject.layer, renderingLayerMask = renderer.renderingLayerMask,
                            matProps = _isolatedSlotBlock, shadowCastingMode = renderer.shadowCastingMode,
                            receiveShadows = renderer.receiveShadows, lightProbeUsage = renderer.lightProbeUsage,
                            reflectionProbeUsage = renderer.reflectionProbeUsage };
                        Graphics.RenderMesh(draw, mesh, index, transform);
                    }
                }
                _slotRenderQueued = view.camera;
            }
            catch
            {
                ForgetSlotIsolation(true);
                Toast(Loc.T("The edited slots could not be shown. Use the texture view to paint underneath."), MessageType.Info);
            }
        }

        void ReflectIsolatedSkin(IsolatedSlots part)
        {
            var mesh = part.baked;
            mesh.GetVertices(_slotMeshVectors);
            ReflectSlotVectors(_slotMeshVectors);
            mesh.SetVertices(_slotMeshVectors, 0, _slotMeshVectors.Count, MeshUpdateFlags.DontRecalculateBounds);
            mesh.GetNormals(_slotMeshVectors);
            if (_slotMeshVectors.Count > 0)
            {
                ReflectSlotVectors(_slotMeshVectors);
                mesh.SetNormals(_slotMeshVectors);
            }
            mesh.GetTangents(_slotMeshTangents);
            for (int i = 0; i < _slotMeshTangents.Count; i++)
            {
                var tangent = _slotMeshTangents[i];
                tangent.x = -tangent.x;
                _slotMeshTangents[i] = tangent;
            }
            if (_slotMeshTangents.Count > 0) mesh.SetTangents(_slotMeshTangents);

            foreach (int slot in part.visible)
            {
                var submesh = mesh.GetSubMesh(slot);
                if (submesh.topology != MeshTopology.Triangles)
                    throw new System.InvalidOperationException("Mirrored isolation requires triangular parts.");
                mesh.GetIndices(_slotMeshIndices, slot, false);
                for (int i = 0; i < _slotMeshIndices.Count; i += 3)
                    (_slotMeshIndices[i + 1], _slotMeshIndices[i + 2]) = (_slotMeshIndices[i + 2], _slotMeshIndices[i + 1]);
                mesh.SetIndices(_slotMeshIndices, MeshTopology.Triangles, slot, false, submesh.baseVertex);
            }
        }

        static void ReflectSlotVectors(List<Vector3> vectors)
        {
            for (int i = 0; i < vectors.Count; i++)
            {
                var vector = vectors[i];
                vector.x = -vector.x;
                vectors[i] = vector;
            }
        }

        void BeginIsolatedSlotCamera(Camera camera)
        {
            bool nextScene = camera == _slotRenderQueued;
            if (!nextScene && _slotRenderCameras.Count == 0) return;
            try
            {
                RestoreMutedSlotRenderers();
                bool isolate = nextScene && SlotPreviewMatches();
                if (nextScene) _slotRenderQueued = null;
                _slotRenderCameras.Push((camera, isolate));
                if (isolate) MuteSlotRenderers();
            }
            catch { AbortSlotCamera(); }
        }

        void FinishIsolatedSlotCamera(Camera camera)
        {
            if (_slotRenderCameras.Count == 0 || _slotRenderCameras.Peek().camera != camera) return;
            try
            {
                RestoreMutedSlotRenderers();
                _slotRenderCameras.Pop();
                if (_slotRenderCameras.Count > 0 && _slotRenderCameras.Peek().isolate) MuteSlotRenderers();
            }
            catch { AbortSlotCamera(); }
        }

        void MuteSlotRenderers()
        {
            if (!_isolateMaterialSlots || _slotIsolation == null) return;
            foreach (var part in _slotIsolation.parts)
                if (part.renderer != null && !part.renderer.forceRenderingOff)
                {
                    _mutedSlotRenderers.Add(part.renderer);
                    part.renderer.forceRenderingOff = true;
                }
        }

        void AbortSlotCamera()
        {
            ClearSlotCameraState();
            _slotCameraFailed = true;
            SceneView.RepaintAll();
        }

        void RestoreMutedSlotRenderers()
        {
            foreach (var renderer in _mutedSlotRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            _mutedSlotRenderers.Clear();
        }

        void FinishIsolatedSlotView(SceneView view)
        {
            if (Event.current.type == EventType.Repaint &&
                (_slotRenderQueued == view.camera || _slotRenderCameras.Any(render => render.camera == view.camera)))
                ClearSlotCameraState();
        }

        void ClearSlotCameraState()
        {
            RestoreMutedSlotRenderers();
            _slotRenderCameras.Clear();
            _slotRenderQueued = null;
        }

        static Bounds WorldSlotBounds(Bounds local, Matrix4x4 matrix)
        {
            var x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            var y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            var z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            var extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2f);
        }
    }
}
