using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    internal sealed class SceneMarquee
    {
        internal struct Blocker
        {
            public Vector3[] verts;
            public int[] tris;
            public Vector2[] uv;
            public CullMode cull;
            public PoiyomiTileDiscard.TriangleFilter tileFilter;
            public SceneCutout cutout;
        }

        const int MaxSide = 512;
        const int BlockerId = -2;
        float[] _depth;
        int[] _ids;
        bool[] _inShape, _candidate, _seen;

        internal List<int> PiecesInShape(MeshUV uv, Camera cam, Rect guiBounds, Func<Vector2, bool> inside,
                                                IList<Blocker> blockers, Predicate<Renderer> isDrawn,
                                                Func<Renderer, bool> partDoubleSided,
                                                Func<MeshUV.Part, PoiyomiTileDiscard.TriangleFilter> partTileFilter,
                                                Predicate<MeshUV.Part> canSelect, out int hiddenBehind)
        {
            hiddenBehind = 0;
            var result = new List<int>();
            if (uv == null || cam == null || guiBounds.width < 1f || guiBounds.height < 1f) return result;

            float scale = Mathf.Min(4f, MaxSide / Mathf.Max(guiBounds.width, guiBounds.height));
            int w = Mathf.Max(1, Mathf.CeilToInt(guiBounds.width * scale));
            int h = Mathf.Max(1, Mathf.CeilToInt(guiBounds.height * scale));
            int size = w * h;
            if (_depth == null || _depth.Length < size)
            {
                int capacity = Mathf.NextPowerOfTwo(size);
                _depth = new float[capacity];
                _ids = new int[capacity];
                _inShape = new bool[capacity];
            }
            var depth = _depth;
            var ids = _ids;
            var inShape = _inShape;
            Array.Fill(depth, float.MaxValue, 0, size);
            Array.Fill(ids, -1, 0, size);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    inShape[y * w + x] = inside(new Vector2(guiBounds.x + (x + 0.5f) / scale, guiBounds.y + (y + 0.5f) / scale));

            var view = new View(cam, guiBounds, scale);
            float slack = Mathf.Max(uv.PosedSize * 0.002f, 1e-4f);
            if (_candidate == null || _candidate.Length != uv.islandCount)
            {
                _candidate = new bool[uv.islandCount];
                _seen = new bool[uv.islandCount];
            }
            else
            {
                Array.Clear(_candidate, 0, _candidate.Length);
                Array.Clear(_seen, 0, _seen.Length);
            }
            var candidate = _candidate;

            if (blockers != null)
                foreach (var b in blockers)
                {
                    DrawMesh(view, depth, ids, null, w, h, b.verts, b.tris, null, 0, b.tris.Length / 3, BlockerId,
                             b.cull, inShape, 0f, b.tileFilter, b.cutout, b.uv);
                }

            foreach (var part in uv.parts)
            {
                if (part.renderer == null || !isDrawn(part.renderer)) continue;
                var tileFilter = partTileFilter(part);
                if (tileFilter.AllDiscarded) continue;
                CullMode materialCull = partDoubleSided != null && partDoubleSided(part.renderer) ? CullMode.Off : CullMode.Back;
                var cull = WindingCull(materialCull, part.renderer.localToWorldMatrix.determinant < 0f);
                bool selectable = canSelect?.Invoke(part) != false;
                DrawMesh(view, depth, ids, selectable ? candidate : null, w, h, uv.PosedCombinedCached(), uv.tris,
                         selectable ? uv.triIsland : null, part.triStart, part.triCount, selectable ? 0 : BlockerId,
                         cull, inShape, selectable ? slack : 0f, tileFilter);
            }

            var seen = _seen;
            for (int i = 0; i < size; i++)
                if (inShape[i] && ids[i] >= 0 && ids[i] < seen.Length) seen[ids[i]] = true;
            for (int i = 0; i < seen.Length; i++)
            {
                if (seen[i]) result.Add(i);
                else if (candidate[i]) hiddenBehind++;
            }
            return result;
        }

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

            internal Vector3 ToBuffer(Vector4 c)
            {
                float nx = c.x / c.w, ny = c.y / c.w, nz = c.z / c.w;
                float guiX = (nx * 0.5f + 0.5f) * _pixelWidth * _pointsPerPixel;
                float guiY = (1f - (ny * 0.5f + 0.5f)) * _pixelHeight * _pointsPerPixel;
                return new Vector3((guiX - _bounds.x) * _scale, (guiY - _bounds.y) * _scale, nz);
            }

            internal Vector3 Pull(Vector3 p, float distance)
            {
                var toward = _ortho ? -_forward : (_eye - p).normalized;
                return p + toward * distance;
            }

            internal bool FacesCamera(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                var n = Vector3.Cross(v1 - v0, v2 - v0);
                return _ortho ? Vector3.Dot(n, -_forward) > 0f : Vector3.Dot(n, _eye - v0) > 0f;
            }
        }

        internal static CullMode WindingCull(CullMode mode, bool mirrored) => !mirrored || mode == CullMode.Off ? mode :
            mode == CullMode.Front ? CullMode.Back : CullMode.Front;

        static void DrawMesh(View view, float[] depth, int[] ids, bool[] candidate, int w, int h, Vector3[] verts, int[] tris,
                             int[] triPiece, int firstTri, int triCount, int fixedId, CullMode cull, bool[] inShape,
                             float pull, PoiyomiTileDiscard.TriangleFilter tileFilter = default, SceneCutout cutout = default, Vector2[] uv = null)
        {
            var poly = new Vector4[4];
            var tex = cutout.Active ? new Vector2[4] : null;
            for (int t = firstTri; t < firstTri + triCount; t++)
            {
                if (tileFilter.Discards(t)) continue;
                Vector3 v0 = verts[tris[t * 3]], v1 = verts[tris[t * 3 + 1]], v2 = verts[tris[t * 3 + 2]];
                if (float.IsNaN(v0.x) || float.IsNaN(v1.x) || float.IsNaN(v2.x)) continue;
                if (cull != CullMode.Off && view.FacesCamera(v0, v1, v2) == (cull == CullMode.Front)) continue;

                if (pull > 0f) { v0 = view.Pull(v0, pull); v1 = view.Pull(v1, pull); v2 = view.Pull(v2, pull); }
                poly[0] = view.Clip(v0); poly[1] = view.Clip(v1); poly[2] = view.Clip(v2);
                if (tex != null)
                {
                    tex[0] = uv[tris[t * 3]];
                    tex[1] = uv[tris[t * 3 + 1]];
                    tex[2] = uv[tris[t * 3 + 2]];
                }
                int n = ClipNear(poly, tex);
                if (n < 3) continue;
                int piece = triPiece != null ? triPiece[t] : fixedId;
                for (int k = 1; k + 1 < n; k++)
                {
                    var sample = tex != null ? new CutoutTriangle(cutout, poly[0], poly[k], poly[k + 1], tex[0], tex[k], tex[k + 1]) : default;
                    Fill(depth, ids, candidate, w, h, view.ToBuffer(poly[0]), view.ToBuffer(poly[k]), view.ToBuffer(poly[k + 1]), piece, inShape, sample);
                }
            }
        }

        static int ClipNear(Vector4[] p, Vector2[] uv)
        {
            float d0 = p[0].z + p[0].w, d1 = p[1].z + p[1].w, d2 = p[2].z + p[2].w;
            if (d0 > 0f && d1 > 0f && d2 > 0f) return 3;
            if (d0 <= 0f && d1 <= 0f && d2 <= 0f) return 0;
            var src = new[] { p[0], p[1], p[2] };
            var dist = new[] { d0, d1, d2 };
            var tex = uv != null ? new[] { uv[0], uv[1], uv[2] } : null;
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                if (dist[i] > 0f)
                {
                    if (uv != null) uv[count] = tex[i];
                    p[count++] = src[i];
                }
                if ((dist[i] > 0f) != (dist[j] > 0f))
                {
                    float f = dist[i] / (dist[i] - dist[j]);
                    if (uv != null) uv[count] = Vector2.Lerp(tex[i], tex[j], f);
                    p[count++] = Vector4.Lerp(src[i], src[j], f);
                }
            }
            return count;
        }

        readonly struct CutoutTriangle
        {
            readonly SceneCutout _cutout;
            readonly Vector3 _inverseW;
            readonly Vector2 _a, _b, _c;

            internal CutoutTriangle(SceneCutout cutout, Vector4 clipA, Vector4 clipB, Vector4 clipC, Vector2 a, Vector2 b, Vector2 c)
            {
                _cutout = cutout;
                _inverseW = new Vector3(1f / clipA.w, 1f / clipB.w, 1f / clipC.w);
                _a = a * _inverseW.x; _b = b * _inverseW.y; _c = c * _inverseW.z;
            }

            internal bool Discards(float a, float b, float c) => _cutout.Active &&
                _cutout.Discards((a * _a + b * _b + c * _c) / (a * _inverseW.x + b * _inverseW.y + c * _inverseW.z));
        }

        static void Fill(float[] depth, int[] ids, bool[] candidate, int w, int h, Vector3 a, Vector3 b, Vector3 c, int id, bool[] inShape,
                         in CutoutTriangle sample)
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
                    if (z < depth[i] && !sample.Discards(w0, w1, w2))
                    {
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
