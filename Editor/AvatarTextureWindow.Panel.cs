using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public partial class AvatarTextureWindow
    {
        static readonly HashSet<string> CommonLayerFields = new HashSet<string>
            { "id", "name", "enabled", "expanded", "groupId", "groupName", "opacity", "blend", "maskMode", "islands", "invertMask", "strokes",
              "clipToBelow", "hasPaintMask", "paintMaskStartsHidden", "maskStrokes", "chR", "chG", "chB", "chA" };

        [SerializeField] bool _openPickPart = true, _openTexturePicker = true, _openSavedEditPicker, _openEditedCopy = true;
        [SerializeField] bool _openSelectionTools = true, _openSelectionScene = true, _openUvTiles;
        [SerializeField] bool _openPaintTools = true, _openMaskPaintingOptions = true, _openLayerMask = true;
        [SerializeField] bool _openSaveResult = true, _openSaveReview = true, _openSaveOutput = true;
        [SerializeField] bool _openBrush = true, _openLayers = true, _openColour = true, _openPaintOptions, _openLayerSettings;

        static readonly string[] FavouriteBrushes = { "Soft round", "Hard round", "Airbrush", "Pencil", "Bristle" };

        static readonly List<(BrushPreset preset, bool user)> FavouriteBrushPresets = BrushLibrary.BuiltIn
            .Where(p => FavouriteBrushes.Contains(p.name)).Select(p => (p, false)).ToList();

        static string NoSubmenu(string s) => s.Replace("/", "∕");

        static string BrushLabel(string name) => name switch
        {
            "Soft round" => Loc.T("Soft round"),
            "Hard round" => Loc.T("Hard round"),
            "Pencil" => Loc.T("Pencil"),
            "Airbrush" => Loc.T("Airbrush"),
            "Marker" => Loc.T("Marker"),
            "Calligraphy" => Loc.T("Calligraphy"),
            "Bristle" => Loc.T("Bristle"),
            "Spray" => Loc.T("Spray"),
            "Chalk" => Loc.T("Chalk"),
            "Splatter" => Loc.T("Splatter"),
            "Smooth blend" => Loc.T("Smooth blend"),
            "Basic hard" => Loc.T("Basic hard"),
            "Basic soft" => Loc.T("Basic soft"),
            "Soft airbrush" => Loc.T("Soft airbrush"),
            "Square" => Loc.T("Square"),
            "Dots" => Loc.T("Dots"),
            "Dry brush" => Loc.T("Dry brush"),
            "Sponge chalk" => Loc.T("Sponge chalk"),
            "Textured spray" => Loc.T("Textured spray"),
            "Grunge" => Loc.T("Grunge"),
            "Ink splatter" => Loc.T("Ink splatter"),
            "Scratches" => Loc.T("Scratches"),
            "Chips" => Loc.T("Chips"),
            "Cracks" => Loc.T("Cracks"),
            "Drips" => Loc.T("Drips"),
            "Stars" => Loc.T("Stars"),
            "Stitches" => Loc.T("Stitches"),
            "Rivets" => Loc.T("Rivets"),
            "Fur" => Loc.T("Fur"),
            _ => name,
        };

        static string LayerTypeLabel(string label) => label switch
        {
            "Paint (brush strokes)" => Loc.T("Paint (brush strokes)"),
            "Gradient Map (recolour, keeps shading)" => Loc.T("Gradient Map (recolour, keeps shading)"),
            "Colour Match (take another material's colours, keeps detail)" => Loc.T("Colour Match (take another material's colours, keeps detail)"),
            "Hue / Saturation / Lightness" => Loc.T("Hue / Saturation / Lightness"),
            "Recolour part (hue on some pieces, like 2 · Recolour › One piece at a time)" => Loc.T("Recolour part (hue on some pieces, like 2 · Recolour › Pieces)"),
            "Ombré Gradient (top → bottom)" => Loc.T("Ombré Gradient (top → bottom)"),
            "Gradient (line, circle or ellipse; drag it with the Gradient tool)" => Loc.T("Gradient (line, circle or ellipse; drag it with the Gradient tool)"),
            "Image ∕ sticker (place a picture on the texture)" => Loc.T("Image ∕ sticker (place a picture on the texture)"),
            "Solid Colour (tint)" => Loc.T("Solid Colour (tint)"),
            "Levels" => Loc.T("Levels"),
            "Correction/Brightness ∕ Contrast" => Loc.T("Brightness ∕ Contrast"),
            "Correction/Colour Balance" => Loc.T("Colour Balance"),
            "Correction/Tone Curve" => Loc.T("Tone Curve"),
            "Correction/Posterization" => Loc.T("Posterization"),
            "Correction/Invert colours" => Loc.T("Invert colours"),
            "Correction/Binarization (threshold)" => Loc.T("Binarization (threshold)"),
            _ => label,
        };

        static string LayerPresetLabel(string path) => path switch
        {
            "Colour/Recolour (keeps shading)" => Loc.T("Colour") + "/" + Loc.T("Recolour (keeps shading)"),
            "Colour/Desaturate" => Loc.T("Colour") + "/" + Loc.T("Desaturate"),
            "Colour/Pastel" => Loc.T("Colour") + "/" + Loc.T("Pastel"),
            "Colour/Vivid" => Loc.T("Colour") + "/" + Loc.T("Vivid"),
            "Colour/Warm tint" => Loc.T("Colour") + "/" + Loc.T("Warm tint"),
            "Colour/Cool tint" => Loc.T("Colour") + "/" + Loc.T("Cool tint"),
            "Colour/Hue shift +60°" => Loc.T("Colour") + "/" + Loc.T("Hue shift +60°"),
            "Tone/Brighten" => Loc.T("Tone") + "/" + Loc.T("Brighten"),
            "Tone/Darken" => Loc.T("Tone") + "/" + Loc.T("Darken"),
            "Tone/More contrast" => Loc.T("Tone") + "/" + Loc.T("More contrast"),
            "Tone/Faded (lift blacks)" => Loc.T("Tone") + "/" + Loc.T("Faded (lift blacks)"),
            "Mask textures/Invert" => Loc.T("Mask textures") + "/" + Loc.T("Invert"),
            "Mask textures/Stronger" => Loc.T("Mask textures") + "/" + Loc.T("Stronger"),
            "Mask textures/Weaker" => Loc.T("Mask textures") + "/" + Loc.T("Weaker"),
            "Mask textures/Softer edges" => Loc.T("Mask textures") + "/" + Loc.T("Softer edges"),
            "Hair/Ombré tips" => Loc.T("Hair") + "/" + Loc.T("Ombré tips"),
            "Hair/Dark roots" => Loc.T("Hair") + "/" + Loc.T("Dark roots"),
            "Hair/Bleached tips" => Loc.T("Hair") + "/" + Loc.T("Bleached tips"),
            "Hair/Two-tone (split colour)" => Loc.T("Hair") + "/" + Loc.T("Two-tone (split colour)"),
            "Hair/Galaxy" => Loc.T("Hair") + "/" + Loc.T("Galaxy"),
            "Hair/Pastel recolour" => Loc.T("Hair") + "/" + Loc.T("Pastel recolour"),
            _ => path,
        };

        static readonly (Tab tab, string icon, string label, string tip)[] TabDefs =
        {
            (Tab.Target, "Settings", "Pick", "1 · Choose the part and the texture to edit"),
            (Tab.Colour, "Grid.FillTool", "Recolour", "2 · Quick recolour of whole materials (shadows, rim, outline… follow along)"),
            (Tab.Select, "Grid.BoxTool", "Limit", "3 · Optional: pick pieces to limit edits to (S)"),
            (Tab.Paint, "Grid.PaintTool", "Paint", "4 · Paint with brushes (B) and edit the layers"),
            (Tab.Save, "SaveAs", "Save", "5 · Save the result as a new PNG"),
        };

        static string TabTip(Tab tab, string tip) => tab switch
        {
            Tab.Target => Loc.T("1 · Choose the part and the texture to edit"),
            Tab.Colour => Loc.T("2 · Quick recolour of whole materials (shadows, rim, outline… follow along)"),
            Tab.Select => Loc.T("3 · Optional: pick pieces to limit edits to (S)"),
            Tab.Paint => Loc.T("4 · Paint with brushes (B) and edit the layers"),
            Tab.Save => Loc.T("5 · Save the result as a new PNG"),
            _ => tip,
        };

        static readonly string[] TabIds = TabDefs.Select(d => "tab." + d.tab.ToString().ToLowerInvariant()).ToArray();

        void DrawPanelArea(Rect panel)
        {
            if (_tab == Tab.Layers)
            {
                _tab = TabAvailable(Tab.Paint, out _) ? Tab.Paint : Tab.Target;
                if (_tab == Tab.Paint) SetTool(Tool.Paint);
            }
            const float tabsH = 36f, contextH = 36f, footerH = 32f;
            bool footer = panel.height >= 160f && NextStep(out _, out _) != null;
            float stripH = NewToUnityDismissed ? 0f : NewToUnityStripHeight(panel.width);
            float updH = UpdateBannerHeight(panel.width);
            bool pinNotices = panel.height - tabsH - contextH - (footer ? footerH : 0f) - stripH - updH >= 100f;
            var tabsR = new Rect(panel.x, panel.y + (pinNotices ? stripH + updH : 0f), panel.width, tabsH);
            var ctxR = new Rect(panel.x, tabsR.yMax, panel.width, contextH);
            var footR = new Rect(panel.x, panel.yMax - footerH, panel.width, footerH);
            var bodyR = new Rect(panel.x, ctxR.yMax, panel.width, Mathf.Max(0f, (footer ? footR.y : panel.yMax) - ctxR.yMax));

            if (pinNotices)
            {
                if (stripH > 0f) DrawNewToUnityStrip(new Rect(panel.x, panel.y, panel.width, stripH));
                if (updH > 0f) DrawUpdateBanner(new Rect(panel.x, panel.y + stripH, panel.width, updH));
            }
            DrawTabBar(tabsR);
            DrawContextStrip(ctxR);

            GUILayout.BeginArea(bodyR);
            _panelScroll = EditorGUILayout.BeginScrollView(_panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            _panelContentWidth = Mathf.Max(120f, bodyR.width - 14f);
            _learnViewportHeight = bodyR.height;
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(_panelContentWidth * 0.36f, 64f, 110f);
            using (new EditorGUILayout.VerticalScope(PanelPadStyle, GUILayout.Width(_panelContentWidth)))
            {
                if (_revealLearnControl != null && LearnPulse.Active) LearnPulse.PanelControlDrawn = RevealLearnControl;
                try
                {
                    if (!TabAvailable(_tab, out _)) _tab = Tab.Target;
                    ValidateMatchCardsReveal();
                    if (_tab != Tab.Select || (!_needsRebuild && _layerLimitTargetId != null && PendingLayerLimit == null))
                    {
                        _layerLimitTargetId = null;
                        _layerLimitUsesFaces = false;
                    }
                    switch (_tab)
                    {
                        case Tab.Target: DrawTargetTab(); break;
                        case Tab.Select: DrawSelectTab(); break;
                        case Tab.Paint: DrawPaintTab(); break;
                        case Tab.Layers: DrawLayersTab(); break;
                        case Tab.Save: DrawSaveTab(); break;
                        case Tab.Colour: DrawColourTab(); break;
                    }
                    if (!pinNotices)
                    {
                        float width = _panelContentWidth - PanelPadStyle.padding.horizontal;
                        if (stripH > 0f) DrawNewToUnityStrip(GUILayoutUtility.GetRect(width, NewToUnityStripHeight(width)));
                        if (updH > 0f) DrawUpdateBanner(GUILayoutUtility.GetRect(width, UpdateBannerHeight(width)));
                    }
                }
                finally { LearnPulse.PanelControlDrawn = null; }
            }
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            if (footer) DrawFooter(footR);
        }

        void DrawTabBar(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.TabBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }
            float w = r.width / TabDefs.Length;
            for (int i = 0; i < TabDefs.Length; i++)
            {
                var (tab, icon, tabLabel, tip) = TabDefs[i];
                string label = StudioGUI.Label(TabIds[i], tabLabel);
                var tr =new Rect(r.x + i * w, r.y, w, r.height);
                bool active = _tab == tab;
                bool available = TabAvailable(tab, out var why);

                if (Event.current.type == EventType.Repaint)
                {
                    if (active)
                    {
                        EditorGUI.DrawRect(new Rect(tr.x, tr.y, tr.width, tr.height - 1), StudioGUI.TabActiveBg);
                        EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 3, tr.width, 3), StudioGUI.Accent);
                    }
                    else if (tr.Contains(Event.current.mousePosition) && available)
                        EditorGUI.DrawRect(new Rect(tr.x, tr.y, tr.width, tr.height - 1), new Color(1, 1, 1, 0.03f));
                }

                var old = GUI.color;
                if (!available) GUI.color = new Color(1, 1, 1, 0.35f);
                if (Event.current.type == EventType.Repaint)
                {
                    var iconRect = new Rect(tr.center.x - 8, tr.y + 2, 16, 16);
                    var glyph = tab switch { Tab.Colour => SelectGlyph.Recolour, Tab.Paint => SelectGlyph.Paint,
                        Tab.Select => SelectGlyph.Click, _ => (SelectGlyph?)null };
                    if (!glyph.HasValue || !StudioGUI.DrawToolGlyph(iconRect, glyph.Value, active ? StudioGUI.CardAccent : StudioGUI.CardText))
                    {
                        var image = StudioGUI.Icon(icon, "").image;
                        if (image != null) GUI.DrawTexture(iconRect, image, ScaleMode.ScaleToFit);
                    }
                }
                bool narrowTab = tr.width < 58f;
                string shortLabel = narrowTab && label.Length > 2 && char.IsDigit(label[0]) && label[1] == ' ' ? label.Substring(2) : label;
                GUI.Label(new Rect(tr.x + 1, tr.y + 18, tr.width - 2, 14), label.Length > 0 && (char.IsDigit(label[0]) || narrowTab) ? shortLabel : $"{i + 1} {label}", StudioGUI.TabLabel);
                GUI.color = old;

                string badge = TabBadge(tab, out var badgeColor);
                if (badge != null)
                {
                    var oc = GUI.contentColor;
                    GUI.contentColor = badgeColor;
                    GUI.Label(new Rect(tr.xMax - 22, tr.y + 2, 20, 14), badge, StudioGUI.MiniRight);
                    GUI.contentColor = oc;
                }

                if (GUI.Button(tr, new GUIContent("", available ? TabTip(tab, tip) : $"{TabTip(tab, tip)}\n\n{why}"), GUIStyle.none)) SetTab(tab);
                EditorGUIUtility.AddCursorRect(tr, MouseCursor.Link);
            }
        }

        string TabBadge(Tab t, out Color color)
        {
            color = StudioGUI.TextDim;
            switch (t)
            {
                case Tab.Target:
                    if (_comp != null) { color = StudioGUI.Good; return "✓"; }
                    return null;
                case Tab.Colour:
                    if (RecolourChanged || (ByPart && Parts.Count > 0)) { color = StudioGUI.Good; return "●"; }
                    return null;
                case Tab.Select:
                    return _selection.Count > 0 ? _selection.Count.ToString() : null;
                case Tab.Paint:
                {
                    int strokes = _recipe.layers.Count;
                    return strokes > 0 ? strokes.ToString() : null;
                }
                case Tab.Layers:
                    return _recipe.layers.Count > 0 ? _recipe.layers.Count.ToString() : null;
                case Tab.Save:
                    switch (CurrentSaveState)
                    {
                        case SaveState.NotSaved: color = StudioGUI.Accent; return "●";
                        case SaveState.Saved: color = StudioGUI.Good; return "✓";
                        default: return null;
                    }
            }
            return null;
        }

        void DrawContextStrip(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.HeaderBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }
            float right = r.xMax - 8;
            if (CurrentSaveState == SaveState.NotSaved)
            {
                var pill = new GUIContent(r.width < 330f ? "●" : Loc.F("● Not saved yet ({0})", _recipe.layers.Count), Loc.T("Your edits aren't in a file yet. Save them in 5 · Save (or Unity will ask before closing)."));
                float pw = EditorStyles.miniLabel.CalcSize(pill).x + 6;
                var pr = new Rect(right - pw, r.y + 10, pw, 16);
                var oc = GUI.contentColor;
                GUI.contentColor = StudioGUI.Accent;
                if (GUI.Button(pr, pill, EditorStyles.miniLabel)) SetTab(Tab.Save);
                GUI.contentColor = oc;
                right = pr.x - 4;
            }

            var slot = CurrentSlot;
            if (slot != null && slot.texture == null) _needsRebuild = true;
            if (_comp == null || slot == null || slot.texture == null)
            {
                string label = WaitingForPreview ? Loc.T("Preview queued…") : _renderer == null
                    ? Loc.T("Nothing to edit yet. Start in 1 · Pick.") : Loc.T("Pick a texture in 1 · Pick.");
                GUI.Label(new Rect(r.x + 8, r.y, right - r.x - 8, r.height),
                    new GUIContent(label, WaitingForPreview ? PreviewWaitText : null), StudioGUI.Hint);
                return;
            }

            var thumb = new Rect(r.x + 6, r.y + 5, 26, 26);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(thumb, StudioGUI.CanvasBg);
                GUI.DrawTexture(thumb, slot.texture, ScaleMode.ScaleToFit);
            }
            float tx = thumb.xMax + 6;
            GUI.Label(new Rect(tx, r.y + 2, right - tx, 18), new GUIContent(slot.texture.name, AssetDatabase.GetAssetPath(slot.texture)), StudioGUI.ContextTitle);
            int nParts = _uv?.parts.Count ?? 0;
            string layersText = Loc.F("{0} layer{1}", _recipe.layers.Count, _recipe.layers.Count == 1 ? "" : "s");
            string sub = $"{_material?.name} · {Loc.F("{0} part{1}", nParts, nParts == 1 ? "" : "s")}" +
                         (_selection.Count > 0 ? " · " + Loc.F("{0} piece{1} selected", _selection.Count, _selection.Count == 1 ? "" : "s") : "") +
                         " · " + layersText;
            string shown = r.width < 330f
                ? (_selection.Count > 0 ? Loc.F("{0} piece{1}", _selection.Count, _selection.Count == 1 ? "" : "s") + " · " : "") + layersText
                : sub;
            GUI.Label(new Rect(tx, r.y + 17, right - tx, 16), new GUIContent(shown, sub), StudioGUI.Hint);
            if (GUI.Button(new Rect(r.x, r.y, tx + 60 - r.x, r.height), new GUIContent("", Loc.T("Change part or texture (1 · Pick)")), GUIStyle.none)) SetTab(Tab.Target);
        }

        [NonSerialized] bool _showShortcutsNext;
        static string ModeSwitchTip => Loc.T("Simple shows the everyday controls. Advanced shows every setting.");

        GenericMenu BuildSettingsMenu()
        {
            var menu = new GenericMenu();
            AddLanguageItems(menu);
            menu.AddItem(new GUIContent(Loc.T("Advanced mode"), ModeSwitchTip), Advanced, () => SetAdvanced(!Advanced));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Keyboard shortcuts…")), false, () => { _showShortcutsNext = true; Repaint(); });
            int counted = Glossary.CountedCount;
            string learning = counted > 0 ? Loc.F("Glossary and skill tree…  {0}/{1}", Learn.DoneCount(), counted).Replace('/', '⁄') : Loc.T("Glossary…");
            menu.AddItem(new GUIContent(learning), false, () => GlossaryWindow.Open());
            var home = OnlineGuide.Get("home");
            string homeUrl = OnlineGuide.Url(home);
            menu.AddItem(new GUIContent(OnlineGuide.PageLabel(home), OnlineGuide.Hover(home, homeUrl)), false, () => OnlineGuide.Open(homeUrl));
            OnlineGuide.AddMenu(menu, _tab.ToString());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Connect an AI helper…")), false, AgentWindow.Open);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Check for updates automatically"), Loc.T("Once a day, asks GitHub only for the latest version number")), UpdateCheck.Enabled, () => { UpdateCheck.SetEnabled(!UpdateCheck.Enabled); if (UpdateCheck.Enabled) UpdateCheck.MaybeCheckAutomatically(); Repaint(); });
            menu.AddItem(new GUIContent(Loc.T("Check for updates now")), false, UpdateCheck.CheckNow);
            menu.AddItem(new GUIContent(Loc.T("Reload Avistry"), Loc.T("Refresh the tools and texture preview while keeping your layers.")), false, ReloadAvistry);
            menu.AddItem(new GUIContent(Loc.T("Review support report…")), false, () => CopyDiagnosticsFlow());
            return menu;
        }

        string NextStep(out Tab next, out string note)
        {
            next = _tab;
            note = null;
            bool hasLayers = _recipe.layers.Count > 0;
            switch (_tab)
            {
                case Tab.Target:
                    if (_material == null || _statusFix == StatusFix.RetryPreview) return null;
                    next = Tab.Colour;
                    note = Loc.T("Quick recolour, or go on to edit the texture.");
                    return NextTabLabel(next);
                case Tab.Colour:
                    if (_comp == null) return null;
                    if (ByPart && Parts.Count > 0)
                    {
                        next = Tab.Save;
                        note = Loc.T("Save the coloured parts as a new PNG.");
                        return NextTabLabel(next);
                    }
                    next = Tab.Select;
                    note = RecolourChanged ? Loc.F("Press ‘{0}’ to finish changing the material colours.", Loc.T("Keep changes")) :
                        Loc.T("Next, edit the texture itself.");
                    return NextTabLabel(next);
                case Tab.Select:
                    if (!TabAvailable(Tab.Paint, out _)) return null;
                    next = Tab.Paint;
                    note = HasFaceSelection ? Loc.T("New layers start with these faces.") : _selection.Count > 0
                        ? Loc.T("New layers start with these pieces.")
                        : Loc.T("New layers start on the whole texture.");
                    return NextTabLabel(next);
                case Tab.Paint:
                    if (!hasLayers) return null;
                    next = Tab.Save;
                    note = Loc.T("Happy with it? Save it as a PNG.");
                    return NextTabLabel(next);
                case Tab.Layers:
                    if (!hasLayers) return null;
                    next = Tab.Save;
                    note = Loc.T("Done editing? Save it as a PNG.");
                    return NextTabLabel(next);
            }
            return null;
        }

        static string NextTabLabel(Tab tab)
        {
            int index = Array.FindIndex(TabDefs, entry => entry.tab == tab);
            return Loc.F("Next: {0}", StudioGUI.Label(TabIds[index], TabDefs[index].label)) + " →";
        }

        void DrawFooter(Rect r)
        {
            string label = NextStep(out var next, out var note);
            if (label == null) return;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.HeaderBg);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), StudioGUI.HeaderLine);
            }
            float bw = Mathf.Min(130f, r.width * 0.45f);
            var br = new Rect(r.xMax - bw - 8, r.y + 4, bw, 24);
            GUI.Label(new Rect(r.x + 8, r.y, br.x - r.x - 12, r.height), note, StudioGUI.Hint);
            if (GUI.Button(br, label)) SetTab(next);
        }

        void RevealPanelGroup(string control)
        {
            switch (control)
            {
                case "pick.partList":
                case "pick.partButton": _openPickPart = true; break;
                case "pick.surface":
                case "pick.image":
                case "pick.fixImport": _openTexturePicker = true; break;
                case "limit.everything": _openSelectionTools = true; break;
                case "limit.clickAvatar": _openSelectionScene = true; break;
                case "paint.tools": _openPaintTools = true; break;
                case "paint.smoothJoins":
                case "paint.onAvatar":
                    if (MaskEditing) _openMaskPaintingOptions = true;
                    else _openPaintOptions = true;
                    break;
                case "layers.hideMask": _openLayerMask = true; break;
                case "save.save": _openSaveOutput = true; break;
                case "save.saveLater": _openSaveDrafts = true; break;
                case "drafts.browse":
                    if (_tab == Tab.Save && CurrentSaveState == SaveState.Saved && _recipe.layers.Count == 0) _openSaveResult = true;
                    else if (_tab == Tab.Save) _openSaveDrafts = true;
                    else if (_tab == Tab.Target) _openSavedEditPicker = true;
                    break;
                case "save.exportStylePack":
                case "save.openStylePack": _openStylePack = true; break;
                case "colour.scope":
                case "colour.parts": _openRecolourScope = true; break;
                case "colour.copyFrom":
                case "colour.matchGoPick": _openRecolourMatch = true; break;
                case "colour.swatches":
                case "colour.cover":
                case "colour.hue":
                case "colour.saturation":
                case "colour.brightness":
                case "colour.shiftTo":
                case "colour.matchBrightness":
                case "colour.tintColour":
                case "colour.tintStrength":
                case "colour.mainTint": _openRecolourControls = true; break;
                case "colour.changes": _openColourChanges = true; break;
                case "colour.materialScope": _rcOthersOpen = true; break;
                case "recolour.poiyomiAnimated": _openQuickMaterial = true; break;
                case "recolour.poiyomiClearAnimation":
                    _openQuickMaterial = true;
                    _openPoiyomiAnimatedSettings = true;
                    break;
                case "save.continueEditing":
                case "save.showPng": _openSaveResult = true; break;
                case "save.goToPaint":
                case "save.reviewLayers": _openSaveReview = true; break;
            }
        }

        void DrawTargetTab()
        {
            var suggested = SuggestedRenderer;
            if (_renderer == null)
            {
                if (StudioGUI.BeginFoldGroup(ref _openPickPart, Loc.T("Which part of your avatar?"), infoId: "hierarchy"))
                    DrawFirstPartPicker(suggested);
                StudioGUI.EndGroup();
                DrawOpenSavedEditGroup();
                return;
            }

            if (StudioGUI.BeginFoldGroup(ref _openPickPart, Loc.T("Part"), _renderer.name, infoId: "mesh"))
                DrawCurrentPartPicker(suggested);
            StudioGUI.EndGroup();

            if (StudioGUI.BeginFoldGroup(ref _openTexturePicker, Loc.T("Texture to edit"), CurrentSlot?.displayName, infoId: "texture"))
                DrawTexturePicker();
            StudioGUI.EndGroup();

            if (CurrentSlot == null || _comp == null)
            {
                DrawOpenSavedEditGroup();
                if (Advanced) DrawAdvancedCopy();
                return;
            }

            if (_editThatMadeTexture != null && _editThatMadeTexture.sourceTexture != null)
            {
                if (StudioGUI.BeginFoldGroup(ref _openEditedCopy, Loc.T("Edited copy"), _editThatMadeTexture.sourceTexture.name, infoId: "texture"))
                    DrawEditedCopy();
                StudioGUI.EndGroup();
            }
            DrawOpenSavedEditGroup();

            if (!Advanced) return;
            DrawMaskBuilderGroup();
            DrawDecalsGroup();
            DrawMapsGroup();
            DrawAdvancedCopy();
            DrawAuditGroup();

            StudioGUI.Section(ref _foldOutputOptions, Loc.T("Quality"));
            StudioGUI.Count("quality.header", GUILayoutUtility.GetLastRect());
            if (_foldOutputOptions)
            {
                int wi = Array.IndexOf(WorkSizes, _workSize);
                int nwi = PickChoice(new GUIContent(Loc.T("Preview size"), Loc.T("Maximum preview resolution. Smaller source textures keep their full size. Saving uses the full source resolution.")),
                    wi, WorkSizes.Select(s => new GUIContent(s + " px")).ToArray());
                StudioGUI.Count("quality.previewSize", GUILayoutUtility.GetLastRect());
                if (WorkSizes[nwi] != _workSize) SetPreviewSize(WorkSizes[nwi]);
                if (_comp != null)
                {
                    GUILayout.Label(Loc.F("Preview {0} × {1} px · Source {2} × {3} px", _comp.Width, _comp.Height,
                        _comp.SourceWidth, _comp.SourceHeight), StudioGUI.Hint);
                    StudioGUI.Count("quality.previewDimensions", GUILayoutUtility.GetLastRect());
                }
            }
        }

        void SetPreviewSize(int size)
        {
            string action = Loc.T("Change preview size");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RecordObject(this, action);
            _workSize = size;
            if (_comp == null ? _renderer != null : PreviewSizeChanged()) _needsRebuild = true;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        bool PreviewSizeChanged()
        {
            if (_comp == null) return false;
            int sourceSize = Mathf.Max(_comp.SourceWidth, _comp.SourceHeight);
            int previewSize = _workSize > 0 ? Mathf.Min(_workSize, sourceSize) : sourceSize;
            return Mathf.Max(_comp.Width, _comp.Height) != previewSize;
        }

        int PickChoice(GUIContent label, int current, GUIContent[] options)
        {
            float previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(PanelContentWidth * 0.4f, EditorStyles.label.CalcSize(label).x + 10f);
            try
            {
                int selected = Mathf.Clamp(current, 0, options.Length - 1);
                return SteppedChoice(label, selected, Enumerable.Range(0, options.Length).ToArray(),
                    (rect, i) => EditorGUI.Popup(rect, i, options));
            }
            finally { EditorGUIUtility.labelWidth = previousWidth; }
        }

        void DrawFirstPartPicker(Renderer suggested)
        {
            GUILayout.Label(Loc.T("Click the part you want to change (body, shirt, hair) in the Hierarchy window, then press the button. Any part of the avatar works. Or drag it onto the texture area on the left, or choose it from the list."), StudioGUI.Hint);
            GUILayout.Space(4);
            StudioGUI.Count("pick.partButton");
            if (suggested != null)
            {
                var sugRoot = TargetResolver.AvatarRoot(suggested);
                string sugLabel = sugRoot != null && sugRoot != suggested.transform ? $"{sugRoot.name} / {suggested.name}" : suggested.name;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (StudioGUI.PrimaryButton(new GUIContent(Loc.F("Edit {0}", sugLabel), sugRoot != null ? Loc.F("Use the part selected in the Hierarchy: ‘{0}’ on the avatar ‘{1}’. Check the avatar name is the one you mean.", suggested.name, sugRoot.name) : Loc.F("Use the part selected in the Hierarchy: ‘{0}’. Check the avatar name is the one you mean.", suggested.name)), StudioGUI.InfoBlue))
                        SetRendererChecked(suggested);
                    StudioGUI.Info("hierarchy");
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                        GUILayout.Button(new GUIContent(Loc.T("Select a part in the Hierarchy first")), GUILayout.Height(28));
                    StudioGUI.Info("hierarchy");
                }
            }
            GUILayout.Space(2);
            StudioGUI.Count("pick.pickHere");
            Renderer picked;
            using (new EditorGUILayout.HorizontalScope())
            {
                picked = (Renderer)EditorGUILayout.ObjectField(new GUIContent(Loc.T("Or pick here")), null, typeof(Renderer), true);
                StudioGUI.Info("mesh");
            }
            if (picked != null) SetRendererChecked(ResolveRenderer(picked) ?? picked);
            DrawPartListButton();
        }

        void DrawCurrentPartPicker(Renderer suggested)
        {
            StudioGUI.Count("pick.part");
            Renderer r;
            using (new EditorGUILayout.HorizontalScope())
            {
                r = (Renderer)EditorGUILayout.ObjectField(_renderer, typeof(Renderer), true);
                StudioGUI.Info("mesh");
            }
            if (r != _renderer && r != null) SetRendererChecked(r);
            if (suggested != null) StudioGUI.Count("pick.switchPart");
            if (suggested != null && GUILayout.Button(new GUIContent(Loc.F("Switch to ‘{0}’", suggested.name), Loc.F("Edit ‘{0}’, the part selected in the Hierarchy", suggested.name)), EditorStyles.miniButton, GUILayout.MinWidth(10)))
                SetRendererChecked(suggested);
            DrawPartListButton();
        }

        void DrawTexturePicker()
        {
            var mats = _renderer.sharedMaterials;
            if (mats.Length > 1)
            {
                var names = mats.Select((m, i) => new GUIContent(NoSubmenu($"{i}: {(m != null ? m.name : Loc.T("(empty)"))}"), m != null ? m.name : null)).ToArray();
                int mi;
                using (new EditorGUILayout.HorizontalScope())
                {
                    mi = PickChoice(new GUIContent(Loc.T("Material"), Loc.T("This part has several materials")), _materialIndex, names);
                    StudioGUI.Count("pick.surface", GUILayoutUtility.GetLastRect());
                    StudioGUI.Info("material");
                }
                if (mi != _materialIndex && ConfirmTargetChange("material"))
                {
                    Undo.RecordObject(this, "Change material");
                    RememberSelectionBeforeSwitch();
                    _materialIndex = mi; _slotIndex = 0; _slotChosenByUser = false; _selection.Clear(); ClearFaceSelection(); _needsRebuild = true;
                    Learn.Did(LearnEvents.MaterialSwitched);
                }
            }
            else
            {
                var material = WaitingForPreview && mats.Length == 1 ? mats[0] : _material;
                EditorGUILayout.LabelField(Loc.T("Material"), material != null ? material.name : Loc.T("(none)"));
            }

            if (_slots.Count > 0)
            {
                var labels = _slots.Select(s => new GUIContent(NoSubmenu(s.displayName + (s.featureOff ? Loc.T("  (feature off)") : "") +
                    (!Advanced && (s.role == TextureRole.Mask || s.role == TextureRole.Normal) ? Loc.T(" (advanced)") : "") +
                    $"  ·  {(s.texture != null ? s.texture.name : Loc.T("(missing)"))}"), s.assetPath)).ToArray();
                int si;
                using (new EditorGUILayout.HorizontalScope())
                {
                    si = PickChoice(new GUIContent(Loc.T("Texture"), Loc.T("The textures this material uses. Colour textures are the usual choice.")), _slotIndex, labels);
                    StudioGUI.Count("pick.image", GUILayoutUtility.GetLastRect());
                    StudioGUI.Info("texture");
                }
                if (si != _slotIndex && ConfirmTargetChange("texture"))
                {
                    Undo.RecordObject(this, "Change texture");
                    ChooseSlot(si);
                    Learn.Did(LearnEvents.TexturePicked);
                }
            }

            var slot = CurrentSlot;
            if (slot == null || _comp == null)
            {
                if (!WaitingForPreview && _statusFix == StatusFix.PickAnotherMesh)
                {
                    EditorGUILayout.HelpBox(SlotStatusText(), MessageType.Warning);
                    StudioGUI.Count("pick.pickAnotherMesh");
                    if (GUILayout.Button(Msg.BtnPickMesh, EditorStyles.miniButton)) DrawPartListMenu();
                }
                else if (!WaitingForPreview && !string.IsNullOrEmpty(_status) && !CanStartUnityTexture) EditorGUILayout.HelpBox(_status, MessageType.Warning);
                if (!WaitingForPreview && _statusFix == StatusFix.RetryPreview)
                {
                    if (GUILayout.Button(Msg.BtnOpenConsole, EditorStyles.miniButton)) OpenConsole();
                    if (GUILayout.Button(Loc.T("Retry preview"), EditorStyles.miniButton)) RetryTexturePreview();
                }
                DrawStarterTexture();
                return;
            }

            string meshes = _uv != null ? string.Join(", ", _uv.parts.Select(p => p.renderer != null ? p.renderer.name : "?").Distinct()) : "";
            GUILayout.Label(new GUIContent(
                    $"{slot.texture.name} · " + Loc.F("{0}px · {1} part{2} · {3} pieces", _comp.SourceWidth, _uv?.parts.Count ?? 0, (_uv?.parts.Count ?? 0) == 1 ? "" : "s", _uv?.islandCount ?? 0),
                    slot.assetPath + "\n" + Loc.F("Used by: {0}\nShader properties: {1}\nUV set: {2}\n{3}", meshes, string.Join(", ", slot.properties), _uv?.uvChannel ?? 0, _comp.SourceNote)),
                StudioGUI.Hint);

            if (slot.role == TextureRole.Normal)
            {
                EditorGUILayout.HelpBox(
                    Loc.T("This image stores surface bumps. To paint, choose a colour or mask in the Texture list above."),
                    MessageType.Info);
            }
            else if (slot.role == TextureRole.Mask)
                StudioGUI.CheckRow(StudioGUI.Check.Note, slot.channelInfo != null
                    ? Loc.F("Mask texture: white = effect on, black = off ({0}). Paint white or black, or choose a mask template in Paint.", slot.channelInfo)
                    : Loc.T("Mask texture: white = effect on, black = off. Paint white or black, or choose a mask template in Paint."));
            else if (slot.channelInfo != null)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("Channels: {0}.", slot.channelInfo));
            if (slot.mixedRoles)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("The shader uses this texture both as colour and as a mask in different places."));
            if (slot.featureOff)
                NoteWithFix(StudioGUI.Check.Problem, Msg.M23FeatureSwitchedOff(), Msg.BtnSwitchOn, () => { if (SwitchFeatureOn(slot)) _needsRebuild = true; }, "pick.switchFeatureOn");
            foreach (var uvp in slot.uvProblems)
                StudioGUI.CheckRow(StudioGUI.Check.Note, uvp);

            var importIssues = VisibleImportIssues(slot, out int fixable);
            if (fixable > 0)
            {
                StudioGUI.Count("pick.fixAllImport");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(Loc.F("Fix {0} image setting{1}", fixable, (fixable == 1 ? "" : "s")), Loc.T("Changes this texture's import settings (affects every material that uses it)")), EditorStyles.miniButton))
                    {
                        foreach (var x in importIssues) x.fix?.Invoke();
                        Learn.Did(LearnEvents.ImportFixed);
                        _needsRebuild = true;
                        Toast(Loc.F("Updated the import settings of {0}.", slot.texture.name), MessageType.Info, 3f);
                    }
                    StudioGUI.Info("import-settings");
                }
                GUILayout.Label(Loc.T("These change the image's import settings; undo them in the Inspector."), StudioGUI.Hint);
            }
            foreach (var issue in importIssues)
            {
                StudioGUI.CheckRow(issue.problem ? StudioGUI.Check.Problem : StudioGUI.Check.Note, issue.text);
                if (issue.fix == null) continue;
                StudioGUI.Count("pick.fixImport");
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent(issue.fixLabel, Loc.T("Changes this texture's import settings (affects every material that uses it)")), EditorStyles.miniButton))
                    {
                        issue.fix();
                        Learn.Did(LearnEvents.ImportFixed);
                        _needsRebuild = true;
                        Toast(Loc.F("Updated the import settings of {0}.", slot.texture.name), MessageType.Info, 3f);
                    }
                    StudioGUI.Info("import-settings");
                }
            }
            if (_transparencyNote != null && slot.properties.Contains("_MainTex"))
                StudioGUI.CheckRow(StudioGUI.Check.Note, _transparencyNote);

            var leftOut = _otherMaterials.Where(m => !_alsoUsers.Any(u => u.material == m)).ToList();
            if (leftOut.Count > 0)
                NoteWithFix(StudioGUI.Check.Note, Msg.M24SharedTexture(leftOut.Count), Msg.BtnTickAll, TickAllMeshes, "pick.tickAll");
            DrawMeshList();
            if (MaterialWriter.IsPoiyomiLocked(_material))
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Locked (optimised) Poiyomi material: texture edits work; colour settings need unlocking (2 · Recolour does it for you)."));
        }

        void DrawEditedCopy()
        {
            GUILayout.Label(Loc.F("This texture was made by Avistry from ‘{0}’. New layers go on top of that earlier edit.", _editThatMadeTexture.sourceTexture.name), StudioGUI.CheckLabel);
            StudioGUI.Count("pick.editedCopy");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(Loc.T("Change the earlier edit instead"), Loc.T("Switch back to the original texture and reopen the layers that made this one")), GUILayout.Height(24)))
                    OpenSavedEdit(_editThatMadeTexture);
                StudioGUI.Info("asset");
            }
        }

        void DrawOpenSavedEditGroup()
        {
            if (!StudioGUI.BeginFoldGroup(ref _openSavedEditPicker, Loc.T("Continue a saved edit"), infoId: "asset"))
            {
                StudioGUI.EndGroup();
                return;
            }
            DrawBrowseDraftsButton();
            DrawOpenStylePack();
            using (new EditorGUILayout.HorizontalScope())
            {
                _recipeToLoad = (TextureRecipe)EditorGUILayout.ObjectField(new GUIContent("", Loc.T("Saved edits are _recipe or _edit files. New ones are in Assets/Avistry/Output or Assets/Avistry/Drafts.")),
                    _recipeToLoad, typeof(TextureRecipe), false);
                StudioGUI.Count("pick.reopenSave", GUILayoutUtility.GetLastRect());
                using (new EditorGUI.DisabledScope(_recipeToLoad == null || WaitingForPreview))
                    if (GUILayout.Button(Loc.T("Open"), GUILayout.Width(52)))
                    {
                        if (_comp == null) OpenSavedEditOnLoadedSource(_recipeToLoad);
                        else OpenSavedEdit(_recipeToLoad);
                    }
                StudioGUI.Info("asset");
            }
            GUILayout.Label(WaitingForPreview ? PreviewWaitText : _comp == null
                ? Loc.T("Open finds the part when there’s one match. You can also pick a part first.")
                : Loc.T("Reopens the layers of an earlier edit so you can keep changing them."), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        void DrawSelectTab()
        {
            if (_uv == null) { EditorGUILayout.HelpBox(Loc.T("This part has no UV data, so pieces can't be selected."), MessageType.Warning); return; }

            var limitTarget = PendingLayerLimit;
            bool faces = SelectionUsesFaces(limitTarget);
            int selected = SelectedElementCount;
            if (limitTarget != null)
                selected = faces ? (HasSelectionForLayerLimit(true) ? _selectedFaces.Count : 0) : _selection.Count(IsCurrentPiece);
            string count = Loc.F("{0} of {1}", selected, faces ? _uv.TriangleCount : _uv.islandCount);
            bool open = StudioGUI.BeginFoldGroup(ref _openSelectionTools, faces ? Loc.T("UV faces") : Loc.T("Pieces"), count, count, infoId: "island");
            Action handoff = null;
            if (open)
            {
                DrawPieceSelectionTools(faces);
                handoff = DrawSelectionHandoff(limitTarget);
            }
            StudioGUI.EndGroup();

            if (StudioGUI.BeginFoldGroup(ref _openSelectionScene, Loc.T("Scene view"), infoId: "scene-view"))
                DrawSceneSelectionOptions();
            StudioGUI.EndGroup();
            DrawTileList();
            handoff?.Invoke();
        }

        void DrawPieceSelectionTools(bool faces)
        {
            DrawSelectModeStrip(false);
            GUILayout.Label(SelectModeHint(), StudioGUI.Hint);
            DrawSelectModeOptions();
            GUILayout.Space(3f);
            DrawSelectOpSwitch();
            DrawSelectionSetActions(PanelContentWidth, false);
            if (faces) return;

            var grow = new GUIContent(Loc.T("Grow"), Loc.T("Add the pieces that touch the selection on the model."));
            var shrink = new GUIContent(Loc.T("Shrink"), Loc.T("Take away the selected pieces that touch a piece that is not selected."));
            var (left, right) = PanelSelectionActionRow(grow, shrink);
            using (new EditorGUI.DisabledScope(_selection.Count == 0))
            {
                if (StudioGUI.SelectionCell(left, SelectGlyph.Grow, grow, false, true)) GrowSelection(1);
                if (StudioGUI.SelectionCell(right, SelectGlyph.Shrink, shrink, false, true)) GrowSelection(-1);
            }
        }

        (Rect first, Rect second) PanelSelectionActionRow(GUIContent first, GUIContent second)
        {
            const float height = 24f, gap = 3f;
            float leftMin = StudioGUI.SelectionCellWidth(first);
            float rightMin = second == null ? 0f : StudioGUI.SelectionCellWidth(second);
            bool stack = second != null && leftMin + rightMin + gap > PanelContentWidth;
            var row = GUILayoutUtility.GetRect(0f, stack ? height * 2f + gap : height, GUILayout.ExpandWidth(true));
            if (second == null) return (row, default);
            if (stack) return (new Rect(row.x, row.y, row.width, height), new Rect(row.x, row.y + height + gap, row.width, height));
            float space = Mathf.Max(0f, row.width - gap);
            float leftWidth = Mathf.Clamp(space * .5f, leftMin, Mathf.Max(leftMin, space - rightMin));
            return (new Rect(row.x, row.y, leftWidth, height), new Rect(row.x + leftWidth + gap, row.y, Mathf.Max(0f, space - leftWidth), height));
        }

        Action DrawSelectionHandoff(Layer limitTarget)
        {
            GUILayout.Label(limitTarget != null ? LayerLimitPrompt(limitTarget) :
                SelectedElementCount == 0 ? Loc.T("New layers start on the whole texture.") :
                HasFaceSelection ? Loc.T("New layers start with these faces.") : Loc.T("New layers start with these pieces."), StudioGUI.CheckLabel);
            Action handoff = null;
            if (limitTarget != null)
            {
                using (new EditorGUI.DisabledScope(!CanApplyLayerLimit(limitTarget)))
                {
                    if (GUILayout.Button(LayerLimitLabel)) handoff = () => ApplyPendingLayerLimit(limitTarget);
                    StudioGUI.Count("limit.applyToLayer", GUILayoutUtility.GetLastRect());
                }
            }
            else if (CanPaintCurrentSelection)
            {
                var paint = PaintSelectionContent();
                var gradient = CanGradientCurrentSelection ? GradientSelectionContent() : null;
                var (left, right) = PanelSelectionActionRow(paint, gradient);
                if (StudioGUI.SelectionCell(left, SelectGlyph.PaintSelected, paint, false, true)) handoff = PaintCurrentSelection;
                StudioGUI.Count("limit.paintSelected", left);
                if (gradient != null)
                {
                    if (StudioGUI.SelectionCell(right, SelectGlyph.Gradient, gradient, false, true)) handoff = GradientCurrentSceneSelection;
                    StudioGUI.Count("limit.gradientSelected", right);
                }
            }
            return handoff;
        }

        void DrawSceneSelectionOptions()
        {
            bool pick = _pickInScene;
            InfoRow("limit.clickAvatar", "scene-view", () => pick = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Select on model"),
                FaceSelectionTool ? Loc.T("Use the selected face tool on the model in the Scene view.")
                    : Loc.T("Click the model in the Scene view to select the piece under the mouse (outlined in white).")), _pickInScene));
            if (pick != _pickInScene) { _pickInScene = pick; _hoverIsland = -1; SceneView.RepaintAll(); }
            bool hl = _highlightInScene;
            InfoRow("limit.showOnAvatar", "scene-view", () => hl = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Highlight on model"),
                Loc.T("The selection is drawn in orange in the Scene view. Parts hidden behind other geometry show faintly.")), _highlightInScene));
            if (hl != _highlightInScene) { _highlightInScene = hl; SceneView.RepaintAll(); }
        }

        void DrawTileList()
        {
            if (!HasOtherTiles) return;
            var counts = _uv.TileCounts();
            string total = Loc.F("{0} tiles", counts.Count);
            if (!StudioGUI.BeginFoldGroup(ref _openUvTiles, Loc.T("UV tiles"), total, total, infoId: "uv-map"))
            {
                StudioGUI.EndGroup();
                return;
            }
            GUILayout.Label(Loc.T("Pieces in other tiles sit outside this texture. You can select and spotlight them, but paint and masks don't reach them."), StudioGUI.Hint);
            int shown = 0;
            foreach (var kv in counts.OrderBy(k => k.Key.y).ThenBy(k => k.Key.x))
            {
                if (++shown > 16) { GUILayout.Label(Loc.F("and {0} more tiles", counts.Count - 16), StudioGUI.Hint); break; }
                string label = Loc.F("Tile {0} · {1} piece{2}", TileLabel(kv.Key), kv.Value, kv.Value == 1 ? "" : "s") + (kv.Key == Vector2Int.zero ? Loc.T("  (this texture)") : "");
                if (GUILayout.Button(new GUIContent(label, Loc.T("Select every piece in this tile. Shift adds, Ctrl removes.")), EditorStyles.miniButton))
                    SelectTile(kv.Key, SelectOpOf(Event.current));
            }
            StudioGUI.EndGroup();
        }

        void InfoRow(string controlId, string conceptId, Action draw)
        {
            StudioGUI.Count(controlId);
            using (new EditorGUILayout.HorizontalScope())
            {
                draw();
                StudioGUI.Info(conceptId);
            }
        }

        void DrawPaintTab()
        {
            bool mask = MaskEditing;
            if (mask) DrawMaskEditingBanner();
            if (mask && (_brush.gradientTool || _brush.blendTool)) SetPaintTool(0);
            bool picking = PickingForPaint;
            bool showGradientMode = !picking && GradientToolActive && SelectedTextureGradient != null;
            if (StudioGUI.BeginFoldGroup(ref _openPaintTools, Loc.T("Paint tools")))
            {
                DrawPaintToolStrip(mask, picking);
                if (showGradientMode) DrawTextureGradientMode();
                DrawPaintSelectionBar(picking);
            }
            StudioGUI.EndGroup();

            int toolIndex = PaintToolIndex;
            if (!picking)
            {
                DrawPaintBrushPanel(mask, toolIndex);
                if (toolIndex == 3) DrawGradientTargetGroup();
                else DrawPaintTargetGroup();
            }

            if (_revealLayerList && Event.current.type == EventType.Repaint)
            {
                _panelScroll.y = GUILayoutUtility.GetLastRect().yMax;
                _revealLayerList = false;
                Repaint();
            }
            DrawLayerList();
            DrawSelectedLayerSettings();
            if (_openLayers && Advanced) DrawHistory();
        }

        void DrawPaintBrushPanel(bool mask, int toolIndex)
        {
            DrawPaintColourGroup(mask, toolIndex);
            bool simpleTool = toolIndex == 2 || toolIndex == 3;
            string brushTitle = toolIndex switch
            {
                1 => mask ? Loc.T("Hide") : Loc.T("Eraser"),
                2 => Loc.T("Fill"),
                3 => Loc.T("Gradient"),
                4 => Loc.T("Blend"),
                _ => mask ? Loc.T("Mask brush") : Loc.T("Brush"),
            };
            string brushSummary = toolIndex switch
            {
                2 => FillScopeSummary(),
                3 => Loc.F("Opacity {0:0}%", _brush.opacity * 100f),
                _ => ActiveBrushLabel + " " + Loc.F("{0:0} px", _brush.size),
            };
            bool brushOpen = StudioGUI.BeginFoldGroup(ref _openBrush, brushTitle, brushSummary, simpleTool ? null : ActiveBrushLabel,
                Loc.T("Settings for the selected tool."));
            if (brushOpen) DrawPaintToolSettings(mask, toolIndex, simpleTool);
            StudioGUI.EndGroup();
        }

        void DrawPaintColourGroup(bool mask, int toolIndex)
        {
            if (mask || _brush.mode == BrushMode.Erase || toolIndex == 4) return;
            if (StudioGUI.BeginFoldGroup(ref _openColour, Loc.T("Colour"), "#" + ColorUtility.ToHtmlStringRGB(_brush.color), null,
                    Loc.T("The brush colour, palettes and colour picker.")))
            {
                DrawColourControls();
                if (toolIndex == 3 && _grad.colours == GradientColours.Custom)
                    GUILayout.Label(Loc.T("The custom gradient sets the colours. The main and sub colours are used by the other Colours options."), StudioGUI.Hint);
            }
            StudioGUI.EndGroup();
        }

        static GUIStyle _panelPad;
        static GUIStyle PanelPadStyle => _panelPad ??= new GUIStyle { padding = new RectOffset(4, 4, 4, 6) };

        void DrawPaintToolStrip(bool mask, bool picking)
        {
            int toolIndex = picking ? -1 : PaintToolIndex;
            int newTool;
            using (new EditorGUILayout.HorizontalScope())
            {
                newTool = PaintToolPalette(toolIndex, mask, PanelContentWidth - 18f, scene: false, picking: picking);
                StudioGUI.Count("paint.tools", GUILayoutUtility.GetLastRect());
                StudioGUI.Info("layer");
            }
            if (newTool != toolIndex) ChangeSceneBar("Select paint tool", () => SetPaintTool(newTool));
        }

        void DrawPaintToolSettings(bool mask, int toolIndex, bool simpleTool)
        {
            if (_brush.gradientTool && _brush.mode == BrushMode.Paint) DrawGradientSettings();
            else if (_brush.fillTool) DrawFillSettings();
            else if (toolIndex == 4) DrawBlendSettings();
            GUILayout.Space(4);
            if (!simpleTool)
            {
                float size = _brush.size;
                InfoRow("paint.size", "layer", () => size = DrawBrushSize(Loc.T("Size (px)"), PanelContentWidth - 24f, "paint.brushPresetSize"));
                if (size != _brush.size)
                {
                    Undo.RecordObject(this, "Change brush size");
                    _brush.size = size;
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
            EditorGUI.BeginChangeCheck();
            string strength = toolIndex switch
            {
                1 => mask ? Loc.T("Hide strength") : Loc.T("Erase strength"),
                _ => Loc.T("Paint strength"),
            };
            if (!_brush.gradientTool)
                InfoRow("paint.strength", "opacity", () => _brush.opacity = EditorGUILayout.Slider(new GUIContent(strength,
                    Loc.T("How strong one stroke (or fill) is")), _brush.opacity, 0f, 1f));
            if (!simpleTool)
                InfoRow("paint.edgeSoftness", "layer", () => _brush.shape.hardness = EditorGUILayout.Slider(new GUIContent(Loc.T("Hardness"), Loc.T("0 = soft edge, 1 = hard edge")), _brush.shape.hardness, 0f, 1f));
            if (EditorGUI.EndChangeCheck()) MarkBrushEdited();
            if (!simpleTool)
            {
                DrawFavouriteBrushes();
                DrawBrushPreviewStrip();
                DrawBristleLoad();
                DrawMoreBrushes();
                DrawBrushAlphaChooser();
                DrawPenControls();
            }
            if (!simpleTool && Advanced)
            {
                StudioGUI.Count("paint.moreBrushSettings");
                _foldBrushAdvanced = StudioGUI.Foldout(_foldBrushAdvanced, StudioGUI.Content("paint.moreBrushSettings", "More brush settings"));
                if (_foldBrushAdvanced) DrawBrushAdvanced();
            }
        }

        int WrapToolbar(int selected, GUIContent[] contents, GUIStyle style = null, float rowHeight = 22f, float width = -1f,
            bool navigate = false, int keyboard = 0)
        {
            style ??= GUI.skin.button;
            if (width <= 0f) width = _panelContentWidth - 16f;
            float widest = 0f;
            foreach (var c in contents) widest = Mathf.Max(widest, style.CalcSize(c).x);
            int perRow = Mathf.Clamp(Mathf.FloorToInt(width / (widest + 4f)), 1, contents.Length);
            int rows = Mathf.CeilToInt(contents.Length / (float)perRow);
            perRow = Mathf.CeilToInt(contents.Length / (float)rows);
            var rect = GUILayoutUtility.GetRect(0f, rowHeight * rows, GUILayout.ExpandWidth(true));
            rect.width = Mathf.Min(rect.width, width);
            return navigate ? DrawStepSelectionGrid(rect, selected, contents, perRow, style, keyboard)
                : GUI.SelectionGrid(rect, selected, contents, perRow, style);
        }

        static readonly StrokeTool[] BlendTools = { StrokeTool.Blur, StrokeTool.Smudge };

        void DrawBlendSettings()
        {
            StrokeTool current = _brush.blendKind == StrokeTool.Smudge ? StrokeTool.Smudge : StrokeTool.Blur;
            StrokeTool kind = current;
            InfoRow("paint.blendKind", "layer", () => kind = BlendTools[ToolPalette(Array.IndexOf(BlendTools, current),
                BlendGlyphs, PaintLabels.blend, PanelContentWidth - 18f, scene: false)]);
            bool smudge = kind == StrokeTool.Smudge;
            float amount = _brush.blendAmount;
            InfoRow("paint.blendAmount", "layer", () => amount = EditorGUILayout.Slider(new GUIContent(smudge ? Loc.T("Smudge length") : Loc.T("Blur amount"),
                smudge ? Loc.T("How far colour is dragged, as a share of the brush size") : Loc.T("How soft, as a share of the brush size")),
                _brush.blendAmount, 0.1f, 3f));
            bool stay = _brush.blendKeepIsland;
            InfoRow("paint.dontSpill", "island", () => stay = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Stay in piece"),
                Loc.T("Recommended: never pulls in colour from empty texture space or a neighbouring part, so seams stay clean.")), _brush.blendKeepIsland));
            GUILayout.Label(Loc.T("Works on what you see. The result goes on a Paint layer (a “Blending” layer is added if needed), so you can erase or undo it. Opacity sets the strength."), StudioGUI.Hint);

            bool discrete = kind != current || stay != _brush.blendKeepIsland;
            if (!discrete && amount == _brush.blendAmount) return;
            if (discrete)
            {
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            Undo.RecordObject(this, "Change blend tool settings");
            if (kind != current) _brush.blendKind = kind;
            _brush.blendAmount = amount;
            _brush.blendKeepIsland = stay;
            if (discrete)
            {
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            Repaint();
            SceneView.RepaintAll();
        }

        static GUIStyle s_maskBannerTitle;

        void DrawMaskEditingBanner()
        {
            var layer = ActiveLayer;
            var r = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(StudioGUI.Accent.r, StudioGUI.Accent.g, StudioGUI.Accent.b, 0.18f));
                EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), StudioGUI.Accent);
            }
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                s_maskBannerTitle ??= new GUIStyle(EditorStyles.boldLabel) { wordWrap = true, clipping = TextClipping.Clip, fixedHeight = 0f };
                GUILayout.Label(new GUIContent(Loc.F("Painting mask for “{0}”", layer.name), layer.name), s_maskBannerTitle,
                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.MaxHeight(36f));
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = StudioGUI.InfoBlue;
                bool done = GUILayout.Button(new GUIContent(Loc.T("Done"), Loc.T("Finish editing the mask and return to painting layers")),
                    GUILayout.ExpandWidth(false), GUILayout.Height(20));
                StudioGUI.Count("paint.maskDone", GUILayoutUtility.GetLastRect());
                if (done)
                    SetMaskEditing(null, false);
                GUI.backgroundColor = oldBg;
                GUILayout.Space(4);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                GUILayout.Label(Loc.T("Show reveals the layer. Hide covers it."), StudioGUI.Hint, GUILayout.MinWidth(0));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                _showMaskOverlay = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Red mask overlay"), Loc.T("Show hidden areas in red")),
                    _showMaskOverlay, GUILayout.MinWidth(0));
                StudioGUI.Count("paint.maskOverlay", GUILayoutUtility.GetLastRect());
                bool invert = GUILayout.Button(new GUIContent(Loc.T("Invert"), Loc.T("Swap hidden and shown")), EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                StudioGUI.Count("paint.maskInvert", GUILayoutUtility.GetLastRect());
                if (invert) InvertPaintMask(layer);
                GUILayout.Space(4);
            }
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        void DrawPaintTargetGroup()
        {
            if (MaskEditing)
            {
                var ml = ActiveLayer;
                if (!StudioGUI.BeginFoldGroup(ref _openMaskPaintingOptions, Loc.T("Mask painting options"), ml.name))
                {
                    StudioGUI.EndGroup();
                    return;
                }
                GUILayout.Label(Loc.F("The mask of “{0}” ({1}). {2} mask stroke(s) · Ctrl+Z undoes the last.", ml.name, ml.TypeName, ml.maskStrokes.Count), StudioGUI.CheckLabel);
                if (ml.maskMode == MaskMode.SelectedIslands)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("The layer is also limited to {0} piece(s); revealing can't show it outside them.", ml.islands.Count));
                if (ml.opacity <= 0.001f) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("This layer is at 0% opacity, so the mask has nothing to show."));
                GUILayout.Space(2);
                EditorGUI.BeginChangeCheck();
                InfoRow("paint.dontSpill", "island", () => _brush.stayInsideIslands = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Don't spill onto other pieces"),
                    Loc.T("Flat masks stay on pieces under the brush centre. A 3D mask can also cross joined seams.")), _brush.stayInsideIslands));
                InfoRow("paint.onAvatar", "scene-view", () => _paintInScene = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Paint directly on the model (Scene view)"),
                    Loc.T("Drag on the model in the Scene view. Alt+drag and right-drag still move the camera.")), _paintInScene));
                DrawSurfaceBrushToggle();
                if (EditorGUI.EndChangeCheck()) { Repaint(); SceneView.RepaintAll(); }
                StudioGUI.Divider();
                DrawSymmetryControls();
                StudioGUI.EndGroup();
                return;
            }
            var optBits = new List<string>();
            if (_brush.stayInsideIslands) optBits.Add(Loc.T("stays in piece"));
            if (_paintInScene) optBits.Add(Loc.T("on model"));
            if (_symmetry) optBits.Add(Loc.T("mirrored"));
            var active = ActiveLayer;
            var activePaint = active as PaintLayer;
            var blockedPaint = EmptyLockedPaintDestination;
            var emissionNotes = EmissionNotes();
            var layerNotes = activePaint != null ? PaintLayerNotes(activePaint) : null;
            if (blockedPaint != null) layerNotes.Add((StudioGUI.Check.Problem, EmptyPaintLockMessage, null));
            int noteCount = emissionNotes.Count + (layerNotes?.Count ?? 0);
            int problemCount = emissionNotes.Count(n => n.kind == StudioGUI.Check.Problem) + (layerNotes?.Count(n => n.kind == StudioGUI.Check.Problem) ?? 0);
            string noteText = problemCount > 0 ? Loc.F("{0} problem{1}", problemCount, problemCount == 1 ? "" : "s") : Loc.F("{0} note{1}", noteCount, noteCount == 1 ? "" : "s");
            string optSummary = noteCount > 0
                ? noteText + (optBits.Count == 0 || PanelContentWidth < 330f ? "" : " · " + string.Join(", ", optBits))
                : optBits.Count == 0 ? Loc.T("defaults") : string.Join(", ", optBits);
            if (problemCount > 0) _openPaintOptions = true;
            if (!StudioGUI.BeginFoldGroup(ref _openPaintOptions, Loc.T("Painting options"), optSummary, null,
                    Loc.T("Where paint goes: the layer it lands on, piece limits, painting on the model and mirroring."), "island"))
            {
                StudioGUI.EndGroup();
                return;
            }
            foreach (var n in emissionNotes) StudioGUI.CheckRow(n.kind, n.text, n.tip);
            if (activePaint != null)
            {
                var pl = activePaint;
                if (!MatchesFaceSelection(pl) && _brush.mode != BrushMode.Erase)
                    GUILayout.Label(Loc.T("New paint goes on a layer limited to the selected faces."), StudioGUI.CheckLabel);
                else if (BlendToolActive && (pl.blend != BlendMode.Normal || pl.HasActiveAdditionalBlends))
                    GUILayout.Label(Loc.F("Blur and smudge go on a new “Blending” layer above “{0}”. They need just Normal blend mode.",
                        pl.name), StudioGUI.CheckLabel);
                else GUILayout.Label((BlendToolActive ? Loc.F("Blending on layer “{0}”.", pl.name) : Loc.F("Painting on layer “{0}”.", pl.name)), StudioGUI.CheckLabel);
                if (Advanced)
                {
                    bool lockT = pl.lockTransparent;
                    InfoRow("paint.lockTransparent", "alpha", () => lockT = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Only recolour existing paint"),
                        Loc.T("New strokes only recolour what's already painted on this layer, never outside it.")), pl.lockTransparent));
                    if (lockT != pl.lockTransparent)
                    {
                        Undo.RecordObject(_recipe, "Only recolour existing paint");
                        pl.lockTransparent = lockT;
                        MarkEdited();
                    }
                }
                foreach (var n in layerNotes) StudioGUI.CheckRow(n.kind, n.text, n.tip);
                if (pl == blockedPaint)
                {
                    bool allow = GUILayout.Button(StudioGUI.Content("paint.allowNewPaint", "Allow new paint"), EditorStyles.miniButton);
                    StudioGUI.Count("paint.allowNewPaint", GUILayoutUtility.GetLastRect());
                    if (allow) AllowNewPaint(_recipe, _comp, pl);
                }
                DrawPaintLayerLimitActions(pl);
            }
            else
            {
                GUILayout.Label(HasFaceSelection ? Loc.T("New paint goes on a layer limited to the selected faces.")
                    : _selection.Count > 0 ? Loc.F("Your first stroke adds a new Paint layer, limited to the {0} selected piece(s).", _selection.Count)
                    : Loc.T("Your first stroke adds a new Paint layer."), StudioGUI.CheckLabel);
            }

            GUILayout.Space(2);
            EditorGUI.BeginChangeCheck();
            InfoRow("paint.dontSpill", "island", () => _brush.stayInsideIslands = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Don't spill onto other pieces"),
                Loc.T("Flat brushes stay on pieces under the brush centre. The 3D brush can also cross joined seams.")), _brush.stayInsideIslands));
            InfoRow("paint.onAvatar", "scene-view", () => _paintInScene = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Paint directly on the model (Scene view)"),
                Loc.T("Drag on the model in the Scene view. Alt+drag and right-drag still move the camera.")), _paintInScene));
            DrawSurfaceBrushToggle();
            if (EditorGUI.EndChangeCheck()) { Repaint(); SceneView.RepaintAll(); }
            StudioGUI.Divider();
            DrawPaintUvLimit();
            DrawSymmetryControls();
            StudioGUI.EndGroup();
        }

        void DrawPaintLayerLimitActions(PaintLayer layer)
        {
            if (MaskSelectedForEditing || _needsRebuild) return;
            bool hasLimit = layer.maskMode != MaskMode.WholeTexture;
            bool hasSelection = CanApplyLayerLimit(layer);
            if (!hasLimit && !hasSelection) return;

            bool faces = hasSelection ? HasFaceSelection : layer.maskMode == MaskMode.SelectedFaces;
            bool exclude = LayerLimitExcludesSelection(layer);
            string edit = hasSelection
                ? exclude ? ExcludeSelectionLabel(faces) : Loc.T("Use current selection")
                : faces ? Loc.T("Pick faces") : Loc.T("Pick pieces");
            var use = new GUIContent(edit);
            var remove = new GUIContent(HasFaceSelection ? Loc.T("Remove layer limit") : Loc.T("Paint everywhere"),
                Loc.T("Remove the selection limit from this layer"));
            int action = WrapToolbar(-1, hasLimit ? new[] { use, remove } : new[] { use }, EditorStyles.miniButton, 19f, PanelContentWidth);
            StudioGUI.Count("paint.layerLimit", GUILayoutUtility.GetLastRect());
            if (action == 0)
            {
                if (hasSelection) ChangeSceneBar(edit, () => CaptureLayerLimitSelection(layer, faces, exclude));
                else if (faces) PickFacesForLayer(layer);
                else PickPiecesForLayer(layer);
            }
            else if (action == 1)
            {
                Undo.RecordObject(_recipe, "Remove limit");
                layer.maskMode = MaskMode.WholeTexture;
                MarkEdited();
            }
        }

        List<(StudioGUI.Check kind, string text, string tip)> EmissionNotes()
        {
            var notes = new List<(StudioGUI.Check, string, string)>();
            if (CurrentSlot == null || CurrentSlot.role != TextureRole.Color) return notes;
            foreach (var g in LookCheck.StrongEmissions(_material))
                if (g.map == null || g.map != CurrentSlot.texture)
                    notes.Add((StudioGUI.Check.Note, g.map != null
                        ? Loc.F("{0} adds a lot of light on top of this texture (from “{1}”), so paint here may barely show on the model. Edit that texture instead to change the look.", g.label, g.map.name)
                        : Loc.F("{0} adds a lot of light on top of this texture, so paint here may barely show on the model. Edit the material's emission instead to change the look.", g.label), null));
            return notes;
        }

        List<(StudioGUI.Check kind, string text, string tip)> PaintLayerNotes(PaintLayer pl)
        {
            var notes = new List<(StudioGUI.Check, string, string)>();
            if (pl.clipToBelow)
            {
                var clipBase = ClipBaseOf(ActiveIndex);
                if (clipBase != null)
                {
                    bool hidden = ClipBaseShowsNothing(clipBase);
                    notes.Add((hidden ? StudioGUI.Check.Problem : StudioGUI.Check.Note, hidden
                        ? ClipBlockedText(clipBase)
                        : Loc.F("Clipped to “{0}”: paint only shows where that layer shows.", clipBase.name), null));
                }
            }
            if (!pl.enabled) notes.Add((StudioGUI.Check.Note, Loc.T("This layer is hidden; it will be shown again when you paint."), null));
            if (pl.opacity <= 0.001f) notes.Add((StudioGUI.Check.Problem, Loc.T("This layer is at 0% opacity, so paint won't show."), null));
            if (pl.maskMode == MaskMode.SelectedIslands)
            {
                int pieces = pl.islands.Count(IsCurrentPiece);
                notes.Add((StudioGUI.Check.Note,
                    pl.invertMask ? Loc.F("Paint everywhere except the pieces in this limit ({0}).", pieces)
                                  : Loc.F("Paint only inside the selected pieces ({0}).", pieces),
                    Loc.T("Use current selection to change the pieces in this limit, or Paint everywhere to clear it.")));
            }
            if (pl.maskMode == MaskMode.SelectedFaces && !HasCurrentFaceLimit(pl))
                notes.Add((StudioGUI.Check.Problem,
                    Loc.T("This face limit does not match the current UV mesh. Pick new faces or choose Everything."), null));
            return notes;
        }

        void MarkBrushEdited()
        {
            _brush.edited = true;
            Repaint();
            SceneView.RepaintAll();
        }

        float PanelContentWidth => _panelContentWidth - 30f;

        bool ActiveBrushIsUser => _brush.presetIsUser || !BrushLibrary.BuiltIn.Any(preset => preset.name == _brush.presetName);

        string ActiveBrushLabel => (ActiveBrushIsUser ? _brush.presetName : BrushLabel(_brush.presetName)) + (_brush.edited ? "*" : "");

        void DrawFavouriteBrushes()
        {
            StudioGUI.Count("paint.brushes");
            DrawBrushButtons(FavouriteBrushPresets);
            if (_brush.shape?.tip == BrushTip.Drips)
                GUILayout.Label(Loc.T("On the model, paint runs down the surface. On the texture, it runs toward the bottom."), StudioGUI.Hint);
        }

        void DrawMoreBrushes()
        {
            int count = BrushLibrary.BuiltIn.Length - FavouriteBrushPresets.Count + BrushLibrary.User.Count;
            StudioGUI.Count("paint.moreBrushes");
            _foldMoreBrushes = StudioGUI.Foldout(_foldMoreBrushes, new GUIContent(Loc.F("More brushes ({0})", count)));
            if (!_foldMoreBrushes) return;
            var more = BrushLibrary.BuiltIn.Where(p => !FavouriteBrushes.Contains(p.name)).Select(p => (preset: p, user: false))
                .Concat(BrushLibrary.User.Select(p => (preset: p, user: true)));
            foreach (var category in more.GroupBy(item => item.user ? "My brushes" : BrushLibrary.CategoryOf(item.preset)))
            {
                GUILayout.Label(Loc.T(category.Key), EditorStyles.miniBoldLabel);
                DrawBrushButtons(category.ToList());
            }
        }

        const float ChipThumbW = 32f, ChipThumbH = 14f;
        static GUIStyle _chipStyle;
        static GUIStyle ChipStyle => _chipStyle ??= new GUIStyle(EditorStyles.miniButton)
            { padding = new RectOffset((int)ChipThumbW + 8, 2, 2, 2), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        BrushChipThumbs _chipThumbs;

        void DrawChipThumb(Rect chip, BrushPreset p)
        {
            if (Event.current.type != EventType.Repaint) return;
            var r = new Rect(chip.x + 4, chip.y + (chip.height - ChipThumbH) * 0.5f, ChipThumbW, ChipThumbH);
            EditorGUI.DrawRect(r, StudioGUI.CanvasBg);
            _chipThumbs ??= new BrushChipThumbs();
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var tex = _chipThumbs.Get(p, Mathf.RoundToInt(r.width * ppp), Mathf.RoundToInt(r.height * ppp));
            var old = GUI.color;
            GUI.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        void DrawBrushButtons(List<(BrushPreset preset, bool user)> list)
        {
            float avail = PanelContentWidth;
            const float spacing = 3f;
            float usualWidth = 84f + ChipThumbW + 8f;
            int preferredColumns = Mathf.Clamp(Mathf.FloorToInt((avail + spacing) / (usualWidth + spacing)), 2, 5);
            BrushPreset apply = null;
            bool applyUser = false;
            bool activeUser = ActiveBrushIsUser;
            string keepsSize = Loc.T("Keeps your current brush size.");
            var chips = new (GUIContent label, float width, bool active)[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var (preset, user) = list[i];
                bool on = _brush.presetName == preset.name && user == activeUser;
                string name = user ? "★ " + preset.name : BrushLabel(preset.name);
                string tip = user ? Loc.T("Your saved brush") : on && _brush.edited
                    ? Loc.T("Click to reset to the original brush") : null;
                var label = new GUIContent("*" + name, name + "\n" + (tip == null ? "" : tip + "\n") + keepsSize);
                chips[i] = (label, ChipStyle.CalcSize(label).x + 4f, on);
                if (!on || !_brush.edited) label.text = name;
            }

            for (int i = 0; i < list.Count;)
            {
                int cols = preferredColumns;
                while (cols > 1)
                {
                    float width = (avail - (cols - 1) * spacing) / cols;
                    int end = Mathf.Min(i + cols, chips.Length);
                    int fit = i;
                    while (fit < end && chips[fit].width <= width) fit++;
                    if (fit == end) break;
                    cols--;
                }
                float bw = (avail - (cols - 1) * spacing) / cols;
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + cols && j < list.Count; j++)
                    {
                        var (p, user) = list[j];
                        var (label, _, on) = chips[j];
                        bool now = GUILayout.Toggle(on, label, ChipStyle, GUILayout.Width(bw), GUILayout.Height(20));
                        DrawChipThumb(GUILayoutUtility.GetLastRect(), p);
                        if (now != on) { apply = p; applyUser = user; }
                    }
                }
                i += cols;
            }
            if (apply != null) ApplyBrushPreset(apply, applyUser);
        }

        void ApplyBrushPreset(BrushPreset preset, bool user)
        {
            Undo.RecordObject(this, Loc.T("Choose brush preset"));
            SetBrushPreset(preset, user);
        }

        void SetBrushPreset(BrushPreset preset, bool user)
        {
            float size = _brush.size;
            preset.ApplyTo(_brush);
            _brush.size = size;
            _brush.presetIsUser = user;
            BrushStampCache.RequestWarm(_brush.shape);
            Repaint();
            SceneView.RepaintAll();
        }

        float DrawBrushSize(string label, float width, string restoreControl, bool scene = false)
        {
            var source = ActiveBrushIsUser ? (IEnumerable<BrushPreset>)BrushLibrary.User : BrushLibrary.BuiltIn;
            var preset = source.FirstOrDefault(brush => brush.name == _brush.presetName);
            var title = new GUIContent(label, Loc.T("Brush diameter in source texture pixels. [ and ] change it."));
            float Slider()
            {
                var content = scene ? SceneSliderLabel(title, width) : title;
                var size = StudioGUI.LogSlider(content, _brush.size, BrushSettings.MinSize, BrushSettings.MaxSize, null);
                if (scene) StudioGUI.Count("scene.brushSize", GUILayoutUtility.GetLastRect());
                return size;
            }
            float saved = preset != null && float.IsFinite(preset.size)
                ? Mathf.Clamp(preset.size, BrushSettings.MinSize, BrushSettings.MaxSize) : _brush.size;
            bool canRestore = !Mathf.Approximately(saved, _brush.size);
            string tip = canRestore ? Loc.F("Use preset size ({0:0.#} px)", saved) : null;
            if (width < 175f)
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    float size = Slider();
                    var rect = GUILayoutUtility.GetRect(0f, canRestore ? EditorGUIUtility.singleLineHeight : 0f, GUILayout.MaxWidth(width));
                    if (!canRestore) return size;
                    bool restore = GUI.Button(rect, new GUIContent(Loc.T("Use preset size"), tip), EditorStyles.miniButton);
                    StudioGUI.Count(restoreControl, rect);
                    return restore ? saved : size;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Min(previousLabelWidth, width * 0.38f);
                float size;
                try { size = Slider(); }
                finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
                float buttonWidth = canRestore ? 24f : 0f;
                var rect = GUILayoutUtility.GetRect(buttonWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(buttonWidth));
                if (!canRestore) return size;
                bool restore = GUI.Button(rect, StudioGUI.Icon("Refresh", "", tip), EditorStyles.miniButton);
                StudioGUI.Count(restoreControl, rect);
                return restore ? saved : size;
            }
        }

        void DrawBrushPreviewStrip()
        {
            var r = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, StudioGUI.CanvasBg);
            if (_brush.opacity <= 0f)
            {
                GUI.Label(new Rect(r.x + 5f, r.y + 2f, r.width - 10f, r.height - 4f),
                    Loc.T("Strength is 0%. Raise it to see a mark."), StudioGUI.Hint);
                return;
            }
            if (_brushPreview == null) _brushPreview = new BrushPreview();
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var tex = _brushPreview.Get(_brush, Mathf.RoundToInt(r.width * ppp), Mathf.RoundToInt(r.height * ppp), _brush.opacity);
            var old = GUI.color;
            GUI.color = _brush.mode == BrushMode.Erase
                ? new Color(1f, 0.6f, 0.6f, 1f)
                : MaskEditing || BlendToolActive ? new Color(0.92f, 0.92f, 0.92f, 1f)
                : new Color(_brush.color.r, _brush.color.g, _brush.color.b, 1f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        void DrawBrushTip(float width = -1f)
        {
            var sh = _brush.shape;
            EnsureBrushAlphaLabels();
            var tip = SteppedChoice(new GUIContent(Loc.T("Tip"), Loc.T("Choose a simple tip, a brush shape or paint that runs down the surface.")),
                sh.tip, AllBrushTips, (rect, value) =>
                {
                    int next = EditorGUI.Popup(rect, Array.IndexOf(AllBrushTips, value), _brushTipLabels);
                    return next >= 0 ? AllBrushTips[next] : value;
                }, width);
            if (width > 0f) StudioGUI.Count("scene.brushTip", GUILayoutUtility.GetLastRect());
            if (tip != sh.tip)
            {
                Undo.RecordObject(this, Loc.T("Change brush tip"));
                sh.tip = tip;
                if (tip == BrushTip.Stamp)
                {
                    if (!BrushStampLibrary.IsValid(sh.alphaId, sh.alphaVersion))
                    { sh.alphaId = "procedural_round"; sh.alphaVersion = BrushStampLibrary.Version; }
                    sh.jitterVersion = 1;
                    _foldBrushAlphas = true;
                }
                BrushStampCache.RequestWarm(sh);
                MarkBrushEdited();
            }
        }

        static readonly bool[] BrushDotModes = { false, true };

        static readonly BristleLoad[] BristleLoads = { BristleLoad.Dry, BristleLoad.Natural, BristleLoad.Loaded };
        string _bristleLoadLanguage;
        GUIContent[] _bristleLoadLabels;

        void DrawBristleLoad(float width = -1f)
        {
            var shape = _brush.shape;
            if (shape?.tip != BrushTip.Bristle) return;
            if (_bristleLoadLabels == null || _bristleLoadLanguage != Loc.Current)
            {
                _bristleLoadLanguage = Loc.Current;
                _bristleLoadLabels = new[]
                {
                    new GUIContent(Loc.T("Dry"), Loc.T("Fine hairs leave broken tracks.")),
                    new GUIContent(Loc.T("Natural"), Loc.T("Separate hairs follow the stroke.")),
                    new GUIContent(Loc.T("Loaded"), Loc.T("Fuller tracks blend into a solid stroke.")),
                };
            }
            GUILayout.Label(Loc.T("Bristle paint load"), EditorStyles.miniLabel);
            int current = Array.IndexOf(BristleLoads, shape.bristleLoad);
            int next = WrapToolbar(current, _bristleLoadLabels, EditorStyles.miniButton, 18f, width, navigate: true);
            if (next == current || next < 0) return;
            Undo.RecordObject(this, Loc.T("Bristle paint load"));
            shape.bristleLoad = BristleLoads[next];
            MarkBrushEdited();
            Learn.Did(LearnEvents.BristleLoadChanged);
        }

        void DrawBrushDotSize(float width = -1f)
        {
            var shape = _brush.shape;
            if (!shape.HasDots) return;
            bool scale = shape.dotSize <= 0f;
            var modes = new[] { Loc.T("Fixed size"), Loc.T("Scale with brush") };
            bool nextScale = SteppedChoice(new GUIContent(Loc.T("Dots"), Loc.T("Change the spread with Size. Fixed dots keep their own diameter.")),
                scale, BrushDotModes, (rect, current) => EditorGUI.Popup(rect, current ? 1 : 0, modes) == 1, width);
            StudioGUI.Count(width > 0f ? "scene.brushDots" : "paint.brushDots", GUILayoutUtility.GetLastRect());
            float size = shape.dotSize;
            if (!scale)
            {
                var label = new GUIContent(Loc.T("Dot size (px)"), Loc.T("Diameter in source texture pixels, before jitter or pen pressure."));
                if (width > 0f)
                {
                    var row = SceneBarRow(width);
                    float previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = Mathf.Min(EditorStyles.label.CalcSize(label).x + 4f, Mathf.Max(48f, row.width - 42f));
                    try { size = EditorGUI.DelayedFloatField(row, label, size); }
                    finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
                }
                else size = EditorGUILayout.DelayedFloatField(label, size);
            }
            if (nextScale) size = 0f;
            else if (scale) size = shape.tip == BrushTip.Spray ? BrushShape.SprayDotSize : BrushShape.SplatterDotSize;
            else size = float.IsFinite(size) ? Mathf.Clamp(size, 1f, BrushShape.MaxDotSize) : shape.dotSize;
            if (size == shape.dotSize) return;
            Undo.RecordObject(this, Loc.T("Change dot size"));
            shape.dotSize = size;
            MarkBrushEdited();
        }

        void DrawBrushAdvanced()
        {
            var sh = _brush.shape;
            DrawBrushTip();
            EditorGUI.BeginChangeCheck();
            sh.spacing = EditorGUILayout.Slider(new GUIContent(Loc.T("Spacing"), Loc.T("Distance between dabs. Higher = dotted line.")),
                sh.spacing, 0.02f, Mathf.Max(4f, sh.spacing));
            if (sh.tip == BrushTip.Bristle || sh.tip == BrushTip.Spray)
                sh.density = EditorGUILayout.IntSlider(new GUIContent(sh.tip == BrushTip.Bristle ? Loc.T("Bristles") : Loc.T("Particles")), sh.density, 1, 48);
            if (sh.tip == BrushTip.Round || sh.tip == BrushTip.Square || sh.tip == BrushTip.Stamp)
                sh.roundness = EditorGUILayout.Slider(new GUIContent(Loc.T("Roundness"), Loc.T("Lower = flat tip (calligraphy, marker)")), sh.roundness, 0.05f, 1f);
            if (sh.tip != BrushTip.Spray)
            {
                sh.angle = EditorGUILayout.Slider(new GUIContent(Loc.T("Angle")), sh.angle, -180f, 180f);
            }
            sh.buildUp = EditorGUILayout.Toggle(new GUIContent(Loc.T("Airbrush (build up)"), Loc.T("Paint keeps building up while you brush over the same spot")), sh.buildUp);
            if (sh.buildUp) sh.flow = EditorGUILayout.Slider(new GUIContent(Loc.T("Flow"), Loc.T("How much each dab adds")), sh.flow, 0.01f, 1f);
            sh.grain = EditorGUILayout.Slider(new GUIContent(Loc.T("Grain"), Loc.T("Paper texture in the paint (chalk, pencil)")), sh.grain, 0f, 1f);
            if (sh.grain > 0f) sh.grainSize = EditorGUILayout.Slider(new GUIContent(Loc.T("Grain size (px)")), sh.grainSize, 1f, 64f);
            sh.scatter = EditorGUILayout.Slider(new GUIContent(Loc.T("Scatter"), Loc.T("Random offset of each dab")), sh.scatter, 0f, 3f);
            sh.sizeJitter = EditorGUILayout.Slider(new GUIContent(Loc.T("Size jitter")), sh.sizeJitter, 0f, 1f);
            sh.opacityJitter = EditorGUILayout.Slider(new GUIContent(Loc.T("Opacity jitter")), sh.opacityJitter, 0f, 1f);
            if (sh.tip == BrushTip.Stamp && sh.jitterVersion > 0)
            {
                float angleJitter = EditorGUILayout.Slider(new GUIContent(Loc.T("Angle jitter")), sh.angleJitter, 0f, 1f);
                if (angleJitter != sh.angleJitter)
                {
                    Undo.RecordObject(this, Loc.T("Change brush jitter"));
                    sh.angleJitter = angleJitter;
                }
            }
            if (PaintToolIndex == 0 && !MaskEditing) DrawBrushColourSettings(sh);
            if (EditorGUI.EndChangeCheck()) MarkBrushEdited();
            DrawBrushDotSize();

            StudioGUI.Divider();
            GUILayout.Label(Loc.T("Save this brush"), StudioGUI.GroupTitle);
            using (new EditorGUILayout.HorizontalScope())
            {
                _newBrushName = EditorGUILayout.TextField(_newBrushName ?? "");
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newBrushName)))
                {
                    if (GUILayout.Button(new GUIContent(Loc.T("Save"), Loc.T("Save these settings as your own brush (kept on this computer, not in the project)")), EditorStyles.miniButton, GUILayout.Width(52)))
                    {
                        if (SaveNamedBrush(_newBrushName)) GUI.FocusControl(null);
                    }
                }
            }
            var mine = ActiveBrushIsUser ? BrushLibrary.User.FirstOrDefault(p => p.name == _brush.presetName) : null;
            if (mine != null && StudioGUI.DangerButton(new GUIContent(Loc.F("Delete my brush “{0}”", mine.name)), EditorStyles.miniButton))
            {
                if (StudioDialog.Ask(Loc.T("Delete brush"), Msg.M11DeleteBrush(mine.name), Msg.BtnDelete, Msg.BtnCancel))
                {
                    BrushLibrary.Delete(mine.name);
                    _brush.edited = true;
                }
            }
        }

        void DrawBrushColourSettings(BrushShape shape)
        {
            shape.colorJitter = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Colour variety per stroke"),
                Loc.T("Each stroke gets a slightly different colour: natural variation for hair strands, fabric or skin.")), shape.colorJitter);
            if (shape.colorJitter)
            {
                EditorGUI.indentLevel++;
                shape.jitterHue = EditorGUILayout.Slider(new GUIContent(Loc.T("Hue (°)"), Loc.T("How far the hue may move, either way")), shape.jitterHue, 0f, 180f);
                shape.jitterSaturation = EditorGUILayout.Slider(new GUIContent(Loc.T("Saturation")), shape.jitterSaturation, 0f, 1f);
                shape.jitterLuminosity = EditorGUILayout.Slider(new GUIContent(Loc.T("Luminosity")), shape.jitterLuminosity, 0f, 1f);
                shape.jitterSubBlend = EditorGUILayout.Slider(new GUIContent(Loc.T("Blend with second colour"), Loc.T("Up to this much of the second colour is mixed into each stroke")), shape.jitterSubBlend, 0f, 1f);
                EditorGUI.indentLevel--;
            }
            _brush.colorMixing = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Colour mixing (blends with what's there)"),
                Loc.T("The brush picks up the colours underneath and mixes them with its own, like wet paint.")), _brush.colorMixing);
            if (_brush.colorMixing)
            {
                EditorGUI.indentLevel++;
                _brush.amountOfPaint = EditorGUILayout.Slider(new GUIContent(Loc.T("Amount of paint"), Loc.T("1 = only the brush colour. Lower = more of the colour underneath shows through.")), _brush.amountOfPaint, 0.05f, 1f);
                _brush.colorStretch = EditorGUILayout.Slider(new GUIContent(Loc.T("Colour stretch"), Loc.T("How far the colours underneath are pulled together, as a share of the brush size")), _brush.colorStretch, 0.1f, 2f);
                EditorGUI.indentLevel--;
            }
        }

        bool SaveNamedBrush(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            string name = input.Trim();
            if (BrushLibrary.User.Any(preset => preset.name == name))
            {
                if (!StudioDialog.Ask(Loc.T("Replace saved brush"), Loc.F("Replace “{0}” with the current brush settings?", name),
                        Loc.T("Replace"), Msg.BtnCancel)) return false;
            }
            else if (BrushLibrary.BuiltIn.Any(preset => string.Equals(preset.name, name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(BrushLabel(preset.name).Trim(), name, StringComparison.OrdinalIgnoreCase)))
            {
                Toast(Loc.F("“{0}” is a built-in brush. Give your brush a different name.", name), MessageType.Warning, 5f);
                return false;
            }

            Undo.RecordObject(this, Loc.T("Save this brush"));
            BrushLibrary.Save(BrushPreset.From(_brush, name));
            _brush.presetName = name;
            _brush.presetIsUser = true;
            _brush.edited = false;
            _newBrushName = "";
            _foldMoreBrushes = true;
            Toast(Loc.F("Saved brush “{0}”. It's under More brushes.", name), MessageType.Info, 3f);
            return true;
        }

        void DrawLayersTab()
        {
            DrawLayerList();
            DrawSelectedLayerSettings();
        }

        void DrawSelectedLayerSettings()
        {
            if ((_revealSelectedGradient || _layerSettingsToReveal != null) && Event.current.type == EventType.Repaint)
            {
                if (_revealSelectedGradient || _activeLayerId == _layerSettingsToReveal)
                {
                    _panelScroll.y = GUILayoutUtility.GetLastRect().yMax;
                    Repaint();
                }
                _revealSelectedGradient = false;
                _layerSettingsToReveal = null;
            }
            if (_recipe.layers.Count == 0) return;
            Action pending = null;
            if (ActiveLayer is Layer layer)
            {
                var blockedBy = BlockedClipBase(layer, ActiveIndex);
                if (blockedBy != null) DrawBlockedClip(layer, blockedBy, ref pending);
                if (!(layer is MixedMergeLayer)) DrawLayerMaskControls(layer, ref pending);
                DrawActiveLayerSettings(ref pending);
            }
            else GUILayout.Label(Loc.T("Click a layer in the list to change its settings."), StudioGUI.Hint);
            pending?.Invoke();
        }

        void DrawLayerList()
        {
            Action pending = null;
            if (!StudioGUI.BeginFoldGroup(ref _openLayers, Loc.T("Layers"), _recipe.layers.Count == 0 ? Loc.T("none yet") : _recipe.layers.Count.ToString(),
                    _recipe.layers.Count == 0 ? Loc.T("none yet") : Loc.T("top = in front"), Advanced ? Loc.T("The layer list and history.") : Loc.T("The layer list."), "layer"))
            {
                StudioGUI.EndGroup();
                return;
            }
            StudioGUI.Count(_recipe.layers.Count == 0 ? "layers.start" : "layers.list");
            DrawLayerCommands(ref pending);
            DrawCombineLayerActions(ref pending);
            var list = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(list, StudioGUI.ListBg);
            DrawGroupedLayerRows(ref pending);
            if (_recipe.layers.Count == 0)
            {
                GUILayout.Label(Loc.T("No layers yet. Your first brush stroke adds a Paint layer, or start with:"), StudioGUI.Hint);
                var startH = new[] { GUILayout.Height(20), GUILayout.MinWidth(0) };
                using (new EditorGUILayout.HorizontalScope())
                {
                    StudioGUI.Count("paint.gradientMap");
                    if (GUILayout.Button(new GUIContent(Loc.T(" Change colours"), StudioGUI.Icon("PreTextureRGB", "").image, Loc.T("Change colours, keep shading: adds a Gradient Map layer with new colours and the same shading")), EditorStyles.miniButtonLeft, startH))
                        pending = () => InsertLayer(new GradientMapLayer());
                    bool canMatch = CanAddColourMatchLayer(out var why);
                    var match = canMatch ? new GUIContent(Loc.T("Copy colours from…"), Loc.T("Take another material's colours, keeping this texture's detail"))
                        : new GUIContent(Loc.T("Pick a colour texture"), why);
                    if (GUILayout.Button(match, EditorStyles.miniButtonRight, startH))
                        pending = canMatch ? (Action)AddColourMatchLayer : () => SetTab(Tab.Target);
                }
                if (_selection.Count > 0)
                {
                    GUILayout.Label(Msg.M18IslandHint(_selection.Count), StudioGUI.Hint);
                    StudioGUI.Count("layers.affectEverything");
                    if (GUILayout.Button(Msg.BtnAffectEverything, EditorStyles.miniButton)) pending = () => AffectEverything();
                }
            }
            DrawBaseRow();
            EditorGUILayout.EndVertical();
            StudioGUI.EndGroup();
            pending?.Invoke();
        }

        void ShowAddLayerMenu() => BuildAddLayerMenu().ShowAsContext();

        GenericMenu BuildAddLayerMenu()
        {
            var menu = new GenericMenu();
            var preview = _comp;
            var targetLayers = _recipe.layers;
            void RunOnTarget(Action action)
            {
                if (this == null) return;
                if (_needsRebuild || preview != _comp || !ReferenceEquals(_recipe?.layers, targetLayers))
                {
                    Toast(Loc.T("The texture or edit has changed. Open Add layer again."), MessageType.Info);
                    return;
                }
                action();
            }

            void AddType(string label, Func<Layer> create, Layer sample)
            {
                var content = new GUIContent(NoSubmenu(LayerTypeLabel(label)));
                if (sample is ColourMatchLayer)
                {
                    if (CanAddColourMatchLayer(out var why))
                        menu.AddItem(content, false, () => RunOnTarget(() => AddLayerFromMenu(AddColourMatchLayer)));
                    else menu.AddDisabledItem(new GUIContent(Loc.T("Colour Match (colour textures only)"), why));
                    return;
                }

                menu.AddItem(content, false, () => RunOnTarget(() => AddLayerFromMenu(() =>
                {
                    InsertLayer(create());
                    if (sample is PaintLayer) SetTab(Tab.Paint);
                })));
            }

            var types = TextureRecipe.LayerTypes.Select(type => (type.label, type.create, sample: type.create())).ToArray();
            bool IsContent(Layer layer) => layer is PaintLayer || layer is DrawnGradientLayer || layer is ImageLayer;
            foreach (var (label, create, sample) in types.Where(type => IsContent(type.sample)))
                AddType(label, create, sample);
            menu.AddSeparator("");
            foreach (var (label, create, sample) in types.Where(type => !IsContent(type.sample)))
                if (Advanced || sample is GradientMapLayer || sample is HslLayer || sample is SolidColorLayer ||
                    sample is ColourMatchLayer || sample is BrightnessContrastLayer)
                    AddType(label, create, sample);

            if (Advanced)
            {
                menu.AddSeparator("");
                if (CurrentSlot != null && CurrentSlot.role == TextureRole.Color)
                {
                    Action bake = OnMapTarget(CurrentSlot, BakeAmbientOcclusion);
                    menu.AddItem(new GUIContent(Loc.T("Ambient occlusion (bake shading in creases)")), false, () => RunOnTarget(bake));
                }
                else menu.AddDisabledItem(new GUIContent(Loc.T("Ambient occlusion (colour textures only)")));
            }
            return menu;
        }

        void AddLayerFromMenu(Action add)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add layer");
            add();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        void RemoveLayerConfirmed(int i)
        {
            var layers = _recipe.layers;
            if (i < 0 || i >= layers.Count) return;
            var l = layers[i];
            if (l is MixedMergeLayer && !StudioDialog.Ask(Loc.T("Delete merged layer"),
                Loc.T("Delete the merged layer and its editable source layers? Ctrl+Z brings them back."), Msg.BtnDelete, Msg.BtnCancel)) return;
            if (l is PaintLayer p && p.strokes.Count > 0 &&
                !StudioDialog.Ask(Loc.T("Delete layer"), Loc.F("Delete “{0}” and its {1} brush stroke(s)?\n\nCtrl+Z brings it back.", l.name, p.strokes.Count), Msg.BtnDelete, Msg.BtnCancel))
                return;
            string below = i > 0 ? layers[i - 1]?.id : null;
            string above = i + 1 < layers.Count ? layers[i + 1]?.id : null;
            var renderer = _renderer;
            var material = _material;
            var texture = CurrentSlot?.texture;
            RemoveLayer(i);
            if (l is PaintLayer || l is MixedMergeLayer) return;
            Toast(Msg.M10Deleted(l.name), MessageType.Info, 6f, Loc.T("Restore layer"), () =>
            {
                if (_needsRebuild || _renderer != renderer || _material != material || CurrentSlot?.texture != texture ||
                    !ReferenceEquals(_recipe.layers, layers))
                {
                    Toast(Loc.T("This layer is no longer available to restore."), MessageType.Info);
                    return;
                }
                RestoreDeletedLayer(l, i, below, above);
            });
        }

        void RestoreDeletedLayer(Layer layer, int index, string below, string above)
        {
            var layers = _recipe.layers;
            if (layers.Exists(item => item?.id == layer.id)) return;
            EndStroke();
            int lower = below == null ? -1 : layers.FindIndex(item => item?.id == below);
            int upper = above == null ? -1 : layers.FindIndex(item => item?.id == above);
            int insertion = lower >= 0 ? lower + 1 : upper >= 0 ? upper : Mathf.Min(index, layers.Count);
            string undoName = Loc.T("Restore layer");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObject(_recipe, undoName);
            Undo.RecordObject(this, undoName);
            var belowLayer = insertion > 0 ? layers[insertion - 1] : null;
            var aboveLayer = insertion < layers.Count ? layers[insertion] : null;
            if (SameLayerGroup(belowLayer, aboveLayer)) CopyLayerGroup(belowLayer, layer);
            else if (!SameLayerGroup(layer, belowLayer) && !SameLayerGroup(layer, aboveLayer)) CopyLayerGroup(null, layer);
            layers.Insert(insertion, layer);
            _activeLayerId = layer.id;
            _openLayerSettings = true;
            MarkEdited();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        void DrawLayerRow(int i, int visualIndex, ref Action pending, float indent = 0f)
        {
            var l = _recipe.layers[i];
            if (l == null) return;
            var e = Event.current;
            var r = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            r.xMin += indent;
            bool active = l.id == _activeLayerId;
            if (e.type == EventType.Repaint)
            {
                if (active) EditorGUI.DrawRect(r, StudioGUI.RowActive);
                else if (visualIndex % 2 == 1) EditorGUI.DrawRect(r, StudioGUI.RowAlt);
            }

            var eye = new Rect(r.x + 4, r.y + 4, 18, 18);
            var chip = new Rect(eye.xMax + 4, r.y + 7, 12, 12);
            string fullInfo = l is MixedMergeLayer ? l.TypeName :
                (l is PaintLayer p ? Loc.F("{0} stroke{1}", p.strokes.Count, p.strokes.Count == 1 ? "" : "s") + " · " : "") + $"{l.blend} {l.opacity * 100f:0}%";
            var blockedBy = BlockedClipBase(l, i);
            string flags = (blockedBy != null ? "⚠ " : "") + (l.maskMode != MaskMode.WholeTexture ? "◧ " : "");
            bool compact = _panelContentWidth < 360f;
            string info = compact ? flags.Trim() : flags + fullInfo;
            bool isPart = l is RecolourPartLayer;
            float badgeW = isPart ? 32f : 0f;
            float maskW = l.hasPaintMask ? 24f : 0f;
            float infoW = info.Length == 0 ? 0f : Mathf.Min(StudioGUI.MiniRight.CalcSize(new GUIContent(info)).x + 4f, r.width * 0.5f);
            var nameRect = new Rect(chip.xMax + 6, r.y, Mathf.Max(20, r.xMax - infoW - badgeW - maskW - 8 - (chip.xMax + 6)), r.height);
            var badgeRect = new Rect(r.xMax - infoW - badgeW - maskW - 4, r.y + 6, badgeW - 4f, 14);
            var infoRect = new Rect(r.xMax - infoW - maskW - 4, r.y, infoW, r.height);
            var layer = l;
            var eyeIcon = StudioGUI.Icon(l.enabled ? "scenevis_visible_hover" : "scenevis_hidden_hover", l.enabled ? "" : "–", l.enabled ? Loc.T("Hide layer") : Loc.T("Show layer"));
            if (GUI.Button(eye, eyeIcon, GUIStyle.none))
                pending = () => { Undo.RecordObject(_recipe, "Toggle layer"); layer.enabled = !layer.enabled; MarkEdited(); };

            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(chip, StudioGUI.HeaderLine);
                EditorGUI.DrawRect(new Rect(chip.x + 1, chip.y + 1, chip.width - 2, chip.height - 2), StudioGUI.LayerChip(l));
            }
            string clipTip = blockedBy != null ? " · " + ClipBlockedText(blockedBy)
                : l.clipToBelow ? " · " + Loc.T("clipped to the layer below") : "";
            GUI.Label(nameRect, new GUIContent((l.clipToBelow ? "↳ " : "") + l.name,
                    l is MixedMergeLayer ? l.TypeName : l.TypeName + clipTip + "\n" + fullInfo + "\n" + Loc.T("Right-click: copy or paste settings")),
                l.enabled && blockedBy == null ? StudioGUI.RowLabel : StudioGUI.RowLabelDisabled);
            string scope = l.maskMode == MaskMode.SelectedFaces ? Loc.F("Limited to {0} face(s)", l.faces?.Count ?? 0)
                : l.maskMode == MaskMode.SelectedIslands ? Loc.F("Only shows on {0} piece(s)", l.islands.Count) : Loc.T("Whole texture");
            string infoTip = scope + (l.hasPaintMask ? " · " + Loc.T("has a painted layer mask") : "");
            if (info.Length > 0) GUI.Label(infoRect, new GUIContent(info, infoTip + clipTip + " · " + fullInfo), StudioGUI.MiniRight);
            if (isPart) DrawPartBadge(badgeRect);
            if (l.hasPaintMask) DrawLayerMaskShortcut(l, new Rect(r.xMax - 23, r.y + 4, 20, 18), ref pending);

            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                _activeLayerId = l.id;
                if (!(l is PaintLayer)) _openLayerSettings = true;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition))
            {
                _activeLayerId = l.id;
                e.Use();
                ShowLayerRowMenu(l);
                Repaint();
            }
        }

        const string LayerClipTypePref = "AvatarTextureStudio.LayerClip.Type";
        const string LayerClipJsonPref = "AvatarTextureStudio.LayerClip.Json";

        void ShowLayerRowMenu(Layer layer)
        {
            if (layer is MixedMergeLayer) return;
            string clipType = EditorPrefs.GetString(LayerClipTypePref, "");
            string clipJson = EditorPrefs.GetString(LayerClipJsonPref, "");
            bool hasClip = clipType.Length > 0 && clipJson.Length > 0;
            bool sameType = hasClip && clipType == layer.GetType().Name;
            string id = layer.id;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Loc.T("Copy settings"), Loc.T("Copy appearance settings to reuse on another layer of the same kind.")), false, () => CopyLayerSettings(id));
            if (sameType)
                menu.AddItem(new GUIContent(Loc.T("Paste settings"), Loc.T("Reuse the copied appearance. This layer keeps its placement, paint, masks, piece limits and channels.")), false, () => PasteLayerSettings(id));
            else
                menu.AddDisabledItem(new GUIContent(hasClip
                    ? Loc.F("Paste settings (copied a {0} layer; this is a {1} layer)", ClipTypeLabel(clipType), layer.TypeName)
                    : Loc.T("Paste settings (copy a layer's settings first)")));
            menu.ShowAsContext();
        }

        static string ClipTypeLabel(string typeName) => typeName.EndsWith("Layer") ? typeName.Substring(0, typeName.Length - 5) : typeName;

        void CopyLayerSettings(string id)
        {
            var layer = _recipe.layers.Find(x => x != null && x.id == id);
            if (layer == null) return;
            EditorPrefs.SetString(LayerClipTypePref, layer.GetType().Name);
            var copy = (Layer)Activator.CreateInstance(layer.GetType());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(layer), copy);
            copy.maskStrokes = new List<PaintStroke>();
            CopyLayerGroup(null, copy);
            if (copy is PaintLayer cp) cp.strokes = new List<PaintStroke>();
            string json = JsonUtility.ToJson(copy);
            EditorPrefs.SetString(LayerClipJsonPref, json);
            _advancedLayerSource = (json, _renderer != null ? _renderer.name : null);
            Toast(Loc.F("Copied the settings of “{0}”. Right-click another {1} layer to paste them.", layer.name, layer.TypeName), MessageType.Info, 4f);
        }

        void PasteLayerSettings(string id)
        {
            int i = _recipe.layers.FindIndex(x => x != null && x.id == id);
            if (i < 0) return;
            var target = _recipe.layers[i];
            string json = EditorPrefs.GetString(LayerClipJsonPref, "");
            if (EditorPrefs.GetString(LayerClipTypePref, "") != target.GetType().Name || json.Length == 0) return;
            var merged = ReadCopiedLayer(json, target.GetType());
            if (merged == null) { Toast(Loc.T("Couldn't read the copied settings. Copy them again."), MessageType.Warning, 5f); return; }
            ApplyCopiedLayerSettings(target, merged, true, true);
        }

        static Layer ReadCopiedLayer(string json, Type type)
        {
            if (string.IsNullOrEmpty(json) || type == null || type.IsAbstract || !type.IsSubclassOf(typeof(Layer))) return null;
            try { return JsonUtility.FromJson(json, type) as Layer; }
            catch (ArgumentException) { return null; }
        }

        void ApplyCopiedLayerSettings(Layer target, Layer copied, bool typeSettings, bool appearance)
        {
            if (target is MixedMergeLayer) return;
            int i = _recipe?.layers.IndexOf(target) ?? -1;
            if (_needsRebuild || i < 0 || target.GetType() != copied.GetType() || (!typeSettings && !appearance)) return;

            EndStroke();
            if (typeSettings) KeepLayerScope(target, copied);
            const string undoName = "Paste layer settings";
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObjects(new UnityEngine.Object[] { _recipe, this }, undoName);
            if (typeSettings)
            {
                if (!appearance)
                {
                    copied.opacity = target.opacity;
                    copied.blend = target.blend;
                    copied.additionalBlends = target.additionalBlends;
                }
                _recipe.layers[i] = copied;
            }
            else
            {
                target.opacity = copied.opacity;
                target.blend = copied.blend;
                target.additionalBlends = copied.additionalBlends;
            }
            _activeLayerId = target.id;
            MarkEdited();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Toast(Loc.F("Pasted the copied settings onto “{0}”. Ctrl+Z undoes it.", target.name), MessageType.Info, 4f);
        }

        static void KeepLayerScope(Layer target, Layer merged)
        {
            merged.id = target.id;
            merged.name = target.name;
            merged.enabled = target.enabled;
            merged.clipToBelow = target.clipToBelow;
            CopyLayerGroup(target, merged);
            merged.expanded = target.expanded;
            merged.maskMode = target.maskMode;
            merged.islands = target.islands;
            merged.faces = target.faces;
            merged.faceLayout = target.faceLayout;
            merged.invertMask = target.invertMask;
            merged.hasPaintMask = target.hasPaintMask;
            merged.paintMaskStartsHidden = target.paintMaskStartsHidden;
            merged.maskStrokes = target.maskStrokes;
            merged.chR = target.chR;
            merged.chG = target.chG;
            merged.chB = target.chB;
            merged.chA = target.chA;

            switch (target)
            {
                case PaintLayer paint:
                    ((PaintLayer)merged).strokes = paint.strokes;
                    break;
                case DrawnGradientLayer gradient:
                    var placed = (DrawnGradientLayer)merged;
                    placed.start = gradient.start;
                    placed.end = gradient.end;
                    placed.sceneProjection = gradient.sceneProjection;
                    placed.sceneStart = gradient.sceneStart;
                    placed.sceneEnd = gradient.sceneEnd;
                    placed.sceneNormal = gradient.sceneNormal;
                    placed.sceneAxis = gradient.sceneAxis;
                    placed.sceneAcross = gradient.sceneAcross;
                    break;
                case ImageLayer image:
                    var picture = (ImageLayer)merged;
                    picture.image = image.image;
                    picture.mask = image.mask;
                    picture.missingPackImage = image.missingPackImage;
                    picture.missingPackMask = image.missingPackMask;
                    picture.maskChannel = image.maskChannel;
                    picture.position = image.position;
                    picture.size = image.size;
                    picture.rotation = image.rotation;
                    picture.tiled = image.tiled;
                    picture.copy = image.copy;
                    picture.imageST = image.imageST;
                    picture.generated = image.generated;
                    picture.flattenedFrom = image.flattenedFrom;
                    picture.offFloats = image.offFloats;
                    picture.offKeywords = image.offKeywords;
                    break;
                case ColourMatchLayer match:
                    var sameSource = (ColourMatchLayer)merged;
                    sameSource.source = match.source;
                    sameSource.sourceTexture = match.sourceTexture;
                    sameSource.useTexture = match.useTexture;
                    sameSource.from = match.from;
                    sameSource.to = match.to;
                    break;
                case RecolourPartLayer part:
                    var recoloured = (RecolourPartLayer)merged;
                    bool modeChanged = recoloured.partMode != part.partMode;
                    bool tintChanged = recoloured.IsTint &&
                        (recoloured.tintColour != part.tintColour || !Mathf.Approximately(recoloured.tintStrength, part.tintStrength));
                    recoloured.modeChosen = part.modeChosen || modeChanged || tintChanged;
                    recoloured.autoTinted = !modeChanged && part.autoTinted;
                    recoloured.swatch = part.swatch;
                    break;
            }
        }

        static GUIStyle _partBadge;
        static GUIStyle PartBadgeStyle => _partBadge ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };

        static void DrawPartBadge(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(StudioGUI.InfoBlue.r, StudioGUI.InfoBlue.g, StudioGUI.InfoBlue.b, 0.28f));
                StudioGUI.DrawBorder(r, new Color(StudioGUI.InfoBlue.r, StudioGUI.InfoBlue.g, StudioGUI.InfoBlue.b, 0.7f), 1f);
            }
            GUI.Label(r, new GUIContent(Loc.T("part"), Loc.T("Made in 2 · Recolour › Pieces; edit it there")), PartBadgeStyle);
        }

        void EditPartInColour(RecolourPartLayer part)
        {
            if (part == null) return;
            SetTab(Tab.Colour);
            if (_tab != Tab.Colour) return;
            if (!ByPart) EnterByPart();
            SelectPart(part);
        }

        void DrawBaseRow()
        {
            var r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), StudioGUI.HeaderLine);
            var tex = CurrentSlot?.texture;
            if (tex != null) GUI.DrawTexture(new Rect(r.x + 26, r.y + 4, 14, 14), tex, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(r.x + 46, r.y, r.width - 50, r.height), new GUIContent(tex != null ? (_panelContentWidth < 360f ? Loc.F("{0}  (original)", tex.name) : Loc.F("{0}  (original, never changed)", tex.name)) : Loc.T("Original"), Loc.T("The source texture (original, never changed).")), StudioGUI.RowLabelDisabled);
        }

        [SerializeField] bool _foldLayerWhere;
        [SerializeField] string _layerLimitTargetId;
        [SerializeField] bool _layerLimitUsesFaces;

        void DrawActiveLayerSettings(ref Action pending)
        {
            int i = ActiveIndex;
            var layer = _recipe.layers[i];
            if (layer is MixedMergeLayer mixed)
            {
                DrawMixedMergeSettings(mixed, ref pending);
                return;
            }
            string settingsTitle = layer is DrawnGradientLayer ? Loc.T("Selected gradient") : Loc.F("{0} settings", layer.TypeName);
            if (!StudioGUI.BeginFoldGroup(ref _openLayerSettings, settingsTitle, layer.name, null, Loc.T("The settings of the layer selected in the list."), "layer"))
            {
                StudioGUI.EndGroup();
                return;
            }

            EditorGUI.BeginChangeCheck();
            string n = layer.name;
            InfoRow("layers.name", "layer", () => n = EditorGUILayout.DelayedTextField(Loc.T("Name"), layer.name));
            var b = layer.blend;
            float matchWidth = layer is ColourMatchLayer ? PanelContentWidth - (Advanced ? 0f : 20f) : -1f;
            InfoRow("layers.blend", "blend-mode", () => b = BlendPopup(new GUIContent(Loc.T("Blend"), Loc.T("How this layer mixes with what's below (Normal, Multiply, Color…)")), layer.blend, matchWidth));
            float o = layer.opacity;
            InfoRow("layers.opacity", "opacity", () => o = layer is ColourMatchLayer
                ? EditorGUILayout.Slider(new GUIContent(Loc.T("Match strength"), Loc.T("Mix this match with the layers below.")), layer.opacity, 0f, 1f, GUILayout.Width(matchWidth))
                : EditorGUILayout.Slider(Loc.T("Layer opacity"), layer.opacity, 0f, 1f));
            if (EditorGUI.EndChangeCheck())
            {
                if (layer is DrawnGradientLayer gradient && gradient.sceneProjection && o > layer.opacity)
                {
                    var candidate = gradient.CopyPlacement();
                    candidate.opacity = o;
                    if (!ValidatePlacedGradientAppearance(gradient, candidate)) o = layer.opacity;
                }
                if (layer.name != n || layer.blend != b || layer.opacity != o)
                {
                    Undo.RecordObject(_recipe, "Edit layer");
                    layer.name = n;
                    if (layer.blend != b) LearnLater(LearnEvents.LayerBlend);
                    if (!Mathf.Approximately(layer.opacity, o)) LearnLater(LearnEvents.LayerOpacity);
                    layer.blend = b;
                    layer.opacity = o;
                    MarkEdited();
                }
            }
            DrawAdditionalBlends(layer);

            StudioGUI.Divider();
            if (layer is RecolourPartLayer partLayer)
            {
                GUILayout.Label(Loc.T("This layer was made in 2 · Recolour › Pieces. Edit it there."), StudioGUI.Hint);
                StudioGUI.Count("layers.editPart");
                if (GUILayout.Button(new GUIContent(Loc.T("Edit in 2 · Recolour"), Loc.T("Opens 2 · Recolour › Pieces with this part chosen")), EditorStyles.miniButton))
                    pending = () => EditPartInColour(partLayer);
            }
            if (layer is PaintLayer pl)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Loc.F("{0} stroke(s) · Ctrl+Z undoes the last", pl.strokes.Count), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (_tool != Tool.Paint) StudioGUI.Count("layers.paintOn");
                    if (_tool != Tool.Paint && GUILayout.Button(StudioGUI.Icon("Grid.PaintTool", Loc.T(" Paint"), Loc.T("Paint on this layer (B)")), EditorStyles.miniButton))
                        pending = () => SetTab(Tab.Paint);
                }
                if (Advanced && pl.strokes.Count > 0) StudioGUI.Count("layers.clearPaint");
                if (Advanced && pl.strokes.Count > 0 && StudioGUI.DangerButton(new GUIContent(Loc.T("Clear all paint on this layer")), EditorStyles.miniButton))
                {
                    if (StudioDialog.Ask(Loc.T("Clear paint"), Loc.F("Remove all {0} stroke(s) from “{1}”?\n\nCtrl+Z brings them back.", pl.strokes.Count, pl.name), Loc.T("Clear"), Loc.T("Cancel")))
                        pending = () => { ClearPaint(pl); GUIUtility.ExitGUI(); };
                }
            }
            else if (layer is ColourMatchLayer cml) DrawColourMatchSettings(cml, ref pending);
            else if (layer is HslLayer hl) DrawHslSettings(hl, ref pending);
            else if (layer is DrawnGradientLayer gradient) DrawSelectedGradientSettings(gradient);
            else if (!(layer is RecolourPartLayer))
            {
                if (SelectedImage == layer)
                    GUILayout.Label(Loc.T("On the texture, drag the middle dot to move, a corner to resize, or the circle to rotate. Hold Shift to keep the proportions while resizing."), StudioGUI.Hint);
                StudioGUI.Count("layers.fields");
                DrawTypeSpecificFields(i, layer);
            }

            StudioGUI.Divider();
            var inUse = new List<string>();
            if (layer.maskMode == MaskMode.SelectedIslands) inUse.Add(layer.invertMask ? Loc.T("all but some pieces") : Loc.T("some pieces"));
            if (layer.maskMode == MaskMode.SelectedFaces) inUse.Add(layer.invertMask ? Loc.T("all but some faces") : Loc.T("some faces"));
            if (layer.clipToBelow) inUse.Add(Loc.T("clipped"));
            if (!layer.chR || !layer.chG || !layer.chB || layer.chA) inUse.Add(Loc.T("channels"));
            string title = inUse.Count == 0 ? StudioGUI.Label("layers.showsOn", "Limit this layer") : Loc.F("Limit this layer: {0}", string.Join(", ", inUse));
            InfoRow("layers.showsOn", "island", () => _foldLayerWhere = StudioGUI.Foldout(_foldLayerWhere, new GUIContent(title,
                Loc.T("Limit this layer to selected pieces or faces, or clip it to the layer below."))));
            if (_foldLayerWhere)
            {
                EditorGUI.BeginChangeCheck();
                StudioGUI.Count("layers.limit");
                GUILayout.Label(Loc.T("Shows on"), EditorStyles.miniLabel);
                var m = (MaskMode)WrapToolbar((int)layer.maskMode, new[]
                {
                    new GUIContent(Loc.T("Everything")),
                    new GUIContent(Loc.T("Some pieces")),
                    new GUIContent(Loc.T("Some faces")),
                }, EditorStyles.miniButton, 19f, PanelContentWidth);
                bool hasCurrentPieces = _selection.Exists(IsCurrentPiece);
                int savedPieceCount = layer.islands.Count(IsCurrentPiece);
                if (m == MaskMode.SelectedIslands && layer.maskMode != m && savedPieceCount == 0 && !hasCurrentPieces)
                {
                    m = layer.maskMode;
                    pending = () => PickPiecesForLayer(layer);
                }
                bool savedFaces = HasCurrentFaceLimit(layer);
                if (m == MaskMode.SelectedFaces && layer.maskMode != m && !savedFaces && !HasFaceSelection)
                {
                    m = layer.maskMode;
                    pending = () => PickFacesForLayer(layer);
                }
                bool inv = m == layer.maskMode ? layer.invertMask : m == MaskMode.SelectedIslands && LayerLimitExcludesSelection(layer);
                if (m == MaskMode.SelectedIslands)
                {
                    StudioGUI.Count("layers.useSelection");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Loc.F("{0} piece(s)", savedPieceCount), EditorStyles.miniLabel, GUILayout.MinWidth(10));
                        GUILayout.FlexibleSpace();
                        var use = hasCurrentPieces
                            ? new GUIContent(inv ? ExcludeSelectionLabel(false) : Loc.T("Use selection"), Loc.T("Replace with the pieces selected now"))
                            : new GUIContent(Loc.T("Pick pieces"));
                        if (GUILayout.Button(use, EditorStyles.miniButtonLeft, GUILayout.MinWidth(10)))
                            pending = () => UsePiecesForLayer(layer);
                        using (new EditorGUI.DisabledScope(savedPieceCount == 0))
                            if (GUILayout.Button(new GUIContent(Loc.T("Show them"), Loc.T("Select this layer's pieces")),
                                    EditorStyles.miniButtonRight, GUILayout.MinWidth(10)))
                                pending = () => SetSelection(layer.islands.Where(IsCurrentPiece));
                    }
                    StudioGUI.Count("layers.invertLimit");
                    inv = EditorGUILayout.Toggle(new GUIContent(Loc.T("Everything except them")), inv);
                    GUILayout.Label(inv ? Loc.T("This layer shows everywhere except on these pieces, whatever you paint.") : Loc.T("This layer only shows on these pieces, whatever you paint."), StudioGUI.Hint);
                }
                else if (m == MaskMode.SelectedFaces)
                {
                    StudioGUI.Count("layers.useSelection");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Loc.F("{0} face(s)", savedFaces ? layer.faces.Count : 0), EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        bool hasFaces = HasSelectionForLayerLimit(true);
                        if (GUILayout.Button(hasFaces ? (inv ? ExcludeSelectionLabel(true) : Loc.T("Use selection")) : Loc.T("Pick faces"),
                            EditorStyles.miniButtonLeft, GUILayout.MinWidth(10)))
                            pending = () =>
                            {
                                if (hasFaces) CaptureLayerLimitSelection(layer, true, LayerLimitExcludesSelection(layer));
                                else PickFacesForLayer(layer);
                            };
                        using (new EditorGUI.DisabledScope(!savedFaces))
                            if (GUILayout.Button(Loc.T("Show them"), EditorStyles.miniButtonRight))
                                pending = () => ApplyFaceSelection(layer.faces, SelectOp.Replace);
                    }
                    if (!savedFaces) GUILayout.Label(Loc.T("This face limit does not match the current UV mesh. Pick new faces or choose Everything."), StudioGUI.Hint);
                    StudioGUI.Count("layers.invertLimit");
                    inv = EditorGUILayout.ToggleLeft(Loc.T("Everything except them"), inv);
                }
                if (EditorGUI.EndChangeCheck() && (m != layer.maskMode || inv != layer.invertMask))
                {
                    bool useSelection = m != layer.maskMode && (m == MaskMode.SelectedIslands && savedPieceCount == 0 ||
                        m == MaskMode.SelectedFaces && !savedFaces);
                    if (TryPlanLayerLimit(layer, m, inv, useSelection, out var apply))
                        CommitLayerLimit(layer, apply, "Edit layer");
                }
                if (Advanced)
                {
                    DrawClipControls(i, layer, ref pending);
                    DrawChannelSwitches(layer, ref pending);
                }
            }
            else if (Advanced && CurrentSlot != null && CurrentSlot.role == TextureRole.Mask)
                DrawChannelSwitches(layer, ref pending);
            StudioGUI.EndGroup();
        }

        void UsePiecesForLayer(Layer layer)
        {
            if (HasSelectionForLayerLimit(false)) CaptureLayerLimitSelection(layer, false, LayerLimitExcludesSelection(layer));
            else PickPiecesForLayer(layer);
        }

        bool IsCurrentPiece(int id) => _uv != null && id >= 0 && id < _uv.islandCount;

        void PickPiecesForLayer(Layer layer) => PickLayerLimit(layer, FaceSelectionTool ? SelectMode.Click : _selectMode);

        void PickFacesForLayer(Layer layer) => PickLayerLimit(layer, SelectMode.Face);

        void PickLayerLimit(Layer layer, SelectMode mode)
        {
            if (_uv == null)
            {
                Toast(Loc.T("This part has no UV data, so pieces can't be selected."), MessageType.Info);
                return;
            }
            if (_needsRebuild || _recipe == null || !_recipe.layers.Contains(layer)) return;
            bool faces = mode == SelectMode.Face;
            ChangeSceneBar(faces ? Loc.T("Pick faces") : Loc.T("Pick pieces"), () =>
            {
                SetTab(Tab.Select);
                if (_tab != Tab.Select) return;
                _openSelectionTools = true;
                _layerLimitTargetId = layer.id;
                _layerLimitUsesFaces = faces;
                SetSelectMode(mode);
            });
        }

        void OpenPaintLayers()
        {
            SetTab(Tab.Paint);
            if (_tab != Tab.Paint) return;
            _openLayers = true;
            _revealLayerList = true;
        }

        Layer ClipBaseOf(int i)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                var l = _recipe.layers[j];
                if (l != null && !l.clipToBelow) return l;
            }
            return null;
        }

        bool ClipBaseShowsNothing(Layer layer)
        {
            if (!layer.enabled || layer.opacity <= 0f || layer is ImageLayer image && image.image == null) return true;
            string liveLayerId = _comp?.LiveStroke != null ? _comp.LiveLayerId : null;
            if (layer is PaintLayer paint && (paint.strokes?.Count ?? 0) == 0 && liveLayerId != layer.id) return true;
            if (layer.hasPaintMask && layer.paintMaskStartsHidden && (layer.maskStrokes?.Count ?? 0) == 0 &&
                liveLayerId != Compositor.MaskKey(layer.id)) return true;
            if (layer.maskMode == MaskMode.SelectedFaces && !HasCurrentFaceLimit(layer)) return true;
            return _uv != null && layer.maskMode == MaskMode.SelectedIslands && !layer.invertMask && layer.islands?.Exists(IsCurrentPiece) != true;
        }

        Layer BlockedClipBase(Layer layer, int i)
        {
            if (!layer.enabled || layer.opacity <= 0f || !layer.clipToBelow) return null;
            var clipBase = ClipBaseOf(i);
            return clipBase != null && ClipBaseShowsNothing(clipBase) ? clipBase : null;
        }

        static string ClipBlockedText(Layer clipBase) => Loc.F("“{0}” currently shows nothing, so this clipped layer is hidden.", clipBase.name);

        void DrawBlockedClip(Layer layer, Layer clipBase, ref Action pending)
        {
            StudioGUI.CheckRow(StudioGUI.Check.Problem, ClipBlockedText(clipBase));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var label = new GUIContent(Loc.T("Release clipping"));
                var rect = GUILayoutUtility.GetRect(label, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
                StudioGUI.Count("layers.releaseClipping", rect);
                if (GUI.Button(rect, label, EditorStyles.miniButton)) pending = () => SetClipToBelow(layer, false);
            }
        }

        List<MaterialWriter.ImportIssue> _issuesSource, _issuesShown;
        bool _issuesAdvanced;
        int _issuesFixable;

        List<MaterialWriter.ImportIssue> VisibleImportIssues(TextureSlot slot, out int fixable)
        {
            var src = MaterialWriter.ImportIssues(slot);
            if (!ReferenceEquals(src, _issuesSource) || _issuesAdvanced != Advanced || _issuesShown == null)
            {
                _issuesSource = src;
                _issuesAdvanced = Advanced;
                _issuesShown = src.Where(x => !x.advancedOnly || Advanced).ToList();
                _issuesFixable = _issuesShown.Count(x => x.fix != null);
            }
            fixable = _issuesFixable;
            return _issuesShown;
        }

        static readonly BlendMode[] SimpleBlendModes ={ BlendMode.Normal, BlendMode.Multiply, BlendMode.Color };
        static readonly Dictionary<BlendMode, (BlendMode[] modes, string[] names)> s_simpleBlendLists = new Dictionary<BlendMode, (BlendMode[], string[])>();

        static BlendMode BlendPopup(GUIContent label, BlendMode current, float width = -1f)
        {
            if (StudioMode.Advanced)
                return SteppedChoice(label, current, AllBlendModes, (rect, value) => (BlendMode)EditorGUI.EnumPopup(rect, value), width);
            if (!s_simpleBlendLists.TryGetValue(current, out var lists))
            {
                var modes = new List<BlendMode>(SimpleBlendModes);
                if (!modes.Contains(current)) modes.Add(current);
                lists = (modes.ToArray(), modes.Select(m => ObjectNames.NicifyVariableName(m.ToString())).ToArray());
                s_simpleBlendLists[current] = lists;
            }
            return SteppedChoice(label, current, lists.modes,
                (rect, value) => lists.modes[EditorGUI.Popup(rect, Array.IndexOf(lists.modes, value), lists.names)], width);
        }

        void DrawClipControls(int i, Layer layer, ref Action pending)
        {
            GUILayout.Space(2);
            var baseLayer = ClipBaseOf(i);
            StudioGUI.Count("layers.clip");
            using (new EditorGUI.DisabledScope(baseLayer == null && !layer.clipToBelow))
            {
                bool clip = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Clip to layer below"),
                    Loc.T("Only show this layer where the layer under it shows. Use it to shade or add highlights inside a base colour without going over its edges.")), layer.clipToBelow);
                if (clip != layer.clipToBelow) pending = () => SetClipToBelow(layer, clip);
            }
            if (layer.clipToBelow && BlockedClipBase(layer, i) == null)
                GUILayout.Label(baseLayer == null ? Loc.T("Nothing below to clip to, so it's shown normally.")
                    : Loc.F("Shows only where “{0}” shows.", baseLayer.name), StudioGUI.Hint);
            else if (baseLayer == null)
                GUILayout.Label(Loc.T("The bottom layer can't be clipped: there's nothing below it."), StudioGUI.Hint);
        }

        void DrawLayerMaskControls(Layer layer, ref Action pending)
        {
            if (!StudioGUI.BeginFoldGroup(ref _openLayerMask, Loc.T("Hide/reveal mask"), infoId: "mask"))
            {
                StudioGUI.EndGroup();
                return;
            }
            bool editing = MaskEditing && _maskEditLayerId == layer.id;
            var action = !layer.hasPaintMask ? new GUIContent(StudioGUI.Label("masks.add", "Add mask"), Loc.T("Add a mask you paint with the brush"))
                : new GUIContent(editing ? Loc.T("Painting…") : Loc.T("Paint mask"),
                    editing ? Loc.T("Stop painting the mask") : Loc.T("Paint on this layer's mask (Reveal / Hide)"));
            using (new EditorGUILayout.HorizontalScope())
            {
                var style = layer.hasPaintMask ? EditorStyles.miniButtonLeft : EditorStyles.miniPullDown;
                var button = GUILayoutUtility.GetRect(action, style, GUILayout.ExpandWidth(true));
                StudioGUI.Count("layers.hideMask", button);
                var oldBg = GUI.backgroundColor;
                if (editing) GUI.backgroundColor = StudioGUI.Accent;
                bool pressed = GUI.Button(button, action, style);
                GUI.backgroundColor = oldBg;
                if (pressed)
                {
                    if (layer.hasPaintMask) pending = () => SetMaskEditing(layer, !editing);
                    else ShowAddLayerMaskMenu(layer);
                }
                if (layer.hasPaintMask)
                {
                    if (GUILayout.Button(new GUIContent(Loc.T("Invert"), Loc.T("Swap hidden and shown")), EditorStyles.miniButtonMid)) pending = () => InvertPaintMask(layer);
                    if (StudioGUI.DangerButton(new GUIContent(Loc.T("Delete"), Loc.T("Remove the mask: the whole layer shows again")), EditorStyles.miniButtonRight))
                    {
                        if (layer.maskStrokes.Count == 0 || StudioDialog.Ask(Loc.T("Delete layer mask"),
                                Loc.F("Delete the mask of “{0}” ({1} stroke(s))? The whole layer will show again.\n\nCtrl+Z brings it back.", layer.name, layer.maskStrokes.Count), Loc.T("Delete"), Loc.T("Cancel")))
                            pending = () => { DeletePaintMask(layer); GUIUtility.ExitGUI(); };
                    }
                }
            }
            if (layer.hasPaintMask)
                GUILayout.Label(layer.paintMaskStartsHidden ? Loc.F("{0} mask stroke(s) · started as hide everything.", layer.maskStrokes.Count) : Loc.F("{0} mask stroke(s) · started as show everything.", layer.maskStrokes.Count), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        static GUIStyle s_layerMaskShortcutStyle;

        void DrawLayerMaskShortcut(Layer layer, Rect rect, ref Action pending)
        {
            bool editing = MaskEditing && _maskEditLayerId == layer.id;
            var before = GUI.backgroundColor;
            if (editing) GUI.backgroundColor = StudioGUI.Accent;
            var tooltip = (editing ? Loc.T("Stop painting the mask") : Loc.T("Paint mask")) + "\n" + layer.name;
            s_layerMaskShortcutStyle ??= new GUIStyle(EditorStyles.miniButton)
                { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            bool pressed = GUI.Button(rect, new GUIContent("◐", tooltip), s_layerMaskShortcutStyle);
            GUI.backgroundColor = before;
            StudioGUI.Count("layers.maskShortcut", rect);
            if (pressed) pending = () => SetMaskEditing(layer, !editing);
        }

        void ShowAddLayerMaskMenu(Layer layer)
        {
            var preview = _comp;
            void AddMask(bool startHidden)
            {
                if (this != null && _comp == preview && !layer.hasPaintMask) BeginMaskEditing(layer, startHidden);
            }
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Loc.T("Show everything (paint to hide parts)")), false, () => AddMask(false));
            menu.AddItem(new GUIContent(Loc.T("Hide everything (paint to reveal parts)")), false, () => AddMask(true));
            menu.ShowAsContext();
        }

        SerializedObject _fieldsSo;

        void DrawTypeSpecificFields(int i, Layer layer)
        {
            if (layer is ImageLayer image)
            {
                string recover = Loc.T("Choose the missing artwork to enable this layer.");
                if (image.image == null && !string.IsNullOrEmpty(image.missingPackImage))
                    EditorGUILayout.HelpBox(Loc.F("“{0}” needs its image “{1}”. {2}", layer.name, image.missingPackImage, recover), MessageType.Warning);
                if (image.NeedsPackMask)
                    EditorGUILayout.HelpBox(Loc.F("“{0}” needs its mask “{1}”. {2}", layer.name, image.missingPackMask, recover), MessageType.Warning);
            }
            if (_fieldsSo == null || _fieldsSo.targetObject == null || _fieldsSo.targetObject != _recipe)
            {
                _fieldsSo?.Dispose();
                _fieldsSo = new SerializedObject(_recipe);
            }
            var so = _fieldsSo;
            so.Update();
            var lp = so.FindProperty("layers").GetArrayElementAtIndex(i);
            var it = lp.Copy();
            var end = lp.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (CommonLayerFields.Contains(it.name)) continue;
                if (it.propertyType == SerializedPropertyType.Gradient)
                {
                    var ramp = DrawGradientRamp(CopyRamp(it.gradientValue), false, preset => ApplyLayerGradientPreset(layer, preset));
                    if (ramp != null) it.gradientValue = ramp;
                }
                else EditorGUILayout.PropertyField(it, true);
            }
            if (layer is RootTipGradientLayer)
                GUILayout.Label(Loc.T("Transparent gradient keys leave the texture untouched."), StudioGUI.Hint);
            if (IsAoLayer(layer)) DrawAoLayerNote();
            if (so.ApplyModifiedProperties()) MarkEdited();
        }

        string _sharedKey;
        int _sharedOutside;
        int _sharedStamp = -1; double _sharedTime;
        static int _envStamp;
        static readonly List<Material> _matScratch = new List<Material>();

        [InitializeOnLoadMethod]
        static void HookEnvStamp()
        {
            EditorApplication.hierarchyChanged += () => _envStamp++;
            EditorApplication.projectChanged += () => _envStamp++;
            Undo.undoRedoPerformed += () => _envStamp++;
        }

        int MeshesOutsideAvatarUsingMaterial()
        {
            string key = (_material != null ? _material.GetInstanceID() : 0) + ":" + (_renderer != null ? _renderer.GetInstanceID() : 0);
            if (key != _sharedKey || _sharedStamp != _envStamp || EditorApplication.timeSinceStartup - _sharedTime > 5.0)
            {
                _sharedKey = key;
                _sharedStamp = _envStamp; _sharedTime = EditorApplication.timeSinceStartup;
                _sharedOutside = 0;
                if (_material != null && _renderer != null)
                {
                    var root = TargetResolver.AvatarRoot(_renderer);
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                    {
                        if (r == null || r.transform.root == root) continue;
                        r.GetSharedMaterials(_matScratch);
                        if (_matScratch.Contains(_material)) _sharedOutside++;
                    }
                }
            }
            return _sharedOutside;
        }

        string SharedMaterialNote()
        {
            if (_material == null || _outputMode == OutputMode.PngOnly) return null;
            var others = _users.Where(u => u.renderer != null && u.renderer != _renderer).Select(u => u.renderer.name).Distinct().ToList();
            string who = others.Count == 0 ? null : (others.Count <= 3 ? string.Join(", ", others) : Loc.F("{0} and {1} more", string.Join(", ", others.Take(3)), others.Count - 3));
            if (_outputMode == OutputMode.DuplicateMaterial)
            {
                if (who == null) return null;
                return Loc.F("“{0}” is also used by {1} on this avatar. They switch to the new material copy too, so they stay in step. Other avatars keep the original.", _material.name, who);
            }
            int outside = MeshesOutsideAvatarUsingMaterial();
            return Msg.M5SharedMaterial(_material.name, _renderer != null ? _renderer.name : "", others.Count + outside);
        }

        static void NoteWithFix(StudioGUI.Check state, string text, string buttonLabel, Action fix, string controlId)
        {
            StudioGUI.CheckRow(state, text);
            StudioGUI.Count(controlId);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(buttonLabel, EditorStyles.miniButton)) fix();
            }
        }

        string _predKey;
        int _predStamp = -1;
        Exporter.Result _pred;
        bool _revealLayerList;

        Exporter.Result PredictedPaths(TextureSlot slot)
        {
            string key = _outputMode + "|" + slot.assetPath + "|" + slot.texture?.GetInstanceID() + "|" + _material.GetInstanceID();
            if (key != _predKey || _predStamp != _envStamp)
            {
                _predKey = key; _predStamp = _envStamp;
                _pred = Exporter.PredictPaths(slot, _material, _outputMode);
            }
            return _pred;
        }

        static bool IsVisibleForSave(Layer layer) => layer != null && layer.enabled && layer.opacity > 0.001f &&
            (layer is not ImageLayer image || image.image != null && !image.NeedsPackMask);

        void DrawSaveTab()
        {
            var slot = CurrentSlot;
            bool hasLayers = _recipe.layers.Count > 0;
            int visible = 0;
            foreach (var l in _recipe.layers) if (IsVisibleForSave(l)) visible++;
            bool saved = CurrentSaveState == SaveState.Saved;
            bool justSavedEmpty = saved && !hasLayers;

            if (saved)
            {
                var files = LastBakeFiles;
                bool missingMaterial = !string.IsNullOrEmpty(_lastBakeMaterial) && files.material == null;
                if (StudioGUI.BeginFoldGroup(ref _openSaveResult, Loc.T("Saved"), files.png.name))
                {
                    StudioGUI.CheckRow(StudioGUI.Check.Ok, missingMaterial ? Loc.F("Saved {0}.png.", files.png.name)
                        : Msg.M1Saved(files.png.name, !_lastBakeClearedLayers));
                    if (missingMaterial)
                        StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Saved material file is missing. The PNG is still available."));
                    if (!string.IsNullOrEmpty(_lastBakeRecipe) && files.edit == null)
                        StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("Saved edit file is missing. The PNG is still available."));
                    if (_lastBakeClearedLayers && !hasLayers)
                        GUILayout.Label(Loc.T("Your layers are now part of that texture, so the list starts empty. Ctrl+Z brings them back."), StudioGUI.Hint);
                    DrawSavedFileActions(files.material != null, files.edit != null);
                    if (justSavedEmpty)
                    {
                        bool continueEditing = StudioGUI.PrimaryButton(new GUIContent(Loc.T("Continue editing")), StudioGUI.InfoBlue);
                        StudioGUI.Count("save.continueEditing", GUILayoutUtility.GetLastRect());
                        if (continueEditing) SetTab(Tab.Paint);
                        DrawBrowseDraftsButton();
                    }
                }
                StudioGUI.EndGroup();
                if (justSavedEmpty)
                {
                    if (Advanced) DrawStylePackControls();
                    return;
                }
            }

            if (StudioGUI.BeginFoldGroup(ref _openSaveReview, Loc.T("Before you save"), infoId: "bake"))
            {
                StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("Editing <b>{0}</b> ({1}).", slot?.texture.name, TextureSlot.RoleLabel(slot?.role ?? TextureRole.Color)));
                if (!hasLayers) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("No edits yet. Paint in 4 · Paint, or add a layer there, first."));
                else if (visible == 0) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("All layers are hidden or at 0% opacity, so the result would look unchanged."));
                else StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("{0} visible layer{1}.", visible, (visible == 1 ? "" : "s")));
                if (_editThatMadeTexture != null)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This texture is already an edited copy; the new PNG will include both edits."));
                if (visible == 0) DrawSavePrimaryAction(visible, hasLayers);
            }
            StudioGUI.EndGroup();

            if (StudioGUI.BeginFoldGroup(ref _openSaveOutput, Loc.T("Save as"), infoId: "png"))
                DrawSaveOutput(slot, hasLayers, visible);
            StudioGUI.EndGroup();

            DrawSaveDraftGroup(hasLayers);
            if (Advanced) DrawStylePackControls();
        }

        void DrawSaveOutput(TextureSlot slot, bool hasLayers, int visible)
        {
            var outputChoices = new[]
            {
                new GUIContent(Loc.T("PNG + material copy"), Loc.T("Creates a new PNG and a copy of the material, and puts that copy on this avatar. The original material and texture stay untouched.")),
                new GUIContent(Loc.T("PNG + update material"), Loc.T("Creates a new PNG and points the existing material at it. Every part, avatar and prefab that uses that material changes too.")),
                new GUIContent(Loc.T("PNG only"), Loc.T("Just creates the PNG; no material and nothing on the avatar changes.")),
            };
            var outputMode = _outputMode;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!Advanced && _outputMode == OutputMode.DuplicateMaterial)
                    GUILayout.Label(outputChoices[(int)_outputMode].text, StudioGUI.CheckLabel);
                else outputMode = SaveAsPopup(outputChoices, _outputMode);
                StudioGUI.Count("save.saveAs", GUILayoutUtility.GetLastRect());
                StudioGUI.Info("png");
            }
            if (outputMode != _outputMode)
            {
                Undo.RecordObject(this, "Change save mode");
                _outputMode = outputMode;
            }
            GUILayout.Label(outputChoices[(int)_outputMode].tooltip, StudioGUI.Hint);
            var sharedNote = SharedMaterialNote();
            if (sharedNote != null)
            {
                if (_outputMode == OutputMode.UpdateOriginalMaterial) NoteWithFix(StudioGUI.Check.Note, sharedNote, Msg.BtnUseCopy, UseCopyInstead, "save.useCopyShared");
                else StudioGUI.CheckRow(StudioGUI.Check.Note, sharedNote);
            }
            if (_outputMode == OutputMode.PngOnly)
                NoteWithFix(StudioGUI.Check.Note, Msg.M6ImageOnly(), Msg.BtnUseCopy, UseCopyInstead, "save.useCopyImageOnly");
            if (Advanced)
            {
                StudioGUI.Count("save.options");
                _foldHelp = StudioGUI.Foldout(_foldHelp, new GUIContent(Loc.T("Quality")));
                if (_foldHelp)
                {
                    StudioGUI.Count("save.dither");
                    _dither = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Dither (prevents banding in smooth gradients)")), _dither);
                }
            }
            if (visible > 0) DrawSavePrimaryAction(visible, hasLayers);
            if (hasLayers && slot != null && _material != null)
            {
                var p = PredictedPaths(slot);
                GUILayout.Label(Loc.F("Creates: {0}", System.IO.Path.GetFileName(p.pngPath)) +
                                (p.materialPath != null ? $", {System.IO.Path.GetFileName(p.materialPath)}" : "") +
                                $", {System.IO.Path.GetFileName(p.recipePath)}", StudioGUI.Hint);
            }
        }

        void DrawSavedFileActions(bool hasMaterial, bool hasEdit)
        {
            var file = new GUIContent(Msg.BtnShowFile);
            var material = new GUIContent(Loc.T("Show material"));
            var edit = new GUIContent(Loc.T("Show saved edit"), Loc.T("The layers that made this PNG, to reopen later"));
            var style = EditorStyles.miniButton;
            float rowWidth = style.CalcSize(file).x + 4f;
            if (hasMaterial) rowWidth += style.CalcSize(material).x + 4f;
            if (hasEdit) rowWidth += style.CalcSize(edit).x + 4f;
            using (IDisposable scope = rowWidth <= PanelContentWidth ? (IDisposable)new EditorGUILayout.HorizontalScope() : new EditorGUILayout.VerticalScope())
            {
                bool showFile = GUILayout.Button(file, style);
                StudioGUI.Count("save.showPng", GUILayoutUtility.GetLastRect());
                if (showFile) RevealLastBakeFile<Texture2D>(_lastBakePng, _lastBakePngGuid);
                if (hasMaterial)
                {
                    bool showMaterial = GUILayout.Button(material, style);
                    StudioGUI.Count("save.showMaterial", GUILayoutUtility.GetLastRect());
                    if (showMaterial) RevealLastBakeFile<Material>(_lastBakeMaterial, _lastBakeMaterialGuid);
                }
                if (hasEdit)
                {
                    bool showEdit = GUILayout.Button(edit, style);
                    StudioGUI.Count("save.showEdit", GUILayoutUtility.GetLastRect());
                    if (showEdit) RevealLastBakeFile<TextureRecipe>(_lastBakeRecipe, _lastBakeRecipeGuid);
                }
            }
        }

        void DrawSavePrimaryAction(int visible, bool hasLayers)
        {
            GUILayout.Space(4);
            string action = visible > 0 ? SaveActionLabel(_outputMode) : hasLayers ? Loc.T("Review layers") : Loc.T("Go to Paint");
            string actionId = visible > 0 ? "save.save" : hasLayers ? "save.reviewLayers" : "save.goToPaint";
            using (new EditorGUILayout.HorizontalScope())
            {
                bool pressed = StudioGUI.PrimaryButton(new GUIContent(action),
                    visible > 0 ? StudioGUI.BakeButton : StudioGUI.InfoBlue, 34f);
                StudioGUI.Count(actionId, GUILayoutUtility.GetLastRect());
                if (pressed)
                {
                    if (visible > 0)
                    {
                        RunBake(true);
                        GUIUtility.ExitGUI();
                    }
                    else
                    {
                        if (hasLayers) OpenPaintLayers();
                        else SetTab(Tab.Paint);
                    }
                }
                StudioGUI.Info("png");
            }
        }

        static string SaveActionLabel(OutputMode mode) => mode switch
        {
            OutputMode.DuplicateMaterial => Loc.T("Save PNG and material copy"),
            OutputMode.UpdateOriginalMaterial => Loc.T("Save PNG and update material"),
            _ => Loc.T("Save as new PNG"),
        };

        static OutputMode SaveAsPopup(GUIContent[] all, OutputMode current)
        {
            var modes = StudioMode.Advanced ? (OutputMode[])Enum.GetValues(typeof(OutputMode)) : new[] { OutputMode.DuplicateMaterial, current };
            var labels = modes.Select(m => all[(int)m]).ToArray();
            return SteppedChoice(GUIContent.none, current, modes, (rect, mode) => modes[EditorGUI.Popup(rect, Array.IndexOf(modes, mode), labels)]);
        }

        static void Ping(string path)
        {
            var o = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (o != null) { EditorGUIUtility.PingObject(o); Selection.activeObject = o; Learn.Did(LearnEvents.AssetPinged); }
        }

        sealed class ShortcutsPopup : PopupWindowContent
        {
            static readonly (string key, string what)[] Shortcuts =
            {
                ("S", "Select tool"),
                ("B / E", "Brush / eraser"),
                ("G / Shift+G", "Fill / gradient"),
                ("J", "Smear / Soften (blur / smudge)"),
                ("I  or  Ctrl+click", "Pick a colour"),
                ("X", "Swap main / second colour"),
                ("[  ]", "Brush size"),
                ("Shift / Ctrl+click", "Add / remove pieces"),
                ("Wheel", "Zoom the texture"),
                ("Right or middle drag", "Pan the texture"),
                ("F", "Fit the texture in view"),
                ("Esc", "Stop painting in the Scene view"),
                ("Ctrl+Z", "Undo (strokes, layers, saves to the avatar)"),
            };

            static string ShortcutText(string s) => s switch
            {
                "Select tool" => Loc.T("Select tool"),
                "Brush / eraser" => Loc.T("Brush / eraser"),
                "Fill / gradient" => Loc.T("Fill / gradient"),
                "Smear / Soften (blur / smudge)" => Loc.T("Smear / Soften (blur / smudge)"),
                "I  or  Ctrl+click" => Loc.T("I  or  Ctrl+click"),
                "Pick a colour" => Loc.T("Pick a colour"),
                "Swap main / second colour" => Loc.T("Swap main / second colour"),
                "Brush size" => Loc.T("Brush size"),
                "Add / remove pieces" => Loc.T("Add / remove pieces"),
                "Wheel" => Loc.T("Wheel"),
                "Zoom the texture" => Loc.T("Zoom the texture"),
                "Right or middle drag" => Loc.T("Right or middle drag"),
                "Pan the texture" => Loc.T("Pan the texture"),
                "Fit the texture in view" => Loc.T("Fit the texture in view"),
                "Stop painting in the Scene view" => Loc.T("Stop painting in the Scene view"),
                "Undo (strokes, layers, saves to the avatar)" => Loc.T("Undo (strokes, layers, saves to the avatar)"),
                _ => s,
            };

            public override Vector2 GetWindowSize() => new Vector2(320, 24 + Shortcuts.Length * 18 + 112);

            public override void OnGUI(Rect rect)
            {
                GUILayout.Label(Loc.T("Shortcuts"), EditorStyles.boldLabel);
                foreach (var (key, what) in Shortcuts)
                {
                    var r = EditorGUILayout.GetControlRect(false, 16);
                    GUI.Label(new Rect(r.x, r.y, 130, r.height), ShortcutText(key), EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(r.x + 130, r.y, r.width - 130, r.height), ShortcutText(what), EditorStyles.miniLabel);
                }
                GUILayout.Space(6);
                GUILayout.Label(Loc.T("B, E, G, Shift+G and J also work in the Scene view while Paint on model is on."), StudioGUI.Hint);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Loc.F("Avistry {0}  ·  by Avistry contributors", Version), EditorStyles.centeredGreyMiniLabel);
            }
        }
    }
}
