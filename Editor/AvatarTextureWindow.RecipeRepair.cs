namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        int RepairRecipe(string where)
        {
            if (_recipe == null) return 0;
            int n = TextureRecipe.DropNullLayers(_recipe.layers, where);
            if (n > 0) _dirty = true;
            return n;
        }
    }
}
