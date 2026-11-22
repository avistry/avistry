using System.Collections.Generic;

namespace Avistry.Textures
{
    /// <summary>
    /// Ids of the things a person can do in the tool. A glossary entry lists the ones that complete it in <c>doneBy</c>,
    /// so renaming a concept never touches C#.
    /// </summary>
    internal static class LearnEvents
    {
        internal const string TargetPicked = "target.picked";
        internal const string TargetPoiyomi = "target.poiyomi";
        internal const string TargetLilToon = "target.liltoon";
        internal const string MaterialSwitched = "material.switched";
        internal const string TexturePicked = "texture.picked";
        internal const string SceneClicked = "scene.clicked";
        internal const string AssetPinged = "asset.pinged";
        internal const string EditSaved = "edit.saved";
        internal const string EditReopened = "edit.reopened";
        internal const string UndoUsed = "undo.used";
        internal const string ModeChanged = "mode.changed";
        internal const string RecolourHue = "recolour.hue";
        internal const string RecolourCover = "recolour.cover";
        internal const string ColourUnlocked = "colour.unlocked";
        internal const string PiecesSelected = "pieces.selected";
        internal const string SeamSmoothStroke = "seam.smoothStroke";
        internal const string LayerStroke = "layer.stroke";
        internal const string LayerErase = "layer.erase";
        internal const string LayerAdded = "layer.added";
        internal const string LayerAddedGradientMap = "layer.added.GradientMapLayer";
        internal const string LayerOpacity = "layer.opacity";
        internal const string LayerBlend = "layer.blend";
        internal const string LayerChannels = "layer.channels";
        internal const string LayerClip = "layer.clip";
        internal const string MaskMade = "mask.made";
        internal const string SaveDone = "save.done";
        internal const string AuditRun = "audit.run";
        internal const string ImportFixed = "import.fixed";
        internal const string MapsAo = "maps.ao";
        internal const string MapsNormal = "maps.normal";
        internal const string MapsFaceShadow = "maps.faceShadow";
        internal const string WindowHierarchy = "window.hierarchy";
        internal const string WindowScene = "window.scene";
        internal const string WindowInspector = "window.inspector";
        internal const string WindowProject = "window.project";
        internal const string AgentApproved = "agent.approved";

        /// <summary>Every id. The glossary validator checks that each one is used by at least one entry.</summary>
        internal static readonly string[] All =
        {
            TargetPicked, TargetPoiyomi, TargetLilToon, MaterialSwitched, TexturePicked, SceneClicked, AssetPinged,
            EditSaved, EditReopened, UndoUsed, ModeChanged, RecolourHue, RecolourCover, ColourUnlocked, PiecesSelected,
            SeamSmoothStroke, LayerStroke, LayerErase, LayerAdded, LayerAddedGradientMap, LayerOpacity, LayerBlend,
            LayerChannels, LayerClip, MaskMade, SaveDone, AuditRun, ImportFixed, MapsAo, MapsNormal, MapsFaceShadow,
            WindowHierarchy, WindowScene, WindowInspector, WindowProject, AgentApproved,
        };

        static readonly HashSet<string> s_all = new HashSet<string>(All);
        static readonly Dictionary<string, string> s_layerAdded = new Dictionary<string, string>();

        internal static bool Has(string id) => id != null && s_all.Contains(id);

        /// <summary>"layer.added.&lt;Kind&gt;" for a layer class name. Built once per kind, so calling it again does not allocate.</summary>
        internal static string LayerAddedOf(string layerTypeName)
        {
            if (!s_layerAdded.TryGetValue(layerTypeName, out var id)) s_layerAdded[layerTypeName] = id = LayerAdded + "." + layerTypeName;
            return id;
        }
    }
}
