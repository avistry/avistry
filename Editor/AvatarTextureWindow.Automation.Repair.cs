namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal int AutoNullLayerCount()
        {
            int n = 0;
            if (_recipe != null) foreach (var l in _recipe.layers) if (l == null) n++;
            return n;
        }

        internal int AutoRepairRecipe() => RepairRecipe("test");
    }
}
