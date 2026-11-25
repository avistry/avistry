using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    internal readonly struct SceneCutout
    {
        static readonly Shader Standard = FindStandard();
        static readonly int Main = Shader.PropertyToID("_MainTex");
        static readonly int Transform = Shader.PropertyToID("_MainTex_ST");
        static readonly int Tint = Shader.PropertyToID("_Color");
        static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        const float AlphaMargin = 1f / 128f;

        readonly NativeArray<Color32> _pixels;
        readonly int _width, _height;
        readonly Vector4 _st;
        readonly float _tint, _cutoff;
        readonly bool _point, _repeatU, _repeatV;

        internal bool Active => _pixels.IsCreated;

        static Shader FindStandard()
        {
            var shader = Shader.Find("Standard");
            return shader != null && AssetDatabase.GetAssetPath(shader) == "Resources/unity_builtin_extra" ? shader : null;
        }

        internal static SceneCutout Read(Renderer renderer, int slot, Material material, ref MaterialPropertyBlock scratch)
        {
            if (Standard == null || material.shader != Standard || GraphicsSettings.currentRenderPipeline != null ||
                !material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_ALPHABLEND_ON") ||
                material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || material.IsKeywordEnabled("_PARALLAXMAP") ||
                material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A")) return default;

            MaterialPropertyBlock block = null;
            if (renderer.HasPropertyBlock())
            {
                block = scratch ??= new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);
                if (block.isEmpty) renderer.GetPropertyBlock(block);
                if ((block.HasProperty(Main) && !block.HasTexture(Main)) ||
                    (block.HasProperty(Transform) && !block.HasVector(Transform)) ||
                    (block.HasProperty(Tint) && !block.HasColor(Tint)) ||
                    (block.HasProperty(Cutoff) && !block.HasFloat(Cutoff))) return default;
            }

            var texture = block != null && block.HasTexture(Main) ? block.GetTexture(Main) : material.GetTexture(Main);
            if (!(texture is Texture2D image) || !image.isReadable || image.format != TextureFormat.RGBA32 || image.mipmapCount != 1 ||
                (image.filterMode != FilterMode.Point && image.filterMode != FilterMode.Bilinear) ||
                !SupportedWrap(image.wrapModeU) || !SupportedWrap(image.wrapModeV)) return default;

            var scale = material.GetTextureScale(Main);
            var offset = material.GetTextureOffset(Main);
            var st = block != null && block.HasVector(Transform) ? block.GetVector(Transform) : new Vector4(scale.x, scale.y, offset.x, offset.y);
            float tint = (block != null && block.HasColor(Tint) ? block.GetColor(Tint) : material.GetColor(Tint)).a;
            float cutoff = block != null && block.HasFloat(Cutoff) ? block.GetFloat(Cutoff) : material.GetFloat(Cutoff);
            if (!(tint >= 0f && tint <= 1f && cutoff > AlphaMargin && cutoff <= 1f) ||
                !float.IsFinite(st.x) || !float.IsFinite(st.y) || !float.IsFinite(st.z) || !float.IsFinite(st.w)) return default;

            return new SceneCutout(image, st, tint, cutoff);
        }

        SceneCutout(Texture2D image, Vector4 st, float tint, float cutoff)
        {
            _pixels = image.GetPixelData<Color32>(0);
            _width = image.width; _height = image.height;
            _st = st; _tint = tint; _cutoff = cutoff;
            _point = image.filterMode == FilterMode.Point;
            _repeatU = image.wrapModeU == TextureWrapMode.Repeat;
            _repeatV = image.wrapModeV == TextureWrapMode.Repeat;
        }

        static bool SupportedWrap(TextureWrapMode wrap) => wrap == TextureWrapMode.Clamp || wrap == TextureWrapMode.Repeat;

        internal bool Discards(Vector2 uv)
        {
            float u = uv.x * _st.x + _st.z, v = uv.y * _st.y + _st.w;
            if (!float.IsFinite(u) || !float.IsFinite(v) || Mathf.Abs(u) > 4096f || Mathf.Abs(v) > 4096f) return false;
            float x = (_repeatU ? u - Mathf.Floor(u) : Mathf.Clamp01(u)) * _width;
            float y = (_repeatV ? v - Mathf.Floor(v) : Mathf.Clamp01(v)) * _height;
            float alpha = _point ? SamplePoint(x, y) : SampleLinear(x - .5f, y - .5f);
            return alpha * (_tint / 255f) < _cutoff - AlphaMargin;
        }

        float SamplePoint(float x, float y)
        {
            int px = Mathf.FloorToInt(x), py = Mathf.FloorToInt(y);
            float fx = x - px, fy = y - py;
            const float edge = .001f;
            int otherX = fx < edge ? px - 1 : fx > 1f - edge ? px + 1 : px;
            int otherY = fy < edge ? py - 1 : fy > 1f - edge ? py + 1 : py;
            float alpha = At(px, py);
            if (otherX != px) alpha = Mathf.Max(alpha, At(otherX, py));
            if (otherY != py)
            {
                alpha = Mathf.Max(alpha, At(px, otherY));
                if (otherX != px) alpha = Mathf.Max(alpha, At(otherX, otherY));
            }
            return alpha;
        }

        float SampleLinear(float x, float y)
        {
            int px = Mathf.FloorToInt(x), py = Mathf.FloorToInt(y);
            float fx = x - px, fy = y - py;
            return Mathf.Lerp(Mathf.Lerp(At(px, py), At(px + 1, py), fx),
                Mathf.Lerp(At(px, py + 1), At(px + 1, py + 1), fx), fy);
        }

        byte At(int x, int y)
        {
            x = _repeatU ? (x + _width) % _width : Mathf.Clamp(x, 0, _width - 1);
            y = _repeatV ? (y + _height) % _height : Mathf.Clamp(y, 0, _height - 1);
            return _pixels[y * _width + x].a;
        }
    }
}
