using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        enum EditorActivity { Ready, Importing, Compiling }
        EditorActivity _editorActivity, _drawEditorActivity;
        MeshUV _preparedSurfaceMesh, _preparedBrushJoins, _surfacePreparationTarget;
        int _preparedSurfacePose;
        double _surfacePreparationCheckAfter, _surfacePreparationAfter;
        PreparationPacing _preparationPacing;

        struct PreparationPacing
        {
            double _lastUpdateEnd, _lastStage, _yieldUntil;
            int _slowUpdates;
            internal bool Busy => _slowUpdates >= 2;

            internal void Observe(double now)
            {
                double interval = _lastUpdateEnd > 0 ? now - _lastUpdateEnd : 0;
                _lastUpdateEnd = now;
                if (interval >= 1 || interval <= 0) _slowUpdates = 0;
                else if (interval >= .05) _slowUpdates = System.Math.Min(3, _slowUpdates + 1);
                else if (interval < .035) _slowUpdates = System.Math.Max(0, _slowUpdates - 1);
            }

            internal bool CanRun(double now) => now >= _yieldUntil && (!Busy || now - _lastStage >= .18);

            internal void Finish(double start, double end)
            {
                _lastUpdateEnd = end;
                _lastStage = start;
                double cost = end - start;
                if (cost >= .004) _yieldUntil = end + System.Math.Min(.15, cost * 3);
            }
        }

        bool WaitingForPreview => _renderer != null && _comp == null && _needsRebuild && _drawEditorActivity != EditorActivity.Ready;

        static string PreviewWaitText => Loc.T("Avistry will build the texture preview when Unity is ready.");

        void ObserveEditorActivity()
        {
            SetEditorActivity(EditorApplication.isCompiling, EditorApplication.isUpdating);
            ReleaseClosedSceneGradientView();
            FinishClosedSceneStroke();
            RestoreCancelledSceneStrokePreview();
            double now = EditorApplication.timeSinceStartup;
            _preparationPacing.Observe(now);
            if (_comp == null) return;
            bool idle = _editorActivity == EditorActivity.Ready && _pixelJob == null && _aoSteps == null && !_needsRebuild &&
                _liveStroke == null && _sceneStrokeDraft == null && !_panning && !_marqueeActive &&
                _gradDrag == null && _imgDrag == null && _sceneGradientDraft == null;
            if (!idle)
            {
                _comp.SuspendPreparation();
                return;
            }
            if (!ReferenceEquals(_surfacePreparationTarget, _uv))
            {
                _surfacePreparationTarget = _uv;
                _surfacePreparationAfter = now + .075;
            }
            var demand = CurrentPreparationDemand(out bool surface);
            _comp.LimitBackgroundRaster = _preparationPacing.Busy;
            if (!_preparationPacing.CanRun(now))
            {
                _comp.RequestPreparation(_uv, demand);
                return;
            }
            try { PrepareNextInteraction(demand, surface, now); }
            finally { _preparationPacing.Finish(now, EditorApplication.timeSinceStartup); }
        }

        void PrepareNextInteraction(Compositor.PreparationDemand demand, bool surface, double now)
        {
            if (_comp.ContinuePreparation(_uv, demand, now)) return;
            if ((_tab == Tab.Select || PickingForPaint) && _selectMode == SelectMode.Wand &&
                _comp.TryPreparedExactIslands(_uv, out var exact) && EnsureWandGeometry(exact, _comp.Width, _comp.Height)) return;
            if (now < _surfacePreparationAfter) return;
            bool painting = _tab == Tab.Paint && _tool == Tool.Paint && !PickingForPaint && CurrentSlot?.role != TextureRole.Normal;
            if (painting && !GradientToolActive && !BlendToolActive && !_brush.fillTool && _comp.CanPrepareIslandMaps &&
                _comp.ContinuePaintGpuPreparation(surface, _brush.shape?.buildUp == true)) return;
            if (!surface || _uv == null) return;
            if ((painting || !ReferenceEquals(_preparedSurfaceMesh, _uv)) && now >= _surfacePreparationCheckAfter)
            {
                _surfacePreparationCheckAfter = now + .075;
                if (_uv.PoseChanged()) { _uv.RefreshPose(); return; }
            }
            if (!ReferenceEquals(_preparedSurfaceMesh, _uv) || _preparedSurfacePose != _uv.PoseVersion)
            {
                _uv.SurfaceMesh();
                _preparedSurfaceMesh = _uv;
                _preparedSurfacePose = _uv.PoseVersion;
                return;
            }
            if (_brush.stayInsideIslands && !ReferenceEquals(_preparedBrushJoins, _uv))
            {
                _uv.PrepareBrushJoins();
                _preparedBrushJoins = _uv;
            }
        }

        Compositor.PreparationDemand PaintPreparationDemand(bool fromScene, out bool surface)
        {
            var demand = Compositor.PreparationDemand.None;
            surface = fromScene && _brush.surfaceBrush && !_brush.fillTool && _brush.shape?.SurfaceCapable == true &&
                (!BlendToolActive || _brush.blendKind != StrokeTool.Smudge);
            if (_brush.fillTool)
            {
                bool geometry = FillScope != PaintFillScope.WholeTexture || FillLimitedByLayer;
                if (geometry) demand |= Compositor.PreparationDemand.ExactIslands;
                if (!_brush.fillWholeIsland)
                {
                    demand |= Compositor.PreparationDemand.Source;
                    if (geometry && _brush.fillAreaScaling > 0f) demand |= Compositor.PreparationDemand.PaddedIslands;
                }
            }
            else if (_brush.stayInsideIslands)
                demand |= Compositor.PreparationDemand.PaddedIslands | Compositor.PreparationDemand.IslandTexture;
            return demand;
        }

        bool CanPreparePickedBrush(TextureSlot slot) =>
            slot != null && slot.role != TextureRole.Normal && (_tab == Tab.Target || _tab == Tab.Select || _tab == Tab.Paint) &&
            _brush.stayInsideIslands && !_brush.fillTool && !_brush.gradientTool && !PickingForPaint &&
            !(_tab == Tab.Select && _selectMode == SelectMode.Wand);

        Compositor.PreparationDemand CurrentPreparationDemand(out bool surface)
        {
            surface = false;
            if (CurrentSlot?.role == TextureRole.Normal) return Compositor.PreparationDemand.None;
            if ((_tab == Tab.Select || PickingForPaint) && _selectMode == SelectMode.Wand)
                return Compositor.PreparationDemand.ExactIslands | Compositor.PreparationDemand.Source |
                    (WandMasksLayer ? Compositor.PreparationDemand.PaddedIslands : Compositor.PreparationDemand.None);
            if (_tab == Tab.Paint && _tool == Tool.Paint && !GradientToolActive && !PickingForPaint)
                return PaintPreparationDemand(ScenePainting, out surface);
            if ((_tab == Tab.Target || _tab == Tab.Select) && _uv != null && CanPreparePickedBrush(CurrentSlot) && _comp.CanPrepareIslandMaps)
            {
                surface = _paintInScene && _brush.surfaceBrush && !_brush.blendTool && _brush.shape?.SurfaceCapable == true;
                var demand = Compositor.PreparationDemand.PaddedIslands;
                if (SystemInfo.SupportsTextureFormat(TextureFormat.R16)) demand |= Compositor.PreparationDemand.IslandTexture;
                return demand;
            }
            return Compositor.PreparationDemand.None;
        }

        void RequestPaintPreparation(bool fromScene)
        {
            if (_comp != null && !_needsRebuild) _comp.RequestPreparation(_uv, PaintPreparationDemand(fromScene, out _));
        }

        void ClearPreparedInteraction()
        {
            _preparedSurfaceMesh = _preparedBrushJoins = _surfacePreparationTarget = null;
            _preparedSurfacePose = 0;
            _surfacePreparationCheckAfter = _surfacePreparationAfter = 0;
        }

        void SetEditorActivity(bool compiling, bool importing)
        {
            var activity = compiling ? EditorActivity.Compiling : importing ? EditorActivity.Importing : EditorActivity.Ready;
            if (activity == _editorActivity) return;
            _editorActivity = activity;
            Repaint();
        }

        bool DrawEditorActivity(Rect status)
        {
            if (_drawEditorActivity == EditorActivity.Ready) return false;
            string label = _drawEditorActivity == EditorActivity.Compiling
                ? Loc.T("Unity is compiling scripts…") : Loc.T("Unity is importing assets…");
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(status.x, status.y + 1f, 3f, status.height - 1f), StudioGUI.InfoBlue);
            var text = new Rect(status.x + 4f, status.y, status.width - 4f, status.height);
            GUI.Label(text, new GUIContent(label, WaitingForPreview ? PreviewWaitText : label), StudioGUI.Status);
            StudioGUI.Count("canvas.editorActivity", text);
            return true;
        }
    }
}
