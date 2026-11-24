using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal int AutoVisibleControls(string tab)
        {
            if (!TryAutoTab(tab, out var t)) return -1;
            CountTab(t);
            return StudioGUI.VisibleControls;
        }

        internal List<string> AutoVisibleControlIds(string tab)
        {
            if (!TryAutoTab(tab, out var t)) return null;
            CountTab(t);
            return new List<string>(StudioGUI.VisibleControlIds);
        }

        internal string AutoGlossaryCheck()
        {
            Glossary.Reload();
            var lines = new List<string>();
            int over = 0;
            foreach (var issue in Glossary.Validate())
                if (issue.Contains(" words (max")) { over++; lines.Add(issue); }

            var unresolved = new List<string>();
            bool advanced = StudioMode.Advanced;
            var tab = _tab;
            try
            {
                foreach (var name in System.Enum.GetNames(typeof(Tab)))
                    foreach (var adv in new[] { false, true })
                    {
                        SetAdvanced(adv);
                        var ids = AutoVisibleControlIds(name);
                        if (ids == null) continue;
                        foreach (var id in ids)
                            if (Glossary.Control(id) == null && !unresolved.Contains(id)) unresolved.Add(id);
                    }
            }
            finally
            {
                SetAdvanced(advanced);
                SetTab(tab);
                AutoSettle();
            }
            foreach (var id in unresolved) lines.Add("unresolved control id: " + id);
            return "overLength=" + over + " unresolved=" + unresolved.Count + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : "");
        }

        internal string AutoOpenInfo(string conceptId)
        {
            if (Glossary.Get(conceptId) == null) return "unknown concept";
            var p = position;
            InfoPopup.Show(new Rect(p.x + 40, p.y + 60, 18, 18), conceptId, Learn.Sandbox);
            var size = InfoPopup.SizeOf(conceptId);
            return "w=" + Mathf.RoundToInt(size.x) + " h=" + Mathf.RoundToInt(size.y) + " title=" + StudioGUI.CardTitle.fontSize;
        }

        static bool TryAutoTab(string name, out Tab t) => System.Enum.TryParse(name, true, out t) && System.Enum.IsDefined(typeof(Tab), t);

        void CountTab(Tab t)
        {
            SetTab(t);
            AutoSettle();
            CountDrawnControls();
        }

        static readonly MethodInfo RepaintImmediatelyMethod = typeof(UnityEditor.EditorWindow).GetMethod("RepaintImmediately",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        void CountDrawnControls()
        {
            StudioGUI.BeginCount();
            try
            {
                Focus();
                RepaintImmediatelyMethod?.Invoke(this, null);
            }
            finally { StudioGUI.EndCount(); }
        }
    }
}
