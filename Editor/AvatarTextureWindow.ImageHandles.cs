using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Handles for the selected Image layer, drawn on the texture view: drag inside the picture to move it (Select tool),
    /// the centre dot to move it with any tool, the corner squares to scale (Shift keeps the shape) and the circle above
    /// the picture to rotate (Shift snaps to 15 degrees). One undo step per drag.
    /// </summary>
    partial class AvatarTextureWindow
    {
        enum ImgGrab { None, Move, Rotate, Corner }

        ImageLayer _imgDrag;
        ImgGrab _imgGrab = ImgGrab.None;
        int _imgCorner;
        Vector2 _imgStartPos, _imgStartSize, _imgGrabUV;
        float _imgStartRot;

        static readonly Vector2[] ImgCorners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };

        /// <summary>The Image layer whose handles are shown, or null (hidden, tiled, empty, or while painting a mask).</summary>
        ImageLayer SelectedImage
        {
            get
            {
                var l = ActiveLayer as ImageLayer;
                return l != null && l.enabled && !l.tiled && l.image != null && !MaskEditing && _comp != null ? l : null;
            }
        }

        // The shader rotates the texture position around the picture's centre by +rotation, then divides by size.
        static void ImgSinCos(ImageLayer l, out float cs, out float sn)
        {
            float a = l.rotation * Mathf.Deg2Rad;
            cs = Mathf.Cos(a); sn = Mathf.Sin(a);
        }

        /// <summary>A point in the picture's own frame (centre = 0, in texture UV units) to texture UV.</summary>
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

        /// <summary>Mouse down on the texture view: grab a handle of the selected Image layer. False when nothing was hit.</summary>
        bool BeginImageDrag(Vector2 local, Event e)
        {
            var l = SelectedImage;
            if (l == null) return false;
            // Inside the picture moves it only with the Select tool, so Paint can still paint over it; modifiers keep their meaning.
            bool body = _tool != Tool.Paint && !e.shift && !e.control && !e.command;
            var grab = ImageHitTest(l, local, body, out int corner);
            if (grab == ImgGrab.None) return false;
            EndStroke();
            ShowEditsIfBeforeOn();
            Undo.IncrementCurrentGroup(); // the drag is its own undo step
            _imgDrag = l;
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
            switch (_imgGrab)
            {
                case ImgGrab.Move:
                    l.position = _imgStartPos + (uv - _imgGrabUV);
                    break;
                case ImgGrab.Rotate:
                {
                    var v = uv - l.position;
                    if (v.sqrMagnitude < 1e-10f) break;
                    float deg = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg; // the picture's "up" points at (sin, cos)
                    if (shift) deg = Mathf.Round(deg / 15f) * 15f;
                    l.rotation = Mathf.Clamp(deg, -180f, 180f);
                    break;
                }
                case ImgGrab.Corner:
                {
                    // The corner opposite the one you drag stays where it is; work in the picture's own (unrotated) frame.
                    var c = ImgCorners[_imgCorner];
                    float w0 = Mathf.Max(1e-4f, Mathf.Abs(_imgStartSize.x)), h0 = Mathf.Max(1e-4f, Mathf.Abs(_imgStartSize.y));
                    var startLayer = new ImageLayer { position = _imgStartPos, rotation = _imgStartRot };
                    var opp = new Vector2(-c.x * w0 * 0.5f, -c.y * h0 * 0.5f);
                    var d = ImgUVToLocal(startLayer, uv);
                    float W = Mathf.Max(0.005f, (d.x - opp.x) * c.x), H = Mathf.Max(0.005f, (d.y - opp.y) * c.y);
                    if (shift)
                    {
                        float f = Mathf.Max(W / w0, H / h0);
                        W = w0 * f; H = h0 * f;
                    }
                    var centre = opp + new Vector2(c.x * W * 0.5f, c.y * H * 0.5f);
                    l.position = ImgLocalToUV(startLayer, centre);
                    l.size = new Vector2(_imgStartSize.x < 0 ? -W : W, _imgStartSize.y < 0 ? -H : H);
                    break;
                }
            }
            ComposeNow();
            Repaint();
        }

        void EndImageDrag()
        {
            var l = _imgDrag;
            if (l == null) return;
            _imgDrag = null;
            Vector2 fp = l.position, fs = l.size;
            float fr = l.rotation;
            if ((fp - _imgStartPos).sqrMagnitude < 1e-12f && (fs - _imgStartSize).sqrMagnitude < 1e-12f && Mathf.Approximately(fr, _imgStartRot))
            {
                Repaint();
                return;
            }
            // The layer was edited live while dragging: record the whole drag as one undo step.
            l.position = _imgStartPos; l.size = _imgStartSize; l.rotation = _imgStartRot;
            Undo.RecordObject(_recipe, _imgGrab == ImgGrab.Move ? "Move image" : _imgGrab == ImgGrab.Rotate ? "Rotate image" : "Scale image");
            l.position = fp; l.size = fs; l.rotation = fr;
            MarkEdited();
            Undo.IncrementCurrentGroup();
            ComposeNow();
        }

        /// <summary>The selected Image layer's outline, corner squares, rotate circle and centre dot (canvas-local coordinates).</summary>
        void DrawImageHandlesOverlay()
        {
            var l = SelectedImage;
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
