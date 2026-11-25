using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _automationSandbox;
        internal bool AutoSandbox
        {
            get => _automationSandbox;
            set
            {
                _automationSandbox = value;
                if (value) SupportSession.Close(this); else SupportSession.Open(this);
                RefreshWindowTitle();
            }
        }

        internal static AvatarTextureWindow AutoFind(bool open)
        {
            var w = Shown();
            if (w == null && open) { Open(); w = Shown(); }
            return w;
        }

        static AvatarTextureWindow Shown()
        {
            var parentField = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var all = Resources.FindObjectsOfTypeAll<AvatarTextureWindow>().Where(x => !x._automationSandbox).ToArray();
            return all.FirstOrDefault(x => parentField != null && parentField.GetValue(x) as Object != null) ?? all.FirstOrDefault();
        }

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
                AutoSettle();
                if (AutoStrokeTotal() > before) return null;
                return _toastUntil != toastBefore && AutoLastToast() != null ? AutoLastToast() : "The stroke didn't start.";
            }
            for (int i = 1; i < uvs.Count; i++) AddStrokePoint(uvs[i], pressure, true, -1);
            EndStroke();
            AutoSettle();
            return null;
        }

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

        internal double AutoToastStamp => _toastUntil;

        internal object AutoLastBake => new { png = _lastBakePng, material = _lastBakeMaterial, recipe = _lastBakeRecipe, clearedLayers = _lastBakeClearedLayers };

        internal string AutoStrokeRays(IList<Ray> rays, float pressure, out int hits)
        {
            hits = 0;
            if (_comp == null || _uv == null) return "Nothing to paint on: choose a target first.";
            if (rays == null || rays.Count == 0) return "No points.";
            if (_tool != Tool.Paint) SetTool(Tool.Paint);
            if (GradientToolActive) return "The gradient tool uses 'gradient', not strokes.";
            if (!CanPaint(out var why)) return why;
            AutoClearToast();
            bool started = false;
            int lastIsland = -1;
            string startProblem = null;
            foreach (var ray in rays)
            {
                if (!RaycastVisible(ray, out var hit, out var coveredBy))
                {
                    if (!started && coveredBy != null)
                    {
                        startProblem = CoveredMessage(coveredBy);
                        if (_brush.fillTool) Toast(startProblem, MessageType.Info, 4f, Msg.BtnUseBrush, () => SetPaintTool(0));
                        else Toast(startProblem, MessageType.Info, 3f);
                        break;
                    }
                    lastIsland = -1;
                    continue;
                }
                hits++;
                if (!started)
                {
                    startProblem = BeginStroke(hit.uv, pressure, hit.island, hit);
                    started = true;
                }
                else AddStrokePoint(hit.uv, pressure, lastIsland == hit.island, hit.island, hit, lastIsland >= 0);
                lastIsland = hit.island;
            }
            EndStroke();
            AutoSettle();
            return startProblem ?? (hits == 0 ? "No point hit a visible piece." : null);
        }

        internal string AutoScenePick(Ray ray, bool add, bool remove)
        {
            if (_uv == null) return "Nothing to pick: choose a target first.";
            AutoClearToast();
            ScenePickClick(ray, add, remove);
            return AutoLastToast();
        }

        internal string AutoGradient(Vector2 fromUV, Vector2 toUV)
        {
            if (_comp == null) return "Nothing to paint on: choose a target first.";
            if (_tool != Tool.Paint) SetTool(Tool.Paint);
            SetPaintTool(3);
            float zoom = _zoom;
            if (zoom <= 0f) _zoom = 1f;
            try
            {
                AutoClearToast();
                if (!BeginTextureGradient(UVToCanvas(fromUV), allowAdjustment: false))
                    return AutoLastToast() ?? "The gradient didn't start.";
                UpdateGradientDrag(UVToCanvas(toUV), false);
                bool placed = EndGradientDrag();
                AutoSettle();
                return placed ? null : AutoLastToast() ?? "The gradient was cancelled.";
            }
            finally
            {
                if (zoom <= 0f) _zoom = zoom;
            }
        }

        internal void AutoPick(Vector2 uv) { PickColour(uv); Repaint(); }

        internal void AutoBake() => Bake();

        internal string AutoBakeAO()
        {
            _lastAo = default;
            BakeAmbientOcclusion();
            AutoSettle();
            return _lastAo.texture == null ? null
                : $"{_lastAo.path}: {_lastAo.width}×{_lastAo.height}, {_lastAo.directions} directions, {_lastAo.casters} caster part(s), {_lastAo.seconds:0.00} s";
        }

        internal Texture2D AutoMakeNormalMap(bool use)
        {
            var made = MakeNormalMap();
            if (use && made != null) UseMadeNormalMap(made);
            AutoSettle();
            return made;
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

        internal string AutoSetLanguage(string code) { SetLanguage(code); AutoSettle(); return Loc.Current; }

        internal float AutoSetPanelWidth(float w) { SetPanelWidth(w); AutoSettle(); return _panelWidth; }

        internal void AutoMatchMaterial(ColourMatchLayer cm, Material m) => SetMatchSource(cm, m, true);
        internal void AutoMatchTexture(ColourMatchLayer cm, Texture2D t) => SetMatchTexture(cm, t, true);
        internal void AutoFindMatchColours(ColourMatchLayer cm) => FindMatchColours(cm);
        internal void AutoApplyMatchMaterialSettings(ColourMatchLayer cm) => ApplyMatchMaterialSettings(cm);

        internal void AutoSelectIslands(List<int> islands, bool add, bool remove) { ApplySelection(islands, add, remove); Repaint(); }
        internal List<int> AutoSelection => new List<int>(_selection);

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

        internal string AutoSceneBox(Rect guiRect, int op, out int count)
        {
            count = 0;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null || sv.camera.pixelWidth < 2) return "skip";
            var cam = sv.camera;
            cam.transform.SetPositionAndRotation(sv.pivot + sv.rotation * new Vector3(0f, 0f, -sv.cameraDistance), sv.rotation);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var view = new Rect(0f, 0f, cam.pixelWidth / ppp, cam.pixelHeight / ppp);
            var box = guiRect.width < 1f || guiRect.height < 1f ? view
                : Rect.MinMaxRect(Mathf.Max(guiRect.xMin, view.xMin), Mathf.Max(guiRect.yMin, view.yMin), Mathf.Min(guiRect.xMax, view.xMax), Mathf.Min(guiRect.yMax, view.yMax));
            return SelectByShapeScene(cam, box, p => box.Contains(p), (SelectOp)op, out count);
        }

        internal bool AutoSceneBarVisible() => SceneBarVisible;

        internal List<string> AutoQuickControls() => QuickMaterialProfiles.For(_material).Select(c => c.id).ToList();

        internal string AutoMakeEditableCopy()
        {
            string r = MakeEditableCopy();
            AutoSettle();
            return r;
        }

        internal string AutoQuickSet(string id, object value)
        {
            var c = QuickMaterialProfiles.For(_material).FirstOrDefault(x => x.id == id);
            return c == null ? $"This material has no '{id}' control." : SetQuick(c, value);
        }

        internal bool AutoQuickEditable() => QuickEditable(out _);

        internal int AutoIslandAt(Vector2 uv)
        {
            if (_uv == null) return -1;
            var list = _uv.IslandsAtUV(uv);
            return list.Count > 0 ? list[0] : -1;
        }
    }
}
