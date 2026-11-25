using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Avistry.Textures
{
    [InitializeOnLoad]
    internal static class BrushStampCache
    {
        const long BudgetBytes = 8L * 1024 * 1024;
        sealed class Entry { internal Texture2D texture; internal long bytes, touched; internal int pins; }
        internal sealed class Lease : IDisposable
        {
            Entry _entry;
            internal Texture2D Texture { get; }
            internal Lease(Texture2D texture, object entry)
            { Texture = texture; _entry = entry as Entry; if (_entry != null) _entry.pins++; }
            public void Dispose() { if (_entry == null) return; _entry.pins--; _entry = null; }
        }
        static readonly Dictionary<(string id, int version, int size, bool display), Entry> Entries =
            new Dictionary<(string, int, int, bool), Entry>();
        static long s_bytes, s_clock;
        static Task<byte[]> s_warmTask;
        static CancellationTokenSource s_warmCancellation;
        static int s_generation, s_warmGeneration;
        static (string id, int version) s_warmKey;
        static (string id, int version)? s_wanted, s_failed;

        static BrushStampCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.update += WarmTick;
        }

        internal static Texture2D Get(BrushShape shape, bool preview = false) => GetTexture(shape, preview ? 64 : 512, false);
        internal static Texture2D GetPreview(BrushShape shape) => GetTexture(shape, 64, true);

        internal static Lease Acquire(BrushShape shape, bool preview)
        {
            var texture = Get(shape, preview);
            Entries.TryGetValue((shape.alphaId, shape.alphaVersion, preview ? 64 : 512, false), out var entry);
            return new Lease(texture, entry);
        }

        internal static void RequestWarm(BrushShape shape)
        {
            if (shape?.tip != BrushTip.Stamp || !BrushStampLibrary.IsValid(shape.alphaId, shape.alphaVersion) ||
                !BrushStampLibrary.TryFind(shape.alphaId, out var definition) || definition.ProceduralKind != 0)
            { s_wanted = null; s_warmCancellation?.Cancel(); return; }
            if (!s_wanted.HasValue || s_wanted.Value != (shape.alphaId, shape.alphaVersion)) s_warmCancellation?.Cancel();
            s_wanted = (shape.alphaId, shape.alphaVersion);
            s_failed = null;
        }

        static void WarmTick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (s_warmTask != null)
            {
                if (!s_warmTask.IsCompleted) return;
                var task = s_warmTask;
                s_warmTask = null;
                s_warmCancellation?.Dispose();
                s_warmCancellation = null;
                try
                {
                    if (task.IsCanceled) { }
                    else if (task.IsFaulted)
                    {
                        _ = task.Exception;
                        if (s_warmGeneration == s_generation && s_wanted.HasValue && s_wanted.Value == s_warmKey)
                            s_failed = s_warmKey;
                    }
                    else if (s_warmGeneration == s_generation && s_wanted.HasValue && s_wanted.Value == s_warmKey &&
                        !Entries.ContainsKey((s_warmKey.id, s_warmKey.version, 512, false)))
                        Upload((s_warmKey.id, s_warmKey.version, 512, false), task.Result);
                }
                catch
                {
                    if (s_warmGeneration == s_generation && s_wanted.HasValue && s_wanted.Value == s_warmKey)
                        s_failed = s_warmKey;
                }
            }
            if (!s_wanted.HasValue || s_wanted == s_failed ||
                Entries.ContainsKey((s_wanted.Value.id, s_wanted.Value.version, 512, false))) return;
            s_warmKey = s_wanted.Value;
            s_warmGeneration = s_generation;
            string id = s_warmKey.id;
            s_warmCancellation = new CancellationTokenSource();
            var token = s_warmCancellation.Token;
            s_warmTask = Task.Run(() => BrushStampLibrary.GenerateForWarm(id, 512, token), token);
            _ = s_warmTask.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        static Texture2D GetTexture(BrushShape shape, int size, bool display)
        {
            if (shape == null || !BrushStampLibrary.IsValid(shape.alphaId, shape.alphaVersion))
                throw new InvalidOperationException(Loc.T("This brush shape version is not available. Update Avistry to open this edit."));
            BrushStampLibrary.TryFind(shape.alphaId, out var definition);
            if (!display && definition.ProceduralKind != 0) return Texture2D.whiteTexture;
            var key = (shape.alphaId, shape.alphaVersion, size, display);
            if (Entries.TryGetValue(key, out var old))
            {
                if (old.texture != null) { old.touched = ++s_clock; return old.texture; }
                Entries.Remove(key); s_bytes -= old.bytes;
            }
            byte[] pixels;
            if (definition.ProceduralKind == 0) pixels = BrushStampLibrary.Generate(shape.alphaId, size);
            else
            {
                pixels = new byte[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(BrushStampLibrary.Procedural(
                        definition.ProceduralKind, (x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1, .5f, 0)) * 255);
            }
            return Upload(key, pixels);
        }

        static Texture2D Upload((string id, int version, int size, bool display) key, byte[] pixels)
        {
            bool narrow = !key.display && SystemInfo.SupportsTextureFormat(TextureFormat.R8) &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Sample) &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Linear);
            var format = narrow ? TextureFormat.R8 : TextureFormat.RGBA32;
            long bytes = 0;
            for (int level = key.size; level > 0; level >>= 1) bytes += (long)level * level * (narrow ? 1 : 4);
            Evict(bytes);
            var texture = new Texture2D(key.size, key.size, format, true, true)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            try
            {
                if (narrow) texture.SetPixelData(pixels, 0);
                else
                {
                    var packed = new Color32[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) packed[i] = key.display
                        ? new Color32(255, 255, 255, pixels[i]) : new Color32(pixels[i], pixels[i], pixels[i], pixels[i]);
                    texture.SetPixels32(packed);
                }
                texture.Apply(true, true);
                Entries[key] = new Entry { texture = texture, bytes = bytes, touched = ++s_clock };
                s_bytes += bytes;
                return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }

        static void Evict(long incoming)
        {
            while (s_bytes + incoming > BudgetBytes)
            {
                (string id, int version, int size, bool display) victim = default;
                Entry oldest = null;
                foreach (var pair in Entries)
                    if (pair.Value.pins == 0 && (oldest == null || pair.Value.touched < oldest.touched))
                    { victim = pair.Key; oldest = pair.Value; }
                if (oldest == null) throw new InvalidOperationException(Loc.T("The active brush shape cache is full. Close another Avistry window and try again."));
                Entries.Remove(victim); s_bytes -= oldest.bytes;
                if (oldest.texture != null) UnityEngine.Object.DestroyImmediate(oldest.texture);
            }
        }

        internal static void Clear()
        {
            s_generation++;
            s_warmCancellation?.Cancel();
            s_wanted = s_failed = null;
            foreach (var entry in Entries.Values) if (entry.texture != null) UnityEngine.Object.DestroyImmediate(entry.texture);
            Entries.Clear(); s_bytes = 0;
        }

        internal static (int entries, long bytes) AutoState() => (Entries.Count, s_bytes);
    }
}
