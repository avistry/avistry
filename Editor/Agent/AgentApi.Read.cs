using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static partial class AgentApi
    {
        const int MaxParts = 500;
        const int PieceSampleSize = 128;
        const int MaxGlossaryHits = 8;

        static AgentResult ReadAction(string action, AgentArgs a)
        {
            switch (action)
            {
                case "status": return ReadStatus(a);
                case "glossary": return ReadGlossary(a);
                case "parts": case "textures": case "layers": case "pieces": case "history": case "last_save": break;
                default: return null;
            }
            var w = Window();
            if (w == null) return NoWindow(action);
            switch (action)
            {
                case "parts": return ReadParts(w, a);
                case "textures": return ReadTextures(w, a);
                case "layers": return ReadLayers(w, a);
                case "pieces": return ReadPieces(w, a);
                case "history": return ReadHistory(w, a);
                default: return ReadLastSave(w, a);
            }
        }

        static Dictionary<string, object> TargetData(AvatarTextureWindow w)
        {
            var r = w.AutoRenderer;
            if (r == null) return null;
            var slot = w.AutoCurrentSlot;
            return D(
                ("part", r.name),
                ("part_id", PartId(r)),
                ("material_slot", w.AutoMaterialIndex),
                ("material", w.AutoMaterial != null ? w.AutoMaterial.name : null),
                ("shader_family", FamilyWord(w.AutoMaterial)),
                ("texture", slot != null && slot.texture != null ? slot.texture.name : null),
                ("texture_role", slot != null ? RoleWord(slot.role) : null));
        }

        static AgentResult ReadStatus(AgentArgs a)
        {
            var w = Window();
            var open = AgentAccess.OpenRequest;
            var d = D(
                ("window_open", w != null),
                ("access", AccessWord()),
                ("mode", w == null ? null : w.Advanced ? "advanced" : "simple"),
                ("tab", w?.AutoTab),
                ("target", w != null ? TargetData(w) : null),
                ("layer_count", w?.AutoRecipe != null ? w.AutoRecipe.layers.Count : 0),
                ("active_layer_id", w?.AutoActiveLayer?.id),
                ("selection_count", w != null ? w.AutoSelection.Count : 0),
                ("unsaved", w != null && w.AutoUnsaved),
                ("history_pos", w != null ? w.AutoHistoryPos : 0),
                ("last_toast", w?.AutoLastToast()),
                ("open_request", open == null ? null : D(("kind", open.kind), ("state", open.state.ToString().ToLowerInvariant()))));
            return AgentResult.Ok(a.action, d);
        }

        static AgentResult ReadParts(AvatarTextureWindow w, AgentArgs a)
        {
            var root = w.AutoAvatarRoot();
            if (root == null) return Fail(a.action, AgentResult.Codes.NoTarget, "No avatar is chosen yet.", "Ask the person to click the avatar in the Hierarchy.");
            var list = new List<object>();
            foreach (var r in PartsUnder(root))
            {
                if (list.Count >= MaxParts) break;
                var slots = new List<object>();
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var textures = new List<object>();
                    foreach (var s in TargetResolver.GetTextureSlots(mats[i]))
                        textures.Add(D(("name", s.texture.name), ("role", RoleWord(s.role)), ("asset", s.assetPath)));
                    slots.Add(D(("index", i), ("material", mats[i].name), ("shader_family", FamilyWord(mats[i])), ("textures", textures)));
                }
                list.Add(D(("part_id", PartId(r)), ("part", r.name), ("path", HierarchyPath(r, root)), ("slots", slots)));
            }
            return AgentResult.Ok(a.action, list);
        }

        static AgentResult ReadTextures(AvatarTextureWindow w, AgentArgs a)
        {
            if (w.AutoRenderer == null) return NoTarget(a.action);
            var list = new List<object>();
            var slots = w.AutoSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.texture == null) continue;
                var warnings = new List<object>();
                foreach (var p in s.uvProblems) warnings.Add(p);
                if (s.featureOff) warnings.Add("The shader ignores this texture because its feature is switched off.");
                if (s.mixedRoles) warnings.Add("This texture is used both as a colour and as data.");
                list.Add(D(
                    ("index", i), ("name", s.texture.name), ("property", s.properties.Count > 0 ? s.properties[0] : null),
                    ("role", RoleWord(s.role)), ("asset", s.assetPath), ("width", s.texture.width), ("height", s.texture.height),
                    ("warnings", warnings)));
            }
            return AgentResult.Ok(a.action, list);
        }

        static Dictionary<string, object> LayerData(Layer l, int index)
        {
            var pieces = new List<object>();
            if (l.islands != null) foreach (var i in l.islands) if (i >= 0) pieces.Add(i);
            return D(
                ("id", l.id), ("index", index), ("kind", KindWord(l)), ("name", l.name), ("enabled", l.enabled),
                ("opacity", l.opacity), ("blend", l.blend.ToString()),
                ("mask_mode", MaskWords[(int)l.maskMode]), ("pieces", pieces), ("clip", l.clipToBelow),
                ("fields", FieldValues(l)));
        }

        static AgentResult ReadLayers(AvatarTextureWindow w, AgentArgs a)
        {
            var recipe = w.AutoRecipe;
            var list = new List<object>();
            if (recipe != null)
                for (int i = 0; i < recipe.layers.Count; i++)
                    if (recipe.layers[i] != null) list.Add(LayerData(recipe.layers[i], i));
            return AgentResult.Ok(a.action, list);
        }

        static AgentResult ReadPieces(AvatarTextureWindow w, AgentArgs a)
        {
            var uv = w.AutoMeshUV;
            if (uv == null) return NoTarget(a.action);
            int offset = a.offset ?? 0;
            int max = a.max ?? AgentArgs.DefaultPieces;
            if (offset < 0 || max < 1 || max > AgentArgs.MaxPiecesPerPage)
                return Fail(a.action, AgentResult.Codes.TooLarge, $"Ask for 1 to {AgentArgs.MaxPiecesPerPage} pieces at a time.", "Use offset to go through the rest.");
            int onlyPart = -1;
            if (!string.IsNullOrEmpty(a.part))
            {
                for (int p = 0; p < uv.parts.Count && onlyPart < 0; p++)
                    if (uv.parts[p].renderer != null && string.Equals(uv.parts[p].renderer.name, a.part, StringComparison.OrdinalIgnoreCase)) onlyPart = p;
                if (onlyPart < 0) return Fail(a.action, AgentResult.Codes.NotFound, $"No part called “{a.part}” uses this texture.", "Use avistry_read parts for the names.");
            }
            var slot = w.AutoCurrentSlot;
            var pixels = slot != null && slot.texture != null ? Recolour.SampleTexture(slot.texture, PieceSampleSize, Color.white) : null;
            var info = uv.PieceStats(pixels, PieceSampleSize);
            var list = new List<object>();
            int seen = 0;
            foreach (var p in info)
            {
                if (p.part < 0 || (onlyPart >= 0 && p.part != onlyPart)) continue;
                if (seen++ < offset) continue;
                if (list.Count >= max) break;
                var r = uv.parts[p.part].renderer;
                list.Add(D(
                    ("id", p.id), ("part", r != null ? r.name : null),
                    ("uv_bounds", new List<object> { p.bounds.x, p.bounds.y, p.bounds.width, p.bounds.height }),
                    ("area", p.area), ("mean_colour", Hex(p.meanColour))));
            }
            return AgentResult.Ok(a.action, list);
        }

        static AgentResult ReadHistory(AvatarTextureWindow w, AgentArgs a)
        {
            var names = new List<object>();
            foreach (var n in w.AutoHistoryNames()) names.Add(n);
            return AgentResult.Ok(a.action, D(("pos", w.AutoHistoryPos), ("names", names)));
        }

        static AgentResult ReadLastSave(AvatarTextureWindow w, AgentArgs a)
        {
            var s = w.AutoLastSave;
            object data = s.png == null ? null : D(("png", s.png), ("material", s.material), ("recipe", s.recipe));
            return AgentResult.Ok(a.action, data);
        }

        static Dictionary<string, object> GlossaryData(Glossary.Entry e) => D(
            ("id", e.id), ("title", e.title), ("tier", Glossary.TierOf(e)),
            ("definition", e.definition), ("what", e.what), ("when", e.when),
            ("example", Glossary.ResolveTokens(e.example, false)),
            ("try_it", Glossary.ResolveTokens(e.tryIt, false)),
            ("watch_out", Glossary.ResolveTokens(e.watchOut, false)),
            ("related", (e.related ?? Array.Empty<string>()).Cast<object>().ToList()));

        static bool Mentions(Glossary.Entry e, string q)
        {
            bool Has(string s) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has(e.id) || Has(e.title) || Has(e.definition)) return true;
            foreach (var s in e.aliases ?? Array.Empty<string>()) if (Has(s)) return true;
            foreach (var s in e.alsoCalled ?? Array.Empty<string>()) if (Has(s)) return true;
            return false;
        }

        static AgentResult ReadGlossary(AgentArgs a)
        {
            var list = new List<object>();
            if (!string.IsNullOrEmpty(a.concept))
            {
                var e = Glossary.Has(a.concept) ? Glossary.Get(a.concept) : null;
                if (e == null) return Fail(a.action, AgentResult.Codes.NotFound, $"No glossary entry called “{a.concept}”.", "Try query with a word.");
                list.Add(GlossaryData(e));
            }
            else if (!string.IsNullOrWhiteSpace(a.query))
            {
                string q = a.query.Trim();
                foreach (var e in Glossary.All)
                {
                    if (list.Count >= MaxGlossaryHits) break;
                    if (Mentions(e, q)) list.Add(GlossaryData(e));
                }
            }
            else return Fail(a.action, AgentResult.Codes.InvalidParam, "Give concept or query.", "For example concept \"tint\" or query \"island\".");
            return AgentResult.Ok(a.action, list);
        }
    }
}
