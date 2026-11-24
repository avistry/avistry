using UnityEngine;

namespace Avistry.Textures
{
    internal static class BrushPenGeometry
    {
        internal static Vector3 FrameX(Vector3 normal)
        {
            var axis = Vector3.Cross(Vector3.up, normal).normalized;
            return axis.sqrMagnitude > .001f ? axis : Vector3.Cross(Vector3.forward, normal).normalized;
        }

        internal static Vector2 WorldToTexture(MeshUV mesh, int triangle, Vector3 direction, int width, int height)
        {
            if (mesh == null || triangle < 0 || triangle >= mesh.TriangleCount) return Vector2.zero;
            var world = mesh.PosedCombinedCached(); int first = triangle * 3;
            int a = mesh.tris[first], b = mesh.tris[first + 1], c = mesh.tris[first + 2];
            var x = world[b] - world[a]; var y = world[c] - world[a];
            float xx = Vector3.Dot(x, x), yy = Vector3.Dot(y, y), xy = Vector3.Dot(x, y), determinant = xx * yy - xy * xy;
            if (Mathf.Abs(determinant) < 1e-20f) return Vector2.zero;
            float dx = Vector3.Dot(direction, x), dy = Vector3.Dot(direction, y);
            var uv = (mesh.uv[b] - mesh.uv[a]) * ((dx * yy - dy * xy) / determinant) +
                (mesh.uv[c] - mesh.uv[a]) * ((dy * xx - dx * xy) / determinant);
            return new Vector2(uv.x * width, uv.y * height);
        }

        internal static Vector3 TextureToWorld(MeshUV mesh, int triangle, Vector2 direction, int width, int height)
        {
            if (mesh == null || triangle < 0 || triangle >= mesh.TriangleCount || width <= 0 || height <= 0) return Vector3.zero;
            int first = triangle * 3;
            int a = mesh.tris[first], b = mesh.tris[first + 1], c = mesh.tris[first + 2];
            var x = mesh.uv[b] - mesh.uv[a]; var y = mesh.uv[c] - mesh.uv[a];
            float determinant = x.x * y.y - x.y * y.x;
            if (Mathf.Abs(determinant) < 1e-20f) return Vector3.zero;
            var uv = new Vector2(direction.x / width, direction.y / height);
            var world = mesh.PosedCombinedCached();
            return (world[b] - world[a]) * ((uv.x * y.y - uv.y * y.x) / determinant) +
                (world[c] - world[a]) * ((uv.y * x.x - uv.x * x.y) / determinant);
        }

        internal static Vector2 Mirror(MeshUV mesh, SymmetryMap map, int source, int destination, int reflection,
            Vector2 sample, bool surface, bool backFace, int width, int height)
        {
            if (sample.y < .5f || !float.IsFinite(sample.x) ||
                !mesh.SurfacePoint(source, Vector3.zero, out _, out var normal) ||
                !mesh.SurfacePoint(destination, Vector3.zero, out _, out var targetNormal)) return Vector2.zero;
            if (backFace) { normal = -normal; targetNormal = -targetNormal; }
            float angle = sample.x * Mathf.Deg2Rad;
            var heading = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var axis = FrameX(normal);
            var world = surface ? axis * heading.x + Vector3.Cross(normal, axis) * heading.y :
                TextureToWorld(mesh, source, heading, width, height);
            if ((reflection & 1) != 0) world = Vector3.Reflect(world, map.normal);
            if ((reflection & 2) != 0) world = Vector3.Reflect(world, map.secondNormal);
            axis = FrameX(targetNormal);
            heading = surface ? new Vector2(Vector3.Dot(world, axis), Vector3.Dot(world, Vector3.Cross(targetNormal, axis))) :
                WorldToTexture(mesh, destination, world, width, height);
            return heading.sqrMagnitude < 1e-20f ? Vector2.zero :
                new Vector2(Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg, 1f);
        }
    }
}
