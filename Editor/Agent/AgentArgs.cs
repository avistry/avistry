using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The parameters of one AI helper call. Every field is optional: null means "not given". The optional bridge fills
    /// it from the tool call and tests fill it directly, so the facade never sees JSON.
    /// </summary>
    internal class AgentArgs
    {
        internal const int MaxStrokePoints = 2048;
        internal const int MaxIds = 4096;
        internal const int MaxPiecesPerPage = 1024;
        internal const int DefaultPieces = 256;
        internal const int MinPictureSize = 256, MaxPictureSize = 1024, DefaultPictureSize = 512, MaxInlinePictureSize = 512;
        internal const int MaxWritesPerWindow = 20;
        internal const double WriteWindowSeconds = 10;

        /// <summary>One of a fixed list of words. MCP for Unity's own usage data records it, so it never carries user content.</summary>
        public string action;

        public string part;           // a part's name
        public string partId;         // a part's GlobalObjectId string
        public int? materialSlot;
        public string texture;        // a texture's name among the current material's slots
        public string onLayers;       // "keep" or "cancel" when changing target with layers on it
        public string tab;            // a Tab enum name, never a number
        public string mode;           // select: set, add, remove, clear, invert, all. brush: brush or eraser
        public int[] ids;             // piece ids
        public string kind;           // layer kind word
        public string name;
        public string layerId;
        public bool? enabled;
        public float? opacity;        // 0 to 1
        public string blend;          // as in the Blend menu
        public string maskMode;
        public bool? clip;
        public Dictionary<string, object> fields; // whitelisted per layer kind: string, bool, long or double values
        public int? toIndex;
        public float? hue;            // degrees, -180 to 180
        public float? saturation;     // -1 to 1
        public float? brightness;     // -1 to 1
        public string colour;         // "#RRGGBB"
        public int? swatch;
        public bool? matchBrightness;
        public float? strength;       // 0 to 1
        public float? size;           // brush px, or picture size px
        public float? hardness;       // 0 to 1
        public Vector2[] points;      // stroke points in UV space
        public float? pressure;       // 0 to 1
        public Vector2? gradientFrom, gradientTo;
        public float? u, v;
        public int? index;
        public int? offset, max;
        public string concept, query; // glossary lookups
        public bool? use;             // make_normal_map and friends: also apply to the material
    }
}
