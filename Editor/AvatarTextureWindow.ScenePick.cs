using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Scene view picking of real texture colours on any part of the avatar (not the lit colour on screen), used by the
    /// brush's Scene picker and by Colour Match (pick the material to match, a target colour, or a whole pair).
    /// </summary>
    partial class AvatarTextureWindow
    {
        struct SceneTexel
        {
            public Color colour;        // gamma
            public Material material;   // null when it came from the screen
            public Renderer renderer;
            public bool onTarget;       // on the mesh and material being edited
            public bool fromScreen;
            public Color raw;           // the texture colour before the material's own colour settings (gamma)
        }

        readonly Dictionary<(int, int, int), MeshUV> _pickMeshes = new Dictionary<(int, int, int), MeshUV>();

        static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r != null && r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null);

        MeshUV PickMesh(Renderer r, int index)
        {
            Mesh m = MeshOf(r);
            var key = (r.GetInstanceID(), m != null ? m.GetInstanceID() : 0, index);
            if (_pickMeshes.TryGetValue(key, out var mu)) return mu; // a failed build stays null so it is not retried on every event
            try { mu = MeshUV.Build(r, index, 0); } catch { mu = null; }
            _pickMeshes[key] = mu;
            return mu;
        }

        // Drops cached meshes of renderers that no longer exist (the key holds the renderer's instance id).
        void PrunePickMeshes()
        {
            List<(int, int, int)> gone = null;
            foreach (var k in _pickMeshes.Keys)
                if (EditorUtility.InstanceIDToObject(k.Item1) == null) (gone ??= new List<(int, int, int)>()).Add(k);
            if (gone == null) return;
            foreach (var k in gone) { _pickMeshes[k]?.ReleaseSurfaceMesh(); _pickMeshes.Remove(k); }
        }

        readonly List<Renderer> _occluders = new List<Renderer>();

        // Whether the user can see this renderer in the Scene view right now.
        static bool IsDrawn(Renderer r) =>
            r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff
            && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
            && !SceneVisibilityManager.instance.IsHidden(r.gameObject)
            && (Tools.visibleLayers & (1 << r.gameObject.layer)) != 0;

        /// <summary>
        /// The part of the avatar that hides the spot at distance maxDist along the ray, or null. Only slots that are not
        /// being edited count. Parts hidden in the Scene view and see-through materials (queue 3000+) are skipped. Known
        /// limits: cutout materials (hair cards, lashes) cover their empty texels too, and a single-sided face seen from
        /// behind is ignored, so a mirrored (negative scale) part may not cover.
        /// </summary>
        Renderer FrontBlocker(Ray ray, float maxDist)
        {
            Renderer found = null;
            // Same idea as the slack in "Match on the model", scaled to the avatar so the surface itself never blocks.
            float nearest = maxDist - Mathf.Max(_uv.PosedSize * 0.002f, 1e-4f);
            foreach (var (r, m, mu) in BlockerSlots())
            {
                if (!mu.Raycast(ray, out var h)) continue;
                if (h.backFace && !IsDoubleSided(m)) continue; // culled, the user sees through it
                float d = Vector3.Distance(ray.origin, h.point);
                if (d < nearest) { nearest = d; found = r; }
            }
            return found;
        }

        static bool IsDoubleSided(Material m) => m.HasProperty("_Cull") && m.GetInt("_Cull") == 0;

        /// <summary>
        /// The slots that can cover the edited surface: other parts of the avatar that are drawn and not see-through
        /// (render queue below 3000), with their posed geometry up to date. One rule for clicks (FrontBlocker) and for the
        /// Scene Box and Lasso (SceneMarquee), so they can never disagree about what is in front.
        /// </summary>
        IEnumerable<(Renderer renderer, Material material, MeshUV mesh)> BlockerSlots()
        {
            var root = TargetResolver.AvatarRoot(_renderer);
            if (root == null) yield break;
            root.GetComponentsInChildren(false, _occluders);
            PrunePickMeshes();
            foreach (var r in _occluders)
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer) || MeshOf(r) == null || !IsDrawn(r)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.renderQueue >= 3000 || IsEditedSlot(r, i)) continue;
                    var mu = PickMesh(r, i);
                    if (mu == null) continue;
                    if (mu.PoseChanged()) mu.RefreshPose();
                    yield return (r, m, mu);
                }
            }
        }

        /// <summary>
        /// The edited surface under a Scene ray, only when nothing else on the avatar is in front of it. When another part
        /// covers the spot, returns false with coveredBy set (a plain miss leaves it null). Used on clicks only.
        /// </summary>
        bool RaycastVisible(Ray ray, out MeshUV.SurfaceHit hit, out Renderer coveredBy)
        {
            coveredBy = null;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            if (!_uv.Raycast(ray, out hit)) return false;
            coveredBy = FrontBlocker(ray, Vector3.Distance(ray.origin, hit.point));
            return coveredBy == null;
        }

        static string CoveredMessage(Renderer r) => Loc.F("Another part covers that spot ({0}). Click a spot you can see.", r.name);

        /// <summary>
        /// The texture colour under the mouse. On the texture being edited: the original colour (originalOnTarget) or the
        /// edited one. On any other part: that material's main texture times its colour. Elsewhere: the screen pixel.
        /// </summary>
        bool PickSceneTexel(Event e, bool targetOnly, bool originalOnTarget, out SceneTexel t)
        {
            t = new SceneTexel();
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var th = new MeshUV.SurfaceHit();
            bool onTarget = false;
            if (_uv != null && _comp != null)
            {
                if (_uv.PoseChanged()) _uv.RefreshPose();
                onTarget = _uv.Raycast(ray, out th);
            }
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
                t.renderer = RendererOfHit(th); // the target or an included mesh
                t.onTarget = true;
                return true;
            }
            if (targetOnly) return false;
            if (best != null)
            {
                var m = best.sharedMaterials[bestIdx];
                t.raw = MaterialTexel(m, bh.uv);
                t.colour = MaterialLook.Texel(m, bh.uv); // as the material shows it (tint, HSV, gradation, layers…)
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

        /// <summary>", texture #RRGGBB" when the material shows the texture in a different colour (else empty).</summary>
        static string RawNote(SceneTexel t)
        {
            if (t.material == null || t.onTarget) return "";
            var a = t.raw; var b = t.colour;
            bool same = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;
            return same ? "" : Loc.F(", as its material shows it; the texture itself is #{0}", ColorUtility.ToHtmlStringRGB(a));
        }

        /// <summary>A material's main texture (with its tiling) times its colour at a mesh UV, as gamma values.</summary>
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
                    Graphics.Blit(tex, rt, Vector2.zero, u); // scale 0: every pixel samples the texture at u
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
                Toast(_renderer != null ? Loc.F("Click on {0} itself (it's the texture whose colour changes).", _renderer.name) : Loc.T("Click on the mesh you're editing itself (it's the texture whose colour changes)."), MessageType.Info, 4f);
                return;
            }
            ApplyMatchPick(layer, t);
            sv.Repaint();
        }

        /// <summary>Texture view click while a Colour Match pick is waiting: picks this texture's own (original) colour.</summary>
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

        /// <summary>What a Colour Match pick does, from the Scene view or the texture view.</summary>
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
