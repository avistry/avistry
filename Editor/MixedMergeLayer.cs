using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    [Serializable]
    internal sealed class MixedMergeLayer : Layer
    {
        [SerializeField] internal Texture2D pixels;
        [NonSerialized] internal RenderTexture preview;
        internal Texture DisplayPixels => preview != null ? (Texture)preview : pixels;

        internal void ReleasePreview()
        {
            if (preview == null) return;
            preview.Release();
            UnityEngine.Object.DestroyImmediate(preview);
            preview = null;
        }

        [SerializeField] internal Texture2D source;
        [SerializeField] internal string uvLayout;
        [SerializeReference] internal List<Layer> originals = new List<Layer>();

        internal bool Matches(Texture2D texture, string layout, int width, int height) =>
            DisplayPixels != null && source != null && source == texture && (uvLayout ?? "") == (layout ?? "") &&
            DisplayPixels.width == width && DisplayPixels.height == height;

        public override int Pass => -1;
        public override string TypeName => Loc.T("Merged layer");
        public override void Apply(Material material, Func<Gradient, Texture2D> lut) { }
    }
}
