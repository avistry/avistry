using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        PaintFillScope FillScope => _brush.fillScope == PaintFillScope.Legacy ? PaintFillScope.ClickedPiece : _brush.fillScope;
        int FillColourRule => _brush.fillWholeIsland ? 0 : _brush.fillAllMatches ? 2 : 1;
        bool HasFillSelection => HasFaceSelection || _selection.Exists(IsCurrentPiece);
        bool FillLimitedByLayer => FillDestination()?.maskMode is MaskMode.SelectedIslands or MaskMode.SelectedFaces;
        static string FillOriginalTextureHint => Loc.T("Matches colours in the original texture.");
        static string SceneFillClickHint => Loc.T("Click the edited part to fill");

        Layer FillDestination()
        {
            if (MaskEditing) return ActiveLayer;
            return ActiveLayer is PaintLayer paint && (FillScope != PaintFillScope.ClickedPiece || MatchesFaceSelection(paint))
                ? paint : null;
        }

        bool InsideFillPaintArea(Vector2 point) => _uvPaintLimit == UvPaintLimit.None ||
            InsidePaintUvLimit(point) && InsidePaintUvLimit(_comp.ScopedFillPixelCentre(point));

        string BasicFillCursorProblem(Vector2 point)
        {
            if (!IsInsideTexture(point)) return Loc.T("Click inside the texture to fill");
            if (!InsideFillPaintArea(point)) return Loc.T("Click inside the paint area");
            if (FillScope == PaintFillScope.Selection && !HasFillSelection) return Loc.T("Select pieces or faces first");
            return null;
        }

        string CanvasFillCursorProblem(Vector2 point)
        {
            string problem = BasicFillCursorProblem(point);
            if (problem != null) return problem;
            if (FillScope == PaintFillScope.ClickedPiece)
            {
                if (_uv == null) return Loc.T("Fill needs a UV map");
                if (!_uv.HasIslandAtUV(point)) return Loc.T("Click a piece to fill it");
            }
            else if (_uv == null && FillDestination() is Layer layer && layer.maskMode != MaskMode.WholeTexture)
                return Loc.T("Fill needs a UV map");
            return null;
        }

        string SceneFillHint()
        {
            string action = FillScope == PaintFillScope.Selection && !HasFillSelection
                ? Loc.T("Select pieces or faces first") : SceneFillClickHint;
            return Loc.F("<b>Avistry · Fill · {0}</b>   {1} · Ctrl+click picks a colour · Esc stops", FillScopeSummary(), action);
        }

        string FillScopeSummary()
        {
            bool limited = FillLimitedByLayer;
            return FillScope switch
            {
                PaintFillScope.Selection => limited && HasFillSelection
                    ? Loc.T("Selection within layer limit") : Loc.T("Current selection"),
                PaintFillScope.WholeTexture => limited ? Loc.T("Within layer limit") : Loc.T("Whole texture"),
                _ => limited ? Loc.T("Piece within layer limit") : Loc.T("Clicked piece"),
            };
        }

        static void DrawSceneFillMarker(Vector3 point, Color colour)
        {
            var centre = (Vector3)HandleUtility.WorldToGUIPoint(point);
            Handles.BeginGUI();
            try
            {
                using var scope = new Handles.DrawingScope(Color.black);
                Handles.DrawWireDisc(centre, Vector3.forward, 7f, 2f);
                Handles.color = colour;
                Handles.DrawWireDisc(centre, Vector3.forward, 6f);
            }
            finally { Handles.EndGUI(); }
        }

        string PlanFill(Vector2 point, int hitPiece, int hitFace, out PaintFillRegion region)
        {
            region = null;
            var scope = FillScope;
            if (scope == PaintFillScope.ClickedPiece)
            {
                string problem = FillPlacementProblem(point, hitPiece);
                if (problem != null) return problem;
            }
            else if (!IsInsideTexture(point)) return Loc.T("The fill point is outside the open texture.");
            if (!InsideFillPaintArea(point))
                return Loc.T("Click inside the paint area shown on the texture.");

            var layer = FillDestination();
            bool limitToCurrent = scope == PaintFillScope.Selection || scope == PaintFillScope.ClickedPiece && layer == null;
            var pickedFaces = limitToCurrent && HasFaceSelection ? _selectedFaces : null;
            var pickedPieces = limitToCurrent && pickedFaces == null ? _selection.Where(IsCurrentPiece).Distinct().ToList() : null;
            if (pickedPieces?.Count == 0) pickedPieces = null;
            if (scope == PaintFillScope.Selection && pickedPieces == null && pickedFaces == null)
                return Loc.T("Select pieces or faces before filling the selection.");

            if (scope == PaintFillScope.ClickedPiece)
            {
                int clicked = _comp.ScopedFillSeedPiece(_uv, point);
                pickedPieces = pickedPieces == null || pickedPieces.Contains(clicked) ? new List<int> { clicked } : new List<int>();
            }

            var layerPieces = layer?.maskMode == MaskMode.SelectedIslands ? layer.islands : null;
            var layerFaces = layer?.maskMode == MaskMode.SelectedFaces ? layer.faces : null;
            if (layer?.maskMode == MaskMode.SelectedFaces && !HasCurrentFaceLimit(layer))
                return Loc.T("This face limit does not match the current UV mesh. Pick new faces or choose Everything.");
            if (_uv == null && (layerPieces != null || layerFaces != null))
                return Loc.T("This layer's limit needs a UV map. Pick a part with UVs or choose Everything for the layer.");

            var plan = new PaintFillRegion { scope = scope, layout = _uv?.FaceLayout };
            plan.limitPieces = pickedPieces != null || layerPieces != null;
            plan.limitFaces = pickedFaces != null || layerFaces != null;
            plan.pieces = FillLimitIntersection(pickedPieces, layerPieces, layer?.invertMask == true, out plan.invertPieces);
            plan.faces = FillLimitIntersection(pickedFaces, layerFaces, layer?.invertMask == true, out plan.invertFaces);
            if (plan.UsesGeometry)
            {
                bool AllowsTriangle(int face) => plan.AllowsFace(face) && plan.AllowsPiece(face < 0 ? -1 : _uv.triIsland[face]);
                bool AllowsTexturePoint(Vector2 uv)
                {
                    var faces = _uv.TrianglesAtUV(uv);
                    return faces.Count == 0 ? AllowsTriangle(-1) : faces.Any(AllowsTriangle);
                }

                bool inLimit = IsCurrentFace(hitFace) ? AllowsTriangle(hitFace) : AllowsTexturePoint(point);
                if (!inLimit && plan.limitFaces) return Loc.T("Click inside the layer's UV face limit to fill.");
                if (!inLimit) return Loc.T("Click inside the selected area and the layer's limit to fill.");

                int seedPiece = _comp.ScopedFillSeedPiece(_uv, point);
                if (!plan.AllowsPiece(seedPiece) || plan.limitFaces && !AllowsTexturePoint(_comp.ScopedFillPixelCentre(point)))
                    return Loc.T("No fill pixel on this piece. Click farther inside or raise Preview size in Advanced Pick settings.");
            }

            region = plan;
            return null;
        }

        static List<int> FillLimitIntersection(List<int> selected, List<int> layer, bool layerInverted, out bool inverted)
        {
            inverted = selected == null && layer != null && layerInverted;
            if (selected == null) return layer != null ? new List<int>(layer) : new List<int>();
            if (layer == null) return new List<int>(selected);
            var ids = new HashSet<int>(layer);
            return selected.Where(id => ids.Contains(id) != layerInverted).ToList();
        }

        static GUIContent[] FillScopeChoices() => new[]
        {
            new GUIContent(Loc.T("Clicked piece"), Loc.T("Stay inside the UV piece under the click.")),
            new GUIContent(Loc.T("Current selection"), Loc.T("Limit the fill to the pieces or faces selected in Limit.")),
            new GUIContent(Loc.T("Whole texture"), Loc.T("Allow the fill anywhere on the texture within the layer's limit.")),
        };

        static GUIContent[] FillRuleChoices(bool compact = false) => new[]
        {
            new GUIContent(Loc.T("All colours"), Loc.T("Fill the chosen area whatever colour it has.")),
            new GUIContent(compact ? Loc.T("Connected") : Loc.T("Connected similar colour"),
                Loc.T("Fill connected pixels close to the clicked original colour.")),
            new GUIContent(compact ? Loc.T("All similar") : Loc.T("All similar colours"),
                Loc.T("Fill matching pixels throughout the scope, even in separate areas.")),
        };

        static GUIContent FillToleranceLabel() => new GUIContent(Loc.T("Tolerance"),
            Loc.T("How far red, green, blue or transparency can differ from the clicked pixel."));

        static GUIContent FillCloseGapsLabel() => new GUIContent(Loc.T("Close gaps (px)"),
            Loc.T("Close breaks up to this many source pixels. Small gaps may need a larger preview."));

        static GUIContent FillAreaScalingLabel() => new GUIContent(Loc.T("Area scaling (px)"),
            Loc.T("Grow the fill within its scope to cover seams."));

        static readonly int[] FillChoiceIndices = { 0, 1, 2 };

        static int FillChoice(string label, int current, GUIContent[] choices, float width = -1f) => SteppedChoice(new GUIContent(label), current,
            FillChoiceIndices, (rect, value) => EditorGUI.Popup(rect, value, choices), width);

        bool PickFillSelectionInPaint => !MaskEditing && _uv != null;
        string FillSelectionAction => PickFillSelectionInPaint ? Loc.T("Pick for paint") : Loc.T("Go to Limit");

        void PickFillSelection()
        {
            if (PickFillSelectionInPaint) SetPickForPaint(true);
            else SetTab(Tab.Select);
        }

        void DrawFillSettings()
        {
            bool hides = _maskFillHides;
            if (MaskEditing)
            {
                GUILayout.Label(new GUIContent(Loc.T("On the mask"), Loc.T("What a fill does to the layer mask")), EditorStyles.miniLabel);
                hides = ToolPalette(hides ? 1 : 0, MaskFillGlyphs, PaintLabels.maskFill, PanelContentWidth, scene: false) == 1;
                StudioGUI.Count("paint.maskFill", GUILayoutUtility.GetLastRect());
            }

            var scope = (PaintFillScope)(1 + FillChoice(Loc.T("Fill scope"), (int)FillScope - 1, FillScopeChoices()));
            if (FillLimitedByLayer && (FillScope != PaintFillScope.Selection || HasFillSelection))
                GUILayout.Label(FillScopeSummary(), StudioGUI.Hint);
            int rule = FillColourRule;
            InfoRow("paint.fillKind", "island", () => rule = FillChoice(Loc.T("Fill colours"), rule,
                FillRuleChoices(PanelContentWidth < 360f)));
            float tolerance = _brush.fillTolerance, grow = _brush.fillAreaScaling;
            int gap = _brush.fillGap;
            if (rule != 0)
            {
                GUILayout.Label(FillOriginalTextureHint, StudioGUI.Hint);
                InfoRow("paint.colourRange", "layer", () => tolerance = EditorGUILayout.Slider(FillToleranceLabel(), tolerance, 0f, 1f));
                if (rule == 1)
                    InfoRow("paint.fillCloseGaps", "layer", () => gap = EditorGUILayout.IntSlider(FillCloseGapsLabel(), gap, 0, BrushSettings.MaxFillGap));
                InfoRow("paint.fillGrow", "seam", () => grow = EditorGUILayout.Slider(FillAreaScalingLabel(), grow, 0f, 16f));
            }
            if (scope == PaintFillScope.Selection && !HasFillSelection)
            {
                GUILayout.Label(Loc.T("Select pieces or faces before filling the selection."), StudioGUI.Hint);
                if (GUILayout.Button(FillSelectionAction, EditorStyles.miniButton)) EditorApplication.delayCall += () => { if (this != null) PickFillSelection(); };
            }
            if (scope == FillScope && rule == FillColourRule && tolerance == _brush.fillTolerance && grow == _brush.fillAreaScaling &&
                gap == _brush.fillGap && hides == _maskFillHides) return;
            Undo.RecordObject(this, "Change fill settings");
            _maskFillHides = hides;
            StoreFillSettings(scope, rule, tolerance, grow, gap);
        }

        void DrawSceneFillSettings(float width, ref Action change)
        {
            var choices = FillScopeChoices();
            bool compact = width < StackedChoiceWidth;
            if (compact)
            {
                choices[0].text = Loc.T("This piece");
                choices[1].text = Loc.T("Selection");
            }
            var scope = (PaintFillScope)(1 + FillChoice(Loc.T("Fill scope"), (int)FillScope - 1, choices, width));
            int rule = FillChoice(Loc.T("Fill colours"), FillColourRule, FillRuleChoices(compact: true), width);
            float tolerance = _brush.fillTolerance;
            int gap = _brush.fillGap;
            float grow = _brush.fillAreaScaling;
            if (rule != 0)
            {
                GUILayout.Label(FillOriginalTextureHint, StudioGUI.Hint, GUILayout.MaxWidth(width));
                var toleranceLabel = SceneSliderLabel(FillToleranceLabel(), width);
                tolerance = EditorGUI.Slider(SceneBarRow(width), toleranceLabel, tolerance, 0f, 1f);
                if (rule == 1)
                {
                    var gapLabel = SceneSliderLabel(FillCloseGapsLabel(), width);
                    var gapRect = SceneBarRow(width);
                    gap = EditorGUI.IntSlider(gapRect, gapLabel, gap, 0, BrushSettings.MaxFillGap);
                    StudioGUI.Count("scene.fillCloseGaps", gapRect);
                }
                var growLabel = SceneSliderLabel(FillAreaScalingLabel(), width);
                var rect = SceneBarRow(width);
                grow = EditorGUI.Slider(rect, growLabel, grow, 0f, 16f);
                StudioGUI.Count("scene.fillGrow", rect);
            }
            if (scope != FillScope || rule != FillColourRule)
                change = () => ChangeSceneBar("Change fill settings", () => StoreFillSettings(scope, rule, tolerance, grow, gap));
            else if (tolerance != _brush.fillTolerance || gap != _brush.fillGap || grow != _brush.fillAreaScaling)
                change = () =>
                {
                    Undo.RecordObject(this, "Change fill settings");
                    _brush.fillTolerance = tolerance;
                    _brush.fillGap = gap;
                    _brush.fillAreaScaling = grow;
                    SelectUiChanged();
                };
        }

        void StoreFillSettings(PaintFillScope scope, int rule, float tolerance, float grow, int gap)
        {
            _brush.fillScope = scope;
            _brush.fillWholeIsland = rule == 0;
            _brush.fillAllMatches = rule == 2;
            _brush.fillTolerance = tolerance;
            _brush.fillAreaScaling = grow;
            _brush.fillGap = gap;
            SelectUiChanged();
        }
    }
}
