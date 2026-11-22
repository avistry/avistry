using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Face shadow map ("SDF" face shading) made from the face mesh. Toon shaders read it instead of the mesh normals,
    /// so the shadow on a face sweeps across it cleanly as the light turns from the front to the side.
    ///
    /// Each texel gets the light angle (0° = light from the front, 180° = from behind) up to which it stays lit. It is
    /// taken from where the point sits around the head: a point at azimuth φ (0 = the middle of the face, + = toward the
    /// side the light comes from) stays lit until 90° + φ, as on a round head.
    ///
    /// Formats (both read from the shader code):
    ///   lilToon (Shadow › Mask type SDF, map in _ShadowStrengthMask): R = light from the object's -X side, G = from +X,
    ///     B = 0 (use the map, not the normals), A = 1. Lit while 0.5·cos θ + 0.5·value + 0.25 > border (0.5), so the edge only moves for light 60°..120° from the front: the map is an even sweep over that range.
    ///   Poiyomi (Lighting type SDF, _SDFShadingTexture): R only, for light from the "Left Direction" side, mirrored in U
    ///     for the other side. Lit while value > (1 − cos θ) / 2.
    /// The same file works for both channel layouts; the values follow the material's shader.
    /// </summary>
    public static class FaceShadow
    {
        public const string Tag = "ATS face shadow";

        [Serializable]
        public class Settings
        {
            public int size = 1024;       // 0 = same as the texture
            public bool swapSides;        // the shadow comes from the wrong side: swap left and right
        }

        public struct Result
        {
            public Color32[] pixels;
            public int width, height;
            public bool headBone;         // the avatar's head bone was used as the centre
            public string note;           // something the user should know (or null)
        }

        public static Result Make(MeshUV uv, HashSet<int> onlyIslands, Material mat, int w, int h, Settings s,
            Func<float, bool> progress = null)
        {
            var res = new Result { width = w, height = h };
            var pos = uv.PosedCombinedCached();
            var family = mat != null ? ShaderProfiles.Detect(mat.shader) : ShaderFamily.Other;

            // Head frame: the avatar faces its root's forward (VRChat avatars face +Z); the head bone is the centre.
            var root = uv.AvatarRoot();
            var anim = root != null ? root.GetComponent<Animator>() : null;
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            var forward = root != null ? root.forward : Vector3.forward;
            forward.y = 0;
            forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);

            bool Use(int tri) => onlyIslands == null || onlyIslands.Count == 0 || onlyIslands.Contains(uv.triIsland[tri]);

            Vector3 centre;
            if (head != null) { centre = head.position; res.headBone = true; }
            else
            {
                var b = new Bounds();
                bool any = false;
                for (int t = 0; t < uv.TriangleCount; t++)
                {
                    if (!Use(t)) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        var p = pos[uv.tris[t * 3 + k]];
                        if (float.IsNaN(p.x)) continue;
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                centre = b.center;
            }

            // Which side the shader's R channel is for: its object -X (lilToon) or its Left Direction (Poiyomi).
            var part = uv.parts.Count > 0 ? uv.parts[0] : null;
            var space = part?.renderer is SkinnedMeshRenderer smr && smr.rootBone != null ? smr.rootBone : part?.renderer?.transform;
            var leftLocal = family == ShaderFamily.Poiyomi && mat.HasProperty("_SDFLeft") ? (Vector3)mat.GetVector("_SDFLeft") : Vector3.left;
            var fwdLocal = family == ShaderFamily.Poiyomi && mat.HasProperty("_SDFForward") ? (Vector3)mat.GetVector("_SDFForward") : Vector3.forward;
            float sideSign = 1f;
            if (space != null)
            {
                var sideWorld = space.TransformDirection(leftLocal);
                sideSign = Vector3.Dot(sideWorld, right) >= 0 ? 1f : -1f;
                var shaderFwd = space.TransformDirection(fwdLocal);
                shaderFwd.y = 0;
                if (shaderFwd.sqrMagnitude > 1e-8f && Vector3.Angle(shaderFwd, forward) > 35f)
                {
                    var want = space.InverseTransformDirection(forward).normalized;
                    res.note = family == ShaderFamily.Poiyomi
                        ? Loc.F("The material's SDF Forward Direction doesn't point where the face looks. Set it to ({0:0.##}, {1:0.##}, {2:0.##}) in the material (Shading › SDF).", want.x, want.y, want.z)
                        : family == ShaderFamily.LilToon
                            ? Loc.T("lilToon takes the face direction from the part's own axes, and this part's axes don't face forward, so the shadow may come from the wrong direction.")
                            : null;
                }
            }
            if (s.swapSides) sideSign = -sideSign;

            // Per vertex: the angle up to which it stays lit, for light from the R side and from the G side.
            int vCount = uv.uv.Length;
            var litR = new float[vCount];
            var litG = new float[vCount];
            for (int i = 0; i < vCount; i++)
            {
                var p = pos[i];
                if (float.IsNaN(p.x)) continue;
                var d = p - centre;
                float phi = Mathf.Atan2(Vector3.Dot(d, right), Vector3.Dot(d, forward)) * Mathf.Rad2Deg; // + = toward the right
                litR[i] = Mathf.Clamp(90f + sideSign * phi, 0f, 180f);
                litG[i] = Mathf.Clamp(90f - sideSign * phi, 0f, 180f);
            }

            float Encode(float litAngle)
            {
                float c = Mathf.Cos(litAngle * Mathf.Deg2Rad);
                // lilToon can only move the shadow edge while the light is 60°..120° from the front (border 0.5), so it gets the
                // whole face as an even 0..1 sweep (the edge crosses the face in that range). Poiyomi compares with (1 - cos θ) / 2: exact.
                return family == ShaderFamily.LilToon ? Mathf.Clamp01(litAngle / 180f) : Mathf.Clamp01((1f - c) * 0.5f);
            }

            var r = new ushort[w * h];
            var g = new ushort[w * h];
            var bl = new ushort[w * h];
            var covered = new bool[w * h];
            var angle = new float[w * h];
            int conflicts = 0, coveredCount = 0;
            int polled = 0;
            bool cancelled = false;
            // Rasterize has no cancel hook: poll in the per-triangle filter and skip the rest once cancelled.
            uv.Rasterize(w, h, t =>
            {
                if (cancelled) return false;
                if (progress != null && (++polled & 255) == 0 && progress(0.8f * t / Mathf.Max(1, uv.TriangleCount))) { cancelled = true; return false; }
                return true;
            }, (px, t, a, b, c) =>
            {
                int i0 = uv.tris[t * 3], i1 = uv.tris[t * 3 + 1], i2 = uv.tris[t * 3 + 2];
                if (float.IsNaN(pos[i0].x) || float.IsNaN(pos[i1].x) || float.IsNaN(pos[i2].x)) return;
                float lr = litR[i0] * a + litR[i1] * b + litR[i2] * c;
                float lg = litG[i0] * a + litG[i1] * b + litG[i2] * c;
                if (covered[px]) { if (Mathf.Abs(angle[px] - lr) > 40f) conflicts++; }
                else coveredCount++;
                covered[px] = true;
                angle[px] = lr;
                r[px] = (ushort)Mathf.RoundToInt(Encode(lr) * 65535f);
                g[px] = (ushort)Mathf.RoundToInt(Encode(lg) * 65535f);
                bl[px] = Use(t) ? (ushort)0 : (ushort)65535; // lilToon: islands left out keep their normal shading
            });

            if (cancelled || (progress != null && progress(0.9f))) { res.pixels = null; return res; } // cancelled: nothing made
            var c1 = (bool[])covered.Clone();
            var c2 = (bool[])covered.Clone();
            MeshUV.Dilate(r, covered, w, h, 8);
            MeshUV.Dilate(g, c1, w, h, 8);
            MeshUV.Dilate(bl, c2, w, h, 8);

            var o = new Color32[w * h];
            bool lil = family == ShaderFamily.LilToon;
            for (int i = 0; i < o.Length; i++)
            {
                byte vr = (byte)(r[i] >> 8), vg = (byte)(g[i] >> 8);
                if (!covered[i]) { vr = 255; vg = 255; }
                o[i] = lil ? new Color32(vr, vg, c2[i] ? (byte)(bl[i] >> 8) : (byte)255, 255) : new Color32(vr, vg, 0, 255);
            }
            res.pixels = o;

            if (coveredCount > 0 && conflicts > coveredCount * 0.05f)
                res.note = (res.note != null ? res.note + " " : "") +
                           Loc.T("Both halves of the face share the same part of the texture (mirrored UVs), so one map can't hold both sides; " +
                                 "the shadow may look patchy there.");
            return res;
        }

        public static Texture2D Save(Texture mainTex, Result r, out string path)
        {
            path = MapBaker.MapPath(mainTex, "FaceShadow", Tag);
            return MapBaker.SavePng(path, r.pixels, r.width, r.height, Tag, false);
        }

        /// <summary>The property the material's shader reads a face shadow map from, or null.</summary>
        public static string Property(Material m)
        {
            if (m == null || m.shader == null) return null;
            var f = ShaderProfiles.Detect(m.shader);
            if (f == ShaderFamily.LilToon && m.HasProperty("_ShadowStrengthMask") && m.HasProperty("_ShadowMaskType")) return "_ShadowStrengthMask";
            if (f == ShaderFamily.Poiyomi && m.HasProperty("_SDFShadingTexture") && m.HasProperty("_LightingMode")) return "_SDFShadingTexture";
            return null;
        }

        /// <summary>Puts the map on the material and switches its face shading to SDF (undoable).</summary>
        public static void Assign(Material m, Texture2D map)
        {
            string prop = Property(m);
            if (prop == null) return;
            Undo.RecordObject(m, "Use face shadow map");
            m.SetTexture(prop, map);
            if (prop == "_ShadowStrengthMask")
            {
                m.SetFloat("_ShadowMaskType", 2f); // SDF
                if (m.HasProperty("_UseShadow")) m.SetFloat("_UseShadow", 1f);
                EditorUtility.SetDirty(m);
                MaterialWriter.AfterLilToonEdit(new[] { m });
            }
            else
            {
                m.SetFloat("_LightingMode", 8f); // SDF
                foreach (var k in m.shaderKeywords)
                    if (k.StartsWith("_LIGHTINGMODE_")) m.DisableKeyword(k);
                m.EnableKeyword("_LIGHTINGMODE_SDF");
                EditorUtility.SetDirty(m);
                MaterialWriter.FixPoiyomiKeywords(new[] { m });
            }
        }

        /// <summary>True when this tool made the texture.</summary>
        public static bool IsMade(Texture tex) =>
            tex != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter imp && (imp.userData ?? "").StartsWith(Tag);
    }
}
