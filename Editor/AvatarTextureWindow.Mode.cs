namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal bool Advanced => StudioMode.Advanced;
        internal void SetAdvanced(bool on)
        {
            bool changed = StudioMode.Advanced != on;
            StudioMode.Advanced = on;
            if (changed) Learn.Did(LearnEvents.ModeChanged);
            Repaint();
        }
    }
}
