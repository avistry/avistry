using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        readonly Dictionary<string, List<ColourMatch.Swatch>> _cmSourceColours = new Dictionary<string, List<ColourMatch.Swatch>>();

        static bool IsColourMatchTexture(TextureSlot slot) => slot != null && slot.texture != null && slot.role == TextureRole.Color;

        bool CanAddColourMatchLayer(out string why)
        {
            if (_needsRebuild || !IsColourMatchTexture(CurrentSlot))
            {
                why = Loc.T("Choose a colour texture in Pick to use Colour Match.");
                return false;
            }
            return TabAvailable(Tab.Paint, out why);
        }

        void ShowColourMatchBlocked(string why) => Toast(why, MessageType.Warning, 6f, Msg.BtnGoToPick, () => SetTab(Tab.Target));

        Action OnCurrentColourMatchTexture(Action action)
        {
            var preview = _comp;
            return () =>
            {
                if (this == null) return;
                if (_needsRebuild || _comp != preview)
                {
                    ShowColourMatchBlocked(Loc.T("The texture has changed. Choose a colour texture in Pick and try again."));
                    return;
                }
                action();
            };
        }

        void DrawAddColourMatchButton(float sceneWidth = 0f)
        {
            if (!CanAddColourMatchLayer(out var why))
            {
                GUILayout.Label(why, StudioGUI.Hint);
                bool goToPick = GUILayout.Button(Msg.BtnGoToPick);
                StudioGUI.Count("colour.matchGoPick", GUILayoutUtility.GetLastRect());
                if (goToPick) SetTab(Tab.Target);
                return;
            }
            var label = new GUIContent(Loc.T("Colour Match"), Loc.T("Match colours to another material…") + "\n" + Loc.T("Adds a Colour Match layer in 4 · Paint"));
            var rect = sceneWidth > 0f ? SceneBarRow(sceneWidth, 26f) : EditorGUILayout.GetControlRect(false, 26f);
            bool add = DrawRecolourButton(rect, SelectGlyph.Match, label, sceneWidth > 0f);
            StudioGUI.Count(sceneWidth > 0f ? "scene.colourMatch" : "colour.copyFrom", rect);
            if (add) EditorApplication.delayCall += OnCurrentColourMatchTexture(AddColourMatchLayer).Invoke;
        }

        (ColourMatchLayer layer, Compositor compositor) _matchCardsToReveal;

        void ValidateMatchCardsReveal()
        {
            if (_matchCardsToReveal.layer != null && (_tab != Tab.Paint || _matchCardsToReveal.compositor != _comp ||
                _matchCardsToReveal.layer != ActiveLayer))
                _matchCardsToReveal = default;
        }

        void AddColourMatchLayer()
        {
            if (!CanAddColourMatchLayer(out var why)) { ShowColourMatchBlocked(why); return; }
            var layer = new ColourMatchLayer();
            InsertLayer(layer, "Add Colour Match");
            SetTab(Tab.Paint);
            if (_tab == Tab.Paint) _matchCardsToReveal = (layer, _comp);
            Toast(Msg.M26ColourMatchAdded() +
                  (_selection.Count == 0 ? " " + Loc.T("Tip: select the pieces of one part in Limit first to match only that part.") : ""), MessageType.Info, 7f);
        }

        List<Material> AvatarMaterialChoices()
        {
            var root = MaterialAudit.FindAvatarRoot(_renderer);
            if (root == null) return new List<Material>();
            return root.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m != _material)
                .Distinct()
                .OrderBy(m => m.name)
                .ToList();
        }

        void DrawColourMatchSettings(ColourMatchLayer cm, ref Action pending)
        {
            var l = cm;
            var srcColours = MatchSourceColours(cm);

            bool compact = PanelContentWidth < 310f;
            float cardW = compact ? PanelContentWidth : (PanelContentWidth - 26f) * 0.5f;
            using (IDisposable layout = compact ? (IDisposable)new EditorGUILayout.VerticalScope() : new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(cardW)))
                {
                    GUILayout.Label(new GUIContent(Loc.T("This texture"), Loc.T("The texture you're editing: its colours change")), EditorStyles.miniBoldLabel);
                    if (!compact) DrawMatchThumb(CurrentSlot?.texture, cardW, null);
                    GUILayout.Label(new GUIContent(_material != null ? _material.name : "–", CurrentSlot?.texture != null ? CurrentSlot.texture.name : null), EditorStyles.miniLabel, GUILayout.Width(cardW - 8));
                    if (!compact) GUILayout.Space(20);
                    DrawSwatchStrip(cm.from, cardW, Loc.T("A main colour of this texture"), false);
                }
                if (!compact)
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(18)))
                    {
                        GUILayout.Space(30);
                        GUILayout.Label("←", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(18));
                        DrawMatchSwap(cm, ref pending);
                    }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(cardW)))
                {
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Width(cardW - 8)))
                    {
                        GUILayout.Label(new GUIContent(Loc.T("Match to"), Loc.T("The material or texture whose colours this texture takes on")), EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();
                        bool shortLabels = cardW < 190f;
                        int currentMode = cm.useTexture ? 1 : 0;
                        var modeRect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, EditorStyles.miniButton,
                            GUILayout.Width(Mathf.Min(116f, cardW - 66f)));
                        int mode = DrawStepSelectionGrid(modeRect, currentMode, new[]
                        {
                            new GUIContent(shortLabels ? Loc.T("Mat.") : Loc.T("Material"), Loc.T("Take the colours of a material (what its main texture looks like on the avatar)")),
                            new GUIContent(shortLabels ? Loc.T("Tex.") : Loc.T("Texture"), Loc.T("Take the colours of any texture in your project, e.g. a reference image or another outfit's texture")),
                        }, 2, EditorStyles.miniButton);
                        StudioGUI.Count("layers.matchMode", modeRect);
                        if (mode != currentMode)
                            pending = () => ChangeRecolourChoice(() => SetMatchMode(l, mode == 1));
                    }
                    Texture srcTex;
                    Color? flat = null;
                    if (cm.useTexture) srcTex = cm.sourceTexture;
                    else
                    {
                        srcTex = MaterialLook.MainTexture(cm.source);
                        if (cm.source != null && srcTex == null) flat = MaterialLook.Tint(cm.source);
                    }
                    DrawMatchThumb(srcTex, cardW, flat);
                    EditorGUI.BeginChangeCheck();
                    if (cm.useTexture)
                    {
                        var tex = (Texture2D)EditorGUILayout.ObjectField(GUIContent.none, cm.sourceTexture, typeof(Texture2D), false, GUILayout.Width(cardW - 8));
                        if (EditorGUI.EndChangeCheck()) pending = () => SetMatchTexture(l, tex, true);
                    }
                    else
                    {
                        var src = (Material)EditorGUILayout.ObjectField(GUIContent.none, cm.source, typeof(Material), false, GUILayout.Width(cardW - 8));
                        if (EditorGUI.EndChangeCheck()) pending = () => SetMatchSource(l, src, true);
                    }
                    StudioGUI.Count("layers.matchSource", GUILayoutUtility.GetLastRect());
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Width(cardW - 8)))
                    {
                        StudioGUI.Count("layers.matchAvatar");
                        if (GUILayout.Button(new GUIContent(Loc.T("Avatar ▾"), cm.useTexture ? Loc.T("Pick one of the textures this avatar's materials use") : Loc.T("Pick one of this avatar's materials")), EditorStyles.miniButtonLeft))
                        {
                            var menu = new GenericMenu();
                            if (cm.useTexture)
                            {
                                var texs = AvatarTextureChoices();
                                if (texs.Count == 0) menu.AddDisabledItem(new GUIContent(Loc.T("No other textures on this avatar's materials")));
                                foreach (var (label, t) in texs)
                                {
                                    var tt = t;
                                    menu.AddItem(new GUIContent(label), t == cm.sourceTexture, () => SetMatchTexture(l, tt, true));
                                }
                            }
                            else
                            {
                                var mats = AvatarMaterialChoices();
                                if (mats.Count == 0) menu.AddDisabledItem(new GUIContent(Loc.T("No other materials on this avatar")));
                                foreach (var m in mats)
                                {
                                    var mm = m;
                                    menu.AddItem(new GUIContent(NoSubmenu(m.name)), m == cm.source, () => SetMatchSource(l, mm, true));
                                }
                            }
                            menu.ShowAsContext();
                        }
                        bool pickingSource = _matchPick == MatchPick.Source && _matchPickLayerId == cm.id;
                        var oldBg = GUI.backgroundColor;
                        if (pickingSource) GUI.backgroundColor = StudioGUI.Accent;
                        StudioGUI.Count("layers.matchScene");
                        if (GUILayout.Button(new GUIContent(Loc.T("Scene"), StudioGUI.Icon("Grid.PickingTool", "").image, Loc.T("Click the part to match in the Scene view (for example the outfit)")), EditorStyles.miniButtonRight))
                        {
                            if (pickingSource) CancelMatchPick(); else StartMatchPick(l, MatchPick.Source);
                        }
                        GUI.backgroundColor = oldBg;
                        if (compact) DrawMatchSwap(cm, ref pending);
                    }
                    DrawSwatchStrip(srcColours.Select(s => s.colour).ToList(), cardW, Loc.T("A main colour of what this matches to · click to use as the brush colour"), true);
                }
            }
            if (Event.current.type == EventType.Repaint && _matchCardsToReveal.layer == cm)
            {
                bool sameTarget = _matchCardsToReveal.compositor == _comp;
                _matchCardsToReveal = default;
                var cards = GUILayoutUtility.GetLastRect();
                if (sameTarget && (cards.yMin < _panelScroll.y || cards.yMax > _panelScroll.y + _learnViewportHeight))
                {
                    _panelScroll.y = Mathf.Max(0f, cards.yMin - 8f);
                    Repaint();
                }
            }

            DrawGlowWarning(cm);
            DrawMaterialLookNotes(cm);

            DrawMatchPairing(cm, ref pending);
            DrawMatchPaletteSearch(cm, ref pending);
            DrawAddPairButton(cm);
            DrawModelMatch(cm);

            if (cm.from.Count == 0)
            {
                GUILayout.Label(!cm.HasSource
                    ? (cm.useTexture ? Loc.T("Choose a reference texture or add pairs from the Scene view.") : Loc.T("Choose a reference material or add pairs from the Scene view."))
                    : Loc.T("Press Find colours, or add pairs from the Scene view."), StudioGUI.Hint);
                DrawMatchMaterialSettings(cm);
                return;
            }

            GUILayout.Space(2);
            GUILayout.Label(Loc.T("This texture  →  becomes"), EditorStyles.miniBoldLabel);
            StudioGUI.Count("layers.matchPairs");
            for (int i = 0; i < Mathf.Min(cm.from.Count, cm.to.Count); i++)
            {
                int idx = i;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var r = GUILayoutUtility.GetRect(30, 18, GUILayout.Width(30));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(r, StudioGUI.HeaderLine);
                        EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), cm.from[i]);
                    }
                    GUI.Label(r, new GUIContent("", Loc.F("#{0}: a main colour of this texture", ColorUtility.ToHtmlStringRGB(cm.from[i]))));
                    GUILayout.Label("→", GUILayout.Width(14));
                    EditorGUI.BeginChangeCheck();
                    var c = EditorGUILayout.ColorField(GUIContent.none, cm.to[i], false, false, false, GUILayout.MinWidth(40));
                    if (EditorGUI.EndChangeCheck()) pending = () => { Undo.RecordObject(_recipe, "Colour Match"); l.to[idx] = new Color(c.r, c.g, c.b, 1f); MarkEdited(); };
                    if (GUILayout.Button(new GUIContent("▾", Loc.T("Pick one of the other material's colours")), EditorStyles.miniButtonLeft, GUILayout.Width(22)))
                    {
                        var menu = new GenericMenu();
                        if (srcColours.Count == 0) menu.AddDisabledItem(new GUIContent(Loc.T("Pick a material to match first")));
                        for (int s = 0; s < srcColours.Count; s++)
                        {
                            var sc = srcColours[s].colour;
                            menu.AddItem(new GUIContent(Loc.F("Colour {0}  ({1}% of it)  #{2}", s + 1, Mathf.RoundToInt(srcColours[s].share * 100), ColorUtility.ToHtmlStringRGB(sc))), false,
                                () => { Undo.RecordObject(_recipe, "Colour Match"); l.to[idx] = sc; MarkEdited(); });
                        }
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent(Loc.T("Keep this colour as it is")), false, () => { Undo.RecordObject(_recipe, "Colour Match"); l.to[idx] = l.from[idx]; MarkEdited(); });
                        menu.ShowAsContext();
                    }
                    bool pickingRow = _matchPick == MatchPick.ToColour && _matchPickLayerId == cm.id && _matchPickRow == i;
                    var rowBg = GUI.backgroundColor;
                    if (pickingRow) GUI.backgroundColor = StudioGUI.Accent;
                    if (GUILayout.Button(StudioGUI.Icon("Grid.PickingTool", "", Loc.T("Pick the colour this becomes by clicking any part of the avatar in the Scene view")), EditorStyles.miniButtonMid, GUILayout.Width(24)))
                    {
                        if (pickingRow) CancelMatchPick(); else StartMatchPick(l, MatchPick.ToColour, idx);
                    }
                    GUI.backgroundColor = rowBg;
                    if (GUILayout.Button(new GUIContent("×", Loc.T("Remove this pair (those colours stay as they are)")), EditorStyles.miniButtonRight, GUILayout.Width(20)))
                        pending = () =>
                        {
                            Undo.RecordObject(_recipe, "Remove match pair");
                            if (idx < l.from.Count) l.from.RemoveAt(idx);
                            if (idx < l.to.Count) l.to.RemoveAt(idx);
                            MarkEdited();
                        };
                }
            }

            GUILayout.Space(2);
            StudioGUI.Count("layers.matchTune");
            _foldMatchTune = StudioGUI.Foldout(_foldMatchTune, new GUIContent(Loc.T("Fine-tune (softness, brightness, colour amount)"),
                Loc.T("Only the colours move: shading, strands and texture detail stay. Use Blend: Color with Brightness 0 to change only the hue.")));
            if (_foldMatchTune)
            {
                StudioGUI.Count("layers.matchSoftness");
                StudioGUI.Count("layers.matchBrightness");
                StudioGUI.Count("layers.matchAmount");
                StudioGUI.Count("layers.matchKeepOthers");
                StudioGUI.Count("layers.matchKeepWhiteBlack");
                EditorGUI.BeginChangeCheck();
                float soft = EditorGUILayout.Slider(new GUIContent(Loc.T("Softness"), Loc.T("How far each match reaches into similar colours. Higher = smoother blends, lower = more separate colours")), cm.softness, 0.02f, 0.4f);
                float bright = EditorGUILayout.Slider(new GUIContent(Loc.T("Brightness"), Loc.T("How much of the other material's light and dark to take. 0 keeps this texture's brightness exactly")), cm.brightness, 0f, 1f);
                float amount = EditorGUILayout.Slider(new GUIContent(Loc.T("Colour amount"), Loc.T("How strongly to take the other material's colours (above 1 = more vivid)")), cm.colourAmount, 0f, 1.5f);
                bool keep = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Leave other colours alone"), Loc.T("Colours far from every matched colour (eyes, small details) keep their own colour")), cm.keepOtherColours);
                bool keepWB = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Keep whites and blacks"), Loc.T("Pure white and pure black stay as they are; colours in between move. Keeps highlights and shadows crisp.")), cm.keepWhitesAndBlacks);
                if (EditorGUI.EndChangeCheck())
                    pending = () =>
                    {
                        Undo.RecordObject(_recipe, "Colour Match");
                        l.softness = soft; l.brightness = bright; l.colourAmount = amount; l.keepOtherColours = keep; l.keepWhitesAndBlacks = keepWB;
                        MarkEdited();
                    };
                GUILayout.Label(Loc.T("Only the colours move: shading and texture detail stay. Blend: Color with Brightness 0 changes only the hue."), StudioGUI.Hint);
            }
            DrawMatchMaterialSettings(cm);
        }

        void DrawMatchSwap(ColourMatchLayer layer, ref Action pending)
        {
            using (new EditorGUI.DisabledScope(layer.useTexture || layer.source == null))
            {
                bool swap = GUILayout.Button(new GUIContent("↔", layer.useTexture ? Loc.T("Swap only works when matching to a material")
                    : Loc.T("Swap: edit the other material instead, matched to this one")), EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(18));
                StudioGUI.Count("layers.matchSwap", GUILayoutUtility.GetLastRect());
                if (swap) pending = () => SwapMatchDirection(layer);
            }
        }

        static readonly ColourMatch.Pairing[] MatchPairings = { ColourMatch.Pairing.Brightness, ColourMatch.Pairing.Closest, ColourMatch.Pairing.Prominence };

        void DrawMatchPairing(ColourMatchLayer layer, ref Action pending)
        {
            var choices = new[]
            {
                new GUIContent(Loc.T("Brightness order"), Loc.T("Pairs darkest with darkest and lightest with lightest.")),
                new GUIContent(Loc.T("Closest colour"), Loc.T("Pairs each colour with the closest colour in the reference.")),
                new GUIContent(Loc.T("Most used colours"), Loc.T("Pairs the most common colours first.")),
            };
            var current = Array.IndexOf(MatchPairings, layer.pairing) >= 0 ? layer.pairing : ColourMatch.Pairing.Brightness;
            var label = new GUIContent(Loc.T("Pair colours by"), Loc.T("Choose how the next Find colours pairs the palettes."));
            if (PanelContentWidth < 260f)
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.MaxWidth(PanelContentWidth));
                label = GUIContent.none;
            }
            var next = SteppedChoice(label, current, MatchPairings,
                (rect, value) => MatchPairings[EditorGUI.Popup(rect, (int)value, choices)], PanelContentWidth);
            var choiceRect = GUILayoutUtility.GetLastRect();
            choiceRect.width = Mathf.Min(choiceRect.width, PanelContentWidth);
            StudioGUI.Count("layers.matchPairing", choiceRect);
            if (next != current) pending = () =>
            {
                Undo.RecordObject(_recipe, Loc.T("Choose how to pair colours"));
                layer.pairing = next;
                MarkEdited();
                Learn.Did(LearnEvents.MatchPairing);
            };
        }

        void DrawMatchPaletteSearch(ColourMatchLayer layer, ref Action pending)
        {
            var choices = ColourCountLabels;
            var findLabel = new GUIContent(layer.from.Count == 0 ? Loc.T("Find colours") : Loc.T("Find colours again"),
                Loc.T("Find the main colours and replace the pairs below."));
            const float height = 22f, gap = 4f;
            float countWidth = choices.Max(choice => EditorStyles.popup.CalcSize(choice).x) + 44f;
            bool stacked = countWidth + gap + GUI.skin.button.CalcSize(findLabel).x > PanelContentWidth;
            var row = GUILayoutUtility.GetRect(0f, height, GUILayout.Width(PanelContentWidth));
            var countRect = new Rect(row.x, row.y, Mathf.Min(countWidth, row.width), row.height);
            int current = Mathf.Clamp(layer.colourCount, ColourCountValues[0], ColourCountValues[^1]);
            int count = SteppedChoice(countRect, current, ColourCountValues, (field, value) =>
                ColourCountValues[EditorGUI.Popup(field, Array.IndexOf(ColourCountValues, value), choices)]);
            StudioGUI.Count("layers.matchColourCount", countRect);
            if (count != current) pending = () => SetMatchColourCount(layer, count);

            using (new EditorGUI.DisabledScope(!layer.HasSource))
            {
                bool find;
                Rect findRect;
                if (stacked)
                {
                    find = DrawRecolourAction(findLabel, height);
                    findRect = GUILayoutUtility.GetLastRect();
                }
                else
                {
                    findRect = new Rect(countRect.xMax + gap, row.y, Mathf.Max(0f, row.width - countRect.width - gap), row.height);
                    find = GUI.Button(findRect, findLabel);
                }
                StudioGUI.Count("layers.matchFind", findRect);
                if (find) pending = () => FindMatchColours(layer);
            }
        }

        void SetMatchColourCount(ColourMatchLayer layer, int count)
        {
            var recipe = _recipe;
            const string undo = "Choose number of colours";
            WithTargetUndo(undo, () =>
            {
                if (_needsRebuild || _recipe != recipe || ActiveLayer != layer) return;
                Undo.RecordObject(recipe, undo);
                layer.colourCount = count;
                RefreshEditedRecipe(affectsImage: false);
                RecordHistory();
            });
        }

        [SerializeField] bool _foldMatchMaterial;
        [SerializeField] bool _foldMatchTune;
        static readonly int[] ColourCountValues = { 2, 3, 4, 5, 6, 7, 8 };
        static GUIContent[] ColourCountLabels => ColourCountValues.Select(v => new GUIContent(Loc.F("{0} colours", v), Loc.T("How many main colours to look for on each texture"))).ToArray();
        [SerializeField] bool _mmLighting = true, _mmRim, _mmOutline, _mmEmission;
        bool _mmShowList;
        List<MaterialSettingsMatch.Change> _mmPlan;
        string _mmPlanKey;
        double _mmPlanTime;

        List<MaterialSettingsMatch.Change> MatchMaterialPlan(Material source)
        {
            string key = (_material != null ? _material.GetInstanceID() : 0) + ":" + (source != null ? source.GetInstanceID() : 0) + ":" + _mmLighting + _mmRim + _mmOutline + _mmEmission;
            if (_mmPlan == null || key != _mmPlanKey || EditorApplication.timeSinceStartup - _mmPlanTime > 1.0)
            {
                _mmPlan = MaterialSettingsMatch.Plan(_material, source, _mmLighting, _mmRim, _mmOutline, _mmEmission);
                _mmPlanKey = key;
                _mmPlanTime = EditorApplication.timeSinceStartup;
            }
            return _mmPlan;
        }

        void DrawMatchMaterialSettings(ColourMatchLayer cm)
        {
            GUILayout.Space(4);
            StudioGUI.Count("layers.matchMaterial");
            _foldMatchMaterial = StudioGUI.Foldout(_foldMatchMaterial, new GUIContent(Loc.T("Also match material settings"),
                Loc.T("Copy the other material's shadow colours, lighting and (optionally) rim / shine and outline colour onto this material")));
            if (!_foldMatchMaterial) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (cm.useTexture) { GUILayout.Label(Loc.T("Material settings can only be copied from a material: switch Match to to Material."), StudioGUI.Hint); return; }
                if (cm.source == null) { GUILayout.Label(Loc.T("Pick the material to match first."), StudioGUI.Hint); return; }
                if (!MaterialSettingsMatch.Compatible(_material, cm.source, out var why)) { StudioGUI.CheckRow(StudioGUI.Check.Note, why); return; }

                StudioGUI.Count("layers.matchLighting");
                StudioGUI.Count("layers.matchRim");
                StudioGUI.Count("layers.matchOutline");
                StudioGUI.Count("layers.matchEmission");
                EditorGUI.BeginChangeCheck();
                _mmLighting = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Shadows and lighting"), Loc.T("Shadow colours, borders and blur, lighting mode, light limits, monochrome lighting")), _mmLighting);
                _mmRim = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Rim light and shine"), Loc.T("Rim light, matcap and backlight colours and strengths")), _mmRim);
                _mmOutline = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Outline colour"), Loc.T("The outline's colour")), _mmOutline);
                _mmEmission = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Glow (Emission) colour and strength"), Loc.T("Emission on or off, glow colours and strengths. Glow textures and masks are never copied.")), _mmEmission);
                if (EditorGUI.EndChangeCheck()) _mmPlan = null;

                var plan = MatchMaterialPlan(cm.source);
                if (plan.Count == 0)
                {
                    StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("These settings already match “{0}”.", cm.source.name));
                }
                else
                {
                    _mmShowList = StudioGUI.Foldout(_mmShowList, new GUIContent(Loc.F("{0} setting{1} differ", plan.Count, (plan.Count == 1 ? "" : "s"))));
                    if (_mmShowList)
                        foreach (var c in plan.Take(40)) GUILayout.Label(MaterialSettingsMatch.Describe(c), StudioGUI.Hint);
                    if (_mmShowList && plan.Count > 40) GUILayout.Label(Loc.F("…and {0} more", plan.Count - 40), StudioGUI.Hint);
                    StudioGUI.Count("layers.matchCopy");
                    if (GUILayout.Button(new GUIContent(Loc.F("Copy {0} setting{1} from “{2}”", plan.Count, (plan.Count == 1 ? "" : "s"), cm.source.name),
                            Loc.T("Changes this material right away (every part using it). Ctrl+Z undoes it.")), GUILayout.Height(24)))
                    {
                        var target = _material;
                        var source = cm.source;
                        EditorApplication.delayCall += () =>
                        {
                            if (this != null && !_needsRebuild && _material == target && ActiveLayer == cm && cm.source == source)
                                ApplyMatchMaterialSettings(cm);
                        };
                    }
                }
                GUILayout.Label(Loc.F("Changes “{0}” itself, right away, like the Colour tab (Ctrl+Z undoes). Textures, masks and the rendering mode stay as they are.", (_material != null ? _material.name : "")), StudioGUI.Hint);
            }
        }

        void ApplyMatchMaterialSettings(ColourMatchLayer cm)
        {
            if (_material == null || cm == null || cm.useTexture || cm.source == null) return;
            if (MaterialWriter.IsPoiyomiLocked(_material))
            {
                if (!ConfirmUnlock(new[] { _material }, "colour settings")) return;
                EditorUtility.DisplayProgressBar(Loc.T("Avistry"), Loc.T("Unlocking the material…"), 0.5f);
                try { MaterialWriter.UnlockPoiyomi(new[] { _material }); }
                finally { EditorUtility.ClearProgressBar(); }
                if (MaterialWriter.IsPoiyomiLocked(_material))
                {
                    Toast(Loc.T("The material couldn't be unlocked. Unlock it in the Poiyomi inspector, then try again."), MessageType.Warning, 6f);
                    return;
                }
            }
            var target = _material;
            var plan = MaterialSettingsMatch.Plan(target, cm.source, _mmLighting, _mmRim, _mmOutline, _mmEmission);
            int n = MaterialSettingsMatch.Apply(target, plan);
            _mmPlan = null;
            _dirty = true;
            Repaint();
            if (n == 0) Toast(Loc.T("Nothing to change: the settings already match."), MessageType.Info, 5f);
            else
            {
                var shader = target.shader;
                Toast(Msg.M27CopiedSettings(n, cm.source.name, target.name), MessageType.Info, 8f,
                    Loc.T("Restore settings"), () => RestoreMatchedSettings(target, shader, plan, n));
            }
        }

        void RestoreMatchedSettings(Material target, Shader shader, List<MaterialSettingsMatch.Change> plan, int copied)
        {
            if (_needsRebuild || target == null || _material != target || target.shader != shader)
            {
                Toast(Loc.T("The material changed. The copied settings are no longer available to restore."), MessageType.Info);
                return;
            }
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int restored = MaterialSettingsMatch.Restore(target, plan);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            _mmPlan = null;
            _dirty = true;
            Toast(restored == 0 ? Loc.T("There are no copied settings left to restore.")
                : Loc.F("Restored {0} of {1} copied settings.", restored, copied), MessageType.Info);
        }


        void DrawMaterialLookNotes(ColourMatchLayer cm)
        {
            string src = !cm.useTexture && cm.source != null ? MaterialLook.Describe(cm.source) : null;
            if (src != null) GUILayout.Label(Loc.F("“{0}” colours its texture ({1}); its colours above are shown as the material shows them.", cm.source.name, src), StudioGUI.Hint);
            string self = MaterialLook.Describe(_material);
            if (self != null) GUILayout.Label(Loc.F("This material also changes its texture's colours ({0}). The pairs work on the texture itself; use Match on the model to match what you see.", self), StudioGUI.Hint);
        }

        void DrawMatchThumb(Texture tex, float cardW, Color? flat)
        {
            var r = GUILayoutUtility.GetRect(cardW - 8, 56, GUILayout.Width(cardW - 8), GUILayout.Height(56));
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, StudioGUI.CanvasBg);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleAndCrop, false);
            else if (flat.HasValue) EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(flat.Value.r, flat.Value.g, flat.Value.b, 1f));
            else GUI.Label(r, Loc.T("nothing yet"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawSwatchStrip(List<Color> colours, float cardW, string tip, bool clickToUse)
        {
            var r = GUILayoutUtility.GetRect(cardW - 8, 14, GUILayout.Width(cardW - 8));
            if (colours == null || colours.Count == 0) { GUI.Label(r, Loc.T("colours: –"), EditorStyles.centeredGreyMiniLabel); return; }
            float w = Mathf.Min(18f, (r.width - (colours.Count - 1) * 2f) / colours.Count);
            for (int i = 0; i < colours.Count; i++)
            {
                var cr = new Rect(r.x + i * (w + 2), r.y, w, 14);
                int click = ColourTile(cr, colours[i], $"#{ColorUtility.ToHtmlStringRGB(colours[i])} · {tip}");
                if (click == 1 && clickToUse) SetMainColour(colours[i]);
            }
        }

        void DrawAddPairButton(ColourMatchLayer cm)
        {
            bool picking = (_matchPick == MatchPick.PairFrom || _matchPick == MatchPick.PairTo) && _matchPickLayerId == cm.id;
            var oldBg = GUI.backgroundColor;
            if (picking) GUI.backgroundColor = StudioGUI.Accent;
            using (new EditorGUI.DisabledScope(cm.from.Count >= 8 && !picking))
            {
                string label = picking ? (_matchPick == MatchPick.PairFrom ? Loc.T("  Click the colour to change… (Esc cancels)") : Loc.T("  Click the colour to take… (Esc cancels)"))
                                       : Loc.T("  Add a pair from the Scene view");
                StudioGUI.Count("layers.matchAddPair");
                if (GUILayout.Button(new GUIContent(label, StudioGUI.Icon("Grid.PickingTool", "").image,
                        Loc.T("Two clicks in the Scene view: first the colour to change on this part, then the colour it should become, on any part of the avatar")),
                        GUILayout.Height(22)))
                {
                    if (picking) CancelMatchPick(); else StartMatchPick(cm, MatchPick.PairFrom);
                }
            }
            GUI.backgroundColor = oldBg;
        }

        readonly Dictionary<string, uint> _cmCrc = new Dictionary<string, uint>();

        string MatchSourceKey(ColourMatchLayer cm)
        {
            uint crc = 0;
            if (!cm.useTexture && cm.source != null)
            {
                string ck = cm.id + ":" + cm.source.GetInstanceID();
                if (Event.current == null || Event.current.type == EventType.Layout || !_cmCrc.TryGetValue(ck, out crc))
                {
                    crc = (uint)cm.source.ComputeCRC();
                    if (_cmCrc.Count > 32) _cmCrc.Clear();
                    _cmCrc[ck] = crc;
                }
            }
            return cm.id + ":" + cm.SourceObject.GetInstanceID() + ":" + cm.colourCount + ":" + crc;
        }

        List<ColourMatch.Swatch> MatchSourceColours(ColourMatchLayer cm)
        {
            if (!cm.HasSource) return new List<ColourMatch.Swatch>();
            string key = MatchSourceKey(cm);
            if (!_cmSourceColours.TryGetValue(key, out var list))
            {
                list = cm.useTexture
                    ? ColourMatch.TextureColours(cm.sourceTexture, MaterialAudit.FindAvatarRoot(_renderer), cm.colourCount)
                    : ColourMatch.MaterialColours(cm.source, MaterialAudit.FindAvatarRoot(_renderer), cm.colourCount);
                if (_cmSourceColours.Count > 32) _cmSourceColours.Clear();
                _cmSourceColours[key] = list;
            }
            return list;
        }

        void DrawGlowWarning(ColourMatchLayer cm)
        {
            var slot = CurrentSlot;
            if (slot == null || slot.role != TextureRole.Color) return;
            foreach (var g in LookCheck.StrongEmissions(_material))
            {
                if (g.map != null && g.map == slot.texture) continue;
                int pct = Mathf.RoundToInt(Mathf.Min(g.amount, 9.99f) * 100f);
                string what = g.map != null ? Loc.F("its texture “{0}”", g.map.name) : Loc.T("no texture, so it glows evenly everywhere");
                StudioGUI.CheckRow(StudioGUI.Check.Problem,
                    Loc.F("{0} is on ({1}, about {2}% extra light), so colour changes on this texture barely show on the model.", g.label, what, pct));
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(18);
                    StudioGUI.Count("layers.matchGlowTexture");
                    if (g.map != null && GUILayout.Button(new GUIContent(Loc.F("Match the {0} texture instead", g.label.ToLowerInvariant()),
                            Loc.F("Switch to “{0}” and run this Colour Match on it: that's the texture that colours the model", g.map.name)), EditorStyles.miniButton))
                    {
                        var map = g.map;
                        EditorApplication.delayCall += OnCurrentColourMatchTexture(() => MoveColourMatchTo(map, cm)).Invoke;
                    }
                    StudioGUI.Count("layers.matchShowMaterial");
                    if (GUILayout.Button(new GUIContent(Loc.T("Show the material"), Loc.T("Select the material to lower or switch off its emission in the Inspector")), EditorStyles.miniButton))
                    {
                        Selection.activeObject = _material;
                        EditorGUIUtility.PingObject(_material);
                    }
                }
            }
        }

        void MoveColourMatchTo(Texture map, ColourMatchLayer cm)
        {
            if (map == null || cm == null || _recipe == null || !_recipe.layers.Contains(cm)) return;
            if (!CanAddColourMatchLayer(out var why)) { ShowColourMatchBlocked(why); return; }
            int idx = _slots.FindIndex(s => s.texture == map);
            if (idx < 0) { Toast(Loc.F("“{0}” isn't one of this material's textures.", map.name), MessageType.Warning, 5f); return; }
            if (!IsColourMatchTexture(_slots[idx]))
            {
                ShowColourMatchBlocked(Loc.T("Choose a colour texture in Pick to use Colour Match."));
                return;
            }
            var settings = JsonUtility.ToJson(cm);
            if (!ConfirmTargetChange("texture")) return;
            Undo.RecordObject(this, "Change texture");
            ChooseSlot(idx);
            Rebuild();
            if (!CanAddColourMatchLayer(out why)) { ShowColourMatchBlocked(why); return; }
            var layer = _recipe.layers.OfType<ColourMatchLayer>().FirstOrDefault(l => l.id == cm.id);
            if (layer == null)
            {
                layer = JsonUtility.FromJson<ColourMatchLayer>(settings);
                layer.source = cm.source;
                layer.sourceTexture = cm.sourceTexture;
                layer.id = System.Guid.NewGuid().ToString("N");
                InsertLayer(layer, "Add Colour Match");
            }
            _activeLayerId = layer.id;
            FindMatchColours(layer);
            SetTab(Tab.Paint);
            Toast(Loc.F("Now matching “{0}”, the texture that colours the model, to “{1}”.", map.name, (layer.HasSource ? layer.SourceName : "?")), MessageType.Info, 6f);
        }

        void SwapMatchDirection(ColourMatchLayer cm)
        {
            if (cm == null || _recipe == null || !_recipe.layers.Contains(cm) || _needsRebuild) return;
            var other = cm.source;
            var self = _material;
            if (other == null || self == null) return;
            var root = MaterialAudit.FindAvatarRoot(_renderer);
            var users = root != null ? TargetResolver.FindMaterialUsers(root, other) : null;
            if (users == null || users.Count == 0 || users[0].renderer == null)
            {
                Toast(Loc.F("“{0}” isn't used by a part on this avatar, so it can't be edited here.", other.name), MessageType.Warning, 5f);
                return;
            }
            var target = TargetResolver.GetTextureSlots(other).FirstOrDefault(IsColourMatchTexture);
            if (target == null)
            {
                ShowColourMatchBlocked(Loc.F("“{0}” has no colour texture to edit. Choose another material in Pick.", other.name));
                return;
            }
            if (!ConfirmTargetChange("part")) return;
            var u = users[0];
            SetRenderer(u.renderer);
            _materialIndex = u.materialIndex;
            _pendingTexture = target.texture;
            Rebuild();
            if (!CanAddColourMatchLayer(out var why)) { ShowColourMatchBlocked(why); return; }
            var layer = new ColourMatchLayer
            {
                source = self, colourCount = cm.colourCount, softness = cm.softness, brightness = cm.brightness,
                colourAmount = cm.colourAmount, keepOtherColours = cm.keepOtherColours, keepWhitesAndBlacks = cm.keepWhitesAndBlacks,
                pairing = cm.pairing,
            };
            layer.name = $"Colour Match ({self.name})";
            InsertLayer(layer, "Add Colour Match");
            FindMatchColours(layer);
            SetTab(Tab.Paint);
            Toast(Loc.F("Now editing “{0}”, matched to “{1}”. Your previous layers were left on the other texture's edit.", other.name, self.name), MessageType.Info, 6f);
        }

        void SetMatchMode(ColourMatchLayer cm, bool texture)
        {
            Undo.RecordObject(_recipe, "Colour Match");
            cm.useTexture = texture;
            if (texture && cm.sourceTexture == null)
                cm.sourceTexture = MaterialLook.MainTexture(cm.source) as Texture2D;
            if (!FindMatchColours(cm)) MarkEdited();
        }

        void SetMatchTexture(ColourMatchLayer cm, Texture2D t, bool find)
        {
            Undo.RecordObject(_recipe, "Colour Match");
            cm.sourceTexture = t;
            cm.useTexture = true;
            if (t != null && (cm.name == "Colour Match" || cm.name.StartsWith("Colour Match ("))) cm.name = $"Colour Match ({t.name})";
            if (!find || !FindMatchColours(cm)) MarkEdited();
        }

        List<(string label, Texture2D tex)> AvatarTextureChoices()
        {
            var list = new List<(string, Texture2D)>();
            var root = MaterialAudit.FindAvatarRoot(_renderer);
            if (root == null) return list;
            var seen = new HashSet<Texture2D>();
            var editing = CurrentSlot?.texture;
            var mats = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().OrderBy(m => m.name);
            foreach (var m in mats)
            {
                var shader = m.shader;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                    string pn = shader.GetPropertyName(i);
                    bool main = (shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.MainTexture) != 0;
                    if (!main && (!(pn == "_MainTex" || pn.Contains("Emission") || pn.Contains("Decal") || pn.Contains("Color")) ||
                        pn.Contains("Mask"))) continue;
                    var t = m.GetTexture(pn) as Texture2D;
                    if (t == null || t == editing || (!main && t.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        !seen.Add(t)) continue;
                    list.Add(($"{NoSubmenu(m.name)}/{NoSubmenu(t.name)}", t));
                }
            }
            return list;
        }

        void SetMatchSource(ColourMatchLayer cm, Material m, bool find)
        {
            Undo.RecordObject(_recipe, "Colour Match");
            cm.source = m;
            cm.useTexture = false;
            if (m != null && (cm.name == "Colour Match" || cm.name.StartsWith("Colour Match ("))) cm.name = $"Colour Match ({m.name})";
            if (!find || !FindMatchColours(cm)) MarkEdited();
        }

        byte[] MatchTargetMask(ColourMatchLayer cm)
        {
            const int size = ColourMatch.SampleSize;
            if (cm.maskMode == MaskMode.SelectedFaces)
            {
                var faces = _uv.RasterFaceMask(size, size, new HashSet<int>(cm.faces));
                if (!cm.invertMask) return faces;

                var occupied = _uv.RasterIslandMask(size, size, null, 0);
                for (int i = 0; i < faces.Length; i++)
                    if (faces[i] != 0) occupied[i] = 0;
                return occupied;
            }

            HashSet<int> islands = null;
            if (cm.maskMode == MaskMode.SelectedIslands && cm.islands.Count > 0)
                islands = cm.invertMask
                    ? new HashSet<int>(Enumerable.Range(0, _uv.islandCount).Where(i => !cm.islands.Contains(i)))
                    : new HashSet<int>(cm.islands);
            return _uv.RasterIslandMask(size, size, islands, 1);
        }

        bool FindMatchColours(ColourMatchLayer cm)
        {
            if (_comp == null || _uv == null || !cm.HasSource || _recipe == null || !_recipe.layers.Contains(cm)) return false;
            if (cm.maskMode == MaskMode.SelectedFaces && !HasCurrentFaceLimit(cm))
            {
                var recipe = _recipe;
                Toast(Loc.T("This face limit does not match the current UV mesh. Pick new faces or choose Everything."),
                    MessageType.Warning, 6f, Loc.T("Pick faces"), () =>
                    {
                        if (_recipe == recipe && recipe.layers.Contains(cm)) PickFacesForLayer(cm);
                    });
                return false;
            }

            var target = ColourMatch.MainColours(_comp.SourceThumbnail(ColourMatch.SampleSize), MatchTargetMask(cm), cm.colourCount);
            string key = MatchSourceKey(cm);
            _cmSourceColours.Remove(key);
            var source = MatchSourceColours(cm);
            if (target.Count == 0 || source.Count == 0)
            {
                Toast(target.Count == 0 ? Loc.T("No colours found on this texture where the layer applies.") : Loc.F("No colours found on “{0}”.", cm.SourceName), MessageType.Warning, 5f);
                return false;
            }
            Undo.RecordObject(_recipe, "Find colours");
            cm.from = target.Select(s => s.colour).ToList();
            cm.to = ColourMatch.PairColours(target, source, cm.pairing);
            MarkEdited();
            Toast(Loc.F("Matched {0} colours to “{1}”. Change any pair with ▾.", target.Count, cm.SourceName), MessageType.Info, 5f);
            return true;
        }
    }
}
