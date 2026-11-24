using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        enum ImgGrab { None, Move, Rotate, Corner }

        ImageLayer _imgDrag, _imgOriginal;
        ImgGrab _imgGrab = ImgGrab.None;
        int _imgCorner, _imgDragControl;
        bool _imgPreviewActive, _imgOriginalView;
        Vector2 _imgStartPos, _imgStartSize, _imgGrabUV;
        float _imgStartRot;

        static readonly Vector2[] ImgCorners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };

        ImageLayer SelectedImage
        {
            get
            {
                var l = ActiveLayer as ImageLayer;
                return l != null && l.enabled && !l.tiled && l.image != null && !MaskEditing && _comp != null ? l : null;
            }
        }

        static void ImgSinCos(ImageLayer l, out float cs, out float sn)
        {
            float a = l.rotation * Mathf.Deg2Rad;
            cs = Mathf.Cos(a); sn = Mathf.Sin(a);
        }

        static Vector2 ImgLocalToUV(ImageLayer l, Vector2 d)
        {
            ImgSinCos(l, out float cs, out float sn);
            return l.position + new Vector2(d.x * cs + d.y * sn, -d.x * sn + d.y * cs);
        }

        static Vector2 ImgUVToLocal(ImageLayer l, Vector2 uv)
        {
            ImgSinCos(l, out float cs, out float sn);
            var d = uv - l.position;
            return new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
        }

        Vector2 ImgCornerCanvas(ImageLayer l, int i)
        {
            var c = ImgCorners[i];
            return UVToCanvas(ImgLocalToUV(l, new Vector2(c.x * Mathf.Abs(l.size.x) * 0.5f, c.y * Mathf.Abs(l.size.y) * 0.5f)));
        }

        Vector2 ImgRotateHandleCanvas(ImageLayer l)
        {
            var top = UVToCanvas(ImgLocalToUV(l, new Vector2(0, Mathf.Abs(l.size.y) * 0.5f)));
            var centre = UVToCanvas(l.position);
            var dir = top - centre;
            dir = dir.sqrMagnitude < 1e-4f ? Vector2.down : dir.normalized;
            return top + dir * 24f;
        }

        ImgGrab ImageHitTest(ImageLayer l, Vector2 local, bool bodyToo, out int corner)
        {
            corner = -1;
            if (Vector2.Distance(local, ImgRotateHandleCanvas(l)) <= 8f) return ImgGrab.Rotate;
            for (int i = 0; i < 4; i++)
                if (Vector2.Distance(local, ImgCornerCanvas(l, i)) <= 8f) { corner = i; return ImgGrab.Corner; }
            if (Vector2.Distance(local, UVToCanvas(l.position)) <= 8f) return ImgGrab.Move;
            if (bodyToo)
            {
                var d = ImgUVToLocal(l, CanvasToUV(local));
                if (Mathf.Abs(d.x) <= Mathf.Abs(l.size.x) * 0.5f && Mathf.Abs(d.y) <= Mathf.Abs(l.size.y) * 0.5f) return ImgGrab.Move;
            }
            return ImgGrab.None;
        }

        bool BeginImageDrag(Vector2 local, Event e)
        {
            var l = SelectedImage;
            if (l == null) return false;
            bool body = _tool != Tool.Paint && !e.shift && !e.control && !e.command;
            var grab = ImageHitTest(l, local, body, out int corner);
            if (grab == ImgGrab.None) return false;
            EndStroke();
            CancelSceneGradient();
            _imgOriginalView = _showOriginal;
            _imgOriginal = l;
            _imgDrag = l.CopyPlacement();
            _imgGrab = grab;
            _imgCorner = corner;
            _imgStartPos = l.position;
            _imgStartSize = l.size;
            _imgStartRot = l.rotation;
            _imgGrabUV = CanvasToUV(local);
            return true;
        }

        void UpdateImageDrag(Vector2 local, bool shift)
        {
            var l = _imgDrag;
            if (l == null || _comp == null) return;
            var uv = CanvasToUV(local);
            var travel = uv - _imgGrabUV;
            switch (_imgGrab)
            {
                case ImgGrab.Move:
                    l.position = _imgStartPos + travel;
                    break;
                case ImgGrab.Rotate:
                {
                    var v = uv - _imgStartPos;
                    if (v.sqrMagnitude < 1e-10f) break;
                    var start = _imgGrabUV - _imgStartPos;
                    float from = Mathf.Atan2(start.x, start.y) * Mathf.Rad2Deg;
                    float to = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
                    float turn = Mathf.DeltaAngle(from, to);
                    if (Mathf.Abs(turn) < 1e-4f) { l.rotation = _imgStartRot; break; }
                    float angle = _imgStartRot + turn;
                    if (shift) angle = Mathf.Round(angle / 15f) * 15f;
                    l.rotation = Mathf.Repeat(angle + 180f, 360f) - 180f;
                    break;
                }
                case ImgGrab.Corner:
                {
                    if (travel == Vector2.zero) { l.position = _imgStartPos; l.size = _imgStartSize; break; }
                    const float MinSize = 0.005f;
                    var c = ImgCorners[_imgCorner];
                    float w0 = Mathf.Max(1e-4f, Mathf.Abs(_imgStartSize.x)), h0 = Mathf.Max(1e-4f, Mathf.Abs(_imgStartSize.y));
                    var startLayer = new ImageLayer { position = _imgStartPos, rotation = _imgStartRot };
                    var opp = new Vector2(-c.x * w0 * 0.5f, -c.y * h0 * 0.5f);
                    var delta = ImgUVToLocal(startLayer, uv) - ImgUVToLocal(startLayer, _imgGrabUV);
                    float W = Mathf.Max(MinSize, w0 + delta.x * c.x), H = Mathf.Max(MinSize, h0 + delta.y * c.y);
                    if (shift)
                    {
                        var pixelSize = new Vector2(_comp.Width, _comp.Height);
                        var diagonal = Vector2.Scale(ImgLocalToUV(startLayer, -2f * opp) - _imgStartPos, pixelSize);
                        var drag = Vector2.Scale(travel, pixelSize);
                        float minScale = Mathf.Max(MinSize / w0, MinSize / h0);
                        float f = Mathf.Max(minScale, 1f + Vector2.Dot(drag, diagonal) / diagonal.sqrMagnitude);
                        W = w0 * f; H = h0 * f;
                    }
                    var centre = opp + new Vector2(c.x * W * 0.5f, c.y * H * 0.5f);
                    l.position = ImgLocalToUV(startLayer, centre);
                    l.size = new Vector2(_imgStartSize.x < 0 ? -W : W, _imgStartSize.y < 0 ? -H : H);
                    break;
                }
            }
            if (!_imgPreviewActive)
            {
                if (!ImagePlacementChanged(l)) return;
                _imgPreviewActive = true;
                _showOriginal = false;
            }
            ComposeNow();
            Repaint();
        }

        bool ImagePlacementChanged(ImageLayer layer) =>
            (layer.position - _imgStartPos).sqrMagnitude >= 1e-12f ||
            (layer.size - _imgStartSize).sqrMagnitude >= 1e-12f ||
            Mathf.Abs(Mathf.DeltaAngle(_imgStartRot, layer.rotation)) >= 1e-4f;

        void EndImageDrag()
        {
            var draft = _imgDrag;
            if (draft == null) return;
            var original = _imgOriginal;
            var grab = _imgGrab;
            bool commit = !_needsRebuild && original == SelectedImage && ImagePlacementChanged(draft);
            CancelImageDrag();
            if (!commit) return;

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            string undoName = grab == ImgGrab.Move ? "Move image" : grab == ImgGrab.Rotate ? "Rotate image" : "Scale image";
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObject(_recipe, undoName);
            if (grab == ImgGrab.Rotate) original.rotation = draft.rotation;
            else original.position = draft.position;
            if (grab == ImgGrab.Corner) original.size = draft.size;
            MarkEdited();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            ComposeNow();
        }

        void CancelImageDrag()
        {
            if (_imgDrag == null) return;
            _imgDrag = _imgOriginal = null;
            _placementPreview.Clear();
            if (_imgPreviewActive)
            {
                _showOriginal = _imgOriginalView;
                _dirty = true;
                SceneView.RepaintAll();
            }
            _imgPreviewActive = false;
            Repaint();
        }

        void ReleaseImageDragControl()
        {
            if (_imgDrag != null || _imgDragControl == 0) return;
            if (GUIUtility.hotControl == _imgDragControl) GUIUtility.hotControl = 0;
            _imgDragControl = 0;
        }

        void DrawImageHandlesOverlay()
        {
            var l = _imgDrag ?? SelectedImage;
            if (l == null || Event.current.type != EventType.Repaint) return;
            var pts = new Vector3[5];
            for (int i = 0; i < 4; i++) pts[i] = ImgCornerCanvas(l, i);
            pts[4] = pts[0];
            Handles.color = new Color(0, 0, 0, 0.8f);
            Handles.DrawAAPolyLine(4f, pts);
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f, pts);

            var top = (pts[2] + pts[3]) * 0.5f;
            var rot = ImgRotateHandleCanvas(l);
            Handles.color = new Color(0, 0, 0, 0.8f);
            Handles.DrawAAPolyLine(4f, top, (Vector3)rot);
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(2f, top, (Vector3)rot);
            Handles.color = new Color(0, 0, 0, 0.85f);
            Handles.DrawSolidDisc(rot, Vector3.forward, 7f);
            Handles.color = StudioGUI.Accent;
            Handles.DrawSolidDisc(rot, Vector3.forward, 5f);

            for (int i = 0; i < 4; i++)
            {
                EditorGUI.DrawRect(new Rect(pts[i].x - 5, pts[i].y - 5, 10, 10), new Color(0, 0, 0, 0.85f));
                EditorGUI.DrawRect(new Rect(pts[i].x - 4, pts[i].y - 4, 8, 8), Color.white);
            }
            var c = UVToCanvas(l.position);
            Handles.color = new Color(0, 0, 0, 0.85f);
            Handles.DrawSolidDisc(c, Vector3.forward, 6f);
            Handles.color = Color.white;
            Handles.DrawSolidDisc(c, Vector3.forward, 4.5f);
            Handles.color = Color.white;

            var mp = Event.current.mousePosition;
            if (_imgDrag == null)
            {
                switch (ImageHitTest(l, mp, _tool != Tool.Paint && !Event.current.shift && !Event.current.control, out _))
                {
                    case ImgGrab.Move: EditorGUIUtility.AddCursorRect(new Rect(mp.x - 10, mp.y - 10, 20, 20), MouseCursor.MoveArrow); break;
                    case ImgGrab.Rotate: EditorGUIUtility.AddCursorRect(new Rect(mp.x - 10, mp.y - 10, 20, 20), MouseCursor.RotateArrow); break;
                    case ImgGrab.Corner: EditorGUIUtility.AddCursorRect(new Rect(mp.x - 10, mp.y - 10, 20, 20), MouseCursor.ScaleArrow); break;
                }
            }
        }

        string ImageHandlesHint() =>
            _tool == Tool.Paint ? Loc.T("Image: drag a corner to scale (Shift keeps its shape)  ·  circle rotates (Shift snaps)  ·  centre dot moves")
                                : Loc.T("Image: drag a corner to scale (Shift keeps its shape)  ·  circle rotates (Shift snaps)  ·  centre dot or inside moves");
    }
}
