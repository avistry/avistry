using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Runs the layer stack on the GPU.
    /// Colour-space rule (the only place it's decided): every working texture holds the RAW file values
    /// (what is stored in the PNG). Only the preview RT converts, so it samples like the imported texture.
    /// </summary>
    public class Compositor : IDisposable
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public RenderTexture Result { get; private set; }   // raw values, ARGBHalf, linear (no conversion)
        public RenderTexture Preview { get; private set; }  // what materials/canvas sample
        public RenderTexture SourcePreview { get; private set; }
        public string SourceNote { get; private set; }
        /// <summary>Width of the full-resolution source (brush sizes are given in these pixels).</summary>
        public int SourceWidth { get; private set; }

        /// <summary>The stroke being drawn right now (not yet in the layer), shown on top of that layer's paint.</summary>
        public PaintStroke LiveStroke;
        public string LiveLayerId;
        /// <summary>Symmetry: the mirrored half of the stroke being drawn (same layer), shown on top of LiveStroke.</summary>
        public PaintStroke LiveMirror;
        RenderTexture _liveRT2;

        RenderTexture _source, _pingA, _pingB;
        Material _layerMat, _utilMat;
        bool _previewIsSRGB;
        readonly Dictionary<string, (long key, Texture2D tex)> _masks = new Dictionary<string, (long, Texture2D)>();
        readonly Dictionary<GradientMode, Texture2D> _gradients = new Dictionary<GradientMode, Texture2D>();
        readonly Dictionary<string, Texture2D> _luts = new Dictionary<string, Texture2D>();
        Texture2D _white;

        static Material LoadMat(string shader)
        {
            var s = Shader.Find(shader);
            if (s == null) throw new Exception($"Shader '{shader}' not found. Is the Avistry package complete?");
            return new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        }

        /// <param name="maxSize">Working size cap (0 = full source resolution).</param>
        public Compositor(TextureSlot slot, int maxSize)
        {
            Texture2D raw = null;
            bool fromFile = false;
            try
            {
                _layerMat = LoadMat("Hidden/Avistry/Layer");
                _utilMat = LoadMat("Hidden/Avistry/Util");
                _white = Texture2D.whiteTexture;

                raw = LoadRawSource(slot, out var note, out fromFile);
                SourceNote = note;
                int w = raw.width, h = raw.height;
                SourceWidth = w;
                if (maxSize > 0 && Mathf.Max(w, h) > maxSize)
                {
                    float s = maxSize / (float)Mathf.Max(w, h);
                    w = Mathf.Max(1, Mathf.RoundToInt(w * s));
                    h = Mathf.Max(1, Mathf.RoundToInt(h * s));
                }
                Width = w; Height = h;

                using (new KeepActiveRT())
                    CreateTargets(slot, raw, fromFile, w, h);
            }
            catch
            {
                if (fromFile && raw != null) UnityEngine.Object.DestroyImmediate(raw);
                Dispose(); // free whatever was created before the failure
                throw;
            }
        }

        /// <summary>
        /// Graphics.Blit leaves RenderTexture.active pointing at its destination. If that leaks out of OnGUI,
        /// the editor draws its own window into our texture, so every GPU entry point restores it.
        /// </summary>
        sealed class KeepActiveRT : IDisposable
        {
            readonly RenderTexture _prev = RenderTexture.active;
            public void Dispose() => RenderTexture.active = _prev;
        }

        void CreateTargets(TextureSlot slot, Texture2D raw, bool fromFile, int w, int h)
        {
            _source = NewRT(w, h, RenderTextureFormat.ARGBHalf, false, false);
            if (fromFile) Graphics.Blit(raw, _source);
            else
            {
                // Imported texture is hardware-decoded when sRGB; re-encode to get raw values back.
                _utilMat.SetFloat("_Convert", slot.importedAsSRGB && IsLinearProject ? 1 : 0);
                Graphics.Blit(raw, _source, _utilMat, 0);
            }
            if (fromFile) UnityEngine.Object.DestroyImmediate(raw);

            _pingA = NewRT(w, h, RenderTextureFormat.ARGBHalf, false, false);
            _pingB = NewRT(w, h, RenderTextureFormat.ARGBHalf, false, false);
            Result = NewRT(w, h, RenderTextureFormat.ARGBHalf, false, false);

            _previewIsSRGB = slot.importedAsSRGB && IsLinearProject;
            Preview = NewRT(w, h, RenderTextureFormat.ARGB32, _previewIsSRGB, true);
            SourcePreview = NewRT(w, h, RenderTextureFormat.ARGB32, _previewIsSRGB, true);
            CopyFilterSettings(slot.texture, Preview);
            CopyFilterSettings(slot.texture, SourcePreview);
            ToPreview(_source, SourcePreview);
        }

        static bool IsLinearProject => QualitySettings.activeColorSpace == ColorSpace.Linear;

        static bool GraphicsFormatIsSRGB(Texture t) => t != null && UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);

        static RenderTexture NewRT(int w, int h, RenderTextureFormat fmt, bool srgb, bool mips)
        {
            var desc = new RenderTextureDescriptor(w, h, fmt, 0)
            {
                sRGB = srgb,
                useMipMap = mips,
                autoGenerateMips = mips,
                msaaSamples = 1,
            };
            var rt = new RenderTexture(desc) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            rt.Create();
            return rt;
        }

        static void CopyFilterSettings(Texture src, RenderTexture dst)
        {
            dst.wrapMode = src.wrapMode;
            dst.filterMode = src.filterMode;
            dst.anisoLevel = Mathf.Max(src.anisoLevel, 1);
        }

        /// <summary>
        /// Full-resolution raw pixels. PNG/JPG are read from disk (lossless, ignores import downscaling/compression);
        /// other formats fall back to the imported texture.
        /// </summary>
        static Texture2D LoadRawSource(TextureSlot slot, out string note, out bool fromFile)
        {
            string ext = Path.GetExtension(slot.assetPath).ToLowerInvariant();
            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && File.Exists(slot.assetPath))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                bool loaded = false;
                try { loaded = t.LoadImage(File.ReadAllBytes(slot.assetPath), false); }
                catch (Exception e) { Debug.LogWarning($"Avistry: could not read '{slot.assetPath}' ({e.Message}); using the imported texture instead."); }
                if (loaded)
                {
                    note = $"Source: {Path.GetFileName(slot.assetPath)} at full resolution {t.width}×{t.height}";
                    fromFile = true;
                    return t;
                }
                UnityEngine.Object.DestroyImmediate(t);
            }
            note = $"Source is {ext}: using the imported copy ({slot.texture.width}×{slot.texture.height}, may be compressed). PNG sources give the best quality.";
            fromFile = false;
            return slot.texture;
        }

        void ToPreview(RenderTexture raw, RenderTexture dst)
        {
            _utilMat.SetFloat("_Convert", _previewIsSRGB ? 1 : 0);
            Graphics.Blit(raw, dst, _utilMat, 1);
        }


        public void Compose(IList<Layer> layers, MeshUV uv)
        {
            if (layers is List<Layer> own) TextureRecipe.DropNullLayers(own, "compose"); // also runs before a bake or save copies the recipe
            using (new KeepActiveRT()) ComposeInternal(layers, uv);
        }

        /// <summary>Paint-cache key of a layer's painted mask (the live stroke targets it via LiveLayerId).</summary>
        public static string MaskKey(string layerId) => layerId + ":mask";

        RenderTexture _coverageRT, _maskOverlayRT;
        Texture2D _clear;

        Texture2D Clear
        {
            get
            {
                if (_clear != null) return _clear;
                _clear = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                _clear.SetPixel(0, 0, Color.clear);
                _clear.Apply(false);
                return _clear;
            }
        }

        /// <summary>
        /// Layers are applied bottom (index 0) to top. Clip to layer below: a clipped layer is limited to the coverage of
        /// the nearest unclipped layer under it (its "base"), like CLIP STUDIO PAINT / Photoshop clipping groups. The base's
        /// coverage (opacity × masks × its own alpha) is rendered once, right after the base, when a clipped layer follows.
        /// A hidden base hides its clipped layers too. A clipped layer with nothing below it is drawn normally.
        /// </summary>
        void ComposeInternal(IList<Layer> layers, MeshUV uv)
        {
            var current = _source;
            var target = _pingA;
            _layerMat.SetFloat("_Aspect", Width / (float)Mathf.Max(1, Height)); // drawn gradients work in pixel proportions
            _layerMat.SetFloat("_CoverageOut", 0);
            bool haveBase = false, baseVisible = false;
            _sigSb.Clear();
            int sigDone = 0; // layers [0, sigDone) are already in _sigSb (BelowSignature, built once per pass)
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null) continue;
                bool clipped = layer.clipToBelow && haveBase;
                if (!layer.clipToBelow) { haveBase = true; baseVisible = false; }
                if (clipped && !baseVisible) continue;
                if (!layer.enabled || layer.opacity <= 0) continue;

                _layerMat.SetFloat("_Opacity", layer.opacity);
                _layerMat.SetFloat("_Blend", (int)layer.blend);
                _layerMat.SetFloat("_InvertMask", layer.invertMask ? 1 : 0);
                _layerMat.SetVector("_ChannelMask", new Vector4(layer.chR ? 1 : 0, layer.chG ? 1 : 0, layer.chB ? 1 : 0, layer.chA ? 1 : 0));
                _layerMat.SetTexture("_MaskTex", MaskFor(layer, uv));
                _layerMat.SetTexture("_GradTex", layer.NeedsGradientTexture ? GradientFor(layer.GradientModeUsed, uv) : (Texture)Texture2D.blackTexture);
                if (layer is PaintLayer paint)
                {
                    bool live = LiveStroke != null && LiveLayerId == paint.id;
                    if (paint.strokes.Count == 0 && !live) continue;
                    string belowKey = null;
                    if (HasBlendStrokes(paint.strokes, live ? LiveStroke : null))
                    {
                        while (sigDone < li) AppendBelowSignature(_sigSb, layers[sigDone++]);
                        belowKey = _sigSb.ToString();
                    }
                    _layerMat.SetTexture("_PaintTex", PaintFor(paint, uv, current, belowKey));
                }
                BindPaintMask(layer, uv);
                _layerMat.SetTexture("_ClipTex", clipped ? (Texture)_coverageRT : _white);
                layer.Apply(_layerMat, g => LutFor(layer.id, g));
                if (layer is ToneCurveLayer curve) _layerMat.SetTexture("_LUT", CurveLut(curve));
                if (layer is ImageLayer img)
                {
                    if (img.image == null) continue;
                    _layerMat.SetTexture("_ImageTex", img.image);
                    _layerMat.SetTexture("_ImageMaskTex", img.mask != null ? (Texture)img.mask : _white);
                    _layerMat.SetFloat("_ImageConvert", IsLinearProject && GraphicsFormatIsSRGB(img.image) ? 1 : 0);
                    _layerMat.SetFloat("_MaskConvert", img.mask != null && IsLinearProject && GraphicsFormatIsSRGB(img.mask) ? 1 : 0);
                }

                Graphics.Blit(current, target, _layerMat, layer.Pass);
                if (!layer.clipToBelow)
                {
                    baseVisible = true;
                    if (NextIsClipped(layers, li))
                    {
                        EnsureCoverageRT();
                        _layerMat.SetFloat("_CoverageOut", 1);
                        Graphics.Blit(current, _coverageRT, _layerMat, layer.Pass); // same input as the layer itself
                        _layerMat.SetFloat("_CoverageOut", 0);
                    }
                }
                current = target;
                target = target == _pingA ? _pingB : _pingA;
            }
            Graphics.Blit(current, Result);
            ToPreview(Result, Preview);
            if (layers.Count > 0) EvictDeadLayers(layers); // an empty list is the view-only normal-map case: keep the caches
        }

        readonly System.Text.StringBuilder _sigSb = new System.Text.StringBuilder(256);
        readonly HashSet<string> _liveIds = new HashSet<string>();
        readonly List<string> _deadKeys = new List<string>();

        /// <summary>Frees the per-layer caches (paint, masks, LUTs) of layers that no longer exist.</summary>
        void EvictDeadLayers(IList<Layer> layers)
        {
            _liveIds.Clear();
            foreach (var l in layers) if (l != null) _liveIds.Add(l.id);

            _deadKeys.Clear();
            foreach (var k in _paint.Keys) if (!IsLiveCacheKey(k)) _deadKeys.Add(k);
            foreach (var k in _deadKeys) { Release(_paint[k].rt); _paint.Remove(k); }

            _deadKeys.Clear();
            foreach (var k in _masks.Keys) if (!IsLiveCacheKey(k)) _deadKeys.Add(k);
            foreach (var k in _deadKeys)
            {
                if (_masks[k].tex != null) UnityEngine.Object.DestroyImmediate(_masks[k].tex);
                _masks.Remove(k);
            }

            _deadKeys.Clear();
            foreach (var k in _luts.Keys) if (!IsLiveCacheKey(k)) _deadKeys.Add(k);
            foreach (var k in _deadKeys)
            {
                if (_luts[k] != null) UnityEngine.Object.DestroyImmediate(_luts[k]);
                _luts.Remove(k);
                _lutHash.Remove(k);
            }
        }

        /// <summary>Cache keys are a layer id, optionally followed by ":mask" or ":curve".</summary>
        bool IsLiveCacheKey(string key)
        {
            if (_liveIds.Contains(key)) return true;
            int c = key.LastIndexOf(':');
            return c > 0 && _liveIds.Contains(key.Substring(0, c));
        }

        static bool NextIsClipped(IList<Layer> layers, int i)
        {
            for (int j = i + 1; j < layers.Count; j++)
                if (layers[j] != null) return layers[j].clipToBelow;
            return false;
        }

        void EnsureCoverageRT()
        {
            if (_coverageRT != null) return;
            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            _coverageRT = NewRT(Width, Height, fmt, false, false);
        }

        /// <summary>Binds the layer's painted mask (or turns it off). Mask strokes are cached and replayed like paint.</summary>
        void BindPaintMask(Layer layer, MeshUV uv)
        {
            if (!layer.hasPaintMask) { _layerMat.SetFloat("_HideMode", 0); return; }
            _layerMat.SetTexture("_HideTex", MaskStrokes(layer, uv));
            _layerMat.SetFloat("_HideMode", layer.paintMaskStartsHidden ? 2 : 1);
        }

        Texture MaskStrokes(Layer layer, MeshUV uv)
        {
            string key = MaskKey(layer.id);
            bool live = LiveStroke != null && LiveLayerId == key;
            if (layer.maskStrokes == null || (layer.maskStrokes.Count == 0 && !live)) return Clear;
            return StrokesTexture(key, layer.maskStrokes, uv);
        }

        /// <summary>The layer's painted mask as a canvas overlay: hidden areas in translucent red.</summary>
        public RenderTexture MaskOverlay(Layer layer, MeshUV uv)
        {
            if (layer == null || !layer.hasPaintMask) return null;
            using (new KeepActiveRT())
            {
                if (_maskOverlayRT == null) _maskOverlayRT = NewRT(Width, Height, RenderTextureFormat.ARGB32, false, false);
                var src = MaskStrokes(layer, uv);
                _utilMat.SetFloat("_Convert", layer.paintMaskStartsHidden ? 1 : 0);
                Graphics.Blit(src, _maskOverlayRT, _utilMat, 3);
            }
            return _maskOverlayRT;
        }

        Texture MaskFor(Layer layer, MeshUV uv)
        {
            if (layer.maskMode == MaskMode.WholeTexture || uv == null) return _white;
            long key = MaskSignature(layer.islands);
            if (_masks.TryGetValue(layer.id, out var cached) && cached.key == key && cached.tex != null) return cached.tex;
            if (cached.tex != null) UnityEngine.Object.DestroyImmediate(cached.tex);

            int dilate = Mathf.Max(1, Width / 1024);
            var bytes = uv.RasterIslandMask(Width, Height, new HashSet<int>(layer.islands), dilate);
            var tex = new Texture2D(Width, Height, TextureFormat.R8, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false);
            _masks[layer.id] = (key, tex);
            return tex;
        }

        /// <summary>Order-independent hash of the working size and the island set (cheap stand-in for a joined string).</summary>
        long MaskSignature(List<int> islands)
        {
            ulong sum = 0, xor = 0;
            foreach (int i in islands)
            {
                ulong x = (ulong)(uint)i * 0x9E3779B97F4A7C15UL;
                x ^= x >> 29; x *= 0xBF58476D1CE4B9FFUL; x ^= x >> 32;
                unchecked { sum += x; }
                xor ^= x;
            }
            return unchecked((long)(sum * 31 + xor + (ulong)islands.Count * 0x100000001B3UL + (ulong)Width * 73856093UL + (ulong)Height * 19349663UL));
        }

        Texture GradientFor(GradientMode mode, MeshUV uv)
        {
            if (uv == null) return Texture2D.blackTexture;
            if (_gradients.TryGetValue(mode, out var tex) && tex != null) return tex;
            var t = uv.ComputeGradientT(mode);
            var values = uv.RasterGradient(Width, Height, t, Mathf.Max(4, Width / 128));
            bool r16 = SystemInfo.SupportsTextureFormat(TextureFormat.R16);
            tex = new Texture2D(Width, Height, r16 ? TextureFormat.R16 : TextureFormat.RFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            if (r16) tex.SetPixelData(values, 0);
            else tex.SetPixelData(values.Select(v => v / 65535f).ToArray(), 0);
            tex.Apply(false);
            _gradients[mode] = tex;
            return tex;
        }

        static readonly Color32[] _lutBuf = new Color32[256];
        readonly Dictionary<string, long> _lutHash = new Dictionary<string, long>(); // cache key -> hash of what the LUT was baked from

        static void HashAdd(ref ulong x, float v) { unchecked { x = (x ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL; } }

        static ulong HashCurve(ulong seed, AnimationCurve c)
        {
            ulong x = seed;
            if (c == null) { HashAdd(ref x, -1); return x; }
            var keys = c.keys;
            HashAdd(ref x, keys.Length);
            foreach (var k in keys) { HashAdd(ref x, k.time); HashAdd(ref x, k.value); HashAdd(ref x, k.inTangent); HashAdd(ref x, k.outTangent); HashAdd(ref x, k.inWeight); HashAdd(ref x, k.outWeight); HashAdd(ref x, (int)k.weightedMode); }
            HashAdd(ref x, (int)c.preWrapMode); HashAdd(ref x, (int)c.postWrapMode);
            return x;
        }

        static long HashGradient(Gradient g)
        {
            ulong x = 1469598103934665603UL;
            if (g == null) return (long)x;
            HashAdd(ref x, (int)g.mode);
            foreach (var k in g.colorKeys) { HashAdd(ref x, k.time); HashAdd(ref x, k.color.r); HashAdd(ref x, k.color.g); HashAdd(ref x, k.color.b); }
            foreach (var k in g.alphaKeys) { HashAdd(ref x, k.time); HashAdd(ref x, k.alpha); }
            return (long)x;
        }

        /// <summary>Tone Curve lookup table (re-baked only when the curve keys change).</summary>
        Texture2D CurveLut(ToneCurveLayer layer)
        {
            string id = layer.id + ":curve";
            if (!_luts.TryGetValue(id, out var lut) || lut == null)
            {
                lut = new Texture2D(256, 1, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                _luts[id] = lut;
                _lutHash.Remove(id); // fresh texture: must be baked
            }
            long h = (long)HashCurve(HashCurve(HashCurve(HashCurve(1469598103934665603UL, layer.rgb), layer.red), layer.green), layer.blue);
            if (_lutHash.TryGetValue(id, out var old) && old == h) return lut;
            lut.SetPixels32(layer.BuildLut());
            lut.Apply(false);
            _lutHash[id] = h;
            return lut;
        }

        Texture2D LutFor(string id, Gradient g)
        {
            if (!_luts.TryGetValue(id, out var lut) || lut == null)
            {
                lut = new Texture2D(256, 1, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                _luts[id] = lut;
                _lutHash.Remove(id); // fresh texture: must be baked
            }
            long h = HashGradient(g);
            if (_lutHash.TryGetValue(id, out var old) && old == h) return lut;
            for (int i = 0; i < 256; i++) _lutBuf[i] = g.Evaluate(i / 255f); // gradient colours are gamma values: stored as-is
            lut.SetPixels32(_lutBuf);
            lut.Apply(false);
            _lutHash[id] = h;
            return lut;
        }


        class PaintCache
        {
            public RenderTexture rt;
            public readonly List<PaintStroke> rendered = new List<PaintStroke>();
            public string belowKey; // layers with Blur / Smudge strokes: what was below when they were rendered
            public int poseVersion; // layers with 3D-brush strokes: the pose they were rendered in
        }

        RenderTexture _dirRT;

        /// <summary>What the last live-stroke raster was made from, so an unchanged live stroke is not re-rasterised.</summary>
        class LiveRaster
        {
            public PaintCache cache; public RenderTexture rt, result;
            public PaintStroke stroke, mirror;
            public int renderedCount, poseVersion, count, mirrorCount;
        }
        LiveRaster _liveDone;

        static bool HasSurfaceStrokes(List<PaintStroke> strokes, PaintStroke live)
        {
            if (live != null && live.surface) return true;
            foreach (var s in strokes) if (s != null && s.surface) return true;
            return false;
        }

        static bool HasBlendStrokes(List<PaintStroke> strokes, PaintStroke live)
        {
            if (live != null && live.IsBlend) return true;
            foreach (var s in strokes) if (s != null && s.IsBlend) return true;
            return false;
        }

        /// <summary>
        /// Appends one layer's part of the fingerprint of everything below a layer. Blur / Smudge strokes read the image
        /// below their layer, so their cached result is only reused while this stays the same.
        /// </summary>
        void AppendBelowSignature(System.Text.StringBuilder sb, Layer l)
        {
            if (l == null) return;
            sb.Append(l.id).Append('|').Append(l.enabled).Append(l.opacity).Append((int)l.blend).Append(l.invertMask)
              .Append((int)l.maskMode).Append(string.Join(",", l.islands)).Append(l.clipToBelow).Append(l.hasPaintMask)
              .Append(l.paintMaskStartsHidden).Append(l.maskStrokes?.Count ?? 0)
              .Append(l.maskStrokes != null && l.maskStrokes.Count > 0 ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(l.maskStrokes[l.maskStrokes.Count - 1]) : 0);
            if (l is PaintLayer pl)
                sb.Append(pl.strokes.Count).Append(pl.strokes.Count > 0 ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pl.strokes[pl.strokes.Count - 1]) : 0);
            else sb.Append(JsonUtility.ToJson(l));
            if (LiveStroke != null && (LiveLayerId == l.id || LiveLayerId == MaskKey(l.id))) sb.Append("live").Append(LiveStroke.points.Count);
            sb.Append(';');
        }

        readonly Dictionary<string, PaintCache> _paint = new Dictionary<string, PaintCache>();
        BrushRenderer _brushRenderer;
        RenderTexture _strokeRT, _scratchRT, _liveRT;
        Texture2D _islandIds, _clipSet;
        ushort[] _islandIdsCpu;   // same ids on the CPU, for the Fill tool
        ushort[] _islandIdsExact; // the same without the padding, for the Wand's colour shares
        Color32[] _sourceCpu;     // original texture at working size, for the Fill tool's colour comparison

        RenderTexture NewPaintRT()
        {
            var rt = NewRT(Width, Height, RenderTextureFormat.ARGBHalf, false, false);
            ClearRT(rt);
            return rt;
        }

        static void ClearRT(RenderTexture rt)
        {
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
        }

        void EnsurePaintTargets()
        {
            if (_brushRenderer == null) _brushRenderer = new BrushRenderer();
            if (_strokeRT == null)
            {
                var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
                _strokeRT = NewRT(Width, Height, fmt, false, false);
            }
            if (_scratchRT == null) _scratchRT = NewPaintRT();
            if (_liveRT == null) _liveRT = NewPaintRT();
        }

        /// <summary>
        /// The layer's paint (straight alpha). Strokes already rendered are kept; only new ones are drawn.
        /// If the stroke list changed in any other way (undo, load), the layer is re-rendered from scratch.
        /// </summary>
        /// <param name="below">The composite under this layer (needed by Blur / Smudge strokes).</param>
        /// <param name="belowKey">Fingerprint of that composite; when it changes, Blur / Smudge strokes are re-rendered.</param>
        Texture PaintFor(PaintLayer layer, MeshUV uv, Texture below = null, string belowKey = null) =>
            StrokesTexture(layer.id, layer.strokes, uv, below, belowKey);

        /// <summary>Renders a stroke list (a Paint layer's paint, or a layer's painted mask) into a cached texture.</summary>
        Texture StrokesTexture(string key, List<PaintStroke> strokes, MeshUV uv, Texture below = null, string belowKey = null)
        {
            EnsurePaintTargets();
            if (!_paint.TryGetValue(key, out var cache))
            {
                cache = new PaintCache { rt = NewPaintRT() };
                _paint[key] = cache;
            }

            int poseVersion = uv != null && HasSurfaceStrokes(strokes, LiveLayerId == key ? LiveStroke : null) ? uv.PoseVersion : 0;
            bool isPrefix = cache.rendered.Count <= strokes.Count && cache.belowKey == belowKey && cache.poseVersion == poseVersion;
            for (int i = 0; isPrefix && i < cache.rendered.Count; i++)
                isPrefix = ReferenceEquals(cache.rendered[i], strokes[i]);
            if (!isPrefix)
            {
                ClearRT(cache.rt);
                cache.rendered.Clear();
            }
            cache.belowKey = belowKey;
            cache.poseVersion = poseVersion;
            bool cleared = !isPrefix;

            for (int i = cache.rendered.Count; i < strokes.Count; i++)
            {
                var s = strokes[i];
                if (s != null)
                {
                    ApplyStroke(s, cache.rt, _scratchRT, uv, below);
                    (cache.rt, _scratchRT) = (_scratchRT, cache.rt);
                }
                cache.rendered.Add(s);
            }

            if (LiveStroke != null && LiveLayerId == key)
            {
                // Nothing changed since the last raster of this live stroke (same points, same base): reuse its result.
                int mirrorCount = LiveMirror != null ? LiveMirror.points.Count : 0;
                bool same = !cleared && belowKey == null && _liveDone != null && ReferenceEquals(_liveDone.cache, cache)
                    && _liveDone.rt == cache.rt && _liveDone.renderedCount == cache.rendered.Count && _liveDone.poseVersion == poseVersion
                    && ReferenceEquals(_liveDone.stroke, LiveStroke) && _liveDone.count == LiveStroke.points.Count
                    && ReferenceEquals(_liveDone.mirror, LiveMirror) && _liveDone.mirrorCount == mirrorCount
                    && _liveDone.result != null && (_liveDone.result == _liveRT || _liveDone.result == _liveRT2);
                if (same) return _liveDone.result;

                ApplyStroke(LiveStroke, cache.rt, _liveRT, uv, below);
                var result = _liveRT;
                if (mirrorCount > 0)
                {
                    if (_liveRT2 == null) _liveRT2 = NewPaintRT();
                    ApplyStroke(LiveMirror, _liveRT, _liveRT2, uv, below);
                    result = _liveRT2;
                }
                _liveDone = new LiveRaster
                {
                    cache = cache, rt = cache.rt, renderedCount = cache.rendered.Count, poseVersion = poseVersion,
                    stroke = LiveStroke, count = LiveStroke.points.Count, mirror = LiveMirror, mirrorCount = mirrorCount, result = result,
                };
                return result;
            }
            return cache.rt;
        }

        void ApplyStroke(PaintStroke s, RenderTexture src, RenderTexture dst, MeshUV uv, Texture below = null)
        {
            float grainSize = Mathf.Max(1f, s.shape != null ? s.shape.grainSize : 6f);
            if (s.isFill && uv != null) RenderFillCoverage(s, uv);
            else if (uv != null && s.IsSurface && s.tool != StrokeTool.Smudge) RenderSurfaceCoverage(s, uv, SourceWidth / grainSize);
            else _brushRenderer.RenderCoverage(s, _strokeRT, SourceWidth / grainSize);
            var mat = _brushRenderer.Material;
            bool blend = s.IsBlend && !s.isFill && s.mode == BrushMode.Paint;
            if (blend && s.tool == StrokeTool.Smudge)
            {
                if (_dirRT == null) _dirRT = NewRT(Width, Height, RenderTextureFormat.ARGBHalf, false, false);
                _brushRenderer.RenderDirection(s, _dirRT);
            }
            bool clip = s.clip && uv != null;
            mat.SetTexture("_StrokeTex", _strokeRT);
            if (clip || (blend && uv != null)) mat.SetTexture("_IslandTex", IslandIdTexture(uv));
            if (clip) mat.SetTexture("_ClipSet", ClipSetTexture(s.clipIslands, uv.islandCount));
            mat.SetVector("_Params", new Vector4(s.opacity, s.mode == BrushMode.Erase ? 1 : 0, clip ? 1 : 0, uv != null ? uv.islandCount : 1));
            mat.SetVector("_Color", new Vector4(s.color.r, s.color.g, s.color.b, 1));
            mat.SetFloat("_LockAlpha", s.lockAlpha ? 1 : 0);
            if (blend)
            {
                mat.SetTexture("_BelowTex", below != null ? below : _source);
                mat.SetTexture("_DirTex", s.tool == StrokeTool.Smudge ? (Texture)_dirRT : Texture2D.blackTexture);
                float texels = Mathf.Max(1f, s.blendSize) * Width / Mathf.Max(1f, SourceWidth);
                mat.SetVector("_BlendParams", new Vector4(texels, s.tool == StrokeTool.Mix ? Mathf.Clamp01(s.mixPaint) : 0, s.blendKeepIsland && uv != null ? 1 : 0, 0));
                Graphics.Blit(src, dst, mat, s.tool == StrokeTool.Smudge ? 5 : 4);
                return;
            }
            Graphics.Blit(src, dst, mat, 1);
        }


        RenderTexture _surfA, _surfB;

        /// <summary>
        /// 3D brush stroke → _strokeRT. The stroke is drawn as spheres on the posed model (so it crosses UV seams), then
        /// its coverage is grown a few texels into the seam padding, like 2D strokes cover it.
        /// </summary>
        void RenderSurfaceCoverage(PaintStroke s, MeshUV uv, float grainCellsPerUV)
        {
            if (_surfA == null)
            {
                var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf) ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
                _surfA = NewRT(Width, Height, fmt, false, false);
                _surfB = NewRT(Width, Height, fmt, false, false);
                _surfA.filterMode = _surfB.filterMode = FilterMode.Point;
            }
            _brushRenderer.RenderSurface(s, _surfA, uv, grainCellsPerUV);
            var mat = _brushRenderer.Material;
            int grow = Mathf.Clamp(Mathf.RoundToInt(Width / 512f), 2, 8);
            RenderTexture a = _surfA, b = _surfB;
            for (int i = 0; i < grow; i++)
            {
                Graphics.Blit(a, b, mat, 8);
                (a, b) = (b, a);
            }
            Graphics.Blit(a, _strokeRT);
        }


        ushort[] IslandIdsCpu(MeshUV uv) => _islandIdsCpu ??= uv.RasterIslandIds(Width, Height, Mathf.Max(4, Width / 128));

        /// <summary>The original texture's pixels at working size (raw file values), for the Wand.</summary>
        internal Color32[] SourcePixels() => SourceCpu();

        /// <summary>Piece id + 1 per texel at working size (0 = empty), for the Wand.</summary>
        internal ushort[] IslandIds(MeshUV uv) => IslandIdsCpu(uv);

        /// <summary>Piece id + 1 per texel without the padding around pieces (0 = empty), so a piece's own texels can be counted.</summary>
        internal ushort[] IslandIdsExact(MeshUV uv) => _islandIdsExact ??= uv.RasterIslandIds(Width, Height, 0);

        /// <summary>Whether two colours differ by at most tol (0 to 255) in every channel. The Fill tool and the Wand share it.</summary>
        internal static bool Close(Color32 a, Color32 b, int tol) =>
            Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol && Mathf.Abs(a.a - b.a) <= tol;

        /// <summary>The original texture's pixels at working size (raw file values), read once.</summary>
        Color32[] SourceCpu()
        {
            if (_sourceCpu != null) return _sourceCpu;
            // The Wand calls this from a mouse handler, so the caller's active RT must survive (see KeepActiveRT).
            using (new KeepActiveRT())
            {
                var rt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(_source, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
                t.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                _sourceCpu = t.GetPixels32();
                UnityEngine.Object.DestroyImmediate(t);
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            return _sourceCpu;
        }

        /// <summary>
        /// Flood fill from the clicked point into _strokeRT. Like CSP's Fill tool, but UV-aware: it never leaves the UV
        /// island that was clicked (island edges are hard borders). "Whole island" fills all of it; otherwise only
        /// connected pixels whose original colour is within Tolerance of the clicked colour. Area scaling then grows the
        /// result by N source pixels, so the fill also covers the seam padding. Deterministic, so it replays at bake size.
        /// </summary>
        ushort[] _fillVal; bool[] _fillCovered; int[] _fillQueue; byte[] _fillBytes; // Fill tool scratch, reused between fills

        void RenderFillCoverage(PaintStroke s, MeshUV uv)
        {
            int w = Width, h = Height, n = w * h;
            if (_fillVal == null || _fillVal.Length != n)
            {
                _fillVal = new ushort[n]; _fillCovered = new bool[n]; _fillQueue = new int[n]; _fillBytes = new byte[n];
            }
            else { Array.Clear(_fillVal, 0, n); Array.Clear(_fillCovered, 0, n); }
            var val = _fillVal;
            var covered = _fillCovered;
            if (s.points.Count > 0)
            {
                int sx = Mathf.Clamp(Mathf.FloorToInt(s.points[0].x * w), 0, w - 1);
                int sy = Mathf.Clamp(Mathf.FloorToInt(s.points[0].y * h), 0, h - 1);
                var ids = IslandIdsCpu(uv);
                ushort island = ids[sy * w + sx];
                if (island != 0)
                {
                    var src = s.fillWholeIsland ? null : SourceCpu();
                    Color32 seedColour = src != null ? src[sy * w + sx] : default;
                    int tol = Mathf.RoundToInt(Mathf.Clamp01(s.fillTolerance) * 255f);
                    var queue = _fillQueue;
                    int head = 0, tail = 0;
                    int start = sy * w + sx;
                    covered[start] = true;
                    queue[tail++] = start;
                    while (head < tail)
                    {
                        int p = queue[head++];
                        val[p] = 65535;
                        int x = p % w, y = p / w;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (covered[q] || ids[q] != island) continue;
                            if (src != null)
                            {
                                if (!Close(src[q], seedColour, tol)) continue;
                            }
                            covered[q] = true;
                            queue[tail++] = q;
                        }
                    }
                    int grow = Mathf.RoundToInt(Mathf.Max(0f, s.fillAreaScaling) * w / Mathf.Max(1f, SourceWidth));
                    if (s.fillAreaScaling > 0) grow = Mathf.Max(1, grow);
                    MeshUV.Dilate(val, covered, w, h, grow);
                }
            }

            var bytes = _fillBytes;
            for (int i = 0; i < n; i++) bytes[i] = (byte)(val[i] >> 8);
            var tex = new Texture2D(w, h, TextureFormat.R8, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false);
            Graphics.Blit(tex, _strokeRT);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>Island id + 1 per texel (0 = empty), dilated into the padding so strokes still cover seams.</summary>
        Texture IslandIdTexture(MeshUV uv)
        {
            if (_islandIds != null) return _islandIds;
            var ids = IslandIdsCpu(uv);
            bool r16 = SystemInfo.SupportsTextureFormat(TextureFormat.R16);
            _islandIds = new Texture2D(Width, Height, r16 ? TextureFormat.R16 : TextureFormat.RFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point,
            };
            if (r16) _islandIds.SetPixelData(ids, 0);
            else _islandIds.SetPixelData(ids.Select(v => v / 65535f).ToArray(), 0);
            _islandIds.Apply(false);
            return _islandIds;
        }

        Texture ClipSetTexture(List<int> islands, int count)
        {
            count = Mathf.Max(1, count);
            if (_clipSet == null || _clipSet.width != count)
            {
                if (_clipSet != null) UnityEngine.Object.DestroyImmediate(_clipSet);
                _clipSet = new Texture2D(count, 1, TextureFormat.R8, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point,
                };
            }
            var bytes = new byte[count];
            foreach (var i in islands) if (i >= 0 && i < count) bytes[i] = 255;
            _clipSet.SetPixelData(bytes, 0);
            _clipSet.Apply(false);
            return _clipSet;
        }

        /// <summary>The edited colour at a UV point, in file values (for the eyedropper).</summary>
        /// <summary>What the eyedropper samples (CLIP STUDIO PAINT: Reference).</summary>
        public enum PickSource
        {
            [InspectorName("Image (what you see)")] Image = 0,
            [InspectorName("Original texture")] Original = 1,
            [InspectorName("Current layer (paint only)")] CurrentLayer = 2,
            [InspectorName("Top layer (topmost paint there)")] TopLayer = 3,
        }

        /// <summary>
        /// Eyedropper: the colour at uv, optionally averaged over a square of (2·radius+1)² working-size texels.
        /// alpha = how much of the sampled area has colour (only meaningful for a paint layer).
        /// </summary>
        public Color Sample(PickSource source, Vector2 uv, int radius, PaintLayer layer, MeshUV meshUv, out float alpha)
        {
            alpha = 1f;
            using (new KeepActiveRT())
            {
                Texture tex = Result;
                bool weighted = false;
                if (source == PickSource.Original) tex = _source;
                else if (source == PickSource.CurrentLayer)
                {
                    if (layer == null || layer.strokes.Count == 0) { alpha = 0; return Color.clear; }
                    // The layer as last composed (Blur / Smudge strokes need the image below, which only Compose has).
                    tex = _paint.TryGetValue(layer.id, out var pc) && pc.rt != null ? pc.rt : PaintFor(layer, meshUv);
                    weighted = true;
                }
                int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * Width), 0, Width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * Height), 0, Height - 1);
                _utilMat.SetVector("_Pick", new Vector4((x + 0.5f) / Width, (y + 0.5f) / Height, Mathf.Clamp(radius, 0, 12), weighted ? 1 : 0));
                var one = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex, one, _utilMat, 4);
                RenderTexture.active = one;
                var t = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                t.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                var c = t.GetPixel(0, 0);
                UnityEngine.Object.DestroyImmediate(t);
                RenderTexture.ReleaseTemporary(one);
                alpha = weighted ? Mathf.Clamp01(c.a) : 1f;
                return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }
        }

        /// <summary>The current result downscaled to size×size (raw file values), for building a palette.</summary>
        /// <summary>The original texture downscaled to size×size (raw file values, before any layer).</summary>
        public Color[] SourceThumbnail(int size) => Thumbnail(_source, size);

        public Color[] ResultThumbnail(int size) => Thumbnail(Result, size);

        Color[] Thumbnail(Texture from, int size)
        {
            using (new KeepActiveRT())
            {
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(from, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                t.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                var px = t.GetPixels();
                UnityEngine.Object.DestroyImmediate(t);
                RenderTexture.ReleaseTemporary(rt);
                return px;
            }
        }

        public Color SampleResult(Vector2 uv)
        {
            using (new KeepActiveRT())
            {
                // Blit the one texel into a 1×1 target (scale 0, offset = texel centre) and read that whole target:
                // ReadPixels of a sub-rectangle has a different y origin per graphics API, a full 1×1 read doesn't.
                int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * Width), 0, Width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * Height), 0, Height - 1);
                var one = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(Result, one, Vector2.zero, new Vector2((x + 0.5f) / Width, (y + 0.5f) / Height));
                RenderTexture.active = one;
                var t = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                t.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                var c = t.GetPixel(0, 0);
                UnityEngine.Object.DestroyImmediate(t);
                RenderTexture.ReleaseTemporary(one);
                return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }
        }

        void DisposePaint()
        {
            foreach (var c in _paint.Values) Release(c.rt);
            _paint.Clear();
            Release(_strokeRT); Release(_scratchRT); Release(_liveRT); Release(_dirRT); Release(_liveRT2); Release(_surfA); Release(_surfB);
            _strokeRT = _scratchRT = _liveRT = _dirRT = _liveRT2 = _surfA = _surfB = null;
            _liveDone = null;
            if (_islandIds != null) UnityEngine.Object.DestroyImmediate(_islandIds);
            if (_clipSet != null) UnityEngine.Object.DestroyImmediate(_clipSet);
            _islandIds = _clipSet = null;
            _islandIdsCpu = _islandIdsExact = null;
            _fillVal = null; _fillCovered = null; _fillQueue = null; _fillBytes = null;
        }

        static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        /// <summary>Drop cached masks/gradients/paint (mesh or selection data changed).</summary>
        public void InvalidateGeometry()
        {
            foreach (var m in _masks.Values) if (m.tex != null) UnityEngine.Object.DestroyImmediate(m.tex);
            foreach (var g in _gradients.Values) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            _masks.Clear();
            _gradients.Clear();
            DisposePaint();
        }

        /// <summary>Reads the result as 8-bit RGBA (raw file values), optionally dithered.</summary>
        public Texture2D ReadResult(bool dither)
        {
            var prev = RenderTexture.active;
            var rt8 = NewRT(Width, Height, RenderTextureFormat.ARGB32, false, false);
            _utilMat.SetFloat("_Dither", dither ? 1 : 0);
            Graphics.Blit(Result, rt8, _utilMat, 2);
            RenderTexture.active = rt8;
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = prev;
            rt8.Release();
            UnityEngine.Object.DestroyImmediate(rt8);
            return tex;
        }

        public void Dispose()
        {
            InvalidateGeometry();
            foreach (var l in _luts.Values) if (l != null) UnityEngine.Object.DestroyImmediate(l);
            _luts.Clear();
            _lutHash.Clear();
            if (_clear != null) UnityEngine.Object.DestroyImmediate(_clear);
            foreach (var rt in new[] { _source, _pingA, _pingB, Result, Preview, SourcePreview, _coverageRT, _maskOverlayRT })
            {
                if (rt == null) continue;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            if (_layerMat != null) UnityEngine.Object.DestroyImmediate(_layerMat);
            if (_utilMat != null) UnityEngine.Object.DestroyImmediate(_utilMat);
            _brushRenderer?.Dispose();
        }
    }
}
