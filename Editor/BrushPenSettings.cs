using System;
using UnityEngine;

namespace Avistry.Textures
{
    [Serializable]
    public sealed class BrushPenSettings
    {
        [Range(.25f, 4f)] public float sensitivity = 1f;
        [Range(0f, 1f)] public float minimumSize = .15f;
        [Range(0f, 1f)] public float minimumStrength;
        [Range(0, 95)] public int lightPressureTrim;
        [Range(0, 95)] public int firmPressureTrim;
        [Range(0f, .95f)] public float stabilisation;
        public BrushPenDirection direction;

        public BrushPenSettings Clone() => (BrushPenSettings)MemberwiseClone();

        internal float Response(float pressure)
        {
            pressure = Mathf.Clamp01(pressure);
            if (lightPressureTrim != 0 || firmPressureTrim != 0)
                pressure = Mathf.InverseLerp(lightPressureTrim * .01f, (100 - firmPressureTrim) * .01f, pressure);
            return sensitivity == 1f ? pressure : Mathf.Pow(pressure,
                1f / Mathf.Clamp(float.IsFinite(sensitivity) ? sensitivity : 1f, .25f, 4f));
        }

        internal bool Valid => float.IsFinite(sensitivity + minimumSize + minimumStrength + stabilisation) &&
            sensitivity >= .25f && sensitivity <= 4f && minimumSize >= 0 && minimumSize <= 1 &&
            minimumStrength >= 0 && minimumStrength <= 1 && stabilisation >= 0 && stabilisation <= .95f &&
            lightPressureTrim >= 0 && lightPressureTrim <= 95 && firmPressureTrim >= 0 && firmPressureTrim <= 95 &&
            lightPressureTrim + firmPressureTrim <= 95 &&
            Enum.IsDefined(typeof(BrushPenDirection), direction);

        internal readonly struct Key : IEquatable<Key>
        {
            readonly (int model, float sensitivity, float minimumSize, float minimumStrength, BrushPenDirection direction,
                int lightTrim, int firmTrim) _value;
            internal Key(PaintStroke stroke)
            {
                var settings = stroke.pen;
                _value = (stroke.pressureModel, settings?.sensitivity ?? 1f, settings?.minimumSize ?? .15f,
                    settings?.minimumStrength ?? 0f, settings?.direction ?? BrushPenDirection.Brush,
                    settings?.lightPressureTrim ?? 0, settings?.firmPressureTrim ?? 0);
            }
            public bool Equals(Key other) => _value.Equals(other._value);
        }
    }
}
