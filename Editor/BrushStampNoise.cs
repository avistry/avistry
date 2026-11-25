using System;

namespace Avistry.Textures
{
    // Adapted from TextureCraft crates/texel/src/noise.rs (MIT); see Third Party Notices.md.
    internal static class BrushStampNoise
    {
        internal static void Pcg(uint x, uint y, uint z, out uint a, out uint b, out uint c)
        {
            unchecked
            {
                a = x * 1664525u + 1013904223u; b = y * 1664525u + 1013904223u; c = z * 1664525u + 1013904223u;
                a += b * c; b += c * a; c += a * b;
                a ^= a >> 16; b ^= b >> 16; c ^= c >> 16;
                a += b * c; b += c * a; c += a * b;
            }
        }

        internal static float Hash(int x, int y, int z, uint seed)
        {
            Pcg(unchecked((uint)x ^ seed * 0x9E3779B9u), (uint)y, (uint)z, out uint a, out _, out _);
            return a / (float)uint.MaxValue;
        }

        static int Cell(float value, out float fraction)
        {
            float floor = MathF.Floor(value);
            fraction = value - floor;
            return floor >= int.MaxValue ? int.MaxValue : floor <= int.MinValue ? int.MinValue : (int)floor;
        }

        static float Smooth(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        internal static float Value(float x, float y, float z, uint seed) => Lattice(x, y, z, seed, false);
        internal static float Perlin(float x, float y, float z, uint seed) => Lattice(x, y, z, seed, true);

        static float Lattice(float x, float y, float z, uint seed, bool gradient)
        {
            int ix = Cell(x, out float fx), iy = Cell(y, out float fy), iz = Cell(z, out float fz);
            float sx = Smooth(fx), sy = Smooth(fy), sz = Smooth(fz);
            float Corner(int dx, int dy, int dz)
            {
                int a = unchecked(ix + dx), b = unchecked(iy + dy), c = unchecked(iz + dz);
                if (!gradient) return Hash(a, b, c, seed);
                Pcg(unchecked((uint)a ^ seed * 0x85EBCA6Bu), (uint)b, (uint)c, out _, out uint code, out _);
                float u = fx - dx, v = fy - dy, w = fz - dz;
                switch (code % 12)
                {
                    case 0: return u + v; case 1: return -u + v; case 2: return u - v; case 3: return -u - v;
                    case 4: return u + w; case 5: return -u + w; case 6: return u - w; case 7: return -u - w;
                    case 8: return v + w; case 9: return -v + w; case 10: return v - w; default: return -v - w;
                }
            }
            float value = Lerp(Lerp(Lerp(Corner(0, 0, 0), Corner(1, 0, 0), sx),
                    Lerp(Corner(0, 1, 0), Corner(1, 1, 0), sx), sy),
                Lerp(Lerp(Corner(0, 0, 1), Corner(1, 0, 1), sx), Lerp(Corner(0, 1, 1), Corner(1, 1, 1), sx), sy), sz);
            return gradient ? Math.Clamp(value * .5f + .5f, 0f, 1f) : value;
        }

        internal static float Fbm(float x, float y, float z, int octaves, uint seed, bool value = false)
        {
            float sum = 0, amplitude = .5f, normalization = 0;
            for (int i = 0; i < octaves; i++)
            {
                uint octaveSeed = unchecked(seed + (uint)i * 31);
                sum += amplitude * (value ? Value(x, y, z, octaveSeed) : Perlin(x, y, z, octaveSeed));
                normalization += amplitude;
                x = x * 2 + 17.13f; y = y * 2 + 17.13f; z = z * 2 + 17.13f;
                amplitude *= .5f;
            }
            return sum / normalization;
        }

        internal static (float first, float second, float id) Cellular(float x, float y, float z, uint seed)
        {
            int ix = Cell(x, out float fx), iy = Cell(y, out float fy), iz = Cell(z, out float fz);
            float first = float.MaxValue, second = float.MaxValue;
            uint id = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Pcg(unchecked((uint)(ix + dx) ^ seed * 0xC2B2AE35u), unchecked((uint)(iy + dy)),
                    unchecked((uint)(iz + dz)), out uint a, out uint b, out uint c);
                float u = dx + a / (float)uint.MaxValue - fx, v = dy + b / (float)uint.MaxValue - fy;
                float w = dz + c / (float)uint.MaxValue - fz;
                float distance = u * u + v * v + w * w;
                if (distance < first) { second = first; first = distance; id = a ^ b; }
                else if (distance < second) second = distance;
            }
            return (MathF.Min(MathF.Sqrt(first), 1), MathF.Min(MathF.Sqrt(second), 1), id / (float)uint.MaxValue);
        }
    }
}
