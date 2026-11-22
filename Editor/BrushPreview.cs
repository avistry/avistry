using System;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Renders a sample S-shaped stroke with the current brush for the panel (re-rendered only when the brush changes).</summary>
    sealed class BrushPreview : IDisposable
    {
        BrushRenderer _renderer;
        RenderTexture _rt;
        long _key = long.MinValue;
        bool _hasKey;

        public Texture Get(BrushSettings b, int w, int h)
        {
            w = Mathf.Max(16, w);
            h = Mathf.Max(8, h);
            long key = Hash(b, w, h);
            if (_rt != null && _rt.IsCreated() && _hasKey && key == _key) return _rt;
            _key = key;
            _hasKey = true;

            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                Release();
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                _rt.Create();
            }
            if (_renderer == null) _renderer = new BrushRenderer();

            // Show the brush at a readable size: big brushes are scaled down to fit the strip.
            float diameter = Mathf.Clamp(b.size, 3f, h * 0.55f);
            float scale = diameter / Mathf.Max(1f, b.size);
            var s = b.NewStroke(1);
            s.mode = BrushMode.Paint;
            s.radius = diameter * 0.5f / w;
            s.seed = 12345;
            s.clip = false;
            const int n = 64;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float x = Mathf.Lerp(0.07f, 0.93f, t);
                float y = 0.5f + 0.22f * Mathf.Sin(t * Mathf.PI * 2f);
                float pressure = Mathf.Max(0.15f, Mathf.Sin(t * Mathf.PI));
                s.points.Add(new Vector4(x, y, pressure, i == 0 ? 0 : 1));
            }

            var prev = RenderTexture.active;
            try
            {
                float grainPx = Mathf.Max(0.5f, (s.shape?.grainSize ?? 6f) * scale);
                _renderer.RenderCoverage(s, _rt, w / grainPx);
            }
            finally { RenderTexture.active = prev; }
            return _rt;
        }

        // Every field that reaches the rendered stroke (BrushSettings.NewStroke + BrushShape).
        static long Hash(BrushSettings b, int w, int h)
        {
            unchecked
            {
                ulong x = 1469598103934665603UL;
                void Add(float v) { x = (x ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL; }
                Add(w); Add(h);
                Add(b.size); Add(b.opacity); Add((int)b.mode);
                Add(b.pressureSize ? 1 : 0); Add(b.pressureOpacity ? 1 : 0);
                Add(b.fillTool ? 1 : 0); Add(b.blendTool ? 1 : 0); Add((int)b.blendKind);
                Add(b.colorMixing ? 1 : 0); Add(b.amountOfPaint); Add(b.colorStretch); Add(b.blendAmount);
                Add(b.blendKeepIsland ? 1 : 0); Add(b.stayInsideIslands ? 1 : 0);
                Add(b.color.r); Add(b.color.g); Add(b.color.b);
                var sh = b.shape;
                if (sh == null) { Add(-1); return (long)x; }
                Add((int)sh.tip); Add(sh.hardness); Add(sh.spacing); Add(sh.buildUp ? 1 : 0); Add(sh.flow);
                Add(sh.roundness); Add(sh.angle); Add(sh.followStroke ? 1 : 0); Add(sh.grain); Add(sh.grainSize);
                Add(sh.scatter); Add(sh.sizeJitter); Add(sh.opacityJitter); Add(sh.density);
                Add(sh.colorJitter ? 1 : 0); Add(sh.jitterHue); Add(sh.jitterSaturation); Add(sh.jitterLuminosity); Add(sh.jitterSubBlend);
                if (sh.colorJitter) { Add(b.subColor.r); Add(b.subColor.g); Add(b.subColor.b); }
                return (long)x;
            }
        }

        void Release()
        {
            if (_rt == null) return;
            _rt.Release();
            UnityEngine.Object.DestroyImmediate(_rt);
            _rt = null;
        }

        public void Dispose()
        {
            Release();
            _renderer?.Dispose();
            _renderer = null;
            _hasKey = false;
        }
    }
}
