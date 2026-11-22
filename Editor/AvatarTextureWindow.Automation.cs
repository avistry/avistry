using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Entry points for automation (the dev-only agent harness in the project calls these; see InternalsVisibleTo).
    /// Each one does what the matching UI interaction does, through the same code path, without needing mouse events.
    /// The UI never calls them. The one shipped caller is Editor/Agent (the AI helper facade), which may use only the
    /// hooks on its allowlist and runs each of them inside the same Undo and dialog rules as a click.
    /// </summary>
    partial class AvatarTextureWindow
    {
        /// <summary>A separate window the agent harness tests in (kept across script reloads); never the user's window.</summary>
        [SerializeField] bool _automationSandbox;
        internal bool AutoSandbox { get => _automationSandbox; set { _automationSandbox = value; titleContent = StudioGUI.Icon("Grid.PaintTool", value ? Loc.T("Avistry Textures (test)") : Loc.T("Avistry Textures"), ""); } }

        internal static AvatarTextureWindow AutoFind(bool open)
        {
            var w = Shown();
            if (w == null && open) { Open(); w = Shown(); }
            return w;
        }

        /// <summary>The window that is actually open (in a dock or floating), not a leftover instance from a reload.</summary>
        static AvatarTextureWindow Shown()
        {
            var parentField = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var all = Resources.FindObjectsOfTypeAll<AvatarTextureWindow>().Where(x => !x._automationSandbox).ToArray();
            return all.FirstOrDefault(x => parentField != null && parentField.GetValue(x) as Object != null) ?? all.FirstOrDefault();
        }

        /// <summary>Runs the pending rebuild and composite now (normally done on the next repaint).</summary>
        internal void AutoSettle()
        {
            if (_needsRebuild) Rebuild();
            if (_comp != null && _dirty) ComposeNow();
            Repaint();
            SceneView.RepaintAll();
        }

        internal string AutoLastToast()
        {
            if (string.IsNullOrEmpty(_toastText)) return null;
            return EditorApplication.timeSinceStartup <= _toastUntil ? _toastText : null;
        }

        internal void AutoClearToast() { _toastText = null; }

        /// <summary>Chooses the mesh, material slot and texture, like the Target tab. Returns null or why it failed.</summary>
        internal string AutoTarget(Renderer r, int materialIndex, int slotIndex, string textureName)
        {
            if (r != null && r != _renderer)
            {
                if (!ConfirmTargetChange("part")) return "Cancelled (edits that were not saved yet were kept).";
                SetRenderer(r);
            }
            if (_renderer == null) return "No part chosen.";
            if (materialIndex >= 0 && materialIndex != _materialIndex)
            {
                if (materialIndex >= _renderer.sharedMaterials.Length) return $"The part has {_renderer.sharedMaterials.Length} material slot(s).";
                if (!ConfirmTargetChange("material")) return "Cancelled (edits that were not saved yet were kept).";
                _materialIndex = materialIndex;
                ChooseSlot(0);
            }
            // The slot's material may have been swapped (e.g. a fixture variant): pick the new one up.
            var slotMats = _renderer.sharedMaterials;
            if (_materialIndex < slotMats.Length && slotMats[_materialIndex] != _material) _needsRebuild = true;
            if (_needsRebuild) Rebuild();
            int want = slotIndex;
            if (!string.IsNullOrEmpty(textureName))
                want = _slots.FindIndex(s => s.texture != null && (s.texture.name == textureName || s.properties.Contains(textureName) || s.displayName == textureName));
            if (want >= 0 && want != _slotIndex)
            {
                if (want >= _slots.Count) return $"This material has {_slots.Count} texture(s).";
                if (!ConfirmTargetChange("texture")) return "Cancelled (edits that were not saved yet were kept).";
                ChooseSlot(want);
                Rebuild();
            }
            else if (!string.IsNullOrEmpty(textureName) && want < 0) return $"No texture called “{textureName}” on this material.";
            AutoSettle();
            return null;
        }

        /// <summary>A brush / eraser / fill / blend / mask stroke through texture UV points (0..1), as on the canvas.</summary>
        internal string AutoStrokeUV(IList<Vector2> uvs, float pressure)
        {
            if (uvs == null || uvs.Count == 0) return "No points.";
            if (_comp == null) return "Nothing to paint on: choose a target first.";
            if (_tool != Tool.Paint) SetTool(Tool.Paint);
            if (GradientToolActive) return "The gradient tool uses 'gradient' (start and end), not strokes.";
            if (!CanPaint(out var why)) return why;
            int before = AutoStrokeTotal();
            double toastBefore = _toastUntil;
            BeginStroke(uvs[0], pressure, -1);
            if (_liveStroke == null)
            {
                // A fill finishes on the first click; anything else that didn't start explains why in a toast.
                AutoSettle();
                if (AutoStrokeTotal() > before) return null;
                return _toastUntil != toastBefore && AutoLastToast() != null ? AutoLastToast() : "The stroke didn't start.";
            }
            for (int i = 1; i < uvs.Count; i++) AddStrokePoint(uvs[i], pressure, true, -1);
            EndStroke();
            AutoSettle();
            return null;
        }

        /// <summary>Paint and mask strokes over every layer (to tell whether a stroke or fill landed).</summary>
        internal int AutoStrokeTotal()
        {
            if (_recipe == null) return 0;
            int n = 0;
            foreach (var l in _recipe.layers)
            {
                if (l == null) continue;
                if (l is PaintLayer pl) n += pl.strokes.Count;
                n += l.maskStrokes.Count;
            }
            return n;
        }

        /// <summary>Changes whenever the window shows a new toast.</summary>
        internal double AutoToastStamp => _toastUntil;

        internal object AutoLastBake => new { png = _lastBakePng, material = _lastBakeMaterial, recipe = _lastBakeRecipe, clearedLayers = _lastBakeClearedLayers };

        /// <summary>
        /// A stroke on the model, as in the Scene view: each point is a ray (origin, direction) in world space. Points
        /// that miss the mesh break the stroke, like lifting the brush over empty space.
        /// </summary>
        internal string AutoStrokeRays(IList<Ray> rays, float pressure, out int hits)
        {
            hits = 0;
            if (_comp == null || _uv == null) return "Nothing to paint on: choose a target first.";
            if (_tool != Tool.Paint) SetTool(Tool.Paint);
            if (GradientToolActive) return "The gradient tool uses 'gradient', not strokes.";
            if (!CanPaint(out var why)) return why;
            if (_uv.PoseChanged()) _uv.RefreshPose();
            AutoClearToast();
            bool started = false;
            int lastIsland = -1;
            foreach (var ray in rays)
            {
                if (!_uv.Raycast(ray, out var hit)) { lastIsland = -1; continue; }
                hits++;
                if (!started)
                {
                    BeginStroke(hit.uv, pressure, hit.island, hit);
                    started = true;
                }
                else AddStrokePoint(hit.uv, pressure, lastIsland == hit.island, hit.island, hit, lastIsland >= 0);
                lastIsland = hit.island;
            }
            EndStroke();
            AutoSettle();
            return hits == 0 ? "No point hit the part." : null;
        }

        /// <summary>A click on the model in the Scene view with "Click the model to select pieces" on; returns the toast it showed, if any.</summary>
        internal string AutoScenePick(Ray ray, bool add, bool remove)
        {
            if (_uv == null) return "Nothing to pick: choose a target first.";
            AutoClearToast();
            ScenePickClick(ray, add, remove);
            return AutoLastToast();
        }

        /// <summary>Draws a gradient from one UV point to another, like dragging the gradient tool on the canvas.</summary>
        internal string AutoGradient(Vector2 fromUV, Vector2 toUV)
        {
            if (_comp == null) return "Nothing to paint on: choose a target first.";
            if (_tool != Tool.Paint) SetTool(Tool.Paint);
            SetPaintTool(3);
            if (!BeginGradientDrag(UVToCanvas(fromUV))) return AutoLastToast() ?? "The gradient didn't start.";
            UpdateGradientDrag(UVToCanvas(toUV), false);
            EndGradientDrag();
            AutoSettle();
            return null;
        }

        internal void AutoPick(Vector2 uv) { PickColour(uv); Repaint(); }

        internal void AutoBake() => Bake();

        /// <summary>1 · Pick › Make maps › Bake ambient occlusion (settings: ao.*). Returns timing and the file.</summary>
        internal string AutoBakeAO()
        {
            _lastAo = default;
            BakeAmbientOcclusion();
            AutoSettle();
            return _lastAo.texture == null ? null
                : $"{_lastAo.path}: {_lastAo.width}×{_lastAo.height}, {_lastAo.directions} directions, {_lastAo.casters} caster part(s), {_lastAo.seconds:0.00} s";
        }

        /// <summary>1 · Pick › Make maps › Make normal map (settings: nrm.*); use = also press Use it.</summary>
        internal Texture2D AutoMakeNormalMap(bool use)
        {
            MakeNormalMap();
            if (use) UseMadeNormalMap();
            AutoSettle();
            return _madeNormalFor(CurrentSlot);
        }

        internal Material AutoMaterial => _material;
        internal Renderer AutoRenderer => _renderer;
        internal MeshUV AutoMeshUV => _uv;
        internal Compositor AutoCompositor => _comp;
        internal TextureRecipe AutoRecipe => _recipe;
        internal List<TextureSlot> AutoSlots => _slots;
        internal int AutoSlotIndex => _slotIndex;
        internal int AutoMaterialIndex => _materialIndex;
        internal Layer AutoActiveLayer => ActiveLayer;
        internal bool AutoDirty => _dirty;

        internal void AutoSelectLayer(Layer l)
        {
            _activeLayerId = l?.id;
            Repaint();
        }

        internal void AutoInsertLayer(Layer l) { InsertLayer(l); AutoSettle(); }
        internal void AutoRemoveLayer(int i) { RemoveLayer(i); AutoSettle(); }
        internal void AutoMoveLayer(int i, int dir) { MoveLayer(i, dir); AutoSettle(); }
        internal void AutoDuplicateLayer(int i) { DuplicateLayer(i); AutoSettle(); }

        /// <summary>After fields were changed from outside: record nothing, just recomposite and redraw.</summary>
        internal void AutoEdited(bool recipe, bool brush)
        {
            if (recipe) MarkEdited();
            if (brush) MarkBrushEdited();
            AutoSettle();
        }

        internal void AutoSetTab(string name)
        {
            if (System.Enum.TryParse<Tab>(name, true, out var t)) SetTab(t);
        }

        internal void AutoSetPaintTool(int t) { SetTool(Tool.Paint); SetPaintTool(t); }

        /// <summary>Sets the language as the Language menu does ("" follows Unity) and returns the language now in use.</summary>
        internal string AutoSetLanguage(string code) { SetLanguage(code); AutoSettle(); return Loc.Current; }

        /// <summary>Sets the side panel's width through the splitter's own clamp (260 at the least) and returns what it ended up as.</summary>
        internal float AutoSetPanelWidth(float w) { SetPanelWidth(w); AutoSettle(); return _panelWidth; }

        // Colour Match
        internal void AutoMatchMaterial(ColourMatchLayer cm, Material m) => SetMatchSource(cm, m, true);
        internal void AutoMatchTexture(ColourMatchLayer cm, Texture2D t) => SetMatchTexture(cm, t, true);
        internal void AutoFindMatchColours(ColourMatchLayer cm) => FindMatchColours(cm);
        internal void AutoApplyMatchMaterialSettings(ColourMatchLayer cm) => ApplyMatchMaterialSettings(cm);

        /// <summary>Selects UV islands, like clicking them in the Select tab (add / remove keep the rest).</summary>
        internal void AutoSelectIslands(List<int> islands, bool add, bool remove) { ApplySelection(islands, add, remove); Repaint(); }
        internal List<int> AutoSelection => new List<int>(_selection);

        // Selection tools. Each runs the method the panel, the texture view or the Scene bar runs; the first four return the
        // selection after the call. They are not on the AI helper's allowlist.
        internal void AutoSetSelectMode(int mode) => SetSelectMode((SelectMode)mode);

        internal List<int> AutoBoxUV(Vector2 a, Vector2 b, int op) { SelectBoxUV(a, b, (SelectOp)op); return AutoSelection; }

        internal List<int> AutoLassoUV(List<Vector2> poly, int op) { SelectLassoUV(poly, (SelectOp)op); return AutoSelection; }

        internal List<int> AutoWandUV(Vector2 uv, float tol, bool contiguous, int op)
        {
            _wandTolerance = tol;
            _wandContiguous = contiguous;
            WandAt(uv, (SelectOp)op);
            return AutoSelection;
        }

        /// <summary>The Wand with Layer mask output on the current layer. Returns why it could not, or null.</summary>
        internal string AutoWandMask(Vector2 uv, float tol, int op)
        {
            _wandTolerance = tol;
            var why = WandMaskAt(uv, (SelectOp)op);
            AutoSettle();
            return why;
        }

        internal List<int> AutoSelectPart(int island, int op) { SelectPartOf(new List<int> { island }, (SelectOp)op); return AutoSelection; }

        internal List<int> AutoSelectTile(Vector2Int tile, int op) { SelectTile(tile, (SelectOp)op); return AutoSelection; }

        internal List<int> AutoGrow(int dir) => GrowSelection(dir);

        /// <summary>
        /// A box over the Scene view (an empty rect = the whole view), like dragging in Box mode. Returns "skip" without a Scene view,
        /// else the reason it could not run, or null.
        /// </summary>
        internal string AutoSceneBox(Rect guiRect, int op, out int count)
        {
            count = 0;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null || sv.camera.pixelWidth < 2) return "skip";
            var cam = sv.camera;
            // The Scene camera only moves to the view on the next repaint: put it where the view is now (the next repaint does the same).
            cam.transform.SetPositionAndRotation(sv.pivot + sv.rotation * new Vector3(0f, 0f, -sv.cameraDistance), sv.rotation);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var view = new Rect(0f, 0f, cam.pixelWidth / ppp, cam.pixelHeight / ppp);
            var box = guiRect.width < 1f || guiRect.height < 1f ? view
                : Rect.MinMaxRect(Mathf.Max(guiRect.xMin, view.xMin), Mathf.Max(guiRect.yMin, view.yMin), Mathf.Min(guiRect.xMax, view.xMax), Mathf.Min(guiRect.yMax, view.yMax));
            return SelectByShapeScene(cam, box, p => box.Contains(p), (SelectOp)op, out count);
        }

        internal bool AutoSceneBarVisible() => SceneBarVisible;

        /// <summary>The ids of the Quick material controls the open material offers (empty for other shaders).</summary>
        internal List<string> AutoQuickControls() => QuickMaterialProfiles.For(_material).Select(c => c.id).ToList();

        /// <summary>Make an editable copy (the dialog is answered by StudioDialog.Override). Returns the copy's path, or the reason it was not made.</summary>
        internal string AutoMakeEditableCopy()
        {
            string r = MakeEditableCopy();
            AutoSettle();
            return r;
        }

        /// <summary>One Quick material control: a Color, a float or a bool by control. Returns null on success, else why it did not change.</summary>
        internal string AutoQuickSet(string id, object value)
        {
            var c = QuickMaterialProfiles.For(_material).FirstOrDefault(x => x.id == id);
            return c == null ? $"This material has no '{id}' control." : SetQuick(c, value);
        }

        internal bool AutoQuickEditable() => QuickEditable(out _);

        /// <summary>The island under a UV point (-1 for none).</summary>
        internal int AutoIslandAt(Vector2 uv)
        {
            if (_uv == null) return -1;
            var list = _uv.IslandsAtUV(uv);
            return list.Count > 0 ? list[0] : -1;
        }
    }
}
