using UnityEngine;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer
    {
        internal static Vector2 TipSample(PaintStroke stroke, int index) => stroke.pressureModel == 1 && stroke.pen != null &&
            stroke.pen.direction != BrushPenDirection.Brush && stroke.tipAngles?.Count == stroke.points.Count &&
            index >= 0 && index < stroke.tipAngles.Count ? stroke.tipAngles[index] : Vector2.zero;

        internal static float PenAngle(PaintStroke stroke, int index)
        {
            var sample = TipSample(stroke, index);
            return sample.y > .5f && float.IsFinite(sample.x) ? sample.x : float.NaN;
        }

        internal static float InterpolatePenAngle(PaintStroke stroke, int previous, int next, float amount)
        {
            float a = PenAngle(stroke, previous), b = PenAngle(stroke, next);
            if (!float.IsFinite(a)) return b;
            if (!float.IsFinite(b)) return a;
            return a + Mathf.DeltaAngle(a, b) * Mathf.Clamp01(amount);
        }

        internal static bool FollowsDirection(PaintStroke stroke, BrushShape shape) => shape.followStroke ||
            stroke.pressureModel == 1 && stroke.pen != null && stroke.pen.direction != BrushPenDirection.Brush;

        internal static float TipRotation(PaintStroke stroke, BrushShape shape, Vector2 direction, float penAngle)
        {
            float angle = shape.angle * Mathf.Deg2Rad;
            if (float.IsFinite(penAngle)) angle += penAngle * Mathf.Deg2Rad;
            else if (FollowsDirection(stroke, shape)) angle += Mathf.Atan2(direction.y, direction.x);
            return angle;
        }
    }
}
