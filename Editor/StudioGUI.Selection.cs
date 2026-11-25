using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal enum SelectGlyph
    {
        Click, Box, Lasso, Wand, Part, Tile, Face, BoundaryLoop, FaceLoop, Vertex, Edge,
        Replace, Add, Subtract, All, None, Invert, Reveal, Hide, Wire, Spotlight, Grow, Shrink, Paint, Gradient,
        Erase, Fill, Blend, Blur, Smudge, Linear, Circle, Ellipse, Material, Piece, Rotate, Tint,
        Recolour, Match, Reset, Keep, Eyedropper, PaintSelected, BackToPaint
    }

    static partial class StudioGUI
    {
        const string SelectionAtlasGuid = "42a5ebad41a047cb8492f2ec1a7b71d4";
        static Texture2D s_selectionAtlas;
        static GUIStyle s_selectionName;
        static readonly GUIContent s_selectionTip = new GUIContent();
        static GUIStyle SelectionName => s_selectionName ??= new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };

        internal static float SelectionCellWidth(GUIContent content) => 32f + Mathf.Ceil(SelectionName.CalcSize(content).x);

        internal static bool DrawToolGlyph(Rect area, SelectGlyph glyph, Color color)
        {
            if (!s_selectionAtlas)
                s_selectionAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(SelectionAtlasGuid));
            if (!s_selectionAtlas) return false;
            int index = (int)glyph;
            float rowHeight = s_selectionAtlas.width / (5f * s_selectionAtlas.height);
            var uv = new Rect((index % 5) * .2f, 1f - (index / 5 + 1) * rowHeight, .2f, rowHeight);
            var tint = GUI.color;
            GUI.color = tint * color;
            GUI.DrawTextureWithTexCoords(area, s_selectionAtlas, uv);
            GUI.color = tint;
            return true;
        }

        internal static bool SelectionCell(Rect rect, SelectGlyph glyph, GUIContent content, bool selected, bool showLabel = false)
        {
            bool click = KeyboardButton(rect, (int)glyph + 37257, out bool hover, out bool focus);
            DrawToolCell(rect, glyph, content, selected, showLabel, hover, focus);
            return click;
        }

        internal static void DrawToolCell(Rect rect, SelectGlyph glyph, GUIContent content,
            bool selected, bool showLabel, bool hover, bool focus)
        {
            if (Event.current.type != EventType.Repaint) return;
            var border = selected || hover ? CardAccent : CardBorder;
            var fill = selected ? CardTryFill : hover ? TabActiveBg : CardInset;
            DrawRoundedBox(rect, fill, border, 4f);
            if (focus)
                DrawRoundedBorder(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), CardBarWhat, 3f);

            float size = Mathf.Min(showLabel ? 18f : 20f, Mathf.Min(rect.height - 5f, rect.width - 4f));
            var icon = new Rect(showLabel ? rect.x + 6f : rect.center.x - size * .5f, rect.center.y - size * .5f, size, size);
            if (DrawToolGlyph(icon, glyph, selected ? CardAccent : CardText))
            {
                if (showLabel)
                {
                    var style = SelectionName;
                    style.normal.textColor = CardText;
                    var label = Rect.MinMaxRect(icon.xMax + 4f, rect.y, rect.xMax - 3f, rect.yMax);
                    GUI.Label(label, content.text, style);
                }
                if (selected)
                    EditorGUI.DrawRect(new Rect(rect.center.x - 4f, rect.yMax - 2f, 8f, 2f), CardAccent);
            }
            else GUI.Label(rect, content.text, EditorStyles.centeredGreyMiniLabel);

            if (hover)
            {
                s_selectionTip.tooltip = string.IsNullOrEmpty(content.tooltip) ? content.text : content.tooltip;
                GUI.Label(rect, s_selectionTip, GUIStyle.none);
            }
        }
    }
}
