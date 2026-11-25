using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        void AddLanguageItems(GenericMenu menu)
        {
            string root = Loc.T("Language") + (Loc.English ? "" : " (Language)") + "/";
            string setting = Loc.Setting;
            menu.AddItem(new GUIContent(root + Loc.T("Follow Unity") + " (" + Loc.NameOf(Loc.UnityLanguage) + ")"), setting == "", () => SetLanguage(""));
            menu.AddSeparator(root);
            for (int i = 0; i < Loc.Codes.Length; i++)
            {
                string code = Loc.Codes[i];
                string label = Loc.Names[i] + (Glossary.IsMachineTranslated(code) ? "  " + Loc.T("(machine translation)") : "");
                menu.AddItem(new GUIContent(root + label), setting == code, () => SetLanguage(code));
            }
        }

        void SetLanguage(string code)
        {
            Loc.Setting = code;
            if (_material != null)
            {
                var fresh = TargetResolver.GetTextureSlots(_material);
                if (fresh.Count == _slots.Count)
                    for (int i = 0; i < fresh.Count; i++) _slots[i].displayName = fresh[i].displayName;
            }
            Repaint();
        }
    }
}
