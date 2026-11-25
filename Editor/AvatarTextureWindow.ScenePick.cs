using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        struct SceneTexel
        {
            public Color colour;
            public Material material;
            public Renderer renderer;
            public bool onTarget;
            public bool fromScreen;
            public bool changedUv;
            public Color raw;
        }

        readonly Dictionary<(int, int, int), MeshUV> _pickMeshes = new Dictionary<(int, int, int), MeshUV>();

        static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r != null && r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null);

        MeshUV PickMesh(Renderer r, int index)
        {
            MeshUV.PoseSample pose = default;
            return PickMeshUsingPose(r, index, ref pose);
        }

        MeshUV PickMeshUsingPose(Renderer r, int index, ref MeshUV.PoseSample pose)
        {
            Mesh m = MeshOf(r);
            var key = (r.GetInstanceID(), m != null ? m.GetInstanceID() : 0, index);
            if (_pickMeshes.TryGetValue(key, out var mu)) return mu;
            try { mu = MeshUV.BuildScenePicker(r, m, index, ref pose); } catch { mu = null; }
            _pickMeshes[key] = mu;
            return mu;
        }

        void PrunePickMeshes()
        {
            List<(int, int, int)> gone = null;
            foreach (var k in _pickMeshes.Keys)
                if (EditorUtility.InstanceIDToObject(k.Item1) == null) (gone ??= new List<(int, int, int)>()).Add(k);
            if (gone == null) return;
            foreach (var k in gone) { _pickMeshes[k]?.ReleaseSurfaceMesh(); _pickMeshes.Remove(k); }
        }

        readonly List<Renderer> _occluders = new List<Renderer>();
        readonly List<(Renderer renderer, Bounds? bounds)> _strokeBlockerParts = new List<(Renderer, Bounds?)>();
        readonly HashSet<MeshUV> _strokeRayPoses = new HashSet<MeshUV>();
        readonly List<Material> _blockerMaterials = new List<Material>();
        readonly List<Material> _editedPartMaterials = new List<Material>();
        bool _strokeRayBatch, _strokeBlockerPartsReady;
        MaterialPropertyBlock _sceneTileProperties, _sceneCutoutProperties;
        System.Func<MeshUV.Part, PoiyomiTileDiscard.TriangleFilter> _editedPartTileFilter;
        System.Func<MeshUV.Part, CullMode> _editedPartCull;

        static bool IsDrawn(Renderer r) =>
            r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff
            && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
            && (Tools.visibleLayers & (1 << r.gameObject.layer)) != 0
            && !SceneVisibilityManager.instance.IsHidden(r.gameObject);

        Renderer FrontBlocker(Ray ray, float maxDist, bool reuseStrokeBatch = false)
        {
            Renderer found = null;
            float nearest = maxDist - Mathf.Max(_uv.PosedSize * 0.002f, 1e-4f);
            foreach (var (r, mu, tileFilter, cull, cutout) in BlockerSlots(ray, nearest, reuseStrokeBatch))
            {
                if (!mu.RaycastBlocker(ray, out var h, tileFilter, cull, cutout)) continue;
                float d = Vector3.Distance(ray.origin, h.point);
                if (d < nearest) { nearest = d; found = r; }
            }
            return found;
        }

        bool BeginStrokeRayBatch()
        {
            if (_strokeRayBatch) return false;
            _strokeRayBatch = true;
            return true;
        }

        void EndStrokeRayBatch()
        {
            _strokeRayBatch = _strokeBlockerPartsReady = false;
            _strokeBlockerParts.Clear();
            _strokeRayPoses.Clear();
        }

        bool NeedsScenePoseRefresh(MeshUV mesh, bool reuseStrokeBatch) =>
            (!reuseStrokeBatch || _strokeRayPoses.Add(mesh)) && mesh.PoseChanged();

        static bool IsPossibleBlocker(Renderer renderer, SlotIsolation isolated) =>
            (renderer is SkinnedMeshRenderer || renderer is MeshRenderer) && MeshOf(renderer) != null && IsDrawn(renderer) &&
            (isolated == null || !isolated.parts.Any(part => part.renderer == renderer));

        static Bounds? StaticBlockerBounds(Renderer renderer)
        {
            if (!(renderer is MeshRenderer)) return null;
            var bounds = renderer.bounds;
            bounds.Expand(0.002f);
            return bounds;
        }

        IEnumerable<(Renderer renderer, MeshUV mesh, PoiyomiTileDiscard.TriangleFilter tileFilter, CullMode cull, SceneCutout cutout)> BlockerSlots(
            Ray? ray = null, float maxDistance = float.PositiveInfinity, bool reuseStrokeBatch = false)
        {
            bool reuse = reuseStrokeBatch && ray.HasValue;
            SlotIsolation isolated = null;
            if (!reuse || !_strokeBlockerPartsReady)
            {
                var root = TargetResolver.AvatarRoot(_renderer);
                if (root == null) yield break;
                root.GetComponentsInChildren(false, _occluders);
                PrunePickMeshes();
                isolated = _isolateMaterialSlots && SlotPreviewMatches() ? _slotIsolation : null;
                if (reuse)
                {
                    foreach (var renderer in _occluders)
                        if (IsPossibleBlocker(renderer, isolated)) _strokeBlockerParts.Add((renderer, StaticBlockerBounds(renderer)));
                    _strokeBlockerPartsReady = true;
                }
            }

            int count = reuse ? _strokeBlockerParts.Count : _occluders.Count;
            for (int part = 0; part < count; part++)
            {
                var r = reuse ? _strokeBlockerParts[part].renderer : _occluders[part];
                if (!reuse && !IsPossibleBlocker(r, isolated)) continue;
                var bounds = ray.HasValue ? (reuse ? _strokeBlockerParts[part].bounds : StaticBlockerBounds(r)) : null;
                if (bounds.HasValue && (!bounds.Value.IntersectRay(ray.Value, out float distance) || distance > maxDistance)) continue;
                r.GetSharedMaterials(_blockerMaterials);
                bool mirrored = r.localToWorldMatrix.determinant < 0f;
                MeshUV.PoseSample pose = default;
                for (int i = 0; i < _blockerMaterials.Count; i++)
                {
                    var m = _blockerMaterials[i];
                    if (m == null || m.renderQueue >= 3000 || (IsEditedSlot(r, i) && _uv?.SlotMissingUv(r, i) != true)) continue;
                    var mu = PickMeshUsingPose(r, i, ref pose);
                    if (mu == null) continue;
                    var tileFilter = ReadTileFilter(mu, mu.parts[0], m);
                    if (tileFilter.AllDiscarded) continue;
                    if (NeedsScenePoseRefresh(mu, reuse)) mu.RefreshPoseUsing(ref pose);
                    var cutout = SceneCutout.Read(r, i, m, ref _sceneCutoutProperties);
                    yield return (r, mu, tileFilter, SceneMarquee.WindingCull(MaterialCullMode(m), mirrored), cutout);
                }
            }
        }

        PoiyomiTileDiscard.TriangleFilter ReadTileFilter(MeshUV mesh, MeshUV.Part part, Material material) =>
            material != null && PoiyomiTileDiscard.TryRead(part.renderer, part.materialSlot, material, ref _sceneTileProperties,
                out int channel, out int discarded) ? mesh.TileDiscardFilter(part, channel, discarded) : default;

        PoiyomiTileDiscard.TriangleFilter EditedPartTileFilter(MeshUV.Part part)
        {
            part.renderer.GetSharedMaterials(_editedPartMaterials);
            return (uint)part.materialSlot < (uint)_editedPartMaterials.Count
                ? ReadTileFilter(_uv, part, _editedPartMaterials[part.materialSlot]) : default;
        }

        CullMode EditedPartCull(MeshUV.Part part)
        {
            part.renderer.GetSharedMaterials(_editedPartMaterials);
            if ((uint)part.materialSlot >= (uint)_editedPartMaterials.Count) return CullMode.Off;
            var material = _editedPartMaterials[part.materialSlot];
            if (material == null) return CullMode.Off;
            var shader = material.shader;
            bool knownStandard = shader != null && (shader.name == "Standard" || shader.name == "Standard (Specular setup)");
            if (!knownStandard && !CullProperties.Any(material.HasProperty)) return CullMode.Off;
            return SceneMarquee.WindingCull(MaterialCullMode(material), part.renderer.localToWorldMatrix.determinant < 0f);
        }

        bool RaycastVisible(Ray ray, out MeshUV.SurfaceHit hit, out Renderer coveredBy) =>
            RaycastVisibleWithUvStatus(ray, out hit, out coveredBy, out _);

        bool RaycastVisibleWithUvStatus(Ray ray, out MeshUV.SurfaceHit hit, out Renderer coveredBy, out bool changedUv,
            bool respectEditedCulling = false) =>
            RaycastVisibleCore(ray, out hit, out coveredBy, out changedUv, false, respectEditedCulling);

        bool RaycastVisibleForStroke(Ray ray, out MeshUV.SurfaceHit hit, out Renderer coveredBy, bool reuseStrokeBatch) =>
            RaycastVisibleCore(ray, out hit, out coveredBy, out _, reuseStrokeBatch && _strokeRayBatch, respectEditedCulling: true);

        bool RaycastVisibleCore(Ray ray, out MeshUV.SurfaceHit hit, out Renderer coveredBy, out bool changedUv,
            bool reuseStrokeBatch, bool respectEditedCulling = false)
        {
            coveredBy = null;
            hit = default;
            changedUv = false;
            var mesh = _uv;
            if (mesh == null) return false;
            if (_strokeRayBatch && !reuseStrokeBatch) _strokeRayPoses.Clear();
            if (NeedsScenePoseRefresh(mesh, reuseStrokeBatch)) mesh.RefreshPose();
            var partCull = respectEditedCulling ? (_editedPartCull ??= EditedPartCull) : null;
            if (!mesh.RaycastScene(ray, out hit, part => IsDrawn(part.renderer), _editedPartTileFilter ??= EditedPartTileFilter, partCull)) return false;
            if (_alsoUsers.Count > 0 && !IncludedHitUsesCurrentUv(mesh.PartOfTriangle(hit.triangle)))
            {
                changedUv = true;
                return false;
            }
            coveredBy = FrontBlocker(ray, Vector3.Distance(ray.origin, hit.point), reuseStrokeBatch);
            return coveredBy == null;
        }

        bool IncludedHitUsesCurrentUv(MeshUV.Part part)
        {
            var slot = CurrentSlot;
            if (part == null || slot == null) return false;
            foreach (var user in _alsoUsers)
            {
                if (user.slot.renderer != part.renderer || user.slot.materialIndex != part.materialSlot) continue;
                if (TargetResolver.GetSharedMesh(part.renderer) != part.mesh || !TextureUserStillAssigned(user, slot.texture))
                    return false;
                bool boundChannel = TryGetIncludedUvChannel(part.renderer, part.materialSlot, slot.texture, out _);
                var actualChannel = TargetResolver.CommonUvChannel(user.material, user.properties);
                return actualChannel.HasValue ? actualChannel.Value == part.uvChannel : !boundChannel;
            }
            return true;
        }

        bool SceneTriangleUsesCurrentUv(int triangle) =>
            _alsoUsers.Count == 0 || IncludedHitUsesCurrentUv(_uv?.PartOfTriangle(triangle));

        bool SceneSlotUsesCurrentUv(Renderer renderer, int materialIndex)
        {
            if (!_alsoUsers.Any(user => user.slot.renderer == renderer && user.slot.materialIndex == materialIndex)) return true;
            return IncludedHitUsesCurrentUv(_uv?.parts.Find(part => part.renderer == renderer && part.materialSlot == materialIndex));
        }

        static string CoveredMessage(Renderer r) => Loc.F("Another part covers that spot ({0}). Click a spot you can see.", r.name);

        bool PickSceneTexel(Event e, bool targetOnly, bool originalOnTarget, out SceneTexel t)
        {
            t = new SceneTexel();
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var th = new MeshUV.SurfaceHit();
            bool onTarget = false;
            if (_uv != null && _comp != null && !SceneTargetOutsideCurrentStage)
                onTarget = RaycastVisibleWithUvStatus(ray, out th, out _, out t.changedUv);
            if (t.changedUv) return false;
            float targetDist = onTarget ? Vector3.Distance(ray.origin, th.point) : float.MaxValue;

            Renderer best = null;
            int bestIdx = -1;
            var bh = new MeshUV.SurfaceHit();
            float bestDist = float.MaxValue;
            if (!targetOnly)
            {
                var go = HandleUtility.PickGameObject(e.mousePosition, false);
                var r = go != null ? go.GetComponent<Renderer>() : null;
                if (r is SkinnedMeshRenderer || r is MeshRenderer)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        var mu = PickMesh(r, i);
                        if (mu == null) continue;
                        if (mu.PoseChanged()) mu.RefreshPose();
                        if (!mu.Raycast(ray, out var h)) continue;
                        float d = Vector3.Distance(ray.origin, h.point);
                        if (d < bestDist) { bestDist = d; best = r; bestIdx = i; bh = h; }
                    }
                }
            }

            if (onTarget && targetDist <= bestDist + 1e-4f)
            {
                t.colour = originalOnTarget
                    ? _comp.Sample(Compositor.PickSource.Original, th.uv, 1, null, _uv, out _)
                    : _comp.SampleResult(th.uv);
                t.material = _material;
                t.renderer = RendererOfHit(th);
                t.onTarget = true;
                return true;
            }
            if (targetOnly) return false;
            if (best != null)
            {
                if (!SceneSlotUsesCurrentUv(best, bestIdx))
                {
                    t.changedUv = true;
                    return false;
                }
                var m = best.sharedMaterials[bestIdx];
                t.raw = MaterialTexel(m, bh.uv);
                t.colour = MaterialLook.Texel(m, bh.uv);
                t.material = m;
                t.renderer = best;
                return true;
            }
            var screen = GUIUtility.GUIToScreenPoint(e.mousePosition) * EditorGUIUtility.pixelsPerPoint;
            var px = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(screen, 1, 1);
            t.colour = px != null && px.Length > 0 ? px[0] : Color.black;
            t.colour.a = 1;
            t.fromScreen = true;
            return true;
        }

        static string RawNote(SceneTexel t)
        {
            if (t.material == null || t.onTarget) return "";
            var a = t.raw; var b = t.colour;
            bool same = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;
            return same ? "" : Loc.F(", as its material shows it; the texture itself is #{0}", ColorUtility.ToHtmlStringRGB(a));
        }

        static Color MaterialTexel(Material m, Vector2 uv)
        {
            var c = Color.white;
            var tex = m != null && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            if (tex != null)
            {
                var s = m.GetTextureScale("_MainTex");
                var o = m.GetTextureOffset("_MainTex");
                var u = new Vector2(Mathf.Repeat(uv.x * s.x + o.x, 1f), Mathf.Repeat(uv.y * s.y + o.y, 1f));
                var prev = RenderTexture.active;
                var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                try
                {
                    Graphics.Blit(tex, rt, Vector2.zero, u);
                    RenderTexture.active = rt;
                    var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
                    read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    c = read.GetPixel(0, 0);
                    Object.DestroyImmediate(read);
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            if (m != null && m.HasProperty("_Color"))
            {
                var mc = m.GetColor("_Color");
                c = new Color(c.r * mc.r, c.g * mc.g, c.b * mc.b, 1f);
            }
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }

        enum MatchPick { None, Source, ToColour, PairFrom, PairTo, ModelSource, ModelTarget }

        MatchPick _matchPick;
        string _matchPickLayerId;
        int _matchPickRow = -1;
        Color _matchPendingFrom;

        void StartMatchPick(ColourMatchLayer layer, MatchPick what, int row = -1)
        {
            _matchPick = what;
            _matchPickLayerId = layer.id;
            _matchPickRow = row;
            _scenePickArmed = false;
            SceneView.lastActiveSceneView?.Focus();
            Toast(Loc.F("{0} (in the Scene view). Esc cancels.", MatchPickPrompt(layer)), MessageType.Info, 6f);
            SceneView.RepaintAll();
            Repaint();
        }

        void CancelMatchPick()
        {
            _matchPick = MatchPick.None;
            _matchPickLayerId = null;
            _matchPickRow = -1;
            SceneView.RepaintAll();
            Repaint();
        }

        string MatchPickPrompt(ColourMatchLayer layer)
        {
            bool named = _renderer != null;
            string self = named ? _renderer.name : "";
            switch (_matchPick)
            {
                case MatchPick.Source: return Loc.T("Click the part whose colours to match (for example the outfit)");
                case MatchPick.ToColour: return Loc.F("Click the colour pair {0} should become, on any part of the avatar", _matchPickRow + 1);
                case MatchPick.PairFrom: return named ? Loc.F("Step 1 of 2: click the colour to change, on {0} or on the texture view", self) : Loc.T("Step 1 of 2: click the colour to change, on this part or on the texture view");
                case MatchPick.PairTo: return Loc.T("Step 2 of 2: click the colour it should become, on any part of the avatar");
                case MatchPick.ModelSource: return named ? Loc.F("Match on the model, step 1 of 2: click the spot on {0} whose colour should change", self) : Loc.T("Match on the model, step 1 of 2: click the spot on this part whose colour should change");
                case MatchPick.ModelTarget: return Loc.T("Match on the model, step 2 of 2: click the spot on the part to match");
                default: return "";
            }
        }

        void HandleMatchPick(SceneView sv, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            var layer = _recipe != null ? _recipe.layers.OfType<ColourMatchLayer>().FirstOrDefault(l => l.id == _matchPickLayerId) : null;
            if (layer == null) { CancelMatchPick(); return; }
            DrawSceneOverlay(Loc.F("<b>Avistry · Colour Match</b>   {0} · Esc cancel", MatchPickPrompt(layer)));
            EditorGUIUtility.AddCursorRect(new Rect(0, 0, sv.position.width, sv.position.height), MouseCursor.ArrowPlus);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { CancelMatchPick(); e.Use(); return; }
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;
            e.Use();
            if (_matchPick == MatchPick.ModelSource || _matchPick == MatchPick.ModelTarget) { HandleModelPick(sv, e, layer); sv.Repaint(); return; }

            bool targetOnly = _matchPick == MatchPick.PairFrom;
            if (!PickSceneTexel(e, targetOnly, true, out var t))
            {
                string hint = t.changedUv ? IncludedUvChangedHint : _renderer != null
                    ? Loc.F("Click on {0} itself (it's the texture whose colour changes).", _renderer.name)
                    : Loc.T("Click on the mesh you're editing itself (it's the texture whose colour changes).");
                Toast(hint, MessageType.Info, 4f);
                return;
            }
            ApplyMatchPick(layer, t);
            sv.Repaint();
        }

        bool HandleCanvasMatchPick(Vector2 uv)
        {
            if (_matchPick == MatchPick.None || _comp == null) return false;
            var layer = _recipe != null ? _recipe.layers.OfType<ColourMatchLayer>().FirstOrDefault(l => l.id == _matchPickLayerId) : null;
            if (layer == null) { CancelMatchPick(); return false; }
            if (_matchPick == MatchPick.Source || _matchPick == MatchPick.ModelSource || _matchPick == MatchPick.ModelTarget)
            {
                Toast(Loc.T("The texture view shows the texture you're editing. Click the part to match in the Scene view instead."), MessageType.Info, 5f);
                return true;
            }
            if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return true;
            var t = new SceneTexel
            {
                colour = _comp.Sample(Compositor.PickSource.Original, uv, 1, null, _uv, out _),
                material = _material, renderer = _renderer, onTarget = true,
            };
            ApplyMatchPick(layer, t);
            return true;
        }

        void ApplyMatchPick(ColourMatchLayer layer, SceneTexel t)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(t.colour);
            switch (_matchPick)
            {
                case MatchPick.Source:
                    if (t.fromScreen || t.material == null) { Toast(Loc.T("That isn't a part of an avatar. Click a part."), MessageType.Info, 4f); return; }
                    if (t.onTarget) { Toast(Loc.T("That's the texture you're editing. Click another part, such as the outfit."), MessageType.Info, 4f); return; }
                    CancelMatchPick();
                    SetMatchSource(layer, t.material, true);
                    break;
                case MatchPick.ToColour:
                    int row = _matchPickRow;
                    if (row < 0 || row >= layer.to.Count) { CancelMatchPick(); return; }
                    Undo.RecordObject(_recipe, "Pick match colour");
                    layer.to[row] = t.colour;
                    if (!layer.HasSource && t.material != null && !t.onTarget) { layer.source = t.material; layer.useTexture = false; }
                    MarkEdited();
                    CancelMatchPick();
                    Toast(t.material != null ? Loc.F("Pair {0} now becomes {1} (from {2}{3}).", row + 1, hex, t.material.name, RawNote(t)) : Loc.F("Pair {0} now becomes {1} (colour on screen).", row + 1, hex), MessageType.Info, 5f);
                    break;
                case MatchPick.PairFrom:
                    _matchPendingFrom = t.colour;
                    _matchPick = MatchPick.PairTo;
                    Toast(Loc.F("Got {0}. Now click the colour it should become.", hex), MessageType.Info, 5f);
                    break;
                case MatchPick.PairTo:
                    Undo.RecordObject(_recipe, "Add match pair");
                    if (layer.from.Count >= 8) { Toast(Loc.T("A Colour Match layer holds up to 8 pairs. Remove one first, or add another Colour Match layer."), MessageType.Warning, 6f); CancelMatchPick(); return; }
                    while (layer.to.Count < layer.from.Count) layer.to.Add(layer.from[layer.to.Count]);
                    layer.from.Add(_matchPendingFrom);
                    layer.to.Add(t.colour);
                    if (!layer.HasSource && t.material != null && !t.onTarget) { layer.source = t.material; layer.useTexture = false; }
                    MarkEdited();
                    CancelMatchPick();
                    Toast(Loc.F("Added a pair: #{0} → {1}{2}.", ColorUtility.ToHtmlStringRGB(_matchPendingFrom), hex, (t.material != null ? RawNote(t) : "")), MessageType.Info, 5f);
                    break;
            }
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
