using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Avistry.Textures
{
    internal static class StylePack
    {
        internal const string Extension = "atspack";
        const int Version = 1;
        const int MaxTargets = 128, MaxLayers = 4096, MaxImages = 512;
        const int MaxManifestBytes = 64 * 1024 * 1024, MaxImageBytes = 192 * 1024 * 1024;
        const long MaxArtBytes = 1024L * 1024 * 1024;
        const long MaxDecodedPixels = 256L * 1024 * 1024;
        const string ManifestName = "manifest.json";

        [Serializable]
        internal class Binding
        {
            public string[] rendererPath;
            public string rendererKind;
            public int materialIndex;
        }

        [Serializable]
        internal sealed class Entry
        {
            public string name, rendererKind, sourceName, uvLayout;
            public string[] rendererPath, properties;
            public int materialIndex, uvChannel, textureRole, sourceWidth, sourceHeight;
            public Binding[] geometry;
            public LayerData[] layers;
        }

        [Serializable]
        internal sealed class LayerData
        {
            public string kind, data;
            public Artwork image, mask;
        }

        internal enum ArtworkState { None = 0, Included = 1, Missing = 2, AvatarAsset = 3, Generated = 4, Unreadable = 5 }

        [Serializable]
        internal sealed class Artwork
        {
            public int id;
            public string name;
            public ArtworkState state;
        }

        [Serializable]
        internal sealed class ImageData
        {
            public int id, width, height, filter, wrap;
            public bool srgb;
            public string name, sha256;
        }

        [Serializable]
        internal sealed class Manifest
        {
            public int version;
            public string name;
            public Entry[] entries;
            public ImageData[] images;
        }

        internal enum LayerIssue { Unreadable, LayoutChanged, ImageUnavailable, MaskUnavailable }

        internal sealed class Edit : IDisposable
        {
            internal readonly List<Layer> Layers = new List<Layer>();
            internal readonly List<(string layer, LayerIssue issue, Artwork artwork)> Issues = new();
            internal readonly Dictionary<int, Texture2D> Textures = new();

            public void Dispose()
            {
                foreach (var texture in Textures.Values)
                    if (texture != null && !EditorUtility.IsPersistent(texture)) Object.DestroyImmediate(texture);
                Textures.Clear();
            }
        }

        static readonly Dictionary<string, Type> LayerTypes = TextureRecipe.LayerTypes.Select(type => type.create().GetType())
            .ToDictionary(type => type.Name, StringComparer.Ordinal);

        internal static void AddMaterialTextures(Material material, HashSet<Texture> protectedTextures)
        {
            if (material == null || material.shader == null) return;
            foreach (string property in material.GetTexturePropertyNames())
                if (material.GetTexture(property) is Texture texture) protectedTextures.Add(texture);
        }

        internal static Manifest Write(string path, string name, IReadOnlyList<(Entry entry, TextureRecipe recipe)> sources,
            HashSet<Texture> protectedTextures, Func<int, int, bool> cancelled)
        {
            if (sources == null || sources.Count == 0 || sources.Count > MaxTargets)
                throw new InvalidDataException("Invalid number of edits.");
            if (File.Exists(path)) throw new IOException("The pack file already exists.");
            protectedTextures = new HashSet<Texture>(protectedTextures ?? Enumerable.Empty<Texture>());
            foreach (var (_, recipe) in sources)
            {
                protectedTextures.Add(recipe.sourceTexture);
                protectedTextures.Add(recipe.bakedTexture);
                AddMaterialTextures(recipe.sourceMaterial, protectedTextures);
                AddMaterialTextures(recipe.bakedMaterial, protectedTextures);
            }

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var manifest = new Manifest { name = name, version = Version, entries = sources.Select(item => item.entry).ToArray() };
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    var images = new List<ImageData>();
                    var artworkByTexture = new Dictionary<Texture2D, Artwork>();
                    long imageBytes = 0;

                    Artwork AddArtwork(Texture2D texture, bool generated)
                    {
                        if (generated) return MarkArtwork(texture, ArtworkState.Generated);
                        if (texture == null) return MarkArtwork(null, ArtworkState.Missing);
                        string sourcePath = AssetDatabase.GetAssetPath(texture);
                        if (protectedTextures.Contains(texture) ||
                            AssetImporter.GetAtPath(sourcePath) is TextureImporter importer && importer.textureType == TextureImporterType.NormalMap)
                            return MarkArtwork(texture, ArtworkState.AvatarAsset);
                        if (artworkByTexture.TryGetValue(texture, out var existing)) return existing;
                        var art = MarkArtwork(texture, ArtworkState.Unreadable);
                        artworkByTexture.Add(texture, art);
                        if (images.Count >= MaxImages) return art;
                        if (cancelled?.Invoke(images.Count, MaxImages) == true) throw new OperationCanceledException();
                        byte[] png;
                        try { png = FullSizePng(texture, sourcePath); }
                        catch (Exception error) when (error is IOException || error is ArgumentException || error is UnityException) { return art; }
                        if (png == null || png.Length > MaxImageBytes || imageBytes + png.Length > MaxArtBytes ||
                            !PngSize(png, out int width, out int height)) return art;
                        int id = images.Count + 1;
                        WriteEntry(zip, ImagePath(id), png);
                        art.id = id;
                        art.state = ArtworkState.Included;
                        imageBytes += png.Length;
                        images.Add(new ImageData
                        {
                            id = id, name = texture.name, width = width, height = height,
                            srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat),
                            filter = (int)texture.filterMode, wrap = (int)texture.wrapMode, sha256 = Fingerprint(png),
                        });
                        return art;
                    }

                    for (int i = 0; i < sources.Count; i++)
                    {
                        if (cancelled?.Invoke(i, sources.Count) == true) throw new OperationCanceledException();
                        var layers = new List<LayerData>();
                        foreach (var layer in sources[i].recipe.layers)
                            StoreLayer(layer, true, layers, AddArtwork);
                        if (layers.Count == 0 || layers.Count > MaxLayers) throw new InvalidDataException("Invalid number of layers.");
                        manifest.entries[i].layers = layers.ToArray();
                    }
                    manifest.images = images.ToArray();
                    byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest));
                    if (json.Length > MaxManifestBytes) throw new InvalidDataException("The edit data is too large.");
                    WriteEntry(zip, ManifestName, json);
                }
                File.Move(temp, path);
                return manifest;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        static void StoreLayer(Layer layer, bool enabled, List<LayerData> records, Func<Texture2D, bool, Artwork> addArtwork)
        {
            if (layer == null) return;
            if (layer is MixedMergeLayer merged)
            {
                foreach (var child in merged.originals ?? Enumerable.Empty<Layer>())
                    if (!(child is MixedMergeLayer)) StoreLayer(child, enabled && merged.enabled && merged.opacity > 0f, records, addArtwork);
                return;
            }
            var kind = layer.GetType().Name;
            if (!LayerTypes.TryGetValue(kind, out var type) || type != layer.GetType())
                throw new InvalidDataException("Unsupported layer type.");

            var copy = (Layer)JsonUtility.FromJson(JsonUtility.ToJson(layer), type);
            copy.enabled &= enabled;
            var record = new LayerData { kind = kind };
            if (layer is ImageLayer original)
            {
                bool derived = !string.IsNullOrEmpty(original.generated) || !string.IsNullOrEmpty(original.flattenedFrom);
                record.image = original.image == null && !string.IsNullOrEmpty(original.missingPackImage)
                    ? MissingArtwork(original.missingPackImage) : addArtwork(original.image, derived);
                if (original.mask != null) record.mask = addArtwork(original.mask, derived);
                else if (original.NeedsPackMask) record.mask = MissingArtwork(original.missingPackMask);
            }
            ClearProjectReferences(copy);
            record.data = JsonUtility.ToJson(copy);
            records.Add(record);
        }

        static void ClearProjectReferences(Layer layer)
        {
            if (layer is ImageLayer image)
            {
                image.image = image.mask = null;
                image.missingPackImage = image.missingPackMask = null;
                image.offFloats = new List<string>();
                image.offKeywords = new List<string>();
            }
            if (layer is ColourMatchLayer match)
            {
                match.source = null;
                match.sourceTexture = null;
            }
        }

        internal static Manifest Read(string path)
        {
            using var file = File.OpenRead(path);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var manifest = JsonUtility.FromJson<Manifest>(Encoding.UTF8.GetString(ReadEntry(zip, ManifestName, MaxManifestBytes)));
            if (manifest == null || manifest.version != Version || manifest.entries == null ||
                manifest.entries.Length == 0 || manifest.entries.Length > MaxTargets || manifest.images == null ||
                manifest.images.Length > MaxImages || zip.Entries.Count > MaxImages + 1 ||
                zip.Entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
                manifest.entries.Any(entry => entry?.layers == null || entry.layers.Length == 0 || entry.layers.Length > MaxLayers) ||
                manifest.images.Any(image => image == null || image.id < 1 || image.id > MaxImages) ||
                manifest.images.Select(image => image.id).Distinct().Count() != manifest.images.Length)
                throw new InvalidDataException("Unsupported or invalid styling pack.");
            return manifest;
        }

        internal static Edit ReadEdit(string path, Manifest manifest, Entry entry, bool layoutMatches) =>
            ReadEditWithinBudget(path, manifest, entry, layoutMatches, MaxDecodedPixels);

        internal static Edit ReadEditWithinBudget(string path, Manifest manifest, Entry entry, bool layoutMatches, long pixelBudget)
        {
            var edit = new Edit();
            try
            {
                foreach (var _ in ReadEditSteps(path, manifest, entry, layoutMatches, edit, pixelBudget)) { }
                return edit;
            }
            catch
            {
                edit.Dispose();
                throw;
            }
        }

        internal static IEnumerable<int> ReadEditSteps(string path, Manifest manifest, Entry entry, bool layoutMatches,
                                                       Edit edit, long pixelBudget = MaxDecodedPixels)
        {
            using var file = File.OpenRead(path);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var images = manifest.images.ToDictionary(image => image.id);
            var groups = new Dictionary<string, string>(StringComparer.Ordinal);
            long encodedBytes = 0, decodedPixels = 0;
            for (int i = 0; i < entry.layers.Length; i++)
            {
                DecodeRecord(entry.layers[i]);
                yield return i + 1;
            }

            void DecodeRecord(LayerData record)
            {
                var layer = DecodeLayer(record);
                if (layer == null)
                {
                    edit.Issues.Add((record?.kind, LayerIssue.Unreadable, null));
                    return;
                }
                layer.id = Guid.NewGuid().ToString("N");
                if (!string.IsNullOrEmpty(layer.groupId))
                {
                    if (!groups.TryGetValue(layer.groupId, out string id)) groups[layer.groupId] = id = Guid.NewGuid().ToString("N");
                    layer.groupId = id;
                }
                if (!layoutMatches && NeedsLayout(layer)) Disable(layer, LayerIssue.LayoutChanged, null);
                if (layer is ImageLayer image)
                {
                    image.image = GetImage(record.image);
                    image.mask = GetImage(record.mask);
                    if (image.image == null)
                    {
                        image.missingPackImage = MissingArtworkName(record.image, layer);
                        Disable(layer, LayerIssue.ImageUnavailable, record.image);
                    }
                    if (record.mask != null && record.mask.state != ArtworkState.None && image.mask == null)
                    {
                        image.missingPackMask = MissingArtworkName(record.mask, layer);
                        Disable(layer, LayerIssue.MaskUnavailable, record.mask);
                    }
                }
                edit.Layers.Add(layer);
            }

            void Disable(Layer layer, LayerIssue issue, Artwork artwork)
            {
                layer.enabled = false;
                edit.Issues.Add((layer.name, issue, artwork));
            }

            Texture2D GetImage(Artwork art)
            {
                if (art == null || art.state != ArtworkState.Included || !images.TryGetValue(art.id, out var data)) return null;
                if (edit.Textures.TryGetValue(art.id, out var texture)) return texture;
                var resource = zip.GetEntry(ImagePath(art.id));
                long pixels = (long)data.width * data.height;
                if (resource != null && resource.Length > 0 && resource.Length <= MaxImageBytes &&
                    data.width > 0 && data.height > 0 && data.width <= SystemInfo.maxTextureSize && data.height <= SystemInfo.maxTextureSize &&
                    encodedBytes + resource.Length <= MaxArtBytes && decodedPixels + pixels <= pixelBudget)
                {
                    encodedBytes += resource.Length;
                    texture = DecodeImage(zip, data);
                    if (texture != null) decodedPixels += pixels;
                }
                edit.Textures.Add(art.id, texture);
                return texture;
            }
        }

        static Layer DecodeLayer(LayerData record)
        {
            if (record?.kind == null || string.IsNullOrEmpty(record.data) || !LayerTypes.TryGetValue(record.kind, out var type)) return null;
            try
            {
                var layer = (Layer)Activator.CreateInstance(type);
                JsonUtility.FromJsonOverwrite(record.data, layer);
                ClearProjectReferences(layer);
                if (!float.IsFinite(layer.opacity) || !Enum.IsDefined(typeof(MaskMode), layer.maskMode) ||
                    !ValidateStrokes(layer.maskStrokes) || layer is PaintLayer paint && !ValidateStrokes(paint.strokes)) return null;
                return layer;
            }
            catch (ArgumentException) { return null; }
        }

        static bool ValidateStrokes(List<PaintStroke> strokes)
        {
            if (strokes == null) return false;
            foreach (var stroke in strokes)
            {
                if (stroke == null || !float.IsFinite(stroke.radius) || !float.IsFinite(stroke.surfRadius) ||
                    !float.IsFinite(stroke.fixedDotRadius) || stroke.fixedDotRadius < 0f || stroke.fixedDotRadius > BrushShape.MaxDotSize * 0.5f ||
                    !float.IsFinite(stroke.opacity) || stroke.bristleModel < 0 || stroke.bristleModel > BrushRenderer.CoherentBristleModel ||
                    stroke.pressureModel < 0 || stroke.pressureModel > 1 ||
                    stroke.pressureModel == 1 && (stroke.pen == null || !stroke.pen.Valid) ||
                    !ValidatePoints(stroke.points) || !ValidateTipAngles(stroke.tipAngles, stroke.points)) return false;
                if (stroke.IsSurface && (stroke.surfRadius < 0.00001f || !ValidateSurfacePoints(stroke.surf))) return false;
                var shape = stroke.shape;
                if (shape != null && (!Enum.IsDefined(typeof(BrushTip), shape.tip) ||
                    !float.IsFinite(shape.angleJitter) || shape.angleJitter < 0 || shape.angleJitter > 1 ||
                    shape.jitterVersion < 0 || shape.jitterVersion > 1 ||
                    shape.tip == BrushTip.Bristle && !Enum.IsDefined(typeof(BristleLoad), shape.bristleLoad) ||
                    shape.tip == BrushTip.Stamp && !BrushStampLibrary.IsValid(shape.alphaId, shape.alphaVersion))) return false;
                if (shape != null && (!float.IsFinite(shape.spacing) || shape.spacing <= 0f ||
                    (shape.tip == BrushTip.Bristle || shape.tip == BrushTip.Spray) && (shape.density < 1 || shape.density > 48))) return false;
                if (stroke.mirrorPaths == null) continue;
                foreach (var path in stroke.mirrorPaths)
                    if (path == null || !float.IsFinite(path.radius) || !ValidatePoints(path.points) ||
                        !ValidateTipAngles(path.tipAngles, path.points) ||
                        path.surfacePoints?.Count > 0 && (stroke.surfRadius < 0.00001f || !ValidateSurfacePoints(path.surfacePoints))) return false;
            }
            return true;
        }

        static bool ValidateTipAngles(List<Vector2> angles, List<Vector4> points)
        {
            if (angles == null || angles.Count == 0) return true;
            if (angles.Count != points.Count) return false;
            foreach (var angle in angles)
                if (!float.IsFinite(angle.x) || !float.IsFinite(angle.y) || Mathf.Abs(angle.x) > 180.001f ||
                    angle.y != 0f && angle.y != 1f) return false;
            return true;
        }

        static bool ValidatePoints(List<Vector4> points)
        {
            if (points == null) return false;
            foreach (var point in points)
                if (!Finite(point) || Mathf.Abs(point.x) > 64f || Mathf.Abs(point.y) > 64f) return false;
            return true;
        }

        static bool ValidateSurfacePoints(List<Vector4> points) => points != null && points.All(point =>
            Finite(point) && point.x >= 0f && Mathf.Abs(point.y) <= 1.001f && Mathf.Abs(point.z) <= 1.001f);

        static bool Finite(Vector4 point) =>
            float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z) && float.IsFinite(point.w);

        internal static bool NeedsLayout(Layer layer)
        {
            if (layer.maskMode != MaskMode.WholeTexture || layer is DrawnGradientLayer drawn && drawn.sceneProjection ||
                layer is RootTipGradientLayer gradient && gradient.GradientModeUsed != GradientMode.UvV ||
                layer is RecolourPartLayer) return true;
            return UsesGeometry(layer.maskStrokes) || layer is PaintLayer paint && UsesGeometry(paint.strokes);
        }

        static bool UsesGeometry(List<PaintStroke> strokes) => strokes?.Any(stroke => stroke != null &&
            (stroke.clip || stroke.surface || stroke.fillRegion?.UsesGeometry == true || stroke.fillMirrorRegion?.UsesGeometry == true ||
             stroke.mirrorPaths?.Any(path => path != null && (path.surfacePoints?.Count > 0 || path.pieces?.Count > 0)) == true ||
             stroke.isFill && stroke.fillRegion == null)) == true;

        static Artwork MissingArtwork(string name) => new Artwork { name = name, state = ArtworkState.Missing };

        static string MissingArtworkName(Artwork artwork, Layer layer) =>
            !string.IsNullOrEmpty(artwork?.name) ? artwork.name : !string.IsNullOrEmpty(layer.name) ? layer.name : layer.TypeName;

        static Artwork MarkArtwork(Texture2D texture, ArtworkState state) =>
            new Artwork { name = texture != null ? texture.name : null, state = state };

        static string ImagePath(int id) => $"art/{id}.png";

        static byte[] FullSizePng(Texture2D texture, string path)
        {
            if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
                new FileInfo(path).Length <= MaxImageBytes)
            {
                byte[] source = File.ReadAllBytes(path);
                if (PngSize(source, out _, out _)) return source;
            }

            var previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
            var buffer = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Texture2D pixels = null;
            try
            {
                GL.sRGBWrite = srgb && QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(texture, buffer);
                RenderTexture.active = buffer;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, !srgb);
                pixels.ReadPixels(new Rect(0, 0, buffer.width, buffer.height), 0, 0, false);
                return pixels.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                RenderTexture.ReleaseTemporary(buffer);
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
        }

        static Texture2D DecodeImage(ZipArchive zip, ImageData data)
        {
            Texture2D texture = null;
            try
            {
                byte[] png = ReadEntry(zip, ImagePath(data.id), MaxImageBytes);
                if (!PngSize(png, out int width, out int height) || width != data.width || height != data.height ||
                    !string.Equals(Fingerprint(png), data.sha256, StringComparison.Ordinal)) return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, !data.srgb)
                {
                    name = data.name ?? "Pack artwork",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = Enum.IsDefined(typeof(FilterMode), data.filter) ? (FilterMode)data.filter : FilterMode.Bilinear,
                    wrapMode = Enum.IsDefined(typeof(TextureWrapMode), data.wrap) ? (TextureWrapMode)data.wrap : TextureWrapMode.Clamp,
                };
                if (texture.LoadImage(png, false) && texture.width == width && texture.height == height) return texture;
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is UnityException) { }
            if (texture != null) Object.DestroyImmediate(texture);
            return null;
        }

        static bool PngSize(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 ||
                png[4] != 13 || png[5] != 10 || png[6] != 26 || png[7] != 10 ||
                png[12] != 73 || png[13] != 72 || png[14] != 68 || png[15] != 82 || BigEndian(png, 8) != 13) return false;
            width = BigEndian(png, 16);
            height = BigEndian(png, 20);
            return width > 0 && height > 0 && width <= SystemInfo.maxTextureSize && height <= SystemInfo.maxTextureSize;
        }

        static int BigEndian(byte[] bytes, int at) => bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];

        static string Fingerprint(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
        {
            using var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open();
            entry.Write(bytes, 0, bytes.Length);
        }

        static byte[] ReadEntry(ZipArchive zip, string name, int limit)
        {
            var entry = zip.GetEntry(name);
            if (entry == null || entry.Length <= 0 || entry.Length > limit) throw new InvalidDataException("Missing or oversized pack entry.");
            var bytes = new byte[(int)entry.Length];
            using var stream = entry.Open();
            int filled = 0;
            while (filled < bytes.Length)
            {
                int read = stream.Read(bytes, filled, bytes.Length - filled);
                if (read == 0) throw new InvalidDataException("Incomplete pack entry.");
                filled += read;
            }
            if (stream.ReadByte() >= 0) throw new InvalidDataException("Unexpected pack entry data.");
            return bytes;
        }
    }
}
