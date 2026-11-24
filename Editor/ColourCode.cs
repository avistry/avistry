using System;
using System.Globalization;
using UnityEngine;

namespace Avistry.Textures
{
    static class ColourCode
    {
        internal static bool TryParse(string input, out Color colour, out bool hasAlpha)
        {
            colour = default;
            hasAlpha = false;
            string text = input?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                hasAlpha = true;
                return true;
            }

            int opening = text.IndexOf('(');
            if (opening < 0)
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = "#" + text.Substring(2);
                if (!text.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString("#" + text, out colour))
                {
                    hasAlpha = text.Length == 4 || text.Length == 8;
                    return true;
                }
                if (!ColorUtility.TryParseHtmlString(text.ToLowerInvariant(), out colour)) return false;
                hasAlpha = text[0] == '#' && (text.Length == 5 || text.Length == 9);
                return true;
            }
            if (!text.EndsWith(")", StringComparison.Ordinal)) return false;
            string format = text.Substring(0, opening).Trim().ToLowerInvariant();
            string body = text.Substring(opening + 1, text.Length - opening - 2);
            if (!ReadComponents(body, out var parts, out var alpha)) return false;
            float opacity = 1f;
            if (alpha != null && !ReadChannel(alpha, 1f, out opacity)) return false;

            switch (format)
            {
                case "rgb":
                case "rgba":
                case "color":
                    float range = format == "color" ? 1f : 255f;
                    if (!ReadChannel(parts[0], range, out float red) || !ReadChannel(parts[1], range, out float green) ||
                        !ReadChannel(parts[2], range, out float blue)) return false;
                    colour = new Color(red, green, blue);
                    break;
                case "hsl":
                case "hsla":
                case "hsv":
                case "hsva":
                    if (!ReadHue(parts[0], out float hue) || !ReadChannel(parts[1], 1f, out float saturation) ||
                        !ReadChannel(parts[2], 1f, out float last)) return false;
                    colour = format.StartsWith("hsl", StringComparison.Ordinal)
                        ? ColourMath.HSLToRGB(new Vector3(hue, saturation, last))
                        : Color.HSVToRGB(hue, saturation, last);
                    break;
                default: return false;
            }
            colour.a = opacity;
            hasAlpha = alpha != null;
            return true;
        }

        static bool ReadComponents(string body, out string[] components, out string alpha)
        {
            var groups = body.Split('/');
            alpha = groups.Length == 2 ? groups[1].Trim() : null;
            if (groups[0].Contains(","))
            {
                components = groups[0].Split(',');
                if (groups.Length == 1 && components.Length == 4)
                {
                    alpha = components[3].Trim();
                    Array.Resize(ref components, 3);
                }
            }
            else components = groups[0].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return groups.Length <= 2 && components.Length == 3;
        }

        static bool ReadChannel(string text, float numberRange, out float value)
        {
            text = text.Trim();
            bool percent = text.EndsWith("%", StringComparison.Ordinal);
            if (percent) text = text.Substring(0, text.Length - 1);
            if (!ReadNumber(text, out value)) return false;
            value /= percent ? 100f : numberRange;
            return value >= 0f && value <= 1f;
        }

        static bool ReadHue(string text, out float hue)
        {
            text = text.Trim();
            float turns;
            if (text.EndsWith("turn", StringComparison.OrdinalIgnoreCase)) { turns = 1f; text = text.Substring(0, text.Length - 4); }
            else if (text.EndsWith("grad", StringComparison.OrdinalIgnoreCase)) { turns = 400f; text = text.Substring(0, text.Length - 4); }
            else if (text.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) { turns = 2f * Mathf.PI; text = text.Substring(0, text.Length - 3); }
            else
            {
                turns = 360f;
                if (text.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 3);
            }
            if (!ReadNumber(text, out hue)) return false;
            hue = Mathf.Repeat(hue / turns, 1f);
            return true;
        }

        static bool ReadNumber(string text, out float number) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && float.IsFinite(number);
    }
}
