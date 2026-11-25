using UnityEngine;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer
    {
        static void EmitFlatDrips(int seed, int site, Vector2 centre, float radius, float opacity, int width, int height)
        {
            float size = radius * .35f;
            for (int particle = 0; particle < BrushDrips.Particles; particle++)
            {
                BrushDrips.Start(seed, site, particle, out float angle, out float distance);
                var start = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (distance * radius);
                for (int life = 0; life < BrushDrips.Lifetime; life++)
                    Quad(start + Vector2.down * (size * life), size,
                        opacity * (1f - life / (float)BrushDrips.Lifetime), 0f, 1f, width, height);
            }
        }
    }
}
