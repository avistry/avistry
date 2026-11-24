using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Avistry.Textures
{
    internal static class MapPixels
    {
        internal static byte[] EncodeRgba(Color32[] pixels, int width, int height) =>
            ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);

        internal static void PadEdges(Color32[] pixels, ushort[] ids, int width, int height, int radius,
                                      bool keepAlpha, CancellationToken cancel = default)
        {
            var frontier = new List<int>();
            for (int y = 0; y < height; y++)
            {
                cancel.ThrowIfCancellationRequested();
                int row = y * width;
                int top = y == 0 ? row : row - width;
                int bottom = y + 1 == height ? row : row + width;
                for (int x = 0; x < width; x++)
                {
                    if (ids[row + x] == 0) continue;
                    int left = x > 0 ? x - 1 : x;
                    int right = x + 1 < width ? x + 1 : x;
                    if (ids[row + left] == 0 || ids[row + right] == 0 ||
                        ids[top + left] == 0 || ids[top + x] == 0 || ids[top + right] == 0 ||
                        ids[bottom + left] == 0 || ids[bottom + x] == 0 || ids[bottom + right] == 0)
                        frontier.Add(row + x);
                }
            }
            if (frontier.Count == 0 || radius <= 0) return;
            var claimed = new bool[pixels.Length];
            var next = new List<int>();
            for (int step = 0; step < radius && frontier.Count > 0; step++)
            {
                next.Clear();
                for (int j = 0; j < frontier.Count; j++)
                {
                    if ((j & 1023) == 0) cancel.ThrowIfCancellationRequested();
                    int i = frontier[j], y = i / width, x = i - y * width;
                    int left = x > 0 ? x - 1 : x, right = x + 1 < width ? x + 1 : x;
                    int top = y > 0 ? y - 1 : y, bottom = y + 1 < height ? y + 1 : y;
                    var colour = pixels[i];
                    for (int ny = top; ny <= bottom; ny++)
                        for (int n = ny * width + left, last = ny * width + right; n <= last; n++)
                        {
                            if (ids[n] != 0 || claimed[n]) continue;
                            claimed[n] = true;
                            if (keepAlpha) colour.a = pixels[n].a;
                            pixels[n] = colour;
                            next.Add(n);
                        }
                }
                (frontier, next) = (next, frontier);
            }
        }
    }
}
