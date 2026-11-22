using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Which pieces can be seen inside a shape drawn in the Scene view (Box and Lasso). A small software raster draws
    /// the edited part's front-facing triangles with their piece id and the covering parts of the avatar with a blocker id;
    /// the nearest triangle wins each pixel, so a piece behind another part, or behind the model itself, is left out.
    /// It runs once when the drag ends, never per mouse move.
    /// </summary>
    internal static class SceneMarquee
    {
        /// <summary>A part of the avatar that is not being edited and can cover the edited surface.</summary>
        internal struct Blocker
        {
            public Vector3[] verts;   // posed world positions, indexed like tris (NaN = not posed)
            public int[] tris;
            public bool doubleSided;  // the material draws both sides, so back faces cover too
            public bool mirrored;     // negative scale: the world-space winding is flipped
        }

        const int MaxSide = 512;      // the long side of the buffer, in pixels
        const int BlockerId = -2;

        /// <param name="guiBounds">The shape's bounds in Scene view GUI points.</param>
        /// <param name="inside">Whether a Scene view GUI point is inside the shape.</param>
        /// <param name="hiddenBehind">How many pieces had a spot in the shape that another part covered, and are not in the result.</param>
        internal static List<int> PiecesInShape(MeshUV uv, Camera cam, Rect guiBounds, Func<Vector2, bool> inside,
                                                IList<Blocker> blockers, Func<Renderer, bool> partDoubleSided, out int hiddenBehind)
        {
            hiddenBehind = 0;
            var result = new List<int>();
            if (uv == null || cam == null || guiBounds.width < 1f || guiBounds.height < 1f) return result;

            float scale = Mathf.Min(4f, MaxSide / Mathf.Max(guiBounds.width, guiBounds.height));
            int w = Mathf.Max(1, Mathf.CeilToInt(guiBounds.width * scale));
            int h = Mathf.Max(1, Mathf.CeilToInt(guiBounds.height * scale));
            var depth = new float[w * h];
            var ids = new int[w * h];
            var inShape = new bool[w * h];
            for (int i = 0; i < depth.Length; i++) { depth[i] = float.MaxValue; ids[i] = -1; }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    inShape[y * w + x] = inside(new Vector2(guiBounds.x + (x + 0.5f) / scale, guiBounds.y + (y + 0.5f) / scale));

            var view = new View(cam, guiBounds, scale);
            // Same slack as a click (FrontBlocker): the edited surface is pulled this far toward the camera, so an overlay or
            // a duplicate slot at the same depth does not hide it.
            float slack = Mathf.Max(uv.PosedSize * 0.002f, 1e-4f);
            var candidate = new bool[uv.islandCount];

            if (blockers != null)
                foreach (var b in blockers)
                {
                    // No bounds culling: a renderer's bounds can be stale for a skinned mesh, and the posed vertices are cached.
                    DrawMesh(view, depth, ids, null, w, h, b.verts, b.tris, null, 0, b.tris.Length / 3, BlockerId, b.doubleSided, b.mirrored, inShape, 0f);
                }

            foreach (var part in uv.parts)
            {
                if (part.renderer == null || !part.renderer.enabled || !part.renderer.gameObject.activeInHierarchy) continue;
                DrawMesh(view, depth, ids, candidate, w, h, uv.PosedCombinedCached(), uv.tris, uv.triIsland,
                         part.triStart, part.triCount, 0, partDoubleSided != null && partDoubleSided(part.renderer),
                         part.renderer.localToWorldMatrix.determinant < 0f, inShape, slack);
            }

            var seen = new bool[uv.islandCount];
            for (int i = 0; i < ids.Length; i++)
                if (inShape[i] && ids[i] >= 0 && ids[i] < seen.Length) seen[ids[i]] = true;
            for (int i = 0; i < seen.Length; i++)
            {
                if (seen[i]) result.Add(i);
                else if (candidate[i]) hiddenBehind++;
            }
            return result;
        }

        /// <summary>The camera, and how a world point lands in the buffer.</summary>
        struct View
        {
            readonly Matrix4x4 _viewProjection;
            readonly Vector3 _eye, _forward;
            readonly bool _ortho;
            readonly float _pixelWidth, _pixelHeight, _pointsPerPixel;
            readonly Rect _bounds;
            readonly float _scale;

            internal View(Camera cam, Rect bounds, float scale)
            {
                _viewProjection = cam.projectionMatrix * cam.worldToCameraMatrix;
                _eye = cam.transform.position;
                _forward = cam.transform.forward;
                _ortho = cam.orthographic;
                _pixelWidth = cam.pixelWidth;
                _pixelHeight = cam.pixelHeight;
                _pointsPerPixel = 1f / Mathf.Max(0.01f, EditorGUIUtility.pixelsPerPoint);
                _bounds = bounds;
                _scale = scale;
            }

            internal Vector4 Clip(Vector3 p) => _viewProjection * new Vector4(p.x, p.y, p.z, 1f);

            // Buffer x, buffer y and depth (smaller = nearer) of a clip-space point.
            internal Vector3 ToBuffer(Vector4 c)
            {
                float nx = c.x / c.w, ny = c.y / c.w, nz = c.z / c.w;
                float guiX = (nx * 0.5f + 0.5f) * _pixelWidth * _pointsPerPixel;
                float guiY = (1f - (ny * 0.5f + 0.5f)) * _pixelHeight * _pointsPerPixel; // GUI y runs down
                return new Vector3((guiX - _bounds.x) * _scale, (guiY - _bounds.y) * _scale, nz);
            }

            // The point moved toward the camera along its own view ray, so it keeps its place on screen.
            internal Vector3 Pull(Vector3 p, float distance)
            {
                var toward = _ortho ? -_forward : (_eye - p).normalized;
                return p + toward * distance;
            }

            internal bool FacesCamera(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                var n = Vector3.Cross(v1 - v0, v2 - v0); // same side test as MeshUV.Raycast: the normal points at the viewer
                return _ortho ? Vector3.Dot(n, -_forward) > 0f : Vector3.Dot(n, _eye - v0) > 0f;
            }
        }

        static void DrawMesh(View view, float[] depth, int[] ids, bool[] candidate, int w, int h, Vector3[] verts, int[] tris,
                             int[] triPiece, int firstTri, int triCount, int fixedId, bool doubleSided, bool mirrored, bool[] inShape, float pull)
        {
            var poly = new Vector4[4];
            for (int t = firstTri; t < firstTri + triCount; t++)
            {
                Vector3 v0 = verts[tris[t * 3]], v1 = verts[tris[t * 3 + 1]], v2 = verts[tris[t * 3 + 2]];
                if (float.IsNaN(v0.x) || float.IsNaN(v1.x) || float.IsNaN(v2.x)) continue;
                if (!doubleSided && view.FacesCamera(v0, v1, v2) == mirrored) continue;

                // Clip to the near plane (z >= -w), so a triangle that reaches behind the camera still draws.
                if (pull > 0f) { v0 = view.Pull(v0, pull); v1 = view.Pull(v1, pull); v2 = view.Pull(v2, pull); }
                poly[0] = view.Clip(v0); poly[1] = view.Clip(v1); poly[2] = view.Clip(v2);
                int n = ClipNear(poly);
                if (n < 3) continue;
                int piece = triPiece != null ? triPiece[t] : fixedId;
                for (int k = 1; k + 1 < n; k++)
                    Fill(depth, ids, candidate, w, h, view.ToBuffer(poly[0]), view.ToBuffer(poly[k]), view.ToBuffer(poly[k + 1]), piece, inShape);
            }
        }

        // Sutherland-Hodgman against the near plane, in place. Returns the vertex count (0, 3 or 4).
        static int ClipNear(Vector4[] p)
        {
            float d0 = p[0].z + p[0].w, d1 = p[1].z + p[1].w, d2 = p[2].z + p[2].w;
            if (d0 > 0f && d1 > 0f && d2 > 0f) return 3;
            if (d0 <= 0f && d1 <= 0f && d2 <= 0f) return 0;
            var src = new[] { p[0], p[1], p[2] };
            var dist = new[] { d0, d1, d2 };
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                if (dist[i] > 0f) p[count++] = src[i];
                if ((dist[i] > 0f) != (dist[j] > 0f))
                {
                    float f = dist[i] / (dist[i] - dist[j]);
                    p[count++] = Vector4.Lerp(src[i], src[j], f);
                }
            }
            return count;
        }

        // One triangle into the buffers. A triangle that loses to a blocker marks its piece as covered, for the toast.
        static void Fill(float[] depth, int[] ids, bool[] candidate, int w, int h, Vector3 a, Vector3 b, Vector3 c, int id, bool[] inShape)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (x0 > x1 || y0 > y1) return;
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-9f) return;
            float inv = 1f / area;
            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) * inv;
                    float w1 = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    float z = w0 * a.z + w1 * b.z + w2 * c.z;
                    int i = y * w + x;
                    if (z < depth[i])
                    {
                        // A piece that was in front a moment ago and is now covered counts as hidden too (for the toast).
                        int prev = ids[i];
                        if (candidate != null && inShape[i] && prev >= 0 && prev != id && prev < candidate.Length) candidate[prev] = true;
                        depth[i] = z; ids[i] = id;
                    }
                    else if (candidate != null && ids[i] != -1 && ids[i] != id && inShape[i] && id >= 0 && id < candidate.Length) candidate[id] = true;
                }
            }
        }
    }
}
