using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal string AutoTab => _tab.ToString();
        internal bool AutoUnsaved => _unsaved;
        internal TextureSlot AutoCurrentSlot => CurrentSlot;
        internal bool AutoMainSlotEdited => MainSlotEdited;
        internal int AutoPaintTool => PaintToolIndex;
        internal OutputMode AutoOutputMode => _outputMode;
        internal (string png, string material, string recipe) AutoLastSave => (_lastBakePng, _lastBakeMaterial, _lastBakeRecipe);

        internal string AutoLastAoPath => _lastAo.texture != null ? _lastAo.path : null;

        internal (Color colour, float size, float hardness, float opacity, bool eraser) AutoBrushInfo =>
            (_brush.color, _brush.size, _brush.shape.hardness, _brush.opacity, _brush.mode == BrushMode.Erase);

        internal string AutoBusyReason()
        {
            if (_saving) return "A save is running.";
            if (_liveStroke != null || _gradDrag != null || _sceneGradientDraft != null) return "The person is painting.";
            if (_imgDrag != null) return "The person is placing an image.";
            return null;
        }

        internal Transform AutoAvatarRoot()
        {
            if (_renderer != null) return TargetResolver.AvatarRoot(_renderer);
            var sel = Selection.activeGameObject;
            if (sel == null) return null;
            var r = ResolveRenderer(sel);
            return r != null ? TargetResolver.AvatarRoot(r) : sel.transform.root;
        }

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

        internal string AutoAgentSandbox(bool on)
        {
            AgentAccess.Sandbox = on;
            AgentApi.Reset();
            return "sandbox=" + (on ? "on" : "off");
        }

        internal string AutoAgentAnswer(bool allow)
        {
            bool answered = AgentApi.Answer(allow, false, false, Learn.Sandbox);
            return answered ? (allow ? "answered=allow" : "answered=deny") : "no open request";
        }
    }
}
