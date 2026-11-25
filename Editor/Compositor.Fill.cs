using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    partial class Compositor
    {
        const byte FillColourEdge = 1, FillOutside = 2, FillClosedGap = 3, FillGapEdge = 4;
        byte[] _fillMirrorBytes;

        int ScopedFillIndex(Vector2 uv)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * Width), 0, Width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * Height), 0, Height - 1);
            return y * Width + x;
        }

        internal int ScopedFillSeedPiece(MeshUV mesh, Vector2 uv) => IslandIdsExact(mesh)[ScopedFillIndex(uv)] - 1;

        internal Vector2 ScopedFillPixelCentre(Vector2 uv)
        {
            int index = ScopedFillIndex(uv);
            return new Vector2((index % Width + 0.5f) / Width, (index / Width + 0.5f) / Height);
        }

        void RenderScopedFillCoverage(PaintStroke stroke, MeshUV uv)
        {
            var region = stroke.fillRegion;
            var mirror = stroke.fillMirrorRegion;
            bool hasMirror = mirror != null && mirror.scope != PaintFillScope.Legacy && stroke.points.Count > 1;
            if (region.scope == PaintFillScope.WholeTexture && stroke.fillWholeIsland && !stroke.HasUvPaintLimit &&
                !region.UsesGeometry && (!hasMirror || !mirror.UsesGeometry))
            {
                bool hasSeed = (stroke.points.Count > 0 && IsScopedFillPointInTexture(stroke.points[0])) ||
                    (hasMirror && IsScopedFillPointInTexture(stroke.points[1]));
                RenderTexture.active = _strokeRT;
                GL.Clear(false, true, hasSeed ? Color.red : Color.black);
                return;
            }

            int size = Width * Height;
            if (_fillBytes == null || _fillBytes.Length != size) _fillBytes = new byte[size];
            bool needsQueue = !stroke.fillWholeIsland && (!stroke.fillAllMatches || stroke.fillAreaScaling > 0f);
            if (needsQueue && (_fillQueue == null || _fillQueue.Length != size)) _fillQueue = new int[size];
            RasterScopedFill(stroke, region, uv, 0, _fillBytes);

            if (hasMirror)
            {
                if (_fillMirrorBytes == null || _fillMirrorBytes.Length != size) _fillMirrorBytes = new byte[size];
                RasterScopedFill(stroke, mirror, uv, 1, _fillMirrorBytes);
                for (int i = 0; i < size; i++) _fillBytes[i] |= _fillMirrorBytes[i];
            }

            var tex = new Texture2D(Width, Height, TextureFormat.R8, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            try
            {
                tex.SetPixelData(_fillBytes, 0);
                tex.Apply(false);
                Graphics.Blit(tex, _strokeRT);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        static bool IsScopedFillPointInTexture(Vector4 point) =>
            point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;

        void RasterScopedFill(PaintStroke stroke, PaintFillRegion region, MeshUV uv, int pointIndex, byte[] result)
        {
            Array.Clear(result, 0, result.Length);
            if (stroke.points.Count <= pointIndex || region.UsesGeometry && (uv == null || region.layout != uv.FaceLayout)) return;
            var point = stroke.points[pointIndex];
            if (!IsScopedFillPointInTexture(point)) return;

            int grow = !stroke.fillWholeIsland && stroke.fillAreaScaling > 0
                ? Mathf.Max(1, Mathf.RoundToInt(stroke.fillAreaScaling * Width / Mathf.Max(1, SourceWidth))) : 0;
            var ids = region.limitPieces ? IslandIdsExact(uv) : null;
            var paddingIds = ids != null && grow > 0 ? IslandIdsCpu(uv) : ids;
            var pieceAllowed = ids != null ? new bool[uv.islandCount + 1] : null;
            if (pieceAllowed != null)
            {
                if (region.invertPieces) Array.Fill(pieceAllowed, true);
                foreach (int piece in region.pieces)
                    if (piece >= 0 && piece < uv.islandCount) pieceAllowed[piece + 1] = !region.invertPieces;
            }
            var faces = region.limitFaces ? uv.RasterFaceMask(Width, Height, new HashSet<int>(region.faces)) : null;
            bool useUvLimit = stroke.HasUvPaintLimit;
            bool Allows(int index, bool padding = false)
            {
                var pieceIds = padding ? paddingIds : ids;
                return (pieceIds == null || pieceIds[index] < pieceAllowed.Length && pieceAllowed[pieceIds[index]]) &&
                    (faces == null || (faces[index] != 0) != region.invertFaces) && (!useUvLimit ||
                    stroke.IncludesPaintUV(new Vector2((index % Width + 0.5f) / Width, (index / Width + 0.5f) / Height)));
            }
            int start = ScopedFillIndex(new Vector2(point.x, point.y));
            if (!Allows(start)) return;

            if (stroke.fillWholeIsland)
            {
                if (!region.UsesGeometry && !useUvLimit) Array.Fill(result, byte.MaxValue);
                else for (int i = 0; i < result.Length; i++) if (Allows(i)) result[i] = byte.MaxValue;
                return;
            }

            var colours = SourceCpu();
            var seedColour = colours[start];
            int tolerance = Mathf.RoundToInt(Mathf.Clamp01(stroke.fillTolerance) * 255f);
            bool Matches(int index) => Close(colours[index], seedColour, tolerance);
            if (stroke.fillAllMatches && grow == 0)
            {
                for (int i = 0; i < result.Length; i++) if (Allows(i) && Matches(i)) result[i] = byte.MaxValue;
                return;
            }

            int gap = stroke.fillAllMatches ? 0 : Mathf.Clamp(stroke.fillGap, 0, BrushSettings.MaxFillGap);
            int gapX = Mathf.FloorToInt(gap * (float)Width / Mathf.Max(1, SourceWidth));
            int gapY = Mathf.FloorToInt(gap * (float)Height / Mathf.Max(1, SourceHeight));
            bool closeGaps = gapX > 0 || gapY > 0;
            if (closeGaps)
            {
                for (int i = 0; i < result.Length; i++)
                    result[i] = !Allows(i) ? FillOutside : Matches(i) ? (byte)0 : FillColourEdge;
                CloseScopedFillGaps(result, gapX, gapY, start);
            }

            int head = 0, tail = 0;
            void Add(int index)
            {
                result[index] = byte.MaxValue;
                _fillQueue[tail++] = index;
            }
            void TryAdd(int x, int y, bool match)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height) return;
                int index = y * Width + x;
                if (closeGaps && match)
                {
                    if (result[index] == 0) Add(index);
                }
                else if (result[index] != byte.MaxValue && Allows(index, padding: !match) && (!match || Matches(index))) Add(index);
            }

            if (stroke.fillAllMatches)
            {
                for (int i = 0; i < result.Length; i++) if (Allows(i) && Matches(i)) Add(i);
            }
            else
            {
                Add(start);
                while (head < tail)
                {
                    int index = _fillQueue[head++], x = index % Width, y = index / Width;
                    TryAdd(x - 1, y, true);
                    TryAdd(x + 1, y, true);
                    TryAdd(x, y - 1, true);
                    TryAdd(x, y + 1, true);
                }
            }

            head = 0;
            for (int pass = 0; pass < grow && head < tail; pass++)
            {
                int end = tail;
                while (head < end)
                {
                    int index = _fillQueue[head++], x = index % Width, y = index / Width;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (dx != 0 || dy != 0) TryAdd(x + dx, y + dy, false);
                }
            }
            if (closeGaps) FinishClosedFillGaps(result, Width, Height);
        }

        void CloseScopedFillGaps(byte[] pixels, int gapX, int gapY, int seed)
        {
            if (gapX > 0)
                for (int y = 0; y < Height; y++) CloseFillGapLine(pixels, y * Width, 1, Width, gapX, seed);
            if (gapY > 0)
                for (int x = 0; x < Width; x++) CloseFillGapLine(pixels, x, Width, Height, gapY, seed);
            int diagonal = Mathf.Min(gapX, gapY);
            if (diagonal == 0 || Width < 2 || Height < 2) return;
            for (int x = 0; x < Width; x++)
            {
                CloseFillGapLine(pixels, x, Width + 1, Mathf.Min(Width - x, Height), diagonal, seed);
                CloseFillGapLine(pixels, x, Width - 1, Mathf.Min(x + 1, Height), diagonal, seed);
            }
            for (int y = 1; y < Height; y++)
            {
                int length = Mathf.Min(Width, Height - y);
                CloseFillGapLine(pixels, y * Width, Width + 1, length, diagonal, seed);
                CloseFillGapLine(pixels, (y + 1) * Width - 1, Width - 1, length, diagonal, seed);
            }
        }

        static void CloseFillGapLine(byte[] pixels, int origin, int step, int length, int maxGap, int seed)
        {
            int seedOffset = seed - origin;
            seedOffset = seedOffset >= 0 && seedOffset % step == 0 ? seedOffset / step : -1;
            int first = -1;
            bool followsEdge = false;
            for (int offset = 0, index = origin; offset < length; offset++, index += step)
            {
                byte state = pixels[index];
                if (state == 0 || state == FillClosedGap)
                {
                    if (first < 0) first = offset;
                    continue;
                }
                if (state == FillColourEdge && followsEdge && first >= 0 && offset - first <= maxGap &&
                    !(seedOffset >= first && seedOffset < offset))
                    for (int gap = origin + first * step; gap < index; gap += step) pixels[gap] = FillClosedGap;
                first = -1;
                followsEdge = state == FillColourEdge;
            }
        }

        static void FinishClosedFillGaps(byte[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (pixels[i] != FillClosedGap) continue;
                if (x > 0 && pixels[i - 1] == byte.MaxValue || x + 1 < width && pixels[i + 1] == byte.MaxValue ||
                    y > 0 && pixels[i - width] == byte.MaxValue || y + 1 < height && pixels[i + width] == byte.MaxValue)
                    pixels[i] = FillGapEdge;
            }
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = pixels[i] == FillGapEdge || pixels[i] == byte.MaxValue ? byte.MaxValue : (byte)0;
        }
    }
}
