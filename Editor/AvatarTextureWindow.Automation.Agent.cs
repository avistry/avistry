using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>Getters and hooks the AI helper facade (Editor/Agent) and the tests use. Nothing here is called by the window itself.</summary>
    partial class AvatarTextureWindow
    {
        internal string AutoTab => _tab.ToString();
        internal bool AutoUnsaved => _unsaved;
        internal TextureSlot AutoCurrentSlot => CurrentSlot;
        internal bool AutoMainSlotEdited => MainSlotEdited;
        internal int AutoPaintTool => PaintToolIndex;
        internal OutputMode AutoOutputMode => _outputMode;
        internal (string png, string material, string recipe) AutoLastSave => (_lastBakePng, _lastBakeMaterial, _lastBakeRecipe);

        /// <summary>The asset path of the last ambient occlusion image, or null when the last bake made none.</summary>
        internal string AutoLastAoPath => _lastAo.texture != null ? _lastAo.path : null;

        internal (Color colour, float size, float hardness, float opacity, bool eraser) AutoBrushInfo =>
            (_brush.color, _brush.size, _brush.shape.hardness, _brush.opacity, _brush.mode == BrushMode.Erase);

        /// <summary>Why the person must not be interrupted right now, or null.</summary>
        internal string AutoBusyReason()
        {
            if (_saving) return "A save is running.";
            if (_liveStroke != null || _gradDrag != null) return "The person is painting.";
            return null;
        }

        /// <summary>The avatar the helper may look at: the current part's, or the one selected in the Hierarchy.</summary>
        internal Transform AutoAvatarRoot()
        {
            if (_renderer != null) return TargetResolver.AvatarRoot(_renderer);
            var sel = Selection.activeGameObject;
            if (sel == null) return null;
            var r = ResolveRenderer(sel);
            return r != null ? TargetResolver.AvatarRoot(r) : sel.transform.root;
        }

        /// <summary>The Paint tab's brush settings, as its controls would set them.</summary>
        internal void AutoBrush(Color? colour, float? size, float? hardness, float? opacity, string mode)
        {
            Undo.RecordObject(this, "AI: Change brush");
            if (colour.HasValue) _brush.color = new Color(colour.Value.r, colour.Value.g, colour.Value.b, 1f);
            if (size.HasValue) _brush.size = Mathf.Clamp(size.Value, BrushSettings.MinSize, BrushSettings.MaxSize);
            if (hardness.HasValue) _brush.shape.hardness = Mathf.Clamp01(hardness.Value);
            if (opacity.HasValue) _brush.opacity = Mathf.Clamp01(opacity.Value);
            if (mode != null) AutoSetPaintTool(mode == "eraser" ? 1 : 0);
            MarkBrushEdited();
        }

        /// <summary>Keeps the AI helper's access level, allowances, open request and activity in memory, starting from Off. Returns "sandbox=on" or "sandbox=off".</summary>
        internal string AutoAgentSandbox(bool on)
        {
            AgentAccess.Sandbox = on;
            AgentApi.Reset();
            return "sandbox=" + (on ? "on" : "off");
        }

        /// <summary>
        /// Answers the open request like the person's click on Allow or Deny. Returns "answered=allow", "answered=deny" or
        /// "no open request". The bonus skill is only marked inside the skill sandbox, so a test never touches real progress.
        /// </summary>
        internal string AutoAgentAnswer(bool allow)
        {
            bool answered = AgentApi.Answer(allow, false, false, Learn.Sandbox);
            return answered ? (allow ? "answered=allow" : "answered=deny") : "no open request";
        }
    }
}
