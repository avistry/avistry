using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        const int MinimumIslandSpanWidth = 17;
        const float MaxIslandSpanCoordinate = 1e8f;
        const long MaxParallelIslandSeedPixels = 4096L * 4096;
        internal bool LimitBackgroundRaster { private get; set; }

        internal ushort[] RasterPaddedIslandIds(int w, int h, int padding, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var values = new ushort[w * h];
            int workers = IslandRasterWorkers(w, h, cancellation);
            int rowsPerStrip = Math.Min(256, Math.Max(64, h / 16));
            int stripWorkers = padding > 0 && padding <= 64 && workers > 1 && (long)w * h <= MaxParallelIslandSeedPixels
                ? Math.Min(workers, h / (3 * (rowsPerStrip + padding * 2))) : 0;
            if (LimitBackgroundRaster) workers = 1;
            if (stripWorkers > 1)
            {
                RasterIslandStrips(w, h, padding, values, rowsPerStrip, Math.Min(workers, stripWorkers), cancellation);
                return values;
            }
            var covered = padding > 0 ? new bool[w * h] : null;
            RasterIslandOwnership(w, h, values, covered, workers, cancellation);
            if (padding > 0)
            {
                bool parallelSeeds = workers > 1 && (long)w * h <= MaxParallelIslandSeedPixels;
                var seed = parallelSeeds ? SeedIslandDilationBands(covered, w, h, workers, cancellation)
                    : SeedIslandDilation(covered, w, h, cancellation);
                DilateValues(values, covered, w, h, padding, cancellation, seed);
            }
            return values;
        }

        void RasterIslandStrips(int w, int h, int padding, ushort[] padded, int rowsPerStrip, int workers,
                                CancellationToken cancellation)
        {
            int strips = (h + rowsPerStrip - 1) / rowsPerStrip;
            void PaintStrips(int worker)
            {
                int capacity = w * Math.Min(h, rowsPerStrip + padding * 2);
                var local = new ushort[capacity];
                var covered = new bool[capacity];
                for (int strip = worker; strip < strips; strip += workers)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int first = strip * rowsPerStrip, last = Math.Min(h, first + rowsPerStrip);
                    int sourceFirst = Math.Max(0, first - padding), sourceLast = Math.Min(h, last + padding);
                    int rows = sourceLast - sourceFirst;
                    if (strip != worker)
                    {
                        Array.Clear(local, 0, w * rows);
                        Array.Clear(covered, 0, w * rows);
                    }
                    RasterIslandRows(w, h, sourceFirst, sourceLast, local, covered, cancellation, sourceFirst);
                    int offset = (first - sourceFirst) * w, count = (last - first) * w;
                    var seeds = SeedIslandDilation(covered, w, rows, cancellation);
                    DilateValues(local, covered, w, rows, padding, cancellation, seeds);
                    Array.Copy(local, offset, padded, first * w, count);
                }
            }
            Parallel.For(0, workers, new ParallelOptions
            {
                MaxDegreeOfParallelism = workers, TaskScheduler = TaskScheduler.Default, CancellationToken = cancellation
            }, PaintStrips);
        }

        int IslandRasterWorkers(int width, int height, CancellationToken cancellation)
        {
            if ((long)width * height < 2048L * 2048 ||
                width < 128 || height < 128 || triIsland.Length < 4096) return 1;
            if (cancellation.CanBeCanceled)
                return (long)width * height <= MaxParallelIslandSeedPixels && Environment.ProcessorCount >= 4 ? 2 : 1;
            return Math.Min(4, Math.Max(1, Environment.ProcessorCount / 2));
        }

        void RasterIslandOwnership(int width, int height, ushort[] values, bool[] covered, int painters,
                                   CancellationToken cancellation)
        {
            if (painters == 1)
            {
                RasterIslandRows(width, height, 0, height, values, covered, cancellation);
                return;
            }

            float low = 1f, high = 0f;
            foreach (var point in uv)
            {
                if (!float.IsFinite(point.y)) continue;
                low = Mathf.Min(low, point.y);
                high = Mathf.Max(high, point.y);
            }
            int occupiedStart = Mathf.FloorToInt(Mathf.Clamp01(low) * height);
            int occupiedRows = Mathf.CeilToInt(Mathf.Clamp01(high) * height) - occupiedStart;
            int bands = Math.Min(occupiedRows, painters * 2);
            if (bands < 2)
            {
                RasterIslandRows(width, height, 0, height, values, covered, cancellation);
                return;
            }
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = painters,
                TaskScheduler = TaskScheduler.Default,
                CancellationToken = cancellation
            };
            Parallel.For(0, bands, options, band => RasterIslandRows(width, height,
                band == 0 ? 0 : occupiedStart + band * occupiedRows / bands,
                band + 1 == bands ? height : occupiedStart + (band + 1) * occupiedRows / bands,
                values, covered, cancellation));
        }

        void RasterIslandRows(int w, int h, int firstRow, int lastRow, ushort[] values, bool[] covered,
                              CancellationToken cancellation, int rowOffset = 0)
        {
            RasterIslandSpans(w, h, firstRow, lastRow, null, (y, from, to, id) =>
            {
                int row = (y - rowOffset) * w;
                if (covered == null) Array.Fill(values, id, row + from, to - from + 1);
                else
                {
                    for (int pixel = row + from; pixel <= row + to; pixel++)
                    {
                        values[pixel] = id;
                        covered[pixel] = true;
                    }
                }
            }, cancellation);
        }

        void RasterIslandSpans(int w, int h, int firstRow, int lastRow, HashSet<int> islands,
                               Action<int, int, int, ushort> write, CancellationToken cancellation)
        {
            for (int t = 0; t < triIsland.Length; t++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (islands != null && !islands.Contains(triIsland[t])) continue;
                Vector2 au = uv[tris[t * 3]], bu = uv[tris[t * 3 + 1]], cu = uv[tris[t * 3 + 2]];
                float ay = au.y * h, by = bu.y * h, cy = cu.y * h;
                if ((ay >= lastRow && by >= lastRow && cy >= lastRow) ||
                    (ay <= firstRow - 1 && by <= firstRow - 1 && cy <= firstRow - 1)) continue;
                Vector2 a = new Vector2(au.x * w, ay);
                Vector2 b = new Vector2(bu.x * w, by);
                Vector2 c = new Vector2(cu.x * w, cy);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(firstRow, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                int y1 = Mathf.Min(lastRow - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                if (x0 > x1 || y0 > y1) continue;

                float area = Edge(a, b, c);
                if (Mathf.Abs(area) < 1e-12f) continue;
                float invArea = 1f / area;
                ushort id = (ushort)Mathf.Min(triIsland[t] + 1, 65535);
                bool useSpans = x1 - x0 >= MinimumIslandSpanWidth - 1 &&
                    IslandSpanCoordinateIsSafe(a) && IslandSpanCoordinateIsSafe(b) && IslandSpanCoordinateIsSafe(c) &&
                    float.IsFinite(invArea);
                var edgeBC = new IslandSpanEdge(b, c, invArea);
                var edgeCA = new IslandSpanEdge(c, a, invArea);
                var edgeAB = new IslandSpanEdge(a, b, invArea);
                for (int y = y0; y <= y1; y++)
                {
                    if ((y & 15) == 0) cancellation.ThrowIfCancellationRequested();
                    float py = y + 0.5f;
                    if (useSpans)
                    {
                        int from = x0, to = x1;
                        if (!edgeBC.Clip(py, ref from, ref to) ||
                            !edgeCA.Clip(py, ref from, ref to) ||
                            !edgeAB.Clip(py, ref from, ref to)) continue;
                        write(y, from, to, id);
                    }
                    else
                    {
                        int start = -1;
                        for (int x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(x + 0.5f, py);
                            float w0 = Edge(b, c, p) * invArea;
                            float w1 = Edge(c, a, p) * invArea;
                            float w2 = Edge(a, b, p) * invArea;
                            if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f)
                            {
                                if (start >= 0) { write(y, start, x - 1, id); start = -1; }
                            }
                            else if (start < 0) start = x;
                        }
                        if (start >= 0) write(y, start, x1, id);
                    }
                }
            }
        }

        static List<int> SeedIslandDilation(bool[] covered, int width, int height, CancellationToken cancellation) =>
            SeedIslandDilationRows(covered, width, height, 0, height, cancellation);

        static List<int> SeedIslandDilationBands(bool[] covered, int width, int height, int workers, CancellationToken cancellation)
        {
            var bands = new List<int>[workers];
            var options = new ParallelOptions { MaxDegreeOfParallelism = workers, TaskScheduler = TaskScheduler.Default, CancellationToken = cancellation };
            Parallel.For(0, workers, options, band => bands[band] = SeedIslandDilationRows(covered, width, height,
                band * height / workers, (band + 1) * height / workers, cancellation));

            int count = 0;
            foreach (var band in bands) count += band.Count;
            var ordered = new List<int>(count);
            foreach (var band in bands) ordered.AddRange(band);
            return ordered;
        }

        static List<int> SeedIslandDilationRows(bool[] covered, int width, int height, int firstRow, int lastRow,
                                                CancellationToken cancellation)
        {
            var frontier = new List<int>();
            for (int y = firstRow; y < lastRow; y++)
            {
                int row = y * width;
                int above = y == 0 ? row : row - width;
                int below = y + 1 == height ? row : row + width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                    if (!covered[i]) continue;
                    int left = x == 0 ? x : x - 1;
                    int right = x + 1 == width ? x : x + 1;
                    if (!covered[above + left] || !covered[above + x] || !covered[above + right] ||
                        !covered[row + left] || !covered[row + right] ||
                        !covered[below + left] || !covered[below + x] || !covered[below + right])
                        frontier.Add(i);
                }
            }
            return frontier;
        }

        static bool IslandSpanCoordinateIsSafe(Vector2 p) =>
            Mathf.Abs(p.x) < MaxIslandSpanCoordinate && Mathf.Abs(p.y) < MaxIslandSpanCoordinate;

        readonly struct IslandSpanEdge
        {
            readonly float dx, dy, x0, y0, invArea;

            internal IslandSpanEdge(Vector2 a, Vector2 b, float invArea)
            {
                dx = b.x - a.x;
                dy = b.y - a.y;
                x0 = a.x;
                y0 = a.y;
                this.invArea = invArea;
            }

            internal bool Clip(float y, ref int from, ref int to)
            {
                float rowTerm = dx * (y - y0);
                bool left = (rowTerm - dy * (from + 0.5f - x0)) * invArea >= -1e-5f;
                bool right = (rowTerm - dy * (to + 0.5f - x0)) * invArea >= -1e-5f;
                if (left == right) return left;
                int low = from, high = to;
                while (high - low > 1)
                {
                    int mid = low + (high - low) / 2;
                    bool inside = (rowTerm - dy * (mid + 0.5f - x0)) * invArea >= -1e-5f;
                    if (inside == left) low = mid;
                    else high = mid;
                }
                if (left) to = low;
                else from = high;
                return true;
            }
        }
    }
}
