using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _foldPenControls;
        BrushPointerInput _brushPointer;
        bool _penInput, _tiltReceived, _rotationReceived;
        Vector2 _penTilt;
        float _penRotation, _penPressure;
        float _peakPenPressure;
        readonly Vector3[] _pressureCurve = new Vector3[33];
        double _penInputTime = -100;
        static readonly int[] PenDirections = { 0, 1, 2, 3 };
        string _penLanguage;
        GUIContent[] _penDirectionLabels;

        (Vector2 position, float pressure) BrushPointer(Event input, bool start)
        {
            ReadBrushPressure(input, start);
            if (_penInput)
            {
                _penTilt = input.tilt; _penRotation = input.twist;
                _tiltReceived |= float.IsFinite(_penTilt.x + _penTilt.y) && _penTilt.sqrMagnitude > .0001f;
                _rotationReceived |= float.IsFinite(_penRotation) && Mathf.Abs(_penRotation) > .0001f;
            }
            var settings = start ? _brush.pen : _liveStroke?.pen ?? _brush.pen;
            return _brushPointer.Filter(input.mousePosition, _penPressure, EditorApplication.timeSinceStartup,
                settings?.stabilisation ?? 0f, start);
        }

        void ReadBrushPressure(Event input, bool start)
        {
            _penInput = input.pointerType == PointerType.Pen;
            _penPressure = Pressure(input);
            if (!_penInput) return;
            _penInputTime = EditorApplication.timeSinceStartup;
            _peakPenPressure = start ? _penPressure : Mathf.Max(_peakPenPressure, _penPressure);
        }

        Vector2 CapturePenAngle(PaintStroke stroke, MeshUV.SurfaceHit? hit)
        {
            var input = Event.current;
            if (stroke.pressureModel != 1 || stroke.pen == null || stroke.pen.direction == BrushPenDirection.Brush ||
                input == null || input.pointerType != PointerType.Pen || !_penInput ||
                input.rawType != EventType.MouseDown && input.rawType != EventType.MouseDrag) return Vector2.zero;
            Vector2 heading;
            if (stroke.pen.direction == BrushPenDirection.PenTilt)
            {
                if (!float.IsFinite(_penTilt.x + _penTilt.y) || _penTilt.sqrMagnitude < .0001f) return Vector2.zero;
                heading = new Vector2(_penTilt.x, -_penTilt.y).normalized;
            }
            else
            {
                if (!_rotationReceived || !float.IsFinite(_penRotation)) return Vector2.zero;
                heading = new Vector2(Mathf.Cos(_penRotation), -Mathf.Sin(_penRotation));
            }
            if (hit.HasValue && _uv != null && SceneView.currentDrawingSceneView?.camera is Camera camera)
            {
                var contact = hit.Value;
                Vector3 world = camera.transform.right * heading.x + camera.transform.up * heading.y;
                var normal = contact.normal.normalized;
                world = Vector3.ProjectOnPlane(world, normal);
                if (world.sqrMagnitude < 1e-10f) return Vector2.zero;
                if (stroke.surface)
                {
                    var axis = BrushPenGeometry.FrameX(normal);
                    heading = new Vector2(Vector3.Dot(world, axis), Vector3.Dot(world, Vector3.Cross(normal, axis)));
                }
                else heading = BrushPenGeometry.WorldToTexture(_uv, contact.triangle, world, _comp?.Width ?? 1, _comp?.Height ?? 1);
            }
            if (heading.sqrMagnitude < 1e-10f) return Vector2.zero;
            return new Vector2(Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg, 1f);
        }

        Vector2 MirrorPenAngle(SymmetryMap map, Vector2 uv, int piece, MeshUV.SurfaceHit? hit, int triangle, int reflection)
        {
            int index = _liveStroke.points.Count - 1;
            var sample = BrushRenderer.TipSample(_liveStroke, index);
            if (sample.y < .5f) return Vector2.zero;
            int source = hit?.triangle ?? -1;
            if (source < 0 && !_uv.TryTriangleAtUV(uv, piece, out source)) return Vector2.zero;
            return BrushPenGeometry.Mirror(_uv, map, source, triangle, reflection, sample, _liveStroke.surface,
                hit?.backFace == true, _comp?.Width ?? 1, _comp?.Height ?? 1);
        }

        void RecordPenAngle(PaintStroke stroke, MeshUV.SurfaceHit? hit)
        {
            if (stroke.tipAngles == null) return;
            stroke.tipAngles.Add(CapturePenAngle(stroke, hit));
        }

        void DrawPenControls(float width = -1f)
        {
            bool scene = width > 0f;
            var fold = EditorGUILayout.GetControlRect(false, 22f);
            if (scene) fold.width = Mathf.Min(fold.width, width);
            bool open = StudioGUI.Foldout(fold, _foldPenControls, new GUIContent(Loc.T("Pen and pointer")));
            if (open != _foldPenControls)
            {
                Undo.RecordObject(this, Loc.T("Show pen controls")); _foldPenControls = open;
            }
            if (!open) return;
            var pen = _brush.pen ?? new BrushPenSettings();
            if (_penLanguage != Loc.Current || _penDirectionLabels == null)
            {
                _penLanguage = Loc.Current;
                _penDirectionLabels = new[] { new GUIContent(Loc.T("Fixed angle")), new GUIContent(Loc.T("Stroke direction")),
                    new GUIContent(Loc.T("Pen tilt")), new GUIContent(Loc.T("Pen rotation")) };
            }
            int current = pen.direction == BrushPenDirection.PenTilt ? 2 : pen.direction == BrushPenDirection.PenRotation ? 3 :
                _brush.shape.followStroke ? 1 : 0;
            int direction = SteppedChoice(new GUIContent(Loc.T("Tip direction")), current, PenDirections,
                (rect, value) => EditorGUI.Popup(rect, value, _penDirectionLabels), width);
            bool size = _brush.pressureSize, strength = _brush.pressureOpacity;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!scene) EditorGUILayout.PrefixLabel(Loc.T("Pen pressure"));
                size = GUILayout.Toggle(size, Loc.T("Size"), EditorStyles.miniButtonLeft);
                strength = GUILayout.Toggle(strength, Loc.T("Strength"), EditorStyles.miniButtonRight);
            }
            float Slider(string label, float value, float low, float high)
            {
                var content = new GUIContent(label);
                if (!scene) return EditorGUILayout.Slider(content, value, low, high);
                content = SceneSliderLabel(content, width);
                return EditorGUI.Slider(SceneBarRow(width), content, value, low, high);
            }
            int PercentSlider(string label, int value, int low, int high)
            {
                var content = new GUIContent(label);
                if (!scene) return EditorGUILayout.IntSlider(content, value, low, high);
                content = SceneSliderLabel(content, width);
                return EditorGUI.IntSlider(SceneBarRow(width), content, value, low, high);
            }
            int light = pen.lightPressureTrim, full = 100 - pen.firmPressureTrim;
            float feel = pen.sensitivity;
            if (size || strength)
            {
                feel = Slider(Loc.T("Pressure sensitivity"), feel, .25f, 4f);
                var test = scene ? SceneBarRow(width, 52f) : EditorGUILayout.GetControlRect(false, 52f);
                DrawPressurePad(test, pen, scene);
                if (Advanced)
                {
                    light = PercentSlider(Loc.T("Minimum input (%)"), light, 0, full - 5);
                    full = PercentSlider(Loc.T("Full output at (%)"), full, light + 5, 100);
                }
                int peak = Mathf.RoundToInt(_peakPenPressure * 100);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(peak < light + 5 || peak == full))
                        if (GUILayout.Button(peak > 0 ? Loc.F("Use peak ({0}%)", peak) : Loc.T("Use pen peak"), EditorStyles.miniButtonLeft))
                            full = peak;
                    using (new EditorGUI.DisabledScope(light == 0 && full == 100))
                        if (GUILayout.Button(Loc.T("Reset range"), EditorStyles.miniButtonRight)) { light = 0; full = 100; }
                }
            }
            float minimumSize = size && Advanced ? Slider(Loc.T("Minimum size"), pen.minimumSize, 0f, 1f) : pen.minimumSize;
            float minimumStrength = strength && Advanced ? Slider(Loc.T("Minimum strength"), pen.minimumStrength, 0f, 1f) :
                pen.minimumStrength;
            float smoothing = Slider(Loc.T("Stabilisation"), pen.stabilisation, 0f, .95f);
            if (direction == 2 && !_tiltReceived)
                GUILayout.Label(Loc.T("Tilt has not been reported yet. Stroke direction is used until the pen is tilted."), StudioGUI.Hint);
            if (direction == 3 && !_rotationReceived)
                GUILayout.Label(Loc.T("Rotation has not been reported yet. Stroke direction is used; some pens do not report rotation."), StudioGUI.Hint);
            if (feel == pen.sensitivity && light == pen.lightPressureTrim && full == 100 - pen.firmPressureTrim &&
                minimumSize == pen.minimumSize && minimumStrength == pen.minimumStrength &&
                smoothing == pen.stabilisation && size == _brush.pressureSize && strength == _brush.pressureOpacity &&
                direction == current) return;
            Undo.RecordObject(this, Loc.T("Change pen controls"));
            if (feel != pen.sensitivity || light != pen.lightPressureTrim || full != 100 - pen.firmPressureTrim)
                LearnLater(LearnEvents.PenPressure);
            _brush.pen = pen.Clone(); _brush.pen.sensitivity = feel; _brush.pen.minimumSize = minimumSize;
            _brush.pen.lightPressureTrim = light; _brush.pen.firmPressureTrim = 100 - full;
            _brush.pen.minimumStrength = minimumStrength; _brush.pen.stabilisation = smoothing;
            _brush.pen.direction = direction == 2 ? BrushPenDirection.PenTilt : direction == 3 ? BrushPenDirection.PenRotation :
                BrushPenDirection.Brush;
            _brush.shape.followStroke = direction != 0;
            _brush.pressureSize = size; _brush.pressureOpacity = strength;
            MarkBrushEdited();
        }

        void DrawPressurePad(Rect rect, BrushPenSettings pen, bool scene)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, rect);
            var type = e.GetTypeForControl(id);
            if (type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                ReadBrushPressure(e, true);
                e.Use();
            }
            else if (GUIUtility.hotControl == id)
            {
                if (type == EventType.MouseDrag) { ReadBrushPressure(e, false); e.Use(); }
                else if (type == EventType.MouseUp || type == EventType.Ignore)
                {
                    GUIUtility.hotControl = 0;
                    if (type == EventType.MouseUp) e.Use();
                }
            }
            if (type != EventType.Used && e.type == EventType.Used)
            {
                if (scene) SceneView.lastActiveSceneView?.Repaint();
                else Repaint();
            }
            if (type != EventType.Repaint) return;
            bool recent = _penInput && EditorApplication.timeSinceStartup - _penInputTime < 8;
            string label = recent || GUIUtility.hotControl == id ? Loc.F("Pressure: {0}% → {1}%", Mathf.RoundToInt(_penPressure * 100f),
                Mathf.RoundToInt(pen.Response(_penPressure) * 100f)) : Loc.T("Test pen pressure here");
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            var chart = new Rect(rect.x + 7, rect.y + 20, Mathf.Max(1, rect.width - 14), rect.height - 26);
            GUI.Label(new Rect(chart.x, rect.y + 1, chart.width, 18), new GUIContent(label,
                Loc.T("Test here without painting. The curve maps pen pressure to brush response. A mouse uses full pressure.")), EditorStyles.miniLabel);
            Vector3 Point(float input) => new Vector3(chart.x + chart.width * input, chart.yMax - chart.height * pen.Response(input));
            var grid = new Color(.5f, .5f, .5f, .22f);
            EditorGUI.DrawRect(new Rect(chart.center.x, chart.y, 1, chart.height), grid);
            EditorGUI.DrawRect(new Rect(chart.x, chart.center.y, chart.width, 1), grid);
            for (int i = 0; i < _pressureCurve.Length; i++) _pressureCurve[i] = Point((float)i / (_pressureCurve.Length - 1));
            using (new Handles.DrawingScope(StudioGUI.InfoBlue))
                Handles.DrawAAPolyLine(2f, _pressureCurve);
            if (recent || GUIUtility.hotControl == id)
            {
                var dot = Point(_penPressure);
                EditorGUI.DrawRect(new Rect(dot.x - 2, dot.y - 2, 5, 5), EditorGUIUtility.isProSkin ? Color.white : Color.black);
            }
        }
    }
}
