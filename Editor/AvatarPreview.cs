using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public class AvatarPreview
    {
        struct Entry
        {
            public Renderer renderer;
            public int index;
            public MaterialPropertyBlock originalBlock;
            public List<string> props;
        }

        readonly List<Entry> _entries = new List<Entry>();
        bool _ndmfPaused;

        public bool Active => _entries.Count > 0;

        public void Apply(List<TargetResolver.SlotRef> slots, List<string> props, Texture original, Texture preview) =>
            Apply(slots.Select(s => (s, props)).ToList(), original, preview);

        public void Apply(List<(TargetResolver.SlotRef slot, List<string> props)> slots, Texture original, Texture preview)
        {
            if (Active && !SameSlots(slots)) Clear();
            var mpb = new MaterialPropertyBlock();

            if (!Active)
            {
                foreach (var (s, props) in slots)
                {
                    if (s.renderer == null) continue;
                    var originalBlock = new MaterialPropertyBlock();
                    s.renderer.GetPropertyBlock(originalBlock, s.materialIndex);
                    _entries.Add(new Entry
                    {
                        renderer = s.renderer, index = s.materialIndex,
                        originalBlock = originalBlock.isEmpty ? null : originalBlock,
                        props = new List<string>(props),
                    });
                }
                PauseNdmf(true);
            }

            foreach (var e in _entries)
            {
                if (e.renderer == null) continue;
                if (e.originalBlock == null) e.renderer.GetPropertyBlock(mpb);
                else e.renderer.GetPropertyBlock(mpb, e.index);
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
            foreach (var e in _entries)
            {
                if (e.renderer == null) continue;
                e.renderer.SetPropertyBlock(e.originalBlock, e.index);
            }
            _entries.Clear();
            PauseNdmf(false);
            SceneView.RepaintAll();
        }


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
