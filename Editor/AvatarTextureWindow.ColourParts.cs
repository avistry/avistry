using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// 2 · Colour › By part: colour one part of the model, then the next, each keeping its own colour. A part is a
    /// RecolourPartLayer (hue / saturation / brightness limited to its UV islands) in the texture's layer stack, so it
    /// shows on the model at once and is saved into the texture in 5 · Save. Parts are picked right on the model in the
    /// Scene view (or on the texture view): click toggles an island, Shift adds, Ctrl removes. Meshes whose own material
    /// uses the same texture are included automatically, since saving the PNG changes them too.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _rcByPart;              // "By part" chosen (old windows: _rc.partial also means it)
        [SerializeField] string _rcPartId;            // the part the sliders edit
        [SerializeField] Renderer _rcLastPickRenderer; // the mesh last clicked (for "Whole mesh")

        bool ByPart => _rcByPart || _rc.partial;

        List<RecolourPartLayer> Parts => _recipe != null ? _recipe.layers.OfType<RecolourPartLayer>().ToList() : new List<RecolourPartLayer>();

        RecolourPartLayer CurrentPart =>
            string.IsNullOrEmpty(_rcPartId) || _recipe == null ? null : _recipe.layers.OfType<RecolourPartLayer>().FirstOrDefault(l => l.id == _rcPartId);

        Texture MaterialMainTexture => _material != null && _material.HasProperty("_MainTex") ? _material.GetTexture("_MainTex") : null;

        /// <summary>The texture being edited is the material's main texture (parts change that one).</summary>
        bool MainSlotEdited => CurrentSlot != null && MaterialMainTexture != null && CurrentSlot.texture == MaterialMainTexture;

        /// <summary>Clicks on the model / texture view go to the parts (not the Select or Paint tools).</summary>
        bool PartPicking => _tab == Tab.Colour && ByPart && _uv != null && _comp != null && MainSlotEdited && _matchPick == MatchPick.None && !_scenePickArmed;

        void EnterByPart()
        {
            Undo.RecordObject(this, "Colour by part");
            _rcByPart = true;
            if (_tool == Tool.Paint) { EndStroke(); _tool = Tool.Select; } // clicks pick parts now, not paint
            var part = CurrentPart ?? Parts.LastOrDefault();
            _rcPartId = part?.id;
            if (part != null) { _activeLayerId = part.id; _selection = new List<int>(part.islands); RebuildSelectionVisuals(); }
            _rcThumbKey = null;
            EditorApplication.delayCall += PrepareByPart;
        }

        void LeaveByPart()
        {
            Undo.RecordObject(this, "Colour whole material");
            _rcByPart = false;
            if (_rc.partial)
            {
                // An old "Selected islands only" shader recolour: back to the whole material, as that mode did.
                _rc.partial = false;
                _rcMaskKey = null;
                ApplyRecolour();
            }
            _rcThumbKey = null;
            SceneView.RepaintAll();
        }

        /// <summary>Opens the main texture and includes every mesh showing it (they all change when the PNG is saved).</summary>
        void PrepareByPart()
        {
            if (this == null || !ByPart || _material == null) return;
            if (_needsRebuild) Rebuild();
            var main = MaterialMainTexture;
            if (main != null && CurrentSlot != null && CurrentSlot.texture != main)
            {
                int idx = _slots.FindIndex(s => s.texture == main);
                if (idx < 0) return;
                if (!ConfirmTargetChange("texture")) return;
                Undo.RecordObject(this, "Change texture");
                ChooseSlot(idx);
                Rebuild();
            }
            if (CurrentSlot != null) // look again: meshes may have been added to the avatar since the last rebuild
                _textureUsers = TargetResolver.FindTextureUsers(TargetResolver.AvatarRoot(_renderer), _material, CurrentSlot.texture);
            var off = _textureUsers.Where(u => u.slot.renderer != null && !IsAlsoIncluded(u)).ToList();
            if (off.Count > 0)
            {
                SetMeshesIncluded(off, true);
                {
                    string names = string.Join(", ", off.Select(u => u.slot.renderer.name));
                    Toast(off.Count == 1
                        ? Loc.F("Also colouring {0}: its material uses the same texture, so it changes too when you save. Ctrl+Z leaves them out.", names)
                        : Loc.F("Also colouring {0}: their materials use the same texture, so they change too when you save. Ctrl+Z leaves them out.", names), MessageType.Info, 6f);
                }
            }
            Repaint();
        }

        string UniquePartName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Part";
            var names = new HashSet<string>(Parts.Select(p => p.name));
            if (!names.Contains(baseName)) return baseName;
            for (int n = 2; ; n++) if (!names.Contains($"{baseName} {n}")) return $"{baseName} {n}";
        }

        /// <summary>The mesh an island belongs to (the edited mesh or an included one).</summary>
        Renderer RendererOfIsland(int island)
        {
            if (_uv == null || island < 0) return null;
            for (int t = 0; t < _uv.triIsland.Length; t++)
                if (_uv.triIsland[t] == island) return _uv.PartOfTriangle(t)?.renderer;
            return null;
        }

        List<int> IslandsOfRenderer(Renderer r)
        {
            var set = new HashSet<int>();
            if (_uv == null || r == null) return new List<int>();
            foreach (var p in _uv.parts)
                if (p.renderer == r)
                    for (int t = p.triStart; t < p.triStart + p.triCount && t < _uv.triIsland.Length; t++) set.Add(_uv.triIsland[t]);
            return set.OrderBy(i => i).ToList();
        }

        /// <summary>A new part (top of the layer stack) on these islands, made current. Records its own undo.</summary>
        RecolourPartLayer NewPart(List<int> islands, string name)
        {
            if (_recipe == null) return null;
            EndStroke();
            Undo.RecordObject(_recipe, "New colour part");
            Undo.RecordObject(this, "New colour part");
            var part = new RecolourPartLayer
            {
                name = UniquePartName(name),
                maskMode = MaskMode.SelectedIslands,
                islands = islands.Where(i => i >= 0).Distinct().ToList(),
                oklab = PreviewOklab,
            };
            foreach (var other in Parts) other.islands.RemoveAll(part.islands.Contains); // an island belongs to one part
            _recipe.layers.Add(part);
            AutoChoosePartMode(part);
            _activeLayerId = _rcPartId = part.id;
            _selection = new List<int>(part.islands);
            RebuildSelectionVisuals();
            _rcThumbKey = null;
            MarkEdited();
            return part;
        }

        void SelectPart(RecolourPartLayer part)
        {
            if (part == null) return;
            Undo.RecordObject(this, "Choose colour part");
            _rcPartId = _activeLayerId = part.id;
            _selection = new List<int>(part.islands.Where(i => i >= 0));
            RebuildSelectionVisuals();
            _rcThumbKey = null;
            Repaint();
        }

        void DeletePart(RecolourPartLayer part)
        {
            int i = _recipe != null ? _recipe.layers.IndexOf(part) : -1;
            if (i < 0) return;
            bool wasCurrent = part.id == _rcPartId;
            RemoveLayer(i); // records the undo
            if (wasCurrent)
            {
                var next = Parts.LastOrDefault();
                _rcPartId = next?.id;
                if (next != null) _activeLayerId = next.id;
                _selection = next != null ? new List<int>(next.islands.Where(x => x >= 0)) : new List<int>();
                RebuildSelectionVisuals();
            }
            _rcThumbKey = null;
        }

        /// <summary>
        /// A click on islands: toggles them in the current part (Shift adds, Ctrl removes). Islands added here leave
        /// any other part. With no current part, a new part is made from them.
        /// </summary>
        void PartPick(List<int> islands, bool add, bool remove, Renderer from)
        {
            islands = islands?.Where(i => i >= 0).Distinct().ToList() ?? new List<int>();
            if (islands.Count == 0)
            {
                Toast(Loc.T("Click on the model itself (a part that uses this texture). Alt+drag or right-drag moves the Scene camera."), MessageType.Info, 4f);
                return;
            }
            if (from == null) from = RendererOfIsland(islands[0]);
            if (from != null) _rcLastPickRenderer = from;
            var part = CurrentPart;
            if (part == null)
            {
                if (remove) return;
                NewPart(islands, from != null ? from.name : "Part");
                Toast(Loc.F("New part “{0}”: colour it with the controls below. Click more pieces to add them.", CurrentPart?.name), MessageType.Info, 5f);
                return;
            }
            Undo.RecordObject(_recipe, "Colour part pieces");
            Undo.RecordObject(this, "Colour part pieces");
            part.islands.RemoveAll(i => i < 0); // "-1 = nothing" left over when a mesh was left out
            bool takeAway = remove || (!add && islands.All(part.islands.Contains));
            if (takeAway) part.islands.RemoveAll(islands.Contains);
            else
            {
                foreach (var i in islands) if (!part.islands.Contains(i)) part.islands.Add(i);
                foreach (var other in Parts) if (other != part) other.islands.RemoveAll(islands.Contains);
            }
            part.maskMode = MaskMode.SelectedIslands;
            AutoChoosePartMode(part);
            _activeLayerId = part.id;
            _selection = new List<int>(part.islands);
            RebuildSelectionVisuals();
            _rcThumbKey = null;
            MarkEdited();
        }

        const float GreyChroma = 0.03f; // average OKLCh chroma below this: hue shift can't give the part a colour
        Color[] _rcRawPixels;
        int _rcRawFor;

        /// <summary>The main texture's own colours, small (without the material's main tint), to judge what a part is made of.</summary>
        Color[] RawMainPixels()
        {
            var tex = MaterialMainTexture;
            if (tex == null) return null;
            int id = tex.GetInstanceID();
            if (_rcRawPixels == null || _rcRawFor != id) { _rcRawPixels = Recolour.SampleTexture(tex, RecolourThumbSize, Color.white); _rcRawFor = id; }
            return _rcRawPixels;
        }

        /// <summary>Average chroma of the part's source colours (0 = grey), or -1 when it has no islands to look at.</summary>
        float PartChroma(RecolourPartLayer p)
        {
            var raw = RawMainPixels();
            var mask = PartThumbMask(p);
            if (raw == null || mask == null || mask.Length != raw.Length) return -1f;
            var px = new List<Color>();
            for (int i = 0; i < mask.Length; i++) if (mask[i] > 0.5f && raw[i].a > 0.1f) px.Add(raw[i]);
            return px.Count == 0 ? -1f : Recolour.AverageChroma(px);
        }

        bool PartIsGrey(RecolourPartLayer p) { float c = PartChroma(p); return c >= 0f && c < GreyChroma; }

        /// <summary>
        /// A part that is still untouched starts in Tint when its colours are mostly white / grey (a hue shift can't colour
        /// grey), and goes back to Shift if islands with real colour are added. Edits are recorded by the caller.
        /// </summary>
        void AutoChoosePartMode(RecolourPartLayer p)
        {
            if (p == null || p.modeChosen || !p.Settings.IsIdentity || p.islands.All(i => i < 0)) return;
            float c = PartChroma(p);
            if (c < 0f) return;
            if (c < GreyChroma && !p.IsTint)
            {
                p.partMode = RecolourPartLayer.PartMode.Tint;
                p.tintColour = new Color(_rcTarget.r, _rcTarget.g, _rcTarget.b, 1f);
                p.autoTinted = true;
            }
            else if (c >= GreyChroma && p.autoTinted)
            {
                p.partMode = RecolourPartLayer.PartMode.Shift;
                p.autoTinted = false;
            }
        }

        /// <summary>Shift colours / Tint toolbar for the part.</summary>
        void SetPartMode(RecolourPartLayer part, RecolourPartLayer.PartMode mode)
        {
            if (part == null || part.partMode == mode) return;
            Undo.RecordObject(_recipe, mode == RecolourPartLayer.PartMode.Tint ? "Tint part" : "Shift part colours");
            part.partMode = mode;
            part.modeChosen = true;
            part.autoTinted = false;
            _rcThumbKey = null;
            MarkEdited();
        }

        /// <summary>The Tint colour and strength of a part (one undo step per drag).</summary>
        void SetPartTint(RecolourPartLayer part, Color colour, float strength)
        {
            if (part == null) return;
            Undo.RecordObject(_recipe, "Tint part");
            part.tintColour = new Color(colour.r, colour.g, colour.b, 1f);
            part.tintStrength = Mathf.Clamp01(strength);
            part.modeChosen = true; // (the "white/grey, so it's tinted" note stays until the toolbar is used)
            _rcThumbKey = null;
            MarkEdited();
        }

        /// <summary>Adds every island of a mesh to the current part (or a new one).</summary>
        void AddWholeMesh(Renderer r)
        {
            var list = IslandsOfRenderer(r);
            if (list.Count == 0) return;
            _rcLastPickRenderer = r;
            if (CurrentPart == null) { NewPart(list, r.name); return; }
            PartPick(list, true, false, r);
        }

        /// <summary>The hue / saturation / brightness of a part, from the Colour sliders (one undo step per drag).</summary>
        void SetPartValues(RecolourPartLayer part, float hue, float sat, float bri)
        {
            if (part == null) return;
            Undo.RecordObject(_recipe, "Colour part");
            part.hue = hue; part.saturation = sat; part.brightness = bri;
            _rcThumbKey = null;
            MarkEdited();
        }

        /// <summary>
        /// Where a Scene ray lands: on the edited meshes, or on a mesh whose own material uses this texture (that one is
        /// included first). Returns false when it hit nothing usable.
        /// </summary>
        bool PartPickRay(Ray ray, bool add, bool remove, bool deferInclude)
        {
            if (_uv == null) return false;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            float best = float.MaxValue;
            int island = -1;
            Renderer hitR = null;
            if (_uv.Raycast(ray, out var h)) { best = Vector3.Distance(ray.origin, h.point); island = h.island; hitR = RendererOfHit(h); }

            TargetResolver.TextureUser other = null;
            foreach (var u in _textureUsers)
            {
                if (u.slot.renderer == null || !u.slot.renderer.gameObject.activeInHierarchy || IsAlsoIncluded(u)) continue;
                var mu = PickMesh(u.slot.renderer, u.slot.materialIndex);
                if (mu == null) continue;
                if (mu.PoseChanged()) mu.RefreshPose();
                if (!mu.Raycast(ray, out var oh)) continue;
                float d = Vector3.Distance(ray.origin, oh.point);
                if (d < best - 1e-4f) { best = d; other = u; }
            }
            if (island >= 0 || other != null)
            {
                var cover = FrontBlocker(ray, best);
                if (cover != null) { Toast(CoveredMessage(cover), MessageType.Info, 3f); return false; }
            }
            if (other != null)
            {
                var u = other;
                if (deferInclude) EditorApplication.delayCall += () => { if (this != null) IncludeAndPick(u, ray, add, remove); };
                else IncludeAndPick(u, ray, add, remove);
                return true;
            }
            if (island < 0)
            {
                PartPick(null, add, remove, null); // explains where to click
                return false;
            }
            PartPick(new List<int> { island }, add, remove, hitR);
            return true;
        }

        void IncludeAndPick(TargetResolver.TextureUser u, Ray ray, bool add, bool remove)
        {
            var r = u.slot.renderer;
            SetMeshesIncluded(new[] { u }, true);
            if (_uv == null) return;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            if (_uv.Raycast(ray, out var h)) PartPick(new List<int> { h.island }, add, remove, RendererOfHit(h));
            Toast(Loc.F("Included {0}: its material ‘{1}’ uses the same texture, so it's coloured (and saved) along with this one. Ctrl+Z undoes.", r.name, u.material.name), MessageType.Info, 6f);
            Repaint();
        }

        /// <summary>Selection follows the current part (the part's islands are what counts). Layout only.</summary>
        void SyncPartSelection()
        {
            if (Event.current.type != EventType.Layout || !ByPart || _recipe == null) return;
            if (ActiveLayer is RecolourPartLayer active && active.id != _rcPartId) { _rcPartId = active.id; _rcThumbKey = null; }
            var part = CurrentPart;
            if (part == null) return;
            var want = part.islands.Where(i => i >= 0).ToList();
            if (want.Count != _selection.Count || want.Except(_selection).Any())
            {
                _selection = want;
                RebuildSelectionVisuals();
            }
        }

        void HandleScenePartPick(SceneView sv, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            var part = CurrentPart;
            DrawSceneOverlay(Loc.T("<b>Avistry · Recolour by part</b>") + "   " +
                             (part != null ? Loc.F("click to add or remove pieces of “{0}” · Shift add · Ctrl remove", part.name) : Loc.T("click a part of the model to start colouring it")) +
                             Loc.T(" · Alt+drag moves the camera"));
            switch (e.type)
            {
                case EventType.MouseMove:
                {
                    if (_uv.PoseChanged()) _uv.RefreshPose();
                    int island = _uv.PickIslandWorld(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                    if (island != _hoverIsland)
                    {
                        _hoverIsland = island;
                        if (island >= 0) BuildHoverHighlight();
                        sv.Repaint();
                    }
                    break;
                }
                case EventType.MouseLeaveWindow:
                    if (_hoverIsland >= 0) { _hoverIsland = -1; sv.Repaint(); }
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    PartPickRay(HandleUtility.GUIPointToWorldRay(e.mousePosition), e.shift, e.control || e.command, true);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        readonly Dictionary<string, float[]> _partThumbMasks = new Dictionary<string, float[]>();
        List<Color> _partPalette = new List<Color>();
        string _partPaletteKey;

        float[] PartThumbMask(RecolourPartLayer p)
        {
            if (_uv == null) return null;
            var ids = p.islands.Where(i => i >= 0).OrderBy(i => i).ToList();
            string key = $"{_uv.islandCount}:{string.Join(",", ids)}";
            if (_partThumbMasks.TryGetValue(key, out var m)) return m;
            if (_partThumbMasks.Count > 64) _partThumbMasks.Clear();
            var bytes = _uv.RasterIslandMask(RecolourThumbSize, RecolourThumbSize, new HashSet<int>(ids), 1);
            m = bytes.Select(b => b / 255f).ToArray();
            _partThumbMasks[key] = m;
            return m;
        }

        string PartsKey() => string.Join(";", Parts.Select(p => $"{p.id}|{p.enabled}|{p.hue:0.0}|{p.saturation:0.000}|{p.brightness:0.000}|{p.oklab}|{p.partMode}|{ColorUtility.ToHtmlStringRGB(p.tintColour)}|{p.tintStrength:0.000}|{p.opacity:0.00}|{string.Join(",", p.islands)}"));

        /// <summary>The "after" thumbnail in By part: every part's colour inside its islands (bottom to top); updates the swatches.</summary>
        Color[] PartsAfterPixels()
        {
            var o = (Color[])_rcPixels.Clone();
            foreach (var p in Parts)
            {
                var mask = PartThumbMask(p);
                if (mask == null || mask.Length != o.Length) continue;
                Color sum = Color.clear;
                float n = 0;
                for (int i = 0; i < o.Length; i++)
                {
                    if (mask[i] <= 0) continue;
                    var shifted = p.Preview(o[i]);
                    if (p.enabled) o[i] = Color.Lerp(o[i], shifted, mask[i] * p.opacity);
                    sum += shifted * mask[i];
                    n += mask[i];
                }
                if (n > 0) { var avg = sum / n; avg.a = 1; p.swatch = new Color(Mathf.Clamp01(avg.r), Mathf.Clamp01(avg.g), Mathf.Clamp01(avg.b), 1); }
            }
            return o;
        }

        /// <summary>Main colours of the current part's islands (for "Shift the selected colour to"), or of the texture.</summary>
        List<Color> PartPalette(RecolourPartLayer part)
        {
            if (part == null || _rcPixels == null) return _rcPalette;
            var mask = PartThumbMask(part);
            if (mask == null || mask.Length != _rcPixels.Length) return _rcPalette;
            string key = $"{_rcSampleFor?.GetInstanceID()}:{string.Join(",", part.islands)}";
            if (key == _partPaletteKey) return _partPalette;
            _partPaletteKey = key;
            var px = new List<Color>();
            for (int i = 0; i < mask.Length; i++) if (mask[i] > 0.5f) px.Add(_rcPixels[i]);
            _partPalette = px.Count > 0 ? Recolour.Palette(px.ToArray(), 5, out _) : new List<Color>();
            return _partPalette;
        }

        void DrawPartChoice()
        {
            SyncPartSelection();
            StudioGUI.BeginGroup(Loc.T("Which part"));
            StudioGUI.Count("colour.scope");
            int mode = WrapToolbar(ByPart ? 1 : 0, new[]
            {
                new GUIContent(Loc.T("Everything"), Loc.T("Recolour everything on the selected materials, through the shader's colour settings (no new PNG)")),
                new GUIContent(Loc.T("One piece at a time"), Loc.T("Colour one part of the model, then the next: each part keeps its own colour. Changes the texture (save it in 5 · Save)")),
            });
            if (mode != (ByPart ? 1 : 0))
            {
                if (mode == 1) EnterByPart(); else LeaveByPart();
            }
            if (ByPart) DrawPartsList();
            StudioGUI.EndGroup();
        }

        void DrawPartsList()
        {
            if (_comp == null || _uv == null)
            {
                StudioGUI.CheckRow(StudioGUI.Check.Problem, _uv == null && _comp != null
                    ? Loc.T("This part has no pieces to pick, so only Everything works here.")
                    : Loc.T("Pick the part and its texture in 1 · Pick first."));
                return;
            }
            if (!MainSlotEdited)
            {
                var main = MaterialMainTexture;
                StudioGUI.CheckRow(StudioGUI.Check.Problem, main != null
                    ? Loc.F("Parts colour the main texture (‘{0}’), but ‘{1}’ is open.", main.name, CurrentSlot?.texture?.name)
                    : Loc.T("This material has no main texture to colour."));
                if (main != null && GUILayout.Button(Loc.F("Edit ‘{0}’", main.name))) EditorApplication.delayCall += PrepareByPart;
                return;
            }

            GUILayout.Label(Loc.T("Click a part of the model in the Scene view (or on the texture) to colour it. Click again to take a piece out; " +
                            "Shift+click adds, Ctrl+click removes. Then make a new part for the next piece."), StudioGUI.Hint);

            StudioGUI.Count("colour.parts");
            System.Action pending = null;
            var parts = Parts;
            var e = Event.current;
            for (int k = parts.Count - 1; k >= 0; k--) // top of the stack first, like the layer list
            {
                var p = parts[k];
                bool current = p.id == _rcPartId;
                var row = EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
                if (e.type == EventType.Repaint && current) EditorGUI.DrawRect(row, StudioGUI.RowActive);
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition) && !current) { var pp = p; pending = () => SelectPart(pp); }

                var sw = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18), GUILayout.Height(20));
                sw.y += 2; sw.height = 16;
                if (e.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(sw, p.swatch);
                    StudioGUI.DrawBorder(sw, current ? StudioGUI.Accent : StudioGUI.HeaderLine, current ? 2 : 1);
                }
                if (current)
                {
                    EditorGUI.BeginChangeCheck();
                    string nm = EditorGUILayout.DelayedTextField(p.name, GUILayout.MinWidth(40));
                    if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(nm))
                    {
                        Undo.RecordObject(_recipe, "Rename part");
                        p.name = nm.Trim();
                        MarkEdited();
                    }
                }
                else GUILayout.Label(new GUIContent(p.name, Loc.T("Click to colour this part")), p.enabled ? StudioGUI.RowLabel : StudioGUI.RowLabelDisabled, GUILayout.MinWidth(40));
                int n = p.islands.Count(i => i >= 0);
                string info = (n == 0 ? Loc.T("no pieces") : Loc.F("{0} piece{1}", n, n == 1 ? "" : "s")) + (p.IsIdentity ? " · " + Loc.T("unchanged") : p.IsTint ? " · " + Loc.T("tint") : $" · {p.hue:+0;-0;0}°");
                GUILayout.Label(info, StudioGUI.MiniRight, GUILayout.Width(Mathf.Min(110f, Mathf.Max(60f, StudioGUI.MiniRight.CalcSize(new GUIContent(info)).x + 2f))));
                if (GUILayout.Button(new GUIContent("×", Loc.F("Delete “{0}” (Ctrl+Z brings it back)", p.name)), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    var pp = p;
                    pending = () => DeletePart(pp);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (parts.Count == 0)
                StudioGUI.CheckRow(StudioGUI.Check.Note, Loc.T("No parts yet. Click the first part on the model (for example the front hair)."));

            using (new EditorGUILayout.HorizontalScope())
            {
                bool fromSelection = CurrentPart == null && _selection.Count > 0;
                if (GUILayout.Button(new GUIContent(Loc.T("+ New part"), fromSelection ? Loc.F("A new part from the {0} selected piece(s)", _selection.Count) : Loc.T("Start a new part, then click it on the model")), GUILayout.Height(22)))
                {
                    var sel = fromSelection ? new List<int>(_selection) : new List<int>();
                    string nm = fromSelection ? (RendererOfIsland(sel[0])?.name ?? "Part") : "Part";
                    pending = () =>
                    {
                        NewPart(sel, nm);
                        if (sel.Count == 0) Toast(Loc.T("New part: now click it on the model in the Scene view."), MessageType.Info, 4f);
                    };
                }
                var meshes = _uv.parts.Select(x => x.renderer).Where(r => r != null).Distinct().ToList();
                using (new EditorGUI.DisabledScope(meshes.Count == 0))
                    if (GUILayout.Button(new GUIContent(Loc.T("Whole part ▾"), Loc.T("Add every piece of a part to this part")), GUILayout.Height(22), GUILayout.Width(100)))
                    {
                        var menu = new GenericMenu();
                        foreach (var r in meshes.OrderBy(r => r == _rcLastPickRenderer ? 0 : 1))
                        {
                            var rr = r;
                            menu.AddItem(new GUIContent(NoSubmenu(r.name) + (r == _rcLastPickRenderer ? Loc.T("  (last clicked)") : "")), false, () => AddWholeMesh(rr));
                        }
                        menu.ShowAsContext();
                    }
            }

            int others = _textureUsers.Count(u => u.slot.renderer != null && !IsAlsoIncluded(u));
            var shown = _uv.parts.Select(x => x.renderer).Where(r => r != null).Distinct().Count();
            if (others > 0)
            {
                StudioGUI.CheckRow(StudioGUI.Check.Note, others == 1 ? Loc.T("1 more part uses this texture with its own material and will change too when you save.") : Loc.F("{0} more parts use this texture with their own material and will change too when you save.", others));
                if (GUILayout.Button(Loc.T("Include them so they can be coloured too"))) EditorApplication.delayCall += PrepareByPart;
            }
            else if (shown > 1)
                GUILayout.Label(Loc.F("{0} parts use this texture; you can click any of them.", shown), StudioGUI.Hint);

            if (pending != null) { EditorApplication.delayCall += () => { if (this != null) { pending(); Repaint(); } }; }
        }

        /// <summary>By part: the texture is changed, so point at saving; the material-wide shader recolour is only a note.</summary>
        void DrawPartsFooter()
        {
            StudioGUI.BeginGroup(null);
            GUILayout.Label(Loc.T("These colours change the texture itself. Save it as a new PNG in 5 · Save; until then they're only a preview. " +
                            "Shadow, rim and emission colours are material-wide, so they stay as they are here (use Everything for those). " +
                            "Ctrl+Z undoes each step."), StudioGUI.Hint);
            using (new EditorGUI.DisabledScope(Parts.Count == 0))
                if (GUILayout.Button(new GUIContent(Loc.T("  Save as PNG in 5 · Save →"), StudioGUI.Icon("SaveAs", "").image), GUILayout.Height(26)))
                    EditorApplication.delayCall += () => SetTab(Tab.Save);
            StudioGUI.EndGroup();
        }

        internal string AutoPartMode(bool byPart)
        {
            if (byPart) { if (!ByPart) EnterByPart(); PrepareByPart(); }
            else if (ByPart) LeaveByPart();
            AutoSettle();
            return null;
        }

        internal RecolourPartLayer AutoPartNew(List<int> islands, string name)
        {
            var p = NewPart(islands ?? new List<int>(), string.IsNullOrEmpty(name) ? (islands != null && islands.Count > 0 ? RendererOfIsland(islands[0])?.name : null) ?? "Part" : name);
            AutoSettle();
            return p;
        }

        internal string AutoMainTint(Color c) { TintMainColour(c); AutoSettle(); return null; }

        internal RecolourPartLayer AutoCurrentPart => CurrentPart;
        internal List<RecolourPartLayer> AutoParts => Parts;
        internal void AutoPartSelect(RecolourPartLayer p) { SelectPart(p); AutoSettle(); }
        internal void AutoPartDelete(RecolourPartLayer p) { DeletePart(p); AutoSettle(); }
        internal void AutoPartWholeMesh(Renderer r) { AddWholeMesh(r ?? _rcLastPickRenderer); AutoSettle(); }

        /// <summary>"Shift the selected colour to": a Shift part turns its hue; a white / grey part can't, so it becomes a Tint of that colour.</summary>
        void ShiftPartToColour(RecolourPartLayer part, List<Color> palette, int swatch, Color target, bool matchBrightness)
        {
            if (PartIsGrey(part))
            {
                Undo.RecordObject(_recipe, "Tint part");
                part.partMode = RecolourPartLayer.PartMode.Tint;
                part.tintColour = new Color(target.r, target.g, target.b, 1f);
                part.modeChosen = true;
                part.autoTinted = false;
                _rcThumbKey = null;
                MarkEdited();
                Toast(Loc.T("This part is white/grey, so it's tinted: a hue shift can't colour grey."), MessageType.Info, 4f);
                return;
            }
            var m = part.Settings;
            Recolour.Match(palette[Mathf.Clamp(swatch, 0, palette.Count - 1)], target, m, matchBrightness, part.oklab);
            SetPartValues(part, m.hue, m.saturation, m.brightness);
        }

        /// <summary>Part settings for the harness. mode: "shift" | "tint"; tint colour and strength 0..1.</summary>
        internal string AutoPartTint(string mode, Color? tint, float? strength)
        {
            var p = CurrentPart;
            if (p == null) return "No current part: make one with part_new or part_pick.";
            if (!string.IsNullOrEmpty(mode))
            {
                if (mode.Equals("tint", System.StringComparison.OrdinalIgnoreCase)) SetPartMode(p, RecolourPartLayer.PartMode.Tint);
                else if (mode.Equals("shift", System.StringComparison.OrdinalIgnoreCase)) SetPartMode(p, RecolourPartLayer.PartMode.Shift);
                else return "mode is 'shift' or 'tint'.";
            }
            if (tint.HasValue || strength.HasValue) SetPartTint(p, tint ?? p.tintColour, strength ?? p.tintStrength);
            AutoSettle();
            return null;
        }

        internal string AutoPartSet(float? hue, float? sat, float? bri, bool? oklab, string name)
        {
            var p = CurrentPart;
            if (p == null) return "No current part: make one with part_new or part_pick.";
            SetPartValues(p, hue ?? p.hue, sat ?? p.saturation, bri ?? p.brightness);
            if (oklab.HasValue || !string.IsNullOrEmpty(name))
            {
                Undo.RecordObject(_recipe, "Colour part");
                if (oklab.HasValue) p.oklab = oklab.Value;
                if (!string.IsNullOrEmpty(name)) p.name = name;
                MarkEdited();
            }
            AutoSettle();
            return null;
        }

        /// <summary>"Shift the selected colour to": sourceSwatch = index in the current part's palette.</summary>
        internal string AutoPartShiftTo(Color target, int sourceSwatch, bool matchBrightness)
        {
            var p = CurrentPart;
            if (p == null) return "No current part.";
            EnsureRecolourPreview();
            var pal = PartPalette(p);
            if (pal.Count == 0) return "The part has no colours to shift (no pieces?).";
            ShiftPartToColour(p, pal, sourceSwatch, target, matchBrightness);
            AutoSettle();
            return null;
        }

        /// <summary>Scene clicks as rays (Shift = add, Ctrl = remove), the same as clicking the model in By part.</summary>
        internal string AutoPartPickRays(IList<Ray> rays, bool add, bool remove, out int hits)
        {
            hits = 0;
            if (!ByPart) return "Choose One piece at a time first (part_mode).";
            if (_uv == null) return "Choose a target first.";
            AutoClearToast();
            foreach (var ray in rays) if (PartPickRay(ray, add, remove, false)) hits++;
            AutoSettle();
            return hits == 0 ? "No point hit a part that uses this texture." : null;
        }

        /// <summary>Texture view clicks in By part (UV points).</summary>
        internal void AutoPartPickUV(Vector2 uv, bool add, bool remove)
        {
            if (_uv != null) PartPick(_uv.IslandsAtUV(uv), add, remove, null);
            AutoSettle();
        }

        internal Color[] AutoPartsAfterPixels() { EnsureRecolourPreview(); return _rcThumbAfter != null ? _rcThumbAfter.GetPixels() : null; }
    }
}
