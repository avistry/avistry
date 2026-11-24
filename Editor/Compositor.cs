using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public partial class Compositor : IDisposable
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public RenderTexture Result { get; private set; }
        public RenderTexture Preview { get; private set; }
        public RenderTexture SourcePreview
        {
            get
            {
                if (_sourcePreview == null || _sourcePreview.IsCreated()) return _sourcePreview;
                using (new KeepActiveRT()) ToPreview(_source, _sourcePreview);
                return _sourcePreview;
            }
        }
        public string SourceNote { get; private set; }
        public int SourceWidth { get; private set; }
        internal int SourceHeight { get; private set; }

        public PaintStroke LiveStroke;
        public string LiveLayerId;
        public PaintStroke LiveMirror;
        RenderTexture _liveRT2;
        bool _allowLiveCoverage;

        RenderTexture _source, _sourcePreview, _pingA, _pingB;
        Material _layerMat, _utilMat;
        RenderTexture _scenePositions;
        MeshUV _scenePositionsMesh;
        int _scenePositionsPose;
        Matrix4x4 _scenePositionsTransform;
        bool _previewIsSRGB;
        readonly Dictionary<string, (long key, Texture2D tex)> _masks = new Dictionary<string, (long, Texture2D)>();
        readonly Dictionary<GradientMode, Texture2D> _gradients = new Dictionary<GradientMode, Texture2D>();
        readonly Dictionary<string, Texture2D> _luts = new Dictionary<string, Texture2D>();
        Texture2D _white;

        internal sealed class PreviewShaderException : Exception
        {
            internal PreviewShaderException(string shader, string reason)
                : base($"Avistry preview shader '{shader}' {reason}.") { }
        }

        static Material LoadMat(string shader)
        {
            var s = Shader.Find(shader);
            if (s == null) throw new PreviewShaderException(shader, "was not found");
            if (ShaderUtil.ShaderHasError(s)) throw new PreviewShaderException(shader, "has import or compile errors");
            if (!s.isSupported) throw new PreviewShaderException(shader, "is not supported by this graphics device");
            return new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        }

        public Compositor(TextureSlot slot, int maxSize) : this(slot, maxSize, null) { }

        internal Compositor(TextureSlot slot, int maxSize, MeshUV pickedBrushMesh) : this(slot, maxSize, pickedBrushMesh, false) { }

        internal Compositor(TextureSlot slot, int maxSize, MeshUV pickedBrushMesh, bool limitBackgroundRaster)
        {
            LimitBackgroundRaster = limitBackgroundRaster;
            _sourceAsset = slot.texture;
            Texture2D raw = null;
            bool fromFile = false;
            try
            {
                _layerMat = LoadMat("Hidden/Avistry/Layer");
                _utilMat = LoadMat("Hidden/Avistry/Util");
                _white = Texture2D.whiteTexture;

                if (pickedBrushMesh != null) StartPickedMap(slot, maxSize, pickedBrushMesh);
                raw = LoadRawSource(slot, out var note, out fromFile);
                SourceNote = note;
                SourceWidth = raw.width;
                SourceHeight = raw.height;
                int predictedWidth = Width, predictedHeight = Height;
                SetWorkingSize(raw.width, raw.height, maxSize);
                if (_pendingIslandIds != null && (Width != predictedWidth || Height != predictedHeight))
                    CancelIslandIdPreparation();

                using (new KeepActiveRT())
                    CreateTargets(slot, raw, fromFile, Width, Height);
            }
            catch
            {
                if (fromFile && raw != null) UnityEngine.Object.DestroyImmediate(raw);
                Dispose();
                throw;
            }
        }

        void SetWorkingSize(int width, int height, int maxSize)
        {
            int longest = Mathf.Max(width, height);
            float scale = maxSize > 0 && longest > maxSize ? maxSize / (float)longest : 1f;
            Width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            Height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        }

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
                _utilMat.SetFloat("_Convert", slot.importedAsSRGB && IsLinearProject ? 1 : 0);
                Graphics.Blit(raw, _source, _utilMat, 0);
            }
            if (fromFile) UnityEngine.Object.DestroyImmediate(raw);

            Result = NewRT(w, h, RenderTextureFormat.ARGBHalf, false, false);

            _previewIsSRGB = slot.importedAsSRGB && IsLinearProject;
            Preview = NewRT(w, h, RenderTextureFormat.ARGB32, _previewIsSRGB, true);
            _sourcePreview = NewRT(w, h, RenderTextureFormat.ARGB32, _previewIsSRGB, true, create: false);
            CopyFilterSettings(slot.texture, Preview);
            CopyFilterSettings(slot.texture, _sourcePreview);
        }

        static bool IsLinearProject => QualitySettings.activeColorSpace == ColorSpace.Linear;

        static bool GraphicsFormatIsSRGB(Texture t) => t != null && UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);

        static RenderTextureFormat SelectTargetFormat(RenderTextureFormat requested, Func<RenderTextureFormat, bool> supported)
        {
            if (supported(requested)) return requested;
            if (requested == RenderTextureFormat.RHalf && supported(RenderTextureFormat.RFloat)) return RenderTextureFormat.RFloat;
            if (requested == RenderTextureFormat.RGHalf && supported(RenderTextureFormat.RGFloat)) return RenderTextureFormat.RGFloat;
            if (requested == RenderTextureFormat.RHalf || requested == RenderTextureFormat.RGHalf)
                if (supported(RenderTextureFormat.ARGBHalf)) return RenderTextureFormat.ARGBHalf;
            if (requested == RenderTextureFormat.ARGBHalf || requested == RenderTextureFormat.RHalf ||
                requested == RenderTextureFormat.RFloat || requested == RenderTextureFormat.RGHalf || requested == RenderTextureFormat.RGFloat)
                if (supported(RenderTextureFormat.ARGBFloat)) return RenderTextureFormat.ARGBFloat;
            if (requested == RenderTextureFormat.R8 && supported(RenderTextureFormat.ARGB32)) return RenderTextureFormat.ARGB32;
            throw new InvalidOperationException(Loc.F("Avistry cannot use the required {0} texture format on this graphics device.", requested));
        }

        static bool SupportsTargetFormat(RenderTextureFormat format, bool srgb, bool blend)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(format) ||
                blend && !SystemInfo.SupportsBlendingOnRenderTextureFormat(format)) return false;
            var graphics = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, srgb);
            return SystemInfo.IsFormatSupported(graphics, UnityEngine.Experimental.Rendering.FormatUsage.Render) &&
                SystemInfo.IsFormatSupported(graphics, UnityEngine.Experimental.Rendering.FormatUsage.Sample) &&
                SystemInfo.IsFormatSupported(graphics, UnityEngine.Experimental.Rendering.FormatUsage.Linear) &&
                (!blend || SystemInfo.IsFormatSupported(graphics, UnityEngine.Experimental.Rendering.FormatUsage.Blend));
        }

        static RenderTexture NewRT(int w, int h, RenderTextureFormat fmt, bool srgb, bool mips,
                                   bool create = true, bool blend = false)
        {
            if (w <= 0 || h <= 0 || w > SystemInfo.maxTextureSize || h > SystemInfo.maxTextureSize)
                throw new InvalidOperationException(Loc.F("Avistry cannot create a {0} × {1} texture on this graphics device.", w, h));
            fmt = SelectTargetFormat(fmt, candidate => SupportsTargetFormat(candidate, srgb, blend));
            var desc = new RenderTextureDescriptor(w, h, fmt, 0)
            {
                sRGB = srgb,
                useMipMap = mips,
                autoGenerateMips = mips,
                msaaSamples = 1,
            };
            var rt = new RenderTexture(desc) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            try
            {
                if (create && !rt.Create())
                    throw new InvalidOperationException(Loc.F("Avistry could not allocate a {0} × {1} {2} texture.", w, h, fmt));
                return rt;
            }
            catch
            {
                Release(rt);
                throw;
            }
        }

        static RenderTexture TemporaryRT(int width, int height, RenderTextureFormat requested, bool readback = false)
        {
            if (width <= 0 || height <= 0 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                throw new InvalidOperationException(Loc.F("Avistry cannot create a {0} × {1} texture on this graphics device.", width, height));
            var format = SelectTargetFormat(requested, candidate => SupportsTargetFormat(candidate, false, false) &&
                (!readback || SystemInfo.IsFormatSupported(
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(candidate, false),
                    UnityEngine.Experimental.Rendering.FormatUsage.ReadPixels)));
            var rt = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            try
            {
                if (!rt.IsCreated() && !rt.Create())
                    throw new InvalidOperationException(Loc.F("Avistry could not allocate a {0} × {1} {2} texture.", width, height, format));
                return rt;
            }
            catch
            {
                RenderTexture.ReleaseTemporary(rt);
                throw;
            }
        }

        static void CopyFilterSettings(Texture src, RenderTexture dst)
        {
            dst.wrapMode = src.wrapMode;
            dst.filterMode = src.filterMode;
            dst.anisoLevel = Mathf.Max(src.anisoLevel, 1);
        }

        static bool UsesSourceImageFile(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        static Texture2D LoadRawSource(TextureSlot slot, out string note, out bool fromFile)
        {
            string ext = Path.GetExtension(slot.assetPath).ToLowerInvariant();
            if (UsesSourceImageFile(slot.assetPath) && File.Exists(slot.assetPath))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                bool loaded = false;
                try { loaded = t.LoadImage(File.ReadAllBytes(slot.assetPath), markNonReadable: true); }
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
            if (!dst.IsCreated() && !dst.Create())
                throw new InvalidOperationException(Loc.F("Avistry could not allocate a {0} × {1} {2} texture.", dst.width, dst.height, dst.format));
            _utilMat.SetFloat("_Convert", _previewIsSRGB ? 1 : 0);
            Graphics.Blit(raw, dst, _utilMat, 1);
        }


        public void Compose(IList<Layer> layers, MeshUV uv) => ComposeStack(layers, uv, null, 0);

        void ComposeStack(IList<Layer> layers, MeshUV uv, Layer liveLayer, int assetStamp)
        {
            if (layers is List<Layer> own) TextureRecipe.DropNullLayers(own, "compose");
            var expanded = ExpandIncompatibleMixedMerges(layers, uv);
            int prefixEnd = ReferenceEquals(layers, expanded) ? LivePrefixEnd(layers, liveLayer) : -1;
            bool resume = CanResumeLivePrefix(expanded, uv, prefixEnd, assetStamp);
            if (!resume) InvalidateLivePrefix();
            _allowLiveCoverage = liveLayer != null && ReferenceEquals(layers, expanded);
            if (!_allowLiveCoverage) _brushRenderer?.InvalidateLiveCoverage();
            try
            {
                using (new KeepActiveRT()) ComposeInternal(expanded, uv, prefixEnd, assetStamp, resume);
            }
            finally { _allowLiveCoverage = false; }
        }

        public static string MaskKey(string layerId) => layerId + MaskKeySuffix;

        const string MaskKeySuffix = ":mask";

        RenderTexture _coverageRT, _maskOverlayRT;
        Texture _maskOverlaySource;
        ulong _maskOverlayVersion, _paintRasterVersion;
        bool _maskOverlayStartsHidden;
        int _maskOverlayRenders, _clipSetUploads;
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

        RenderTexture LayerTarget(RenderTexture current, bool usePingA, bool last, bool keepPrefix)
        {
            if (keepPrefix)
            {
                usePingA = _livePrefixBuffer != _pingA;
                var scratch = usePingA ? _pingA : _pingB;
                if (current != Result && (last || current == scratch)) return Result;
            }
            else if (last) return Result;

            ref var ping = ref (usePingA ? ref _pingA : ref _pingB);
            return ping ??= NewRT(Width, Height, RenderTextureFormat.ARGBHalf, false, false);
        }

        void ComposeInternal(IList<Layer> layers, MeshUV uv, int prefixEnd, int assetStamp, bool resume)
        {
            var current = resume ? _livePrefixBuffer : _source;
            bool usePingA = true;
            _layerMat.SetFloat("_Aspect", Width / (float)Mathf.Max(1, Height));
            _layerMat.SetFloat("_CoverageOut", 0);
            bool haveBase = resume && _livePrefixHasBase, baseVisible = resume && _livePrefixBaseVisible;
            RenderTexture scenePositions = null;
            int startingPose = uv?.PoseVersion ?? 0;
            _sigSb.Clear();
            int sigDone = 0;
            for (int li = resume ? prefixEnd : 0; li < layers.Count; li++)
            {
                if (!resume && li == prefixEnd && current != _source && startingPose == (uv?.PoseVersion ?? 0))
                    RememberLivePrefix(layers, uv, current, prefixEnd, assetStamp, haveBase, baseVisible);
                var layer = layers[li];
                if (layer == null) continue;
                bool clipped = layer.clipToBelow && haveBase;
                if (!layer.clipToBelow) { haveBase = true; baseVisible = false; }
                if (clipped && !baseVisible) continue;
                if (!layer.enabled || layer.opacity <= 0) continue;

                bool last = li == layers.Count - 1;
                bool keepPrefix = _livePrefixBuffer != null && li >= prefixEnd;
                if (layer is MixedMergeLayer merged)
                {
                    var mixedTarget = LayerTarget(current, usePingA, last, keepPrefix);
                    RenderMixedMerge(merged, mixedTarget, NextIsClipped(layers, li));
                    baseVisible = true;
                    current = mixedTarget;
                    usePingA = mixedTarget != _pingA;
                    continue;
                }

                if (layer is DrawnGradientLayer gradient)
                {
                    _layerMat.SetFloat("_SceneGradient", gradient.sceneProjection ? 1f : 0f);
                    if (gradient.sceneProjection)
                    {
                        scenePositions ??= SceneGradientPositions(uv);
                        if (scenePositions == null) continue;
                        _layerMat.SetTexture("_ScenePositions", scenePositions);
                        _layerMat.SetVector("_SceneOrigin", gradient.sceneStart);
                        _layerMat.SetVector("_SceneAxis", gradient.sceneAxis);
                        _layerMat.SetVector("_SceneAcross", gradient.sceneAcross);
                    }
                }

                _layerMat.SetFloat("_Opacity", layer.opacity);
                _layerMat.SetFloat("_Blend", (int)layer.blend);
                _layerMat.SetVector("_AdditionalBlendStrengths", layer.AdditionalBlendStrengths(out Vector4 blendModes));
                _layerMat.SetVector("_AdditionalBlendModes", blendModes);
                bool canInvert = uv != null && (layer.maskMode == MaskMode.SelectedIslands ||
                    layer.maskMode == MaskMode.SelectedFaces && layer.faceLayout == uv.FaceLayout &&
                    layer.faces?.Exists(face => face >= 0 && face < uv.TriangleCount) == true);
                _layerMat.SetFloat("_InvertMask", canInvert && layer.invertMask ? 1 : 0);
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
                        while (sigDone < li) AppendBelowSignature(_sigSb, layers[sigDone++], _scenePositionsPose);
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
                    if (img.image == null || img.NeedsPackMask) continue;
                    _layerMat.SetTexture("_ImageTex", img.image);
                    _layerMat.SetTexture("_ImageMaskTex", img.mask != null ? (Texture)img.mask : _white);
                    _layerMat.SetFloat("_ImageConvert", IsLinearProject && GraphicsFormatIsSRGB(img.image) ? 1 : 0);
                    _layerMat.SetFloat("_MaskConvert", img.mask != null && IsLinearProject && GraphicsFormatIsSRGB(img.mask) ? 1 : 0);
                }

                var target = LayerTarget(current, usePingA, last, keepPrefix);
                Graphics.Blit(current, target, _layerMat, layer.Pass);
                if (!layer.clipToBelow)
                {
                    baseVisible = true;
                    if (NextIsClipped(layers, li))
                    {
                        EnsureCoverageRT();
                        _layerMat.SetFloat("_CoverageOut", 1);
                        Graphics.Blit(current, _coverageRT, _layerMat, layer.Pass);
                        _layerMat.SetFloat("_CoverageOut", 0);
                    }
                }
                current = target;
                usePingA = target != _pingA;
            }
            if (current != Result) Graphics.Blit(current, Result);
            ToPreview(Result, Preview);
            if (layers.Count > 0) EvictDeadLayers(layers);
        }

        readonly System.Text.StringBuilder _sigSb = new System.Text.StringBuilder(256);
        readonly HashSet<string> _liveIds = new HashSet<string>();
        readonly List<string> _deadKeys = new List<string>();

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
            _coverageRT = NewRT(Width, Height, RenderTextureFormat.R8, false, false);
        }

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

        public RenderTexture MaskOverlay(Layer layer, MeshUV uv)
        {
            if (layer == null || !layer.hasPaintMask) return null;
            using (new KeepActiveRT())
            {
                var src = MaskStrokes(layer, uv);
                ulong version = 0;
                if (_paint.TryGetValue(MaskKey(layer.id), out var cache))
                    version = _liveDone != null && ReferenceEquals(_liveDone.cache, cache) && _liveDone.result == src
                        ? _liveDone.version : cache.version;
                if (_maskOverlayRT != null && _maskOverlayRT.IsCreated() && _maskOverlaySource == src &&
                    _maskOverlayVersion == version && _maskOverlayStartsHidden == layer.paintMaskStartsHidden)
                    return _maskOverlayRT;
                if (_maskOverlayRT == null) _maskOverlayRT = NewRT(Width, Height, RenderTextureFormat.ARGB32, false, false);
                else if (!_maskOverlayRT.IsCreated() && !_maskOverlayRT.Create())
                    throw new InvalidOperationException("Avistry could not recreate the painted mask preview.");
                _utilMat.SetFloat("_Convert", layer.paintMaskStartsHidden ? 1 : 0);
                Graphics.Blit(src, _maskOverlayRT, _utilMat, 3);
                _maskOverlaySource = src;
                _maskOverlayVersion = version;
                _maskOverlayStartsHidden = layer.paintMaskStartsHidden;
                _maskOverlayRenders++;
            }
            return _maskOverlayRT;
        }

        Texture MaskFor(Layer layer, MeshUV uv)
        {
            bool faces = layer.maskMode == MaskMode.SelectedFaces;
            if (faces && (uv == null || layer.faceLayout != uv.FaceLayout || layer.faces == null))
                return Texture2D.blackTexture;
            if (layer.maskMode == MaskMode.WholeTexture || uv == null) return _white;
            var selected = faces ? layer.faces : layer.islands;
            long key = MaskSignature(selected) ^ (faces ? long.MinValue : 0);
            if (_masks.TryGetValue(layer.id, out var cached) && cached.key == key && cached.tex != null) return cached.tex;
            if (cached.tex != null) UnityEngine.Object.DestroyImmediate(cached.tex);

            int dilate = Mathf.Max(1, Width / 1024);
            var set = new HashSet<int>(selected);
            var bytes = faces ? uv.RasterFaceMask(Width, Height, set) : uv.RasterIslandMask(Width, Height, set, dilate);
            var tex = new Texture2D(Width, Height, TextureFormat.R8, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false);
            _masks[layer.id] = (key, tex);
            return tex;
        }

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

        RenderTexture SceneGradientPositions(MeshUV geometry)
        {
            var root = geometry?.AvatarRoot();
            if (root == null) return null;
            if (geometry.PoseChanged()) geometry.RefreshPose();
            var worldToLocal = root.worldToLocalMatrix;
            if (_scenePositions != null && _scenePositionsMesh == geometry && _scenePositionsPose == geometry.PoseVersion &&
                _scenePositionsTransform == worldToLocal)
                return _scenePositions;

            _scenePositions ??= NewRT(Width, Height, RenderTextureFormat.ARGBHalf, false, false);
            _scenePositions.filterMode = FilterMode.Point;
            const int DrawPositions = 5;
            const int GrowPositions = 6;
            var temporary = TemporaryRT(Width, Height, _scenePositions.format);
            temporary.filterMode = FilterMode.Point;
            temporary.wrapMode = TextureWrapMode.Clamp;
            try
            {
                var properties = new MaterialPropertyBlock();
                properties.SetMatrix("_SceneWorldToLocal", worldToLocal);
                using (var command = new CommandBuffer { name = "Avistry gradient positions" })
                {
                    command.SetRenderTarget(temporary);
                    command.ClearRenderTarget(false, true, Color.clear);
                    command.SetViewport(new Rect(0, 0, Width, Height));
                    command.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 100));
                    command.DrawMesh(geometry.SurfaceMesh(), Matrix4x4.identity, _utilMat, 0, DrawPositions, properties);
                    GL.PushMatrix();
                    try { Graphics.ExecuteCommandBuffer(command); }
                    finally { GL.PopMatrix(); }
                }

                Graphics.Blit(temporary, _scenePositions, _utilMat, GrowPositions);
            }
            finally { RenderTexture.ReleaseTemporary(temporary); }
            _scenePositionsMesh = geometry;
            _scenePositionsPose = geometry.PoseVersion;
            _scenePositionsTransform = worldToLocal;
            return _scenePositions;
        }

        static readonly Color32[] _lutBuf = new Color32[256];
        readonly Dictionary<string, long> _lutHash = new Dictionary<string, long>();

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
                _lutHash.Remove(id);
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
                _lutHash.Remove(id);
            }
            long h = HashGradient(g);
            if (_lutHash.TryGetValue(id, out var old) && old == h) return lut;
            for (int i = 0; i < 256; i++) _lutBuf[i] = g.Evaluate(i / 255f);
            lut.SetPixels32(_lutBuf);
            lut.Apply(false);
            _lutHash[id] = h;
            return lut;
        }


        class PaintCache
        {
            public RenderTexture rt;
            public readonly List<PaintStroke> rendered = new List<PaintStroke>();
            public string belowKey;
            public int poseVersion;
            public ulong version;
        }

        RenderTexture _dirRT;

        class LiveRaster
        {
            public PaintCache cache; public RenderTexture rt, result;
            public PaintStroke stroke, mirror;
            public int renderedCount, poseVersion, count, mirrorCount;
            public ulong version;
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

        void AppendBelowSignature(System.Text.StringBuilder sb, Layer l, int scenePose)
        {
            if (l == null) return;
            sb.Append(l.id).Append('|').Append(l.enabled).Append(l.opacity).Append((int)l.blend).Append(l.invertMask)
              .Append((int)l.maskMode).Append(string.Join(",", l.islands)).Append(l.clipToBelow).Append(l.hasPaintMask)
              .Append(l.paintMaskStartsHidden).Append(l.maskStrokes?.Count ?? 0)
              .Append(l.maskStrokes != null && l.maskStrokes.Count > 0 ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(l.maskStrokes[l.maskStrokes.Count - 1]) : 0);
            if (l.maskMode == MaskMode.SelectedFaces && l.faces != null)
                sb.Append(l.faceLayout).Append('|').Append(string.Join(",", l.faces));
            if (l is PaintLayer pl)
            {
                sb.Append(pl.strokes.Count).Append(pl.strokes.Count > 0 ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pl.strokes[pl.strokes.Count - 1]) : 0);
                if (_paint.TryGetValue(l.id, out var paint) && paint.poseVersion != 0)
                    sb.Append("|paintPose:").Append(paint.poseVersion);
                var strengths = l.AdditionalBlendStrengths(out Vector4 modes);
                for (int index = 0; index < Layer.MaxAdditionalBlends; index++)
                    if (strengths[index] > 0f) sb.Append('|').Append((int)modes[index]).Append(':').Append(strengths[index]);
            }
            else if (l is MixedMergeLayer merged) AppendTextureSignature(sb, merged.pixels);
            else
            {
                sb.Append(JsonUtility.ToJson(l));
                if (l is ImageLayer image)
                {
                    AppendTextureSignature(sb, image.image);
                    AppendTextureSignature(sb, image.mask);
                }
            }
            if (l.hasPaintMask && _paint.TryGetValue(MaskKey(l.id), out var mask) && mask.poseVersion != 0)
                sb.Append("|maskPose:").Append(mask.poseVersion);
            if (l is DrawnGradientLayer gradient && gradient.sceneProjection) sb.Append('|').Append(scenePose);
            if (LiveStroke != null && (LiveLayerId == l.id || LiveLayerId == MaskKey(l.id))) sb.Append("live").Append(LiveStroke.points.Count);
            sb.Append(';');
        }

        static void AppendTextureSignature(System.Text.StringBuilder sb, Texture2D texture)
        {
            sb.Append('|');
            if (texture == null) return;
            var (updates, hash) = TextureContentVersion(texture);
            sb.Append(texture.GetInstanceID()).Append(':').Append(updates).Append(':').Append(hash);
        }

        static (uint updates, Hash128 hash) TextureContentVersion(Texture2D texture) =>
            texture != null ? (texture.updateCount, texture.imageContentsHash) : default;

        readonly Dictionary<string, PaintCache> _paint = new Dictionary<string, PaintCache>();
        BrushRenderer _brushRenderer;
        RenderTexture _strokeRT, _scratchRT, _liveRT, _mirroredCoverageRT;
        Texture2D _islandIds, _clipSet;
        byte[] _clipSetBytes;
        HashSet<int> _clipSetIslands = new HashSet<int>(), _nextClipSetIslands = new HashSet<int>();
        ushort[] _islandIdsCpu;
        ushort[] _islandIdsExact;
        Color32[] _sourceCpu;

        RenderTexture NewPaintRT() => NewRT(Width, Height, RenderTextureFormat.ARGBHalf, false, false);

        Texture PaintBase(PaintCache cache, PaintStroke stroke)
        {
            if (cache.rt == null && stroke.IsBlend)
            {
                cache.rt = NewPaintRT();
                ClearRT(cache.rt);
            }
            return cache.rt != null ? (Texture)cache.rt : Clear;
        }

        static void ClearRT(RenderTexture rt)
        {
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
        }

        void EnsurePaintTargets()
        {
            if (_brushRenderer == null) _brushRenderer = new BrushRenderer();
            if (_strokeRT == null) _strokeRT = NewRT(Width, Height, RenderTextureFormat.RHalf, false, false, blend: true);
        }

        int _requestedPaintPasses;
        bool _paintPreparationUnavailable, _surfacePaddingChecked;

        internal bool ContinuePaintGpuPreparation(bool surface, bool buildUp)
        {
            if (_preparationDisposed || _paintPreparationUnavailable || LiveStroke != null ||
                (long)Width * Height > 4096L * 4096) return false;
            try
            {
                if (_brushRenderer == null) { _brushRenderer = new BrushRenderer(); return true; }
                int coveragePass = surface ? (buildUp ? 7 : 6) : (buildUp ? 2 : 0);
                if (RequestPaintPass(_brushRenderer.Material, coveragePass, coveragePass) ||
                    RequestPaintPass(_brushRenderer.Material, 1, 1) || RequestPaintPass(_layerMat, 5, 9)) return true;
                if (_strokeRT == null) { EnsurePaintTargets(); return true; }
                if (_liveRT == null) { _liveRT = NewPaintRT(); return true; }
                if (surface && _surfA == null)
                {
                    if (!_surfacePaddingChecked)
                    {
                        if (RenderTexture.active == _strokeRT) return false;
                        using (new KeepActiveRT()) EnsureSurfacePadding();
                    }
                    else CreateSurfaceTarget();
                    return true;
                }
            }
            catch (Exception)
            {
                _paintPreparationUnavailable = true;
            }
            return false;
        }

        bool RequestPaintPass(Material material, int pass, int bit)
        {
            int flag = 1 << bit;
            if ((_requestedPaintPasses & flag) != 0) return false;
            bool compiled = ShaderUtil.IsPassCompiled(material, pass);
            if (!compiled) ShaderUtil.CompilePass(material, pass, false);
            _requestedPaintPasses |= flag;
            return !compiled;
        }

        Texture PaintFor(PaintLayer layer, MeshUV uv, Texture below = null, string belowKey = null) =>
            StrokesTexture(layer.id, layer.strokes, uv, below, belowKey);

        Texture StrokesTexture(string key, List<PaintStroke> strokes, MeshUV uv, Texture below = null, string belowKey = null)
        {
            EnsurePaintTargets();
            if (!_paint.TryGetValue(key, out var cache))
            {
                cache = new PaintCache();
                _paint[key] = cache;
            }

            int poseVersion = uv != null && HasSurfaceStrokes(strokes, LiveLayerId == key ? LiveStroke : null) ? uv.PoseVersion : 0;
            bool isPrefix = cache.rendered.Count <= strokes.Count && cache.belowKey == belowKey && cache.poseVersion == poseVersion;
            for (int i = 0; isPrefix && i < cache.rendered.Count; i++)
                isPrefix = ReferenceEquals(cache.rendered[i], strokes[i]);
            if (!isPrefix)
            {
                if (cache.rt != null) ClearRT(cache.rt);
                cache.rendered.Clear();
                cache.version = ++_paintRasterVersion;
            }
            cache.belowKey = belowKey;
            cache.poseVersion = poseVersion;
            bool cleared = !isPrefix;

            for (int i = cache.rendered.Count; i < strokes.Count; i++)
            {
                var s = strokes[i];
                if (s != null)
                {
                    _scratchRT ??= NewPaintRT();
                    ApplyStroke(s, PaintBase(cache, s), _scratchRT, uv, below);
                    (cache.rt, _scratchRT) = (_scratchRT, cache.rt);
                    cache.version = ++_paintRasterVersion;
                }
                cache.rendered.Add(s);
            }

            if (LiveStroke != null && LiveLayerId == key)
            {
                int mirrorCount = LiveMirror != null ? LiveMirror.points.Count : 0;
                bool same = !cleared && belowKey == null && _liveDone != null && ReferenceEquals(_liveDone.cache, cache)
                    && _liveDone.rt == cache.rt && _liveDone.renderedCount == cache.rendered.Count && _liveDone.poseVersion == poseVersion
                    && ReferenceEquals(_liveDone.stroke, LiveStroke) && _liveDone.count == LiveStroke.points.Count
                    && ReferenceEquals(_liveDone.mirror, LiveMirror) && _liveDone.mirrorCount == mirrorCount
                    && _liveDone.result != null && (_liveDone.result == _liveRT || _liveDone.result == _liveRT2)
                    && (!CanAppendStrokeCoverage(LiveStroke) ||
                        _brushRenderer.CanContinueLiveCoverage(LiveStroke, _strokeRT, StrokeGrainCells(LiveStroke)))
                    && (!_brushRenderer.HasLiveSurfaceCoverage || !LiveStroke.IsSurface ||
                        _brushRenderer.CanContinueLiveSurfaceCoverage(LiveStroke, _surfacePrefixRT, uv, StrokeGrainCells(LiveStroke)));
                if (same) return _liveDone.result;

                _liveRT ??= NewPaintRT();
                ApplyStroke(LiveStroke, PaintBase(cache, LiveStroke), _liveRT, uv, below);
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
                    version = ++_paintRasterVersion,
                };
                return result;
            }
            return cache.rt != null ? (Texture)cache.rt : Clear;
        }

        void ApplyStroke(PaintStroke s, Texture src, RenderTexture dst, MeshUV uv, Texture below = null)
        {
            bool combined = s.mirrorPaths?.Exists(path => path?.points?.Count > 0) == true;
            if (combined) RenderMirroredCoverage(s, uv);
            else RenderStrokeCoverage(s, uv);
            var mat = _brushRenderer.Material;
            bool blend = s.IsBlend && !s.isFill && s.mode == BrushMode.Paint;
            if (blend && s.tool == StrokeTool.Smudge)
            {
                if (_dirRT == null) _dirRT = NewRT(Width, Height, RenderTextureFormat.ARGBHalf, false, false, blend: true);
                _brushRenderer.RenderDirection(s, _dirRT);
            }
            bool clip = s.clip && uv != null && !combined;
            mat.SetTexture("_StrokeTex", combined ? _mirroredCoverageRT : _strokeRT);
            if (clip || (blend && uv != null)) mat.SetTexture("_IslandTex", IslandIdTexture(uv));
            if (clip) mat.SetTexture("_ClipSet", ClipSetTexture(s.clipIslands, uv.islandCount));
            mat.SetVector("_Params", new Vector4(s.opacity, s.mode == BrushMode.Erase ? 1 : 0, clip ? 1 : 0, uv != null ? uv.islandCount : 1));
            mat.SetVector("_Color", new Vector4(s.color.r, s.color.g, s.color.b, 1));
            mat.SetFloat("_LockAlpha", s.lockAlpha ? 1 : 0);
            mat.SetVector("_PaintUvBounds", s.limitPaintUV ? s.paintUVBounds : new Vector4(0f, 0f, 1f, 1f));
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

        float StrokeGrainCells(PaintStroke stroke) => SourceWidth / Mathf.Max(1f, stroke.shape?.grainSize ?? 6f);

        bool CanAppendStrokeCoverage(PaintStroke stroke) => _allowLiveCoverage && ReferenceEquals(stroke, LiveStroke) &&
            stroke.points?.Count > 0 && !stroke.surface && !stroke.isFill && !stroke.IsBlend &&
            LiveMirror == null && (stroke.mirrorPaths == null || stroke.mirrorPaths.Count == 0);

        internal void InvalidateLiveStrokeCoverage()
        {
            _brushRenderer?.InvalidateLiveCoverage();
            ReleaseLiveSurfaceCoverage();
            _surfacePrefixRejectedStroke = null;
            _liveDone = null;
        }

        void RenderStrokeCoverage(PaintStroke stroke, MeshUV uv)
        {
            if (ReferenceEquals(stroke, LiveStroke) && !stroke.IsSurface) ReleaseLiveSurfaceCoverage();
            float grain = StrokeGrainCells(stroke);
            if (CanAppendStrokeCoverage(stroke))
            {
                _brushRenderer.RenderLiveCoverage(stroke, _strokeRT, grain);
                return;
            }
            _brushRenderer.InvalidateLiveCoverage();
            if (stroke.isFill && stroke.fillRegion != null && stroke.fillRegion.scope != PaintFillScope.Legacy) RenderScopedFillCoverage(stroke, uv);
            else if (stroke.isFill && uv != null) RenderFillCoverage(stroke, uv);
            else if (uv != null && stroke.IsSurface && stroke.tool != StrokeTool.Smudge) RenderSurfaceCoverage(stroke, uv, grain);
            else _brushRenderer.RenderCoverage(stroke, _strokeRT, grain);
        }

        void RenderMirroredCoverage(PaintStroke stroke, MeshUV uv)
        {
            if (_mirroredCoverageRT == null) _mirroredCoverageRT = NewRT(Width, Height, _strokeRT.format, false, false, blend: true);
            ClearRT(_mirroredCoverageRT);
            void Include(PaintStroke path)
            {
                RenderStrokeCoverage(path, uv);
                var mat = _brushRenderer.Material;
                bool clip = path.clip && uv != null;
                if (clip)
                {
                    mat.SetTexture("_IslandTex", IslandIdTexture(uv));
                    mat.SetTexture("_ClipSet", ClipSetTexture(path.clipIslands, uv.islandCount));
                }
                mat.SetVector("_Params", new Vector4(1f, 0f, clip ? 1f : 0f, uv?.islandCount ?? 1));
                Graphics.Blit(_strokeRT, _mirroredCoverageRT, mat, 10);
            }

            Include(stroke);
            foreach (var path in stroke.mirrorPaths)
                if (path?.points?.Count > 0) Include(stroke.ForMirror(path));
        }

        RenderTexture _surfA, _surfB, _surfacePrefixRT;
        ComputeShader _surfacePadding;
        int _surfacePaddingKernel;
        PaintStroke _surfacePrefixRejectedStroke;
        Func<RenderTexture> _createSurfacePrefix;
        const long MaximumSurfacePrefixBytes = 32L * 1024 * 1024;

        void EnsureSurfacePadding()
        {
            if (_surfacePaddingChecked) return;
            PrepareSurfacePadding();
            _surfacePaddingChecked = true;
        }

        void CreateSurfaceTarget()
        {
            _surfA = NewRT(Width, Height, RenderTextureFormat.RGHalf, false, false, blend: true);
            _surfA.filterMode = FilterMode.Point;
        }

        void PrepareSurfacePadding()
        {
            _surfacePadding = null;
            if ((long)Width * Height < 512L * 512 || !SystemInfo.supportsComputeShaders || _strokeRT.format != RenderTextureFormat.RHalf ||
                !SystemInfo.IsFormatSupported(_strokeRT.graphicsFormat, UnityEngine.Experimental.Rendering.FormatUsage.LoadStore)) return;
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath("b761e15e20ce497ba14a3dbe59fe3c99"));
            if (shader == null || !shader.HasKernel("PadSurface")) return;
            int kernel = shader.FindKernel("PadSurface");
            if (!shader.IsSupported(kernel)) return;

            _brushRenderer.InvalidateLiveCoverage();
            if (RenderTexture.active == _strokeRT) RenderTexture.active = null;
            _strokeRT.Release();
            _strokeRT.enableRandomWrite = true;
            if (CreateWritableCoverage())
            {
                _surfacePadding = shader;
                _surfacePaddingKernel = kernel;
            }
            else RestoreRasterCoverage();
        }

        bool CreateWritableCoverage()
        {
            try
            {
                return _strokeRT.Create();
            }
            catch (Exception error) when (error is InvalidOperationException || error is UnityException || error is OutOfMemoryException)
            {
                return false;
            }
        }

        void RestoreRasterCoverage()
        {
            _surfacePadding = null;
            if (RenderTexture.active == _strokeRT) RenderTexture.active = null;
            _strokeRT.Release();
            _strokeRT.enableRandomWrite = false;
            if (!_strokeRT.Create())
                throw new InvalidOperationException(Loc.F("Avistry could not allocate a {0} × {1} {2} texture.", Width, Height, _strokeRT.format));
        }

        void ReleaseLiveSurfaceCoverage()
        {
            _brushRenderer?.InvalidateLiveSurfaceCoverage();
            Release(_surfacePrefixRT);
            _surfacePrefixRT = null;
        }

        bool UseLiveSurfaceCoverage(PaintStroke stroke, MeshUV mesh, float grainCellsPerUV)
        {
            bool eligible = _allowLiveCoverage && ReferenceEquals(stroke, LiveStroke) && !stroke.isFill && !stroke.IsBlend &&
                LiveMirror == null && (stroke.mirrorPaths == null || stroke.mirrorPaths.Count == 0) &&
                (_surfA.format == RenderTextureFormat.RGHalf || _surfA.format == RenderTextureFormat.ARGBHalf) &&
                (long)Width * Height * 2 <= MaximumSurfacePrefixBytes &&
                !ReferenceEquals(stroke, _surfacePrefixRejectedStroke) && SupportsTargetFormat(RenderTextureFormat.RHalf, false, true);
            if (!eligible) { ReleaseLiveSurfaceCoverage(); return false; }
            if (_brushRenderer.HasLiveSurfaceCoverage &&
                !_brushRenderer.CanContinueLiveSurfaceCoverage(stroke, _surfacePrefixRT, mesh, grainCellsPerUV))
                ReleaseLiveSurfaceCoverage();
            return true;
        }

        RenderTexture CreateLiveSurfacePrefix()
        {
            if (_surfacePrefixRT == null)
            {
                try
                {
                    _surfacePrefixRT = NewRT(Width, Height, RenderTextureFormat.RHalf, false, false, blend: true);
                    _surfacePrefixRT.filterMode = FilterMode.Point;
                }
                catch (Exception error) when (error is InvalidOperationException || error is UnityException || error is OutOfMemoryException)
                {
                    Release(_surfacePrefixRT);
                    _surfacePrefixRT = null;
                    _surfacePrefixRejectedStroke = LiveStroke;
                    return null;
                }
            }
            return _surfacePrefixRT;
        }

        void RenderSurfaceCoverage(PaintStroke s, MeshUV uv, float grainCellsPerUV)
        {
            if (_surfA == null)
            {
                EnsureSurfacePadding();
                CreateSurfaceTarget();
            }
            if (UseLiveSurfaceCoverage(s, uv, grainCellsPerUV))
            {
                try { _brushRenderer.RenderLiveSurfaceCoverage(s, _surfA, _surfacePrefixRT, uv, grainCellsPerUV,
                    _createSurfacePrefix ??= CreateLiveSurfacePrefix); }
                catch { ReleaseLiveSurfaceCoverage(); throw; }
            }
            else _brushRenderer.RenderSurface(s, _surfA, uv, grainCellsPerUV);
            int grow = Mathf.Clamp(Mathf.RoundToInt(Width / 512f), 2, 8);
            if (_surfacePadding != null && !_strokeRT.IsCreated() && !CreateWritableCoverage()) RestoreRasterCoverage();
            if (_surfacePadding != null)
            {
                _surfacePadding.SetInt("_Width", Width);
                _surfacePadding.SetInt("_Height", Height);
                _surfacePadding.SetInt("_Radius", grow);
                _surfacePadding.SetTexture(_surfacePaddingKernel, "_Surface", _surfA);
                _surfacePadding.SetTexture(_surfacePaddingKernel, "_Coverage", _strokeRT);
                _surfacePadding.Dispatch(_surfacePaddingKernel, (Width + 15) / 16, (Height + 15) / 16, 1);
                return;
            }
            if (_surfB == null)
            {
                _surfB = NewRT(Width, Height, _surfA.format, false, false);
                _surfB.filterMode = FilterMode.Point;
            }
            var mat = _brushRenderer.Material;
            RenderTexture a = _surfA, b = _surfB;
            for (int i = 1; i < grow; i++)
            {
                Graphics.Blit(a, b, mat, 8);
                (a, b) = (b, a);
            }
            Graphics.Blit(a, _strokeRT, mat, 8);
        }


        internal int FillSeedPiece(MeshUV geometry, Vector2 uv)
        {
            int x = Mathf.Min(Mathf.FloorToInt(uv.x * Width), Width - 1);
            int y = Mathf.Min(Mathf.FloorToInt(uv.y * Height), Height - 1);
            return IslandIdsCpu(geometry)[y * Width + x] - 1;
        }

        internal Color32[] SourcePixels() => SourceCpu();

        internal ushort[] IslandIds(MeshUV uv) => IslandIdsCpu(uv);

        internal ushort[] IslandIdsExact(MeshUV uv) => ExactIslandIdsCpu(uv);

        internal static bool Close(Color32 a, Color32 b, int tol) =>
            Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol && Mathf.Abs(a.a - b.a) <= tol;

        Color32[] SourceCpu()
        {
            if (_sourceCpu != null) return _sourceCpu;
            CancelSourcePreparation();
            using (new KeepActiveRT())
            {
                var rt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Texture2D texture = null;
                try
                {
                    Graphics.Blit(_source, rt);
                    RenderTexture.active = rt;
                    texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    _sourceCpu = texture.GetPixels32();
                }
                finally
                {
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            return _sourceCpu;
        }

        ushort[] _fillVal; bool[] _fillCovered; int[] _fillQueue; byte[] _fillBytes;

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

        Texture IslandIdTexture(MeshUV uv)
        {
            if (_islandIds != null) return _islandIds;
            var ids = IslandIdsCpu(uv);
            bool r16 = SystemInfo.SupportsTextureFormat(TextureFormat.R16);
            var texture = new Texture2D(Width, Height, r16 ? TextureFormat.R16 : TextureFormat.RFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point,
            };
            try
            {
                if (r16) texture.SetPixelData(ids, 0);
                else
                {
                    var pixels = texture.GetRawTextureData<float>();
                    for (int i = 0; i < ids.Length; i++) pixels[i] = ids[i] / 65535f;
                }
                texture.Apply(false, true);
                _islandIds = texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
            return _islandIds;
        }

        Texture ClipSetTexture(List<int> islands, int count)
        {
            count = Mathf.Max(1, count);
            _nextClipSetIslands.Clear();
            foreach (int island in islands)
                if (island >= 0 && island < count) _nextClipSetIslands.Add(island);
            if (_clipSet != null && _clipSet.width == count && _clipSetIslands.SetEquals(_nextClipSetIslands)) return _clipSet;
            if (_clipSet == null || _clipSet.width != count)
            {
                if (_clipSet != null) UnityEngine.Object.DestroyImmediate(_clipSet);
                _clipSet = new Texture2D(count, 1, TextureFormat.R8, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point,
                };
            }
            if (_clipSetBytes == null || _clipSetBytes.Length != count) _clipSetBytes = new byte[count];
            else Array.Clear(_clipSetBytes, 0, count);
            foreach (int island in _nextClipSetIslands) _clipSetBytes[island] = 255;
            _clipSet.SetPixelData(_clipSetBytes, 0);
            _clipSet.Apply(false);
            (_clipSetIslands, _nextClipSetIslands) = (_nextClipSetIslands, _clipSetIslands);
            _clipSetUploads++;
            return _clipSet;
        }

        void ResetPaintAuxiliaryCaches()
        {
            _clipSetBytes = null;
            _clipSetIslands.Clear();
            _nextClipSetIslands.Clear();
            _maskOverlaySource = null;
            _maskOverlayVersion = 0;
        }

        internal (int clipUploads, int maskOverlayRenders) AutoPaintCacheCounts() => (_clipSetUploads, _maskOverlayRenders);

        public enum PickSource
        {
            [InspectorName("Image (what you see)")] Image = 0,
            [InspectorName("Original texture")] Original = 1,
            [InspectorName("Current layer (paint only)")] CurrentLayer = 2,
            [InspectorName("Top layer (topmost paint there)")] TopLayer = 3,
        }

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
                    tex = _paint.TryGetValue(layer.id, out var pc) && pc.rt != null ? pc.rt : PaintFor(layer, meshUv);
                    weighted = true;
                }
                int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * Width), 0, Width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * Height), 0, Height - 1);
                _utilMat.SetVector("_Pick", new Vector4((x + 0.5f) / Width, (y + 0.5f) / Height, Mathf.Clamp(radius, 0, 12), weighted ? 1 : 0));
                var one = TemporaryRT(1, 1, RenderTextureFormat.ARGBHalf, readback: true);
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

        public Color[] SourceThumbnail(int size) => Thumbnail(_source, size);

        public Color[] ResultThumbnail(int size) => Thumbnail(Result, size);

        Color[] Thumbnail(Texture from, int size)
        {
            using (new KeepActiveRT())
            {
                var rt = TemporaryRT(size, size, RenderTextureFormat.ARGBHalf, readback: true);
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
                int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * Width), 0, Width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * Height), 0, Height - 1);
                var one = TemporaryRT(1, 1, RenderTextureFormat.ARGBHalf, readback: true);
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
            CancelIslandIdPreparation();
            InvalidateLiveStrokeCoverage();
            foreach (var c in _paint.Values) Release(c.rt);
            _paint.Clear();
            Release(_strokeRT); Release(_scratchRT); Release(_liveRT); Release(_dirRT); Release(_liveRT2); Release(_surfA); Release(_surfB);
            Release(_mirroredCoverageRT);
            _strokeRT = _scratchRT = _liveRT = _dirRT = _liveRT2 = _surfA = _surfB = null;
            _surfacePaddingChecked = _paintPreparationUnavailable = false;
            _mirroredCoverageRT = null;
            if (_islandIds != null) UnityEngine.Object.DestroyImmediate(_islandIds);
            if (_clipSet != null) UnityEngine.Object.DestroyImmediate(_clipSet);
            _islandIds = _clipSet = null;
            ResetPaintAuxiliaryCaches();
            _islandIdsCpu = _islandIdsExact = null;
            _islandCacheMesh = null;
            ClearPreparedIslandOwners();
            _fillVal = null; _fillCovered = null; _fillQueue = null; _fillBytes = null;
            _fillMirrorBytes = null;
        }

        static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        public void InvalidateGeometry()
        {
            InvalidateLivePrefix();
            Release(_scenePositions);
            _scenePositions = null;
            _scenePositionsMesh = null;
            foreach (var m in _masks.Values) if (m.tex != null) UnityEngine.Object.DestroyImmediate(m.tex);
            foreach (var g in _gradients.Values) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            _masks.Clear();
            _gradients.Clear();
            DisposePaint();
        }

        public Texture2D ReadResult(bool dither)
        {
            var target = QuantizeResult(dither);
            try { return ReadQuantizedTexture(target, true); }
            finally { ReleaseReadbackTarget(target); }
        }

        public void Dispose()
        {
            DisposePreparation();
            InvalidateGeometry();
            foreach (var l in _luts.Values) if (l != null) UnityEngine.Object.DestroyImmediate(l);
            _luts.Clear();
            _lutHash.Clear();
            if (_clear != null) UnityEngine.Object.DestroyImmediate(_clear);
            foreach (var rt in new[] { _source, _pingA, _pingB, Result, Preview, _sourcePreview, _coverageRT, _maskOverlayRT })
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
