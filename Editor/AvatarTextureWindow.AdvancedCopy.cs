using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [Flags]
        enum CopyMaterialGroup { Lighting = 1, Rim = 2, Outline = 4, Glow = 8 }

        [SerializeField] bool _foldAdvancedCopy;
        [SerializeField] bool _advancedCopyLayers;
        [SerializeField] Renderer _advancedCopySourcePart;
        [SerializeField] int _advancedCopySourceSlot;
        [SerializeField] CopyMaterialGroup _advancedCopyGroups = CopyMaterialGroup.Lighting;
        [SerializeField] bool _advancedCopyLayerValues = true, _advancedCopyLayerAppearance = true;
        [SerializeField] string _advancedCopyLayerId;
        bool _showAdvancedCopyPlan;
        static GUIStyle s_copyMaterialCreateStyle;
        (string json, string part) _advancedLayerSource;
        (Material source, Material destination, int sourceCrc, int destinationCrc) _copyPlanKey;
        List<MaterialSettingsMatch.Change> _copyPlan;

        void DrawAdvancedCopy()
        {
            bool open = StudioGUI.BeginFoldGroup(ref _foldAdvancedCopy, Loc.T("Advanced Copy"),
                tooltip: Loc.T("Reuse selected settings between parts or layers."));
            if (open)
            {
                int kind = _advancedCopyLayers ? 1 : 0;
                int selected = DrawAdvancedCopyChoice("copy.kind", GUIContent.none, kind, new[]
                {
                    new GUIContent(Loc.T("Material settings")),
                    new GUIContent(Loc.T("Layer settings")),
                });
                if (selected != kind)
                    ChangeAdvancedCopyChoice("Change copy type", () => _advancedCopyLayers = selected == 1);
                if (_advancedCopyLayers) DrawAdvancedLayerCopy();
                else DrawAdvancedMaterialCopy();
            }
            StudioGUI.EndGroup();
        }

        int DrawAdvancedCopyChoice(string id, GUIContent label, int selected, GUIContent[] choices)
        {
            if (!string.IsNullOrEmpty(label.text) && EditorStyles.label.CalcSize(label).x + 6f > PanelContentWidth * .4f)
            {
                GUILayout.Label(label, EditorStyles.miniLabel);
                label = GUIContent.none;
            }
            int next = PickChoice(label, selected, choices);
            StudioGUI.Count(id, GUILayoutUtility.GetLastRect());
            return next;
        }

        void ChangeAdvancedCopyChoice(string name, Action change)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, name);
            change();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        void DrawAdvancedMaterialCopy()
        {
            GUILayout.Label(Loc.F("Destination: {0} · Slot {1} · {2}", _renderer.name, _materialIndex + 1,
                    _material != null ? _material.name : Loc.T("No material")),
                StudioGUI.CheckLabel, GUILayout.MinWidth(0));
            if (_material == null) return;
            if (ShaderProfiles.Detect(_material.shader) == ShaderFamily.Other)
            {
                GUILayout.Label(Loc.T("Copy material settings between lilToon materials or between Poiyomi materials."), StudioGUI.Hint);
                return;
            }

            bool browse = GUILayout.Button(Loc.T("Browse avatar materials…"), EditorStyles.miniButton, GUILayout.Height(22f), GUILayout.MinWidth(0));
            StudioGUI.Count("copy.materialSource", GUILayoutUtility.GetLastRect());
            if (browse) BuildAdvancedMaterialSourceMenu().ShowAsContext();
            var part = (Renderer)EditorGUILayout.ObjectField(new GUIContent(Loc.T("Source part"), Loc.T("Drag the part to copy from here.")),
                _advancedCopySourcePart, typeof(Renderer), true, GUILayout.MinWidth(0));
            if (part != _advancedCopySourcePart) SetAdvancedMaterialSource(part, 0);
            if (part == null) return;

            var materials = part.sharedMaterials;
            if (materials.Length == 0)
            {
                GUILayout.Label(Loc.T("Choose a source part with a material."), StudioGUI.Hint);
                return;
            }
            int index = Mathf.Clamp(_advancedCopySourceSlot, 0, materials.Length - 1);
            if (materials.Length > 1)
            {
                var names = materials.Select((m, i) => new GUIContent(NoSubmenu($"{i + 1} · {(m != null ? m.name : Loc.T("(empty)"))}"))).ToArray();
                int chosen = DrawAdvancedCopyChoice("copy.sourceSlot", new GUIContent(Loc.T("Source material")), index, names);
                if (chosen != index)
                {
                    ChangeAdvancedCopyChoice("Choose source material", () => _advancedCopySourceSlot = chosen);
                    index = chosen;
                }
            }
            var source = materials[index];
            if (materials.Length == 1 && source != null) EditorGUILayout.LabelField(Loc.T("Source material"), source.name);
            if (source == _material)
            {
                GUILayout.Label(Loc.T("Choose a different source material."), StudioGUI.Hint);
                return;
            }
            if (!MaterialSettingsMatch.Compatible(_material, source, out var why))
            {
                StudioGUI.CheckRow(StudioGUI.Check.Note, why);
                return;
            }

            if (QuickRecolourBusy)
            {
                GUILayout.Label(Loc.T("Open Recolour and keep or reset the current changes before copying."), StudioGUI.Hint);
                if (GUILayout.Button(Loc.T("Go to Recolour"), EditorStyles.miniButton)) SetTab(Tab.Colour);
                return;
            }
            if (MaterialWriter.IsPoiyomiLocked(_material))
            {
                GUILayout.Label(Loc.T("Unlock the destination material in the Inspector before copying settings."), StudioGUI.Hint);
                DrawOpenQuickMaterialInspector(_material);
                return;
            }

            var differences = AdvancedMaterialDifferences(source);
            if (differences.Count == 0)
            {
                GUILayout.Label(Loc.T("These materials have no differences in settings Avistry can copy."), StudioGUI.Hint);
                return;
            }
            GUILayout.Label(Loc.T("Settings that differ"), StudioGUI.CheckLabel);
            DrawCopyMaterialGroup(CopyMaterialGroup.Lighting, Loc.T("Shadows and lighting"), differences);
            DrawCopyMaterialGroup(CopyMaterialGroup.Rim, Loc.T("Rim light and shine"), differences);
            DrawCopyMaterialGroup(CopyMaterialGroup.Outline, Loc.T("Outline colour"), differences);
            DrawCopyMaterialGroup(CopyMaterialGroup.Glow, Loc.T("Glow"), differences);
            int selectedCount = differences.Count(IsSelectedCopyChange);
            if (selectedCount == 0)
            {
                GUILayout.Label(Loc.T("Choose a group with differences to copy."), StudioGUI.Hint);
                return;
            }

            _showAdvancedCopyPlan = StudioGUI.Foldout(_showAdvancedCopyPlan,
                new GUIContent(Loc.F("Review {0} settings", selectedCount)));
            StudioGUI.Count("copy.materialReview", GUILayoutUtility.GetLastRect());
            GUILayout.Label(Loc.T("The picked slot gets a new material with its existing textures and render settings. Ctrl+Z restores the previous material."), StudioGUI.Hint);
            using (new EditorGUI.DisabledScope(_needsRebuild))
            {
                var label = new GUIContent(Loc.T("Create material copy with settings"));
                s_copyMaterialCreateStyle ??= new GUIStyle(GUI.skin.button) { wordWrap = true, clipping = TextClipping.Clip };
                float height = Mathf.Max(24f, s_copyMaterialCreateStyle.CalcHeight(label, PanelContentWidth));
                bool create = GUILayout.Button(label, s_copyMaterialCreateStyle, GUILayout.Height(height), GUILayout.MinWidth(0));
                StudioGUI.Count("copy.materialCreate", GUILayoutUtility.GetLastRect());
                if (create) QueueAdvancedMaterialCopy(part, index, source, PlanAdvancedMaterialCopy(source));
            }
            if (_showAdvancedCopyPlan)
            {
                GUILayout.Label(Loc.T("Current value → source value"), StudioGUI.CheckLabel);
                foreach (var change in differences)
                    if (IsSelectedCopyChange(change)) GUILayout.Label(MaterialSettingsMatch.Describe(change), StudioGUI.Hint);
            }
        }

        GenericMenu BuildAdvancedMaterialSourceMenu()
        {
            var menu = new GenericMenu();
            var part = _renderer;
            int materialIndex = _materialIndex;
            var destination = _material;
            var shader = destination.shader;
            var root = TargetResolver.AvatarRoot(part);
            if (root != null)
                foreach (var sourcePart in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(sourcePart is MeshRenderer || sourcePart is SkinnedMeshRenderer) || TargetResolver.AvatarRoot(sourcePart) != root) continue;
                    var materials = sourcePart.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i];
                        if (source == destination || !MaterialSettingsMatch.Compatible(destination, source, out _)) continue;
                        int slot = i;
                        string path = sourcePart.transform == root ? root.name : AnimationUtility.CalculateTransformPath(sourcePart.transform, root);
                        string label = Loc.F("{0} · Slot {1} · {2}", path, slot + 1, source.name);
                        menu.AddItem(new GUIContent(NoSubmenu(label)), _advancedCopySourcePart == sourcePart && _advancedCopySourceSlot == slot, () =>
                        {
                            if (this == null) return;
                            if (_tab != Tab.Target || !Advanced || !_foldAdvancedCopy || _advancedCopyLayers ||
                                !QuickTargetStillMatches(part, materialIndex, destination, shader) || sourcePart == null ||
                                TargetResolver.AvatarRoot(part) != root || TargetResolver.AvatarRoot(sourcePart) != root ||
                                !SlotUsesMaterial(sourcePart, slot, source) || !MaterialSettingsMatch.Compatible(destination, source, out _))
                            {
                                ShowAdvancedCopyChanged();
                                return;
                            }
                            SetAdvancedMaterialSource(sourcePart, slot);
                        });
                    }
                }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent(Loc.T("No compatible material slots on this avatar.")));
            return menu;
        }

        void SetAdvancedMaterialSource(Renderer part, int slot)
        {
            if (_advancedCopySourcePart == part && _advancedCopySourceSlot == slot) return;
            WithTargetUndo("Choose material copy source", () =>
            {
                Undo.RecordObject(this, "Choose material copy source");
                _advancedCopySourcePart = part;
                _advancedCopySourceSlot = slot;
            });
            Repaint();
        }

        void DrawCopyMaterialGroup(CopyMaterialGroup group, string label, List<MaterialSettingsMatch.Change> differences)
        {
            bool selected = (_advancedCopyGroups & group) != 0;
            int count = differences.Count(c => CopyGroup(c.group) == group);
            bool next = EditorGUILayout.ToggleLeft(Loc.F("{0} ({1})", label, count), selected);
            if (next == selected) return;
            Undo.RecordObject(this, "Choose material settings to copy");
            _advancedCopyGroups = next ? _advancedCopyGroups | group : _advancedCopyGroups & ~group;
        }

        static CopyMaterialGroup CopyGroup(MaterialSettingsMatch.Group group) => group switch
        {
            MaterialSettingsMatch.Group.Lighting => CopyMaterialGroup.Lighting,
            MaterialSettingsMatch.Group.RimShine => CopyMaterialGroup.Rim,
            MaterialSettingsMatch.Group.Outline => CopyMaterialGroup.Outline,
            MaterialSettingsMatch.Group.Emission => CopyMaterialGroup.Glow,
            _ => 0,
        };

        bool IsSelectedCopyChange(MaterialSettingsMatch.Change change) => (_advancedCopyGroups & CopyGroup(change.group)) != 0;

        List<MaterialSettingsMatch.Change> PlanAdvancedMaterialCopy(Material source) =>
            AdvancedMaterialDifferences(source).FindAll(IsSelectedCopyChange);

        List<MaterialSettingsMatch.Change> AdvancedMaterialDifferences(Material source)
        {
            var key = (source, _material, source.ComputeCRC(), _material.ComputeCRC());
            if (_copyPlan == null || _copyPlanKey != key)
            {
                _copyPlanKey = key;
                _copyPlan = MaterialSettingsMatch.Plan(_material, source, true, true, true, true);
            }
            return _copyPlan;
        }

        void QueueAdvancedMaterialCopy(Renderer sourcePart, int sourceSlot, Material source, List<MaterialSettingsMatch.Change> plan)
        {
            var destinationPart = _renderer;
            var destinationSlot = _materialIndex;
            var destination = _material;
            var shader = destination.shader;
            var sourceShader = source.shader;
            var sourceCrc = source.ComputeCRC();
            var destinationCrc = destination.ComputeCRC();
            var groups = _advancedCopyGroups;
            bool recordLearning = !Learn.Suppressed;

            bool StillCurrent() => this != null && _tab == Tab.Target && Advanced && !_advancedCopyLayers && !QuickRecolourBusy &&
                _advancedCopySourcePart == sourcePart && _advancedCopyGroups == groups &&
                QuickTargetStillMatches(destinationPart, destinationSlot, destination, shader) &&
                destination.ComputeCRC() == destinationCrc && !MaterialWriter.IsPoiyomiLocked(destination) &&
                sourcePart != null && source != null && source.shader == sourceShader && source.ComputeCRC() == sourceCrc &&
                SlotUsesMaterial(sourcePart, sourceSlot, source) &&
                Mathf.Clamp(_advancedCopySourceSlot, 0, sourcePart.sharedMaterials.Length - 1) == sourceSlot;

            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (!StillCurrent()) { ShowAdvancedCopyChanged(); return; }
                EndStroke();
                var users = new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = destinationPart, materialIndex = destinationSlot } };
                string path = Exporter.CopyMaterial(destination, users, StillCurrent, copy => MaterialSettingsMatch.PrepareCopy(copy, plan));
                if (path == null) { ShowAdvancedCopyChanged(); return; }
                _needsRebuild = true;
                if (recordLearning) Learn.Did(LearnEvents.MaterialCopied);
                Toast(Loc.T("Copied the settings into a new material on the picked part. Ctrl+Z restores the previous material."), MessageType.Info);
                Repaint();
                SceneView.RepaintAll();
            };
        }

        static bool SlotUsesMaterial(Renderer part, int index, Material material)
        {
            var materials = part.sharedMaterials;
            return index >= 0 && index < materials.Length && materials[index] == material;
        }

        void ShowAdvancedCopyChanged()
        {
            if (_renderer != null && _material != null && !SlotUsesMaterial(_renderer, _materialIndex, _material)) _needsRebuild = true;
            Toast(Loc.T("The copy source or destination changed. Review the settings and try again."), MessageType.Info);
        }

        void DrawAdvancedLayerCopy()
        {
            if (!TabAvailable(Tab.Paint, out var why))
            {
                GUILayout.Label(why, StudioGUI.Hint);
                return;
            }
            var layers = _recipe?.layers.Where(l => l != null).Reverse().ToList();
            if (_comp == null || layers == null || layers.Count == 0)
            {
                GUILayout.Label(Loc.T("Add a layer in Paint to copy or receive its settings."), StudioGUI.Hint);
                if (TabAvailable(Tab.Paint, out _) && GUILayout.Button(Loc.T("Go to Paint"), EditorStyles.miniButton)) SetTab(Tab.Paint);
                return;
            }
            GUILayout.Label(Loc.F("Picked part: {0} · {1}", _renderer.name, CurrentSlot?.texture?.name ?? ""), StudioGUI.CheckLabel);
            int index = layers.FindIndex(l => l.id == _advancedCopyLayerId);
            if (index < 0) index = Mathf.Max(0, layers.IndexOf(ActiveLayer));
            var labels = layers.Select(l => new GUIContent(NoSubmenu($"{l.name} · {CopyLayerTypeLabel(l)}"))).ToArray();
            int chosen = DrawAdvancedCopyChoice("copy.layer", new GUIContent(Loc.T("Layer")), index, labels);
            if (chosen != index)
                ChangeAdvancedCopyChoice("Choose layer to copy settings", () => _advancedCopyLayerId = layers[chosen].id);
            var layer = layers[chosen];
            using (new EditorGUI.DisabledScope(_needsRebuild))
                if (GUILayout.Button(Loc.T("Copy this layer's settings"), EditorStyles.miniButton)) CopyLayerSettings(layer.id);

            string json = EditorPrefs.GetString(LayerClipJsonPref, "");
            string typeName = EditorPrefs.GetString(LayerClipTypePref, "");
            var type = typeof(Layer).Assembly.GetType(typeof(Layer).Namespace + "." + typeName);
            var copied = ReadCopiedLayer(json, type);
            if (copied == null) return;
            string source = json == _advancedLayerSource.json && !string.IsNullOrEmpty(_advancedLayerSource.part)
                ? $"{_advancedLayerSource.part} · {copied.name}" : copied.name;
            GUILayout.Label(Loc.F("Copied from: {0}", source), StudioGUI.CheckLabel);
            GUILayout.Label(Loc.F("Paste into: {0}", layer.name), StudioGUI.CheckLabel);
            if (copied.GetType() != layer.GetType())
            {
                GUILayout.Label(Loc.F("Choose a {0} layer on the picked part to paste these settings.", CopyLayerTypeLabel(copied)), StudioGUI.Hint);
                return;
            }
            bool typeSettings = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Layer type settings"),
                Loc.T("Values such as saturation or gradient colours.")), _advancedCopyLayerValues);
            bool appearance = EditorGUILayout.ToggleLeft(new GUIContent(Loc.T("Layer opacity and blend mode"),
                Loc.T("Includes any additional blends on the copied layer.")), _advancedCopyLayerAppearance);
            if (typeSettings != _advancedCopyLayerValues || appearance != _advancedCopyLayerAppearance)
            {
                Undo.RecordObject(this, "Choose layer settings to copy");
                _advancedCopyLayerValues = typeSettings;
                _advancedCopyLayerAppearance = appearance;
            }
            using (new EditorGUI.DisabledScope(_needsRebuild || (!typeSettings && !appearance)))
                if (GUILayout.Button(Loc.T("Paste chosen settings"), GUILayout.Height(24)))
                    ApplyCopiedLayerSettings(layer, copied, typeSettings, appearance);
        }

        static string CopyLayerTypeLabel(Layer layer) => Loc.T(layer.TypeName);
    }
}
