using System.Collections.Generic;
using UnityEditor;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static string FaceLoopTip => Loc.T("Follow a row of quad faces or triangles with a clear path. Seams and branches stop the row.");
        static string FaceLoopHint => Loc.T("Click near a connecting edge to select a face row. Shift adds, Ctrl removes.");

        MeshUV.FaceLoopStatus FindSceneFaceLoop(MeshUV.SurfaceHit hit, out IReadOnlyList<int> faces)
        {
            var posed = _uv.PosedCombinedCached();
            return _uv.FindFaceLoop(new[] { hit.triangle }, i => HandleUtility.WorldToGUIPoint(posed[i]),
                HandleUtility.WorldToGUIPoint(hit.point), out faces);
        }

        MeshUV.BoundaryLoopStatus FindSceneBoundaryLoop(MeshUV.SurfaceHit hit, out IReadOnlyList<int> faces)
        {
            var posed = _uv.PosedCombinedCached();
            return _uv.FindBoundaryLoop(new[] { hit.triangle }, i => HandleUtility.WorldToGUIPoint(posed[i]),
                HandleUtility.WorldToGUIPoint(hit.point), out faces);
        }

        void ApplyBoundaryLoop(MeshUV.BoundaryLoopStatus status, IReadOnlyList<int> faces, SelectOp op, SceneView scene = null)
        {
            if (status == MeshUV.BoundaryLoopStatus.Found)
            {
                ApplyFaceSelection(faces, op);
                return;
            }
            string hint = status == MeshUV.BoundaryLoopStatus.Missing
                ? Loc.T("Click a face next to a closed UV boundary to select its loop.")
                : scene != null ? Loc.T("Click closer to one boundary edge to select its loop.")
                : Loc.T("More than one UV boundary is under the pointer. Zoom in or pick on the model.");
            ReportTopologyFeedback(hint, scene);
        }

        void ApplyFaceLoop(MeshUV.FaceLoopStatus status, IReadOnlyList<int> faces, SelectOp op, SceneView scene = null)
        {
            if (status == MeshUV.FaceLoopStatus.Found || status == MeshUV.FaceLoopStatus.Stopped)
                ApplyFaceSelection(faces, op);

            string hint;
            switch (status)
            {
                case MeshUV.FaceLoopStatus.Found: return;
                case MeshUV.FaceLoopStatus.Stopped: hint = Loc.T("The face loop stopped where the next face could not be identified."); break;
                case MeshUV.FaceLoopStatus.NoPair: hint = Loc.T("No clear face row here. Use Face to select individual triangles."); break;
                case MeshUV.FaceLoopStatus.Overlap: hint = Loc.T("UV faces overlap here. Pick the face loop on the model."); break;
                case MeshUV.FaceLoopStatus.TooLarge: hint = Loc.T("This part or face loop is too large to select as a loop. Use Face to select triangles."); break;
                default: hint = Loc.T("Click closer to a connecting edge, away from its corners."); break;
            }
            ReportTopologyFeedback(hint, scene);
        }
    }
}
