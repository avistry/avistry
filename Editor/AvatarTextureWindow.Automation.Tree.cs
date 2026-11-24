using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal string AutoSkillTree()
        {
            var next = new List<string>(3);
            Learn.NextUp(next, 3);
            int bonus = 0;
            foreach (var e in Glossary.All) if (e.bonus) bonus++;
            int medals = Learn.HasGraduateMedal ? 1 : 0;
            for (int t = 1; t <= Glossary.TierCount; t++) if (Learn.TierComplete(t)) medals++;
            return "nodes=" + Glossary.CountedCount + " bonus=" + bonus + " next=" + next.Count + " medals=" + medals;
        }

        internal string AutoGlossaryView(string view, int width)
        {
            var v = string.Equals(view, "az", System.StringComparison.OrdinalIgnoreCase) ? GlossaryWindow.View.AZ : GlossaryWindow.View.Tree;
            var w = GlossaryWindow.OpenSized(v, width, 560);
            DrawNow(w);
            return w.Describe();
        }

        internal string AutoGlossarySnap(string path)
        {
            var w = GlossaryWindow.Instance;
            if (w == null) return "The glossary is not open: call AutoGlossaryView first.";
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            w.Focus();
            DrawNow(w);
            DrawNow(w);
            float ppp = EditorGUIUtility.pixelsPerPoint;
            var pos = w.position;
            int pw = (int)(pos.width * ppp), ph = (int)(pos.height * ppp);
            var host = typeof(EditorWindow).GetField("m_Parent", BF)?.GetValue(w);
            var grab = host?.GetType().GetMethod("GrabPixels", BF);
            if (grab == null) return "This Unity version cannot grab the window.";
            var rt = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                grab.Invoke(host, new object[] { rt, new Rect(0, 0, pw, ph) });
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
                var px = tex.GetPixels32();
                var flipped = new Color32[px.Length];
                for (int y = 0; y < ph; y++) System.Array.Copy(px, y * pw, flipped, (ph - 1 - y) * pw, pw);
                tex.SetPixels32(flipped);
                tex.Apply();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            return "saved=" + path;
        }

        static void DrawNow(EditorWindow w)
        {
            typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(w, null);
        }
    }
}
