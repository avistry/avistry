using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        IEnumerator<float> _aoSteps;
        CancellationTokenSource _aoCancel;
        Func<bool> _aoStillCurrent;
        int _aoProgressId = -1;
        int _aoPercent;

        void BeginOcclusionBake()
        {
            if (_aoSteps != null || _pixelJob != null) return;
            var slot = CurrentSlot;
            if (slot?.role != TextureRole.Color || _uv == null || _comp == null) { MapTargetChanged(); return; }
            var target = CaptureMapTarget(slot);
            var source = slot.texture;
            var shader = _material.shader;
            var preview = _comp;
            var recipe = _recipe;
            var size = MapSize(_ao.size, preview.SourceWidth, preview.SourceHeight, AoMaxSize, out bool capped);
            var settings = new MapBaker.AoSettings { samples = _ao.samples, size = _ao.size, otherMeshes = _ao.otherMeshes,
                spread = _ao.spread, strength = _ao.strength };
            _aoCancel = new CancellationTokenSource();
            _aoStillCurrent = () => this != null && preview == _comp && !_needsRebuild && _recipe == recipe;
            bool CanSave()
            {
                if (_aoStillCurrent() && CanRunMapAction(target, source, shader)) return true;
                MapTargetChanged();
                return false;
            }
            _aoSteps = MapBaker.OcclusionSteps(_uv, source, size.x, size.y, settings, true, _aoCancel.Token, CanSave,
                made => { if (CanSave()) ApplyOcclusionResult(made, $"{size.x}×{size.y}", capped); });
            try
            {
                _aoProgressId = Progress.Start(Loc.T("Ambient occlusion"));
                var cancel = _aoCancel;
                Progress.RegisterCancelCallback(_aoProgressId, () => { cancel.Cancel(); return true; });
                _aoPercent = -1;
                EditorApplication.update += TickOcclusionBake;
                Repaint();
            }
            catch
            {
                StopOcclusionBake();
                throw;
            }
        }

        void TickOcclusionBake()
        {
            try
            {
                if (_aoCancel.IsCancellationRequested || !_aoStillCurrent())
                {
                    bool cancelled = _aoCancel.IsCancellationRequested;
                    StopOcclusionBake();
                    if (cancelled) Toast(Msg.M19SoftShadow(true, null), MessageType.Info, 3f);
                    else MapTargetChanged();
                    return;
                }
                if (_liveStroke != null || _sceneStrokeDraft != null || _marqueeActive || _panning ||
                    _gradDrag != null || _imgDrag != null || _sceneGradientDraft != null || EditorApplication.isCompiling ||
                    EditorApplication.isUpdating) return;
                int steps = _preparationPacing.Busy ? 1 : 4;
                double until = EditorApplication.timeSinceStartup + 0.003;
                for (int i = 0; i < steps; i++)
                {
                    if (!MapBaker.StepOcclusion(_aoSteps)) { StopOcclusionBake(); Repaint(); return; }
                    if (_aoSteps.Current >= 0.88f || EditorApplication.timeSinceStartup >= until) break;
                }
                int percent = Mathf.RoundToInt(100 * _aoSteps.Current);
                if (_aoPercent == percent) return;
                _aoPercent = percent;
                Progress.Report(_aoProgressId, _aoSteps.Current);
                Repaint();
            }
            catch (Exception error)
            {
                StopOcclusionBake();
                Debug.LogException(error);
                Toast(Msg.M19SoftShadow(false, error.Message), MessageType.Warning, 8f);
            }
        }

        void StopOcclusionBake()
        {
            EditorApplication.update -= TickOcclusionBake;
            _aoCancel?.Cancel();
            _aoSteps?.Dispose();
            _aoCancel?.Dispose();
            _aoCancel = null;
            _aoSteps = null;
            _aoStillCurrent = null;
            if (_aoProgressId >= 0 && Progress.Exists(_aoProgressId)) Progress.Remove(_aoProgressId);
            _aoProgressId = -1;
        }
    }
}
