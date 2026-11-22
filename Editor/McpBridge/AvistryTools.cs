using System;
using System.Collections.Generic;
using System.Globalization;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Avistry.Textures.McpBridge
{
    /// <summary>
    /// Looks at the open Avistry Textures window without changing anything. The three tools in this file only turn
    /// MCP for Unity's JSON into <c>AgentArgs</c> and the answer back into JSON. Every rule (access level, Undo,
    /// approval, limits) lives in <c>AgentApi</c>, so this file is the only one an MCP for Unity API change touches.
    /// </summary>
    [McpForUnityTool("avistry_read", Group = "core", Description =
        "Avistry (Unity editor): look at the open texture edit without changing anything. Start with action 'status'. " +
        "In this tool a 'part' is a mesh and a 'piece' is a UV island. Use 'glossary' to explain terms in the tool's own words.")]
    public static class AvistryReadTool
    {
        public static object HandleCommand(JObject @params) => Bridge.Run(Bridge.Kind.Read, @params);

        public class Parameters
        {
            [ToolParameter("One of: status, parts, textures, layers, pieces, history, last_save, glossary.")]
            public string action { get; set; }

            [ToolParameter("pieces: the part's name, from the 'parts' action.", Required = false)]
            public string part { get; set; }

            [ToolParameter("pieces: how many pieces to skip.", Required = false, DefaultValue = "0")]
            public int offset { get; set; }

            [ToolParameter("pieces: how many to return, up to 1024.", Required = false, DefaultValue = "256")]
            public int max { get; set; }

            [ToolParameter("glossary: a concept id, for example 'tint'.", Required = false)]
            public string concept { get; set; }

            [ToolParameter("glossary: words to search for.", Required = false)]
            public string query { get; set; }
        }
    }

    /// <summary>Makes undoable edits in the open window. Each call is one Ctrl+Z step named "AI: ...".</summary>
    [McpForUnityTool("avistry_edit", Group = "core", Description =
        "Avistry: make undoable edits in the open window (target, pieces, recolour, layers, paint). " +
        "Each call is one Ctrl+Z step named 'AI: …'. The person's original files are never changed. " +
        "Saving is not here: use avistry_request.")]
    public static class AvistryEditTool
    {
        public static object HandleCommand(JObject @params) => Bridge.Run(Bridge.Kind.Edit, @params);

        public class Parameters
        {
            [ToolParameter("One of: open_window, set_target, set_tab, select, add_layer, set_layer, move_layer, duplicate_layer, delete_layer, select_layer, recolour, recolour_to, tint, brush, stroke, gradient, pick_colour, history_go_to, undo_last.")]
            public string action { get; set; }

            [ToolParameter("set_target: the part's id from avistry_read parts.", Required = false)]
            public string part_id { get; set; }

            [ToolParameter("set_target, recolour, recolour_to: the part's name.", Required = false)]
            public string part { get; set; }

            [ToolParameter("set_target: which material slot of the part.", Required = false, DefaultValue = "0")]
            public int material_slot { get; set; }

            [ToolParameter("set_target: the texture's name among the material's textures.", Required = false)]
            public string texture { get; set; }

            [ToolParameter("set_target: 'keep' or 'cancel' when the open edit has layers. Default cancel.", Required = false, DefaultValue = "cancel")]
            public string on_layers { get; set; }

            [ToolParameter("set_tab: Target, Colour, Select, Paint or Save.", Required = false)]
            public string tab { get; set; }

            [ToolParameter("select: set, add, remove, clear, invert or all. brush: brush or eraser.", Required = false)]
            public string mode { get; set; }

            [ToolParameter("select, recolour, recolour_to, set_layer: piece ids, at most 4096.", Required = false)]
            public int[] ids { get; set; }

            [ToolParameter("add_layer: paint, hsl, levels, gradient_map, solid_colour, brightness_contrast, colour_balance, posterize, invert, threshold, tone_curve or root_tip_gradient.", Required = false)]
            public string kind { get; set; }

            [ToolParameter("add_layer, set_layer, recolour: a short name for the layer.", Required = false)]
            public string name { get; set; }

            [ToolParameter("set_layer, move_layer, duplicate_layer, delete_layer, select_layer: the layer's id from avistry_read layers.", Required = false)]
            public string layer_id { get; set; }

            [ToolParameter("set_layer: show or hide the layer.", Required = false)]
            public bool enabled { get; set; }

            [ToolParameter("set_layer: 0 to 1.", Required = false)]
            public float opacity { get; set; }

            [ToolParameter("set_layer: blend mode name as in the Blend menu.", Required = false)]
            public string blend { get; set; }

            [ToolParameter("set_layer: whole texture or selected pieces, as in the Layers tab.", Required = false)]
            public string mask_mode { get; set; }

            [ToolParameter("set_layer: clip the layer to the one below.", Required = false)]
            public bool clip { get; set; }

            [ToolParameter("set_layer: a few settings of the layer kind, by name (see 'fields' in avistry_read layers).", Required = false)]
            public object fields { get; set; }

            [ToolParameter("move_layer: the new position in the stack.", Required = false)]
            public int to_index { get; set; }

            [ToolParameter("recolour: hue shift in degrees, -180 to 180.", Required = false)]
            public float hue { get; set; }

            [ToolParameter("recolour: -1 to 1.", Required = false)]
            public float saturation { get; set; }

            [ToolParameter("recolour: -1 to 1.", Required = false)]
            public float brightness { get; set; }

            [ToolParameter("recolour_to, tint, brush: a colour as #RRGGBB.", Required = false)]
            public string colour { get; set; }

            [ToolParameter("recolour_to: which swatch of the colour set to use.", Required = false, DefaultValue = "0")]
            public int swatch { get; set; }

            [ToolParameter("recolour_to: keep each piece's brightness.", Required = false, DefaultValue = "true")]
            public bool match_brightness { get; set; }

            [ToolParameter("tint: 0 to 1.", Required = false)]
            public float strength { get; set; }

            [ToolParameter("brush: size in pixels.", Required = false)]
            public float size { get; set; }

            [ToolParameter("brush: 0 to 1.", Required = false)]
            public float hardness { get; set; }

            [ToolParameter("stroke: UV points as [[u, v], ...] or a flat list, at most 2048 points.", Required = false)]
            public object[] points { get; set; }

            [ToolParameter("stroke: 0 to 1.", Required = false)]
            public float pressure { get; set; }

            [ToolParameter("gradient: start as [u, v].", Required = false)]
            public float[] from_uv { get; set; }

            [ToolParameter("gradient: end as [u, v].", Required = false)]
            public float[] to_uv { get; set; }

            [ToolParameter("pick_colour: u, 0 to 1.", Required = false)]
            public float u { get; set; }

            [ToolParameter("pick_colour: v, 0 to 1.", Required = false)]
            public float v { get; set; }

            [ToolParameter("history_go_to: the step's index from avistry_read history.", Required = false)]
            public int index { get; set; }
        }
    }

    /// <summary>Asks the person to approve something that writes new files or shares a picture, and waits for the answer.</summary>
    [McpForUnityTool("avistry_request", Group = "core", RequiresPolling = true, PollAction = "status", MaxPollSeconds = 300, Description =
        "Avistry: ask the person in Unity to approve something that writes files or shares a picture. " +
        "The call waits until they click Allow or Deny (up to 5 minutes). Saving always makes new files.")]
    public static class AvistryRequestTool
    {
        public static object HandleCommand(JObject @params) => Bridge.Run(Bridge.Kind.Request, @params);

        public class Parameters
        {
            [ToolParameter("One of: save_png, bake_ao, make_normal_map, make_face_shadow, picture, cancel.")]
            public string action { get; set; }

            [ToolParameter("picture: width and height in pixels, 256 to 1024. Pictures up to 512 come back inline.", Required = false, DefaultValue = "512")]
            public int size { get; set; }

            [ToolParameter("make_normal_map, make_face_shadow: also use the new map on the material.", Required = false, DefaultValue = "false")]
            public bool use { get; set; }
        }
    }

    /// <summary>Turns tool calls into <c>AgentArgs</c> and <c>AgentResult</c> back into MCP for Unity responses. No rules here.</summary>
    static class Bridge
    {
        internal enum Kind { Read, Edit, Request }

        sealed class Problem : Exception
        {
            internal Problem(string message) : base(message) { }
        }

        internal static object Run(Kind kind, JObject p)
        {
            string action = null;
            try
            {
                if (p == null) throw new Problem("No parameters were sent.");
                action = Str(p, "action");
                if (string.IsNullOrEmpty(action)) throw new Problem("Give an action.");
                AgentArgs a = Parse(p);
                AgentResult r = kind == Kind.Read ? AgentApi.Read(action, a) : kind == Kind.Edit ? AgentApi.Edit(action, a) : AgentApi.Request(action, a);
                return Wrap(r);
            }
            catch (Problem ex)
            {
                return Wrap(AgentResult.Fail(action, AgentResult.Codes.InvalidParam, ex.Message, "Check the parameter names and types."));
            }
            catch (Exception)
            {
                // Details could name folders on this computer, so the Console gets them and the answer does not.
                Debug.LogWarning("[Avistry] The AI helper bridge could not read a call.");
                return Wrap(AgentResult.Fail(action, AgentResult.Codes.Unsupported, "The call could not be read.", "Check the parameters and try again."));
            }
        }

        internal static object Wrap(AgentResult r)
        {
            var envelope = new Dictionary<string, object> { ["ok"] = r.ok, ["action"] = r.action };
            if (r.ok)
            {
                envelope["changed"] = r.changed ?? Array.Empty<string>();
                envelope["undo"] = r.undo;
                envelope["toast"] = r.toast;
                envelope["data"] = r.data;
                if (r.pending) return new PendingResponse(r.message, 1.0, envelope);
                return new SuccessResponse(string.IsNullOrEmpty(r.message) ? "Done." : r.message, envelope);
            }
            envelope["message"] = r.message;
            envelope["hint"] = r.hint;
            envelope["toast"] = r.toast;
            return new ErrorResponse(r.code, envelope);
        }

        static AgentArgs Parse(JObject p)
        {
            var a = new AgentArgs
            {
                action = Str(p, "action"),
                part = Str(p, "part"),
                partId = Str(p, "part_id"),
                materialSlot = Int(p, "material_slot"),
                texture = Str(p, "texture"),
                onLayers = Str(p, "on_layers"),
                tab = Str(p, "tab"),
                mode = Str(p, "mode"),
                ids = Ids(p, "ids"),
                kind = Str(p, "kind"),
                name = Str(p, "name"),
                layerId = Str(p, "layer_id"),
                enabled = Bool(p, "enabled"),
                opacity = Num(p, "opacity"),
                blend = Str(p, "blend"),
                maskMode = Str(p, "mask_mode"),
                clip = Bool(p, "clip"),
                fields = Fields(p, "fields"),
                toIndex = Int(p, "to_index"),
                hue = Num(p, "hue"),
                saturation = Num(p, "saturation"),
                brightness = Num(p, "brightness"),
                colour = Str(p, "colour"),
                swatch = Int(p, "swatch"),
                matchBrightness = Bool(p, "match_brightness"),
                strength = Num(p, "strength"),
                size = Num(p, "size"),
                hardness = Num(p, "hardness"),
                points = Points(p, "points"),
                pressure = Num(p, "pressure"),
                gradientFrom = Pair(p, "from_uv", "from"),
                gradientTo = Pair(p, "to_uv", "to"),
                u = Num(p, "u"),
                v = Num(p, "v"),
                index = Int(p, "index"),
                offset = Int(p, "offset"),
                max = Int(p, "max"),
                concept = Str(p, "concept"),
                query = Str(p, "query"),
                use = Bool(p, "use"),
            };
            return a;
        }

        static JToken Get(JObject p, string name)
        {
            if (!p.TryGetValue(name, StringComparison.Ordinal, out JToken t) || t == null || t.Type == JTokenType.Null) return null;
            return t;
        }

        static string Str(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (t.Type == JTokenType.String) return (string)t;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.Boolean) return Convert.ToString(((JValue)t).Value, CultureInfo.InvariantCulture);
            throw new Problem($"“{name}” should be text.");
        }

        static bool TryNumber(JToken t, out double n)
        {
            n = 0;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) { n = (double)t; return !double.IsNaN(n) && !double.IsInfinity(n); }
            return t.Type == JTokenType.String && double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n);
        }

        static float? Num(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (!TryNumber(t, out double n)) throw new Problem($"“{name}” should be a number.");
            return (float)n;
        }

        static int? Int(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (!TryNumber(t, out double n) || n != Math.Floor(n) || Math.Abs(n) > int.MaxValue) throw new Problem($"“{name}” should be a whole number.");
            return (int)n;
        }

        static bool? Bool(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.String && bool.TryParse((string)t, out bool b)) return b;
            throw new Problem($"“{name}” should be true or false.");
        }

        static int[] Ids(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (!(t is JArray arr)) throw new Problem($"“{name}” should be a list of whole numbers.");
            var list = new int[arr.Count];
            for (int i = 0; i < list.Length; i++)
            {
                if (!TryNumber(arr[i], out double n) || n != Math.Floor(n) || Math.Abs(n) > int.MaxValue) throw new Problem($"“{name}” should be a list of whole numbers.");
                list[i] = (int)n;
            }
            return list;
        }

        /// <summary>Accepts [[u, v], ...] or the flat [u, v, u, v, ...].</summary>
        static Vector2[] Points(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (!(t is JArray arr)) throw new Problem($"“{name}” should be a list of [u, v] pairs.");
            if (arr.Count > 0 && arr[0] is JArray)
            {
                var pairs = new Vector2[arr.Count];
                for (int i = 0; i < pairs.Length; i++)
                {
                    if (!(arr[i] is JArray pair) || pair.Count != 2 || !TryNumber(pair[0], out double u) || !TryNumber(pair[1], out double v))
                        throw new Problem($"“{name}” should be a list of [u, v] pairs.");
                    pairs[i] = new Vector2((float)u, (float)v);
                }
                return pairs;
            }
            if (arr.Count % 2 != 0) throw new Problem($"“{name}” as a flat list needs an even number of values: u, v, u, v, …");
            var flat = new Vector2[arr.Count / 2];
            for (int i = 0; i < flat.Length; i++)
            {
                if (!TryNumber(arr[2 * i], out double u) || !TryNumber(arr[2 * i + 1], out double v)) throw new Problem($"“{name}” should hold numbers.");
                flat[i] = new Vector2((float)u, (float)v);
            }
            return flat;
        }

        static Vector2? Pair(JObject p, string name, string alias)
        {
            JToken t = Get(p, name) ?? Get(p, alias);
            if (t == null) return null;
            if (!(t is JArray arr) || arr.Count != 2 || !TryNumber(arr[0], out double u) || !TryNumber(arr[1], out double v))
                throw new Problem($"“{name}” should be [u, v].");
            return new Vector2((float)u, (float)v);
        }

        /// <summary>Values stay plain (text, true or false, whole or decimal numbers), so the facade can check each against its whitelist.</summary>
        static Dictionary<string, object> Fields(JObject p, string name)
        {
            JToken t = Get(p, name);
            if (t == null) return null;
            if (!(t is JObject obj)) throw new Problem($"“{name}” should be an object of setting names and values.");
            var d = new Dictionary<string, object>();
            foreach (var kv in obj)
            {
                JToken v = kv.Value;
                switch (v.Type)
                {
                    case JTokenType.String: d[kv.Key] = (string)v; break;
                    case JTokenType.Boolean: d[kv.Key] = (bool)v; break;
                    case JTokenType.Integer: d[kv.Key] = (long)v; break;
                    case JTokenType.Float: d[kv.Key] = (double)v; break;
                    default: throw new Problem($"“{kv.Key}” in “{name}” should be text, a number or true or false.");
                }
            }
            return d;
        }
    }
}
