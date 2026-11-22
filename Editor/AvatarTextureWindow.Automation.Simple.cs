namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal bool AutoNewToUnityVisible()
        {
            s_newToUnityDismissed = UnityEditor.EditorPrefs.GetBool(StudioMode.NewToUnityKey, false);
            return !s_newToUnityDismissed.Value;
        }

        internal string AutoDismissNewToUnity()
        {
            DismissNewToUnity();
            AutoSettle();
            return null;
        }
    }
}
