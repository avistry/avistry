using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class PoiyomiTileDiscard
    {
        const string Enabled = "_UVTileDissolveEnabled";
        const string AtMax = "_UVTileDissolveDiscardAtMax";
        const string Channel = "_UVTileDissolveUV";
        const float Threshold = .999f;

        static readonly string[] PropertyNames = PropertyList();
        static readonly int EnabledId = Shader.PropertyToID(Enabled);
        static readonly Binding[] UnlockedBindings = Array.ConvertAll(PropertyNames, name => new Binding(Shader.PropertyToID(name)));
        static readonly Dictionary<Shader, Binding[]> LockedBindings = new Dictionary<Shader, Binding[]>();
        static readonly Regex Programs = new Regex(@"(?ms)^[ \t]*CGPROGRAM\s*$(?<code>.*?)^\s*ENDCG\b");
        static readonly Regex Distort = new Regex(@"(?m)^\s*#define[ \t]+DISTORT\b");
        static readonly Regex VertexRule = new Regex(
            @"(?ms)^[ \t]*if\s*\((?<flags>[^\r\n]*_UVTileDissolveEnabled[^\r\n]*&&[^\r\n]*_UVTileDissolveDiscardAtMax[^\r\n]*)\)\s*\{\s*float2\s+dissolveUdim\s*=\s*0\s*;" +
            @"(?<tiles>[^{}]+?)const float threshold\s*=\s*0\.999\s*;\s*if\s*\(isDiscardedFromDissolve\s*>\s*threshold\)[^{]*\{\s*return\s*\(VertexOut\)POI_NAN\s*;");

        static PoiyomiTileDiscard() => EditorApplication.projectChanged += LockedBindings.Clear;

        static string[] PropertyList()
        {
            var names = new string[19];
            names[0] = Enabled; names[1] = AtMax; names[2] = Channel;
            for (int i = 0; i < 16; i++) names[i + 3] = $"_UVTileDissolveAlpha_Row{i / 4}_{i % 4}";
            return names;
        }

        readonly struct Binding : IEquatable<Binding>
        {
            readonly int _id;
            readonly float _constant;
            readonly bool _live;

            internal Binding(int id) { _id = id; _constant = 0f; _live = true; }
            internal Binding(float value) { _id = 0; _constant = value; _live = false; }
            public bool Equals(Binding other) => _id == other._id && _constant == other._constant && _live == other._live;

            internal float Read(Material material, MaterialPropertyBlock block)
            {
                if (!_live) return _constant;
                if (block != null && block.HasFloat(_id)) return block.GetFloat(_id);
                if (block != null && block.HasInteger(_id)) return block.GetInteger(_id);
                return material.HasInteger(_id) ? material.GetInteger(_id) : material.HasProperty(_id) ? material.GetFloat(_id) : float.NaN;
            }
        }

        internal static bool TryRead(Renderer renderer, int slot, Material material, ref MaterialPropertyBlock scratch,
                                     out int channel, out int tiles)
        {
            channel = 0;
            tiles = 0;
            if (!material.HasProperty(EnabledId) || material.shader == null) return false;
            var shader = material.shader;
            string name = shader.name;
            const string lockedPrefix = "Hidden/Locked/";
            bool locked = name.StartsWith(lockedPrefix, StringComparison.OrdinalIgnoreCase);
            var sourceName = locked ? name.Substring(lockedPrefix.Length) : name;
            if (!sourceName.StartsWith(".poiyomi/Poiyomi 8.", StringComparison.OrdinalIgnoreCase) &&
                !sourceName.StartsWith(".poiyomi/Poiyomi 9.", StringComparison.OrdinalIgnoreCase) &&
                !sourceName.StartsWith(".poiyomi/Old Versions/8.", StringComparison.OrdinalIgnoreCase) &&
                !sourceName.StartsWith(".poiyomi/Old Versions/9.", StringComparison.OrdinalIgnoreCase)) return false;

            var bindings = UnlockedBindings;
            if (locked)
            {
                if (!LockedBindings.TryGetValue(shader, out bindings))
                    LockedBindings[shader] = bindings = ReadLocked(shader);
                if (bindings == null) return false;
            }
            else if (!material.IsKeywordEnabled("DISTORT")) return false;

            MaterialPropertyBlock block = null;
            if (renderer.HasPropertyBlock())
            {
                block = scratch ??= new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);
                if (block.isEmpty) renderer.GetPropertyBlock(block);
            }
            if (bindings[0].Read(material, block) != 1f || bindings[1].Read(material, block) != 1f) return false;
            float uv = bindings[2].Read(material, block);
            if (!(uv >= 0f && uv <= 3f) || uv != (int)uv) return false;
            channel = (int)uv;
            int discarded = 0;
            for (int i = 0; i < 16; i++)
            {
                float amount = bindings[i + 3].Read(material, block);
                if (!float.IsFinite(amount)) return false;
                if (amount > Threshold) discarded |= 1 << i;
            }
            tiles = discarded;
            return discarded != 0;
        }

        static Binding[] ReadLocked(Shader shader)
        {
            string path = AssetDatabase.GetAssetPath(shader);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) return null;
            string source;
            try { source = File.ReadAllText(Path.Combine(Application.dataPath, path.Substring("Assets/".Length))); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            if (source.IndexOf("UsePass", StringComparison.Ordinal) >= 0 ||
                source.IndexOf("HLSLPROGRAM", StringComparison.Ordinal) >= 0) return null;

            Binding[] result = null;
            foreach (Match pass in Programs.Matches(source))
            {
                string code = pass.Groups["code"].Value;
                if (!Distort.IsMatch(code)) return null;
                var rule = VertexRule.Match(code);
                if (!rule.Success) return null;
                string vertex = rule.Groups["flags"].Value + rule.Groups["tiles"].Value;
                var bindings = new Binding[PropertyNames.Length];
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!TryReadBinding(shader, vertex, PropertyNames[i], out bindings[i])) return null;
                    if (result != null && !bindings[i].Equals(result[i])) return null;
                }
                result ??= bindings;
            }
            return result;
        }

        static bool TryReadBinding(Shader shader, string vertex, string property, out Binding binding)
        {
            binding = default;
            var literal = Regex.Match(vertex, @"(?<value>[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)[fF]?\s*/\*\s*" + property + @"\s*\*/");
            if (literal.Success)
            {
                if (!float.TryParse(literal.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    !float.IsFinite(value)) return false;
                binding = new Binding(value);
                return true;
            }
            var uniform = Regex.Match(vertex, @"\b" + property + @"(?:_[A-Za-z0-9_]+)?\b");
            if (!uniform.Success || shader.FindPropertyIndex(uniform.Value) < 0) return false;
            binding = new Binding(Shader.PropertyToID(uniform.Value));
            return true;
        }

        internal readonly struct TriangleFilter
        {
            readonly ushort[] _tiles;
            readonly int _discarded;
            readonly int _firstTriangle;
            internal readonly bool AllDiscarded;

            internal TriangleFilter(ushort[] tiles, int discarded, int firstTriangle, bool allDiscarded)
            {
                _tiles = tiles;
                _discarded = discarded;
                _firstTriangle = firstTriangle;
                AllDiscarded = allDiscarded;
            }

            internal bool Discards(int triangle) => _tiles != null && (_tiles[triangle - _firstTriangle] & _discarded) != 0;
        }

        internal static ushort Tile(Vector2 uv)
        {
            if (!(uv.x >= 0 && uv.x < 4 && uv.y >= 0 && uv.y < 4)) return 0;
            return (ushort)(1 << ((int)uv.y * 4 + (int)uv.x));
        }
    }
}
