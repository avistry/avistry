using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public partial class Compositor
    {
        const long MaximumSourcePreparationPixels = 2048L * 2048;
        static int s_sourceReadbackCount;
        SourceReadback _sourcePreparation;
        int _sourcePreparationRevision;
        bool _sourcePreparationUnavailable;
        bool? _sourceReadbackFlip;

        bool ContinueSourcePreparation()
        {
            if (_sourceCpu != null || _sourcePreparationUnavailable || _preparationDisposed ||
                IslandPreparationPixels < MinimumIslandPreparationPixels ||
                IslandPreparationPixels > MaximumSourcePreparationPixels) return false;
            if (_sourcePreparation != null) return true;
            if (s_sourceReadbackCount != 0) return false;
            if (!SystemInfo.supportsAsyncGPUReadback || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32) ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, FormatUsage.ReadPixels))
            {
                _sourcePreparationUnavailable = true;
                return false;
            }
            try { _sourcePreparation = new SourceReadback(this, !_sourceReadbackFlip.HasValue, _sourcePreparationRevision); }
            catch { _sourcePreparationUnavailable = true; }
            return _sourcePreparation != null;
        }

        void CancelSourcePreparation()
        {
            _sourcePreparationRevision++;
            _sourcePreparation?.Retire();
            _sourcePreparation = null;
        }

        sealed class SourceReadback
        {
            internal static readonly Color32[] ProbePixels =
            {
                new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255),
                new Color32(0, 0, 255, 255), new Color32(255, 255, 0, 255),
                new Color32(255, 0, 255, 255), new Color32(255, 255, 255, 255)
            };

            Compositor _owner;
            readonly int _revision;
            readonly bool _probe;
            RenderTexture _target;
            Texture2D _probeTexture;
            AsyncGPUReadbackRequest _request;
            bool _reloadLocked, _requestIssued;

            internal SourceReadback(Compositor owner, bool probe, int revision)
            {
                _owner = owner;
                _probe = probe;
                _revision = revision;
                EditorApplication.LockReloadAssemblies();
                _reloadLocked = true;
                s_sourceReadbackCount++;
                try
                {
                    using (new KeepActiveRT())
                    {
                        _target = NewRT(probe ? 2 : owner.Width, probe ? 3 : owner.Height,
                            RenderTextureFormat.ARGB32, false, false);
                        if (!SystemInfo.IsFormatSupported(_target.graphicsFormat, FormatUsage.ReadPixels))
                            throw new InvalidOperationException("Source readback format is unavailable.");
                        Texture source = owner._source;
                        if (probe)
                        {
                            _probeTexture = new Texture2D(2, 3, TextureFormat.RGBA32, false, true)
                                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                            _probeTexture.SetPixels32(ProbePixels);
                            _probeTexture.Apply(false);
                            source = _probeTexture;
                        }
                        Graphics.Blit(source, _target);
                        _request = AsyncGPUReadback.Request(_target, 0, TextureFormat.RGBA32);
                        _requestIssued = true;
                        _request.forcePlayerLoopUpdate = true;
                    }
                    EditorApplication.update += Tick;
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch
                {
                    if (_requestIssued)
                    {
                        Retire();
                        EditorApplication.update -= Tick;
                        EditorApplication.update += Tick;
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                    else Release();
                    throw;
                }
            }

            internal void Retire() => _owner = null;

            void Tick()
            {
                if (!_request.done && !_request.hasError)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                try
                {
                    var owner = _owner;
                    if (owner == null || owner._preparationDisposed || owner._sourcePreparationRevision != _revision ||
                        !ReferenceEquals(owner._sourcePreparation, this)) return;
                    if (_request.hasError)
                    {
                        owner._sourcePreparationUnavailable = true;
                        return;
                    }
                    var pixels = _request.GetData<Color32>().ToArray();
                    if (_probe)
                    {
                        bool same = MatchesProbe(pixels, false), flipped = MatchesProbe(pixels, true);
                        if (same == flipped) owner._sourcePreparationUnavailable = true;
                        else owner._sourceReadbackFlip = flipped;
                    }
                    else if (pixels.Length == owner.Width * owner.Height && owner._sourceCpu == null)
                    {
                        if (owner._sourceReadbackFlip == true) FlipRows(pixels, owner.Width, owner.Height);
                        owner._sourceCpu = pixels;
                    }
                    else owner._sourcePreparationUnavailable = true;
                }
                catch
                {
                    if (_owner != null) _owner._sourcePreparationUnavailable = true;
                }
                finally
                {
                    if (_owner != null && ReferenceEquals(_owner._sourcePreparation, this)) _owner._sourcePreparation = null;
                    _owner = null;
                    Release();
                }
            }

            static bool MatchesProbe(Color32[] pixels, bool flipped)
            {
                if (pixels.Length != ProbePixels.Length) return false;
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 2; x++)
                        if (!pixels[x + (flipped ? 2 - y : y) * 2].Equals(ProbePixels[x + y * 2])) return false;
                return true;
            }

            internal static void FlipRows(Color32[] pixels, int width, int height)
            {
                for (int y = 0; y < height / 2; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int a = y * width + x, b = (height - y - 1) * width + x;
                        (pixels[a], pixels[b]) = (pixels[b], pixels[a]);
                    }
            }

            void Release()
            {
                EditorApplication.update -= Tick;
                try
                {
                    using (new KeepActiveRT())
                    {
                        Compositor.Release(_target);
                        _target = null;
                        if (_probeTexture != null) UnityEngine.Object.DestroyImmediate(_probeTexture);
                        _probeTexture = null;
                    }
                }
                finally
                {
                    if (_reloadLocked)
                    {
                        _reloadLocked = false;
                        s_sourceReadbackCount--;
                        EditorApplication.UnlockReloadAssemblies();
                    }
                }
            }
        }
    }
}
