using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Small stroke pictures for the brush chips. One BrushPreview per preset object, made the first time a chip is drawn and
    /// kept until the window closes. A preset that was saved again is a new object, so it gets a fresh picture.
    /// </summary>
    sealed class BrushChipThumbs : IDisposable
    {
        sealed class Entry
        {
            public BrushSettings settings = new BrushSettings();
            public BrushPreview preview = new BrushPreview();
        }

        readonly Dictionary<BrushPreset, Entry> _byName = new Dictionary<BrushPreset, Entry>();

        /// <summary>The picture for a preset at w×h pixels. Re-renders only when the preset or the size changes.</summary>
        public Texture Get(BrushPreset p, int w, int h)
        {
            if (!_byName.TryGetValue(p, out var e))
            {
                if (_byName.Count > 48) Dispose(); // saved-again and deleted user brushes leave old objects behind; start over
                _byName[p] = e = new Entry();
                p.ApplyTo(e.settings);
                e.settings.color = Color.white; // the chip tints it; keeping it fixed keeps the picture stable when the brush colour changes
            }
            return e.preview.Get(e.settings, w, h);
        }

        public void Dispose()
        {
            foreach (var e in _byName.Values) e.preview.Dispose();
            _byName.Clear();
        }
    }
}
