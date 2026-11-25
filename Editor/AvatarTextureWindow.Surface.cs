using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        System.Func<MeshUV.Part, MeshUV.Part, bool> _canJoinBrushSlots;

        void SetupSurfaceStroke(PaintStroke s, MeshUV.SurfaceHit? hit)
        {
            if (s == null || hit == null || _uv == null || _comp == null) return;
            if (!_brush.surfaceBrush || s.isFill || s.tool == StrokeTool.Smudge) return;
            if (s.shape == null || !s.shape.SurfaceCapable) return;
            var h = hit.Value;
            float size = _uv.PosedSize;
            if (h.uvPerWorld <= 0 || size <= 0) return;
            float worldR = Mathf.Min(s.radius / h.uvPerWorld, 2f);
            s.surface = true;
            s.surfRadius = worldR / size;
            s.surfTwoSided = MaterialIsTwoSided(_material);
        }

        void AddBrushClipPieces(PaintStroke stroke, List<int> pieces, Vector2 uv, int piece, float pressure,
            int triangle, Vector3 bary)
        {
            if (!stroke.clip || _uv == null) return;
            if (piece >= 0 && !pieces.Contains(piece)) pieces.Add(piece);
            _uv.IncludeIslandsAtUV(uv, pieces);
            if (!stroke.surface || triangle < 0) return;

            bary.x = 1f - bary.y - bary.z;
            float radius = BrushClipRadius(stroke, _uv.PosedSize, pressure);
            _uv.IncludeBrushJoins(triangle, bary, radius, pieces, _editedPartTileFilter ??= EditedPartTileFilter,
                _canJoinBrushSlots ??= CanJoinBrushSlots);
        }

        static float BrushClipRadius(PaintStroke stroke, float posedSize, float pressure)
        {
            float radius = Mathf.Max(1e-6f, stroke.surfRadius * posedSize * BrushRenderer.SizeFactor(stroke, pressure));
            var shape = stroke.shape;
            if (shape?.tip != BrushTip.Stamp) return radius;
            float size = shape.jitterVersion == 1 ? 1f + Mathf.Max(0f, shape.sizeJitter) : 1f;
            float roundness = Mathf.Max(.05f, shape.roundness);
            float extent = Mathf.Sqrt(1f + roundness * roundness + .35f * .35f);
            return radius * (size * extent + Mathf.Sqrt(2f) * Mathf.Max(0f, shape.scatter));
        }

        bool CanJoinBrushSlots(MeshUV.Part first, MeshUV.Part second)
        {
            var slot = CurrentSlot;
            var renderer = first.renderer;
            if (_material == null || slot?.texture == null || renderer == null || renderer != second.renderer ||
                TargetResolver.GetSharedMesh(renderer) != first.mesh || !IsDrawn(renderer)) return false;
            renderer.GetSharedMaterials(_editedPartMaterials);
            if ((uint)first.materialSlot >= (uint)_editedPartMaterials.Count ||
                (uint)second.materialSlot >= (uint)_editedPartMaterials.Count) return false;
            var a = _editedPartMaterials[first.materialSlot];
            var b = _editedPartMaterials[second.materialSlot];
            var mode = MaterialCullMode(_material);
            return a != null && b != null && MaterialCullMode(a) == mode && MaterialCullMode(b) == mode &&
                BrushSlotStillEdited(first, a, slot) && BrushSlotStillEdited(second, b, slot);
        }

        bool BrushSlotStillEdited(MeshUV.Part part, Material material, TextureSlot slot)
        {
            foreach (var user in _alsoUsers)
                if (user.slot.renderer == part.renderer && user.slot.materialIndex == part.materialSlot)
                    return user.material == material && IncludedHitUsesCurrentUv(part);
            return material == _material && SavedEditSlotStillAssigned(material, slot);
        }

        static bool MaterialIsTwoSided(Material material) => MaterialCullMode(material) == CullMode.Off;

        static readonly int[] CullProperties = { Shader.PropertyToID("_Cull"), Shader.PropertyToID("_CullMode"), Shader.PropertyToID("_Culling") };

        static CullMode MaterialCullMode(Material material)
        {
            if (material != null)
                foreach (int property in CullProperties)
                {
                    if (!material.HasProperty(property)) continue;
                    float value = material.GetFloat(property);
                    if (!float.IsFinite(value)) break;
                    int mode = Mathf.RoundToInt(value);
                    if (mode >= (int)CullMode.Off && mode <= (int)CullMode.Back) return (CullMode)mode;
                    break;
                }
            return CullMode.Back;
        }

        void DrawSurfaceBrushToggle()
        {
            if (!_paintInScene) return;
            EditorGUI.indentLevel++;
            bool on;
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("paint.smoothJoins");
                on = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Smooth across joins"),
                    Loc.T("On the model, supported brush tips follow the surface across UV seams and keep their size.")), _brush.surfaceBrush);
                StudioGUI.Info("seam");
            }
            if (on != _brush.surfaceBrush) { _brush.surfaceBrush = on; Repaint(); }
            if (on)
            {
                if (_brush.fillTool) { }
                else if (BlendToolActive && _brush.blendKind == StrokeTool.Smudge)
                    GUILayout.Label(Loc.T("Smudge is painted flat on the texture, so it stops at seams. Blur crosses them."), StudioGUI.Hint);
                else if (!_brush.shape.SurfaceCapable)
                    GUILayout.Label(Loc.T("This brush tip is painted flat on the texture, so it stops at seams. Round tips without scatter cross them."), StudioGUI.Hint);
                else if (_brush.stayInsideIslands)
                    GUILayout.Label(Loc.T("The 3D brush can cross a joined seam before the brush centre reaches it."), StudioGUI.Hint);
            }
            EditorGUI.indentLevel--;
        }
    }
}
