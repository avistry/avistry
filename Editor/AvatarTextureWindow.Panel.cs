using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    // Side panel, organised as workflow steps (tabs): 1 Target · 2 Select · 3 Paint · 4 Layers · 5 Save.
    // Fixed at the top: the tab bar (with progress marks) and a context strip that always says what you're editing.
    // Fixed at the bottom: the next step. Each tab has one highlighted action and explains anything that's disabled.
    public partial class AvatarTextureWindow
    {
        static readonly HashSet<string> CommonLayerFields = new HashSet<string>
            { "id", "name", "enabled", "expanded", "opacity", "blend", "maskMode", "islands", "invertMask", "strokes",
              "clipToBelow", "hasPaintMask", "paintMaskStartsHidden", "maskStrokes", "chR", "chG", "chB", "chA" };

        // Foldable groups of 4 · Paint (open by default, remembered)
        [SerializeField] bool _openBrush = true, _openLayers = true, _openColour = true, _openPaintOptions, _openLayerSettings;

        static readonly string[] FavouriteBrushes = { "Soft round", "Hard round", "Airbrush", "Pencil", "Bristle" };

        /// <summary>Popup items treat '/' as a submenu separator; swap it for a look-alike.</summary>
        static string NoSubmenu(string s) => s.Replace("/", "∕");

        // Preset names are keys saved in recipes, so they are translated only where they are shown.
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
            _ => name,
        };

        static string LayerTypeLabel(string label) => label switch
        {
            "Paint (brush strokes)" => Loc.T("Paint (brush strokes)"),
            "Gradient Map (recolour, keeps shading)" => Loc.T("Gradient Map (recolour, keeps shading)"),
            "Colour Match (take another material's colours, keeps detail)" => Loc.T("Colour Match (take another material's colours, keeps detail)"),
            "Hue / Saturation / Lightness" => Loc.T("Hue / Saturation / Lightness"),
            "Recolour part (hue on some pieces, like 2 · Recolour › One piece at a time)" => Loc.T("Recolour part (hue on some pieces, like 2 · Recolour › One piece at a time)"),
            "Ombré Gradient (top → bottom)" => Loc.T("Ombré Gradient (top → bottom)"),
            "Gradient (line, circle or ellipse; drag it with the Gradient tool)" => Loc.T("Gradient (line, circle or ellipse; drag it with the Gradient tool)"),
            "Image ∕ sticker (place a picture on the texture)" => Loc.T("Image ∕ sticker (place a picture on the texture)"),
            "Solid Colour (tint)" => Loc.T("Solid Colour (tint)"),
            "Levels" => Loc.T("Levels"),
            "Correction/Brightness ∕ Contrast" => Loc.T("Correction") + "/" + Loc.T("Brightness ∕ Contrast"),
            "Correction/Colour Balance" => Loc.T("Correction") + "/" + Loc.T("Colour Balance"),
            "Correction/Tone Curve" => Loc.T("Correction") + "/" + Loc.T("Tone Curve"),
            "Correction/Posterization" => Loc.T("Correction") + "/" + Loc.T("Posterization"),
            "Correction/Invert colours" => Loc.T("Correction") + "/" + Loc.T("Invert colours"),
            "Correction/Binarization (threshold)" => Loc.T("Correction") + "/" + Loc.T("Binarization (threshold)"),
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

        // Control ids of the tabs, built once (the tab bar draws on every repaint).
        static readonly string[] TabIds = TabDefs.Select(d => "tab." + d.tab.ToString().ToLowerInvariant()).ToArray();

        void DrawPanelArea(Rect panel)
        {
            if (_tab == Tab.Layers) // the layer list lives in 4 · Paint now
            {
                _tab = TabAvailable(Tab.Paint, out _) ? Tab.Paint : Tab.Target;
                if (_tab == Tab.Paint) SetTool(Tool.Paint);
            }
            const float tabsH = 36f, contextH = 36f, footerH = 32f;
            float stripH = NewToUnityDismissed ? 0f : NewToUnityStripHeight(panel.width);
            var stripR = new Rect(panel.x, panel.y, panel.width, stripH);
            float updH = UpdateBannerHeight(panel.width);
            var updR = new Rect(panel.x, stripR.yMax, panel.width, updH);
            var tabsR = new Rect(panel.x, updR.yMax, panel.width, tabsH);
            var ctxR = new Rect(panel.x, tabsR.yMax, panel.width, contextH);
            bool footer = NextStep(out _, out _) != null;
            var footR = new Rect(panel.x, panel.yMax - footerH, panel.width, footerH);
            var bodyR = new Rect(panel.x, ctxR.yMax, panel.width, (footer ? footR.y : panel.yMax) - ctxR.yMax);

            if (stripH > 0f) DrawNewToUnityStrip(stripR);
            if (updH > 0f) DrawUpdateBanner(updR);
            DrawTabBar(tabsR);
            DrawContextStrip(ctxR);

            // The body is pinned to the panel width (no horizontal scrolling); long text clips or wraps.
            GUILayout.BeginArea(bodyR);
            _panelScroll = EditorGUILayout.BeginScrollView(_panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            _panelContentWidth = Mathf.Max(120f, bodyR.width - 14f);
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(_panelContentWidth * 0.36f, 64f, 110f);
            using (new EditorGUILayout.VerticalScope(PanelPadStyle, GUILayout.Width(_panelContentWidth)))
            {
                if (!TabAvailable(_tab, out _)) _tab = Tab.Target;
                switch (_tab)
                {
                    case Tab.Target: DrawTargetTab(); break;
                    case Tab.Select: DrawSelectTab(); break;
                    case Tab.Paint: DrawPaintTab(); break;
                    case Tab.Layers: DrawLayersTab(); break;
                    case Tab.Save: DrawSaveTab(); break;
                    case Tab.Colour: DrawColourTab(); break;
                }
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
                var ic = StudioGUI.Icon(icon, "");
                if (ic.image != null) GUI.DrawTexture(new Rect(tr.center.x - 8, tr.y + 2, 16, 16), ic.image, ScaleMode.ScaleToFit);
                // Below 58 px a tab is too narrow for "2 Recolour": drop the number (the tooltip still has it).
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

        /// <summary>Progress marks on the tabs (goal-gradient): what's done, what's waiting.</summary>
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
                    int strokes = _recipe.layers.Count; // layers (the layer list is in this tab)
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

        /// <summary>Always visible: what is being edited, and whether there is unsaved work (working memory).</summary>
        void DrawContextStrip(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.HeaderBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }
            var help = new Rect(r.xMax - 26, r.y + 8, 20, 20);
            if (GUI.Button(help, StudioGUI.Icon("_Help", "", Loc.T("Shortcuts, glossary and mode")), EditorStyles.label)) ShowHelpMenu();
            // Menu callbacks run outside OnGUI, where a popup cannot work out its screen position.
            if (_showShortcutsNext && Event.current.type == EventType.Layout)
            {
                _showShortcutsNext = false;
                PopupWindow.Show(help, new ShortcutsPopup());
            }

            float right = help.x - 4;
            var modeLabel = new GUIContent(Advanced ? Loc.T("Advanced") : Loc.T("Simple"), ModeSwitchTip);
            float modeW = Mathf.Max(52f, EditorStyles.miniButton.CalcSize(modeLabel).x + 4f);
            var modeR = new Rect(right - modeW, r.y + 9, modeW, 18);
            if (GUI.Button(modeR, modeLabel, EditorStyles.miniButton)) SetAdvanced(!Advanced);
            right = modeR.x - 4;
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
            if (slot != null && slot.texture == null) _needsRebuild = true; // the texture was deleted or reimported: look again
            if (_comp == null || slot == null || slot.texture == null)
            {
                GUI.Label(new Rect(r.x + 8, r.y, right - r.x - 8, r.height),
                    new GUIContent(_renderer == null ? Loc.T("Nothing to edit yet. Start in 1 · Pick.") : Loc.T("Pick a texture in 1 · Pick.")), StudioGUI.Hint);
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
            // A narrow strip keeps the two numbers that change; the full line is in the tooltip.
            string shown = r.width < 330f
                ? (_selection.Count > 0 ? Loc.F("{0} piece{1}", _selection.Count, _selection.Count == 1 ? "" : "s") + " · " : "") + layersText
                : sub;
            GUI.Label(new Rect(tx, r.y + 17, right - tx, 16), new GUIContent(shown, sub), StudioGUI.Hint);
            if (GUI.Button(new Rect(r.x, r.y, tx + 60 - r.x, r.height), new GUIContent("", Loc.T("Change part or texture (1 · Pick)")), GUIStyle.none)) SetTab(Tab.Target);
        }

        [NonSerialized] bool _showShortcutsNext;
        static string ModeSwitchTip => Loc.T("Simple shows the everyday controls. Advanced shows every setting.");

        void ShowHelpMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Loc.T("Keyboard shortcuts…")), false, () => { _showShortcutsNext = true; Repaint(); });
            int counted = Glossary.CountedCount;
            menu.AddItem(new GUIContent(counted > 0 ? Loc.F("Glossary and skill tree…  {0}/{1}", Learn.DoneCount(), counted) : Loc.T("Glossary…")), false, () => GlossaryWindow.Open());
            OnlineGuide.AddMenu(menu, _tab.ToString());
            menu.AddItem(new GUIContent(Loc.T("Connect an AI helper…")), false, AgentWindow.Open);
            menu.AddItem(new GUIContent(Loc.T("Check for updates automatically"), Loc.T("Once a day, asks GitHub only for the latest version number")), UpdateCheck.Enabled, () => { UpdateCheck.SetEnabled(!UpdateCheck.Enabled); if (UpdateCheck.Enabled) UpdateCheck.MaybeCheckAutomatically(); Repaint(); });
            menu.AddItem(new GUIContent(Loc.T("Check for updates now")), false, UpdateCheck.CheckNow);
            menu.AddItem(new GUIContent(Loc.T("Copy diagnostics…")), false, () =>
            {
                string problem = CopyDiagnosticsFlow();
                if (problem != null) Toast(problem, MessageType.Warning);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(Loc.T("Advanced mode"), ModeSwitchTip), Advanced, () => SetAdvanced(!Advanced));
            AddLanguageItems(menu);
            menu.ShowAsContext();
        }

        /// <summary>The suggested next step for the footer (Zeigarnik / goal-gradient). Null when there is none.</summary>
        string NextStep(out Tab next, out string note)
        {
            next = _tab;
            note = null;
            bool hasLayers = _recipe.layers.Count > 0;
            switch (_tab)
            {
                case Tab.Target:
                    if (_material == null) return null;
                    next = Tab.Colour;
                    note = Loc.T("Quick recolour, or go on to edit the texture.");
                    return Loc.T("Next: 2 Colour  →");
                case Tab.Colour:
                    if (_comp == null) return null;
                    if (ByPart && Parts.Count > 0)
                    {
                        next = Tab.Save;
                        note = Loc.T("Save the coloured parts as a new PNG.");
                        return Loc.T("Next: 5 Save  →");
                    }
                    next = Tab.Select;
                    note = RecolourChanged ? Loc.T("Press Done when you're happy, or keep editing.") : Loc.T("Next, edit the texture itself.");
                    return Loc.T("Next: 3 Select  →");
                case Tab.Select:
                    if (!TabAvailable(Tab.Paint, out _)) return null;
                    next = Tab.Paint;
                    note = _selection.Count > 0 ? Loc.F("Edits will stay inside {0} piece(s).", _selection.Count) : Loc.T("Nothing selected: edits apply everywhere.");
                    return Loc.T("Next: 4 Paint  →");
                case Tab.Paint:
                    if (!hasLayers) return null;
                    next = Tab.Save;
                    note = Loc.T("Happy with it? Save it as a PNG.");
                    return Loc.T("Next: 5 Save  →");
                case Tab.Layers:
                    if (!hasLayers) return null;
                    next = Tab.Save;
                    note = Loc.T("Done editing? Save it as a PNG.");
                    return Loc.T("Next: 5 Save  →");
            }
            return null;
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

        void DrawTargetTab()
        {
            var suggested = SuggestedRenderer;

            if (_renderer == null)
            {
                StudioGUI.BeginGroup(Loc.T("Which part of your avatar?"), null, "hierarchy");
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
                StudioGUI.EndGroup();

                DrawOpenSavedEditGroup();
                return;
            }

            StudioGUI.BeginGroup(Loc.T("Part"), null, "mesh");
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
            StudioGUI.EndGroup();

            StudioGUI.BeginGroup(Loc.T("Texture to edit"), null, "texture");
            var mats = _renderer.sharedMaterials;
            if (mats.Length > 1)
            {
                var names = mats.Select((m, i) => NoSubmenu($"{i}: {(m != null ? m.name : Loc.T("(empty)"))}")).ToArray();
                StudioGUI.Count("pick.surface");
                int mi;
                using (new EditorGUILayout.HorizontalScope())
                {
                    mi = EditorGUILayout.Popup(new GUIContent(Loc.T("Material"), Loc.T("This part has several materials")), _materialIndex, names);
                    StudioGUI.Info("material");
                }
                if (mi != _materialIndex && ConfirmTargetChange("material"))
                {
                    Undo.RecordObject(this, "Change material");
                    _materialIndex = mi; _slotIndex = 0; _slotChosenByUser = false; _selection.Clear(); _needsRebuild = true;
                    Learn.Did(LearnEvents.MaterialSwitched);
                }
            }
            else EditorGUILayout.LabelField(Loc.T("Material"), _material != null ? _material.name : Loc.T("(none)"));

            if (_slots.Count > 0)
            {
                // Friendly name first (what it does), then the file; "off" when the shader currently ignores it.
                var labels = _slots.Select(s => NoSubmenu($"{s.displayName}  ·  {(s.texture != null ? s.texture.name : Loc.T("(missing)"))}" + (s.featureOff ? Loc.T("  (feature off)") : "") +
                    (!Advanced && (s.role == TextureRole.Mask || s.role == TextureRole.Normal) ? Loc.T(" (advanced)") : ""))).ToArray();
                StudioGUI.Count("pick.image");
                int si;
                using (new EditorGUILayout.HorizontalScope())
                {
                    si = EditorGUILayout.Popup(new GUIContent(Loc.T("Texture"), Loc.T("The textures this material uses. Colour textures are the usual choice.")), _slotIndex, labels);
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
                if (_statusFix == StatusFix.PickAnotherMesh)
                {
                    EditorGUILayout.HelpBox(SlotStatusText(), MessageType.Warning);
                    StudioGUI.Count("pick.pickAnotherMesh");
                    if (GUILayout.Button(Msg.BtnPickMesh, EditorStyles.miniButton)) DrawPartListMenu();
                }
                else if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Warning);
                StudioGUI.EndGroup();
                return;
            }

            string meshes = _uv != null ? string.Join(", ", _uv.parts.Select(p => p.renderer != null ? p.renderer.name : "?").Distinct()) : "";
            GUILayout.Label(new GUIContent(
                    Loc.F("{0}px · {1} part{2} · {3} pieces", _comp.SourceWidth, _uv?.parts.Count ?? 0, (_uv?.parts.Count ?? 0) == 1 ? "" : "s", _uv?.islandCount ?? 0),
                    Loc.F("Used by: {0}\nShader properties: {1}\nUV set: {2}\n{3}", meshes, string.Join(", ", slot.properties), _uv?.uvChannel ?? 0, _comp.SourceNote)),
                StudioGUI.Hint);

            if (slot.role == TextureRole.Normal)
            {
                EditorGUILayout.HelpBox(Msg.M20NormalMap(), MessageType.Warning);
                StudioGUI.Count("pick.goToPick");
                if (GUILayout.Button(Msg.BtnGoToPick, EditorStyles.miniButton)) SetTab(Tab.Target);
            }
            else if (slot.role == TextureRole.Mask)
                StudioGUI.CheckRow(StudioGUI.Check.Note, slot.channelInfo != null
                    ? Loc.F("Mask texture: white = effect on, black = off ({0}). Paint white/black, or use Ready-made looks › Mask textures.", slot.channelInfo)
                    : Loc.T("Mask texture: white = effect on, black = off. Paint white/black, or use Ready-made looks › Mask textures."));
            else if (slot.channelInfo != null)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("Channels: {0}.", slot.channelInfo));
            if (slot.mixedRoles)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("The shader uses this texture both as colour and as a mask in different places."));
            if (slot.featureOff)
                NoteWithFix(StudioGUI.Check.Problem, Msg.M23FeatureSwitchedOff(), Msg.BtnSwitchOn, () => { if (SwitchFeatureOn(slot)) _needsRebuild = true; }, "pick.switchFeatureOn");
            foreach (var uvp in slot.uvProblems)
                StudioGUI.CheckRow(StudioGUI.Check.Note, uvp);

            // Import settings that don't match how the shader reads the texture, or that the shader docs advise against:
            // say so, and offer the fix where one is safe.
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
            StudioGUI.EndGroup();

            if (_editThatMadeTexture != null && _editThatMadeTexture.sourceTexture != null)
            {
                StudioGUI.BeginGroup(Loc.T("Edited copy"), null, "texture");
                GUILayout.Label(Loc.F("This texture was made by Avistry from ‘{0}’. New layers go on top of that earlier edit.", _editThatMadeTexture.sourceTexture.name), StudioGUI.CheckLabel);
                StudioGUI.Count("pick.editedCopy");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(Loc.T("Change the earlier edit instead"), Loc.T("Switch back to the original texture and reopen the layers that made this one")), GUILayout.Height(24)))
                        OpenSavedEdit(_editThatMadeTexture);
                    StudioGUI.Info("asset");
                }
                StudioGUI.EndGroup();
            }

            DrawOpenSavedEditGroup();

            if (!Advanced) return;

            DrawMaskBuilderGroup();
            DrawDecalsGroup();
            DrawMapsGroup();
            StudioGUI.Count("audit.check");
            _foldAudit = EditorGUILayout.Foldout(_foldAudit, new GUIContent(Loc.T("Check this avatar's materials"), Loc.T("Finds pink or broken materials, missing textures and wrong texture settings, with safe fixes")), true);
            if (_foldAudit) DrawAuditGroup();

            StudioGUI.Count("quality.header");
            _foldOutputOptions = EditorGUILayout.Foldout(_foldOutputOptions, Loc.T("Quality"), true);
            if (_foldOutputOptions)
            {
                int wi = Array.IndexOf(WorkSizes, _workSize);
                StudioGUI.Count("quality.previewSize");
                int nwi = EditorGUILayout.Popup(new GUIContent(Loc.T("Preview size"), Loc.T("Resolution while editing (lower = faster). Saving always uses the full resolution.")),
                    Mathf.Max(0, wi), WorkSizes.Select(s => s + " px").ToArray());
                if (WorkSizes[nwi] != _workSize) { _workSize = WorkSizes[nwi]; _needsRebuild = true; }
            }
        }

        void DrawOpenSavedEditGroup()
        {
            StudioGUI.BeginGroup(Loc.T("Continue a saved edit"), null, "asset");
            StudioGUI.Count("pick.reopenSave");
            using (new EditorGUILayout.HorizontalScope())
            {
                _recipeToLoad = (TextureRecipe)EditorGUILayout.ObjectField(new GUIContent("", Loc.T("Saved edits are the *_recipe / *_edit files created next to a texture when you save")),
                    _recipeToLoad, typeof(TextureRecipe), false);
                using (new EditorGUI.DisabledScope(_recipeToLoad == null || _comp == null))
                    if (GUILayout.Button(Loc.T("Open"), GUILayout.Width(52))) OpenSavedEdit(_recipeToLoad);
                StudioGUI.Info("asset");
            }
            GUILayout.Label(_comp == null ? Loc.T("Pick the part first, then open the edit.") : Loc.T("Reopens the layers of an earlier edit so you can keep changing them."), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        void DrawSelectTab()
        {
            if (_uv == null) { EditorGUILayout.HelpBox(Loc.T("This part has no UV data, so pieces can't be selected."), MessageType.Warning); return; }

            GUILayout.Label(Loc.T("Pieces are the separate pieces of the texture sheet. What you select here limits painting (4 · Paint) and colour parts (2 · Recolour › One piece at a time)."), StudioGUI.Hint);
            GUILayout.Space(2);
            StudioGUI.BeginGroup(Loc.T("Pieces"), Loc.F("{0} of {1}", _selection.Count, _uv.islandCount), "island");
            GUILayout.Label(_selection.Count == 0 ? Loc.T("Nothing selected: edits apply to the whole texture.") :
                Loc.F("{0} piece{1} selected: new layers and paint stay inside them.", _selection.Count, (_selection.Count == 1 ? "" : "s")), StudioGUI.CheckLabel);
            GUILayout.Space(2);
            DrawSelectModeStrip(false);
            DrawSelectOpSwitch();
            DrawSelectModeOptions();
            GUILayout.Space(2);
            StudioGUI.Count("limit.everything");
            StudioGUI.Count("limit.nothing");
            StudioGUI.Count("limit.invert");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Loc.T("Everything"), EditorStyles.miniButtonLeft, GUILayout.Height(22))) SelectEverything();
                if (GUILayout.Button(Loc.T("Nothing"), EditorStyles.miniButtonMid, GUILayout.Height(22))) SelectNothing();
                if (GUILayout.Button(Loc.T("Invert"), EditorStyles.miniButtonRight, GUILayout.Height(22))) InvertSelection();
                StudioGUI.Info("island");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selection.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent(Loc.T("Grow"), Loc.T("Add the pieces that touch the selection on the model.")), EditorStyles.miniButtonLeft)) GrowSelection(1);
                    if (GUILayout.Button(new GUIContent(Loc.T("Shrink"), Loc.T("Take away the selected pieces that touch a piece that is not selected.")), EditorStyles.miniButtonRight)) GrowSelection(-1);
                }
            }
            GUILayout.Label(SelectModeHint(), StudioGUI.Hint);
            StudioGUI.EndGroup();
            DrawTileList();

            StudioGUI.BeginGroup(Loc.T("Scene view"), null, "scene-view");
            bool pick = _pickInScene;
            InfoRow("limit.clickAvatar", "scene-view", () => pick = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Click the model to select pieces"),
                Loc.T("Click the model in the Scene view to select the piece under the mouse (outlined in white).")), _pickInScene));
            if (pick != _pickInScene) { _pickInScene = pick; _hoverIsland = -1; SceneView.RepaintAll(); }
            bool hl = _highlightInScene;
            InfoRow("limit.showOnAvatar", "scene-view", () => hl = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Show the selection on the model"),
                Loc.T("Selected pieces are drawn in orange in the Scene view. Parts hidden behind other geometry show faintly.")), _highlightInScene));
            if (hl != _highlightInScene) { _highlightInScene = hl; SceneView.RepaintAll(); }
            StudioGUI.EndGroup();
        }

        /// <summary>One button per UV tile, when the part's UVs go beyond the texture square.</summary>
        void DrawTileList()
        {
            if (!HasOtherTiles) return;
            var counts = _uv.TileCounts();
            StudioGUI.BeginGroup(Loc.T("UV tiles"), Loc.F("{0} tiles", counts.Count), "uv-map");
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

        /// <summary>One row: the control, then its info icon. Counts as one visible control.</summary>
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
            int toolIndex = PaintToolIndex;
            bool simpleTool = toolIndex == 2 || toolIndex == 3; // fill and gradient: no brush size, hardness or presets
            string brushTitle = toolIndex == 4 ? Loc.T("Smear / Soften") : toolIndex == 3 ? Loc.T("Gradient") : toolIndex == 2 ? Loc.T("Fill") : mask ? Loc.T("Mask brush") : Loc.T("Brush");
            string brushSummary = simpleTool ? Loc.F("Paint strength {0:0}%", _brush.opacity * 100f) : BrushLabel(_brush.presetName) + (_brush.edited ? "*" : "") + " " + Loc.F("{0:0} px", _brush.size);
            bool brushOpen = StudioGUI.BeginFoldGroup(ref _openBrush, brushTitle, brushSummary, simpleTool ? null : $"{BrushLabel(_brush.presetName)}{(_brush.edited ? "*" : "")}",
                Loc.T("The tools, presets, size, opacity and hardness. Click to fold."));
            if (brushOpen) DrawBrushGroupBody(mask, toolIndex, simpleTool);
            StudioGUI.EndGroup();

            DrawLayerList(); // always visible, right under the tools: pick or add a layer without scrolling
            if (_openLayers && Advanced) DrawHistory();
            DrawSelectedLayerSettings(); // the settings of the layer picked in the list, right under the list (closed until opened)

            // The eraser, blend tools and mask painting have no colour: no Colour group for them.
            if (!mask && _brush.mode != BrushMode.Erase && toolIndex != 4)
            {
                if (StudioGUI.BeginFoldGroup(ref _openColour, Loc.T("Colour"), "#" + ColorUtility.ToHtmlStringRGB(_brush.color), null, Loc.T("The brush colour, palettes and colour picker. Click to fold.")))
                {
                    DrawColourControls();
                    if (toolIndex == 3 && _grad.colours == GradientColours.Custom)
                        GUILayout.Label(Loc.T("The custom gradient sets the colours. The main and sub colours are used by the other Colours options."), StudioGUI.Hint);
                }
                StudioGUI.EndGroup();
            }

            if (toolIndex == 3) DrawGradientTargetGroup();
            else DrawPaintTargetGroup();
        }

        static GUIStyle _panelPad, _toolButton;
        static GUIStyle PanelPadStyle => _panelPad ??= new GUIStyle { padding = new RectOffset(4, 4, 4, 6) };
        static GUIStyle ToolButtonStyle => _toolButton ??= new GUIStyle(GUI.skin.button) { padding = new RectOffset(2, 2, 2, 2), fontSize = 11 };

        void DrawBrushGroupBody(bool mask, int toolIndex, bool simpleTool)
        {
            var toolStyle = ToolButtonStyle;
            StudioGUI.Count("paint.tools");
            int newTool;
            using (new EditorGUILayout.HorizontalScope())
            {
                var toolRect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
                newTool = GUI.Toolbar(toolRect, toolIndex, FitTools(mask
                    ? new[]
                    {
                        StudioGUI.Icon("Grid.PaintTool", Loc.T(" Show"), Loc.T("Paint to show this part of the layer again (B)")),
                        StudioGUI.Icon("Grid.EraserTool", Loc.T(" Hide"), Loc.T("Paint to hide this part of the layer (E)")),
                        StudioGUI.Icon("Grid.FillTool", Loc.T(" Fill"), Loc.T("Hide or reveal a whole piece with one click (G)")),
                        GradientToolContent(Loc.T(" Gradient"), Loc.T("Gradients can't be drawn on masks. Click Done above to draw gradients.")),
                        StudioGUI.Icon("Grid.MoveTool", Loc.T(" Blend"), Loc.T("Blur and smudge can't be used on masks. Click Done above to use them.")),
                    }
                    : new[]
                    {
                        StudioGUI.Icon("Grid.PaintTool", Loc.T(" Paint"), Loc.T("Paint colour (B)")),
                        StudioGUI.Icon("Grid.EraserTool", Loc.T(" Erase"), Loc.T("Erase paint on the selected Paint layer (E)")),
                        StudioGUI.Icon("Grid.FillTool", Loc.T(" Fill"), Loc.T("Fill an area with one click (G). Stops at the edges of the piece you click.")),
                        GradientToolContent(Loc.T(" Gradient"), Loc.T("Drag to draw a gradient: linear, circle or ellipse (Shift+G). Each one is an editable layer.")),
                        StudioGUI.Icon("Grid.MoveTool", Loc.T(" Blend"), Loc.T("Blur or smudge what's already there (J). Works on a Paint layer, so it can be erased or undone.")),
                    }), toolStyle);
                StudioGUI.Info("layer");
            }
            if (newTool != toolIndex)
            {
                if (mask && newTool >= 3) Toast(newTool == 3 ? Loc.T("Gradients can't be used on a layer mask. Click Done (above) to go back to painting colour.") : Loc.T("Blur and smudge can't be used on a layer mask. Click Done (above) to go back to painting colour."), MessageType.Info, 5f);
                else SetPaintTool(newTool);
            }
            GUILayout.Space(4);
            if (_brush.gradientTool && _brush.mode == BrushMode.Paint) DrawGradientSettings();
            else if (_brush.fillTool) DrawFillSettings();
            else
            {
                if (toolIndex == 4) { DrawBlendSettings(); GUILayout.Space(3); }
                DrawBrushPresetGrid();
                GUILayout.Space(3);
                DrawBrushPreviewStrip();
            }
            GUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            if (!simpleTool)
                InfoRow("paint.size", "layer", () => _brush.size = StudioGUI.LogSlider(new GUIContent(Loc.T("Size (px)"), Loc.T("Brush diameter in texture pixels. [ and ] change it.")), _brush.size, BrushSettings.MinSize, BrushSettings.MaxSize, null));
            InfoRow("paint.strength", "opacity", () => _brush.opacity = EditorGUILayout.Slider(new GUIContent(Loc.T("Paint strength"), toolIndex == 3 ? Loc.T("Layer opacity of new gradient layers") : Loc.T("How strong one stroke (or fill) is")), _brush.opacity, 0f, 1f));
            if (!simpleTool)
                InfoRow("paint.edgeSoftness", "layer", () => _brush.shape.hardness = EditorGUILayout.Slider(new GUIContent(Loc.T("Hardness"), Loc.T("0 = soft edge, 1 = hard edge")), _brush.shape.hardness, 0f, 1f));
            if (EditorGUI.EndChangeCheck()) MarkBrushEdited();
            if (!simpleTool && Advanced)
            {
                StudioGUI.Count("paint.moreBrushSettings");
                _foldBrushAdvanced = EditorGUILayout.Foldout(_foldBrushAdvanced, new GUIContent(Loc.T("More brush settings"), Loc.T("Tip shape, spacing, angle, airbrush, grain, jitter, pen pressure, save your own brush")), true);
                if (_foldBrushAdvanced) DrawBrushAdvanced();
            }
        }

        /// <summary>Narrow panel: icon-only tool buttons (the name moves into the tooltip) so all five fit. The toolbar is drawn
        /// in a rect that takes whatever width is left, so it can never push the panel wider; names show only when each button
        /// has room for its own.</summary>
        GUIContent[] FitTools(GUIContent[] tools)
        {
            var style = ToolButtonStyle;
            float widest = 0f;
            foreach (var c in tools) widest = Mathf.Max(widest, style.CalcSize(c).x);
            const float groupPadding = 16f, infoIcon = 18f, gap = 6f;
            if ((_panelContentWidth - groupPadding - infoIcon) / tools.Length >= widest + gap) return tools;
            return tools.Select(c => new GUIContent(c.image != null ? "" : c.text.Trim(), c.image, c.text.Trim() + ": " + c.tooltip)).ToArray();
        }

        /// <summary>A toolbar that falls back to two or more rows when its labels do not fit side by side, so a narrow panel never clips
        /// them. The decision uses the panel width (not the layout rect), so it is the same in the Layout and Repaint passes.
        /// <paramref name="width"/> is the room inside the group; leave it out in the side panel.</summary>
        int WrapToolbar(int selected, GUIContent[] contents, GUIStyle style = null, float rowHeight = 22f, float width = -1f)
        {
            style ??= GUI.skin.button;
            if (width <= 0f) width = _panelContentWidth - 16f;
            float widest = 0f;
            foreach (var c in contents) widest = Mathf.Max(widest, style.CalcSize(c).x);
            int perRow = Mathf.Clamp(Mathf.FloorToInt(width / (widest + 4f)), 1, contents.Length);
            int rows = Mathf.CeilToInt(contents.Length / (float)perRow);
            perRow = Mathf.CeilToInt(contents.Length / (float)rows);
            var rect = GUILayoutUtility.GetRect(0f, rowHeight * rows, GUILayout.ExpandWidth(true));
            return GUI.SelectionGrid(rect, selected, contents, perRow, style);
        }

        /// <summary>Blend tool settings (CLIP STUDIO PAINT's Blend group: Blur, Finger tip).</summary>
        void DrawBlendSettings()
        {
            int kind = 0;
            InfoRow("paint.blendKind", "layer", () => kind = GUILayout.Toolbar(_brush.blendKind == StrokeTool.Smudge ? 1 : 0, new[]
            {
                new GUIContent(Loc.T("Blur"), Loc.T("Soften: blends colours under the brush with their surroundings")),
                new GUIContent(Loc.T("Smudge"), Loc.T("Finger tip: drags colour along the direction you move the brush")),
            }, EditorStyles.miniButton));
            _brush.blendKind = kind == 1 ? StrokeTool.Smudge : StrokeTool.Blur;
            InfoRow("paint.blendAmount", "layer", () => _brush.blendAmount = EditorGUILayout.Slider(new GUIContent(_brush.blendKind == StrokeTool.Smudge ? Loc.T("Smudge length") : Loc.T("Blur amount"),
                _brush.blendKind == StrokeTool.Smudge ? Loc.T("How far colour is dragged, as a share of the brush size") : Loc.T("How soft, as a share of the brush size")),
                _brush.blendAmount, 0.1f, 3f));
            InfoRow("paint.dontSpill", "island", () => _brush.blendKeepIsland = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Only blend within the same piece"),
                Loc.T("Recommended: never pulls in colour from empty texture space or a neighbouring part, so seams stay clean.")), _brush.blendKeepIsland));
            GUILayout.Label(Loc.T("Works on what you see. The result goes on a Paint layer (a “Blending” layer is added if needed), so you can erase or undo it. Opacity sets the strength."), StudioGUI.Hint);
        }

        /// <summary>Fill tool settings, named like CLIP STUDIO PAINT's Fill tool (Tolerance, Area scaling).</summary>
        void DrawFillSettings()
        {
            if (MaskEditing)
            {
                StudioGUI.Count("paint.maskFill");
                var r = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(Loc.T("On the mask"), Loc.T("What a fill does to the layer mask")));
                _maskFillHides = GUI.Toolbar(r, _maskFillHides ? 1 : 0, new[] { Loc.T("Show"), Loc.T("Hide") }, EditorStyles.miniButton) == 1;
            }
            int mode = 0;
            InfoRow("paint.fillKind", "island", () => mode = GUILayout.Toolbar(_brush.fillWholeIsland ? 0 : 1, new[]
            {
                new GUIContent(Loc.T("Whole piece"), Loc.T("Fill the entire piece you click")),
                new GUIContent(Loc.T("Similar colour"), Loc.T("Fill connected pixels whose original colour is close to the one you click (Tolerance)")),
            }, EditorStyles.miniButton));
            _brush.fillWholeIsland = mode == 0;
            if (!_brush.fillWholeIsland)
                InfoRow("paint.colourRange", "layer", () => _brush.fillTolerance = EditorGUILayout.Slider(new GUIContent(Loc.T("Tolerance"), Loc.T("How different a colour may be and still get filled (CSP: colour margin)")), _brush.fillTolerance, 0f, 1f));
            InfoRow("paint.fillGrow", "seam", () => _brush.fillAreaScaling = EditorGUILayout.Slider(new GUIContent(Loc.T("Area scaling (px)"), Loc.T("Grows the filled area so it also covers the edge and seam pixels (avoids a thin unfilled outline)")), _brush.fillAreaScaling, 0f, 16f));
            GUILayout.Label(Loc.T("Click the texture or the model. A fill never leaves the piece you click; Similar colour compares the original texture's colours."), StudioGUI.Hint);
        }

        /// <summary>Where paint will land, and anything that would block it, stated up front (no surprises).</summary>
        /// <summary>Unmissable while painting a mask: what's being edited, what the tools do, and the way out.</summary>
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
                GUILayout.Label(Loc.F("Painting the MASK of “{0}”", layer.name), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = StudioGUI.InfoBlue;
                StudioGUI.Count("paint.maskDone");
                if (GUILayout.Button(new GUIContent(Loc.T("Done"), Loc.T("Stop editing the mask and paint colour again")), GUILayout.Width(60), GUILayout.Height(20)))
                    SetMaskEditing(null, false);
                GUI.backgroundColor = oldBg;
                GUILayout.Space(4);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                GUILayout.Label(Loc.T("Show shows the layer, Hide hides it (red on the texture). Nothing is deleted, so you can always paint it back."), StudioGUI.Hint);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                StudioGUI.Count("paint.maskOverlay");
                _showMaskOverlay = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Show hidden areas in red"), Loc.T("Overlay on the texture view while editing the mask")), _showMaskOverlay);
                StudioGUI.Count("paint.maskInvert");
                if (GUILayout.Button(new GUIContent(Loc.T("Invert"), Loc.T("Swap hidden and shown")), EditorStyles.miniButton, GUILayout.Width(52))) InvertPaintMask(layer);
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
                StudioGUI.BeginGroup(Loc.T("Mask painting options"));
                var ml = ActiveLayer;
                GUILayout.Label(Loc.F("The mask of “{0}” ({1}). {2} mask stroke(s) · Ctrl+Z undoes the last.", ml.name, ml.TypeName, ml.maskStrokes.Count), StudioGUI.CheckLabel);
                if (ml.maskMode == MaskMode.SelectedIslands)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.F("The layer is also limited to {0} piece(s); revealing can't show it outside them.", ml.islands.Count));
                if (ml.opacity <= 0.001f) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("This layer is at 0% opacity, so the mask has nothing to show."));
                GUILayout.Space(2);
                EditorGUI.BeginChangeCheck();
                InfoRow("paint.dontSpill", "island", () => _brush.stayInsideIslands = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Don't spill onto other pieces"),
                    Loc.T("Mask strokes only change the pieces under the brush centre.")), _brush.stayInsideIslands));
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
            // The notes sit inside the fold, so the closed header says how many there are: a problem is never hidden by it.
            var emissionNotes = EmissionNotes();
            var layerNotes = activePaint != null ? PaintLayerNotes(activePaint) : null;
            int noteCount = emissionNotes.Count + (layerNotes?.Count ?? 0);
            int problemCount = emissionNotes.Count(n => n.kind == StudioGUI.Check.Problem) + (layerNotes?.Count(n => n.kind == StudioGUI.Check.Problem) ?? 0);
            string noteText = problemCount > 0 ? Loc.F("{0} problem{1}", problemCount, problemCount == 1 ? "" : "s") : Loc.F("{0} note{1}", noteCount, noteCount == 1 ? "" : "s");
            string optSummary = noteCount > 0
                ? noteText + (optBits.Count == 0 || PanelContentWidth < 330f ? "" : " · " + string.Join(", ", optBits))
                : optBits.Count == 0 ? Loc.T("defaults") : string.Join(", ", optBits);
            if (problemCount > 0) _openPaintOptions = true; // a problem is never hidden by a closed fold
            if (!StudioGUI.BeginFoldGroup(ref _openPaintOptions, Loc.T("Painting options"), optSummary, null,
                    Loc.T("Where paint goes: the layer it lands on, piece limits, painting on the model and mirroring. Click to fold."), "island"))
            {
                StudioGUI.EndGroup();
                return;
            }
            foreach (var n in emissionNotes) StudioGUI.CheckRow(n.kind, n.text, n.tip);
            if (activePaint != null)
            {
                var pl = activePaint;
                if (BlendToolActive && pl.blend != BlendMode.Normal)
                    GUILayout.Label(Loc.F("Blur and smudge go on a new “Blending” layer above “{0}” (it uses {1}, they need Normal).", pl.name, pl.blend), StudioGUI.CheckLabel);
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
                if (pl.maskMode == MaskMode.SelectedIslands)
                {
                    StudioGUI.Count("paint.layerLimit");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent(Loc.T("Use current selection"), Loc.T("Limit this layer to the pieces selected now")), EditorStyles.miniButtonLeft)) CaptureSelection(pl);
                        if (GUILayout.Button(new GUIContent(Loc.T("Paint everywhere"), Loc.T("Remove the piece limit from this layer")), EditorStyles.miniButtonRight))
                        {
                            Undo.RecordObject(_recipe, "Remove limit");
                            pl.maskMode = MaskMode.WholeTexture;
                            MarkEdited();
                        }
                        StudioGUI.Info("island");
                    }
                }
            }
            else
            {
                GUILayout.Label(_selection.Count > 0 ? Loc.F("Your first stroke adds a new Paint layer, limited to the {0} selected piece(s).", _selection.Count)
                                                     : Loc.T("Your first stroke adds a new Paint layer."), StudioGUI.CheckLabel);
            }

            GUILayout.Space(2);
            EditorGUI.BeginChangeCheck();
            InfoRow("paint.dontSpill", "island", () => _brush.stayInsideIslands = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Don't spill onto other pieces"),
                Loc.T("Paint only lands on the piece under the brush centre, so it never spills onto neighbouring parts of the texture.")), _brush.stayInsideIslands));
            InfoRow("paint.onAvatar", "scene-view", () => _paintInScene = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Paint directly on the model (Scene view)"),
                Loc.T("Drag on the model in the Scene view. Alt+drag and right-drag still move the camera.")), _paintInScene));
            DrawSurfaceBrushToggle();
            if (EditorGUI.EndChangeCheck()) { Repaint(); SceneView.RepaintAll(); }
            StudioGUI.Divider();
            DrawSymmetryControls();
            StudioGUI.EndGroup();
        }

        /// <summary>"Your emission may hide paint" notes for the texture being edited (the look check is cached for a second).</summary>
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

        /// <summary>What may stop paint on this layer from showing: clipping, hidden, 0% opacity and a piece limit.</summary>
        List<(StudioGUI.Check kind, string text, string tip)> PaintLayerNotes(PaintLayer pl)
        {
            var notes = new List<(StudioGUI.Check, string, string)>();
            if (pl.clipToBelow)
            {
                var clipBase = ClipBaseOf(ActiveIndex);
                if (clipBase != null)
                {
                    bool hidden = !clipBase.enabled || clipBase.opacity <= 0.001f;
                    notes.Add((hidden ? StudioGUI.Check.Problem : StudioGUI.Check.Note, hidden
                        ? Loc.F("Clipped to “{0}”, which is hidden, so nothing you paint here shows.", clipBase.name)
                        : Loc.F("Clipped to “{0}”: paint only shows where that layer shows.", clipBase.name), null));
                }
            }
            if (!pl.enabled) notes.Add((StudioGUI.Check.Note, Loc.T("This layer is hidden; it will be shown again when you paint."), null));
            if (pl.opacity <= 0.001f) notes.Add((StudioGUI.Check.Problem, Loc.T("This layer is at 0% opacity, so paint won't show."), null));
            if (pl.maskMode == MaskMode.SelectedIslands)
                notes.Add((StudioGUI.Check.Note, pl.invertMask ? Loc.F("Paint only inside the selected pieces ({0}), inverted.", pl.islands.Count) : Loc.F("Paint only inside the selected pieces ({0}).", pl.islands.Count),
                    Loc.T("This layer only shows on the pieces chosen in 3 · Limit. Use current selection to change them, or Paint everywhere to remove the limit.")));
            return notes;
        }

        void MarkBrushEdited()
        {
            _brush.edited = true;
            Repaint();
            SceneView.RepaintAll();
        }

        float PanelContentWidth => _panelContentWidth - 30f; // minus tab and group padding

        /// <summary>Five everyday brushes up front; the rest one click away (choice overload / Hick's law).</summary>
        void DrawBrushPresetGrid()
        {
            var favourites = BrushLibrary.BuiltIn.Where(p => FavouriteBrushes.Contains(p.name)).Select(p => (preset: p, user: false)).ToList();
            var more = BrushLibrary.BuiltIn.Where(p => !FavouriteBrushes.Contains(p.name)).Select(p => (preset: p, user: false))
                .Concat(BrushLibrary.User.Select(p => (preset: p, user: true))).ToList();
            if (more.Any(x => x.preset.name == _brush.presetName)) _foldMoreBrushes = true; // never hide the brush in use

            StudioGUI.Count("paint.brushes");
            DrawBrushButtons(favourites);
            StudioGUI.Count("paint.moreBrushes");
            _foldMoreBrushes = EditorGUILayout.Foldout(_foldMoreBrushes, Loc.F("More brushes ({0})", more.Count), true);
            if (_foldMoreBrushes) DrawBrushButtons(more);
        }

        const float ChipThumbW = 32f, ChipThumbH = 14f;
        static GUIStyle _chipStyle;
        static GUIStyle ChipStyle => _chipStyle ??= new GUIStyle(EditorStyles.miniButton)
            { padding = new RectOffset((int)ChipThumbW + 8, 2, 2, 2), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        BrushChipThumbs _chipThumbs;

        /// <summary>A short sample stroke at the left of a brush chip, so brushes can be told apart without trying them.</summary>
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
            // each chip also holds a stroke thumbnail; longer names (Japanese, Korean, Chinese) get fewer, wider chips instead of being cut
            float need = 84f + ChipThumbW + 8f;
            foreach (var (p, user) in list) need = Mathf.Max(need, ChipStyle.CalcSize(new GUIContent("*" + (user ? "★ " : "") + BrushLabel(p.name))).x + 4f);
            need = Mathf.Min(need, (avail - 3f) * 0.5f); // one very long user brush name is cut rather than costing everyone a column
            int cols = Mathf.Clamp((int)((avail + 3f) / (need + 3f)), 1, 5);
            float bw = (avail - (cols - 1) * 3f) / cols;
            BrushPreset apply = null;
            for (int i = 0; i < list.Count; i += cols)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + cols && j < list.Count; j++)
                    {
                        var (p, user) = list[j];
                        bool on = _brush.presetName == p.name;
                        var label = new GUIContent((on && _brush.edited ? "*" : "") + (user ? "★ " : "") + BrushLabel(p.name),
                            user ? Loc.T("Your saved brush") : (on && _brush.edited ? Loc.T("Click to reset to the original brush") : BrushLabel(p.name)));
                        bool now = GUILayout.Toggle(on, label, ChipStyle, GUILayout.Width(bw), GUILayout.Height(20));
                        DrawChipThumb(GUILayoutUtility.GetLastRect(), p);
                        if (now != on) apply = p; // clicking the current (edited) brush resets it
                    }
                }
            }
            if (apply != null)
            {
                apply.ApplyTo(_brush);
                Repaint();
                SceneView.RepaintAll();
            }
        }

        void DrawBrushPreviewStrip()
        {
            var r = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, StudioGUI.CanvasBg);
            if (_brushPreview == null) _brushPreview = new BrushPreview();
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var tex = _brushPreview.Get(_brush, Mathf.RoundToInt(r.width * ppp), Mathf.RoundToInt(r.height * ppp));
            var old = GUI.color;
            GUI.color = _brush.mode == BrushMode.Erase
                ? new Color(1f, 0.6f, 0.6f, 1f)
                : MaskEditing ? new Color(0.92f, 0.92f, 0.92f, Mathf.Max(0.15f, _brush.opacity)) // masks have no colour
                : new Color(_brush.color.r, _brush.color.g, _brush.color.b, Mathf.Max(0.15f, _brush.opacity));
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        void DrawBrushAdvanced()
        {
            var sh = _brush.shape;
            EditorGUI.BeginChangeCheck();
            sh.tip = (BrushTip)EditorGUILayout.EnumPopup(new GUIContent(Loc.T("Tip"), Loc.T("Round / Square dabs, Bristle draws parallel streaks, Spray scatters particles")), sh.tip);
            sh.spacing = EditorGUILayout.Slider(new GUIContent(Loc.T("Spacing"), Loc.T("Distance between dabs. Higher = dotted line.")), sh.spacing, 0.02f, 3f);
            if (sh.tip == BrushTip.Bristle || sh.tip == BrushTip.Spray)
                sh.density = EditorGUILayout.IntSlider(new GUIContent(sh.tip == BrushTip.Bristle ? Loc.T("Bristles") : Loc.T("Particles")), sh.density, 1, 48);
            if (sh.tip == BrushTip.Round || sh.tip == BrushTip.Square)
                sh.roundness = EditorGUILayout.Slider(new GUIContent(Loc.T("Roundness"), Loc.T("Lower = flat tip (calligraphy, marker)")), sh.roundness, 0.05f, 1f);
            if (sh.tip != BrushTip.Spray)
            {
                sh.angle = EditorGUILayout.Slider(new GUIContent(Loc.T("Angle")), sh.angle, -180f, 180f);
                sh.followStroke = EditorGUILayout.Toggle(new GUIContent(Loc.T("Follow stroke"), Loc.T("Rotate the tip with the stroke direction")), sh.followStroke);
            }
            sh.buildUp = EditorGUILayout.Toggle(new GUIContent(Loc.T("Airbrush (build up)"), Loc.T("Paint keeps building up while you brush over the same spot")), sh.buildUp);
            if (sh.buildUp) sh.flow = EditorGUILayout.Slider(new GUIContent(Loc.T("Flow"), Loc.T("How much each dab adds")), sh.flow, 0.01f, 1f);
            sh.grain = EditorGUILayout.Slider(new GUIContent(Loc.T("Grain"), Loc.T("Paper texture in the paint (chalk, pencil)")), sh.grain, 0f, 1f);
            if (sh.grain > 0f) sh.grainSize = EditorGUILayout.Slider(new GUIContent(Loc.T("Grain size (px)")), sh.grainSize, 1f, 64f);
            sh.scatter = EditorGUILayout.Slider(new GUIContent(Loc.T("Scatter"), Loc.T("Random offset of each dab")), sh.scatter, 0f, 3f);
            sh.sizeJitter = EditorGUILayout.Slider(new GUIContent(Loc.T("Size jitter")), sh.sizeJitter, 0f, 1f);
            sh.opacityJitter = EditorGUILayout.Slider(new GUIContent(Loc.T("Opacity jitter")), sh.opacityJitter, 0f, 1f);
            sh.colorJitter = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Colour variety per stroke"),
                Loc.T("Each stroke gets a slightly different colour: natural variation for hair strands, fabric or skin.")), sh.colorJitter);
            if (sh.colorJitter)
            {
                EditorGUI.indentLevel++;
                sh.jitterHue = EditorGUILayout.Slider(new GUIContent(Loc.T("Hue (°)"), Loc.T("How far the hue may move, either way")), sh.jitterHue, 0f, 180f);
                sh.jitterSaturation = EditorGUILayout.Slider(new GUIContent(Loc.T("Saturation")), sh.jitterSaturation, 0f, 1f);
                sh.jitterLuminosity = EditorGUILayout.Slider(new GUIContent(Loc.T("Luminosity")), sh.jitterLuminosity, 0f, 1f);
                sh.jitterSubBlend = EditorGUILayout.Slider(new GUIContent(Loc.T("Blend with second colour"), Loc.T("Up to this much of the second colour is mixed into each stroke")), sh.jitterSubBlend, 0f, 1f);
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
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent(Loc.T("Pen pressure"), Loc.T("Graphics tablet pressure controls size and/or opacity")));
                _brush.pressureSize = GUILayout.Toggle(_brush.pressureSize, Loc.T("Size"), EditorStyles.miniButtonLeft);
                _brush.pressureOpacity = GUILayout.Toggle(_brush.pressureOpacity, Loc.T("Opacity"), EditorStyles.miniButtonRight);
            }
            if (EditorGUI.EndChangeCheck()) MarkBrushEdited();

            StudioGUI.Divider();
            GUILayout.Label(Loc.T("Save this brush"), StudioGUI.GroupTitle);
            using (new EditorGUILayout.HorizontalScope())
            {
                _newBrushName = EditorGUILayout.TextField(_newBrushName ?? "");
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newBrushName)))
                {
                    if (GUILayout.Button(new GUIContent(Loc.T("Save"), Loc.T("Save these settings as your own brush (kept on this computer, not in the project)")), EditorStyles.miniButton, GUILayout.Width(52)))
                    {
                        string name = _newBrushName.Trim();
                        BrushLibrary.Save(BrushPreset.From(_brush, name));
                        _brush.presetName = name;
                        _brush.edited = false;
                        _newBrushName = "";
                        GUI.FocusControl(null);
                        Toast(Loc.F("Saved brush “{0}”. It's under More brushes.", name), MessageType.Info, 3f);
                    }
                }
            }
            var mine = BrushLibrary.User.FirstOrDefault(p => p.name == _brush.presetName);
            if (mine != null && StudioGUI.DangerButton(new GUIContent(Loc.F("Delete my brush “{0}”", mine.name)), EditorStyles.miniButton))
            {
                if (StudioDialog.Ask(Loc.T("Delete brush"), Msg.M11DeleteBrush(mine.name), Msg.BtnDelete, Msg.BtnCancel))
                {
                    BrushLibrary.Delete(mine.name);
                    _brush.edited = true;
                }
            }
        }

        /// <summary>The old Layers tab: the layer list and the selected layer's settings (now both shown in 4 · Paint).</summary>
        void DrawLayersTab()
        {
            DrawLayerList();
            DrawSelectedLayerSettings();
        }

        void DrawSelectedLayerSettings()
        {
            if (_recipe.layers.Count == 0) return;
            Action pending = null;
            if (ActiveLayer != null) DrawActiveLayerSettings(ref pending);
            else GUILayout.Label(Loc.T("Click a layer in the list to change its settings."), StudioGUI.Hint);
            pending?.Invoke();
        }

        /// <summary>The layer list with its Add / Looks / order / delete buttons. Always shown, also before the first layer exists.</summary>
        void DrawLayerList()
        {
            Action pending = null;
            if (!StudioGUI.BeginFoldGroup(ref _openLayers, Loc.T("Layers"), _recipe.layers.Count == 0 ? Loc.T("none yet") : _recipe.layers.Count.ToString(),
                    _recipe.layers.Count == 0 ? Loc.T("none yet") : Loc.T("top = in front"), Advanced ? Loc.T("The layer list and history. Click to fold.") : Loc.T("The layer list. Click to fold."), "layer"))
            {
                StudioGUI.EndGroup();
                return;
            }
            StudioGUI.Count(_recipe.layers.Count == 0 ? "layers.start" : "layers.list");
            var list = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(list, StudioGUI.ListBg);
            for (int vi = 0; vi < _recipe.layers.Count; vi++)
                DrawLayerRow(_recipe.layers.Count - 1 - vi, vi, ref pending);
            if (_recipe.layers.Count == 0)
            {
                // Empty state, inside the list: say what will happen and offer the common starts.
                GUILayout.Label(Loc.T("No layers yet. Your first brush stroke adds a Paint layer, or start with:"), StudioGUI.Hint);
                // Two rows of two, each button allowed to shrink (text clips), so a narrow panel is never pushed wider.
                var startH = new[] { GUILayout.Height(20), GUILayout.MinWidth(0) };
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(Loc.T(" Change colours"), StudioGUI.Icon("PreTextureRGB", "").image, Loc.T("Change colours, keep shading: adds a Gradient Map layer with new colours and the same shading")), EditorStyles.miniButtonLeft, startH))
                        pending = () => InsertLayer(new GradientMapLayer());
                    if (GUILayout.Button(new GUIContent(Loc.T("Copy colours from…"), Loc.T("Take another material's colours, keeping this texture's detail")), EditorStyles.miniButtonRight, startH))
                        pending = () => AddColourMatchLayer();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(Loc.T(" Ready-made looks"), StudioGUI.Icon("Favorite", "").image, Loc.T("Ready-made looks: ombré, pastel, tints, mask fixes…")), EditorStyles.miniButtonLeft, startH))
                        ShowLayerPresetMenu();
                    if (GUILayout.Button(new GUIContent(Loc.T("More layer types"), Loc.T("Every layer type")), EditorStyles.miniButtonRight, startH))
                        ShowAddLayerMenu();
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

            // Footer chunks: create | order | manage, with the destructive button set apart and red.
            StudioGUI.Count("layers.toolbar");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(StudioGUI.Icon("Toolbar Plus", Loc.T(" Add"), Loc.T("Add a layer above the selected one")), EditorStyles.toolbarDropDown, GUILayout.MinWidth(40)))
                    ShowAddLayerMenu();
                if (GUILayout.Button(StudioGUI.Icon("Favorite", _panelContentWidth < 360f ? Loc.T(" Looks") : Loc.T(" Ready-made looks"), Loc.T("Ready-made looks (work on any part), or save yours")), EditorStyles.toolbarDropDown, GUILayout.MinWidth(40)))
                    ShowLayerPresetMenu();
                GUILayout.FlexibleSpace();
                int ai = ActiveIndex;
                using (new EditorGUI.DisabledScope(ai < 0))
                {
                    if (GUILayout.Button(new GUIContent("▲", Loc.T("Move up (in front)")), EditorStyles.toolbarButton, GUILayout.Width(24))) pending = () => MoveLayer(ai, +1);
                    if (GUILayout.Button(new GUIContent("▼", Loc.T("Move down (behind)")), EditorStyles.toolbarButton, GUILayout.Width(24))) pending = () => MoveLayer(ai, -1);
                    GUILayout.Space(6);
                    if (GUILayout.Button(StudioGUI.Icon("TreeEditor.Duplicate", "", Loc.T("Duplicate layer")), EditorStyles.toolbarButton, GUILayout.Width(26))) pending = () => DuplicateLayer(ai);
                    GUILayout.Space(10);
                    if (StudioGUI.DangerButton(StudioGUI.Icon("TreeEditor.Trash", "", Loc.T("Delete layer (Ctrl+Z restores it)")), EditorStyles.toolbarButton, GUILayout.Width(26)))
                        pending = () => RemoveLayerConfirmed(ai);
                }
            }
            StudioGUI.EndGroup();
            pending?.Invoke();
        }

        void ShowAddLayerMenu()
        {
            var menu = new GenericMenu();
            foreach (var (label, create) in TextureRecipe.LayerTypes)
            {
                var c = create;
                var sample = c();
                bool everyday = sample is PaintLayer || sample is HslLayer || sample is DrawnGradientLayer || sample is ImageLayer;
                if (!everyday && !Advanced) continue;
                string path = everyday ? NoSubmenu(LayerTypeLabel(label)) : Loc.T("More adjustments") + "/" + LayerTypeLabel(label);
                if (sample is PaintLayer) menu.AddItem(new GUIContent(path), false, () => { InsertLayer(c()); SetTab(Tab.Paint); });
                else menu.AddItem(new GUIContent(path), false, () => InsertLayer(c()));
            }
            if (Advanced)
            {
                menu.AddSeparator("");
                if (CurrentSlot != null && CurrentSlot.role == TextureRole.Color)
                    menu.AddItem(new GUIContent(Loc.T("More adjustments/Ambient occlusion (bake shading in creases)")), false, BakeAmbientOcclusion);
                else menu.AddDisabledItem(new GUIContent(Loc.T("More adjustments/Ambient occlusion (colour textures only)")));
            }
            menu.ShowAsContext();
        }

        void RemoveLayerConfirmed(int i)
        {
            if (i < 0 || i >= _recipe.layers.Count) return;
            var l = _recipe.layers[i];
            if (l is PaintLayer p && p.strokes.Count > 0 &&
                !StudioDialog.Ask(Loc.T("Delete layer"), Loc.F("Delete “{0}” and its {1} brush stroke(s)?\n\nCtrl+Z brings it back.", l.name, p.strokes.Count), Msg.BtnDelete, Msg.BtnCancel))
                return;
            string name = l.name;
            bool paint = l is PaintLayer;
            RemoveLayer(i);
            if (!paint) Toast(Msg.M10Deleted(name), MessageType.Info, 6f, Msg.BtnUndo, Undo.PerformUndo);
        }

        void DrawLayerRow(int i, int visualIndex, ref Action pending)
        {
            var l = _recipe.layers[i];
            if (l == null) return;
            var e = Event.current;
            var r = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            bool active = l.id == _activeLayerId;
            if (e.type == EventType.Repaint)
            {
                if (active) EditorGUI.DrawRect(r, StudioGUI.RowActive);
                else if (visualIndex % 2 == 1) EditorGUI.DrawRect(r, StudioGUI.RowAlt);
            }

            var eye = new Rect(r.x + 4, r.y + 4, 18, 18);
            var chip = new Rect(eye.xMax + 4, r.y + 7, 12, 12);
            string fullInfo = (l is PaintLayer p ? Loc.F("{0} stroke{1}", p.strokes.Count, p.strokes.Count == 1 ? "" : "s") + " · " : "") + $"{l.blend} {l.opacity * 100f:0}%";
            string flags = (l.hasPaintMask ? "◐ " : "") + (l.maskMode == MaskMode.SelectedIslands ? "◧ " : "");
            bool compact = _panelContentWidth < 360f; // narrow panel: the blend / opacity text moves into the tooltip so nothing clips
            string info = compact ? flags.Trim() : flags + fullInfo;
            bool isPart = l is RecolourPartLayer;
            float badgeW = isPart ? 32f : 0f;
            float infoW = info.Length == 0 ? 0f : Mathf.Min(StudioGUI.MiniRight.CalcSize(new GUIContent(info)).x + 4f, r.width * 0.5f);
            var nameRect = new Rect(chip.xMax + 6, r.y, Mathf.Max(20, r.xMax - infoW - badgeW - 8 - (chip.xMax + 6)), r.height);
            var badgeRect = new Rect(r.xMax - infoW - badgeW - 4, r.y + 6, badgeW - 4f, 14);
            var infoRect = new Rect(r.xMax - infoW - 4, r.y, infoW, r.height);
            var layer = l;
            var eyeIcon = StudioGUI.Icon(l.enabled ? "scenevis_visible_hover" : "scenevis_hidden_hover", l.enabled ? "" : "–", l.enabled ? Loc.T("Hide layer") : Loc.T("Show layer"));
            if (GUI.Button(eye, eyeIcon, GUIStyle.none))
                pending = () => { Undo.RecordObject(_recipe, "Toggle layer"); layer.enabled = !layer.enabled; MarkEdited(); };

            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(chip, StudioGUI.HeaderLine);
                EditorGUI.DrawRect(new Rect(chip.x + 1, chip.y + 1, chip.width - 2, chip.height - 2), StudioGUI.LayerChip(l));
            }
            GUI.Label(nameRect, new GUIContent((l.clipToBelow ? "↳ " : "") + l.name, l.TypeName + (l.clipToBelow ? " · " + Loc.T("clipped to the layer below") : "") + "\n" + fullInfo + "\n" + Loc.T("Right-click: copy or paste settings")), l.enabled ? StudioGUI.RowLabel : StudioGUI.RowLabelDisabled);
            string infoTip = (l.maskMode == MaskMode.SelectedIslands ? Loc.F("Only shows on {0} piece(s)", l.islands.Count) : Loc.T("Whole texture")) + (l.hasPaintMask ? " · " + Loc.T("has a painted layer mask") : "");
            if (info.Length > 0) GUI.Label(infoRect, new GUIContent(info, infoTip + " · " + fullInfo), StudioGUI.MiniRight);
            if (isPart) DrawPartBadge(badgeRect);

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

        /// <summary>Right-click menu on a layer row: copy its settings, or paste copied ones onto it.</summary>
        void ShowLayerRowMenu(Layer layer)
        {
            string clipType = EditorPrefs.GetString(LayerClipTypePref, "");
            string clipJson = EditorPrefs.GetString(LayerClipJsonPref, "");
            bool hasClip = clipType.Length > 0 && clipJson.Length > 0;
            bool sameType = hasClip && clipType == layer.GetType().Name;
            string id = layer.id;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(Loc.T("Copy settings"), Loc.T("Remember this layer's settings (not its paint, mask strokes or piece selection)")), false, () => CopyLayerSettings(id));
            if (sameType)
                menu.AddItem(new GUIContent(Loc.T("Paste settings"), Loc.T("Replace this layer's settings with the copied ones. Its name, paint, mask and piece selection stay.")), false, () => PasteLayerSettings(id));
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
            // Settings only: serialize a copy with the bulky paint data cleared so EditorPrefs stays small and the layer is untouched.
            var copy = (Layer)Activator.CreateInstance(layer.GetType());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(layer), copy);
            copy.maskStrokes = new List<PaintStroke>();
            if (copy is PaintLayer cp) cp.strokes = new List<PaintStroke>();
            EditorPrefs.SetString(LayerClipJsonPref, JsonUtility.ToJson(copy));
            Toast(Loc.F("Copied the settings of “{0}”. Right-click another {1} layer to paste them.", layer.name, layer.TypeName), MessageType.Info, 4f);
        }

        void PasteLayerSettings(string id)
        {
            int i = _recipe.layers.FindIndex(x => x != null && x.id == id);
            if (i < 0) return;
            var target = _recipe.layers[i];
            string json = EditorPrefs.GetString(LayerClipJsonPref, "");
            if (EditorPrefs.GetString(LayerClipTypePref, "") != target.GetType().Name || json.Length == 0) return;
            Layer merged;
            try { merged = (Layer)JsonUtility.FromJson(json, target.GetType()); }
            catch (Exception) { merged = null; }
            if (merged == null) { Toast(Loc.T("Couldn't read the copied settings. Copy them again."), MessageType.Warning, 5f); return; }

            // Keep what belongs to this layer: identity, name, where it shows, and anything painted.
            merged.id = target.id;
            merged.name = target.name;
            merged.enabled = target.enabled;
            merged.clipToBelow = target.clipToBelow;
            merged.expanded = target.expanded;
            merged.maskMode = target.maskMode;
            merged.islands = target.islands;
            merged.invertMask = target.invertMask;
            merged.hasPaintMask = target.hasPaintMask;
            merged.paintMaskStartsHidden = target.paintMaskStartsHidden;
            merged.maskStrokes = target.maskStrokes;
            if (merged is PaintLayer mp && target is PaintLayer tp) mp.strokes = tp.strokes;

            EndStroke();
            Undo.RecordObject(_recipe, "Paste layer settings");
            _recipe.layers[i] = merged;
            _activeLayerId = merged.id;
            MarkEdited();
            Toast(Loc.F("Pasted the copied settings onto “{0}”. Ctrl+Z undoes it.", target.name), MessageType.Info, 4f);
        }

        static GUIStyle _partBadge;
        static GUIStyle PartBadgeStyle => _partBadge ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };

        /// <summary>Small "part" tag on layers made in 2 · Colour › By part, so they don't pass for paint layers.</summary>
        static void DrawPartBadge(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(StudioGUI.InfoBlue.r, StudioGUI.InfoBlue.g, StudioGUI.InfoBlue.b, 0.28f));
                StudioGUI.DrawBorder(r, new Color(StudioGUI.InfoBlue.r, StudioGUI.InfoBlue.g, StudioGUI.InfoBlue.b, 0.7f), 1f);
            }
            GUI.Label(r, new GUIContent(Loc.T("part"), Loc.T("Made in 2 · Recolour › One piece at a time; edit it there")), PartBadgeStyle);
        }

        /// <summary>Opens 2 · Colour › By part with this part chosen.</summary>
        void EditPartInColour(RecolourPartLayer part)
        {
            if (part == null) return;
            if (!ByPart) EnterByPart();
            SelectPart(part);
            SetTab(Tab.Colour);
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

        void DrawActiveLayerSettings(ref Action pending)
        {
            int i = ActiveIndex;
            var layer = _recipe.layers[i];
            if (!StudioGUI.BeginFoldGroup(ref _openLayerSettings, Loc.F("{0} settings", layer.TypeName), layer.name, null, Loc.T("The settings of the layer selected in the list. Click to fold."), "layer"))
            {
                StudioGUI.EndGroup();
                return;
            }

            // Common settings, edited directly (cheap: no serialisation of paint strokes every repaint).
            EditorGUI.BeginChangeCheck();
            string n = layer.name;
            InfoRow("layers.name", "layer", () => n = EditorGUILayout.DelayedTextField(Loc.T("Name"), layer.name));
            var b = layer.blend;
            InfoRow("layers.blend", "blend-mode", () => b = BlendPopup(new GUIContent(Loc.T("Blend"), Loc.T("How this layer mixes with what's below (Normal, Multiply, Color…)")), layer.blend));
            float o = layer.opacity;
            InfoRow("layers.opacity", "opacity", () => o = EditorGUILayout.Slider(Loc.T("Layer opacity"), layer.opacity, 0f, 1f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_recipe, "Edit layer");
                layer.name = n;
                if (layer.blend != b) LearnLater(LearnEvents.LayerBlend);
                if (!Mathf.Approximately(layer.opacity, o)) LearnLater(LearnEvents.LayerOpacity);
                layer.blend = b;
                layer.opacity = o;
                MarkEdited();
            }

            StudioGUI.Divider();
            if (layer is RecolourPartLayer partLayer)
            {
                GUILayout.Label(Loc.T("This layer was made in 2 · Recolour › One piece at a time. Edit it there."), StudioGUI.Hint);
                StudioGUI.Count("layers.editPart");
                if (GUILayout.Button(new GUIContent(Loc.T("Edit in 2 · Recolour"), Loc.T("Opens 2 · Recolour › One piece at a time with this part chosen")), EditorStyles.miniButton))
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
            else if (!(layer is RecolourPartLayer))
            {
                StudioGUI.Count("layers.fields");
                DrawTypeSpecificFields(i, layer); // a part's colour is set in 2 · Colour, not as raw fields here
            }

            // Where the layer shows: islands, clipping, layer mask, channels. Folded; the title lists what's in use.
            StudioGUI.Divider();
            var inUse = new List<string>();
            if (layer.maskMode == MaskMode.SelectedIslands) inUse.Add(layer.invertMask ? Loc.T("all but some pieces") : Loc.T("some pieces"));
            if (layer.clipToBelow) inUse.Add(Loc.T("clipped"));
            if (layer.hasPaintMask) inUse.Add(Loc.T("mask"));
            if (!layer.chR || !layer.chG || !layer.chB || layer.chA) inUse.Add(Loc.T("channels"));
            bool editingMask = MaskEditing && _maskEditLayerId == layer.id;
            string title = inUse.Count == 0 ? Loc.T("Limit this layer (pieces, clip, mask)") : Loc.F("Limit this layer: {0}", string.Join(", ", inUse));
            bool open = false;
            InfoRow("layers.showsOn", "island", () => open = EditorGUILayout.Foldout(_foldLayerWhere || editingMask, new GUIContent(title,
                Loc.T("Limit this layer to some pieces, clip it to the layer below, or paint a mask to hide parts of it")), true));
            if (!editingMask) _foldLayerWhere = open;
            if (open || editingMask)
            {
                EditorGUI.BeginChangeCheck();
                StudioGUI.Count("layers.limit");
                var mr = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(Loc.T("Shows on"), Loc.T("Limit this layer to some pieces")));
                var m = (MaskMode)GUI.Toolbar(mr, (int)layer.maskMode, new[] { Loc.T("Everything"), Loc.T("Some pieces") }, EditorStyles.miniButton);
                bool inv = layer.invertMask;
                if (m == MaskMode.SelectedIslands)
                {
                    StudioGUI.Count("layers.useSelection");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Loc.F("{0} piece(s)", layer.islands.Count), EditorStyles.miniLabel, GUILayout.MinWidth(10));
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent(Loc.T("Use selection"), Loc.T("Replace with the pieces selected now")), EditorStyles.miniButtonLeft, GUILayout.MinWidth(10))) pending = () => CaptureSelection(layer);
                        if (GUILayout.Button(new GUIContent(Loc.T("Show them"), Loc.T("Select this layer's pieces")), EditorStyles.miniButtonRight, GUILayout.MinWidth(10))) pending = () => SetSelection(layer.islands);
                    }
                    StudioGUI.Count("layers.invertLimit");
                    inv = EditorGUILayout.Toggle(new GUIContent(Loc.T("Everything except them")), inv);
                    GUILayout.Label(inv ? Loc.T("This layer shows everywhere except on these pieces, whatever you paint.") : Loc.T("This layer only shows on these pieces, whatever you paint."), StudioGUI.Hint);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_recipe, "Edit layer");
                    if (m == MaskMode.SelectedIslands && layer.maskMode != m && layer.islands.Count == 0) layer.islands = new List<int>(_selection);
                    layer.maskMode = m;
                    layer.invertMask = inv;
                    MarkEdited();
                }
                DrawClipAndMaskControls(i, layer, ref pending);
                if (Advanced) DrawChannelSwitches(layer, ref pending);
            }
            else if (Advanced && CurrentSlot != null && CurrentSlot.role == TextureRole.Mask)
                DrawChannelSwitches(layer, ref pending); // on mask textures the channels matter, so they stay visible
            StudioGUI.EndGroup();
        }

        /// <summary>The unclipped layer a clipped layer at index i clips to (null if there is none).</summary>
        Layer ClipBaseOf(int i)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                var l = _recipe.layers[j];
                if (l != null && !l.clipToBelow) return l;
            }
            return null;
        }

        // The import issues this mode shows. MaterialWriter hands back the same list until it rebuilds, so the filtered copy
        // is rebuilt only when that list or the mode changes, not on every redraw.
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

        /// <summary>Advanced: every blend mode. Simple: the three everyday ones, plus the current one so it never changes silently.</summary>
        static BlendMode BlendPopup(GUIContent label, BlendMode current)
        {
            if (StudioMode.Advanced) return (BlendMode)EditorGUILayout.EnumPopup(label, current);
            if (!s_simpleBlendLists.TryGetValue(current, out var lists))
            {
                var modes = new List<BlendMode>(SimpleBlendModes);
                if (!modes.Contains(current)) modes.Add(current);
                lists = (modes.ToArray(), modes.Select(m => ObjectNames.NicifyVariableName(m.ToString())).ToArray());
                s_simpleBlendLists[current] = lists;
            }
            return lists.modes[EditorGUILayout.Popup(label, Array.IndexOf(lists.modes, current), lists.names)];
        }

        /// <summary>CLIP STUDIO PAINT-style "Clip to layer below" and a paintable layer mask, for every layer type.</summary>
        void DrawClipAndMaskControls(int i, Layer layer, ref Action pending)
        {
            var l = layer;
            GUILayout.Space(2);
            var baseLayer = ClipBaseOf(i);
            if (Advanced)
            {
                StudioGUI.Count("layers.clip");
                using (new EditorGUI.DisabledScope(baseLayer == null && !layer.clipToBelow))
                {
                    bool clip = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Clip to layer below"),
                        Loc.T("Only show this layer where the layer under it shows. Use it to shade or add highlights inside a base colour without going over its edges.")), layer.clipToBelow);
                    if (clip != layer.clipToBelow) pending = () => SetClipToBelow(l, clip);
                }
                if (layer.clipToBelow)
                    GUILayout.Label(baseLayer == null ? Loc.T("Nothing below to clip to, so it's shown normally.")
                        : !baseLayer.enabled || baseLayer.opacity <= 0.001f ? Loc.F("Shows only where “{0}” shows. That layer is hidden, so this one is too.", baseLayer.name)
                        : Loc.F("Shows only where “{0}” shows.", baseLayer.name), StudioGUI.Hint);
                else if (baseLayer == null)
                    GUILayout.Label(Loc.T("The bottom layer can't be clipped: there's nothing below it."), StudioGUI.Hint);
            }

            StudioGUI.Count("layers.hideMask");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(Loc.T("Hide/reveal mask"), Loc.T("Paint to hide or reveal parts of this layer without deleting anything")), GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                if (!layer.hasPaintMask)
                {
                    if (GUILayout.Button(new GUIContent(Loc.T("Add mask ▾"), Loc.T("Add a mask you paint with the brush")), EditorStyles.miniPullDown))
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent(Loc.T("Show everything (paint to hide parts)")), false, () => { AddPaintMask(l, false); SetMaskEditing(l, true); });
                        menu.AddItem(new GUIContent(Loc.T("Hide everything (paint to reveal parts)")), false, () => { AddPaintMask(l, true); SetMaskEditing(l, true); });
                        menu.ShowAsContext();
                    }
                }
                else
                {
                    bool editing = MaskEditing && _maskEditLayerId == layer.id;
                    var oldBg = GUI.backgroundColor;
                    if (editing) GUI.backgroundColor = StudioGUI.Accent;
                    if (GUILayout.Button(new GUIContent(editing ? Loc.T("Painting…") : Loc.T("Paint mask"), editing ? Loc.T("Stop painting the mask") : Loc.T("Paint on this layer's mask (Reveal / Hide)")), EditorStyles.miniButtonLeft))
                        pending = () => SetMaskEditing(l, !editing);
                    GUI.backgroundColor = oldBg;
                    if (GUILayout.Button(new GUIContent(Loc.T("Invert"), Loc.T("Swap hidden and shown")), EditorStyles.miniButtonMid)) pending = () => InvertPaintMask(l);
                    if (StudioGUI.DangerButton(new GUIContent(Loc.T("Delete"), Loc.T("Remove the mask: the whole layer shows again")), EditorStyles.miniButtonRight))
                    {
                        if (layer.maskStrokes.Count == 0 || StudioDialog.Ask(Loc.T("Delete layer mask"),
                                Loc.F("Delete the mask of “{0}” ({1} stroke(s))? The whole layer will show again.\n\nCtrl+Z brings it back.", layer.name, layer.maskStrokes.Count), Loc.T("Delete"), Loc.T("Cancel")))
                            pending = () => { DeletePaintMask(l); GUIUtility.ExitGUI(); };
                    }
                }
                StudioGUI.Info("mask");
            }
            if (layer.hasPaintMask)
                GUILayout.Label(layer.paintMaskStartsHidden ? Loc.F("{0} mask stroke(s) · started as hide everything.", layer.maskStrokes.Count) : Loc.F("{0} mask stroke(s) · started as show everything.", layer.maskStrokes.Count), StudioGUI.Hint);
        }

        SerializedObject _fieldsSo;

        void DrawTypeSpecificFields(int i, Layer layer)
        {
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
                EditorGUILayout.PropertyField(it, true);
            }
            if (layer is RootTipGradientLayer)
                GUILayout.Label(Loc.T("Transparent gradient keys leave the texture untouched."), StudioGUI.Hint);
            if (IsAoLayer(layer)) DrawAoLayerNote();
            if (so.ApplyModifiedProperties()) MarkEdited();
        }

        void ShowLayerPresetMenu()
        {
            var menu = new GenericMenu();
            foreach (var (path, make) in LayerPresets.BuiltIn)
            {
                var mk = make;
                menu.AddItem(new GUIContent(LayerPresetLabel(path)), false, () => ApplyLayerPreset(mk(), LayerPresetLabel(path).Substring(LayerPresetLabel(path).LastIndexOf('/') + 1)));
            }

            menu.AddSeparator("");
            var user = LayerPresets.UserPresets();
            if (user.Count == 0) menu.AddDisabledItem(new GUIContent(Loc.T("My looks/(none saved yet)")));
            foreach (var preset in user)
            {
                var p = preset;
                menu.AddItem(new GUIContent("My looks/" + NoSubmenu(p.name)), false, () => ApplyLayerPreset(LayerPresets.LayersOf(p), p.name));
            }

            menu.AddSeparator("");
            if (_recipe.layers.Any(l => l != null && !(l is PaintLayer)))
                menu.AddItem(new GUIContent(Loc.T("Save these layers as my look…")), false, () =>
                {
                    var saved = LayerPresets.SaveAsPreset(_recipe);
                    if (saved != null) Toast(Loc.F("Saved look {0}. Find it under Ready-made looks › My looks.", System.IO.Path.GetFileNameWithoutExtension(saved)), MessageType.Info);
                });
            else menu.AddDisabledItem(new GUIContent(Loc.T("Save these layers as my look… (add a layer first)")));
            menu.AddItem(new GUIContent(Loc.T("Show my looks folder")), false, () =>
            {
                var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LayerPresets.UserFolder);
                if (folder != null) { EditorGUIUtility.PingObject(folder); Learn.Did(LearnEvents.AssetPinged); }
                else Toast(Loc.F("No looks saved yet. They go in {0}.", LayerPresets.UserFolder), MessageType.Info);
            });
            menu.ShowAsContext();
        }

        string _sharedKey;
        int _sharedOutside;
        int _sharedStamp = -1; double _sharedTime;
        static int _envStamp; // bumped when the scene, project or undo state changes, so caches below refresh
        static readonly List<Material> _matScratch = new List<Material>();

        [InitializeOnLoadMethod]
        static void HookEnvStamp()
        {
            EditorApplication.hierarchyChanged += () => _envStamp++;
            EditorApplication.projectChanged += () => _envStamp++;
            Undo.undoRedoPerformed += () => _envStamp++;
        }

        /// <summary>Meshes in the open scenes, outside this avatar, that use the edited material (cached until the scene changes).</summary>
        int MeshesOutsideAvatarUsingMaterial()
        {
            string key = (_material != null ? _material.GetInstanceID() : 0) + ":" + (_renderer != null ? _renderer.GetInstanceID() : 0);
            if (key != _sharedKey || _sharedStamp != _envStamp || EditorApplication.timeSinceStartup - _sharedTime > 5.0) // 5s fallback: material swaps raise no editor event
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

        /// <summary>
        /// Says who else is affected by saving: other meshes on this avatar that share the material, and (when the
        /// existing material is updated) every other avatar or prefab using it. Null when only this mesh uses it
        /// and the original material isn't touched.
        /// </summary>
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

        /// <summary>A checklist note with a one-click fix button under it.</summary>
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

        string _predKey, _bakeExistsKey;
        int _predStamp = -1, _bakeExistsStamp = -1;
        Exporter.Result _pred;
        bool _bakeExists;

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

        bool LastBakePngExists()
        {
            if (string.IsNullOrEmpty(_lastBakePng)) return false;
            if (_lastBakePng != _bakeExistsKey || _bakeExistsStamp != _envStamp)
            {
                _bakeExistsKey = _lastBakePng; _bakeExistsStamp = _envStamp;
                _bakeExists = AssetDatabase.LoadAssetAtPath<Texture2D>(_lastBakePng) != null;
            }
            return _bakeExists;
        }

        void DrawSaveTab()
        {
            var slot = CurrentSlot;
            bool hasLayers = _recipe.layers.Count > 0;
            int visible = 0;
            foreach (var l in _recipe.layers) if (l != null && l.enabled && l.opacity > 0.001f) visible++;
            // A save empties the layer list; the checklist describes the next save, so it waits for a new edit or Undo.
            bool justSavedEmpty = CurrentSaveState == SaveState.Saved && !hasLayers;

            // The end moment (peak-end): a clear, persistent result with somewhere to go.
            if (CurrentSaveState == SaveState.Saved)
            {
                StudioGUI.BeginGroup(Loc.T("Saved"));
                StudioGUI.CheckRow(StudioGUI.Check.Ok, Msg.M1Saved(System.IO.Path.GetFileNameWithoutExtension(_lastBakePng), string.IsNullOrEmpty(_lastBakeMaterial)));
                if (_lastBakeClearedLayers && !hasLayers)
                    GUILayout.Label(Loc.T("Your layers are now part of that texture, so the list starts empty. Ctrl+Z brings them back."), StudioGUI.Hint);
                StudioGUI.Count("save.showPng");
                StudioGUI.Count("save.showMaterial");
                StudioGUI.Count("save.showEdit");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Msg.BtnShowFile, EditorStyles.miniButtonLeft)) ShowFileAndLearn(_lastBakePng);
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastBakeMaterial)))
                        if (GUILayout.Button(Loc.T("Show material"), EditorStyles.miniButtonMid)) Ping(_lastBakeMaterial);
                    if (GUILayout.Button(new GUIContent(Loc.T("Show saved edit"), Loc.T("The layers that made this PNG, to reopen later")), EditorStyles.miniButtonRight)) Ping(_lastBakeRecipe);
                }
                StudioGUI.EndGroup();
            }

            if (!justSavedEmpty)
            {
                StudioGUI.BeginGroup(Loc.T("Before you save"), null, "bake");
                StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("Editing <b>{0}</b> ({1}).", slot?.texture.name, TextureSlot.RoleLabel(slot?.role ?? TextureRole.Color)));
                if (!hasLayers) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("No edits yet. Paint in 4 · Paint, or add a layer there, first."));
                else if (visible == 0) StudioGUI.CheckRow(StudioGUI.Check.Problem, Loc.T("All layers are hidden or at 0% opacity, so the result would look unchanged."));
                else StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("{0} visible layer{1}.", visible, (visible == 1 ? "" : "s")));
                if (_editThatMadeTexture != null)
                    StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("This texture is already an edited copy; the new PNG will include both edits."));
                var sharedNote = SharedMaterialNote();
                if (sharedNote != null)
                {
                    if (_outputMode == OutputMode.UpdateOriginalMaterial) NoteWithFix(StudioGUI.Check.Note, sharedNote, Msg.BtnUseCopy, UseCopyInstead, "save.useCopyShared");
                    else StudioGUI.CheckRow(StudioGUI.Check.Note, sharedNote);
                }
                if (_outputMode == OutputMode.PngOnly)
                    NoteWithFix(StudioGUI.Check.Note, Msg.M6ImageOnly(), Msg.BtnUseCopy, UseCopyInstead, "save.useCopyImageOnly");
                StudioGUI.EndGroup();
            }

            StudioGUI.BeginGroup(Loc.T("Save as"), null, "png");
            var outputChoices = new[]
            {
                new GUIContent(Loc.T("New PNG + new material copy (recommended)"), Loc.T("Creates a new PNG and a copy of the material, and puts that copy on this avatar. The original material and texture stay untouched.")),
                new GUIContent(Loc.T("New PNG, update the existing material"), Loc.T("Creates a new PNG and points the existing material at it. Every part, avatar and prefab that uses that material changes too.")),
                new GUIContent(Loc.T("New PNG only"), Loc.T("Just creates the PNG; no material and nothing on the avatar changes.")),
            };
            InfoRow("save.saveAs", "png", () => _outputMode = SaveAsPopup(outputChoices, _outputMode));
            if (slot != null && _material != null)
            {
                var p = PredictedPaths(slot);
                GUILayout.Label(Loc.F("Creates: {0}", System.IO.Path.GetFileName(p.pngPath)) +
                                (p.materialPath != null ? $", {System.IO.Path.GetFileName(p.materialPath)}" : "") +
                                $", {System.IO.Path.GetFileName(p.recipePath)}", StudioGUI.Hint);
            }
            if (Advanced)
            {
                StudioGUI.Count("save.options");
                var advanced = EditorGUILayout.Foldout(_foldHelp, Loc.T("Advanced"), true);
                if (advanced != _foldHelp) _foldHelp = advanced;
                if (_foldHelp)
                {
                    StudioGUI.Count("save.dither");
                    _dither = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Dither (prevents banding in smooth gradients)")), _dither);
                }
            }
            GUILayout.Space(4);

            string blocker = !hasLayers ? (!string.IsNullOrEmpty(_lastBakePng) ? Loc.T("Saved. Add a layer to save again") : Loc.T("Nothing to save yet")) : visible == 0 ? Loc.T("All layers are hidden") : null;
            StudioGUI.Count("save.save");
            using (new EditorGUI.DisabledScope(blocker != null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (StudioGUI.PrimaryButton(StudioGUI.Icon("SaveAs", blocker ?? Loc.T("  Save as new PNG"), Loc.T("Render at full resolution and save a new PNG")), StudioGUI.BakeButton, 34f))
                    {
                        Bake();
                        GUIUtility.ExitGUI(); // dialogs/progress bar ran mid-OnGUI: abandon this pass now the state is settled
                    }
                    StudioGUI.Info("png");
                }
            }
            StudioGUI.EndGroup();

            StudioGUI.BeginGroup(Loc.T("Not finished?"), null, "asset");
            GUILayout.Label(Loc.T("Keep your layers without making a PNG, and continue another time (open it from 1 · Pick)."), StudioGUI.Hint);
            StudioGUI.Count("save.saveLater");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasLayers))
                    if (GUILayout.Button(new GUIContent(Loc.T("Save edit for later"), Loc.T("Saves the layers next to the texture. Nothing is baked.")))) SaveEditAsset();
                StudioGUI.Info("asset");
            }
            if (!string.IsNullOrEmpty(_lastSavedEdit) && !_unsaved)
                GUILayout.Label(Loc.F("Last saved: {0}", System.IO.Path.GetFileName(_lastSavedEdit)), StudioGUI.Hint);
            StudioGUI.EndGroup();
        }

        /// <summary>Simple hides the modes that touch other avatars or create no material; a hidden mode already chosen stays listed so it never stays active unseen.</summary>
        static OutputMode SaveAsPopup(GUIContent[] all, OutputMode current)
        {
            if (StudioMode.Advanced) return (OutputMode)EditorGUILayout.Popup(GUIContent.none, (int)current, all);
            var modes = new List<OutputMode> { OutputMode.DuplicateMaterial };
            if (!modes.Contains(current)) modes.Add(current);
            int picked = EditorGUILayout.Popup(GUIContent.none, modes.IndexOf(current), modes.Select(m => all[(int)m]).ToArray());
            return modes[picked];
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
                GUILayout.Label(Loc.T("Tip: recolour with Layers › Recolour (keeps the shading), fade colours with an Ombré Gradient, " +
                                "and paint details with “Brush stays inside the piece it starts on” on. Nothing touches your files until you save."), StudioGUI.Hint);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Loc.F("Avistry {0}  ·  by Avistry contributors", Version), EditorStyles.centeredGreyMiniLabel);
            }
        }
    }
}
