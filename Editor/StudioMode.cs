using UnityEditor;

namespace Avistry.Textures
{
    internal static class StudioMode
    {
        public const string PrefKey = "AvatarTextureStudio.Advanced";
        public const string NewToUnityKey = "AvatarTextureStudio.NewToUnityDismissed";
        static bool? s_adv;

        public static bool Advanced
        {
            get => s_adv ??= EditorPrefs.GetBool(PrefKey, false);
            set
            {
                if (Advanced == value) return;
                s_adv = value;
                EditorPrefs.SetBool(PrefKey, value);
                Changed?.Invoke();
            }
        }

        public static bool Simple => !Advanced;
        public static event System.Action Changed;
    }
}
