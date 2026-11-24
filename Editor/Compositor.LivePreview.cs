using System;
using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class Compositor
    {
        readonly List<LivePrefixLayer> _livePrefixLayers = new List<LivePrefixLayer>();
        RenderTexture _livePrefixBuffer;
        PaintStroke _livePrefixStroke;
        IList<Layer> _livePrefixStack;
        MeshUV _livePrefixGeometry;
        Transform _livePrefixProjectionRoot;
        Matrix4x4 _livePrefixProjectionTransform;
        int _livePrefixPose, _livePrefixAssetStamp;
        bool _livePrefixHasBase, _livePrefixBaseVisible, _livePrefixProjecting, _livePrefixBoundaryClipped;

        internal void ComposeLivePreview(IList<Layer> layers, MeshUV uv, Layer liveLayer, int assetStamp) =>
            ComposeStack(layers, uv, liveLayer, assetStamp);

        internal void InvalidateLivePrefix()
        {
            _livePrefixBuffer = null;
            _livePrefixStroke = null;
            _livePrefixStack = null;
            _livePrefixGeometry = null;
            _livePrefixProjectionRoot = null;
            _livePrefixLayers.Clear();
        }

        int LivePrefixEnd(IList<Layer> layers, Layer active)
        {
            if (LiveStroke == null || active == null || string.IsNullOrEmpty(active.id) ||
                !LiveKeyMatches(active.id)) return -1;
            int index = layers.IndexOf(active);
            if (index <= 0) return -1;
            for (int i = 0; i < index; i++)
                if (layers[i] != null && LiveKeyMatches(layers[i].id)) return -1;
            if (active.clipToBelow)
                for (int i = index; i < layers.Count; i++)
                    if (layers[i] is Layer layer && (!layer.clipToBelow || layer is MixedMergeLayer) && NextIsClipped(layers, i))
                        return -1;
            return index;
        }

        bool LiveKeyMatches(string id) => LiveLayerId != null && (LiveLayerId == id ||
            LiveLayerId.Length == (id?.Length ?? 0) + MaskKeySuffix.Length && LiveLayerId.EndsWith(MaskKeySuffix, StringComparison.Ordinal) &&
            LiveLayerId.StartsWith(id ?? "", StringComparison.Ordinal));

        bool CanResumeLivePrefix(IList<Layer> layers, MeshUV uv, int end, int assetStamp)
        {
            if (end <= 0 || _livePrefixBuffer == null || !_livePrefixBuffer.IsCreated() ||
                !ReferenceEquals(_livePrefixStroke, LiveStroke) || !ReferenceEquals(_livePrefixStack, layers) ||
                _livePrefixLayers.Count != end || !ReferenceEquals(_livePrefixGeometry, uv) ||
                _livePrefixBoundaryClipped != layers[end].clipToBelow ||
                _livePrefixPose != (uv?.PoseVersion ?? 0) || _livePrefixAssetStamp != assetStamp) return false;
            if (layers[end].clipToBelow && _livePrefixBaseVisible && (_coverageRT == null || !_coverageRT.IsCreated())) return false;
            if (_livePrefixProjecting && (uv == null || uv.PoseChanged() || _livePrefixProjectionRoot == null ||
                uv.AvatarRoot() != _livePrefixProjectionRoot ||
                !_livePrefixProjectionRoot.worldToLocalMatrix.Equals(_livePrefixProjectionTransform))) return false;
            for (int i = 0; i < end; i++)
                if (!_livePrefixLayers[i].Matches(layers[i])) return false;
            return true;
        }

        void RememberLivePrefix(IList<Layer> layers, MeshUV uv, RenderTexture buffer, int end, int assetStamp,
                                bool haveBase, bool baseVisible)
        {
            _livePrefixBuffer = buffer;
            _livePrefixStroke = LiveStroke;
            _livePrefixStack = layers;
            _livePrefixGeometry = uv;
            _livePrefixPose = uv?.PoseVersion ?? 0;
            _livePrefixAssetStamp = assetStamp;
            _livePrefixBoundaryClipped = layers[end].clipToBelow;
            _livePrefixHasBase = haveBase;
            _livePrefixBaseVisible = baseVisible;
            _livePrefixProjecting = false;
            for (int i = 0; i < end; i++)
            {
                var layer = layers[i];
                _livePrefixLayers.Add(new LivePrefixLayer(layer));
                if (layer is DrawnGradientLayer gradient && gradient.sceneProjection && gradient.enabled && gradient.opacity > 0f)
                    _livePrefixProjecting = true;
            }
            if (!_livePrefixProjecting) return;
            _livePrefixProjectionRoot = uv?.AvatarRoot();
            if (_livePrefixProjectionRoot != null) _livePrefixProjectionTransform = _livePrefixProjectionRoot.worldToLocalMatrix;
        }

        readonly struct LivePrefixLayer
        {
            readonly Layer _layer;
            readonly Texture2D _image, _mask;
            readonly (uint updates, Hash128 hash) _imageVersion, _maskVersion;

            internal LivePrefixLayer(Layer layer)
            {
                _layer = layer;
                _image = ImageFor(layer);
                _mask = (layer as ImageLayer)?.mask;
                _imageVersion = TextureContentVersion(_image);
                _maskVersion = TextureContentVersion(_mask);
            }

            static Texture2D ImageFor(Layer layer) => layer is MixedMergeLayer merge ? merge.pixels : (layer as ImageLayer)?.image;
            static bool Same(Texture2D current, Texture2D captured, (uint updates, Hash128 hash) version) => ReferenceEquals(current, captured) &&
                (ReferenceEquals(current, null) || current != null && TextureContentVersion(current) == version);

            internal bool Matches(Layer layer) => ReferenceEquals(_layer, layer) &&
                Same(ImageFor(layer), _image, _imageVersion) && Same((layer as ImageLayer)?.mask, _mask, _maskVersion);
        }
    }
}
