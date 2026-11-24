using System.Collections.Generic;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal string AutoSetAdvanced(bool on)
        {
            SetAdvanced(on);
            AutoSettle();
            return null;
        }

        internal string AutoSetWindowSize(int w, int h)
        {
            var docked = typeof(UnityEditor.EditorWindow).GetProperty("docked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (docked != null && (bool)docked.GetValue(this)) return "docked";
            position = new Rect(position.x, position.y, w, h);
            AutoSettle();
            return null;
        }

        internal string[] AutoLastToastActions()
        {
            var list = new List<string>();
            if (_lastToastAction != null) list.Add(_lastToastAction);
            if (_lastToastAction2 != null) list.Add(_lastToastAction2);
            return list.ToArray();
        }
    }
}
