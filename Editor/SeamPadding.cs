using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Fills the empty space around pieces with their edge colours, so mip maps and filtering don't pull the background into seams.</summary>
    internal static class SeamPadding
    {
        static string CloseWarning => Loc.T("Some pieces sit very close together, so their edge colours may mix slightly.");

        public static int Radius(int w, int h) => Mathf.Max(4, Mathf.Max(w, h) / 128);

        /// <summary>Pads RGB in place (alpha kept). Returns one warning line when pieces sit closer than the padding, else null.</summary>
        public static string Apply(Texture2D tex, MeshUV uv)
        {
            int w = tex.width, h = tex.height, r = Radius(w, h);
            var ids0 = uv.RasterIslandIds(w, h, 0); // 0 = no piece
            var covered = new float[ids0.Length];
            for (int i = 0; i < covered.Length; i++) covered[i] = ids0[i] != 0 ? 1f : 0f;

            var px = tex.GetPixels32();
            var alpha = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) alpha[i] = px[i].a;
            MapBaker.DilateColours(px, covered, w, h, r);
            for (int i = 0; i < px.Length; i++) px[i].a = alpha[i];
            tex.SetPixels32(px);
            tex.Apply(false);

            var idsR = uv.RasterIslandIds(w, h, r);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    ushort a = idsR[i];
                    if (a == 0) continue;
                    if (x + 1 < w && idsR[i + 1] != 0 && idsR[i + 1] != a) return CloseWarning;
                    if (y + 1 < h && idsR[i + w] != 0 && idsR[i + w] != a) return CloseWarning;
                }
            return null;
        }
    }
}
