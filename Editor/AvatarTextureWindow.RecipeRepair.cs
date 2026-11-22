namespace Avistry.Textures
{
    /// <summary>
    /// Keeps empty layers out of the working recipe. Unity can restore a recipe with null entries in its
    /// [SerializeReference] layer list (undo across a re-target or bake, script reload); everything that walks the
    /// list would then throw, and a bake would write the empty entries into the saved edit.
    /// </summary>
    partial class AvatarTextureWindow
    {
        /// <summary>Drops null layers from the working recipe (one warning with the count). Returns how many.</summary>
        int RepairRecipe(string where)
        {
            if (_recipe == null) return 0;
            int n = TextureRecipe.DropNullLayers(_recipe.layers, where);
            if (n > 0) _dirty = true;
            return n;
        }
    }
}
