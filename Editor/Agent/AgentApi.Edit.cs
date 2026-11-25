using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static partial class AgentApi
    {
        const int MaxNameChars = 60;

        static AgentResult Done(string action, object data, string changed) => AgentResult.Ok(action, data, null, null, changed);

        static AgentResult EditAction(string action, AgentArgs a)
        {
            switch (action)
            {
                case "open_window": case "set_target": case "set_tab": case "select": case "add_layer": case "set_layer":
                case "move_layer": case "duplicate_layer": case "delete_layer": case "select_layer": case "recolour":
                case "recolour_to": case "tint": case "brush": case "stroke": case "gradient": case "pick_colour":
                case "history_go_to": case "undo_last":
                    break;
                default:
                    return null;
            }
            if (action == "open_window")
            {
                using (Learn.Suppress())
                {
                    AvatarTextureWindow.Open(false);
                    var opened = Window();
                    if (opened == null) return NoWindow(action);
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("AI: Open window");
                    opened.PickSelectionIfEmpty();
                    Undo.FlushUndoRecordObjects();
                    Undo.SetCurrentGroupName("AI: Open window");
                    Undo.CollapseUndoOperations(group);
                    Undo.IncrementCurrentGroup();
                }
                return Done(action, D(), "Opened the window");
            }
            var w = Window();
            if (w == null) return NoWindow(action);
            var busy = BusyCheck(action, w);
            if (busy != null) return busy;
            if (!TakeWriteSlot())
                return Fail(action, AgentResult.Codes.Busy, $"More than {AgentArgs.MaxWritesPerWindow} edits came in within {AgentArgs.WriteWindowSeconds:0} seconds.", "Wait a few seconds, then go on.");
            switch (action)
            {
                case "set_target": return EditSetTarget(w, a);
                case "set_tab": return EditSetTab(w, a);
                case "select": return EditSelect(w, a);
                case "add_layer": return EditAddLayer(w, a);
                case "set_layer": return EditSetLayer(w, a);
                case "move_layer": return EditMoveLayer(w, a);
                case "duplicate_layer": return EditDuplicateLayer(w, a);
                case "delete_layer": return EditDeleteLayer(w, a);
                case "select_layer": return EditSelectLayer(w, a);
                case "recolour": return EditRecolour(w, a, false);
                case "recolour_to": return EditRecolour(w, a, true);
                case "tint": return EditTint(w, a);
                case "brush": return EditBrush(w, a);
                case "stroke": return EditStroke(w, a);
                case "gradient": return EditGradient(w, a);
                case "pick_colour": return EditPickColour(w, a);
                case "history_go_to": return EditHistoryGoTo(w, a);
                default: return UndoLast(w, a.action);
            }
        }

        static AgentResult Invalid(string action, string message, string hint = null) => Fail(action, AgentResult.Codes.InvalidParam, message, hint);

        static string Clip(string s) => s.Length <= MaxNameChars ? s : s.Substring(0, MaxNameChars);

        static bool FindLayer(AvatarTextureWindow w, string id, out Layer layer, out int index)
        {
            layer = null; index = -1;
            var recipe = w.AutoRecipe;
            if (recipe == null || string.IsNullOrEmpty(id)) return false;
            index = recipe.layers.FindIndex(l => l != null && l.id == id);
            if (index >= 0) layer = recipe.layers[index];
            return layer != null;
        }

        static AgentResult LayerNotFound(string action, string id) =>
            Fail(action, AgentResult.Codes.NotFound, string.IsNullOrEmpty(id) ? "Give layer_id." : "No layer has that layer_id.", "Use avistry_read layers for the ids.");

        static bool CheckRange(float? v, float min, float max) => !v.HasValue || (!float.IsNaN(v.Value) && v.Value >= min && v.Value <= max);

        static bool TryIds(AvatarTextureWindow w, int[] ids, string action, out List<int> list, out AgentResult fail)
        {
            list = new List<int>();
            fail = null;
            var uv = w.AutoMeshUV;
            if (uv == null) { fail = NoTarget(action); return false; }
            if (ids == null) { fail = Invalid(action, "Give ids.", "Use avistry_read pieces for the ids."); return false; }
            if (ids.Length > AgentArgs.MaxIds) { fail = Fail(action, AgentResult.Codes.TooLarge, $"At most {AgentArgs.MaxIds} ids at a time.", "Send fewer ids."); return false; }
            foreach (int id in ids)
            {
                if (id < 0 || id >= uv.islandCount) { fail = Fail(action, AgentResult.Codes.NotFound, $"There is no piece {id}. This texture has {uv.islandCount} pieces.", "Use avistry_read pieces for the ids."); return false; }
                if (!list.Contains(id)) list.Add(id);
            }
            return true;
        }

        static bool TryPieces(AvatarTextureWindow w, AgentArgs a, out List<int> list, out AgentResult fail)
        {
            if (a.ids != null) return TryIds(w, a.ids, a.action, out list, out fail);
            list = new List<int>();
            fail = null;
            var uv = w.AutoMeshUV;
            if (uv == null) { fail = NoTarget(a.action); return false; }
            if (string.IsNullOrEmpty(a.part)) { fail = Invalid(a.action, "Give ids or part.", "Use avistry_read pieces for the ids."); return false; }
            var set = new HashSet<int>();
            bool found = false;
            foreach (var p in uv.parts)
            {
                if (p.renderer == null || !string.Equals(p.renderer.name, a.part, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                for (int t = p.triStart; t < p.triStart + p.triCount && t < uv.triIsland.Length; t++) set.Add(uv.triIsland[t]);
            }
            if (!found) { fail = Fail(a.action, AgentResult.Codes.NotFound, $"No part called “{a.part}” uses this texture.", "Use avistry_read parts for the names."); return false; }
            list = set.OrderBy(i => i).ToList();
            return true;
        }

        static AgentResult EditSetTarget(AvatarTextureWindow w, AgentArgs a)
        {
            var root = w.AutoAvatarRoot();
            if (root == null) return Fail(a.action, AgentResult.Codes.NoTarget, "No avatar is chosen yet.", "Ask the person to click the avatar in the Hierarchy.");
            if (a.onLayers != null && a.onLayers != "keep" && a.onLayers != "cancel") return Invalid(a.action, "on_layers is keep or cancel.");
            var parts = PartsUnder(root);
            Renderer r;
            if (!string.IsNullOrEmpty(a.partId))
            {
                r = parts.FirstOrDefault(p => PartId(p) == a.partId);
                if (r == null) return Fail(a.action, AgentResult.Codes.NotFound, "No part under the current avatar has that part_id.", "Use avistry_read parts for the ids.");
            }
            else if (!string.IsNullOrEmpty(a.part))
            {
                var named = parts.Where(p => string.Equals(p.name, a.part, StringComparison.OrdinalIgnoreCase)).ToList();
                if (named.Count == 0) return Fail(a.action, AgentResult.Codes.NotFound, $"No part called “{a.part}” under the current avatar.", "Use avistry_read parts for the names.");
                if (named.Count > 1) return Invalid(a.action, $"More than one part is called “{a.part}”.", "Use part_id from avistry_read parts.");
                r = named[0];
            }
            else r = w.AutoRenderer;
            if (r == null) return Invalid(a.action, "Give part or part_id.", "Use avistry_read parts for the names.");

            int slot = a.materialSlot ?? (r == w.AutoRenderer ? w.AutoMaterialIndex : 0);
            var mats = r.sharedMaterials;
            if (slot < 0 || slot >= mats.Length || mats[slot] == null)
                return Invalid(a.action, $"Material slot {slot} is empty or does not exist on this part.", "Use avistry_read parts for the slots.");

            return InScope(w, "Change target", EditAnswers(a.onLayers), () =>
            {
                Undo.RecordObject(w, "AI: Change target");
                var before = (w.AutoRenderer, w.AutoMaterialIndex);
                string problem = w.AutoTarget(r, slot, -1, a.texture);
                if (problem != null)
                {
                    AgentResult fail;
                    if (problem.StartsWith("Cancelled", StringComparison.Ordinal))
                        fail = Fail(a.action, AgentResult.Codes.InvalidParam, "This texture already has layers, so the target was not changed.", "Pass on_layers keep to carry them over, or ask the person.");
                    else
                    {
                        bool missing = problem.StartsWith("No texture", StringComparison.Ordinal);
                        fail = Fail(a.action, missing ? AgentResult.Codes.NotFound : AgentResult.Codes.Unsupported, problem, missing ? "Use avistry_read parts for the texture names." : null);
                    }
                    if ((w.AutoRenderer, w.AutoMaterialIndex) != before)
                    {
                        fail.partial = true;
                        fail.changed = new[] { $"Changed target to {w.AutoRenderer.name}, then stopped" };
                    }
                    return fail;
                }
                return Done(a.action, D(("target", TargetData(w))), $"Changed target to {r.name}");
            });
        }

        static AgentResult EditSetTab(AvatarTextureWindow w, AgentArgs a)
        {
            string name = Glossary.TryTabs.FirstOrDefault(t => string.Equals(t, a.tab, StringComparison.OrdinalIgnoreCase));
            if (name == null) return Invalid(a.action, "tab is one of: " + string.Join(", ", Glossary.TryTabs) + ".");
            return InScope(w, null, EditAnswers(null), () =>
            {
                w.AutoClearToast();
                w.AutoSetTab(name);
                if (w.AutoTab != name)
                    return Fail(a.action, AgentResult.Codes.Unsupported, w.AutoLastToast() ?? "That tab is not available yet.", "Pick a part and a texture first.");
                return Done(a.action, D(("tab", name)), null);
            });
        }

        static AgentResult EditSelect(AvatarTextureWindow w, AgentArgs a)
        {
            var uv = w.AutoMeshUV;
            if (uv == null) return NoTarget(a.action);
            string mode = (a.mode ?? "").ToLowerInvariant();
            var list = new List<int>();
            AgentResult fail = null;
            switch (mode)
            {
                case "set": case "add": case "remove":
                    if (!TryIds(w, a.ids, a.action, out list, out fail)) return fail;
                    break;
                case "clear": break;
                case "all": list = Enumerable.Range(0, uv.islandCount).ToList(); break;
                case "invert":
                {
                    var now = new HashSet<int>(w.AutoSelection);
                    list = Enumerable.Range(0, uv.islandCount).Where(i => !now.Contains(i)).ToList();
                    break;
                }
                default: return Invalid(a.action, "mode is one of: set, add, remove, clear, invert, all.");
            }
            return InScope(w, "Select pieces", EditAnswers(null), () =>
            {
                w.AutoSelectIslands(list, mode == "add", mode == "remove");
                int n = w.AutoSelection.Count;
                return Done(a.action, D(("selection_count", n)), n == 1 ? "Selected 1 piece" : $"Selected {n} pieces");
            });
        }

        static AgentResult EditAddLayer(AvatarTextureWindow w, AgentArgs a)
        {
            if (w.AutoCompositor == null) return NoTarget(a.action);
            var layer = NewLayer(a.kind);
            if (layer == null) return Invalid(a.action, "kind is one of: " + AddableKindList() + ".");
            if (!string.IsNullOrEmpty(a.name)) layer.name = Clip(a.name);
            return InScope(w, "Add layer", EditAnswers(null), () =>
            {
                w.AutoInsertLayer(layer);
                return Done(a.action, D(("layer_id", layer.id)), $"Added layer “{layer.name}”");
            });
        }

        static AgentResult EditSetLayer(AvatarTextureWindow w, AgentArgs a)
        {
            if (!FindLayer(w, a.layerId, out var layer, out int index)) return LayerNotFound(a.action, a.layerId);
            if (layer is MixedMergeLayer && (a.opacity.HasValue || a.blend != null || a.maskMode != null || a.clip.HasValue || a.ids != null || a.fields?.Count > 0))
                return Invalid(a.action, "A merged layer keeps its rendered settings. Separate it in Avistry to edit its source layers.");
            if (!CheckRange(a.opacity, 0, 1)) return Invalid(a.action, "opacity is a number from 0 to 1.");
            BlendMode blend = default;
            if (a.blend != null && !TryBlend(a.blend, out blend)) return Invalid(a.action, $"“{a.blend}” is not a blend mode.", "Use a name from the Blend menu, for example Multiply or Soft light.");
            MaskMode mask = default;
            if (a.maskMode != null && !TryMaskMode(a.maskMode, out mask)) return Invalid(a.action, "mask_mode is whole_texture or selected_pieces.");
            List<int> ids = null;
            if (a.ids != null && !TryIds(w, a.ids, a.action, out ids, out var failIds)) return failIds;

            var changes = new List<(FieldSpec spec, object value)>();
            if (a.fields != null)
                foreach (var kv in a.fields)
                {
                    var spec = FindField(layer, kv.Key);
                    if (spec == null)
                        return Invalid(a.action, $"“{kv.Key}” can't be set on a {KindWord(layer)} layer.", "Settable fields here: " + FieldWords(layer) + ".");
                    object value = ConvertField(spec, kv.Value, out string problem);
                    if (value == null) return Invalid(a.action, problem);
                    changes.Add((spec, value));
                }
            if (a.name == null && !a.enabled.HasValue && !a.opacity.HasValue && a.blend == null && a.maskMode == null && !a.clip.HasValue && ids == null && changes.Count == 0)
                return Invalid(a.action, "Nothing to change. Give name, enabled, opacity, blend, mask_mode, clip, ids or fields.");

            return InScope(w, "Edit layer", EditAnswers(null), () =>
            {
                Undo.RecordObject(w.AutoRecipe, "AI: Edit layer");
                if (a.name != null) layer.name = Clip(a.name);
                if (a.enabled.HasValue) layer.enabled = a.enabled.Value;
                if (a.opacity.HasValue) layer.opacity = a.opacity.Value;
                if (a.blend != null) layer.blend = blend;
                if (ids != null) { layer.islands = ids; layer.maskMode = MaskMode.SelectedIslands; }
                if (a.maskMode != null) layer.maskMode = mask;
                if (a.clip.HasValue) layer.clipToBelow = a.clip.Value;
                foreach (var (spec, value) in changes)
                {
                    object stored = spec.type == FieldType.Enum ? Enum.ToObject(spec.Info.FieldType, (int)value) : value;
                    if (spec.type == FieldType.Colour) { var c = (Color)value; c.a = ((Color)spec.Info.GetValue(layer)).a; stored = c; }
                    spec.Info.SetValue(layer, stored);
                }
                w.AutoEdited(true, false);
                return Done(a.action, D(("layer", LayerData(layer, index))), $"Changed layer “{layer.name}”");
            });
        }

        static AgentResult EditMoveLayer(AvatarTextureWindow w, AgentArgs a)
        {
            if (!FindLayer(w, a.layerId, out var layer, out int index)) return LayerNotFound(a.action, a.layerId);
            int count = w.AutoRecipe.layers.Count;
            if (!a.toIndex.HasValue || a.toIndex.Value < 0 || a.toIndex.Value >= count)
                return Invalid(a.action, $"to_index is 0 to {count - 1}. 0 is the bottom layer.");
            int to = a.toIndex.Value;
            if (index != to && (layer is MixedMergeLayer || to == 0 && w.AutoRecipe.layers[0] is MixedMergeLayer))
                return Invalid(a.action, "A merged layer stays at the bottom of the stack.");
            return InScope(w, "Move layer", EditAnswers(null), () =>
            {
                for (int i = index; i != to; i += to > index ? 1 : -1) w.AutoMoveLayer(i, to > index ? 1 : -1);
                return Done(a.action, D(("layer_id", layer.id)), $"Moved layer “{layer.name}”");
            });
        }

        static AgentResult EditDuplicateLayer(AvatarTextureWindow w, AgentArgs a)
        {
            if (!FindLayer(w, a.layerId, out var layer, out int index)) return LayerNotFound(a.action, a.layerId);
            if (layer is MixedMergeLayer) return Invalid(a.action, "Separate the merged layer before duplicating its source layers.");
            return InScope(w, "Duplicate layer", EditAnswers(null), () =>
            {
                w.AutoDuplicateLayer(index);
                var copy = w.AutoActiveLayer;
                return Done(a.action, D(("layer_id", copy?.id)), $"Duplicated layer “{layer.name}”");
            });
        }

        static AgentResult EditDeleteLayer(AvatarTextureWindow w, AgentArgs a)
        {
            if (!FindLayer(w, a.layerId, out var layer, out int index)) return LayerNotFound(a.action, a.layerId);
            string name = layer.name;
            return InScope(w, "Delete layer", EditAnswers(null), () =>
            {
                w.AutoRemoveLayer(index);
                return Done(a.action, D(("layer_id", a.layerId)), $"Deleted layer “{name}”");
            });
        }

        static AgentResult EditSelectLayer(AvatarTextureWindow w, AgentArgs a)
        {
            if (!FindLayer(w, a.layerId, out var layer, out _)) return LayerNotFound(a.action, a.layerId);
            return InScope(w, "Select layer", EditAnswers(null), () =>
            {
                Undo.RecordObject(w, "AI: Select layer");
                w.AutoSelectLayer(layer);
                return Done(a.action, D(("layer_id", layer.id)), $"Selected layer “{layer.name}”");
            });
        }

        static AgentResult EditRecolour(AvatarTextureWindow w, AgentArgs a, bool toColour)
        {
            if (w.AutoMeshUV == null || w.AutoCompositor == null) return NoTarget(a.action);
            if (!TryPieces(w, a, out var ids, out var fail)) return fail;
            if (ids.Count == 0) return Invalid(a.action, "No pieces to recolour.");
            if (!w.AutoMainSlotEdited)
                return Fail(a.action, AgentResult.Codes.Unsupported, "Recolouring works on the material's main colour texture, and another texture is open.", "Use set_target with the main colour texture first.");
            Color target = default;
            if (toColour)
            {
                if (!TryColour(a.colour, out target)) return Invalid(a.action, "colour is a colour like #RRGGBB.");
                if (a.swatch.HasValue && a.swatch.Value < 0) return Invalid(a.action, "swatch is 0 or more.");
            }
            else if (!CheckRange(a.hue, -180, 180) || !CheckRange(a.saturation, -1, 1) || !CheckRange(a.brightness, -1, 1))
                return Invalid(a.action, "hue is -180 to 180; saturation and brightness are -1 to 1.");
            if (!string.IsNullOrEmpty(a.name)) a.name = Clip(a.name);

            return InScope(w, "Recolour pieces", EditAnswers(null), () =>
            {
                w.AutoPartMode(true);
                var part = w.AutoPartNew(ids, a.name);
                if (part == null) return Fail(a.action, AgentResult.Codes.Unsupported, "The pieces could not be recoloured.", "Pick a texture first.");
                string problem = toColour
                    ? w.AutoPartShiftTo(target, a.swatch ?? 0, a.matchBrightness ?? true)
                    : w.AutoPartSet(a.hue ?? 0f, a.saturation ?? 0f, a.brightness ?? 0f, null, null);
                if (problem != null) return Fail(a.action, AgentResult.Codes.Unsupported, problem);
                var data = D(("layer_id", part.id));
                if (toColour) data["mode"] = part.IsTint ? "tint" : "shift";
                return Done(a.action, data, ids.Count == 1 ? "Recoloured 1 piece" : $"Recoloured {ids.Count} pieces");
            });
        }

        static AgentResult EditTint(AvatarTextureWindow w, AgentArgs a)
        {
            if (w.AutoMeshUV == null || w.AutoCompositor == null) return NoTarget(a.action);
            Color? tint = null;
            if (a.colour != null)
            {
                if (!TryColour(a.colour, out var c)) return Invalid(a.action, "colour is a colour like #RRGGBB.");
                tint = c;
            }
            if (!CheckRange(a.strength, 0, 1)) return Invalid(a.action, "strength is a number from 0 to 1.");
            if (tint == null && !a.strength.HasValue) return Invalid(a.action, "Give colour or strength.");
            if (w.AutoCurrentPart == null) return Fail(a.action, AgentResult.Codes.NotFound, "There is no colour part to tint yet.", "Use recolour or recolour_to first.");
            return InScope(w, "Tint pieces", EditAnswers(null), () =>
            {
                string problem = w.AutoPartTint("tint", tint, a.strength);
                if (problem != null) return Fail(a.action, AgentResult.Codes.Unsupported, problem);
                return Done(a.action, D(("layer_id", w.AutoCurrentPart?.id)), "Tinted a colour part");
            });
        }

        static Dictionary<string, object> BrushData(AvatarTextureWindow w)
        {
            var b = w.AutoBrushInfo;
            return D(("colour", Hex(b.colour)), ("size", b.size), ("hardness", b.hardness), ("opacity", b.opacity), ("mode", b.eraser ? "eraser" : "brush"));
        }

        static AgentResult EditBrush(AvatarTextureWindow w, AgentArgs a)
        {
            Color? colour = null;
            if (a.colour != null)
            {
                if (!TryColour(a.colour, out var c)) return Invalid(a.action, "colour is a colour like #RRGGBB.");
                colour = c;
            }
            if (!CheckRange(a.size, BrushSettings.MinSize, BrushSettings.MaxSize)) return Invalid(a.action, $"size is {BrushSettings.MinSize:0} to {BrushSettings.MaxSize:0} pixels.");
            if (!CheckRange(a.hardness, 0, 1) || !CheckRange(a.opacity, 0, 1)) return Invalid(a.action, "hardness and opacity are numbers from 0 to 1.");
            if (a.mode != null && a.mode != "brush" && a.mode != "eraser") return Invalid(a.action, "mode is brush or eraser.");
            if (colour == null && !a.size.HasValue && !a.hardness.HasValue && !a.opacity.HasValue && a.mode == null)
                return Invalid(a.action, "Nothing to change. Give colour, size, hardness, opacity or mode.");
            return InScope(w, "Change brush", EditAnswers(null), () =>
            {
                w.AutoBrush(colour, a.size, a.hardness, a.opacity, a.mode);
                return Done(a.action, D(("brush", BrushData(w))), "Changed the brush");
            });
        }

        static AgentResult EditStroke(AvatarTextureWindow w, AgentArgs a)
        {
            if (a.points == null || a.points.Length == 0) return Invalid(a.action, "Give points, a list of [u, v] pairs from 0 to 1.");
            if (a.points.Length > AgentArgs.MaxStrokePoints) return Fail(a.action, AgentResult.Codes.TooLarge, $"At most {AgentArgs.MaxStrokePoints} points in one stroke.", "Split it into several strokes.");
            foreach (var p in a.points)
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)
                    return Invalid(a.action, "Every point is [u, v] with both from 0 to 1.");
            if (!CheckRange(a.pressure, 0, 1)) return Invalid(a.action, "pressure is a number from 0 to 1.");
            if (w.AutoCompositor == null) return NoTarget(a.action);
            float pressure = a.pressure ?? 1f;
            return InScope(w, "Paint stroke", EditAnswers(null), () =>
            {
                if (w.AutoPaintTool >= 2) w.AutoSetPaintTool(0);
                int before = w.AutoStrokeTotal();
                string problem = w.AutoStrokeUV(a.points, pressure);
                if (problem != null) return Fail(a.action, AgentResult.Codes.Unsupported, problem, "Pick a texture and a paint layer first.");
                return Done(a.action, D(("strokes", w.AutoStrokeTotal() - before)), "Painted a stroke");
            });
        }

        static AgentResult EditGradient(AvatarTextureWindow w, AgentArgs a)
        {
            if (!a.gradientFrom.HasValue || !a.gradientTo.HasValue) return Invalid(a.action, "Give from and to, each as [u, v].");
            foreach (var p in new[] { a.gradientFrom.Value, a.gradientTo.Value })
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)
                    return Invalid(a.action, "from and to are [u, v] with both from 0 to 1.");
            if (w.AutoCompositor == null) return NoTarget(a.action);
            return InScope(w, "Draw gradient", EditAnswers(null), () =>
            {
                string problem = w.AutoGradient(a.gradientFrom.Value, a.gradientTo.Value);
                if (problem != null) return Fail(a.action, AgentResult.Codes.Unsupported, problem);
                return Done(a.action, D(("layer_id", w.AutoActiveLayer?.id)), "Drew a gradient");
            });
        }

        static AgentResult EditPickColour(AvatarTextureWindow w, AgentArgs a)
        {
            if (!a.u.HasValue || !a.v.HasValue || !CheckRange(a.u, 0, 1) || !CheckRange(a.v, 0, 1)) return Invalid(a.action, "Give u and v, each from 0 to 1.");
            if (w.AutoCompositor == null) return NoTarget(a.action);
            return InScope(w, "Pick colour", EditAnswers(null), () =>
            {
                Undo.RecordObject(w, "AI: Pick colour");
                w.AutoPick(new Vector2(a.u.Value, a.v.Value));
                return Done(a.action, D(("colour", Hex(w.AutoBrushInfo.colour))), "Picked a colour");
            });
        }

        static AgentResult EditHistoryGoTo(AvatarTextureWindow w, AgentArgs a)
        {
            int count = w.AutoHistoryNames().Count;
            if (!a.index.HasValue || a.index.Value < -1 || a.index.Value >= count)
                return Invalid(a.action, $"index is -1 (before the first step) to {count - 1}.", "Use avistry_read history for the steps.");
            return InScope(w, null, EditAnswers(null), () =>
            {
                w.AutoGoToHistory(a.index.Value);
                AgentAccess.Log("Went to step " + (a.index.Value + 1));
                return Done(a.action, D(("pos", w.AutoHistoryPos)), null);
            });
        }

        delegate void ReadUndoRecords(List<string> applied, List<string> redo);

        static ReadUndoRecords s_readUndoRecords = FindUndoTimeline();
        static readonly List<string> s_appliedUndo = new List<string>();
        static readonly List<string> s_redoUndo = new List<string>();
        static string[] s_helperUndoSnapshot;
        static AvatarTextureWindow s_helperUndoWindow;
        static string s_helperUndoName;
        static double s_checkUndoAfter;
        static bool s_canUndoLatest;

        static ReadUndoRecords FindUndoTimeline()
        {
            try
            {
                var method = typeof(Undo).GetMethod("GetRecords", BindingFlags.Static | BindingFlags.NonPublic, null,
                    new[] { typeof(List<string>), typeof(List<string>) }, null);
                return (ReadUndoRecords)method?.CreateDelegate(typeof(ReadUndoRecords));
            }
            catch (Exception) { return null; }
        }

        static bool ReadUndoTimeline()
        {
            if (s_readUndoRecords == null) return false;
            s_appliedUndo.Clear();
            s_redoUndo.Clear();
            try { s_readUndoRecords(s_appliedUndo, s_redoUndo); }
            catch (Exception)
            {
                s_readUndoRecords = null;
                ClearHelperUndo();
                return false;
            }
            return true;
        }

        static void TrackHelperUndo(AvatarTextureWindow w, string name)
        {
            ClearHelperUndo();
            s_helperUndoWindow = w;
            s_helperUndoName = name;
            s_helperUndoSnapshot = s_appliedUndo.ToArray();
            Undo.undoRedoEvent += OnUndoTimelineChanged;
            Undo.postprocessModifications += OnUndoEditsRecorded;
        }

        static void OnUndoTimelineChanged(in UndoRedoInfo _) => ClearHelperUndo();

        static UndoPropertyModification[] OnUndoEditsRecorded(UndoPropertyModification[] changes)
        {
            if (changes.Length > 0) ClearHelperUndo();
            return changes;
        }

        static void ClearHelperUndo()
        {
            Undo.undoRedoEvent -= OnUndoTimelineChanged;
            Undo.postprocessModifications -= OnUndoEditsRecorded;
            s_helperUndoWindow = null;
            s_helperUndoName = null;
            s_helperUndoSnapshot = null;
            s_checkUndoAfter = 0;
        }

        static bool IsLatestHelperUndo(AvatarTextureWindow w) => w != null && w == s_helperUndoWindow &&
            s_helperUndoSnapshot != null && ReadUndoTimeline() && s_appliedUndo.SequenceEqual(s_helperUndoSnapshot);

        internal static bool CanUndoLast(AvatarTextureWindow w)
        {
            if (w == null || w != s_helperUndoWindow) return false;
            double now = EditorApplication.timeSinceStartup;
            if (now >= s_checkUndoAfter)
            {
                s_canUndoLatest = IsLatestHelperUndo(w);
                s_checkUndoAfter = now + 0.25;
            }
            return s_canUndoLatest;
        }

        internal static AgentResult UndoLast(AvatarTextureWindow w, string action)
        {
            if (s_helperUndoName == null) return Fail(action, AgentResult.Codes.NotFound, "There is no helper step available to undo.");
            var busy = BusyCheck(action, w);
            if (busy != null) return busy;
            Undo.FlushUndoRecordObjects();
            if (!IsLatestHelperUndo(w))
                return Fail(action, AgentResult.Codes.Unsupported, "Unity's latest Undo action could not be verified as the helper's edit.",
                    "Check Unity's Edit menu to see what Undo will take back.");
            string name = s_helperUndoName;
            ClearHelperUndo();
            using (Learn.Suppress())
            {
                Undo.PerformUndo();
                w.AutoSettle();
            }
            return Done(action, D(("undone", name)), "Undid " + name);
        }
    }
}
