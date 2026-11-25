using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        EditorPixelJob _pixelJob;

        Texture2D MakeNormalMap() => RunNormalBake(false);

        Texture2D RunNormalBake(bool background)
        {
            if (_pixelJob != null || _aoSteps != null) return null;
            var slot = CurrentSlot;
            if (slot == null || _uv == null || _comp == null) { Toast(Loc.T("Pick a part and its texture first (1 · Pick)."), MessageType.Warning); return null; }
            if (slot.role != TextureRole.Color) { Toast(Loc.T("A normal map is made from a colour texture. Pick the main texture in 1 · Pick."), MessageType.Warning); return null; }
            string prop = MapBaker.NormalProperty(_material);
            var current = prop != null ? _material.GetTexture(prop) : null;
            var baseNormal = _nrm.combine ? NormalToCombineWith(current, out _) : null;
            var target = CaptureMapTarget(slot);
            var shader = target.material.shader;
            if (!CanRunMapAction(target, slot.texture, shader)) { MapTargetChanged(); return null; }

            var t0 = EditorApplication.timeSinceStartup;
            var preview = _comp;
            var recipe = _recipe;
            int revision = _historyRevision;
            var source = slot.texture;
            var sourceHash = source.imageContentsHash;
            var baseHash = baseNormal != null ? baseNormal.imageContentsHash : default;
            var settings = new MapBaker.NormalSettings { strength = _nrm.strength, smoothness = _nrm.smoothness,
                combine = _nrm.combine, withLayers = _nrm.withLayers, darkRaised = _nrm.darkRaised };
            try
            {
                var raster = _uv.CopyIslandRasterInput();
                int w = 0, h = 0;
                Color32[] existing = null;
                Task<Color32[]> Capture(CancellationToken token)
                {
                    using var comp = new Compositor(slot, 0);
                    comp.Compose(settings.withLayers ? recipe.layers.Where(l => !IsAoLayer(l)).ToList() : new List<Layer>(), _uv);
                    w = comp.Width;
                    h = comp.Height;
                    if (baseNormal != null) existing = MapBaker.ReadNormalMap(baseNormal, w, h);
                    if (background) return comp.ReadResultPixelsAsync(false, token);
                    var tex = comp.ReadResult(false);
                    try { return Task.FromResult(tex.GetPixels32()); }
                    finally { DestroyImmediate(tex); }
                }
                byte[] Calculate(Color32[] pixels, CancellationToken token, Func<float, bool> progress)
                {
                    var ids = raster.RasterIslandIdMap(w, h, 0, token);
                    var output = MapBaker.BuildNormalPixels(pixels, w, h, ids, settings, existing, progress, token);
                    token.ThrowIfCancellationRequested();
                    return output == null ? null : MapPixels.EncodeRgba(output, w, h);
                }

                Texture2D Finish(byte[] png)
                {
                    if (!CanRunMapAction(target, source, shader) || _comp != preview || _recipe != recipe ||
                        settings.withLayers && (revision != _historyRevision || _liveStroke != null || _sceneStrokeDraft != null ||
                            _gradDrag != null || _imgDrag != null || _sceneGradientDraft != null) ||
                        source.imageContentsHash != sourceHash || baseNormal != null && baseNormal.imageContentsHash != baseHash ||
                        prop != null && target.material.GetTexture(prop) != current)
                    {
                        MapTargetChanged();
                        return null;
                    }
                    var made = MapBaker.SaveEncodedMap(source, "normal", MapBaker.NormalTag, png, w, h,
                        out _, baseNormal, MapTargetKey(target));
                    if (made == null) throw new InvalidOperationException(Loc.T("Unity couldn't import the normal map."));
                    Undo.RecordObject(this, "Make normal map");
                    _madeNormal = made;
                    _madeNormalSource = slot.texture;
                    _madeNormalTarget = target;
                    Learn.Did(LearnEvents.MapsNormal);
                    double secs = EditorApplication.timeSinceStartup - t0;
                    bool inUse = target.material != null && NormalMapInUse(target.material, current, made, slot.uvChannel);
                    Toast(inUse
                            ? Loc.F("Updated ‘{0}’, which the material already uses ({1:0.0} s).", _madeNormal.name, secs)
                            : Loc.F("Saved ‘{0}’ ({1} px, {2:0.0} s){3}. The material doesn't use it yet.", _madeNormal.name, w, secs, (baseNormal != null ? $", combined with ‘{baseNormal.name}’" : "")),
                        MessageType.Info, 7f, inUse || prop == null ? null : Loc.T("Apply to material"),
                        inUse || prop == null ? null : BoundMapAction(target, slot.texture, shader, () => UseMadeNormalMap(made)));
                    EditorGUIUtility.PingObject(made);
                    return made;
                }
                if (!background)
                {
                    var pixels = Capture(default).GetAwaiter().GetResult();
                    var png = Calculate(pixels, default, f => EditorUtility.DisplayCancelableProgressBar(Loc.T("Normal map"),
                        Loc.T("Making the bumps…"), f));
                    return png == null ? null : Finish(png);
                }
                _pixelJob = new EditorPixelJob(Loc.T("Normal map"), Capture,
                    Calculate, png => Finish(png), NormalBakeError,
                    () => Toast(Loc.T("Normal map cancelled. Nothing was changed."), MessageType.Info, 3f),
                    () => { _pixelJob = null; _nrmCacheValid = false; }, Repaint);
            }
            catch (Exception error)
            {
                NormalBakeError(error);
            }
            finally
            {
                if (!background) EditorUtility.ClearProgressBar();
                _nrmCacheValid = false;
                Repaint();
            }
            return null;
        }

        void NormalBakeError(Exception error)
        {
            Debug.LogException(error);
            Toast(Loc.F("Couldn't make the normal map: {0}", error.Message), MessageType.Warning, 8f);
        }
    }
}
