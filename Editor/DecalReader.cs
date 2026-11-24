using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    public static class DecalReader
    {
        public class Decal
        {
            public string label;
            public string shown;
            public ImageLayer layer;
            public string blocked;
            public List<string> notes = new List<string>();
        }

        public static List<Decal> Read(Material m)
        {
            var list = new List<Decal>();
            if (m == null || m.shader == null) return list;
            switch (ShaderProfiles.Detect(m.shader))
            {
                case ShaderFamily.Poiyomi: for (int i = 0; i < 4; i++) ReadPoiyomi(m, i, list); break;
                case ShaderFamily.LilToon: ReadLil(m, "2nd", "Main colour layer 2", list); ReadLil(m, "3rd", "Main colour layer 3", list); break;
            }
            return list;
        }

        static float F(Material m, string p, float fallback = 0f) => m.HasProperty(p) ? m.GetFloat(p) : fallback;
        static Vector4 V(Material m, string p, Vector4 fallback) => m.HasProperty(p) ? m.GetVector(p) : fallback;
        static bool On(Material m, string p) => m.HasProperty(p) && m.GetFloat(p) > 0.5f;

        static void MainTextureNotes(Material m, Decal d)
        {
            if (m.HasProperty("_MainTex"))
            {
                Vector2 s = m.GetTextureScale("_MainTex"), o = m.GetTextureOffset("_MainTex");
                if ((s - Vector2.one).sqrMagnitude > 1e-6f || o.sqrMagnitude > 1e-6f)
                    d.blocked = Loc.T("the main texture uses tiling or an offset, so the decal doesn't sit at a fixed place on it");
            }
            if (m.HasProperty("_Color"))
            {
                var c = m.GetColor("_Color");
                if (Mathf.Abs(c.r - 1) + Mathf.Abs(c.g - 1) + Mathf.Abs(c.b - 1) > 0.02f)
                    d.notes.Add(Loc.T("The material's main colour isn't white, so it will now tint the decal too."));
            }
        }


        static void ReadPoiyomi(Material m, int n, List<Decal> list)
        {
            string s = n == 0 ? "" : n.ToString();
            if (!On(m, "_DecalEnabled" + s)) return;
            var tex = m.HasProperty("_DecalTexture" + s) ? m.GetTexture("_DecalTexture" + s) as Texture2D : null;
            var d = new Decal { label = $"Decal {n + 1}" + (tex != null ? $" ({tex.name})" : ""), shown = Loc.F("Decal {0}", n + 1) + (tex != null ? $" ({tex.name})" : "") };
            list.Add(d);
            if (tex == null) { d.blocked = Loc.T("it has no picture (or uses a video / render texture)"); return; }
            if (Mathf.RoundToInt(F(m, $"_DecalTexture{s}UV")) != 0) { d.blocked = Loc.T("it isn't placed with the main UV map (UV0)"); return; }
            MainTextureNotes(m, d);
            if (d.blocked != null) return;

            Vector2 pos = V(m, "_DecalPosition" + s, new Vector4(0.5f, 0.5f));
            Vector2 scale = V(m, "_DecalScale" + s, Vector4.one);
            var so = V(m, "_DecalSideOffset" + s, Vector4.zero);
            int aspect = Mathf.RoundToInt(F(m, $"_Decal{n}AspectRatioMode"));
            if (aspect != 0 && tex.width > 0 && tex.height > 0)
            {
                float w = tex.width, h = tex.height;
                var k = w > h ? new Vector2(1f, h / w) : new Vector2(w / h, 1f);
                if (aspect == 2) k = new Vector2(1f / k.x, 1f / k.y);
                scale = Vector2.Scale(scale, k);
            }
            var colour = m.HasProperty("_DecalColor" + s) ? m.GetColor("_DecalColor" + s) : Color.white;
            float alpha = Mathf.Clamp01(Mathf.Clamp01(F(m, "_DecalBlendAlpha" + s, 1f)) * F(m, "_DecalAlphaIntensity" + s, 1f));
            int sym = Mathf.RoundToInt(F(m, "_DecalSymmetryMode" + s));
            int blend = Mathf.RoundToInt(F(m, "_DecalBlendType" + s));
            var st = new Vector4(1, 1, 0, 0);
            if (m.HasProperty("_DecalTexture" + s))
            {
                Vector2 ts = m.GetTextureScale("_DecalTexture" + s), to = m.GetTextureOffset("_DecalTexture" + s);
                st = new Vector4(ts.x, ts.y, to.x, to.y);
            }

            var layer = new ImageLayer
            {
                name = d.label,
                image = tex,
                position = pos + new Vector2((so.y - so.x) * 0.5f, (so.w - so.z) * 0.5f),
                size = scale + new Vector2(so.x + so.y, so.z + so.w),
                rotation = Mathf.Repeat(F(m, "_DecalRotation" + s) + 180f, 360f) - 180f,
                tiled = On(m, "_DecalTiled" + s),
                copy = sym == 1 ? ImageCopy.Mirror : sym == 2 ? ImageCopy.Copy : ImageCopy.None,
                tint = new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(colour.a)),
                opacity = alpha,
                imageST = st,
                flattenedFrom = $"Poiyomi {d.label}",
            };
            layer.blend = PoiBlend(blend, d);
            var mask = m.HasProperty("_DecalMask") ? m.GetTexture("_DecalMask") as Texture2D : null;
            if (mask != null)
            {
                layer.mask = mask;
                layer.maskChannel = Mathf.Clamp(Mathf.RoundToInt(F(m, $"_Decal{n}MaskChannel", n)), 0, 3);
                if (Mathf.RoundToInt(F(m, "_DecalMaskUV")) != 0) d.notes.Add(Loc.T("The decal mask uses another UV map, so the masked area may differ."));
            }
            layer.offFloats.Add("_DecalEnabled" + s);
            if (n > 0) layer.offKeywords.Add("POI_DECAL_" + n);

            if (colour.maxColorComponent > 1.001f) d.notes.Add(Loc.T("Its colour is brighter than white (HDR); that extra brightness can't be stored in the texture."));
            if (F(m, "_DecalEmissionStrength" + s) > 0.001f) d.notes.Add(Loc.T("It glows (emission). A flattened decal doesn't glow."));
            if (Mathf.Abs(F(m, "_DecalRotationSpeed" + s)) > 1e-4f) d.notes.Add(Loc.T("It spins. A flattened decal stays still."));
            if (V(m, "_DecalTexturePan" + s, Vector4.zero).sqrMagnitude > 1e-8f) d.notes.Add(Loc.T("Its picture scrolls. A flattened decal stays still."));
            if (On(m, "_DecalHueShiftEnabled" + s)) d.notes.Add(Loc.T("Its hue shift isn't carried over."));
            if (On(m, "_DecalColorAdjustEnabled" + s)) d.notes.Add(Loc.T("Its colour adjustment isn't carried over."));
            if (Mathf.RoundToInt(F(m, "_DecalMirroredUVMode" + s)) != 0) d.notes.Add(Loc.T("It uses a mirrored-UV mode (left / right side only), which a texture can't reproduce: check both sides afterwards."));
            if (Mathf.RoundToInt(F(m, "_DecalOverrideAlpha" + s)) != 0) d.notes.Add(Loc.T("It changes the material's transparency; that part isn't carried over."));
            if (Mathf.Abs(F(m, $"_Decal{n}Depth")) > 1e-4f) d.notes.Add(Loc.T("Its depth (parallax) effect is lost."));
            string theme = n == 0 ? "_DecalColorThemeIndex" : $"_DecalColor{n}ThemeIndex";
            if (Mathf.RoundToInt(F(m, theme)) != 0) d.notes.Add(Loc.T("It uses a theme colour; the decal's own colour is used instead."));
            d.layer = layer;
        }

        static BlendMode PoiBlend(int type, Decal d)
        {
            switch (type)
            {
                case 1: return BlendMode.Darken;
                case 2: return BlendMode.Multiply;
                case 5: return BlendMode.Lighten;
                case 6: return BlendMode.Screen;
                case 7: return BlendMode.Subtract;
                case 8: return BlendMode.Add;
                case 9: return BlendMode.Overlay;
                case 20: d.notes.Add(Loc.T("Poiyomi's “Mixed” blend has no exact match here; Add is used, so it may look a little brighter.")); return BlendMode.Add;
                default: return BlendMode.Normal;
            }
        }


        static void ReadLil(Material m, string k, string label, List<Decal> list)
        {
            if (!On(m, $"_UseMain{k}Tex")) return;
            string texProp = $"_Main{k}Tex";
            var tex = m.HasProperty(texProp) ? m.GetTexture(texProp) as Texture2D : null;
            var d = new Decal { label = label + (tex != null ? $" ({tex.name})" : ""), shown = (k == "2nd" ? Loc.T("Main colour layer 2") : Loc.T("Main colour layer 3")) + (tex != null ? $" ({tex.name})" : "") };
            list.Add(d);
            if (tex == null) { d.blocked = Loc.T("it has no picture"); return; }
            if (Mathf.RoundToInt(F(m, $"_Main{k}Tex_UVMode")) != 0) { d.blocked = Loc.T("it isn't placed with the main UV map (UV0)"); return; }
            if (On(m, $"_Main{k}TexIsMSDF")) { d.blocked = Loc.T("it's an MSDF (sharp text) texture, which only the shader can draw"); return; }
            MainTextureNotes(m, d);
            if (d.blocked != null) return;

            Vector2 ts = m.GetTextureScale(texProp), to = m.GetTextureOffset(texProp);
            if (Mathf.Abs(ts.x) < 1e-5f || Mathf.Abs(ts.y) < 1e-5f) { d.blocked = Loc.T("its size is zero"); return; }
            float angle = F(m, $"_Main{k}TexAngle");
            var size = new Vector2(1f / ts.x, 1f / ts.y);
            var p = new Vector2((0.5f - to.x) / ts.x, (0.5f - to.y) / ts.y) - new Vector2(0.5f, 0.5f);
            float cs = Mathf.Cos(-angle), sn = Mathf.Sin(-angle);
            var centre = new Vector2(p.x * cs - p.y * sn, p.x * sn + p.y * cs) + new Vector2(0.5f, 0.5f);
            bool isDecal = On(m, $"_Main{k}TexIsDecal");
            bool copy = On(m, $"_Main{k}TexShouldCopy"), flipCopy = On(m, $"_Main{k}TexShouldFlipCopy");
            var colour = m.HasProperty($"_Color{k}") ? m.GetColor($"_Color{k}") : Color.white;

            var layer = new ImageLayer
            {
                name = label,
                image = tex,
                position = centre,
                size = size,
                rotation = Mathf.Repeat(angle * Mathf.Rad2Deg + 180f, 360f) - 180f,
                tiled = !isDecal,
                copy = isDecal && copy ? (flipCopy ? ImageCopy.MirrorFlipped : ImageCopy.Mirror) : ImageCopy.None,
                tint = new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(colour.a)),
                flattenedFrom = $"lilToon {label}",
            };
            switch (Mathf.RoundToInt(F(m, $"_Main{k}TexBlendMode")))
            {
                case 1: layer.blend = BlendMode.Add; break;
                case 2: layer.blend = BlendMode.Screen; break;
                case 3: layer.blend = BlendMode.Multiply; break;
                default: layer.blend = BlendMode.Normal; break;
            }
            var mask = m.HasProperty($"_Main{k}BlendMask") ? m.GetTexture($"_Main{k}BlendMask") as Texture2D : null;
            if (mask != null) { layer.mask = mask; layer.maskChannel = 0; }
            layer.offFloats.Add($"_UseMain{k}Tex");

            if (colour.maxColorComponent > 1.001f) d.notes.Add(Loc.T("Its colour is brighter than white (HDR); that extra brightness can't be stored in the texture."));
            if (F(m, $"_Main{k}EnableLighting", 1f) < 0.999f) d.notes.Add(Loc.T("It's set to ignore lighting (it stays bright in the dark). A flattened decal is lit like the rest."));
            if (V(m, $"_Main{k}Tex_ScrollRotate", Vector4.zero).sqrMagnitude > 1e-8f) d.notes.Add(Loc.T("It scrolls or spins. A flattened decal stays still."));
            if (Mathf.RoundToInt(F(m, $"_Main{k}TexAlphaMode")) != 0) d.notes.Add(Loc.T("It changes the material's transparency; that part isn't carried over."));
            if (On(m, $"_Main{k}TexIsLeftOnly") || On(m, $"_Main{k}TexIsRightOnly") || On(m, $"_Main{k}TexShouldFlipMirror"))
                d.notes.Add(Loc.T("It uses a left / right-side-only setting, which a texture can't reproduce: check both sides afterwards."));
            if (flipCopy && Mathf.Abs(angle) > 1e-4f) d.notes.Add(Loc.T("Flipped copy together with rotation may sit slightly differently."));
            var anim = V(m, $"_Main{k}TexDecalAnimation", new Vector4(1, 1, 1, 30));
            if (Mathf.Abs(anim.x - 1) + Mathf.Abs(anim.y - 1) > 1e-4f) d.notes.Add(Loc.T("It's an animated sprite sheet. Only one frame layout is flattened, so check the result."));
            if (V(m, $"_Main{k}DissolveParams", Vector4.zero).x > 0.5f) d.notes.Add(Loc.T("Its dissolve effect isn't carried over."));
            d.layer = layer;
        }

        public static void TurnOffFlattened(Material m, IEnumerable<Layer> layers)
        {
            if (m == null || layers == null) return;
            foreach (var l in layers)
            {
                if (!(l is ImageLayer img) || !img.enabled || img.opacity <= 0f || img.image == null) continue;
                foreach (var p in img.offFloats) if (m.HasProperty(p)) m.SetFloat(p, 0f);
                foreach (var k in img.offKeywords) m.DisableKeyword(k);
            }
        }
    }
}
