using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        enum GradientDirection { Free, Horizontal, Vertical, Both, Custom }
        enum UvPaintLimit { None, Horizontal, Vertical, Both, Custom }
        static readonly GradientDirection[] GradientDirections = (GradientDirection[])System.Enum.GetValues(typeof(GradientDirection));
        static readonly GradientDirection[] BasicGradientDirections = { GradientDirection.Free, GradientDirection.Horizontal, GradientDirection.Vertical };
        static readonly UvPaintLimit[] UvPaintLimits = (UvPaintLimit[])System.Enum.GetValues(typeof(UvPaintLimit));

        [SerializeField] GradientDirection _gradientDirection;
        [SerializeField] float _gradientDirectionAngle = 45f;
        Vector3 _sceneGradientRight, _sceneGradientUp;
        [SerializeField] UvPaintLimit _uvPaintLimit;
        [SerializeField] Vector2 _paintU = new Vector2(.25f, .75f);
        [SerializeField] Vector2 _paintV = new Vector2(.25f, .75f);

        Vector4 CurrentPaintUvBounds => _uvPaintLimit switch
        {
            UvPaintLimit.Horizontal => new Vector4(_paintU.x, 0f, _paintU.y, 1f),
            UvPaintLimit.Vertical => new Vector4(0f, _paintV.x, 1f, _paintV.y),
            UvPaintLimit.Both or UvPaintLimit.Custom => new Vector4(_paintU.x, _paintV.x, _paintU.y, _paintV.y),
            _ => new Vector4(0f, 0f, 1f, 1f),
        };

        bool InsidePaintUvLimit(Vector2 point)
        {
            if (_uvPaintLimit == UvPaintLimit.None) return true;
            var bounds = CurrentPaintUvBounds;
            return point.x >= bounds.x && point.y >= bounds.y && point.x <= bounds.z && point.y <= bounds.w;
        }

        void CapturePaintUvLimit(PaintStroke stroke)
        {
            stroke.limitPaintUV = _uvPaintLimit != UvPaintLimit.None;
            stroke.paintUVBounds = CurrentPaintUvBounds;
        }

        void DrawPaintUvLimitGuide(Rect texture)
        {
            if (_uvPaintLimit == UvPaintLimit.None) return;
            var b = CurrentPaintUvBounds;
            var area = Rect.MinMaxRect(texture.x + b.x * texture.width, texture.y + (1f - b.w) * texture.height,
                texture.x + b.z * texture.width, texture.y + (1f - b.y) * texture.height);
            StudioGUI.DrawBorder(area, new Color(.25f, .81f, 1f, .9f), 1f);
        }

        void DrawPaintUvLimit(float width = -1f)
        {
            if (!Advanced)
            {
                if (_uvPaintLimit != UvPaintLimit.None)
                    GUILayout.Label(Loc.T("Paint area limit is on. Change it in Advanced."), StudioGUI.Hint);
                return;
            }
            EditorGUI.BeginChangeCheck();
            bool compact = width > 0f && width < StackedChoiceWidth;
            var choices = new[] { Loc.T("Unlimited"), Loc.T("Horizontal"), Loc.T("Vertical"), Loc.T("Both axes"),
                compact ? Loc.T("Custom") : Loc.T("Custom bounds") };
            var limit = SteppedChoice(new GUIContent(Loc.T("Paint area"),
                Loc.T("Limit new brush and Fill marks to part of the texture. The blue outline shows the area.")), _uvPaintLimit, UvPaintLimits,
                (rect, current) => (UvPaintLimit)EditorGUI.Popup(rect, (int)current, choices), width);
            StudioGUI.Count(width > 0f ? "scene.paintArea" : "paint.uvAreaLimit", GUILayoutUtility.GetLastRect());
            Vector2 u = _paintU, v = _paintV;
            if (_uvPaintLimit == UvPaintLimit.Horizontal || _uvPaintLimit == UvPaintLimit.Both)
                DrawPaintAreaRange(Loc.T("Horizontal area"), ref u, width);
            if (_uvPaintLimit == UvPaintLimit.Vertical || _uvPaintLimit == UvPaintLimit.Both)
                DrawPaintAreaRange(Loc.T("Vertical area"), ref v, width);
            if (_uvPaintLimit == UvPaintLimit.Custom)
            {
                u = DrawPaintAreaCoordinates(Loc.T("Horizontal start and end"), u, width);
                v = DrawPaintAreaCoordinates(Loc.T("Vertical start and end"), v, width);
                GUILayout.Label(Loc.T("Enter the range on each axis from 0 to 1."), StudioGUI.Hint);
            }
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(this, "Change paint area");
            _uvPaintLimit = limit;
            _paintU = NormalizePaintUvRange(u);
            _paintV = NormalizePaintUvRange(v);
            Repaint();
            SceneView.RepaintAll();
        }

        static void DrawPaintAreaRange(string label, ref Vector2 range, float width)
        {
            if (width > 0f)
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                label = "";
            }
            EditorGUILayout.MinMaxSlider(label, ref range.x, ref range.y, 0f, 1f);
        }

        static Vector2 DrawPaintAreaCoordinates(string label, Vector2 range, float width)
        {
            if (width <= 0f) return EditorGUILayout.Vector2Field(label, range);
            GUILayout.Label(label, EditorStyles.miniLabel);
            return EditorGUILayout.Vector2Field(GUIContent.none, range);
        }

        static Vector2 NormalizePaintUvRange(Vector2 range)
        {
            if (!float.IsFinite(range.x) || !float.IsFinite(range.y)) return new Vector2(0f, 1f);
            float min = Mathf.Clamp01(Mathf.Min(range.x, range.y));
            float max = Mathf.Clamp01(Mathf.Max(range.x, range.y));
            const float minimumWidth = 1f / 4096f;
            if (max - min >= minimumWidth) return new Vector2(min, max);
            min = Mathf.Min(min, 1f - minimumWidth);
            return new Vector2(min, min + minimumWidth);
        }

        Vector2 ConstrainGradientDirection(Vector2 delta)
        {
            switch (_gradientDirection)
            {
                case GradientDirection.Horizontal: return new Vector2(delta.x, 0f);
                case GradientDirection.Vertical: return new Vector2(0f, delta.y);
                case GradientDirection.Both: return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                    ? new Vector2(delta.x, 0f) : new Vector2(0f, delta.y);
                case GradientDirection.Custom:
                    float angle = _gradientDirectionAngle * Mathf.Deg2Rad;
                    var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    return axis * Vector2.Dot(delta, axis);
                default: return delta;
            }
        }

        Vector2 ConstrainTextureGradient(Vector2 anchor, Vector2 point, bool snap)
        {
            if (_gradientDirection == GradientDirection.Free) return snap ? Snap45(anchor, point) : point;
            var size = new Vector2(_comp.Width, _comp.Height);
            var axis = ConstrainGradientDirection(Vector2.Scale(point - anchor, size));
            return anchor + new Vector2(axis.x / size.x, axis.y / size.y);
        }

        void SetSceneGradientAxes(Vector3 right, Vector3 up)
        {
            _sceneGradientRight = Vector3.ProjectOnPlane(right, _sceneGradientNormal).normalized;
            if (_sceneGradientRight == Vector3.zero)
                _sceneGradientRight = Vector3.Cross(up, _sceneGradientNormal).normalized;
            _sceneGradientUp = Vector3.Cross(_sceneGradientNormal, _sceneGradientRight).normalized;
            if (Vector3.Dot(_sceneGradientUp, up) < 0f) _sceneGradientUp = -_sceneGradientUp;
        }

        Vector3 ConstrainSceneGradientPoint(Vector3 anchor, Vector3 point)
        {
            if (_gradientDirection == GradientDirection.Free) return point;
            var delta = point - anchor;
            var along = ConstrainGradientDirection(new Vector2(Vector3.Dot(delta, _sceneGradientRight), Vector3.Dot(delta, _sceneGradientUp)));
            return anchor + _sceneGradientRight * along.x + _sceneGradientUp * along.y +
                Vector3.Project(delta, _sceneGradientNormal);
        }

        static string GradientDirectionLabel(GradientDirection direction) => direction switch
        {
            GradientDirection.Horizontal => Loc.T("Horizontal"),
            GradientDirection.Vertical => Loc.T("Vertical"),
            GradientDirection.Both => Loc.T("Horizontal or vertical"),
            GradientDirection.Custom => Loc.T("Custom angle"),
            _ => Loc.T("Free"),
        };

        void DrawGradientDirection(float sceneWidth = -1f)
        {
            bool inScene = sceneWidth > 0f;
            if (!Advanced && !inScene)
            {
                if (_gradientDirection != GradientDirection.Free)
                    GUILayout.Label(Loc.F("Direction limit: {0}. Change it in Advanced.", GradientDirectionLabel(_gradientDirection)), StudioGUI.Hint);
                return;
            }
            EditorGUI.BeginChangeCheck();
            bool compact = inScene && sceneWidth < StackedChoiceWidth;
            var choices = GradientDirections;
            if (!Advanced)
            {
                choices = _gradientDirection == GradientDirection.Both || _gradientDirection == GradientDirection.Custom
                    ? new[] { GradientDirection.Free, GradientDirection.Horizontal, GradientDirection.Vertical, _gradientDirection }
                    : BasicGradientDirections;
            }
            var directions = System.Array.ConvertAll(choices,
                direction => compact && direction == GradientDirection.Both ? Loc.T("Either axis") : GradientDirectionLabel(direction));
            var choice = SteppedChoice(new GUIContent(inScene ? Loc.T("Direction") : Loc.T("Direction limit"),
                Loc.T("Constrain a new drag or an end handle. The axes follow the texture or the current Scene view.")),
                _gradientDirection, choices, (rect, current) =>
                {
                    int picked = EditorGUI.Popup(rect, System.Array.IndexOf(choices, current), directions);
                    return picked >= 0 ? choices[picked] : current;
                }, sceneWidth);
            StudioGUI.Count(inScene ? "scene.gradientDirection" : "paint.gradientDirection", GUILayoutUtility.GetLastRect());
            float angle = _gradientDirectionAngle;
            if (Advanced && choice == GradientDirection.Custom)
            {
                var angleLabel = new GUIContent(Loc.T("Angle (°)"), Loc.T("0° points right and 90° points up."));
                angle = inScene ? EditorGUI.Slider(SceneBarRow(sceneWidth), angleLabel, angle, -180f, 180f)
                    : EditorGUILayout.Slider(angleLabel, angle, -180f, 180f);
            }
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(this, "Change gradient direction limit");
            _gradientDirection = choice;
            _gradientDirectionAngle = angle;
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
