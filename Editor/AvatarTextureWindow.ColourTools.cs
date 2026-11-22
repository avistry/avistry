using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Small colour helpers for the colour palettes (gamma / file values throughout).</summary>
    static class ColourMath
    {
        /// <summary>Approximate color axes (CLIP STUDIO PAINT cycles H, S, V, L, R, G, B).</summary>
        public enum Axis { H, S, V, L, R, G, B }

        public static readonly string[] AxisNames = { "Hue", "Saturation", "Value", "Lightness", "Red", "Green", "Blue" };

        public static Vector3 RGBToHSL(Color c)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float l = (mx + mn) * 0.5f, d = mx - mn, h = 0, s = 0;
            if (d > 1e-6f)
            {
                s = l > 0.5f ? d / (2f - mx - mn) : d / (mx + mn);
                if (mx == c.r) h = (c.g - c.b) / d + (c.g < c.b ? 6f : 0f);
                else if (mx == c.g) h = (c.b - c.r) / d + 2f;
                else h = (c.r - c.g) / d + 4f;
                h /= 6f;
            }
            return new Vector3(h, s, l);
        }

        static float Hue2RGB(float p, float q, float t)
        {
            t = Mathf.Repeat(t, 1f);
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        public static Color HSLToRGB(Vector3 hsl)
        {
            float h = hsl.x, s = hsl.y, l = hsl.z;
            if (s <= 1e-6f) return new Color(l, l, l, 1f);
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s, p = 2f * l - q;
            return new Color(Hue2RGB(p, q, h + 1f / 3f), Hue2RGB(p, q, h), Hue2RGB(p, q, h - 1f / 3f), 1f);
        }

        /// <summary>Moves one property of the colour by t (−1..1; for hue, 1 = half way round the wheel).</summary>
        public static Color Vary(Color c, Axis axis, float t)
        {
            switch (axis)
            {
                case Axis.R: return new Color(Mathf.Clamp01(c.r + t), c.g, c.b, 1f);
                case Axis.G: return new Color(c.r, Mathf.Clamp01(c.g + t), c.b, 1f);
                case Axis.B: return new Color(c.r, c.g, Mathf.Clamp01(c.b + t), 1f);
                case Axis.L:
                {
                    var hsl = RGBToHSL(c);
                    hsl.z = Mathf.Clamp01(hsl.z + t);
                    return HSLToRGB(hsl);
                }
                default:
                {
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    if (axis == Axis.H) h = Mathf.Repeat(h + t * 0.5f, 1f);
                    else if (axis == Axis.S) s = Mathf.Clamp01(s + t);
                    else v = Mathf.Clamp01(v + t);
                    var o = Color.HSVToRGB(h, s, v);
                    o.a = 1f;
                    return o;
                }
            }
        }

        /// <summary>Perceptual mix (OKLab), so midpoints don't go muddy or dark like plain RGB mixing.</summary>
        public static Color Mix(Color a, Color b, float t)
        {
            var c = Recolour.FromOklab(Vector3.Lerp(Recolour.ToOklab(a), Recolour.ToOklab(b), t), 1f);
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }
    }

    [Serializable]
    class ColourSet
    {
        public string name;
        public List<Color> colors = new List<Color>();
    }

    [Serializable]
    class ColourSetStore
    {
        public List<ColourSet> sets = new List<ColourSet>();
    }

    /// <summary>
    /// CLIP STUDIO PAINT-style colour tools for the Paint tab: main / sub colour (X swaps), Color history, Color set,
    /// Approximate color and Intermediate color palettes, and eyedropper settings (Reference, Average color).
    /// The colour wheel and sliders are Unity's colour picker, opened from the main / sub swatches.
    /// </summary>
    partial class AvatarTextureWindow
    {
        enum PaletteView { History = 0, ColourSet = 1, Approximate = 2, Intermediate = 3 }

        const string ColourSetsPref = "AvatarTextureStudio.ColourSets";
        static string TextureSetName => Loc.T("From this texture");

        [SerializeField] PaletteView _paletteView = PaletteView.History;
        [SerializeField] ColourMath.Axis _approxAcross = ColourMath.Axis.H, _approxDown = ColourMath.Axis.V;
        [SerializeField] float _approxSpreadAcross = 0.12f, _approxSpreadDown = 0.4f;
        [SerializeField] Color[] _mixCorners;
        [SerializeField] int _mixSteps = 7;
        [SerializeField] Compositor.PickSource _pickSource = Compositor.PickSource.Image;
        [SerializeField] bool _pickAverage;
        [SerializeField] int _pickRadius = 2;
        [SerializeField] bool _autoRegisterPicks;
        [SerializeField] bool _foldPalettes;
        [SerializeField] int _colourSetIndex = 1; // 0 = from this texture, 1.. = the user's sets

        ColourSetStore _colourSets;
        List<Color> _texturePalette;
        bool _renamingSet;

        void SwapColours()
        {
            Undo.RecordObject(this, "Swap colours");
            (_brush.color, _brush.subColor) = (_brush.subColor, _brush.color);
            Repaint();
        }

        void SetMainColour(Color c, string undo = "Pick colour")
        {
            Undo.RecordObject(this, undo);
            c.a = 1f;
            _brush.color = c;
            Repaint();
        }

        /// <summary>Main and sub colour, swap, eyedropper and Scene picker, then the palettes.</summary>
        void DrawColourControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                StudioGUI.Count("colour.main");
                _brush.color = EditorGUILayout.ColorField(new GUIContent("", Loc.T("Main colour. Click for the colour wheel and sliders.")), _brush.color, false, false, false, GUILayout.MinWidth(60));
                StudioGUI.Info("tint");
                StudioGUI.Count("colour.swap");
                if (GUILayout.Button(new GUIContent("↔", Loc.T("Swap main and second colour (X)")), EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18))) SwapColours();
                StudioGUI.Count("colour.secondColour");
                _brush.subColor = EditorGUILayout.ColorField(new GUIContent("", Loc.T("Second colour: X swaps it with the main colour. Used by Colour variety per stroke and gradients.")), _brush.subColor, false, false, false, GUILayout.Width(40));
                StudioGUI.Info("tint");
                StudioGUI.Count("colour.eyedropper");
                bool armed = GUILayout.Toggle(_eyedropperArmed, StudioGUI.Icon("Grid.PickingTool", "", Loc.T("Pick a colour from the texture or the model (I, or Ctrl+click)")), GUI.skin.button, GUILayout.Width(30), GUILayout.Height(18));
                if (armed != _eyedropperArmed) { _eyedropperArmed = armed; Repaint(); }
                var oldBg = GUI.backgroundColor;
                if (_scenePickArmed) GUI.backgroundColor = StudioGUI.Accent;
                if (GUILayout.Button(new GUIContent(Loc.T("Scene"), StudioGUI.Icon("Grid.PickingTool", "").image, Loc.T("Pick a colour from anything in the Scene view: the exact texture colour on this part, or the on-screen colour anywhere else (Shift = always on-screen)")), GUILayout.Width(64), GUILayout.Height(18)))
                {
                    _scenePickArmed = !_scenePickArmed;
                    if (_scenePickArmed) { SceneView.lastActiveSceneView?.Focus(); Toast(Loc.T("Click anything in the Scene view to pick its colour. Shift+click = colour as seen on screen. Esc cancels."), MessageType.Info, 5f); }
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = oldBg;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Loc.T("Main"), StudioGUI.Hint, GUILayout.MinWidth(60));
                GUILayout.Space(24);
                GUILayout.Label(Loc.T("Second"), StudioGUI.Hint, GUILayout.Width(40));
                GUILayout.Space(94);
            }

            GUILayout.Space(3);
            if (!_foldPalettes || _paletteView != PaletteView.History) DrawRecentTiles();
            if (!Advanced) return;
            _foldPalettes = EditorGUILayout.Foldout(_foldPalettes, new GUIContent(Loc.T("Colour sets, variations and eyedropper"),
                Loc.T("Saved colour sets, colours of this texture, approximate and intermediate colours, eyedropper settings")), true);
            if (!_foldPalettes) return;
            var view = (PaletteView)GUILayout.Toolbar((int)_paletteView, new[]
            {
                new GUIContent(Loc.T("Recent"), Loc.T("Recent colours: colours you used recently")),
                new GUIContent(_panelContentWidth < 380f ? Loc.T("Set") : Loc.T("Saved"), Loc.T("Saved colours: your saved colours, or the colours of this texture")),
                new GUIContent(_panelContentWidth < 380f ? Loc.T("Similar") : Loc.T("Shades"), Loc.T("Shades: variations of the main colour (for shading and highlights)")),
                new GUIContent(_panelContentWidth < 380f ? Loc.T("Mix") : Loc.T("Blend grid"), Loc.T("Blend grid: blends between four corner colours")),
            }, EditorStyles.miniButton);
            if (view != _paletteView) { _paletteView = view; _renamingSet = false; }
            GUILayout.Space(2);
            switch (_paletteView)
            {
                case PaletteView.History: DrawHistoryPalette(); break;
                case PaletteView.ColourSet: DrawColourSetPalette(); break;
                case PaletteView.Approximate: DrawApproximatePalette(); break;
                case PaletteView.Intermediate: DrawIntermediatePalette(); break;
            }

            GUILayout.Space(2);
            GUILayout.Label(new GUIContent(Loc.T("Eyedropper"), Loc.T("What the eyedropper (I / Ctrl+click) samples")), EditorStyles.miniBoldLabel);
            // eyedropper settings
            {
                EditorGUI.indentLevel++;
                _pickSource = (Compositor.PickSource)EditorGUILayout.EnumPopup(new GUIContent(Loc.T("Reference"),
                    Loc.T("Image: the colour you see. Original texture: before your edits. Current layer: only the paint on the selected Paint layer. Top layer: the topmost paint at that point.")), _pickSource);
                _showPickerCircle = EditorGUILayout.Toggle(new GUIContent(Loc.T("Show picker circle"), Loc.T("While picking, a ring at the cursor shows the colour under it (top) next to the current colour (bottom)")), _showPickerCircle);
                _pickAverage = EditorGUILayout.Toggle(new GUIContent(Loc.T("Average colour"), Loc.T("Average a small area instead of one pixel (smooths out noise and texture grain)")), _pickAverage);
                if (_pickAverage)
                {
                    int size = EditorGUILayout.IntSlider(new GUIContent(Loc.T("Area (px)"), Loc.T("Width of the sampled square, in pixels of the texture view")), _pickRadius * 2 + 1, 3, 25);
                    _pickRadius = Mathf.Clamp((size - 1) / 2, 1, 12);
                }
                _autoRegisterPicks = EditorGUILayout.Toggle(new GUIContent(Loc.T("Add to colour set"), Loc.T("Every colour you pick is also added to the selected colour set")), _autoRegisterPicks);
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>Eyedropper (I / Ctrl+click / the model): samples by the Reference and Average color settings.</summary>
        void PickColour(Vector2 uv)
        {
            if (_comp == null) return;
            var layer = ActiveLayer as PaintLayer;
            int radius = _pickAverage ? _pickRadius : 0;
            if (_pickSource == Compositor.PickSource.TopLayer)
            {
                // Top layer: the paint of the topmost visible Paint layer that has any at this point; else what you see.
                Color picked = default;
                bool found = false;
                for (int i = _recipe.layers.Count - 1; i >= 0 && !found; i--)
                {
                    if (!(_recipe.layers[i] is PaintLayer pl) || !pl.enabled || pl.strokes.Count == 0) continue;
                    var pc = _comp.Sample(Compositor.PickSource.CurrentLayer, uv, radius, pl, _uv, out float pa);
                    if (pa >= 0.02f) { picked = pc; found = true; }
                }
                if (!found) picked = _comp.Sample(Compositor.PickSource.Image, uv, radius, null, _uv, out _);
                SetMainColour(picked);
                if (!MaskEditing) _brush.mode = BrushMode.Paint;
                _eyedropperArmed = false;
                if (_autoRegisterPicks) AddToColourSet(picked, quiet: true);
                return;
            }
            var c = _comp.Sample(_pickSource, uv, radius, layer, _uv, out float alpha);
            if (_pickSource == Compositor.PickSource.CurrentLayer && alpha < 0.02f)
            {
                Toast(layer == null
                        ? Loc.T("The eyedropper is set to “Current layer”, but the selected layer isn't a Paint layer.")
                        : Loc.F("No paint there on “{0}” (the eyedropper is set to “Current layer”).", layer.name),
                    MessageType.Info, 5f, Loc.T("Pick from image"), () => { _pickSource = Compositor.PickSource.Image; Repaint(); });
                return;
            }
            SetMainColour(c);
            if (!MaskEditing) _brush.mode = BrushMode.Paint;
            _eyedropperArmed = false;
            if (_autoRegisterPicks) AddToColourSet(c, quiet: true);
        }

        [SerializeField] bool _showPickerCircle = true;
        Vector2 _pickerCircleAt = new Vector2(-1, -1);
        Color _pickerCircleColour;

        /// <summary>
        /// Clip Studio's colour picker circle: while the eyedropper is active (I, or Ctrl held), a ring at the cursor shows
        /// the colour under it in the top half and the current colour in the bottom half.
        /// </summary>
        void DrawPickerCircle(Vector2 mp)
        {
            if (!_showPickerCircle || _comp == null || Event.current.type != EventType.Repaint) return;
            var uv = CanvasToUV(mp);
            if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return;
            if ((uv - _pickerCircleAt).sqrMagnitude > 1e-10f) // one small read per mouse move, not per repaint
            {
                _pickerCircleAt = uv;
                var src = _pickSource == Compositor.PickSource.Original ? Compositor.PickSource.Original : Compositor.PickSource.Image;
                _pickerCircleColour = _comp.Sample(src, uv, _pickAverage ? _pickRadius : 0, null, _uv, out _);
            }
            var c = new Vector3(mp.x, mp.y, 0);
            const float outer = 34f, inner = 22f;
            // IMGUI's y axis points down: angles go clockwise from +x, so 180°..360° is the top half.
            Handles.color = new Color(0, 0, 0, 0.85f);
            Handles.DrawSolidDisc(c, Vector3.forward, outer + 2f);
            Handles.color = new Color(_pickerCircleColour.r, _pickerCircleColour.g, _pickerCircleColour.b, 1f);
            Handles.DrawSolidArc(c, Vector3.forward, Vector3.left, 180f, outer);
            Handles.color = new Color(_brush.color.r, _brush.color.g, _brush.color.b, 1f);
            Handles.DrawSolidArc(c, Vector3.forward, Vector3.right, 180f, outer);
            Handles.color = new Color(0.16f, 0.16f, 0.16f, 1f);
            Handles.DrawSolidDisc(c, Vector3.forward, inner);
            Handles.color = Color.white;
            Handles.DrawLine(c + Vector3.left * 4, c + Vector3.right * 4);
            Handles.DrawLine(c + Vector3.up * 4, c + Vector3.down * 4);
        }

        /// <summary>Draws a colour tile. Returns 1 on left click, 2 on right click, 0 otherwise.</summary>
        static int ColourTile(Rect r, Color c, string tip, bool outline = false)
        {
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, outline ? Color.white : StudioGUI.HeaderLine);
                EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(c.r, c.g, c.b, 1f));
            }
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            GUI.Label(r, new GUIContent("", tip), GUIStyle.none);
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                e.Use();
                return e.button == 1 ? 2 : 1;
            }
            return 0;
        }

        /// <summary>A wrapping grid of tiles. Returns the clicked index (and whether it was a right click), or -1.</summary>
        int TileGrid(IList<Color> colours, float tile, out bool rightClick, Func<int, string> tip, int extraTiles = 0)
        {
            rightClick = false;
            float width = PanelContentWidth - 18f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / (tile + 2)));
            int count = colours.Count + extraTiles;
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)perRow));
            var area = GUILayoutUtility.GetRect(0, rows * (tile + 2), GUILayout.ExpandWidth(true));
            int hit = -1;
            for (int i = 0; i < colours.Count; i++)
            {
                var r = new Rect(area.x + (i % perRow) * (tile + 2), area.y + (i / perRow) * (tile + 2), tile, tile);
                int click = ColourTile(r, colours[i], tip(i));
                if (click != 0) { hit = i; rightClick = click == 2; }
            }
            return hit;
        }

        Rect ExtraTileRect(int index, float tile)
        {
            var last = GUILayoutUtility.GetLastRect();
            float width = PanelContentWidth - 18f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / (tile + 2)));
            return new Rect(last.x + (index % perRow) * (tile + 2), last.y + (index / perRow) * (tile + 2), tile, tile);
        }

        /// <summary>One row of recent colours, always visible under the colour fields.</summary>
        void DrawRecentTiles()
        {
            if (_recentColors.Count == 0) return;
            StudioGUI.Count("colour.recent");
            float width = PanelContentWidth - 18f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / 18f));
            var row = _recentColors.Count > perRow ? _recentColors.GetRange(0, perRow) : _recentColors;
            int hit = TileGrid(row, 16, out _, i => Loc.T("Recent colour: click to use"));
            if (hit >= 0) SetMainColour(row[hit]);
        }

        void DrawHistoryPalette()
        {
            if (_recentColors.Count == 0) { GUILayout.Label(Loc.T("Colours you paint with appear here."), StudioGUI.Hint); return; }
            int hit = TileGrid(_recentColors, 18, out _, i => Loc.T("Recent colour: click to use"));
            if (hit >= 0) SetMainColour(_recentColors[hit]);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(Loc.T("Save as colour set"), Loc.T("Register to colour set: keeps these colours as a new set")), EditorStyles.miniButtonLeft))
                {
                    var set = new ColourSet { name = UniqueSetName("Recent colours"), colors = new List<Color>(_recentColors) };
                    ColourSets.sets.Add(set);
                    SaveColourSets();
                    _colourSetIndex = ColourSets.sets.Count;
                    _paletteView = PaletteView.ColourSet;
                }
                if (GUILayout.Button(new GUIContent(Loc.T("Clear"), Loc.T("Clear the colour history")), EditorStyles.miniButtonRight))
                {
                    Undo.RecordObject(this, "Clear colour history");
                    _recentColors.Clear();
                }
            }
        }

        ColourSetStore ColourSets
        {
            get
            {
                if (_colourSets != null) return _colourSets;
                try { _colourSets = JsonUtility.FromJson<ColourSetStore>(EditorPrefs.GetString(ColourSetsPref, "")); }
                catch { _colourSets = null; }
                if (_colourSets == null) _colourSets = new ColourSetStore();
                if (_colourSets.sets.Count == 0) _colourSets.sets.Add(new ColourSet { name = "My colours" });
                return _colourSets;
            }
        }

        const int MaxColoursPerSet = 64;

        void SaveColourSets()
        {
            try
            {
                foreach (var s in ColourSets.sets)
                    if (s.colors.Count > MaxColoursPerSet) s.colors.RemoveRange(MaxColoursPerSet, s.colors.Count - MaxColoursPerSet);
                EditorPrefs.SetString(ColourSetsPref, JsonUtility.ToJson(ColourSets));
            }
            catch (Exception ex) { Debug.LogWarning("Avistry: couldn't save colour sets: " + ex.Message); }
        }

        string UniqueSetName(string baseName)
        {
            string n = baseName;
            for (int k = 2; ColourSets.sets.Exists(s => s.name == n); k++) n = $"{baseName} {k}";
            return n;
        }

        ColourSet CurrentUserSet => _colourSetIndex >= 1 && _colourSetIndex <= ColourSets.sets.Count ? ColourSets.sets[_colourSetIndex - 1] : null;

        void AddToColourSet(Color c, bool quiet = false)
        {
            var set = CurrentUserSet ?? ColourSets.sets[0];
            c.a = 1f;
            if (set.colors.Exists(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.004f))
            {
                if (!quiet) Toast(Loc.F("That colour is already in “{0}”.", set.name), MessageType.Info, 3f);
                return;
            }
            set.colors.Add(c);
            SaveColourSets();
            Repaint();
        }

        void DrawColourSetPalette()
        {
            var sets = ColourSets.sets;
            _colourSetIndex = Mathf.Clamp(_colourSetIndex, 0, sets.Count);
            var names = new List<string> { TextureSetName };
            foreach (var s in sets) names.Add(NoSubmenu(s.name));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_renamingSet && CurrentUserSet != null)
                {
                    GUI.SetNextControlName("ATSRenameSet");
                    string n = EditorGUILayout.DelayedTextField(CurrentUserSet.name);
                    if (n != CurrentUserSet.name && !string.IsNullOrWhiteSpace(n)) { CurrentUserSet.name = n.Trim(); SaveColourSets(); _renamingSet = false; }
                    if (GUILayout.Button(Loc.T("OK"), EditorStyles.miniButton, GUILayout.Width(30))) _renamingSet = false;
                }
                else
                {
                    int idx = EditorGUILayout.Popup(_colourSetIndex, names.ToArray());
                    if (idx != _colourSetIndex) { _colourSetIndex = idx; if (idx == 0) _texturePalette = null; }
                }
                if (GUILayout.Button(new GUIContent("▾", Loc.T("Colour set options: new, rename, duplicate, delete, save texture colours")), EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(Loc.T("New colour set")), false, () =>
                    {
                        sets.Add(new ColourSet { name = UniqueSetName("Colour set") });
                        SaveColourSets();
                        _colourSetIndex = sets.Count;
                        _renamingSet = true;
                        Repaint();
                    });
                    if (CurrentUserSet != null)
                    {
                        var cur = CurrentUserSet;
                        menu.AddItem(new GUIContent(Loc.T("Rename this set")), false, () => { _renamingSet = true; Repaint(); });
                        menu.AddItem(new GUIContent(Loc.T("Duplicate this set")), false, () =>
                        {
                            sets.Add(new ColourSet { name = UniqueSetName(cur.name), colors = new List<Color>(cur.colors) });
                            SaveColourSets();
                            _colourSetIndex = sets.Count;
                            Repaint();
                        });
                        if (sets.Count > 1)
                            menu.AddItem(new GUIContent(Loc.T("Delete this set…")), false, () =>
                            {
                                if (!StudioDialog.Ask(Loc.T("Delete colour set"), Loc.F("Delete “{0}” ({1} colours)? This can't be undone.", cur.name, cur.colors.Count), Loc.T("Delete"), Loc.T("Cancel"))) return;
                                sets.Remove(cur);
                                SaveColourSets();
                                _colourSetIndex = Mathf.Clamp(_colourSetIndex, 1, sets.Count);
                                Repaint();
                            });
                    }
                    if (_colourSetIndex == 0 || _texturePalette != null)
                        menu.AddItem(new GUIContent(Loc.T("Save texture colours as a set")), false, () =>
                        {
                            var pal = TexturePalette();
                            if (pal.Count == 0) return;
                            sets.Add(new ColourSet { name = UniqueSetName(CurrentSlot?.texture != null ? CurrentSlot.texture.name : "Texture colours"), colors = new List<Color>(pal) });
                            SaveColourSets();
                            _colourSetIndex = sets.Count;
                            Repaint();
                        });
                    menu.ShowAsContext();
                }
            }

            if (_colourSetIndex == 0)
            {
                var pal = TexturePalette();
                if (pal.Count == 0) { GUILayout.Label(Loc.T("Pick a texture in 1 · Pick to see its colours."), StudioGUI.Hint); return; }
                int hit = TileGrid(pal, 18, out _, i => Loc.T("Colour from this texture: click to use"));
                if (hit >= 0) SetMainColour(pal[hit]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Loc.T("The main colours of the texture as it looks now."), StudioGUI.Hint);
                    if (GUILayout.Button(new GUIContent(Loc.T("Refresh"), Loc.T("Read the colours again (after painting)")), EditorStyles.miniButton, GUILayout.Width(58))) _texturePalette = null;
                }
                return;
            }

            var set = CurrentUserSet;
            int clicked = TileGrid(set.colors, 18, out bool right, i => Loc.T("Click to use · right-click to replace or delete"), extraTiles: 1);
            var plus = ExtraTileRect(set.colors.Count, 18);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(plus, StudioGUI.HeaderLine);
                GUI.Label(plus, "+", new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter });
            }
            if (GUI.Button(plus, new GUIContent("", Loc.T("Add the main colour to this set")), GUIStyle.none)) AddToColourSet(_brush.color);
            if (clicked >= 0 && !right) SetMainColour(set.colors[clicked]);
            if (clicked >= 0 && right)
            {
                int i = clicked;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(Loc.T("Use this colour")), false, () => SetMainColour(set.colors[i]));
                menu.AddItem(new GUIContent(Loc.T("Replace with the main colour")), false, () => { set.colors[i] = new Color(_brush.color.r, _brush.color.g, _brush.color.b, 1f); SaveColourSets(); Repaint(); });
                menu.AddItem(new GUIContent(Loc.T("Move to the front")), false, () => { var c = set.colors[i]; set.colors.RemoveAt(i); set.colors.Insert(0, c); SaveColourSets(); Repaint(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(Loc.T("Delete colour")), false, () => { set.colors.RemoveAt(i); SaveColourSets(); Repaint(); });
                menu.ShowAsContext();
            }
            if (set.colors.Count == 0) GUILayout.Label(Loc.T("Click + to add the main colour. Saved sets are shared by all your projects."), StudioGUI.Hint);
        }

        List<Color> TexturePalette()
        {
            if (_texturePalette != null) return _texturePalette;
            if (_comp == null) return new List<Color>(); // not cached: try again once a texture is loaded
            var px = _comp.ResultThumbnail(96);
            for (int i = 0; i < px.Length; i++) px[i].a = 1f; // colour only: masks and emission maps often have no alpha
            _texturePalette = Recolour.Palette(px, 16, out _);
            return _texturePalette;
        }

        void DrawApproximatePalette()
        {
            const int cols = 9, rows = 7;
            float width = PanelContentWidth - 18f;
            float tile = Mathf.Clamp(Mathf.Floor(width / cols) - 2, 12, 34);
            var area = GUILayoutUtility.GetRect(0, rows * (tile + 2), GUILayout.ExpandWidth(true));
            var main = _brush.color;
            for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                float tx = (i - (cols - 1) / 2f) / ((cols - 1) / 2f) * _approxSpreadAcross;
                float ty = ((rows - 1) / 2f - j) / ((rows - 1) / 2f) * _approxSpreadDown;
                var c = ColourMath.Vary(ColourMath.Vary(main, _approxAcross, tx), _approxDown, ty);
                bool centre = i == (cols - 1) / 2 && j == (rows - 1) / 2;
                var r = new Rect(area.x + i * (tile + 2), area.y + j * (tile + 2), tile, tile);
                if (ColourTile(r, c, centre ? Loc.T("The main colour") : Loc.T("Click to use this variation"), centre) == 1) SetMainColour(c);
            }
            DrawApproxAxis(Loc.T("Across"), ref _approxAcross, ref _approxSpreadAcross);
            DrawApproxAxis(Loc.T("Down"), ref _approxDown, ref _approxSpreadDown);
            GUILayout.Label(Loc.T("Variations of the main colour (centre, white outline). Try Hue across and Value down to find shadow and highlight colours."), StudioGUI.Hint);
        }

        static string AxisLabel(string name) => name switch
        {
            "Hue" => Loc.T("Hue"), "Saturation" => Loc.T("Saturation"), "Value" => Loc.T("Value"), "Lightness" => Loc.T("Lightness"),
            "Red" => Loc.T("Red"), "Green" => Loc.T("Green"), "Blue" => Loc.T("Blue"), _ => name,
        };

        void DrawApproxAxis(string label, ref ColourMath.Axis axis, ref float spread)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(48));
                axis = (ColourMath.Axis)EditorGUILayout.Popup((int)axis, Array.ConvertAll(ColourMath.AxisNames, AxisLabel), GUILayout.Width(88));
                spread = EditorGUILayout.Slider(spread, 0.02f, 1f);
            }
        }

        void DrawIntermediatePalette()
        {
            if (_mixCorners == null || _mixCorners.Length != 4)
                _mixCorners = new[] { _brush.color, _brush.subColor, Color.white, new Color(0.12f, 0.1f, 0.16f, 1f) };
            int n = Mathf.Clamp(_mixSteps, 3, 12);
            float width = PanelContentWidth - 18f;
            float tile = Mathf.Clamp(Mathf.Floor(width / n) - 2, 10, 30);
            var area = GUILayoutUtility.GetRect(0, n * (tile + 2), GUILayout.ExpandWidth(true));
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1), v = j / (float)(n - 1);
                var top = ColourMath.Mix(_mixCorners[0], _mixCorners[1], u);
                var bottom = ColourMath.Mix(_mixCorners[2], _mixCorners[3], u);
                var c = ColourMath.Mix(top, bottom, v);
                int corner = (i == 0 && j == 0) ? 0 : (i == n - 1 && j == 0) ? 1 : (i == 0 && j == n - 1) ? 2 : (i == n - 1 && j == n - 1) ? 3 : -1;
                var r = new Rect(area.x + i * (tile + 2), area.y + j * (tile + 2), tile, tile);
                int click = ColourTile(r, c, corner >= 0 ? Loc.T("Corner: click to put the main colour here · right-click to use it") : Loc.T("Click to use this blend"), corner >= 0);
                if (click == 0) continue;
                if (corner >= 0 && click == 1)
                {
                    Undo.RecordObject(this, "Set corner colour");
                    _mixCorners[corner] = new Color(_brush.color.r, _brush.color.g, _brush.color.b, 1f);
                }
                else SetMainColour(c);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Loc.T("Steps"), GUILayout.Width(48));
                _mixSteps = EditorGUILayout.IntSlider(_mixSteps, 3, 12);
            }
            GUILayout.Label(Loc.T("Click a corner (white outline) to put the main colour there; click any other tile to use it. Blends are perceptual, so they don't go muddy."), StudioGUI.Hint);
        }
    }
}
