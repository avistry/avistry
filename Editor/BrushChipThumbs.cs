using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    sealed class BrushChipThumbs : IDisposable
    {
        sealed class Entry
        {
            public BrushSettings settings = new BrushSettings();
            public BrushPreview preview = new BrushPreview();
            public long used;
        }

        const int MaximumEntries = 96;
        readonly Dictionary<BrushPreset, Entry> _byName = new Dictionary<BrushPreset, Entry>();
        long _clock;

        public Texture Get(BrushPreset p, int w, int h)
        {
            if (!_byName.TryGetValue(p, out var e))
            {
                if (_byName.Count >= MaximumEntries) EvictOldest();
                _byName[p] = e = new Entry();
                p.ApplyTo(e.settings);
                e.settings.color = Color.white;
            }
            e.used = ++_clock;
            return e.preview.Get(e.settings, w, h);
        }

        void EvictOldest()
        {
            BrushPreset oldest = null;
            long used = long.MaxValue;
            foreach (var pair in _byName)
                if (pair.Value.used < used) { oldest = pair.Key; used = pair.Value.used; }
            if (oldest == null) return;
            _byName[oldest].preview.Dispose();
            _byName.Remove(oldest);
        }

        public void Dispose()
        {
            foreach (var e in _byName.Values) e.preview.Dispose();
            _byName.Clear();
        }
    }
}
