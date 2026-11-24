using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        class ModelMatchState
        {
            public ModelMatch.View source, target;
            public ModelMatch.Result result;
            public MeshUV sourceGeometry;
        }

        readonly Dictionary<string, ModelMatchState> _modelMatch = new Dictionary<string, ModelMatchState>();

        ModelMatchState ModelMatchOf(ColourMatchLayer cm)
        {
            if (!_modelMatch.TryGetValue(cm.id, out var s)) _modelMatch[cm.id] = s = new ModelMatchState();
            return s;
        }

        bool RaycastAnyMesh(Ray ray, out Renderer best, out int bestIdx, out MeshUV.SurfaceHit bestHit)
        {
            best = null; bestIdx = -1; bestHit = new MeshUV.SurfaceHit();
            float bestDist = float.MaxValue;
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!r.bounds.IntersectRay(ray)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var mu = PickMesh(r, i);
                    if (mu == null) continue;
                    if (mu.PoseChanged()) mu.RefreshPose();
                    if (!mu.Raycast(ray, out var h)) continue;
                    float d = Vector3.Distance(ray.origin, h.point);
                    if (d < bestDist) { bestDist = d; best = r; bestIdx = i; bestHit = h; }
                }
            }
            return best != null;
        }

        string PickModelPoint(ColourMatchLayer cm, bool source, Ray ray, Camera cam)
        {
            if (_uv == null || _comp == null) return Loc.T("Choose what to edit first.");
            var st = ModelMatchOf(cm);
            bool anyHit = RaycastAnyMesh(ray, out var r, out int idx, out var hit);
            if (source)
            {
                if (!RaycastVisibleWithUvStatus(ray, out var th, out var covered, out var changedUv))
                {
                    if (changedUv) return IncludedUvChangedHint;
                    if (covered != null) return CoveredMessage(covered);
                    return _renderer != null ? Loc.F("Click on {0} itself.", _renderer.name) : Loc.T("Click on the mesh you're editing itself.");
                }
                float dt = Vector3.Distance(ray.origin, th.point);
                if (anyHit && !IsEditedSlot(r, idx) && Vector3.Distance(ray.origin, hit.point) < dt - 0.002f)
                    return Loc.F("Something is in front of that point ({0}). Click a spot you can see.", r.name);
                var texel = _comp.Sample(Compositor.PickSource.Original, th.uv, 1, null, _uv, out _);
                var part = _uv.PartOfTriangle(th.triangle);
                st.source = ModelMatch.View.From(cam, th.point, part?.renderer ?? _renderer, part != null ? part.materialSlot : _materialIndex, th.uv, texel);
                st.sourceGeometry = _uv;
            }
            else
            {
                if (!anyHit) return Loc.T("Click a part of the avatar (for example the outfit).");
                var m = r.sharedMaterials[idx];
                if (m != _material && IsEditedSlot(r, idx))
                    return Loc.F("“{0}” is included in this edit (1 · Pick › Parts on this texture), so it would change along with it. Pick a part that isn't being edited.", r.name);
                if (m == _material)
                    return Loc.F("“{0}” uses the material you're editing (“{1}”), so it would change along with it. Pick a part with its own material.", r.name, m.name);
                st.target = ModelMatch.View.From(cam, hit.point, r, idx, hit.uv, Color.clear);
            }
            st.result = new ModelMatch.Result();
            return null;
        }

        void HandleModelPick(SceneView sv, Event e, ColourMatchLayer layer)
        {
            bool source = _matchPick == MatchPick.ModelSource;
            string err = PickModelPoint(layer, source, HandleUtility.GUIPointToWorldRay(e.mousePosition), sv.camera);
            if (err != null) { Toast(err, MessageType.Info, 5f); return; }
            var st = ModelMatchOf(layer);
            CancelMatchPick();
            if (source && !st.target.valid) { StartMatchPick(layer, MatchPick.ModelTarget); return; }
            Toast(source ? Loc.T("Point on this part picked.") : Loc.F("Target point picked on “{0}”.", st.target.renderer.name), MessageType.Info, 3f);
            Repaint();
        }

        string RunModelMatch(ColourMatchLayer cm, System.Func<string, float, bool> cancelProgress = null)
        {
            if (_comp == null || _recipe == null) return Loc.T("Choose what to edit first.");
            if (cm == null || !_recipe.layers.Contains(cm)) return Loc.T("Pick a Colour Match layer to edit first.");
            if (!cm.enabled) return Loc.T("Show this Colour Match layer before matching on the model.");
            if (cm.opacity <= 0f) return Loc.T("Raise this layer’s opacity above 0% before matching on the model.");
            var st = ModelMatchOf(cm);
            if (!st.source.valid || !st.target.valid) return Loc.T("Pick both points first.");
            bool SourceStillMatches() => !_needsRebuild && st.sourceGeometry == _uv &&
                IsEditedSlot(st.source.renderer, st.source.submesh) && SceneSlotUsesCurrentUv(st.source.renderer, st.source.submesh);
            var changed = Loc.T("The part being edited changed. Pick the point on this part again.");
            if (!SourceStillMatches()) return changed;
            if (st.target.renderer == null) return Loc.T("The target is gone. Pick it again.");
            if (IsEditedSlot(st.target.renderer, st.target.submesh))
                return Loc.F("“{0}” is included in this edit (1 · Pick › Parts on this texture), so it would change along with it. Pick a part that isn't being edited.", st.target.renderer.name);
            if (CurrentSlot == null || CurrentSlot.role != TextureRole.Color)
                return Loc.T("This works on colour textures (the main texture or a glow texture).");

            bool previewWas = _previewOnAvatar, originalWas = _showOriginal;
            bool committed = false;
            Color[] originalFrom = null, originalTo = null;
            Undo.FlushUndoRecordObjects();
            if (!SourceStillMatches()) return changed;
            try
            {
                _previewOnAvatar = true;
                _showOriginal = false;
                EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Comparing the colours on the model…"), 0f);
                ComposeNow();
                if (!ModelMatch.Measure(st.target, out var goal, out var why)) return why + " " + Loc.T("Pick the target again.");

                if (!ModelMatch.Measure(st.source, out var now, out why)) return why + " " + Loc.T("Pick the point on this part again.");

                var clicked = Recolour.ToOklab(st.source.texel);
                int k = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < Mathf.Min(cm.from.Count, cm.to.Count); i++)
                {
                    float d = Vector3.Distance(Recolour.ToOklab(cm.from[i]), clicked);
                    if (d < bestD) { bestD = d; k = i; }
                }
                bool added = k < 0 || bestD > 0.12f;
                if (added && cm.from.Count >= 8)
                    return Loc.T("This layer already has 8 pairs and none is close to the clicked colour. Remove one, or click a colour that's already in a pair.");
                originalFrom = cm.from.ToArray();
                originalTo = cm.to.ToArray();
                if (added)
                {
                    while (cm.to.Count < cm.from.Count) cm.to.Add(cm.from[cm.to.Count]);
                    cm.from.Add(new Color(st.source.texel.r, st.source.texel.g, st.source.texel.b, 1f));
                    cm.to.Add(cm.from[cm.from.Count - 1]);
                    k = cm.from.Count - 1;
                }

                var g = Recolour.ToOklab(goal);
                var t = Recolour.ToOklab(cm.to[k]);
                ComposeNow();
                var rcur = Recolour.ToOklab(now);
                var slope = Vector3.one;
                var bestT = cm.to[k];
                var bestR = now;
                float bestDiff = ModelMatch.Difference(goal, now);
                const int MaxTries = 14;
                int stuck = 0;
                for (int it = 0; it < MaxTries && bestDiff > ModelMatch.GoodEnough * 0.5f; it++)
                {
                    string progress = Loc.F("Matching the colours on the model… (difference {0:0.0})", bestDiff);
                    float fraction = (it + 1f) / MaxTries;
                    if (cancelProgress?.Invoke(progress, fraction) ?? EditorUtility.DisplayCancelableProgressBar(Loc.T("Avistry"), progress, fraction))
                        return Loc.T("Match cancelled. Your colours stayed as they were.");
                    var want = ModelMatch.Step(t, rcur, g, ref slope);
                    var colour = Recolour.FromOklab(want, 1f);
                    colour = new Color(Mathf.Clamp01(colour.r), Mathf.Clamp01(colour.g), Mathf.Clamp01(colour.b), 1f);
                    var next = Recolour.ToOklab(colour);
                    if ((next - t).sqrMagnitude < 1e-8f) { if (++stuck >= 2) break; continue; }
                    stuck = 0;
                    cm.to[k] = colour;
                    ComposeNow();
                    if (!ModelMatch.Measure(st.source, out var m, out why)) return why + " " + Loc.T("Pick the point on this part again.");
                    var rn = Recolour.ToOklab(m);
                    ModelMatch.Learn(ref slope, t, rcur, next, rn);
                    t = next; rcur = rn;
                    float diff = ModelMatch.Difference(goal, m);
                    if (diff < bestDiff) { bestDiff = diff; bestT = colour; bestR = m; }
                }
                cm.to[k] = bestT;
                var matchedFrom = cm.from.ToArray();
                var matchedTo = cm.to.ToArray();
                SetModelMatchPairs(cm, originalFrom, originalTo);
                _previewOnAvatar = previewWas;
                _showOriginal = originalWas;
                Undo.IncrementCurrentGroup();
                string undoName = Loc.T("Match on the model");
                Undo.SetCurrentGroupName(undoName);
                Undo.RecordObjects(new Object[] { this, _recipe }, undoName);
                SetModelMatchPairs(cm, matchedFrom, matchedTo);
                MarkEdited();
                committed = true;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();

                bool reached = bestDiff <= ModelMatch.GoodEnough;
                string note = reached ? null : WhyNotReached(goal, bestR);
                st.result = new ModelMatch.Result { done = true, goal = goal, achieved = bestR, difference = bestDiff, reached = reached, pair = k, note = note };
                Toast(reached
                        ? Loc.F("Matched on the model (pair {0}{1}): the two points now look the same.", k + 1, (added ? ", new" : ""))
                        : Loc.F("Closest possible on the model (pair {0}), difference {1:0.0}. {2}", k + 1, bestDiff, note),
                    reached ? MessageType.Info : MessageType.Warning, 7f);
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!committed)
                {
                    if (originalFrom != null) SetModelMatchPairs(cm, originalFrom, originalTo);
                    _showOriginal = originalWas;
                }
                _previewOnAvatar = previewWas;
                ComposeNow();
            }
        }

        static void SetModelMatchPairs(ColourMatchLayer layer, Color[] from, Color[] to)
        {
            layer.from.Clear();
            layer.from.AddRange(from);
            layer.to.Clear();
            layer.to.AddRange(to);
        }

        string WhyNotReached(Color goal, Color got)
        {
            var g = Recolour.ToOklab(goal);
            var r = Recolour.ToOklab(got);
            LookCheck.Forget(_material);
            bool glow = LookCheck.StrongEmissions(_material, 0.05f).Count > 0;
            if (r.x > g.x + 0.03f)
                return glow ? Loc.T("The glow (Emission) on this material makes it brighter than the target, whatever the texture says. Lower or switch off its Emission, or match the emission texture instead.")
                            : Loc.T("It can't get as dark as the target: the lighting or material settings on this part brighten it.");
            if (r.x < g.x - 0.03f) return Loc.T("It can't get as bright as the target: the material or lighting here darkens it (for example a dark Main Color or shadow settings).");
            return Loc.T("The material changes the colour on top of the texture (for example its tint or hue settings). Try “Also match material settings”.");
        }

        void DrawModelMatch(ColourMatchLayer cm)
        {
            var st = ModelMatchOf(cm);
            GUILayout.Space(4);
            GUILayout.Label(new GUIContent(Loc.T("Match on the model"),
                Loc.T("Compares the two colours as you see them in the Scene (lighting, glow and material settings included) and adjusts this texture until they look the same")), EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("layers.matchPickSource");
                PickButton(cm, MatchPick.ModelSource, st.source.valid ? Loc.T("1. This part ✓") : Loc.T("1. Point on this part"),
                    Loc.T("Click the spot on this part whose colour should change (as seen now: the camera view is remembered)"));
                StudioGUI.Count("layers.matchPickTarget");
                PickButton(cm, MatchPick.ModelTarget, st.target.valid ? Loc.T("2. Target ✓") : Loc.T("2. Point on the target"),
                    Loc.T("Click the spot on the part to match, for example the outfit"));
            }
            using (new EditorGUI.DisabledScope(!st.source.valid || !st.target.valid))
            {
                StudioGUI.Count("layers.matchOnModel");
                if (GUILayout.Button(new GUIContent(Loc.T("Match on the model"), Loc.T("Adjusts the colour pair closest to the clicked colour until both points look the same (one undo step)")), GUILayout.Height(22)))
                {
                    var layer = cm;
                    EditorApplication.delayCall += () =>
                    {
                        if (this == null) return;
                        string err = RunModelMatch(layer);
                        if (err != null) Toast(err, MessageType.Warning, 6f);
                        Repaint();
                    };
                }
            }
            var res = st.result;
            if (res.done)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    ResultSwatch(res.goal, Loc.T("Target as seen on the model"));
                    GUILayout.Label("→", GUILayout.Width(14));
                    ResultSwatch(res.achieved, Loc.T("This part as seen on the model now"));
                    GUILayout.Label(res.reached ? Loc.F("Pair {0}: looks the same (difference {1:0.0})", res.pair + 1, res.difference) : Loc.F("Pair {0}: closest possible (difference {1:0.0})", res.pair + 1, res.difference),
                        EditorStyles.miniLabel);
                }
                if (!res.reached && !string.IsNullOrEmpty(res.note)) StudioGUI.CheckRow(StudioGUI.Check.Note, res.note);
            }
            else if (!st.source.valid)
                GUILayout.Label(Loc.T("Uses the view you have when you click, so pick both points from an angle where the light is like you want it."), StudioGUI.Hint);
        }

        void PickButton(ColourMatchLayer cm, MatchPick what, string label, string tip)
        {
            bool picking = _matchPick == what && _matchPickLayerId == cm.id;
            var old = GUI.backgroundColor;
            if (picking) GUI.backgroundColor = StudioGUI.Accent;
            if (GUILayout.Button(new GUIContent(picking ? Loc.T("Click in the Scene…") : label, StudioGUI.Icon("Grid.PickingTool", "").image, tip), EditorStyles.miniButton))
            {
                if (picking) CancelMatchPick(); else StartMatchPick(cm, what);
            }
            GUI.backgroundColor = old;
        }

        static void ResultSwatch(Color c, string tip)
        {
            var r = GUILayoutUtility.GetRect(22, 16, GUILayout.Width(22));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.HeaderLine);
                EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), c);
            }
            GUI.Label(r, new GUIContent("", $"#{ColorUtility.ToHtmlStringRGB(c)} · {tip}"));
        }

        internal string AutoModelPick(ColourMatchLayer cm, bool source, Vector2 viewport)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return "No Scene view is open.";
            var ray = sv.camera.ViewportPointToRay(new Vector3(viewport.x, 1f - viewport.y, 0));
            return PickModelPoint(cm, source, ray, sv.camera);
        }

        internal string AutoModelMatch(ColourMatchLayer cm, out ModelMatch.Result result)
        {
            string err = RunModelMatch(cm);
            result = ModelMatchOf(cm).result;
            return err;
        }
    }
}
