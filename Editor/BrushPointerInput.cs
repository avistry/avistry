using UnityEngine;

namespace Avistry.Textures
{
    internal struct BrushPointerInput
    {
        Vector2 _position, _rawPosition;
        float _pressure;
        double _time;
        bool _ready;

        internal static float Pressure(PointerType pointer, float value) => pointer == PointerType.Pen
            ? float.IsFinite(value) ? Mathf.Clamp01(value) : 0f : 1f;

        internal (Vector2 position, float pressure) Filter(Vector2 position, float pressure, double time, float amount, bool start)
        {
            if (!float.IsFinite(position.x + position.y + pressure)) return (_position, _pressure);
            amount = float.IsFinite(amount) ? Mathf.Clamp(amount, 0f, .95f) : 0f;
            if (!_ready || start || amount <= 0)
            {
                _ready = true; _position = _rawPosition = position; _pressure = pressure; _time = time;
                return (position, pressure);
            }
            float seconds = Mathf.Clamp((float)(time - _time), .001f, .05f);
            float speed = Vector2.Distance(position, _rawPosition) / seconds;
            float cutoff = Mathf.Lerp(32f, 3.5f, amount) + speed * Mathf.Lerp(.18f, .025f, amount);
            float positionBlend = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff * seconds);
            float pressureBlend = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Lerp(35f, 8f, amount) * seconds);
            _position = Vector2.Lerp(_position, position, positionBlend);
            _pressure = Mathf.Lerp(_pressure, pressure, pressureBlend);
            _rawPosition = position; _time = time;
            return (_position, _pressure);
        }
    }
}
