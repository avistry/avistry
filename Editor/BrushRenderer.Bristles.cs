using System;
using UnityEngine;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer
    {
        internal const int CoherentBristleModel = 1;

        internal struct BristleFiber
        {
            internal float Offset, Width, Strength, Phase, Lag, Feather;
        }

        internal struct BristleSample
        {
            internal Vector2 Left, Right;
            internal float Opacity;
        }

        internal sealed class BristleState
        {
            internal BristleFiber[] Fibers = Array.Empty<BristleFiber>();
            internal BristleSample[] Samples = Array.Empty<BristleSample>();
            internal int Seed, Count;
            internal bool Active;
            internal float Distance;
            internal float TipAngle = float.NaN;
            internal Vector2 Center, Direction;

            internal void ResetRun() { Active = false; Distance = 0f; TipAngle = float.NaN; }

            internal void Prepare(int seed, int density)
            {
                int count = Mathf.Clamp(density, 1, 32) * 2;
                if (Seed == seed && Count == count) return;
                if (Fibers.Length < count)
                {
                    Fibers = new BristleFiber[count];
                    Samples = new BristleSample[count];
                }
                for (int i = 0; i < count; i++)
                {
                    var rng = new BrushRng(seed, 2000003 + i);
                    float offset = (i + .15f + rng.Next() * .7f) / count * 2f - 1f;
                    Fibers[i] = new BristleFiber
                    {
                        Offset = offset, Width = Mathf.Lerp(.65f, 1.45f, rng.Next()) / count,
                        Strength = Mathf.Lerp(.4f, 1f, rng.Next()) * Mathf.Lerp(.55f, 1f, 1f - offset * offset),
                        Phase = rng.Next() * 31f, Lag = (rng.Next() - .5f) * .5f, Feather = rng.Next()
                    };
                }
                Seed = seed; Count = count; ResetRun();
            }
        }

        internal interface IBristleSink
        {
            void Ribbon(BristleSample previous, BristleSample next);
            void Cap(Vector2 center, Vector2 direction, float length, float width, float opacity);
        }

        struct BristleDraw : IBristleSink
        {
            readonly int _width, _height;
            internal BristleDraw(int width, int height) { _width = width; _height = height; }

            public void Ribbon(BristleSample previous, BristleSample next)
            {
                Corner(previous.Left, 0, -1, previous.Opacity, _width, _height);
                Corner(previous.Right, 0, 1, previous.Opacity, _width, _height);
                Corner(next.Right, 0, 1, next.Opacity, _width, _height);
                Corner(next.Left, 0, -1, next.Opacity, _width, _height);
            }

            public void Cap(Vector2 center, Vector2 direction, float length, float width, float opacity)
            {
                var along = direction * length;
                var across = new Vector2(-direction.y, direction.x) * width;
                Corner(center - along - across, -1, -1, opacity, _width, _height);
                Corner(center - along + across, -1, 1, opacity, _width, _height);
                Corner(center + along + across, 1, 1, opacity, _width, _height);
                Corner(center + along - across, 1, -1, opacity, _width, _height);
            }
        }

        BristleState _fullBristles, _liveBristles;

        static bool UsesCoherentBristles(PaintStroke stroke, BrushShape shape) =>
            shape.tip == BrushTip.Bristle && stroke.bristleModel == CoherentBristleModel;

        static float CoherentBristleStep(BrushShape shape, float radius) => Mathf.Max(.5f, Mathf.Clamp(shape.spacing, .12f, .3f) * radius);

        BristleState BristlesFor(PaintStroke stroke, BrushShape shape, bool retain, bool reset)
        {
            var state = retain ? _liveBristles ??= new BristleState() : _fullBristles ??= new BristleState();
            state.Prepare(stroke.seed, shape.density);
            if (reset) state.ResetRun();
            return state;
        }

        static float BristleNoise(int seed, int channel, float distance)
        {
            int cell = Mathf.FloorToInt(distance);
            float f = distance - cell;
            f = f * f * (3f - 2f * f);
            var first = new BrushRng(seed, unchecked(channel * 486187739 + cell * 16777619));
            var second = new BrushRng(seed, unchecked(channel * 486187739 + (cell + 1) * 16777619));
            return Mathf.Lerp(first.Next(), second.Next(), f);
        }

        internal static void EmitBristleDab<T>(PaintStroke stroke, BrushShape shape, Vector4 point, Vector2 direction,
            float baseRadius, int width, int height, BristleState state, ref T sink) where T : struct, IBristleSink
        {
            var center = new Vector2(point.x * width, point.y * height);
            float advance = state.Active ? Vector2.Distance(center, state.Center) / Mathf.Max(baseRadius, 1e-5f) : 0f;
            state.Distance += advance;
            if (!state.Active) state.Direction = direction;
            else
            {
                float angle = Mathf.Atan2(state.Direction.y, state.Direction.x) * Mathf.Rad2Deg;
                float wanted = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                angle += Mathf.DeltaAngle(angle, wanted) * (1f - Mathf.Exp(-advance / .28f));
                state.Direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            }
            float tipAngle = TipRotation(stroke, shape, state.Direction, state.TipAngle);
            tipAngle += (BristleNoise(stroke.seed, 67, state.Distance * .9f) * 2f - 1f) * shape.angleJitter * Mathf.PI;
            var along = new Vector2(Mathf.Cos(tipAngle), Mathf.Sin(tipAngle));
            var across = new Vector2(-along.y, along.x);
            float radius = baseRadius * SizeFactor(stroke, point.z) *
                (1f - shape.sizeJitter * BristleNoise(stroke.seed, 71, state.Distance * 1.6f));
            float opacity = OpacityFactor(stroke, point.z) *
                (1f - shape.opacityJitter * BristleNoise(stroke.seed, 97, state.Distance * 1.4f));
            if (shape.buildUp) opacity *= shape.flow;
            if (shape.scatter > 0f)
                center += (along * (BristleNoise(stroke.seed, 107, state.Distance) - .5f) +
                    across * (BristleNoise(stroke.seed, 109, state.Distance) - .5f)) * (shape.scatter * baseRadius);
            for (int i = 0; i < state.Count; i++)
            {
                var fiber = state.Fibers[i];
                float noiseAt = state.Distance * .8f + fiber.Phase;
                float drift = (BristleNoise(stroke.seed, i + 211, noiseAt) - .5f) * .07f;
                float taper = Mathf.Lerp(.35f, 1f, Mathf.SmoothStep(0f, 1f, state.Distance / (.3f + fiber.Feather * .4f)));
                float fiberWidth = radius * fiber.Width * taper * Mathf.Lerp(.72f, 1.18f, BristleNoise(stroke.seed, i + 307, noiseAt));
                float capRadius = fiberWidth;
                float deposit = BristleNoise(stroke.seed, i + 401, noiseAt);
                float strength = opacity * fiber.Strength * taper * Mathf.Lerp(.65f, 1f, deposit);
                if (shape.bristleLoad == BristleLoad.Dry)
                {
                    fiberWidth *= .7f;
                    capRadius = fiberWidth;
                    strength *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, .69f, deposit));
                }
                else if (shape.bristleLoad == BristleLoad.Loaded)
                {
                    float roomAtEdge = radius * (1f - Mathf.Abs(fiber.Offset + drift)) - fiberWidth;
                    fiberWidth += Mathf.Clamp(roomAtEdge, 0f, radius * taper * .9f / state.Count);
                    strength = Mathf.Lerp(strength, opacity * taper, .52f);
                }
                var fiberCenter = center + across * ((fiber.Offset + drift) * radius * Mathf.Lerp(.8f, 1f, taper)) +
                    along * (fiber.Lag * radius);
                var edge = new Vector2(-state.Direction.y, state.Direction.x) * fiberWidth;
                var sample = new BristleSample { Left = fiberCenter - edge, Right = fiberCenter + edge, Opacity = strength };
                if (state.Active) sink.Ribbon(state.Samples[i], sample);
                sink.Cap(fiberCenter, state.Direction, capRadius * (1.5f + fiber.Feather * 1.7f), fiberWidth, strength * .8f);
                state.Samples[i] = sample;
            }
            state.Center = new Vector2(point.x * width, point.y * height);
            state.Active = true;
        }
    }
}
