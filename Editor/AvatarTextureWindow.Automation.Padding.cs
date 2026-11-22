using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal string AutoSetPadding(bool on) { Exporter.PadSeams = on; return null; }

        /// <summary>Renders a colour slot with and without seam padding and compares them.</summary>
        internal string AutoPaddingCheck(int size)
        {
            var slot = CurrentSlot;
            if (slot == null || slot.role != TextureRole.Color || _uv == null) return "not a colour slot";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var a = Exporter.RenderRecipe(slot, _recipe.layers, _uv, false, false, out _, size);
            long msA = sw.ElapsedMilliseconds;
            sw.Restart();
            var b = Exporter.RenderRecipe(slot, _recipe.layers, _uv, false, true, out var warn, size);
            long msB = sw.ElapsedMilliseconds;
            int w = a.width, h = a.height;
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            DestroyImmediate(a);
            DestroyImmediate(b);
            var covered = _uv.RasterIslandIds(w, h, 0);
            bool coveredEqual = true;
            var dist = new int[pa.Length];
            var src = new int[pa.Length];
            var frontier = new List<int>();
            for (int i = 0; i < pa.Length; i++)
            {
                dist[i] = covered[i] != 0 ? 0 : -1;
                src[i] = i;
                if (covered[i] == 0) continue;
                frontier.Add(i);
                if (!pa[i].Equals(pb[i])) coveredEqual = false;
            }
            // 8-connected distance from coverage, up to the padding radius.
            int radius = SeamPadding.Radius(w, h);
            var next = new List<int>();
            for (int step = 1; step <= radius && frontier.Count > 0; step++)
            {
                next.Clear();
                foreach (int i in frontier)
                {
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int n = ny * w + nx;
                            if (dist[n] >= 0) continue;
                            dist[n] = step;
                            src[n] = src[i];
                            next.Add(n);
                        }
                }
                (frontier, next) = (next, frontier);
            }
            // A zone pixel counts as filled when padding changed it, or when it already holds its nearest piece pixel's colour
            // (layers can colour the zone themselves). A little slack covers ties between two pieces.
            int unfilled = 0, zone = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (dist[i] <= 0) continue;
                zone++;
                var q = pb[src[i]];
                bool changed = pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b;
                if (!changed && (pb[i].r != q.r || pb[i].g != q.g || pb[i].b != q.b)) unfilled++;
            }
            bool filled = unfilled * 100 <= zone;
            return "coveredEqual=" + (coveredEqual ? "true" : "false") + " uncoveredDiffer=" + (filled ? "true" : "false")
                + " unfilled=" + unfilled + " ms=" + (msB - msA) + " warning=" + (warn ?? "none");
        }

        /// <summary>Bakes one slot of a test copy (PNG only) and returns the SHA-256 of the file.</summary>
        internal string AutoExportSlotHash(int slotIndex)
        {
            if (_material == null || !AssetDatabase.GetAssetPath(_material).StartsWith("Assets/_ATS_Test/")) return "refused: not a test copy";
            if (slotIndex < 0 || slotIndex >= _slots.Count) return "no such slot";
            var result = Exporter.Bake(_slots[slotIndex], _material, new List<TargetResolver.SlotRef>(), _recipe, _uv, OutputMode.PngOnly, false, null, null);
            if (string.IsNullOrEmpty(result.pngPath)) return "bake failed";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(result.pngPath))).Replace("-", "").ToLowerInvariant();
        }
    }
}
