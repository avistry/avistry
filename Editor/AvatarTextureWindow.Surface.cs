using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// 3D brush: strokes painted on the model in the Scene view are stored as points on the mesh surface and drawn as
    /// spheres on the posed model, so paint is round on the surface and crosses UV seams (see BrushRenderer.RenderSurface).
    /// </summary>
    partial class AvatarTextureWindow
    {
        /// <summary>Turns a new stroke into a 3D-brush stroke when it starts on the model and the brush supports it.</summary>
        void SetupSurfaceStroke(PaintStroke s, MeshUV.SurfaceHit? hit)
        {
            if (s == null || hit == null || _uv == null || _comp == null) return;
            if (!_brush.surfaceBrush || s.isFill || s.tool == StrokeTool.Smudge) return;
            if (s.shape == null || !s.shape.SurfaceCapable) return;
            var h = hit.Value;
            float size = _uv.PosedSize;
            if (h.uvPerWorld <= 0 || size <= 0) return;
            // Same size as the ring shown on the model: the brush's texture size at the spot where the stroke starts.
            float worldR = Mathf.Min(s.radius / h.uvPerWorld, 2f);
            s.surface = true;
            s.surfRadius = worldR / size;
            s.surfTwoSided = MaterialIsTwoSided(_material);
        }

        /// <summary>
        /// True when the material draws both sides of its faces (hair cards, skirts…). Then a face is painted whichever
        /// way it points; on one-sided materials the brush skips faces that point away, so paint doesn't go through to
        /// the far side of a thin part. Poiyomi and lilToon both store culling in _Cull (0 = off).
        /// </summary>
        static bool MaterialIsTwoSided(Material m)
        {
            if (m == null) return false;
            foreach (var p in new[] { "_Cull", "_CullMode", "_Culling" })
                if (m.HasProperty(p)) return Mathf.RoundToInt(m.GetFloat(p)) == 0;
            return false;
        }

        /// <summary>The "Paint across seams" option, shown under "Paint directly on the model".</summary>
        void DrawSurfaceBrushToggle()
        {
            if (!_paintInScene) return;
            EditorGUI.indentLevel++;
            bool on;
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("paint.smoothJoins");
                on = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Smooth across joins"),
                    Loc.T("On the model, the brush is a round spot on the surface: lines continue cleanly across UV seams and aren't stretched by the texture layout.")), _brush.surfaceBrush);
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
                    GUILayout.Label(Loc.T("With “Brush stays inside the piece it starts on” on, paint only reaches the parts the brush centre has touched."), StudioGUI.Hint);
            }
            EditorGUI.indentLevel--;
        }
    }
}
