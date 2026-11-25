using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static readonly int SceneGradientHint = "Avistry.SceneGradient".GetHashCode();
        static GUIStyle s_sceneGradientLabelStyle, s_inwardGradientLabelStyle;
        readonly List<Layer> _placementPreview = new List<Layer>();
        DrawnGradientLayer _sceneGradientDraft, _sceneGradientOriginal;
        System.Func<bool> _sceneGradientTargetMatches;
        Compositor _sceneGradientCompositor;
        Transform _sceneGradientRoot;
        Vector3 _sceneGradientNormal;
        Vector3 _sceneGradientGrab;
        GradHandle _sceneGradientHandle;
        bool _sceneGradientReady;
        bool _sceneGradientOriginalView;
        int _sceneGradientPose;
        SceneView _sceneGradientView;
        int _sceneGradientControl;
        Vector2 _sceneGradientMouseDown;
        Vector2 _sceneGradientEndpointGrab;
        MeshUV _sceneGradientClearUv;
        int _sceneGradientClearPose;
        SceneGradientRecovery _sceneGradientRecovery;

        sealed class SceneGradientRecovery
        {
            public Compositor preview;
            public TextureRecipe recipe;
            public int[] selection;
            public bool faces;
            public string reason;
        }

        bool SceneGradientActive => CanvasPainting && GradientToolActive && _paintInScene && _uv != null && _comp != null &&
                                    CurrentSlot != null && CurrentSlot.role != TextureRole.Normal;

        static string SceneGradientTargetChanged =>
            Loc.T("Edit changed. Redraw gradient.");

        SceneGradientRecovery CurrentSceneGradientRecovery
        {
            get
            {
                var notice = _sceneGradientRecovery;
                if (notice != null && (_needsRebuild || !SceneGradientActive || notice.preview != _comp ||
                    notice.recipe != _recipe || notice.faces != HasFaceSelection ||
                    !notice.selection.SequenceEqual(notice.faces ? _selectedFaces : _selection)))
                    _sceneGradientRecovery = notice = null;
                return notice;
            }
        }

        void ReportSceneGradientOverlap(SceneView scene, string reason)
        {
            bool faces = HasFaceSelection;
            _sceneGradientRecovery = new SceneGradientRecovery
            {
                preview = _comp, recipe = _recipe, faces = faces,
                selection = (faces ? _selectedFaces : _selection).ToArray(), reason = reason,
            };
            if (scene != null && (SceneBarWindow() != this || !scene.TryGetOverlay("avistry-scene-bar", out var overlay) ||
                !(overlay is AvistrySceneBar bar) || !bar.displayed || bar.collapsed || !bar.PanelIsVisible))
                scene.ShowNotification(new GUIContent(reason), 5.0);
        }

        void ReportSceneGradientGesture(SceneView scene, string reason, float seconds)
        {
            Toast(reason, MessageType.Info, seconds);
            scene.ShowNotification(new GUIContent(reason), 3.0);
        }

        void DrawSceneGradientRecovery(float width, ref System.Action change)
        {
            var notice = CurrentSceneGradientRecovery;
            if (notice == null) return;
            var message = new GUIContent(notice.reason);
            var style = StudioGUI.CheckLabel;
            var row = SceneBarRow(width, style.CalcHeight(message, Mathf.Max(1f, width - 7f)) + 4f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(row.x, row.y + 1f, 2f, row.height - 2f), StudioGUI.Accent);
            row.xMin += 6f;
            GUI.Label(row, message, style);
            var draw = SceneBarRow(width, 22f);
            if (GUI.Button(draw, new GUIContent(Loc.T("Draw on texture"),
                Loc.T("Keep the selection and draw a new gradient on the texture.")), EditorStyles.miniButton))
                change = () => RecoverSceneGradientOnTexture(notice);
            StudioGUI.Count("scene.gradientOnTexture", draw);
        }

        void RecoverSceneGradientOnTexture(SceneGradientRecovery notice)
        {
            if (notice == null || notice != CurrentSceneGradientRecovery) return;
            _sceneGradientRecovery = null;
            if (_toastText == notice.reason) _toastText = null;
            ChangeSceneBar("Draw gradient on texture", () =>
            {
                SetTab(Tab.Paint);
                _paintInScene = false;
                _eyedropperArmed = false;
            });
            Focus();
        }

        IList<Layer> LayersForPreview()
        {
            var layers = _recipe.layers;
            Layer draft, original;
            int index;
            if (_gradPreviewActive && _gradDrag != null)
            {
                draft = _gradDrag;
                original = _gradOriginal;
                index = GradientPlacementIndex();
            }
            else if (_sceneGradientReady && _sceneGradientDraft != null)
            {
                draft = _sceneGradientDraft;
                original = _sceneGradientOriginal;
                int active = ActiveIndex;
                index = original != null ? layers.IndexOf(original) : active < 0 ? layers.Count : active + 1;
            }
            else if (_imgPreviewActive && _imgDrag != null)
            {
                draft = _imgDrag;
                original = _imgOriginal;
                index = layers.IndexOf(original);
            }
            else return layers;
            if (index < 0) return layers;

            _placementPreview.Clear();
            _placementPreview.AddRange(layers);
            if (original == null) _placementPreview.Insert(index, draft);
            else _placementPreview[index] = draft;
            return _placementPreview;
        }

        bool SceneGradientHit(Ray ray, out MeshUV.SurfaceHit hit, out string reason, bool pickingColour = false)
        {
            reason = null;
            if (!RaycastVisibleWithUvStatus(ray, out hit, out var covered, out var changedUv, respectEditedCulling: true))
            {
                reason = changedUv ? IncludedUvChangedHint : covered == null
                    ? (pickingColour ? ScenePaintColourPickHint : Loc.T("Drag on the model to place the gradient."))
                    : ScenePaintCoverHint(covered, out _);
                return false;
            }
            if (RendererOfHit(hit) == null)
            {
                reason = pickingColour ? ScenePaintColourPickHint : Loc.T("Drag on a visible part of the model to place the gradient.");
                return false;
            }
            if (hit.uv.x < 0f || hit.uv.x > 1f || hit.uv.y < 0f || hit.uv.y > 1f)
            {
                reason = pickingColour ? ScenePaintColourPickHint : Loc.T("That piece sits outside this texture. Drag over a piece on this texture.");
                return false;
            }
            return true;
        }

        string BeginSceneGradient(Ray ray, Vector3 viewNormal, GradHandle handle)
        {
            if (!SceneGradientActive) return Loc.T("Turn on painting on the model with the Gradient tool selected first.");
            var root = _uv.AvatarRoot();
            if (root == null || viewNormal.sqrMagnitude < 1e-8f) return Loc.T("Pick a part you can see in the Scene view.");
            var selected = handle == GradHandle.None ? null : SelectedGradient;
            MeshUV.SurfaceHit hit = default;
            if (selected == null && !SceneGradientHit(ray, out hit, out var reason)) return reason;
            Vector3 normal = selected != null
                ? root.worldToLocalMatrix.transpose.MultiplyVector(selected.sceneNormal).normalized
                : viewNormal.normalized;
            Vector3 grab = Vector3.zero;
            if (selected != null)
            {
                var anchor = handle switch
                {
                    GradHandle.Start => selected.sceneStart,
                    GradHandle.End => selected.sceneEnd,
                    _ => (selected.sceneStart + selected.sceneEnd) * 0.5f,
                };
                if (!ProjectSceneGradientPointer(ray, normal, root.TransformPoint(anchor), out var pressed))
                    return Loc.T("Turn the Scene view to face the gradient before moving it.");
                if (handle == GradHandle.Move) grab = root.InverseTransformPoint(pressed);
            }

            CancelGradientDrag();
            CancelImageDrag();
            EndStroke();
            var targetMatches = CaptureSavedEditContext(_recipe, CurrentSlot);
            if (!targetMatches())
            {
                if (_needsRebuild) _preview.Clear();
                return SceneGradientTargetChanged;
            }
            _sceneGradientTargetMatches = targetMatches;
            _sceneGradientCompositor = _comp;
            _sceneGradientOriginal = selected;
            _sceneGradientRoot = root;
            _sceneGradientNormal = normal;
            SetSceneGradientAxes(root.right, root.up);
            _sceneGradientGrab = grab;
            _sceneGradientHandle = selected == null ? GradHandle.End : handle;
            _sceneGradientReady = false;
            _sceneGradientOriginalView = _showOriginal;
            if (selected != null)
            {
                _sceneGradientDraft = selected.CopyPlacement();
            }
            else
            {
                var point = root.InverseTransformPoint(hit.point);
                var draft = NewDrawnGradientLayer();
                draft.sceneProjection = true;
                draft.sceneStart = point;
                draft.sceneEnd = point;
                draft.sceneNormal = root.localToWorldMatrix.transpose.MultiplyVector(normal);
                CaptureGradientPieces(draft);
                _sceneGradientDraft = draft;
            }
            return null;
        }

        bool UpdateSceneGradient(Ray ray)
        {
            var draft = _sceneGradientDraft;
            var root = _sceneGradientRoot;
            if (draft == null || root == null) return false;
            if (_sceneGradientHandle == GradHandle.Move)
            {
                var original = _sceneGradientOriginal;
                var middle = root.TransformPoint((original.sceneStart + original.sceneEnd) * 0.5f);
                if (!ProjectSceneGradientPointer(ray, _sceneGradientNormal, middle, out var moved)) return false;
                var offset = root.InverseTransformPoint(moved) - _sceneGradientGrab;
                draft.sceneStart = original.sceneStart + offset;
                draft.sceneEnd = original.sceneEnd + offset;
                return PreviewSceneGradientPlacement();
            }

            bool movingStart = _sceneGradientHandle == GradHandle.Start;
            Vector3 fixedPoint = root.TransformPoint(movingStart ? draft.sceneEnd : draft.sceneStart);
            var planePoint = fixedPoint;
            if (_sceneGradientOriginal != null)
                planePoint = root.TransformPoint(movingStart ? _sceneGradientOriginal.sceneStart : _sceneGradientOriginal.sceneEnd);
            if (!ProjectSceneGradientPointer(ray, _sceneGradientNormal, planePoint, out var point)) return false;
            point = ConstrainSceneGradientPoint(fixedPoint, point);
            Vector3 start = movingStart ? point : fixedPoint;
            Vector3 end = movingStart ? fixedPoint : point;
            return SetSceneGradientProjection(draft, root, _sceneGradientNormal, start, end) && PreviewSceneGradientPlacement();
        }

        bool SetSceneGradientProjection(DrawnGradientLayer layer, Transform root, Vector3 normal, Vector3 start, Vector3 end)
        {
            var along = Vector3.ProjectOnPlane(end - start, normal);
            float span = along.magnitude;
            if (!float.IsFinite(span) || span <= Mathf.Max(1e-6f, _uv.PosedSize * 1e-5f)) return false;

            var transpose = root.localToWorldMatrix.transpose;
            layer.sceneStart = root.InverseTransformPoint(start);
            layer.sceneEnd = root.InverseTransformPoint(end);
            layer.sceneAxis = transpose.MultiplyVector(along / (span * span));
            layer.sceneAcross = transpose.MultiplyVector(Vector3.Cross(normal, along) / (span * span));
            return true;
        }

        bool TryPlacedSceneGradientAngle(DrawnGradientLayer layer, out Transform root, out Vector3 normal,
            out Vector3 right, out Vector3 up, out float angle)
        {
            root = _uv?.AvatarRoot();
            normal = right = up = Vector3.zero;
            angle = 0f;
            if (root == null || !layer.sceneProjection || layer.shape == GradientShape.Circle) return false;
            normal = root.worldToLocalMatrix.transpose.MultiplyVector(layer.sceneNormal).normalized;
            if (normal == Vector3.zero) return false;

            right = Vector3.ProjectOnPlane(root.right, normal);
            right = (right.sqrMagnitude > 1e-8f ? right : Vector3.Cross(root.up, normal)).normalized;
            up = Vector3.Cross(normal, right);
            if (Vector3.Dot(up, root.up) < 0f) up = -up;
            var delta = root.TransformVector(layer.sceneEnd - layer.sceneStart);
            float x = Vector3.Dot(delta, right), y = Vector3.Dot(delta, up);
            angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            return float.IsFinite(angle) && x * x + y * y > 1e-12f;
        }

        void SetPlacedSceneGradientAngle(DrawnGradientLayer layer, float degrees)
        {
            if (_needsRebuild || _comp == null || _sceneGradientDraft != null || SelectedGradient != layer ||
                !EditingPlacedSceneGradient || !float.IsFinite(degrees) ||
                !TryPlacedSceneGradientAngle(layer, out var root, out var normal, out var right, out var up, out float previous)) return;
            degrees %= 360f;
            if (Mathf.Abs(Mathf.DeltaAngle(previous, degrees)) < 0.01f) return;

            var start = root.TransformPoint(layer.sceneStart);
            var end = root.TransformPoint(layer.sceneEnd);
            var depth = Vector3.Project(end - start, normal);
            float span = Vector3.ProjectOnPlane(end - start, normal).magnitude;
            float radians = degrees * Mathf.Deg2Rad;
            var rotated = (right * Mathf.Cos(radians) + up * Mathf.Sin(radians)) * span + depth;
            if (layer.shape == GradientShape.Linear) start = (start + end - rotated) * 0.5f;

            var candidate = layer.CopyPlacement();
            if (!SetSceneGradientProjection(candidate, root, normal, start, start + rotated)) return;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            string reason = SceneGradientOverlapProblem(candidate, root);
            if (reason != null)
            {
                Toast(reason, MessageType.Info, 5f);
                if (SceneGradientActive) ReportSceneGradientOverlap(SceneView.lastActiveSceneView, reason);
                return;
            }

            const string action = "Rotate gradient on model";
            WithTargetUndo(action, () =>
            {
                Undo.RecordObject(_recipe, action);
                if (_showOriginal) Undo.RecordObject(this, action);
                ApplySceneGradientProjection(layer, candidate);
                MarkEdited();
            });
            if (_sceneGradientRecovery != null && _toastText == _sceneGradientRecovery.reason) _toastText = null;
            _sceneGradientRecovery = null;
            ComposeNow();
            SceneView.RepaintAll();
        }

        static bool ProjectSceneGradientPointer(Ray ray, Vector3 normal, Vector3 origin, out Vector3 point)
        {
            var plane = new Plane(normal, origin);
            point = default;
            if (!plane.Raycast(ray, out float distance) || !float.IsFinite(distance)) return false;
            point = ray.GetPoint(distance);
            return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }

        bool PreviewSceneGradientPlacement()
        {
            _sceneGradientReady = true;
            ShowEditsIfBeforeOn();
            ComposeNow();
            return true;
        }

        bool CommitSceneGradient(out string reason, out bool targetChanged)
        {
            reason = null;
            targetChanged = false;
            var draft = _sceneGradientDraft;
            var original = _sceneGradientOriginal;
            if (draft == null || !_sceneGradientReady || (original != null && !_recipe.layers.Contains(original))) return false;
            if (original != null && draft.sceneStart == original.sceneStart && draft.sceneEnd == original.sceneEnd)
            {
                CancelSceneGradient(restoreComparison: false);
                if (_dirty) ComposeNow();
                return true;
            }
            reason = SceneGradientOverlapProblem(draft, _sceneGradientRoot);
            if (reason != null) return false;

            Undo.FlushUndoRecordObjects();
            targetChanged = !SceneGradientTargetStillMatches();
            if (targetChanged) return false;
            CancelSceneGradient(restoreComparison: false);
            Undo.IncrementCurrentGroup();
            string action = original == null ? "Draw gradient on model" : _sceneGradientHandle == GradHandle.Move
                ? "Move gradient on model" : "Adjust gradient on model";
            Undo.SetCurrentGroupName(action);
            if (original == null)
            {
                InsertLayer(draft, action, captureSelection: false);
                if (_grad.colours != GradientColours.Custom) RememberColour(_brush.color);
            }
            else
            {
                Undo.RecordObject(_recipe, action);
                ApplySceneGradientProjection(original, draft);
                MarkEdited();
            }
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            ComposeNow();
            Learn.Did(LearnEvents.SceneGradient);
            SceneView.RepaintAll();
            return true;
        }

        static void ApplySceneGradientProjection(DrawnGradientLayer layer, DrawnGradientLayer placement)
        {
            layer.sceneStart = placement.sceneStart;
            layer.sceneEnd = placement.sceneEnd;
            layer.sceneAxis = placement.sceneAxis;
            layer.sceneAcross = placement.sceneAcross;
        }

        string SceneGradientOverlapProblem(DrawnGradientLayer layer, Transform root)
        {
            if (_uv == null || root == null || layer.gradient == null || layer.opacity <= 0f) return null;
            bool alreadyClear = _uv == _sceneGradientClearUv && _uv.PoseVersion == _sceneGradientClearPose;
            if (alreadyClear && _alsoUsers.Count == 0) return null;
            bool faces = layer.maskMode == MaskMode.SelectedFaces;
            if (faces && !HasCurrentFaceLimit(layer)) return null;
            var scoped = layer.maskMode == MaskMode.WholeTexture ? null : new HashSet<int>(faces ? layer.faces : layer.islands);
            var pieces = _uv.triIsland;
            bool InsideScope(int t) => scoped == null || scoped.Contains(faces ? t : pieces[t]) != layer.invertMask;
            if (_alsoUsers.Count > 0)
                foreach (var part in _uv.parts)
                {
                    if (IncludedHitUsesCurrentUv(part)) continue;
                    for (int t = part.triStart; t < part.triStart + part.triCount; t++)
                        if (InsideScope(t)) return IncludedUvChangedHint;
                }
            if (alreadyClear) return null;
            var positions = _uv.PosedCombinedCached();
            var uv = _uv.uv;
            var indices = _uv.tris;
            var worldToLocal = root.worldToLocalMatrix;
            var projected = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var offset = worldToLocal.MultiplyPoint3x4(positions[i]) - layer.sceneStart;
                projected[i] = new Vector2(Vector3.Dot(offset, layer.sceneAxis), Vector3.Dot(offset, layer.sceneAcross));
            }

            Color[] ramp = null;
            bool uniformRamp = false;
            void ReadRamp()
            {
                if (ramp != null) return;
                ramp = new Color[256];
                uniformRamp = true;
                for (int i = 0; i < ramp.Length; i++)
                {
                    ramp[i] = (Color32)layer.gradient.Evaluate(i / 255f);
                    if (ramp[i] != ramp[0]) uniformRamp = false;
                }
            }

            Color Sample(Vector2 p)
            {
                float t = SceneGradientLookupPosition(layer, p);
                if (t < 0f) return Color.clear;
                float index = t * 255f;
                int low = Mathf.FloorToInt(index);
                var c = Color.Lerp(ramp[low], ramp[Mathf.Min(low + 1, 255)], index - low);
                float weight = c.a * layer.opacity;
                return new Color(c.r * weight, c.g * weight, c.b * weight, weight);
            }

            var polygon = new Vector2[12];
            var clipBuffer = new Vector2[12];
            int w = Mathf.Max(_comp.Width, _comp.SourceWidth), h = Mathf.Max(_comp.Height, _comp.SourceHeight);
            int checksLeft = 262144;
            string ProblemAt(Vector2 point, SceneGradientUvTriangle a, SceneGradientUvTriangle b, bool sameScope)
            {
                var ca = Sample(a.Project(point, projected, indices));
                var cb = Sample(b.Project(point, projected, indices));
                const float step = 1f / 255f;
                if (!sameScope && Mathf.Max(ca.a, cb.a) > step)
                    return faces
                        ? Loc.T("Selected and unselected faces share texture pixels. Select them together, or draw on the texture to colour both.")
                        : Loc.T("Selected and unselected pieces share texture pixels. Select them together, or draw on the texture to colour both.");
                if (sameScope && (Mathf.Abs(ca.r - cb.r) > step || Mathf.Abs(ca.g - cb.g) > step ||
                                  Mathf.Abs(ca.b - cb.b) > step || Mathf.Abs(ca.a - cb.a) > step))
                    return Loc.T("Shared texture pixels would get different colours. Change the gradient direction, or draw on the texture to colour them together.");
                return null;
            }

            string CheckColours(SceneGradientUvTriangle a, SceneGradientUvTriangle b, int count, bool sameScope)
            {
                ReadRamp();
                if (uniformRamp && (ramp[0].a == 0f || (sameScope && layer.edge != GradientEdge.DontDraw))) return null;
                Vector2 min = polygon[0], max = min, centre = Vector2.zero;
                bool identical = sameScope;
                float tolerance = layer.edge == GradientEdge.NotRepeat ? 1e-6f : 0f;
                bool aBefore = layer.shape == GradientShape.Linear && layer.edge == GradientEdge.DontDraw, aAfter = aBefore;
                bool bBefore = aBefore, bAfter = aBefore;
                for (int i = 0; i < count; i++)
                {
                    var p = polygon[i];
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                    centre += p;
                    var qa = a.Project(p, projected, indices);
                    var qb = b.Project(p, projected, indices);
                    identical &= Mathf.Abs(qa.x - qb.x) <= tolerance &&
                        (layer.shape == GradientShape.Linear || Mathf.Abs(qa.y - qb.y) <= tolerance);
                    aBefore &= qa.x < 0f; aAfter &= qa.x > 1f;
                    bBefore &= qb.x < 0f; bAfter &= qb.x > 1f;
                }
                if (identical || ((aBefore || aAfter) && (bBefore || bAfter))) return null;

                string problem = ProblemAt(centre / count, a, b, sameScope);
                if (problem != null) return problem;
                for (int i = 0; i < count; i++)
                {
                    problem = ProblemAt(polygon[i], a, b, sameScope);
                    if (problem != null) return problem;
                }

                int x0 = Mathf.Max(0, Mathf.CeilToInt(min.x * w - 0.5f));
                int x1 = Mathf.Min(w - 1, Mathf.FloorToInt(max.x * w - 0.5f));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(min.y * h - 0.5f));
                int y1 = Mathf.Min(h - 1, Mathf.FloorToInt(max.y * h - 0.5f));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (--checksLeft < 0)
                        return Loc.T("Use the texture view to draw a gradient across these shared pixels.");
                    var point = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
                    if (!a.Contains(point) || !b.Contains(point)) continue;
                    problem = ProblemAt(point, a, b, sameScope);
                    if (problem != null) return problem;
                }
                return null;
            }

            int grid = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(pieces.Length / 8f)), 16, 128);
            int Bin(float value) => Mathf.Min(grid - 1, Mathf.FloorToInt(value * grid));
            var bins = new List<int>[grid * grid];
            var triangles = new SceneGradientUvTriangle[pieces.Length];
            bool overlap = false;
            for (int t = 0; t < pieces.Length; t++)
            {
                int i = t * 3;
                int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                if (!FiniteUV(projected[ia]) || !FiniteUV(projected[ib]) || !FiniteUV(projected[ic])) continue;
                var a = new SceneGradientUvTriangle(i, uv[ia], uv[ib], uv[ic]);
                if (!double.IsFinite(a.area) || a.area == 0 || !FiniteUV(a.min) || !FiniteUV(a.max) ||
                    a.min.x >= a.max.x || a.min.y >= a.max.y) continue;
                triangles[t] = a;
                int startX = Bin(a.min.x), startY = Bin(a.min.y);
                int endX = Bin(a.max.x), endY = Bin(a.max.y);
                bool inScope = InsideScope(t);
                for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                {
                    var bin = bins[y * grid + x] ??= new List<int>();
                    foreach (int other in bin)
                    {
                        var b = triangles[other];
                        if (Mathf.Max(startX, Bin(b.min.x)) != x || Mathf.Max(startY, Bin(b.min.y)) != y ||
                            a.max.x <= b.min.x || b.max.x <= a.min.x || a.max.y <= b.min.y || b.max.y <= a.min.y) continue;

                        int count = SharedSceneGradientPolygon(a, b, polygon, clipBuffer);
                        if (count == 0) continue;
                        overlap = true;
                        bool otherInScope = InsideScope(other);
                        if (!inScope && !otherInScope) continue;
                        var problem = CheckColours(a, b, count, inScope == otherInScope);
                        if (problem != null) return problem;
                    }
                    bin.Add(t);
                }
            }
            if (!overlap)
            {
                _sceneGradientClearUv = _uv;
                _sceneGradientClearPose = _uv.PoseVersion;
            }
            return null;
        }

        bool ValidatePlacedGradientAppearance(DrawnGradientLayer layer, DrawnGradientLayer candidate)
        {
            bool changesProjection = candidate.gradient != layer.gradient || candidate.shape != layer.shape ||
                candidate.edge != layer.edge || candidate.flatness != layer.flatness;
            if (!candidate.sceneProjection || _uv == null || _comp == null || (!changesProjection && candidate.opacity <= layer.opacity))
                return true;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            string reason = SceneGradientOverlapProblem(candidate, _uv.AvatarRoot());
            if (reason == null) return true;
            Toast(reason, MessageType.Info, 5f);
            if (SceneGradientActive) ReportSceneGradientOverlap(SceneView.lastActiveSceneView, reason);
            return false;
        }

        readonly struct SceneGradientUvTriangle
        {
            readonly int index;
            internal readonly Vector2 a, b, c, min, max;
            internal readonly double area;

            internal SceneGradientUvTriangle(int index, Vector2 a, Vector2 b, Vector2 c)
            {
                this.index = index;
                this.a = a;
                this.b = b;
                this.c = c;
                min = Vector2.Max(Vector2.zero, Vector2.Min(a, Vector2.Min(b, c)));
                max = Vector2.Min(Vector2.one, Vector2.Max(a, Vector2.Max(b, c)));
                area = SideOfEdge(a, b, c);
            }

            internal Vector2 Project(Vector2 point, Vector2[] projected, int[] indices)
            {
                float first = (float)(SideOfEdge(b, c, point) / area);
                float second = (float)(SideOfEdge(c, a, point) / area);
                var last = projected[indices[index + 2]];
                return last + (projected[indices[index]] - last) * first + (projected[indices[index + 1]] - last) * second;
            }

            internal bool Contains(Vector2 p) => TriangleContains(a, b, c, p);
        }

        static int SharedSceneGradientPolygon(SceneGradientUvTriangle a, SceneGradientUvTriangle b, Vector2[] polygon, Vector2[] buffer)
        {
            polygon[0] = a.a; polygon[1] = a.b; polygon[2] = a.c;
            int count = 3;
            void Clip(Vector2 from, Vector2 to)
            {
                if (count == 0) return;
                int next = 0;
                var prev = polygon[count - 1];
                double prevSide = SideOfEdge(from, to, prev);
                for (int i = 0; i < count; i++)
                {
                    var p = polygon[i];
                    double side = SideOfEdge(from, to, p);
                    if ((side > 0 && prevSide < 0) || (side < 0 && prevSide > 0))
                        buffer[next++] = Vector2.LerpUnclamped(prev, p, (float)(prevSide / (prevSide - side)));
                    if (side >= 0) buffer[next++] = p;
                    prev = p;
                    prevSide = side;
                }
                count = next;
                System.Array.Copy(buffer, polygon, count);
            }

            if (b.area > 0) { Clip(b.a, b.b); Clip(b.b, b.c); Clip(b.c, b.a); }
            else { Clip(b.b, b.a); Clip(b.c, b.b); Clip(b.a, b.c); }
            Clip(Vector2.zero, Vector2.right);
            Clip(Vector2.right, Vector2.one);
            Clip(Vector2.one, Vector2.up);
            Clip(Vector2.up, Vector2.zero);
            if (count < 3) return 0;
            double area = 0;
            for (int i = 1; i + 1 < count; i++) area += SideOfEdge(polygon[0], polygon[i], polygon[i + 1]);
            return System.Math.Abs(area) > 1e-12 ? count : 0;
        }

        static float SceneGradientLookupPosition(DrawnGradientLayer layer, Vector2 projected)
        {
            float x = projected.x, t = x;
            if (layer.shape != GradientShape.Linear)
            {
                float y = projected.y;
                if (layer.shape == GradientShape.Ellipse) y /= Mathf.Max(layer.flatness, 0.01f);
                t = Mathf.Sqrt(x * x + y * y);
            }
            switch (layer.edge)
            {
                case GradientEdge.Repeat: return t - Mathf.Floor(t);
                case GradientEdge.ReverseRepeat: return 1f - Mathf.Abs((t * 0.5f - Mathf.Floor(t * 0.5f)) * 2f - 1f);
                case GradientEdge.DontDraw when t < 0f || t > 1f: return -1f;
                default: return Mathf.Clamp01(t);
            }
        }

        void CancelSceneGradient(bool restoreComparison = true)
        {
            if (_needsRebuild || !SceneGradientActive) _sceneGradientRecovery = null;
            _sceneGradientEndpointGrab = Vector2.zero;
            if (_sceneGradientDraft == null) return;
            _sceneGradientDraft = _sceneGradientOriginal = null;
            _sceneGradientTargetMatches = null;
            _sceneGradientCompositor = null;
            _sceneGradientRoot = null;
            _placementPreview.Clear();
            if (_sceneGradientReady)
            {
                if (restoreComparison) _showOriginal = _sceneGradientOriginalView;
                _dirty = true;
            }
            _sceneGradientReady = false;
            Repaint();
            if (_sceneGradientView != null) _sceneGradientView.Repaint();
        }

        bool SceneGradientTargetStillMatches() => _sceneGradientTargetMatches?.Invoke() == true;

        void RestoreSceneGradientAfterFailedPlacement()
        {
            bool samePreview = _comp == _sceneGradientCompositor;
            CancelSceneGradient();
            if (!samePreview) return;
            if (_needsRebuild) _preview.Clear();
            else if (_dirty) ComposeNow();
        }

        void ReleaseSceneGradientControl(SceneView scene)
        {
            if (scene != _sceneGradientView || _sceneGradientControl == 0) return;
            bool ownsPointer = GUIUtility.hotControl == _sceneGradientControl;
            if (_sceneGradientDraft != null && ownsPointer) return;
            if (ownsPointer) GUIUtility.hotControl = 0;
            _sceneGradientView = null;
            _sceneGradientControl = 0;
            RestoreAbandonedSceneGradient();
        }

        void ReleaseClosedSceneGradientView()
        {
            if (_sceneGradientControl == 0 || _sceneGradientView != null) return;
            _sceneGradientView = null;
            _sceneGradientControl = 0;
            RestoreAbandonedSceneGradient();
        }

        void RestoreAbandonedSceneGradient()
        {
            if (_sceneGradientDraft == null) return;
            CancelSceneGradient();
            if (_dirty && !_needsRebuild) ComposeNow();
            SceneView.RepaintAll();
        }

        static bool IsSceneGradientViewportPoint(SceneView scene, Vector2 mouse)
        {
            Rect viewport = scene.cameraViewport;
            if (!new Rect(Vector2.zero, viewport.size).Contains(mouse)) return false;
            var root = scene.rootVisualElement;
            var element = root.panel?.Pick(root.LocalToWorld(mouse + viewport.position));
            for (; element != null; element = element.parent)
                if (element.ClassListContains(Overlay.ussClassName)) return false;
            return true;
        }

        void HandleSceneGradient(SceneView scene, Event e)
        {
            if (scene.camera == null) return;
            int id = GUIUtility.GetControlID(SceneGradientHint, FocusType.Passive);
            bool otherDrag = _sceneGradientView != null && _sceneGradientView != scene;
            if (e.type == EventType.Layout && !otherDrag) HandleUtility.AddDefaultControl(id);
            var selected = EditingPlacedSceneGradient ? SelectedGradient : null;
            bool canAdjust = selected?.enabled == true;
            bool pickingColour = _sceneGradientDraft == null && ScenePaintColourPick(e);
            if (e.type == EventType.Repaint)
            {
                if (pickingColour) DrawScenePaintColourPickHint(scene);
                else
                {
                    DrawSceneGradientHint(canAdjust ? selected : null);
                    if (!otherDrag) DrawSceneGradientGuide(scene, _sceneGradientDraft ?? selected);
                }
            }
            if (otherDrag) return;
            RepaintSceneOnColourPickModifier(scene, e);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && GUIUtility.hotControl == 0:
                {
                    var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    if (pickingColour)
                    {
                        if (SceneGradientHit(ray, out var hit, out var reason, pickingColour: true)) PickColour(hit.uv);
                        else ReportSceneGradientGesture(scene, reason, 3f);
                        scene.Repaint();
                    }
                    else
                    {
                        _sceneGradientRecovery = null;
                        var endpointGrab = Vector2.zero;
                        var handle = canAdjust ? SceneGradientHandleAt(selected, e.mousePosition, out endpointGrab) : GradHandle.None;
                        var reason = BeginSceneGradient(ray, scene.camera.transform.forward, handle);
                        if (reason != null) ReportSceneGradientGesture(scene, reason, 3f);
                        else
                        {
                            SetSceneGradientAxes(scene.camera.transform.right, scene.camera.transform.up);
                            _sceneGradientControl = id;
                            _sceneGradientView = scene;
                            _sceneGradientMouseDown = e.mousePosition;
                            _sceneGradientEndpointGrab = endpointGrab;
                            GUIUtility.hotControl = id;
                            scene.Repaint();
                        }
                    }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when _sceneGradientView == scene && GUIUtility.hotControl == id:
                    if ((e.mousePosition - _sceneGradientMouseDown).sqrMagnitude >= 9f && IsSceneGradientViewportPoint(scene, e.mousePosition))
                        UpdateSceneGradientAtPointer(e.mousePosition);
                    e.Use();
                    scene.Repaint();
                    break;
                case EventType.MouseUp when _sceneGradientView == scene && GUIUtility.hotControl == id && e.button == 0:
                {
                    string overlap = null;
                    bool targetChanged = !SceneGradientTargetStillMatches();
                    bool inViewport = IsSceneGradientViewportPoint(scene, e.mousePosition);
                    if (targetChanged || (e.mousePosition - _sceneGradientMouseDown).sqrMagnitude < 9f ||
                        !inViewport || !UpdateSceneGradientAtPointer(e.mousePosition) ||
                        !CommitSceneGradient(out overlap, out targetChanged))
                    {
                        RestoreSceneGradientAfterFailedPlacement();
                        if (targetChanged) ReportSceneGradientGesture(scene, SceneGradientTargetChanged, 5f);
                        else if (overlap != null)
                        {
                            Toast(overlap, MessageType.Info, 5f);
                            ReportSceneGradientOverlap(scene, overlap);
                        }
                        else ReportSceneGradientGesture(scene, inViewport
                            ? Loc.T("Drag to set the length and direction of the gradient.")
                            : Loc.T("Release inside the Scene view to place the gradient."), 5f);
                    }
                    ReleaseSceneGradientControl(scene);
                    e.Use();
                    scene.Repaint();
                    break;
                }
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && (_sceneGradientDraft != null || CanUseShortcuts(e)):
                    if (_sceneGradientDraft != null)
                    {
                        CancelSceneGradient();
                        ReleaseSceneGradientControl(scene);
                        if (_dirty) ComposeNow();
                    }
                    else if (_eyedropperArmed) _eyedropperArmed = false;
                    else SetTool(Tool.Select);
                    e.Use();
                    Repaint();
                    scene.Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.I && _sceneGradientDraft == null && CanUseShortcuts(e):
                    _eyedropperArmed = !_eyedropperArmed;
                    e.Use();
                    Repaint();
                    scene.Repaint();
                    break;
            }
        }

        bool UpdateSceneGradientAtPointer(Vector2 mouse)
        {
            var point = mouse - _sceneGradientEndpointGrab;
            return FiniteUV(point) && UpdateSceneGradient(HandleUtility.GUIPointToWorldRay(point));
        }

        GradHandle SceneGradientHandleAt(DrawnGradientLayer gradient, Vector2 mouse, out Vector2 endpointGrab)
        {
            endpointGrab = Vector2.zero;
            var root = _uv.AvatarRoot();
            if (root == null) return GradHandle.None;
            var startPoint = root.TransformPoint(gradient.sceneStart);
            var endPoint = root.TransformPoint(gradient.sceneEnd);
            var start = HandleUtility.WorldToGUIPoint(startPoint);
            var end = HandleUtility.WorldToGUIPoint(endPoint);
            float a = FiniteUV(start) ? (start - mouse).sqrMagnitude : float.PositiveInfinity;
            float b = FiniteUV(end) ? (end - mouse).sqrMagnitude : float.PositiveInfinity;
            Vector2 middle = HandleUtility.WorldToGUIPoint((startPoint + endPoint) * 0.5f);
            float middleDistance = (middle - mouse).sqrMagnitude;
            if (middleDistance <= 64f && middleDistance < Mathf.Min(a, b)) return GradHandle.Move;
            if (!(Mathf.Min(a, b) <= 81f)) return GradHandle.None;
            bool grabStart = a < b;
            endpointGrab = mouse - (grabStart ? start : end);
            return grabStart ? GradHandle.Start : GradHandle.End;
        }

        void DrawSceneGradientGuide(SceneView scene, DrawnGradientLayer gradient)
        {
            if (gradient == null || !gradient.sceneProjection || !gradient.enabled) return;
            var root = _uv.AvatarRoot();
            if (root == null) return;
            Vector3 start = root.TransformPoint(gradient.sceneStart), end = root.TransformPoint(gradient.sceneEnd);
            var camera = scene.camera;
            var facing = camera.transform.forward;
            float viewportWidth = scene.cameraViewport.width;
            var depth = Handles.zTest;
            using (new Handles.DrawingScope())
            try
            {
                Handles.zTest = CompareFunction.Always;
                Handles.color = new Color(0f, 0f, 0f, 0.85f);
                Handles.DrawAAPolyLine(5f, start, end);
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(2f, start, end);
                DrawSceneGradientPoint(start, facing, gradient.gradient.Evaluate(0f), viewportWidth, Loc.T("Start"));
                if (start != end)
                {
                    DrawSceneGradientPoint(end, facing, gradient.gradient.Evaluate(1f), viewportWidth, Loc.T("End"));
                    var middle = (start + end) * 0.5f;
                    Handles.color = Color.white;
                    Handles.RectangleHandleCap(0, middle, camera.transform.rotation, HandleUtility.GetHandleSize(middle) * 0.042f, EventType.Repaint);
                }
            }
            finally { Handles.zTest = depth; }
        }

        static void DrawSceneGradientPoint(Vector3 point, Vector3 facing, Color colour, float viewportWidth, string text)
        {
            float size = HandleUtility.GetHandleSize(point) * 0.055f;
            Handles.color = Color.black;
            Handles.DrawSolidDisc(point, facing, size * 1.25f);
            Handles.color = Color.white;
            Handles.DrawSolidDisc(point, facing, size);
            colour.a = 1f;
            Handles.color = colour;
            Handles.DrawSolidDisc(point, facing, size * 0.68f);

            var style = GUI.skin.label;
            var content = new GUIContent("    " + text);
            float x = HandleUtility.WorldToGUIPoint(point).x;
            if (x + style.CalcSize(content).x > viewportWidth && x < viewportWidth)
            {
                if (s_sceneGradientLabelStyle != style)
                {
                    s_sceneGradientLabelStyle = style;
                    s_inwardGradientLabelStyle = new GUIStyle(style) { alignment = TextAnchor.UpperRight };
                    s_inwardGradientLabelStyle.padding.right += 10;
                }
                content.text = text;
                style = s_inwardGradientLabelStyle;
            }
            using (new Handles.DrawingScope(Color.white)) Handles.Label(point, content, style);
        }

        void RefreshSceneGradientPose()
        {
            if (_comp == null || _uv == null || !_recipe.layers.Exists(layer =>
                layer is DrawnGradientLayer gradient && gradient.sceneProjection && gradient.enabled)) return;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            if (_sceneGradientPose != _uv.PoseVersion) ComposeNow();
        }

        internal string AutoSceneGradient(Ray from, Ray to, Vector3 viewNormal)
        {
            var reason = BeginSceneGradient(from, viewNormal, GradHandle.None);
            if (reason != null) return reason;
            bool targetChanged = false;
            if (UpdateSceneGradient(to) && CommitSceneGradient(out reason, out targetChanged)) return null;
            RestoreSceneGradientAfterFailedPlacement();
            if (targetChanged) return SceneGradientTargetChanged;
            return reason ?? "The gradient needs two different points.";
        }
    }
}
