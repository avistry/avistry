namespace Avistry.Textures
{
    // These texts were written and reviewed as one set; keep the wording consistent when changing one.
    internal static class Msg
    {
        internal static string BtnShowFile => Loc.T("Show file");
        internal static string BtnTryAgain => Loc.T("Try again");
        internal static string BtnOpenConsole => Loc.T("Open Console");
        internal static string BtnUndo => Loc.T("Undo");
        internal static string BtnAffectEverything => Loc.T("Affect everything");
        internal static string BtnSet100 => Loc.T("Set to 100%");
        internal static string BtnShowAllParts => Loc.T("Show all parts");
        internal static string BtnUseBrush => Loc.T("Use brush");
        internal static string BtnUseCopy => Loc.T("Use a copy instead");
        internal static string BtnTickAll => Loc.T("Tick all");
        internal static string BtnSwitchOn => Loc.T("Switch it on");
        internal static string BtnSwitchEffectOn => Loc.T("Switch effect on");
        internal static string BtnTurnAllOn => Loc.T("Turn all on");
        internal static string BtnFindColours => Loc.T("Find colours");
        internal static string BtnDone => Loc.T("Done");
        internal static string BtnPickMesh => Loc.T("Pick another part");
        internal static string BtnGoToPick => Loc.T("Go to Pick");
        internal static string BtnMoveLayers => Loc.T("Move layers");
        internal static string BtnStartEmpty => Loc.T("Start empty");
        internal static string BtnCancel => Loc.T("Cancel");
        internal static string BtnUnlock => Loc.T("Unlock");
        internal static string BtnRemoveLimit => Loc.T("Remove limit");
        internal static string BtnSave => Loc.T("Save");
        internal static string BtnReplace => Loc.T("Replace");
        internal static string BtnDelete => Loc.T("Delete");
        internal static string BtnPaintAnywhere => Loc.T("Paint anywhere");
        internal static string BtnOpenHere => Loc.T("Open here anyway");
        internal static string BtnGoBack => Loc.T("Go back to");

        internal static string M1Saved(string fileName, bool imageOnly)
        {
            return imageOnly
                ? Loc.F("Saved {0}.png. Your avatar is unchanged; assign the image to a material to see it.", fileName)
                : Loc.F("Saved {0}.png. Your avatar now uses it.", fileName);
        }

        internal static string M2SaveFailed()
        {
            return Loc.T("Couldn't save. Nothing on your avatar was changed. Most common cause: the folder is read-only or the file is open elsewhere. The Unity Console window has details.");
        }

        internal static string M3BakeConfirm(string fileName)
        {
            return Loc.F("Save your edit? You'll get: a new image ({0}.png, 2048 px), a copy of the material that your avatar will use, and a saved edit so you can continue later. Your original texture is not changed. Ctrl+Z puts the material back.", fileName);
        }

        internal static string M4TargetChange(int layerCount, string textureName, bool unsaved)
        {
            return unsaved
                ? Loc.F("You have {0} layers that are not saved yet on \"{1}\". Move to the new texture with the same layers, or start empty?", layerCount, textureName)
                : Loc.F("You have {0} layers on \"{1}\". Move to the new texture with the same layers, or start empty?", layerCount, textureName);
        }

        internal static string M5SharedMaterial(string materialName, string meshName, int otherMeshes)
        {
            return Loc.F("Heads up: \"{0}\" is shared. Replacing it changes {1}, {2} other parts, and any other avatars or prefabs that use it.", materialName, meshName, otherMeshes);
        }

        internal static string M6ImageOnly()
        {
            return Loc.T("Image only: your avatar won't change. You'll need to assign the new image yourself.");
        }

        internal static string M7OpenSavedEdit(string editFor, string current)
        {
            return Loc.F("This edit belongs to \"{0}\", but you're on \"{1}\", which is already the saved result. Opening it here would apply it twice.", editFor, current);
        }

        internal static string M8ReplaceLayers(string sourceName, int layerCount)
        {
            return Loc.F("Open \"{0}\" and replace your {1} layers that are not saved yet? Ctrl+Z undoes it.", sourceName, layerCount);
        }

        internal static string M9LockedMaterial()
        {
            return Loc.T("This material is \"locked\" (the shader froze its settings to run faster). It must be unlocked before colours can change. Poiyomi relocks it when you upload.");
        }

        internal static string M10Deleted(string layerName)
        {
            return Loc.F("Deleted \"{0}\".", layerName);
        }

        internal static string M11DeleteBrush(string brushName)
        {
            return Loc.F("Delete brush \"{0}\"? You can't get it back.", brushName);
        }

        internal static string M12MaskPainting(string materialName)
        {
            return Loc.F("You're painting the mask of \"{0}\", not colour. Brush shows, Eraser hides (red). Click Done to paint colour again.", materialName);
        }

        internal static string M13EmptySpaceStroke()
        {
            return Loc.T("Nothing happened: you started outside the model (the dim area). Start on the bright parts.");
        }

        internal static string M14EraserNeedsPaint()
        {
            return Loc.T("Erase only works on a layer with brush paint. Pick a Paint layer, or switch to the brush.");
        }

        internal static string M15ZeroOpacity(string layerName)
        {
            return Loc.F("\"{0}\" is fully see-through (0%), so you can't see your paint.", layerName);
        }

        internal static string M16MaskedTo(string layerName, int parts)
        {
            return Loc.F("Nothing shows: \"{0}\" is limited to {1} parts and you painted outside them.", layerName, parts);
        }

        internal static string M17FocusDimming()
        {
            return Loc.T("You painted outside the parts you focused. It's dimmed in the view.");
        }

        internal static string M18IslandHint(int parts)
        {
            return Loc.F("New layers will only affect the {0} parts you selected.", parts);
        }

        internal static string M19SoftShadow(bool cancelled, string message)
        {
            return cancelled
                ? Loc.T("Soft-shadow step cancelled. Nothing changed.")
                : Loc.F("Couldn't make soft shadows: {0}. Pick a colour texture in Pick and try again.", message);
        }

        internal static string M20NormalMap()
        {
            return Loc.T("This is a normal map. It stores surface bumps, not colour, so painting would break the lighting. Choose a colour texture instead.");
        }

        internal static string M21MaskCreated(string materialName)
        {
            return Loc.F("Created a mask picture for \"{0}\". White = effect on, black = off. It's all white right now, so nothing looks different yet; paint black where it should be off.", materialName);
        }

        internal static string M22ChannelsOff()
        {
            return Loc.T("All colour channels are off, so this layer does nothing.");
        }

        internal static string M23FeatureSwitchedOff()
        {
            return Loc.T("Your edits won't show yet: this texture is used by a feature that's switched off on the material.");
        }

        internal static string M24SharedTexture(int otherMaterials)
        {
            return Loc.F("This texture is also used by {0} other materials. Tick the parts you want to change.", otherMaterials);
        }

        internal static string M25SlotStatus(bool noUvMap)
        {
            return noUvMap
                ? Loc.T("This part has no UV map (needed to paint on it). Pick another part.")
                : Loc.T("This slot has no material.");
        }

        internal static string M26ColourMatchAdded()
        {
            return Loc.T("Added \"Copy colours\". Choose the material to copy from, then press Find colours.");
        }

        internal static string M27CopiedSettings(int settings, string source, string target)
        {
            return Loc.F("Copied {0} lighting settings from \"{1}\" to \"{2}\". This changed the real material. Ctrl+Z undoes it.", settings, source, target);
        }
    }
}
