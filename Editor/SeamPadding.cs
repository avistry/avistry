using System.Threading;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class SeamPadding
    {
        internal static string CloseWarning => Loc.T("Some pieces sit very close together, so their edge colours may mix slightly.");

        public static int Radius(int w, int h) => Mathf.Max(4, Mathf.Max(w, h) / 128);

        public static string Apply(Texture2D tex, MeshUV uv)
        {
            var pixels = tex.GetPixels32();
            bool close = ApplyPixels(pixels, uv, tex.width, tex.height, default);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return close ? CloseWarning : null;
        }

        internal static bool ApplyPixels(Color32[] pixels, MeshUV uv, int w, int h, CancellationToken cancel)
        {
            int radius = Radius(w, h);
            var ids = uv.RasterPaddedIslandIds(w, h, 0, cancel);
            MapPixels.PadEdges(pixels, ids, w, h, radius, true, cancel);
            var idsR = uv.RasterPaddedIslandIds(w, h, radius, cancel);
            for (int y = 0; y < h; y++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    ushort a = idsR[i];
                    if (a == 0) continue;
                    if (x + 1 < w && idsR[i + 1] != 0 && idsR[i + 1] != a) return true;
                    if (y + 1 < h && idsR[i + w] != 0 && idsR[i + w] != a) return true;
                }
            }
            return false;
        }
    }
}
