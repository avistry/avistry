using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Colour Match helpers: the main colours of a texture (k-means in OKLab, only over the parts of the texture the
    /// mesh uses), and pairing two palettes by brightness so light stays light and dark stays dark.
    /// </summary>
    public static class ColourMatch
    {
        public struct Swatch
        {
            public Color colour;   // gamma
            public float share;    // 0..1 of the sampled area
        }

        public const int SampleSize = 96;

        /// <summary>Main colours of the pixels (gamma) where mask > 0.5 (null = everywhere), darkest first.</summary>
        public static List<Swatch> MainColours(Color[] px, byte[] mask, int k)
        {
            var labs = new List<Vector3>(px?.Length ?? 0);
            if (px == null) return new List<Swatch>();
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a < 0.1f) continue;
                if (mask != null && (i >= mask.Length || mask[i] < 128)) continue;
                labs.Add(Recolour.ToOklab(new Color(Mathf.Clamp01(px[i].r), Mathf.Clamp01(px[i].g), Mathf.Clamp01(px[i].b), 1f)));
            }
            if (labs.Count == 0) return new List<Swatch>();
            k = Mathf.Clamp(k, 1, Mathf.Min(8, labs.Count));

            // Deterministic start: the sample nearest the average, then repeatedly the sample farthest from all centres.
            var mean = Vector3.zero;
            foreach (var l in labs) mean += l;
            mean /= labs.Count;
            var centres = new List<Vector3> { labs.OrderBy(l => (l - mean).sqrMagnitude).First() };
            var dist = labs.Select(l => (l - centres[0]).sqrMagnitude).ToArray();
            while (centres.Count < k)
            {
                int far = 0;
                for (int i = 1; i < dist.Length; i++) if (dist[i] > dist[far]) far = i;
                if (dist[far] < 1e-6f) break; // fewer distinct colours than asked for
                centres.Add(labs[far]);
                for (int i = 0; i < dist.Length; i++) dist[i] = Mathf.Min(dist[i], (labs[i] - labs[far]).sqrMagnitude);
            }

            bool pruned = false;
            var assign = new int[labs.Count];
            for (int iter = 0; iter < 20; iter++)
            {
                var sum = new Vector3[centres.Count];
                var cnt = new int[centres.Count];
                for (int i = 0; i < labs.Count; i++)
                {
                    int best = 0;
                    float bd = float.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        float d = (labs[i] - centres[c]).sqrMagnitude;
                        if (d < bd) { bd = d; best = c; }
                    }
                    assign[i] = best;
                    sum[best] += labs[i];
                    cnt[best]++;
                }
                bool moved = false;
                for (int c = 0; c < centres.Count; c++)
                {
                    if (cnt[c] == 0) continue;
                    var nc = sum[c] / cnt[c];
                    if ((nc - centres[c]).sqrMagnitude > 1e-8f) moved = true;
                    centres[c] = nc;
                }
                // Tiny clusters (a few stray pixels) aren't main colours: drop them and let the rest take their pixels.
                if (iter == 6)
                {
                    pruned = true;
                    for (int c = centres.Count - 1; c >= 0 && centres.Count > 1; c--)
                        if (cnt[c] < labs.Count * 0.015f) { centres.RemoveAt(c); moved = true; }
                }
                if (!moved) break;
            }

            int[] counts;
            while (true)
            {
                counts = new int[centres.Count];
                for (int i = 0; i < labs.Count; i++)
                {
                    int best = 0;
                    float bd = float.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        float d = (labs[i] - centres[c]).sqrMagnitude;
                        if (d < bd) { bd = d; best = c; }
                    }
                    counts[best]++;
                }
                if (pruned) break;
                // Converged before the in-loop prune ran: prune once here, then count again.
                pruned = true;
                bool removed = false;
                for (int c = centres.Count - 1; c >= 0 && centres.Count > 1; c--)
                    if (counts[c] < labs.Count * 0.015f) { centres.RemoveAt(c); removed = true; }
                if (!removed) break;
            }
            var result = new List<Swatch>();
            for (int c = 0; c < centres.Count; c++)
            {
                if (counts[c] == 0) continue;
                var col = Recolour.FromOklab(centres[c], 1f);
                result.Add(new Swatch { colour = new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), 1f), share = counts[c] / (float)labs.Count });
            }
            return result.OrderBy(s => Recolour.ToOklab(s.colour).x).ToList();
        }

        /// <summary>
        /// For each colour of this texture (darkest first), the other material's colour of the same brightness rank.
        /// Keeps the light / dark structure: highlights stay highlights, shadows stay shadows.
        /// </summary>
        public static List<Color> PairByBrightness(List<Swatch> target, List<Swatch> source)
        {
            var result = new List<Color>();
            if (target.Count == 0 || source.Count == 0) return result;
            for (int i = 0; i < target.Count; i++)
            {
                float t = target.Count == 1 ? 0.5f : i / (float)(target.Count - 1);
                int j = Mathf.Clamp(Mathf.RoundToInt(t * (source.Count - 1)), 0, source.Count - 1);
                result.Add(source[j].colour);
            }
            return result;
        }

        /// <summary>The main colours of a material: its main texture (times its colour), only where its meshes use it.</summary>
        public static List<Swatch> MaterialColours(Material m, Transform avatarRoot, int k)
        {
            if (m == null) return new List<Swatch>();
            Color[] px = null;
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) px = MaterialLook.Pixels(m, SampleSize); // as the material shows it
            if (px == null)
            {
                var c = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                return new List<Swatch> { new Swatch { colour = new Color(c.r, c.g, c.b, 1f), share = 1f } };
            }
            byte[] mask = null;
            var users = avatarRoot != null ? TargetResolver.FindMaterialUsers(avatarRoot, m) : null;
            if (users != null && users.Count > 0)
            {
                var uv = MeshUV.Build(users, 0);
                if (uv != null) mask = uv.RasterIslandMask(SampleSize, SampleSize, null, 1);
            }
            return MainColours(px, mask, k);
        }

        /// <summary>
        /// Main colours of a texture on its own. Where this avatar uses it as a main texture, only the parts its meshes
        /// cover count (no empty space between UV islands); otherwise every pixel that isn't transparent.
        /// </summary>
        public static List<Swatch> TextureColours(Texture2D t, Transform avatarRoot, int k)
        {
            if (t == null) return new List<Swatch>();
            var px = Recolour.SampleTexture(t, SampleSize, Color.white);
            if (px == null || px.Length == 0) return new List<Swatch>();
            byte[] mask = null;
            if (avatarRoot != null)
            {
                var users = new List<TargetResolver.SlotRef>();
                foreach (var m in avatarRoot.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                    if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == t) users.AddRange(TargetResolver.FindMaterialUsers(avatarRoot, m));
                if (users.Count > 0)
                {
                    var uv = MeshUV.Build(users, 0);
                    if (uv != null) mask = uv.RasterIslandMask(SampleSize, SampleSize, null, 1);
                }
            }
            if (mask == null)
            {
                mask = new byte[px.Length];
                int opaque = 0;
                for (int i = 0; i < px.Length; i++) if (px[i].a > 0.1f) { mask[i] = 255; opaque++; }
                if (opaque == 0) mask = null; // fully transparent texture: use every pixel rather than nothing
            }
            return MainColours(px, mask, k);
        }
    }
}
