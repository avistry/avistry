using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Avistry.Textures
{
    public sealed partial class BrushRenderer
    {
        static readonly BrushShape DefaultDabShape = new BrushShape();

        struct DabCursor
        {
            internal int Count, Site;
            internal float Carry;
            internal Vector2 Direction;
        }

        readonly List<Vector4> _liveCoveragePoints = new List<Vector4>();
        readonly List<Vector2> _liveCoverageAngles = new List<Vector2>();
        PaintStroke _liveCoverageStroke;
        RenderTexture _liveCoverageTarget;
        Vector2Int _liveCoverageSize;
        DabCursor _liveCoverageCursor;
        DabSettings _liveCoverageSettings;
        RenderTargetSetup _liveCoverageSetup;

        internal void InvalidateLiveCoverage()
        {
            _liveCoverageStroke = null;
            _liveCoverageTarget = null;
            _liveCoveragePoints.Clear();
            _liveCoverageAngles.Clear();
            _liveCoverageCursor = default;
        }

        internal void RenderLiveCoverage(PaintStroke stroke, RenderTexture target, float grainCellsPerUV)
        {
            var settings = new DabSettings(stroke, grainCellsPerUV);
            if (!CanContinueLiveCoverage(stroke, target, settings)) InvalidateLiveCoverage();
            var cursor = _liveCoverageCursor;
            int previous = cursor.Count;
            int pass = stroke.shape?.buildUp == true ? 2 : 0;
            try
            {
                if (previous == 0 || previous < stroke.points.Count)
                    Render(stroke, target, grainCellsPerUV, pass, ref cursor, retain: true);
                for (int i = previous; i < stroke.points.Count; i++)
                {
                    _liveCoveragePoints.Add(stroke.points[i]);
                    _liveCoverageAngles.Add(TipSample(stroke, i));
                }
            }
            catch
            {
                InvalidateLiveCoverage();
                throw;
            }
            _liveCoverageCursor = cursor;
            _liveCoverageStroke = stroke;
            _liveCoverageTarget = target;
            _liveCoverageSize = new Vector2Int(target.width, target.height);
            _liveCoverageSettings = settings;
        }

        void BindLiveCoverageTarget(RenderTexture target, bool append)
        {
            if (_liveCoverageSetup.color == null)
                _liveCoverageSetup = new RenderTargetSetup
                {
                    color = new RenderBuffer[1], colorLoad = new RenderBufferLoadAction[1],
                    colorStore = new[] { RenderBufferStoreAction.Store }, depthLoad = RenderBufferLoadAction.DontCare,
                    depthStore = RenderBufferStoreAction.DontCare, cubemapFace = CubemapFace.Unknown,
                };
            _liveCoverageSetup.color[0] = target.colorBuffer;
            _liveCoverageSetup.depth = target.depthBuffer;
            _liveCoverageSetup.colorLoad[0] = append ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare;
            Graphics.SetRenderTarget(_liveCoverageSetup);
        }

        internal bool CanContinueLiveCoverage(PaintStroke stroke, RenderTexture target, float grainCellsPerUV) =>
            CanContinueLiveCoverage(stroke, target, new DabSettings(stroke, grainCellsPerUV));

        bool CanContinueLiveCoverage(PaintStroke stroke, RenderTexture target, DabSettings settings)
        {
            int count = _liveCoverageCursor.Count;
            if (count == 0 || !ReferenceEquals(stroke, _liveCoverageStroke) || !ReferenceEquals(target, _liveCoverageTarget) ||
                !target.IsCreated() || _liveCoverageSize.x != target.width || _liveCoverageSize.y != target.height ||
                !_liveCoverageSettings.Equals(settings) || stroke.points.Count < count) return false;
            for (int i = 0; i < count; i++)
                if (!_liveCoveragePoints[i].Equals(stroke.points[i]) || !_liveCoverageAngles[i].Equals(TipSample(stroke, i))) return false;

            int last = count - 1;
            if (stroke.points.Count > count && (last == 0 || stroke.points[last].w < 0.5f) &&
                !_liveCoverageCursor.Direction.Equals(RunDirection(stroke, last, target.width, target.height))) return false;
            return true;
        }

        readonly struct DabSettings : IEquatable<DabSettings>
        {
            readonly (int seed, int bristleModel, float radius, float dotRadius, bool pressureSize, bool pressureOpacity, float grainCells) _stroke;
            readonly (BrushTip tip, int density, float hardness, float grain, float spacing, float sizeJitter, float opacityJitter) _shape;
            readonly (float scatter, float flow, bool buildUp, float angle, float roundness, bool followStroke) _placement;
            readonly (string id, int version, float angleJitter, int jitterVersion) _stamp;
            readonly BrushPenSettings.Key _pen;
            readonly BristleLoad _bristleLoad;

            internal DabSettings(PaintStroke stroke, float grainCells)
            {
                var shape = stroke.shape ?? DefaultDabShape;
                _stroke = (stroke.seed, stroke.bristleModel, stroke.radius, stroke.fixedDotRadius, stroke.pressureSize, stroke.pressureOpacity, grainCells);
                _shape = (shape.tip, shape.density, shape.hardness, shape.grain, shape.spacing, shape.sizeJitter, shape.opacityJitter);
                _placement = (shape.scatter, shape.flow, shape.buildUp, shape.angle, shape.roundness, shape.followStroke);
                _stamp = (shape.alphaId, shape.alphaVersion, shape.angleJitter, shape.jitterVersion);
                _pen = new BrushPenSettings.Key(stroke);
                _bristleLoad = shape.bristleLoad;
            }

            public bool Equals(DabSettings other) => _stroke.Equals(other._stroke) && _shape.Equals(other._shape) &&
                _placement.Equals(other._placement) && _stamp.Equals(other._stamp) && _pen.Equals(other._pen) &&
                _bristleLoad == other._bristleLoad;
        }
    }
}
