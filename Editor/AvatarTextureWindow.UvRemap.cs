using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static string CombinedUvChangeBlockedHint => Loc.T("Changing these parts would affect saved paint or limits. Keep the current parts.");

        bool TryPlanCombinedUvRemap(MeshUV previous, MeshUV next, out Action apply)
        {
            apply = null;
            var remap = new CombinedUvRemap(previous, next);
            if (_recipe != null && !_recipe.layers.TrueForAll(remap.PlanLayer)) return false;

            var selection = remap.Selection(_selection, remap.islands);
            var faces = previous != null && _selectedFaceLayout == previous.FaceLayout
                ? remap.Selection(_selectedFaces, remap.triangles) : new List<int>();
            apply = () =>
            {
                remap.Apply();
                _selection = selection;
                ClearFaceSelection();
                if (faces.Count == 0) return;
                _selectedFaces = faces;
                _selectedFaceLayout = next.FaceLayout;
            };
            return true;
        }

        sealed class CombinedUvRemap
        {
            readonly MeshUV _previous, _next;
            readonly List<Action> _changes = new List<Action>();
            readonly HashSet<Layer> _plannedLayers = new HashSet<Layer>();
            readonly HashSet<PaintStroke> _plannedStrokes = new HashSet<PaintStroke>();
            readonly bool _layoutChanged;
            float? _radiusScale;
            internal readonly int[] triangles, islands;

            internal CombinedUvRemap(MeshUV previous, MeshUV next)
            {
                _previous = previous;
                _next = next;
                (triangles, islands) = previous != null ? previous.RemapTo(next) : (Array.Empty<int>(), Array.Empty<int>());
                _layoutChanged = previous?.CombinedLayout != next?.CombinedLayout ||
                    Array.Exists(triangles, triangle => triangle < 0);
            }

            internal void Apply()
            {
                foreach (var change in _changes) change();
            }

            static int Map(int id, int[] map) => (uint)id < (uint)map.Length ? map[id] : -1;

            internal List<int> Selection(List<int> source, int[] map) => source.Select(id => Map(id, map)).Where(id => id >= 0).ToList();

            bool PlanIds(List<int> source, int[] map, bool pieceIds = false)
            {
                if (source == null || source.Count == 0) return true;
                List<(int at, int value)> moved = null;
                for (int i = 0; i < source.Count; i++)
                {
                    if (pieceIds && source[i] == -1) continue;
                    int mapped = Map(source[i], map);
                    if (mapped < 0) return false;
                    if (mapped != source[i]) (moved ??= new List<(int, int)>()).Add((i, mapped));
                }
                if (moved != null) _changes.Add(() => { foreach (var (at, value) in moved) source[at] = value; });
                return true;
            }

            bool PlanSurface(List<Vector4> source)
            {
                List<(int at, Vector4 point)> moved = null;
                for (int i = 0; i < source.Count; i++)
                {
                    var point = source[i];
                    if (!float.IsFinite(point.x)) return false;
                    int mapped = Map(Mathf.RoundToInt(point.x), triangles);
                    if (mapped < 0) return false;
                    if (mapped == point.x) continue;
                    point.x = mapped;
                    (moved ??= new List<(int, Vector4)>()).Add((i, point));
                }
                if (moved != null) _changes.Add(() => { foreach (var (at, point) in moved) source[at] = point; });
                return true;
            }

            bool PlanFillRegion(PaintFillRegion region)
            {
                if (region?.UsesGeometry != true || _previous == null || region.layout != _previous.FaceLayout) return true;
                if (_next == null || region.limitPieces && !PlanIds(region.pieces, islands, pieceIds: true) ||
                    region.limitFaces && !PlanIds(region.faces, triangles)) return false;
                if (region.layout != _next.FaceLayout) _changes.Add(() => region.layout = _next.FaceLayout);
                return true;
            }

            bool PlanStroke(PaintStroke stroke)
            {
                if (stroke == null || !_plannedStrokes.Add(stroke)) return true;
                if (_layoutChanged && stroke.isFill && (stroke.fillRegion == null || stroke.fillRegion.scope == PaintFillScope.Legacy))
                    return false;
                if (stroke.clip && !PlanIds(stroke.clipIslands, islands, pieceIds: true)) return false;
                if (!PlanFillRegion(stroke.fillRegion) || !PlanFillRegion(stroke.fillMirrorRegion)) return false;

                bool surface = stroke.IsSurface;
                if (surface && !PlanSurface(stroke.surf)) return false;
                if (stroke.mirrorPaths != null)
                    foreach (var mirror in stroke.mirrorPaths)
                    {
                        if (mirror == null) continue;
                        if (stroke.clip && !PlanIds(mirror.pieces, islands, pieceIds: true)) return false;
                        if (!stroke.surface || mirror.surfacePoints == null || mirror.surfacePoints.Count == 0 ||
                            mirror.surfacePoints.Count != mirror.points?.Count) continue;
                        if (!PlanSurface(mirror.surfacePoints)) return false;
                        surface = true;
                    }
                if (surface)
                {
                    float scale = SurfaceRadiusScale();
                    if (scale != 1f) _changes.Add(() => stroke.surfRadius *= scale);
                }
                return true;
            }

            float SurfaceRadiusScale()
            {
                if (_radiusScale.HasValue) return _radiusScale.Value;
                if (_previous == null || _next == null || _next.PosedSize <= 0f) return 1f;
                if (_previous.PoseChanged()) _previous.RefreshPose();
                float scale = _previous.PosedSize / _next.PosedSize;
                _radiusScale = scale > 0f && float.IsFinite(scale) ? scale : 1f;
                return _radiusScale.Value;
            }

            internal bool PlanLayer(Layer layer)
            {
                if (layer == null || !_plannedLayers.Add(layer)) return true;
                if (!PlanIds(layer.islands, islands, pieceIds: true)) return false;
                if (_previous != null && layer.faceLayout == _previous.FaceLayout)
                {
                    if (_next == null || !PlanIds(layer.faces, triangles)) return false;
                    if (layer.faceLayout != _next.FaceLayout) _changes.Add(() => layer.faceLayout = _next.FaceLayout);
                }
                if (layer.maskStrokes?.TrueForAll(PlanStroke) == false) return false;
                if (layer is PaintLayer paint) return paint.strokes?.TrueForAll(PlanStroke) != false;
                return layer is not MixedMergeLayer merged || merged.originals?.TrueForAll(PlanLayer) != false;
            }
        }
    }
}
