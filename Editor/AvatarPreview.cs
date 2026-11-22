using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Shows the working texture on the avatar with MaterialPropertyBlocks (nothing is written to assets).
    /// While active, NDMF's proxy previews (Modular Avatar / Avatar Optimizer) are paused through
    /// NDMFPreview.DisablePreviewDepth, because they render copies that would hide this preview.
    /// </summary>
    public class AvatarPreview
    {
        struct Entry
        {
            public Renderer renderer;
            public int index;
            public bool hadOtherOverrides;
            public Texture original;
            public List<string> props;   // this slot's material properties that show the texture
        }

        readonly List<Entry> _entries = new List<Entry>();
        bool _ndmfPaused;

        public bool Active => _entries.Count > 0;

        public void Apply(List<TargetResolver.SlotRef> slots, List<string> props, Texture original, Texture preview) =>
            Apply(slots.Select(s => (s, props)).ToList(), original, preview);

        /// <summary>Shows the preview on every slot, each with its own material's properties (meshes sharing one texture).</summary>
        public void Apply(List<(TargetResolver.SlotRef slot, List<string> props)> slots, Texture original, Texture preview)
        {
            if (Active && !SameSlots(slots)) Clear();
            var mpb = new MaterialPropertyBlock();

            if (!Active)
            {
                foreach (var (s, props) in slots)
                {
                    if (s.renderer == null) continue;
                    s.renderer.GetPropertyBlock(mpb, s.materialIndex);
                    _entries.Add(new Entry
                    {
                        renderer = s.renderer, index = s.materialIndex, hadOtherOverrides = !mpb.isEmpty,
                        original = original, props = new List<string>(props),
                    });
                }
                PauseNdmf(true);
            }

            foreach (var e in _entries)
            {
                if (e.renderer == null) continue;
                e.renderer.GetPropertyBlock(mpb, e.index);
                foreach (var p in e.props) mpb.SetTexture(p, preview);
                e.renderer.SetPropertyBlock(mpb, e.index);
            }
            SceneView.RepaintAll();
        }

        bool SameSlots(List<(TargetResolver.SlotRef slot, List<string> props)> slots) =>
            slots.Count(s => s.slot.renderer != null) == _entries.Count &&
            slots.All(s => s.slot.renderer == null ||
                           _entries.Any(e => e.renderer == s.slot.renderer && e.index == s.slot.materialIndex && e.props.SequenceEqual(s.props)));

        public void Clear()
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var e in _entries)
            {
                if (e.renderer == null) continue;
                if (!e.hadOtherOverrides)
                {
                    e.renderer.SetPropertyBlock(new MaterialPropertyBlock(), e.index);
                }
                else
                {
                    // Another tool also uses a property block here: only put our properties back.
                    e.renderer.GetPropertyBlock(mpb, e.index);
                    foreach (var p in e.props) if (e.original != null) mpb.SetTexture(p, e.original);
                    e.renderer.SetPropertyBlock(mpb, e.index);
                }
            }
            _entries.Clear();
            PauseNdmf(false);
            SceneView.RepaintAll();
        }

        // NDMF is optional and reached via reflection.

        static PropertyInfo _ndmfDepth;
        static bool _ndmfLookedUp;

        void PauseNdmf(bool pause)
        {
            if (pause == _ndmfPaused) return;
            var prop = NdmfDepthProperty();
            if (prop == null) { _ndmfPaused = false; return; }
            try
            {
                int depth = (int)prop.GetValue(null);
                prop.SetValue(null, Math.Max(0, depth + (pause ? 1 : -1)));
                _ndmfPaused = pause;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Avistry] Could not toggle NDMF preview: " + ex.Message);
            }
        }

        static PropertyInfo NdmfDepthProperty()
        {
            if (_ndmfLookedUp) return _ndmfDepth;
            _ndmfLookedUp = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("nadena.dev.ndmf.preview.NDMFPreview", false);
                if (t == null) continue;
                _ndmfDepth = t.GetProperty("DisablePreviewDepth", BindingFlags.Public | BindingFlags.Static);
                break;
            }
            return _ndmfDepth;
        }
    }
}
