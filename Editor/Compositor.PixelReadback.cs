using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Avistry.Textures
{
    public partial class Compositor
    {
        RenderTexture QuantizeResult(bool dither)
        {
            using var active = new KeepActiveRT();
            var target = NewRT(Width, Height, RenderTextureFormat.ARGB32, false, false);
            try
            {
                _utilMat.SetFloat("_Dither", dither ? 1 : 0);
                Graphics.Blit(Result, target, _utilMat, 2);
                return target;
            }
            catch { ReleaseReadbackTarget(target); throw; }
        }

        static void ReleaseReadbackTarget(RenderTexture target)
        {
            if (target == null) return;
            target.Release();
            Object.DestroyImmediate(target);
        }

        static Texture2D ReadQuantizedTexture(RenderTexture target, bool upload)
        {
            using var active = new KeepActiveRT();
            RenderTexture.active = target;
            var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                if (upload) tex.Apply(false);
                return tex;
            }
            catch { Object.DestroyImmediate(tex); throw; }
        }

        static Color32[] ReadQuantizedPixels(RenderTexture target)
        {
            var tex = ReadQuantizedTexture(target, false);
            try { return tex.GetRawTextureData<Color32>().ToArray(); }
            finally { Object.DestroyImmediate(tex); }
        }

        internal Task<Color32[]> ReadResultPixelsAsync(bool dither, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            return PixelReadback.Start(QuantizeResult(dither), cancel);
        }

        sealed class PixelReadback
        {
            static bool? s_flip;
            static bool s_unavailable;
            readonly TaskCompletionSource<Color32[]> _result = new TaskCompletionSource<Color32[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly RenderTexture _target;
            readonly CancellationToken _cancel;
            RenderTexture _probe;
            AsyncGPUReadbackRequest _request;

            PixelReadback(RenderTexture target, CancellationToken cancel) { _target = target; _cancel = cancel; }

            internal static Task<Color32[]> Start(RenderTexture target, CancellationToken cancel)
            {
                if (s_unavailable || !SystemInfo.supportsAsyncGPUReadback ||
                    !SystemInfo.IsFormatSupported(target.graphicsFormat, FormatUsage.ReadPixels) ||
                    !SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, FormatUsage.ReadPixels))
                {
                    try { return Task.FromResult(ReadQuantizedPixels(target)); }
                    finally { ReleaseReadbackTarget(target); }
                }
                var pending = new PixelReadback(target, cancel);
                EditorApplication.LockReloadAssemblies();
                EditorApplication.update += pending.Tick;
                try
                {
                    if (!s_flip.HasValue) pending.MakeProbe();
                    pending.Request(pending._probe != null ? pending._probe : target);
                }
                catch { pending.Fallback(); }
                return pending._result.Task;
            }

            void Request(RenderTexture target)
            {
                _request = AsyncGPUReadback.Request(target, 0, GraphicsFormat.R8G8B8A8_UNorm);
            }

            void MakeProbe()
            {
                using var active = new KeepActiveRT();
                _probe = NewRT(2, 3, RenderTextureFormat.ARGB32, false, false);
                var pattern = new Texture2D(2, 3, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
                try
                {
                    pattern.SetPixels32(SourceReadback.ProbePixels);
                    pattern.Apply(false);
                    Graphics.Blit(pattern, _probe);
                }
                finally { Object.DestroyImmediate(pattern); }
            }

            void Tick()
            {
                if (!_request.done && !_request.hasError) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                if (_cancel.IsCancellationRequested) { _result.TrySetCanceled(); Release(); return; }
                if (_request.hasError) { Fallback(); return; }
                try
                {
                    var data = _request.GetData<Color32>();
                    if (_probe != null)
                    {
                        var actual = data.ToArray();
                        var expected = ReadQuantizedPixels(_probe);
                        bool same = actual.SequenceEqual(expected);
                        if (actual.Length == expected.Length) SourceReadback.FlipRows(actual, 2, 3);
                        bool flipped = actual.SequenceEqual(expected);
                        if (same == flipped) { s_unavailable = true; Fallback(); return; }
                        s_flip = flipped;
                        ReleaseReadbackTarget(_probe);
                        _probe = null;
                        Request(_target);
                        return;
                    }
                    if (data.Length != (long)_target.width * _target.height) { Fallback(); return; }
                    var pixels = data.ToArray();
                    if (s_flip == true) SourceReadback.FlipRows(pixels, _target.width, _target.height);
                    _result.TrySetResult(pixels);
                }
                catch { Fallback(); return; }
                Release();
            }

            void Fallback()
            {
                try
                {
                    if (_cancel.IsCancellationRequested) _result.TrySetCanceled();
                    else _result.TrySetResult(ReadQuantizedPixels(_target));
                }
                catch (Exception error) { _result.TrySetException(error); }
                finally { Release(); }
            }

            void Release()
            {
                EditorApplication.update -= Tick;
                try { ReleaseReadbackTarget(_probe); ReleaseReadbackTarget(_target); }
                finally { EditorApplication.UnlockReloadAssemblies(); }
            }
        }
    }
}
