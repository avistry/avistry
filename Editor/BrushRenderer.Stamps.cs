using System;
using UnityEngine;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer
    {
        internal bool PreviewStamp;
        BrushStampCache.Lease _stampLease;
        string _stampId;
        int _stampVersion;
        bool _stampPreview;

        internal void ConfigureStamp(BrushShape shape, bool preview)
        {
            if (shape.tip != BrushTip.Stamp)
            {
                _stampLease?.Dispose(); _stampLease = null;
                Material.SetFloat("_StampKind", 0);
                return;
            }
            if (!BrushStampLibrary.IsValid(shape.alphaId, shape.alphaVersion) ||
                !BrushStampLibrary.TryFind(shape.alphaId, out var definition))
                throw new InvalidOperationException(Loc.T("This brush shape version is not available. Update Avistry to open this edit."));
            if (_stampLease == null || _stampLease.Texture == null || _stampId != shape.alphaId ||
                _stampVersion != shape.alphaVersion || _stampPreview != preview)
            {
                _stampLease?.Dispose(); _stampLease = null;
                _stampId = null; _stampVersion = 0;
                var lease = BrushStampCache.Acquire(shape, preview);
                _stampLease = lease;
                _stampId = shape.alphaId; _stampVersion = shape.alphaVersion; _stampPreview = preview;
            }
            Material.SetTexture("_StampTex", _stampLease.Texture);
            Material.SetFloat("_StampKind", definition.ProceduralKind + 1);
        }

        internal static uint StampSeed(PaintStroke stroke, int site) => unchecked((uint)stroke.seed ^ (uint)site * 0x9E3779B9u);

        internal static float StampRandom(uint seed, uint channel)
        {
            BrushStampNoise.Pcg(seed, channel, 0x5EED, out uint value, out _, out _);
            return value / (float)uint.MaxValue;
        }
    }
}
