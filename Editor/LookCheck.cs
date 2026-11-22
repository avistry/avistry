using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Finds shader features that add so much light on top of a texture that edits to it barely show on the model
    /// (mainly Emission: a bright emission texture, or emission without a texture that glows everywhere).
    /// </summary>
    public static class LookCheck
    {
        public class Glow
        {
            public string label;        // "Emission", "Emission 2"
            public string mapProperty;  // texture property of that emission
            public Texture2D map;       // null = glows evenly everywhere
            public float amount;        // average light it adds (0..1+, 1 = full white)
        }

        static readonly Dictionary<int, (double time, List<Glow> list)> s_cache = new Dictionary<int, (double, List<Glow>)>();

        /// <summary>Emissions that add at least <paramref name="min"/> light on average. Cached for a second per material.</summary>
        public static List<Glow> StrongEmissions(Material m, float min = 0.25f)
        {
            var result = new List<Glow>();
            if (m == null) return result;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (s_cache.TryGetValue(m.GetInstanceID(), out var c) && now - c.time < 1.0) return c.list;
            foreach (var g in Emissions(m)) if (g.amount >= min) result.Add(g);
            s_cache[m.GetInstanceID()] = (now, result);
            return result;
        }

        /// <summary>Drops the cached answer for a material (after its settings changed).</summary>
        public static void Forget(Material m) { if (m != null) s_cache.Remove(m.GetInstanceID()); }

        static IEnumerable<Glow> Emissions(Material m)
        {
            switch (ShaderProfiles.Detect(m.shader))
            {
                case ShaderFamily.Poiyomi:
                    for (int i = 0; i < 4; i++)
                    {
                        string s = i == 0 ? "" : i.ToString();
                        if (!(m.HasProperty("_EnableEmission" + s) && m.GetFloat("_EnableEmission" + s) > 0.5f)) continue;
                        float strength = m.HasProperty("_EmissionStrength" + s) ? m.GetFloat("_EmissionStrength" + s) : 1f;
                        var col = m.HasProperty("_EmissionColor" + s) ? m.GetColor("_EmissionColor" + s) : Color.white;
                        var g = Make(m, i == 0 ? Loc.T("Emission") : Loc.F("Emission {0}", i + 1), "_EmissionMap" + s, strength * col.maxColorComponent);
                        if (g != null) yield return g;
                    }
                    break;
                case ShaderFamily.LilToon:
                    foreach (var (use, map, colour, label) in new[] { ("_UseEmission", "_EmissionMap", "_EmissionColor", Loc.T("Emission")), ("_UseEmission2nd", "_Emission2ndMap", "_Emission2ndColor", Loc.T("Emission 2")) })
                    {
                        if (!(m.HasProperty(use) && m.GetFloat(use) > 0.5f)) continue;
                        var col = m.HasProperty(colour) ? m.GetColor(colour) : Color.white;
                        var g = Make(m, label, map, col.maxColorComponent * col.a);
                        if (g != null) yield return g;
                    }
                    break;
            }
        }

        static Glow Make(Material m, string label, string mapProp, float strength)
        {
            if (strength <= 0.01f) return null;
            var map = m.HasProperty(mapProp) ? m.GetTexture(mapProp) as Texture2D : null;
            float avg = 1f;
            if (map != null)
            {
                var px = Recolour.SampleTexture(map, 24, Color.white);
                if (px != null && px.Length > 0)
                {
                    double sum = 0;
                    foreach (var p in px) sum += p.maxColorComponent;
                    avg = (float)(sum / px.Length);
                }
            }
            return new Glow { label = label, mapProperty = mapProp, map = map, amount = strength * avg };
        }
    }
}
