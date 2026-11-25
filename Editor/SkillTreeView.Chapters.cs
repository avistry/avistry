using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal sealed partial class SkillTreeView
    {
        Vector2 _chapterScroll;
        int _chapterCursor = -1;
        static readonly Type[] ChapterTypes = {
            typeof(GameObject), typeof(Texture2D), typeof(Mesh), typeof(RenderTexture),
            typeof(Material), typeof(Light), typeof(TrailRenderer), typeof(Shader), typeof(Animator)
        };
        static readonly Texture[] ChapterIcons = new Texture[ChapterTypes.Length];
        static GUIStyle s_libraryTitle, s_chapterTitle, s_chapterBlurb;
        static bool s_libraryPro;

        static void ChapterStyles()
        {
            if (s_libraryTitle != null && s_libraryPro == EditorGUIUtility.isProSkin) return;
            s_libraryPro = EditorGUIUtility.isProSkin;
            s_libraryTitle = Plain(22, StudioGUI.CardText, FontStyle.Bold, TextAnchor.MiddleLeft);
            s_chapterTitle = Plain(15, StudioGUI.CardText, FontStyle.Bold, TextAnchor.MiddleLeft);
            s_chapterBlurb = Plain(12, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.UpperLeft);
            s_chapterBlurb.wordWrap = true;
        }

        void DrawChapters(Rect area)
        {
            ChapterStyles();
            var tiers = Glossary.Tiers;
            var ev = Event.current;
            if (ev.type == EventType.Repaint) EditorGUI.DrawRect(area, StudioGUI.CardBg);
            float width = Mathf.Max(1f, area.width - ScrollBarW);
            float inset = width >= 640f ? 18f : 10f;
            float span = width - inset * 2f;
            const float gap = 8f, cardH = 111f;
            int cols = span >= 705f ? 3 : span >= 458f ? 2 : 1;
            float tile = (span - (cols - 1) * gap) / cols;
            float top = 64f;
            bool compact = span < 448f;
            if (compact && _next.Count > 0) top += 30f;
            int rows = Mathf.CeilToInt(tiers.Count / (float)cols);
            float tail = top + rows * (cardH + gap);
            string bonus = null;
            foreach (var e in _gloss) if (e.bonus) { bonus = e.id; break; }
            LastHeight = tail + 10f + MedalsH + (bonus == null ? 0f : 32f);
            int pick = -1;
            if (ev.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "" && !ev.control && !ev.command && !ev.alt)
            {
                int target = _chapterCursor;
                switch (ev.keyCode)
                {
                    case KeyCode.RightArrow: target++; break;
                    case KeyCode.LeftArrow: target--; break;
                    case KeyCode.UpArrow: target -= cols; break;
                    case KeyCode.DownArrow: target = target < 0 ? 0 : target + cols; break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter: pick = _chapterCursor; break;
                }
                if (target != _chapterCursor || pick >= 0)
                {
                    _chapterCursor = Mathf.Clamp(target, 0, tiers.Count - 1);
                    float cardY = top + (_chapterCursor / cols) * (cardH + gap);
                    _chapterScroll.y = Mathf.Clamp(_chapterScroll.y, cardY + cardH - area.height, cardY);
                    ev.Use();
                }
            }
            _chapterScroll = GUI.BeginScrollView(area, _chapterScroll, new Rect(0f, 0f, width, LastHeight));
            GUI.Label(new Rect(inset, 9f, span, 38f), Loc.T("Choose a chapter"), s_libraryTitle);
            string concept = null;
            if (_next.Count > 0)
            {
                float actionW = compact ? span : Mathf.Min(258f, span * .48f);
                var action = new Rect(compact ? inset : width - inset - actionW, compact ? 49f : 15f, actionW, 27f);
                var next = Glossary.Get(_next[0]);
                var label = new GUIContent(Loc.T("Continue learning") + "  ›  " + next.title, next.definition);
                if (ChapterAction(action, label, StudioGUI.CardAccent)) concept = next.id;
            }
            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                var r = new Rect(inset + (i % cols) * (tile + gap), top + (i / cols) * (cardH + gap), tile, cardH);
                if (r.yMax < _chapterScroll.y || r.y > _chapterScroll.y + area.height) continue;
                if (DrawChapterTile(r, t, i == _chapterCursor)) pick = i;
            }
            if (bonus != null)
            {
                var e = Glossary.Get(bonus);
                if (ChapterAction(new Rect(inset, tail, span, 28f), new GUIContent(Loc.T("Optional lessons") + "   ›   " + e.title, e.definition), StudioGUI.CardTextDim))
                    concept = bonus;
            }
            DrawMedals(new Rect(0, LastHeight - MedalsH, width, MedalsH));
            GUI.EndScrollView();
            if (pick >= 0 && pick < tiers.Count)
            {
                _chapterCursor = pick;
                foreach (var e in _gloss)
                {
                    if (e.bonus || Glossary.TierOf(e) != tiers[pick].n) continue;
                    if (concept == null) concept = e.id;
                    if (!Learn.IsDone(e.id)) { concept = e.id; break; }
                }
            }
            if (concept != null) Select(concept, true);
        }

        static bool ChapterAction(Rect r, GUIContent label, Color accent)
        {
            if (Event.current.type == EventType.Repaint)
            {
                bool hover = r.Contains(Event.current.mousePosition);
                var fill = Color.Lerp(StudioGUI.CardInset, accent, hover ? .2f : .09f);
                StudioGUI.DrawRoundedBox(r, fill, Color.Lerp(StudioGUI.CardBorder, accent, .5f), 5f);
                GUI.Label(new Rect(r.x + 11f, r.y, r.width - 22f, r.height), label, s_tierName);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            }
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        bool DrawChapterTile(Rect r, Glossary.Tier chapter, bool focused)
        {
            int tier = chapter.n;
            var colour = StudioGUI.TierColour(tier);
            bool hover = r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                StudioGUI.DrawRounded(r, focused || hover ? colour : StudioGUI.CardBorder, 8f);
                var inner = new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f);
                StudioGUI.DrawRounded(inner, Color.Lerp(StudioGUI.CardInset, colour, hover ? .16f : .045f), 7f);
                var icon = new Rect(r.x + 12f, r.y + 10f, 24f, 24f);
                StudioGUI.DrawRounded(icon, Color.Lerp(StudioGUI.CardInset, colour, .24f), 5f);
                int index = tier - 1;
                Texture image = null;
                if (index < ChapterIcons.Length)
                    image = ChapterIcons[index] ?? (ChapterIcons[index] = EditorGUIUtility.ObjectContent(null, ChapterTypes[index]).image);
                if (image) GUI.DrawTexture(new Rect(icon.x + 4f, icon.y + 4f, 16f, 16f), image, ScaleMode.ScaleToFit);
                var count = new Rect(icon.xMax + 4f, icon.y, r.xMax - icon.xMax - 16f, icon.height);
                GUI.Label(count, _tierCount[tier], _tierComplete[tier] ? s_countDone : s_count);
                GUI.Label(new Rect(r.x + 12f, r.y + 35f, r.width - 24f, 23f), chapter.name, s_chapterTitle);
                GUI.Label(new Rect(r.x + 12f, r.y + 61f, r.width - 24f, 37f), chapter.blurb, s_chapterBlurb);
                var track = new Rect(r.x + 12f, r.yMax - 9f, r.width - 24f, 3f);
                StudioGUI.DrawRounded(track, StudioGUI.CardBorder, 1.5f);
                track.width *= _tierFraction[tier];
                if (track.width > 0f) StudioGUI.DrawRounded(track, colour, 1.5f);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            }
            return GUI.Button(r, _medalTip[tier], GUIStyle.none);
        }
    }
}
