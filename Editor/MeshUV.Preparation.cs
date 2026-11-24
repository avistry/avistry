using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class MeshUV
    {
        internal sealed class PreparedIslandIds
        {
            internal ushort[] exact, padded;
            internal int[] totals, owners;
        }

        internal PreparedIslandIds PrepareIslandOwnership(int width, int height, int padding, bool keepExact,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!keepExact) return new PreparedIslandIds
            {
                padded = padding > 0 ? RasterPaddedIslandIds(width, height, padding, cancellation) : null
            };
            var values = new ushort[width * height];
            var covered = padding > 0 ? new bool[values.Length] : null;
            RasterIslandOwnership(width, height, values, covered, 1, cancellation);
            var result = new PreparedIslandIds { exact = values, totals = new int[islandCount] };
            for (int i = 0; i < values.Length; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int id = values[i];
                if (id > 0 && id <= result.totals.Length) result.totals[id - 1]++;
            }
            result.owners = WandOwners(values, width, height, result.totals, cancellation);
            if (padding > 0)
            {
                var padded = (ushort[])values.Clone();
                var seeds = SeedIslandDilation(covered, width, height, cancellation);
                DilateValues(padded, covered, width, height, padding, cancellation, seeds);
                result.padded = padded;
            }
            cancellation.ThrowIfCancellationRequested();
            return result;
        }

        internal int[] WandOwners(ushort[] ids, int width, int height, int[] totals, CancellationToken cancellation)
        {
            var owners = new int[totals.Length];
            Dictionary<int, Dictionary<int, int>> votes = null;
            for (int i = 0; i < owners.Length; i++) owners[i] = totals[i] > 0 ? i : -1;
            for (int t = 0; t < triIsland.Length; t++)
            {
                if ((t & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int piece = triIsland[t];
                if (piece < 0 || piece >= owners.Length || totals[piece] > 0) continue;
                var centre = (uv[tris[t * 3]] + uv[tris[t * 3 + 1]] + uv[tris[t * 3 + 2]]) / 3f;
                if (float.IsNaN(centre.x) || centre.x < 0f || centre.y < 0f || centre.x >= 1f || centre.y >= 1f) continue;
                int under = ids[Mathf.FloorToInt(centre.y * height) * width + Mathf.FloorToInt(centre.x * width)] - 1;
                if (under < 0 || under >= owners.Length || totals[under] == 0) continue;
                votes ??= new Dictionary<int, Dictionary<int, int>>();
                if (!votes.TryGetValue(piece, out var tally)) votes[piece] = tally = new Dictionary<int, int>();
                tally.TryGetValue(under, out int count);
                tally[under] = count + 1;
            }
            if (votes != null)
                foreach (var pair in votes)
                {
                    int best = -1, bestCount = 0;
                    foreach (var vote in pair.Value)
                        if (vote.Value > bestCount) { best = vote.Key; bestCount = vote.Value; }
                    owners[pair.Key] = best;
                }
            return owners;
        }
    }
}
