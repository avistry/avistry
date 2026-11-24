using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public static class FaceShadow
    {
        public const string Tag = "ATS face shadow";

        [Serializable]
        public class Settings
        {
            public int size = 1024;
            public bool swapSides;
        }

        public struct Result
        {
            public Color32[] pixels;
            public int width, height;
            public bool headBone;
            public string note;
        }

        public static Result Make(MeshUV uv, HashSet<int> onlyIslands, Material mat, int w, int h, Settings s,
            Func<float, bool> progress = null) => Prepare(uv, onlyIslands, mat, w, h, s, false)(progress, default);

        internal static Func<Func<float, bool>, System.Threading.CancellationToken, Result> Prepare(MeshUV uv,
            HashSet<int> onlyIslands, Material mat, int w, int h, Settings s, bool snapshot)
        {
            var res = new Result { width = w, height = h };
            var pos = uv.PosedCombinedCached();
            var family = mat != null ? ShaderProfiles.Detect(mat.shader) : ShaderFamily.Other;

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

            string overlapHint = Loc.T("Both halves of the face share the same part of the texture (mirrored UVs), so one map can't hold both sides; " +
                                 "the shadow may look patchy there.");
            if (snapshot)
            {
                uv = uv.CopyIslandRasterInput();
                pos = (Vector3[])pos.Clone();
                if (onlyIslands != null) onlyIslands = new HashSet<int>(onlyIslands);
            }
            return (progress, cancel) => RenderPixels(uv, onlyIslands, pos, family, centre, right, forward, sideSign,
                res, overlapHint, progress, cancel);
        }

        static Result RenderPixels(MeshUV uv, HashSet<int> onlyIslands, Vector3[] pos, ShaderFamily family,
            Vector3 centre, Vector3 right, Vector3 forward, float sideSign, Result res, string overlapHint,
            Func<float, bool> progress, System.Threading.CancellationToken cancel)
        {
            int w = res.width, h = res.height;
            int vCount = uv.uv.Length;
            var litR = new float[vCount];
            var litG = new float[vCount];
            for (int i = 0; i < vCount; i++)
            {
                var p = pos[i];
                if (float.IsNaN(p.x)) continue;
                var d = p - centre;
                float phi = Mathf.Atan2(Vector3.Dot(d, right), Vector3.Dot(d, forward)) * Mathf.Rad2Deg;
                litR[i] = Mathf.Clamp(90f + sideSign * phi, 0f, 180f);
                litG[i] = Mathf.Clamp(90f - sideSign * phi, 0f, 180f);
            }

            float Encode(float litAngle)
            {
                float c = Mathf.Cos(litAngle * Mathf.Deg2Rad);
                return family == ShaderFamily.LilToon ? Mathf.Clamp01(litAngle / 180f) : Mathf.Clamp01((1f - c) * 0.5f);
            }

            bool lil = family == ShaderFamily.LilToon;
            var pixels = new Color32[w * h];
            Array.Fill(pixels, new Color32(255, 255, lil ? (byte)255 : (byte)0, 255));
            var covered = new ushort[pixels.Length];
            var angle = new float[w * h];
            int conflicts = 0, coveredCount = 0;
            int polled = 0;
            bool cancelled = false;
            uv.RasterizeTriangles(w, h, t =>
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
                if (covered[px] != 0) { if (Mathf.Abs(angle[px] - lr) > 40f) conflicts++; }
                else coveredCount++;
                covered[px] = 1;
                angle[px] = lr;
                byte r = (byte)(Mathf.RoundToInt(Encode(lr) * 65535f) >> 8);
                byte g = (byte)(Mathf.RoundToInt(Encode(lg) * 65535f) >> 8);
                bool face = onlyIslands == null || onlyIslands.Count == 0 || onlyIslands.Contains(uv.triIsland[t]);
                pixels[px] = new Color32(r, g, lil && !face ? (byte)255 : (byte)0, 255);
            }, cancel);

            if (cancelled || (progress != null && progress(0.9f))) return res;
            MapPixels.PadEdges(pixels, covered, w, h, 8, false, cancel);
            res.pixels = pixels;
            if (coveredCount > 0 && conflicts > coveredCount * 0.05f)
                res.note = (res.note != null ? res.note + " " : "") +
                           overlapHint;
            return res;
        }

        public static Texture2D Save(Texture mainTex, Result r, out string path) =>
            MapBaker.SaveMap(mainTex, "FaceShadow", Tag, r.pixels, r.width, r.height, out path);

        internal static Texture2D SaveForTarget(Texture mainTex, Result r, string targetKey) =>
            MapBaker.SaveMap(mainTex, "FaceShadow", Tag, r.pixels, r.width, r.height, out _, targetKey: targetKey);

        public static string Property(Material m)
        {
            if (m == null || m.shader == null) return null;
            var f = ShaderProfiles.Detect(m.shader);
            if (f == ShaderFamily.LilToon && m.HasProperty("_ShadowStrengthMask") && m.HasProperty("_ShadowMaskType")) return "_ShadowStrengthMask";
            if (f == ShaderFamily.Poiyomi && m.HasProperty("_SDFShadingTexture") && m.HasProperty("_LightingMode")) return "_SDFShadingTexture";
            return null;
        }

        public static void Assign(Material m, Texture2D map)
        {
            string prop = Property(m);
            if (prop == null) return;
            Undo.RecordObject(m, "Use face shadow map");
            m.SetTexture(prop, map);
            if (prop == "_ShadowStrengthMask")
            {
                m.SetFloat("_ShadowMaskType", 2f);
                if (m.HasProperty("_UseShadow")) m.SetFloat("_UseShadow", 1f);
                EditorUtility.SetDirty(m);
                MaterialWriter.AfterLilToonEdit(new[] { m });
            }
            else
            {
                m.SetFloat("_LightingMode", 8f);
                foreach (var k in m.shaderKeywords)
                    if (k.StartsWith("_LIGHTINGMODE_")) m.DisableKeyword(k);
                m.EnableKeyword("_LIGHTINGMODE_SDF");
                EditorUtility.SetDirty(m);
                MaterialWriter.FixPoiyomiKeywords(new[] { m });
            }
        }

        public static bool IsMade(Texture tex) =>
            tex != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter imp && (imp.userData ?? "").StartsWith(Tag);
    }
}
