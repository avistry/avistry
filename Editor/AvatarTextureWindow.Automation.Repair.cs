namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        /// <summary>Empty entries in the working recipe's layer list (0 when healthy).</summary>
        internal int AutoNullLayerCount()
        {
            int n = 0;
            if (_recipe != null) foreach (var l in _recipe.layers) if (l == null) n++;
            return n;
        }

        /// <summary>Runs the repair the tool does on undo, restore and compose. Returns how many were dropped.</summary>
        internal int AutoRepairRecipe() => RepairRecipe("test");
    }
}
