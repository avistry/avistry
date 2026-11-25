using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class Compositor
    {
        readonly Texture2D _sourceAsset;

        internal RenderTexture TakeMixedMergePreview()
        {
            var preview = Result;
            Result = null;
            return preview;
        }

        internal static Texture2D CaptureMixedMergePixels(RenderTexture source)
        {
            using (new KeepActiveRT())
            {
                var texture = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                try
                {
                    RenderTexture.active = source;
                    texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                    texture.Apply(false, false);
                    return texture;
                }
                catch
                {
                    Object.DestroyImmediate(texture);
                    throw;
                }
            }
        }

        IList<Layer> ExpandIncompatibleMixedMerges(IList<Layer> layers, MeshUV uv)
        {
            List<Layer> expanded = null;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer is MixedMergeLayer merged && merged.enabled &&
                    (i != 0 || !merged.Matches(_sourceAsset, uv?.FaceLayout, SourceWidth, SourceHeight)))
                {
                    if (expanded == null)
                    {
                        expanded = new List<Layer>(layers.Count);
                        for (int earlier = 0; earlier < i; earlier++) expanded.Add(layers[earlier]);
                    }
                    if (merged.originals != null)
                        foreach (var original in merged.originals)
                            if (original != null && !(original is MixedMergeLayer)) expanded.Add(original);
                }
                else expanded?.Add(layer);
            }
            return expanded ?? layers;
        }

        void RenderMixedMerge(MixedMergeLayer layer, RenderTexture target, bool nextClips)
        {
            Graphics.Blit(layer.DisplayPixels, target);
            if (!nextClips) return;
            EnsureCoverageRT();
            Graphics.Blit(layer.DisplayPixels, _coverageRT, _utilMat, 7);
        }
    }
}
