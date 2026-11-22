using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal enum CardMode { Popup, Detail, Row }

    /// <summary>What a click on the card asks the host to do. The host acts after drawing, never inside the card.</summary>
    internal struct CardResult
    {
        public string GoTo;
        public bool Back, OpenTree, ShowMe, Close;
    }

    /// <summary>
    /// One glossary entry as a card: kicker, title, lede, coloured sections, the Try it box, links. The info popup, the
    /// skill tree's detail pane and the A to Z rows all draw it, so the copy looks the same everywhere. Each part measures
    /// and draws in the same function, so the height always fits.
    /// </summary>
    internal static class ConceptCard
    {
        internal const float PopupWidth = 360f;
        internal const float MaxPopupHeight = 560f;

        const float Pad = 14f, TopPad = 12f, SectionGap = 6f, HeadGap = 2f;
        const float Indent = 10f, BarW = 3f;
        const float ChipH = 20f, ChipGap = 4f, ChipPad = 8f, ChipDot = 8f;
        const float PillH = 18f, RowH = 22f, BoxPad = 8f, FooterH = 32f, LinkH = 20f;
        const float ShowMeW = 72f, GotItW = 92f, ScrollBarW = 15f, WebRowH = 36f, WebIcon = 20f;
        static string GotItText => Loc.T("When this makes sense to you, press I've got it. It is only a note for you.");

        /// <summary>
        /// What "Show me" does with a concept id. The skill tree and the main window set it; while it is null the button is
        /// not drawn, because the card itself never touches the main window.
        /// </summary>
        internal static Action<string> ShowMeHandler;

        /// <summary>"Open in skill tree": the glossary window scrolls to the concept.</summary>
        internal static void OpenInTree(string conceptId) => GlossaryWindow.Open(conceptId);

        static GUIContent s_hWhat => new GUIContent(Loc.T("What it is"));
        static GUIContent s_hWhen => new GUIContent(Loc.T("When to use it"));
        static GUIContent s_hExample => new GUIContent(Loc.T("Example"));
        static GUIContent s_hWatch => new GUIContent(Loc.T("Watch out"));
        static GUIContent s_hAfter => new GUIContent(Loc.T("Comes after"));
        static GUIContent s_hRelated => new GUIContent(Loc.T("Related"));
        static GUIContent s_gotItBody => new GUIContent(GotItText);

        sealed class Chip { public string Id; public GUIContent Content; public float Width; public int Tier; }

        /// <summary>One "Learn more online" row: the label, the address under it, and the page and full address on hover.</summary>
        sealed class WebLink { public string Url; public GUIContent Label, Shown, Hover; public bool Video; }

        sealed class Info
        {
            public Glossary.Entry Entry;
            public int Tier;
            public string Kicker;
            public GUIContent Title, Also, Lede, What, When, Example, TryIt, Watch, Unity;
            public Chip[] After, Related;
            public WebLink[] Web;
            public GUIContent WebHead;
            public bool HasBox, CanShowMe;
            public string ManualUrl;
        }

        static readonly Dictionary<string, Info> s_simple = new Dictionary<string, Info>(), s_advanced = new Dictionary<string, Info>();
        static readonly int ButtonHint = "ATSCardButton".GetHashCode();

        static GUIStyle s_chip, s_link, s_button, s_pill, s_status, s_bang, s_close, s_webLabel, s_webUrl;
        static bool s_stylesPro;
        static string s_stylesLang;
        static float s_pillW, s_treeW, s_manualW, s_backW;

        static GUIStyle Plain(int size, Color colour, FontStyle weight, TextAnchor anchor)
        {
            var s = new GUIStyle(EditorStyles.label)
            {
                fontSize = size,
                fontStyle = weight,
                alignment = anchor,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                margin = new RectOffset(),
                padding = new RectOffset(),
            };
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = colour;
            return s;
        }

        static void Styles()
        {
            // The widths below are measured from translated words, so a language change measures again.
            if (s_chip != null && s_stylesPro == EditorGUIUtility.isProSkin && s_stylesLang == Loc.Current) return;
            s_stylesPro = EditorGUIUtility.isProSkin;
            s_stylesLang = Loc.Current;
            s_chip = Plain(11, StudioGUI.CardText, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_link = Plain(11, StudioGUI.CardAccent, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_button = Plain(11, StudioGUI.CardAccent, FontStyle.Bold, TextAnchor.MiddleCenter);
            s_pill = Plain(11, StudioGUI.CardText, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_status = Plain(11, StudioGUI.CardText, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_bang = Plain(13, StudioGUI.CardDanger, FontStyle.Bold, TextAnchor.UpperLeft);
            s_close = Plain(18, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleCenter);
            s_webLabel = Plain(12, StudioGUI.CardText, FontStyle.Bold, TextAnchor.MiddleLeft);
            s_webUrl = Plain(10, StudioGUI.CardTextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            s_pillW = 8f + 12f + 4f + Mathf.Max(s_pill.CalcSize(new GUIContent(Loc.T("Done"))).x, s_pill.CalcSize(new GUIContent(Loc.T("Read"))).x, s_pill.CalcSize(new GUIContent(Loc.T("New"))).x) + 8f;
            s_treeW = s_link.CalcSize(new GUIContent(Loc.T("Open in skill tree ›"))).x;
            s_manualW = s_link.CalcSize(new GUIContent(Loc.T("Open manual"))).x;
            s_backW = s_link.CalcSize(new GUIContent(Loc.T("‹ Back"))).x;
        }

        static GUIContent Text(string s) => string.IsNullOrEmpty(s) ? null : new GUIContent(s);

        // Rebuilt only when the entry object or the mode changes, so a redraw never builds strings.
        static Info Resolve(Glossary.Entry e)
        {
            Styles();
            bool adv = StudioMode.Advanced;
            var cache = adv ? s_advanced : s_simple;
            if (cache.TryGetValue(e.id, out var i) && i.Entry == e) return i;
            i = new Info { Entry = e, Tier = Glossary.TierOf(e) };
            var tier = Glossary.TierInfo(i.Tier);
            i.Kicker = e.bonus ? Loc.T("BONUS · NOT COUNTED") : Loc.F("TIER {0}", i.Tier) + " · " + (tier != null && tier.name != null ? tier.name.ToUpperInvariant() : "");
            i.Title = Text(e.title);
            i.Also = e.alsoCalled != null && e.alsoCalled.Length > 0 ? new GUIContent(Loc.F("Also called: {0}", string.Join(", ", e.alsoCalled))) : null;
            i.Lede = Text(Glossary.ResolveTokens(e.definition, adv));
            i.What = Text(Glossary.ResolveTokens(e.what, adv));
            i.When = Text(Glossary.ResolveTokens(e.when, adv));
            i.Example = Text(Glossary.ResolveTokens(e.example, adv));
            i.TryIt = Text(Glossary.ResolveTokens(e.tryIt, adv));
            i.Watch = Text(Glossary.ResolveTokens(e.watchOut, adv));
            i.Unity = string.IsNullOrEmpty(e.whereInUnity) ? null : new GUIContent(Loc.F("In Unity: {0}", e.whereInUnity));
            i.After = Chips(e.requires);
            i.Related = Chips(e.related);
            i.HasBox = i.TryIt != null || e.gotIt;
            i.CanShowMe = i.TryIt != null && (!string.IsNullOrEmpty(e.tryTab) || !string.IsNullOrEmpty(e.tryControl));
            i.ManualUrl = !string.IsNullOrEmpty(e.docsUrl) && e.docsUrl.StartsWith("https://", StringComparison.Ordinal) ? e.docsUrl : null;
            i.Web = WebLinks(e);
            i.WebHead = new GUIContent(OnlineGuide.Label("web.heading", "Learn more online"), OnlineGuide.Tip("web.heading"));
            cache[e.id] = i;
            return i;
        }

        static Chip[] Chips(string[] ids)
        {
            if (ids == null || ids.Length == 0) return Array.Empty<Chip>();
            var list = new List<Chip>(ids.Length);
            foreach (var id in ids)
            {
                var re = Glossary.Get(id);
                if (re == null) continue;
                var c = new GUIContent(re.title);
                list.Add(new Chip { Id = id, Content = c, Tier = Glossary.TierOf(re), Width = ChipPad + ChipDot + 4f + s_chip.CalcSize(c).x + ChipPad });
            }
            return list.ToArray();
        }

        // The feature tour section that shows the concept (when there is one), then the concept in the online glossary.
        static WebLink[] WebLinks(Glossary.Entry e)
        {
            var list = new List<WebLink>(2);
            var page = OnlineGuide.ForConcept(e.id);
            if (page != null)
            {
                string url = OnlineGuide.Url(page);
                list.Add(new WebLink { Url = url, Video = page.Video, Label = new GUIContent(OnlineGuide.PageLabel(page)), Shown = new GUIContent(OnlineGuide.Shown(url)), Hover = new GUIContent("", OnlineGuide.Hover(page, url)) });
            }
            string term = OnlineGuide.TermUrl(e.id);
            string tip = OnlineGuide.Tip("web.term");
            list.Add(new WebLink
            {
                Url = term,
                Label = new GUIContent(OnlineGuide.Label("web.term", "{0} in the online glossary").Replace("{0}", e.title)),
                Shown = new GUIContent(OnlineGuide.Shown(term)),
                Hover = new GUIContent("", string.IsNullOrEmpty(tip) ? term : tip + "\n" + term),
            });
            return list.ToArray();
        }

        /// <summary>The addresses a card links to, one per line: the feature page first when there is one. For tests.</summary>
        internal static string WebLinksOf(Glossary.Entry e)
        {
            var i = Resolve(e);
            var sb = new StringBuilder();
            foreach (var l in i.Web) sb.Append(l.Video ? "video " : "page ").Append(l.Url).Append('\n');
            return sb.ToString();
        }

        /// <summary>The sections a card draws for this entry, in order, as comma-separated names. Empty fields are skipped.</summary>
        internal static string SectionsOf(Glossary.Entry e)
        {
            var i = Resolve(e);
            var sb = new StringBuilder("kicker,title");
            if (i.Also != null) sb.Append(",also");
            if (i.Lede != null) sb.Append(",lede");
            if (i.What != null) sb.Append(",what");
            if (i.When != null) sb.Append(",when");
            if (i.Example != null) sb.Append(",example");
            if (i.HasBox) sb.Append(",try");
            if (i.Watch != null) sb.Append(",watch");
            if (i.Unity != null) sb.Append(",unity");
            if (i.After.Length > 0) sb.Append(",after");
            if (i.Related.Length > 0) sb.Append(",related");
            if (i.Web.Length > 0) sb.Append(",online");
            sb.Append(",footer");
            return sb.ToString();
        }

        /// <summary>Popup size: 360 wide, as tall as the content up to 560. The Try it box has the same height in every state.</summary>
        internal static Vector2 PopupSize(Glossary.Entry e)
        {
            var i = Resolve(e);
            var res = default(CardResult);
            float h = Header(i, CardMode.Popup, 0, 0, PopupWidth, false, true, ref res) + Body(i, 0, 0, PopupWidth, false, ref res) + FooterH;
            return new Vector2(PopupWidth, Mathf.Min(Mathf.Ceil(h), MaxPopupHeight));
        }

        /// <summary>
        /// The popup: header and footer stay pinned, and the middle scrolls when the card is taller than the window.
        /// </summary>
        internal static void DrawPopup(Rect rect, Glossary.Entry e, ref Vector2 scroll, ref CardResult res, bool canBack)
        {
            var i = Resolve(e);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, StudioGUI.CardBg);
            var none = default(CardResult);
            float head = Header(i, CardMode.Popup, 0, 0, rect.width, false, true, ref none);
            float body = Body(i, 0, 0, rect.width, false, ref none);
            float view = rect.height - head - FooterH;
            bool scrolls = body > view + 0.5f;
            if (scrolls) body = Body(i, 0, 0, rect.width - ScrollBarW, false, ref none);

            Header(i, CardMode.Popup, rect.x, rect.y, rect.width, true, true, ref res);
            if (scrolls)
            {
                scroll = GUI.BeginScrollView(new Rect(rect.x, rect.y + head, rect.width, view), scroll, new Rect(0, 0, rect.width - ScrollBarW, body));
                Body(i, 0, 0, rect.width - ScrollBarW, true, ref res);
                GUI.EndScrollView();
            }
            else Body(i, rect.x, rect.y + head, rect.width, true, ref res);
            Footer(i, CardMode.Popup, rect.x, rect.yMax - FooterH, rect.width, true, canBack, ref res);
        }

        /// <summary>Measures and, when <paramref name="draw"/>, draws a card in <paramref name="r"/> (height is ignored). Returns the height used.</summary>
        internal static float Layout(Glossary.Entry e, Rect r, CardMode mode, bool draw, bool expanded = true)
        {
            var res = default(CardResult);
            return Layout(e, r, mode, draw, ref res, expanded);
        }

        internal static float Layout(Glossary.Entry e, Rect r, CardMode mode, bool draw, ref CardResult res, bool expanded = true)
        {
            if (e == null) return 0;
            var i = Resolve(e);
            var none = default(CardResult);
            bool full = expanded || mode != CardMode.Row;
            float head = Header(i, mode, r.x, r.y, r.width, false, full, ref none);
            float body = full ? Body(i, r.x, r.y + head, r.width, false, ref none) : 0f;
            float foot = full ? Footer(i, mode, r.x, r.y + head + body, r.width, false, false, ref none) : 0f;
            float total = head + body + foot;
            if (!draw) return total;
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, total), StudioGUI.CardBg);
            Header(i, mode, r.x, r.y, r.width, true, full, ref res);
            if (full)
            {
                Body(i, r.x, r.y + head, r.width, true, ref res);
                Footer(i, mode, r.x, r.y + head + body, r.width, true, false, ref res);
            }
            return total;
        }

        static Color StateColour(Learn.State s, int tier) => s == Learn.State.Done ? StudioGUI.CardGood : s == Learn.State.Read ? StudioGUI.TierColour(tier) : StudioGUI.CardTextDim;

        static GUIStyle TitleStyle(CardMode mode) => mode == CardMode.Detail ? StudioGUI.CardTitleDetail : mode == CardMode.Row ? StudioGUI.CardTitleRow : StudioGUI.CardTitle;

        static float Header(Info i, CardMode mode, float x, float y0, float w, bool draw, bool divider, ref CardResult res)
        {
            var e = i.Entry;
            float cx = x + Pad, cw = w - Pad * 2, y = y0 + TopPad;
            var state = Learn.StateOf(e.id);
            bool unmarkable = e.gotIt && state == Learn.State.Done;
            float titleW = cw;

            if (mode == CardMode.Row)
            {
                titleW = cw - s_pillW - 6f;
                if (draw) Pill(new Rect(cx + cw - s_pillW, y, s_pillW, PillH), state, i.Tier);
            }
            else
            {
                float right = cx + cw;
                if (mode == CardMode.Popup)
                {
                    right -= 18f;
                    if (draw && CloseButton(new Rect(right, y, 18f, PillH))) res.Close = true;
                    right -= 4f;
                }
                if (unmarkable)
                {
                    right -= 18f;
                    if (draw) MoreButton(new Rect(right, y, 18f, PillH), e.id);
                    right -= 4f;
                }
                right -= s_pillW;
                if (draw)
                {
                    Pill(new Rect(right, y, s_pillW, PillH), state, i.Tier);
                    GUI.Label(new Rect(cx, y + 2f, right - cx - 6f, 14f), i.Kicker, StudioGUI.CardKicker(i.Tier));
                }
                y += PillH + 4f;
            }

            float h = TitleStyle(mode).CalcHeight(i.Title, titleW);
            if (draw) GUI.Label(new Rect(cx, y, titleW, h), i.Title, TitleStyle(mode));
            y += h + HeadGap;

            if (i.Also != null)
            {
                h = StudioGUI.CardAlso.CalcHeight(i.Also, cw);
                if (draw) GUI.Label(new Rect(cx, y, cw, h), i.Also, StudioGUI.CardAlso);
                y += h + 4f;
            }
            if (i.Lede != null)
            {
                h = StudioGUI.CardLede.CalcHeight(i.Lede, cw);
                if (draw) GUI.Label(new Rect(cx, y, cw, h), i.Lede, StudioGUI.CardLede);
                y += h;
            }
            y += 10f;
            if (divider)
            {
                if (draw) EditorGUI.DrawRect(new Rect(cx, y, cw, 1f), StudioGUI.CardDivider);
                y += 1f;
            }
            if (draw && mode != CardMode.Row) EditorGUI.DrawRect(new Rect(x, y0, 4f, y - y0), StudioGUI.TierColour(i.Tier));
            return y - y0;
        }

        static float Body(Info i, float x, float y0, float w, bool draw, ref CardResult res)
        {
            float cx = x + Pad, cw = w - Pad * 2, y = y0 + 10f;
            y += Section(s_hWhat, i.What, StudioGUI.CardBarWhat, false, cx, y, cw, draw);
            y += Section(s_hWhen, i.When, StudioGUI.CardBarWhen, false, cx, y, cw, draw);
            y += Section(s_hExample, i.Example, StudioGUI.CardBarExample, true, cx, y, cw, draw);
            y += TryBox(i, cx, y, cw, draw, ref res);
            y += WatchOut(i, cx, y, cw, draw);
            if (i.Unity != null)
            {
                float h = StudioGUI.CardMeta.CalcHeight(i.Unity, cw);
                if (draw) GUI.Label(new Rect(cx, y, cw, h), i.Unity, StudioGUI.CardMeta);
                y += h + SectionGap + 2f;
            }
            y += ChipBlock(s_hAfter, i.After, cx, y, cw, draw, ref res);
            y += ChipBlock(s_hRelated, i.Related, cx, y, cw, draw, ref res);
            y += WebBlock(i, cx, y, cw, draw);
            return y - y0 + 4f;
        }

        static float Section(GUIContent heading, GUIContent text, Color bar, bool boxed, float cx, float y, float cw, bool draw)
        {
            if (text == null) return 0;
            float bw = cw - Indent;
            float hh = StudioGUI.CardHeading.CalcHeight(heading, bw);
            float inner = boxed ? bw - BoxPad * 2 : bw;
            float th = StudioGUI.CardBody.CalcHeight(text, inner);
            float bh = boxed ? th + BoxPad * 2 : th;
            float total = hh + HeadGap + bh;
            if (draw)
            {
                EditorGUI.DrawRect(new Rect(cx, y, BarW, total), bar);
                GUI.Label(new Rect(cx + Indent, y, bw, hh), heading, StudioGUI.CardHeading);
                float by = y + hh + HeadGap;
                if (boxed)
                {
                    StudioGUI.DrawRoundedBox(new Rect(cx + Indent, by, bw, bh), StudioGUI.CardInset, StudioGUI.CardBorder, 4f);
                    GUI.Label(new Rect(cx + Indent + BoxPad, by + BoxPad, inner, th), text, StudioGUI.CardBody);
                }
                else GUI.Label(new Rect(cx + Indent, by, bw, th), text, StudioGUI.CardBody);
            }
            return total + SectionGap + 2f;
        }

        // Same height whether it is not done yet or done, because a popup cannot resize once it is open.
        static float TryBox(Info i, float cx, float y, float cw, bool draw, ref CardResult res)
        {
            if (!i.HasBox) return 0;
            var e = i.Entry;
            var text = i.TryIt ?? s_gotItBody;
            float inner = cw - BoxPad * 2;
            float th = StudioGUI.CardBody.CalcHeight(text, inner);
            float h = BoxPad + RowH + HeadGap + th + 6f + RowH + BoxPad;
            if (draw)
            {
                StudioGUI.DrawRounded(new Rect(cx, y, cw, h), StudioGUI.CardTryFill, 6f);
                float ix = cx + BoxPad, iy = y + BoxPad;
                GUI.Label(new Rect(ix, iy, inner - ShowMeW - 6f, RowH), i.TryIt != null ? Loc.T("Try it") : Loc.T("Got it?"), StudioGUI.CardHeading);
                if (i.CanShowMe && ShowMeHandler != null && PrimaryButton(new Rect(ix + inner - ShowMeW, iy + 1f, ShowMeW, LinkH + 1f), Loc.T("Show me")))
                {
                    res.ShowMe = true;
                    res.Close = true;
                }
                iy += RowH + HeadGap;
                GUI.Label(new Rect(ix, iy, inner, th), text, StudioGUI.CardBody);
                iy += th + 6f;
                bool done = Learn.IsDone(e.id);
                StudioGUI.DrawStateGlyph(new Rect(ix, iy + 5f, 12f, 12f), done ? Learn.State.Done : Learn.State.New, done ? StudioGUI.CardGood : StudioGUI.CardTextDim);
                GUI.Label(new Rect(ix + 16f, iy, 90f, RowH), done ? Loc.T("Done") : Loc.T("Not yet"), s_status);
                if (e.gotIt && !done && PrimaryButton(new Rect(ix + inner - GotItW, iy + 1f, GotItW, LinkH + 1f), Loc.T("I've got it")))
                    Learn.GotIt(e.id);
            }
            return h + SectionGap + 2f;
        }

        static float WatchOut(Info i, float cx, float y, float cw, bool draw)
        {
            if (i.Watch == null) return 0;
            const float Lead = 16f;
            float bw = cw - Lead;
            float hh = StudioGUI.CardHeading.CalcHeight(s_hWatch, bw);
            float th = StudioGUI.CardBody.CalcHeight(i.Watch, bw);
            if (draw)
            {
                GUI.Label(new Rect(cx + 2f, y - 1f, Lead, 18f), "!", s_bang);
                GUI.Label(new Rect(cx + Lead, y, bw, hh), s_hWatch, StudioGUI.CardHeading);
                GUI.Label(new Rect(cx + Lead, y + hh + HeadGap, bw, th), i.Watch, StudioGUI.CardBody);
            }
            return hh + HeadGap + th + SectionGap + 2f;
        }

        static float ChipBlock(GUIContent heading, Chip[] chips, float cx, float y, float cw, bool draw, ref CardResult res)
        {
            if (chips.Length == 0) return 0;
            float hh = StudioGUI.CardHeading.CalcHeight(heading, cw);
            if (draw) GUI.Label(new Rect(cx, y, cw, hh), heading, StudioGUI.CardHeading);
            float top = y + hh + 3f, x = 0f;
            int row = 0;
            for (int k = 0; k < chips.Length; k++)
            {
                var c = chips[k];
                float w = Mathf.Min(c.Width, cw);
                if (x > 0f && x + w > cw) { x = 0f; row++; }
                if (draw && ChipButton(new Rect(cx + x, top + row * (ChipH + ChipGap), w, ChipH), c)) res.GoTo = c.Id;
                x += w + ChipGap;
            }
            return hh + 3f + (row + 1) * (ChipH + ChipGap) + 2f;
        }

        static float WebBlock(Info i, float cx, float y, float cw, bool draw)
        {
            if (i.Web.Length == 0) return 0;
            float hh = StudioGUI.CardHeading.CalcHeight(i.WebHead, cw);
            if (draw) GUI.Label(new Rect(cx, y, cw, hh), i.WebHead, StudioGUI.CardHeading);
            float top = y + hh + 3f;
            for (int k = 0; k < i.Web.Length; k++)
                if (draw && WebRow(new Rect(cx, top + k * (WebRowH + ChipGap), cw, WebRowH), i.Web[k], i.Tier)) OnlineGuide.Open(i.Web[k].Url);
            return hh + 3f + i.Web.Length * (WebRowH + ChipGap) + SectionGap;
        }

        // A play glyph marks a page with a video. The address stays visible under the label, so a click never surprises.
        static bool WebRow(Rect r, WebLink l, int tier)
        {
            bool click = Click(r, out var hover, out var focus);
            if (Event.current.type == EventType.Repaint)
            {
                StudioGUI.DrawRoundedBox(r, hover ? StudioGUI.CardTryFill : StudioGUI.CardInset, hover ? StudioGUI.CardAccent : StudioGUI.CardBorder, 6f);
                var tile = new Rect(r.x + 8f, r.y + (r.height - WebIcon) * 0.5f, WebIcon, WebIcon);
                var colour = StudioGUI.TierColour(tier);
                StudioGUI.DrawRounded(tile, new Color(colour.r, colour.g, colour.b, 0.18f), 5f);
                StudioGUI.DrawWebGlyph(new Rect(tile.x + 3f, tile.y + 3f, WebIcon - 6f, WebIcon - 6f), l.Video, colour);
                float tx = tile.xMax + 8f, tw = r.xMax - 24f - tx;
                GUI.Label(new Rect(tx, r.y + 3f, tw, 16f), l.Label, s_webLabel);
                GUI.Label(new Rect(tx, r.y + 19f, tw, 13f), l.Shown, s_webUrl);
                StudioGUI.DrawExternalGlyph(new Rect(r.xMax - 19f, r.y + (r.height - 11f) * 0.5f, 11f, 11f), hover ? StudioGUI.CardAccent : StudioGUI.CardTextDim);
                GUI.Label(r, l.Hover, GUIStyle.none);
                if (focus && GUIUtility.hotControl == 0) FocusRing(r, 6f);
            }
            return click;
        }

        static float Footer(Info i, CardMode mode, float x, float y, float w, bool draw, bool canBack, ref CardResult res)
        {
            bool popup = mode == CardMode.Popup;
            if (!popup && i.ManualUrl == null) return 10f;
            float cx = x + Pad, cw = w - Pad * 2;
            if (draw)
            {
                if (popup) EditorGUI.DrawRect(new Rect(cx, y, cw, 1f), StudioGUI.CardDivider);
                float ry = y + (popup ? 8f : 0f);
                float right = cx + cw;
                if (popup)
                {
                    right -= s_treeW;
                    if (LinkButton(new Rect(right, ry, s_treeW, LinkH), Loc.T("Open in skill tree ›"))) { res.OpenTree = true; res.Close = true; }
                    right -= 14f;
                    if (canBack && LinkButton(new Rect(cx, ry, s_backW, LinkH), Loc.T("‹ Back"))) res.Back = true;
                }
                if (i.ManualUrl != null)
                {
                    right -= s_manualW;
                    if (LinkButton(new Rect(right, ry, s_manualW, LinkH), Loc.T("Open manual"))) Application.OpenURL(i.ManualUrl);
                }
            }
            return popup ? FooterH : LinkH + 10f;
        }

        // One keyboard-focusable button: mouse, Enter and Space all click it, and Tab reaches it in layout order.
        static bool Click(Rect r, out bool hover, out bool focus)
        {
            int id = GUIUtility.GetControlID(ButtonHint, FocusType.Keyboard, r);
            var e = Event.current;
            hover = r.Contains(e.mousePosition);
            focus = GUIUtility.keyboardControl == id;
            bool click = false;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && hover)
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        click = hover;
                    }
                    break;
                case EventType.KeyDown:
                    if (focus && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space))
                    {
                        e.Use();
                        click = true;
                    }
                    break;
                case EventType.Repaint:
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                    break;
            }
            return click;
        }

        static void FocusRing(Rect r, float radius)
        {
            StudioGUI.DrawRoundedBorder(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), StudioGUI.CardAccent, radius + 1f, 1f);
        }

        static bool PrimaryButton(Rect r, string label)
        {
            bool click = Click(r, out var hover, out var focus);
            if (Event.current.type == EventType.Repaint)
            {
                StudioGUI.DrawRoundedBox(r, hover ? StudioGUI.CardTryFill : StudioGUI.CardInset, StudioGUI.CardAccent, 4f);
                GUI.Label(r, label, s_button);
                if (focus && GUIUtility.hotControl == 0) FocusRing(r, 4f);
            }
            return click;
        }

        static bool LinkButton(Rect r, string label)
        {
            bool click = Click(r, out var hover, out var focus);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(r, label, s_link);
                if (hover) EditorGUI.DrawRect(new Rect(r.x, r.yMax - 4f, r.width, 1f), StudioGUI.CardAccent);
                if (focus && GUIUtility.hotControl == 0) FocusRing(r, 2f);
            }
            return click;
        }

        static bool ChipButton(Rect r, Chip c)
        {
            bool click = Click(r, out var hover, out var focus);
            if (Event.current.type == EventType.Repaint)
            {
                var state = Learn.StateOf(c.Id);
                StudioGUI.DrawRoundedBox(r, hover ? StudioGUI.CardTryFill : StudioGUI.CardInset, StudioGUI.CardBorder, ChipH * 0.5f);
                StudioGUI.DrawStateGlyph(new Rect(r.x + ChipPad, r.y + (r.height - ChipDot) * 0.5f, ChipDot, ChipDot), state, StateColour(state, c.Tier));
                GUI.Label(new Rect(r.x + ChipPad + ChipDot + 4f, r.y, r.width - ChipPad * 2 - ChipDot - 4f, r.height), c.Content, s_chip);
                if (focus && GUIUtility.hotControl == 0) FocusRing(r, ChipH * 0.5f);
            }
            return click;
        }

        static bool CloseButton(Rect r)
        {
            bool click = Click(r, out var hover, out var focus);
            if (Event.current.type == EventType.Repaint)
            {
                if (hover) StudioGUI.DrawRounded(r, StudioGUI.CardTryFill, 4f);
                GUI.Label(r, "×", s_close);
                if (focus && GUIUtility.hotControl == 0) FocusRing(r, 4f);
            }
            return click;
        }

        // The "more" menu holds "Mark as not done", the undo of the I've got it button.
        static void MoreButton(Rect r, string conceptId)
        {
            bool click = Click(r, out var hover, out var focus);
            if (Event.current.type == EventType.Repaint)
            {
                if (hover) StudioGUI.DrawRounded(r, StudioGUI.CardTryFill, 4f);
                var dot = StudioGUI.CardTextDim;
                for (int k = -1; k <= 1; k++) StudioGUI.DrawRounded(new Rect(r.center.x - 1.5f, r.center.y - 1.5f + k * 4f, 3f, 3f), dot, 1.5f);
                if (focus && GUIUtility.hotControl == 0) FocusRing(r, 4f);
            }
            if (!click) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Loc.T("Mark as not done")), false, () => Learn.Unmark(conceptId));
            menu.DropDown(r);
        }

        static void Pill(Rect r, Learn.State state, int tier)
        {
            if (Event.current.type != EventType.Repaint) return;
            var colour = StateColour(state, tier);
            StudioGUI.DrawRounded(r, new Color(colour.r, colour.g, colour.b, 0.16f), PillH * 0.5f);
            StudioGUI.DrawStateGlyph(new Rect(r.x + 8f, r.y + 3f, 12f, 12f), state, colour);
            GUI.Label(new Rect(r.x + 24f, r.y, r.width - 24f, r.height), state == Learn.State.Done ? Loc.T("Done") : state == Learn.State.Read ? Loc.T("Read") : Loc.T("New"), s_pill);
        }
    }
}
