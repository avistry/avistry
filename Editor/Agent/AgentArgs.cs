using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    internal class AgentArgs
    {
        internal const int MaxStrokePoints = 2048;
        internal const int MaxIds = 4096;
        internal const int MaxPiecesPerPage = 1024;
        internal const int DefaultPieces = 256;
        internal const int MinPictureSize = 256, MaxPictureSize = 1024, DefaultPictureSize = 512, MaxInlinePictureSize = 512;
        internal const int MaxWritesPerWindow = 20;
        internal const double WriteWindowSeconds = 10;

        public string action;

        public string part;
        public string partId;
        public int? materialSlot;
        public string texture;
        public string onLayers;
        public string tab;
        public string mode;
        public int[] ids;
        public string kind;
        public string name;
        public string layerId;
        public bool? enabled;
        public float? opacity;
        public string blend;
        public string maskMode;
        public bool? clip;
        public Dictionary<string, object> fields;
        public int? toIndex;
        public float? hue;
        public float? saturation;
        public float? brightness;
        public string colour;
        public int? swatch;
        public bool? matchBrightness;
        public float? strength;
        public float? size;
        public float? hardness;
        public Vector2[] points;
        public float? pressure;
        public Vector2? gradientFrom, gradientTo;
        public float? u, v;
        public int? index;
        public int? offset, max;
        public string concept, query;
        public bool? use;
    }
}
