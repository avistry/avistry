using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public static class ModelMatch
    {
        [Serializable]
        public struct View
        {
            public bool valid;
            public Vector3 point;
            public Vector3 camPos;
            public Quaternion camRot;
            public float fov, orthoSize, near, far, aspect;
            public bool ortho;
            public Renderer renderer;
            public int submesh;
            public Vector2 uv;
            public Color texel;

            public static View From(Camera cam, Vector3 point, Renderer r, int submesh, Vector2 uv, Color texel) => new View
            {
                valid = true, point = point, renderer = r, submesh = submesh, uv = uv, texel = texel,
                camPos = cam.transform.position, camRot = cam.transform.rotation,
                fov = cam.fieldOfView, orthoSize = cam.orthographicSize, near = cam.nearClipPlane, far = cam.farClipPlane,
                aspect = cam.pixelHeight > 0 ? cam.pixelWidth / (float)cam.pixelHeight : 1.5f, ortho = cam.orthographic,
            };
        }

        public struct Result
        {
            public bool done;
            public Color goal, achieved;
            public float difference;
            public bool reached;
            public int pair;
            public string note;
        }

        public const float GoodEnough = 2f;

        public static bool Measure(View v, out Color colour, out string why)
        {
            colour = Color.black;
            why = null;
            if (!v.valid) { why = Loc.T("The point isn't picked yet."); return false; }
            const int H = 360, N = 9;
            float W = Mathf.Clamp(H * v.aspect, 64, 1280);
            var go = new GameObject("ATS Measure Camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(N, N, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            Texture2D read = null;
            var prev = RenderTexture.active;
            try
            {
                var cam = go.AddComponent<Camera>();
                var sv = SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) cam.CopyFrom(sv.camera);
                cam.cameraType = CameraType.Game;
                cam.ResetProjectionMatrix();
                cam.transform.SetPositionAndRotation(v.camPos, v.camRot);
                cam.orthographic = v.ortho;
                cam.orthographicSize = v.orthoSize;
                cam.fieldOfView = v.fov;
                cam.nearClipPlane = v.near;
                cam.farClipPlane = v.far;
                cam.aspect = W / H;
                var vp = cam.WorldToViewportPoint(v.point);
                if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) { why = Loc.T("The point is outside the camera view it was picked from."); return false; }
                float x0 = vp.x * 2f - 1f, y0 = vp.y * 2f - 1f, sx = W / N, sy = H / (float)N;
                var zoom = Matrix4x4.identity;
                zoom.m00 = sx; zoom.m03 = -sx * x0;
                zoom.m11 = sy; zoom.m13 = -sy * y0;
                cam.projectionMatrix = zoom * cam.projectionMatrix;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                RenderTexture.active = rt;
                read = new Texture2D(3, 3, TextureFormat.RGBAHalf, false, true);
                read.ReadPixels(new Rect(N / 2 - 1, N / 2 - 1, 3, 3), 0, 0, false);
                read.Apply(false);
                Color sum = Color.clear;
                foreach (var c in read.GetPixels()) sum += c;
                var avg = sum / 9f;
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                var shown = new Color(Mathf.Clamp01(avg.r), Mathf.Clamp01(avg.g), Mathf.Clamp01(avg.b), 1f);
                colour = linear ? shown.gamma : shown;
                return true;
            }
            finally
            {
                RenderTexture.active = prev;
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        public static float Difference(Color a, Color b) => Vector3.Distance(Recolour.ToOklab(a), Recolour.ToOklab(b)) * 100f;

        public static Vector3 Step(Vector3 target, Vector3 rendered, Vector3 goal, ref Vector3 slope, float damping = 0.85f)
        {
            var next = target;
            for (int c = 0; c < 3; c++)
                next[c] = target[c] + damping * (goal[c] - rendered[c]) / Mathf.Max(0.02f, slope[c]);
            return next;
        }

        public static void Learn(ref Vector3 slope, Vector3 t0, Vector3 r0, Vector3 t1, Vector3 r1)
        {
            for (int c = 0; c < 3; c++)
            {
                float dt = t1[c] - t0[c];
                if (Mathf.Abs(dt) > 1e-3f) slope[c] = Mathf.Clamp((r1[c] - r0[c]) / dt, 0.02f, 20f);
            }
        }

        public static Vector3 Lin(Color g) { var l = g.linear; return new Vector3(l.r, l.g, l.b); }
        public static Color Gam(Vector3 l) => new Color(l.x, l.y, l.z, 1f).gamma;
    }
}
