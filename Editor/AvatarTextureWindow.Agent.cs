using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        const float AgentBannerH = 52f, AgentStripH = 20f;

        static GUIStyle s_agentText, s_agentStrip;
        static GUIContent s_allow => new GUIContent(Loc.T("Allow"), Loc.T("Let the helper do this once"));
        static GUIContent s_allowOnce => new GUIContent(Loc.T("Allow once"), Loc.T("Let the helper see one picture"));
        static GUIContent s_allowSession => new GUIContent(Loc.T("Allow until Unity closes"), Loc.T("Let the helper see pictures without asking again, until you close Unity"));
        static GUIContent s_deny => new GUIContent(Loc.T("Deny"), Loc.T("Say no. Nothing is saved or shared"));
        static GUIContent s_what => new GUIContent(Loc.T("What will change?"), Loc.T("Shows exactly what this will do"));
        static GUIContent s_undo => new GUIContent(Loc.T("Undo"), Loc.T("Undo the helper's last step"));
        static GUIContent s_stop => new GUIContent(Loc.T("Stop"), Loc.T("Switch the AI helper off"));

        [System.NonSerialized] GUIContent _agentStripContent;
        [System.NonSerialized] int _agentStripKey = -1;
        [System.NonSerialized] string _agentStripLang;

        [InitializeOnLoadMethod]
        static void WatchAgentAccess()
        {
            AgentAccess.Changed -= RepaintAgentWindows;
            AgentAccess.Changed += RepaintAgentWindows;
        }

        static void RepaintAgentWindows()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<AvatarTextureWindow>()) if (w != null) w.Repaint();
        }

        float AgentBannerHeight
        {
            get
            {
                var r = AgentAccess.OpenRequest;
                return AgentAccess.Current != AgentAccess.Level.Off && r != null && r.state == AgentAccess.RequestState.Waiting ? AgentBannerH : 0f;
            }
        }

        float AgentStripHeight
        {
            get
            {
                if (AgentAccess.Current == AgentAccess.Level.Off) return 0f;
                var log = AgentAccess.Recent;
                return log.Count > 0 && EditorApplication.timeSinceStartup - log[log.Count - 1].time < 600 ? AgentStripH : 0f;
            }
        }

        bool AgentButton(ref float x, float y, float width, GUIContent content)
        {
            bool clicked = GUI.Button(new Rect(x, y, width, 20), content, EditorStyles.miniButton);
            x += width + 4;
            return clicked;
        }

        void DrawAgentBanner(Rect r)
        {
            var req = AgentAccess.OpenRequest;
            if (req == null || req.state != AgentAccess.RequestState.Waiting) return;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.CardTryFill);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), StudioGUI.CardAccent);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }
            s_agentText ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { clipping = TextClipping.Clip };
            s_agentText.normal.textColor = StudioGUI.CardText;
            GUI.Label(new Rect(r.x + 10, r.y + 3, r.width - 16, 26), req.summary, s_agentText);

            float x = r.x + 10, y = r.yMax - 24;
            bool picture = req.kind == "picture";
            if (picture)
            {
                if (AgentButton(ref x, y, 76, s_allowOnce)) AgentApi.Answer(true, false, true);
                if (AgentButton(ref x, y, 132, s_allowSession)) AgentApi.Answer(true, true, true);
                if (AgentButton(ref x, y, 52, s_deny)) AgentApi.Answer(false);
                return;
            }
            if (AgentButton(ref x, y, 56, s_allow)) AgentApi.Answer(true, false, true);
            if (AgentButton(ref x, y, 52, s_deny)) AgentApi.Answer(false);
            if (AgentButton(ref x, y, 112, s_what))
            {
                string text = AgentApi.OpenRequestDetail ?? req.summary;
                EditorApplication.delayCall += () => StudioDialog.Ask(Loc.T("What will change?"), text, Loc.T("OK"));
            }
        }

        static string AgoText(double seconds)
        {
            if (seconds < 5) return Loc.T("just now");
            if (seconds < 60) return Loc.F("{0} s ago", (int)seconds / 5 * 5);
            return Loc.F("{0} min ago", (int)seconds / 60);
        }

        void DrawAgentStrip(Rect r)
        {
            var log = AgentAccess.Recent;
            if (log.Count == 0) return;
            var last = log[log.Count - 1];
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.StatusBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }

            double age = EditorApplication.timeSinceStartup - last.time;
            bool canUndo = AgentApi.CanUndoLast(this);
            int key = (log.Count * 10000 + (age < 60 ? (int)age / 5 : 100 + (int)age / 60)) * 2 + (canUndo ? 1 : 0);
            if (key != _agentStripKey || _agentStripLang != Loc.Current)
            {
                _agentStripKey = key;
                _agentStripLang = Loc.Current;
                _agentStripContent = new GUIContent(Loc.F("AI helper · {0} · {1}", last.text, AgoText(age)),
                    Loc.T("Unity’s Edit menu shows which action Undo will take back."));
            }

            float right = r.xMax - 6;
            var stop = new Rect(right - 40, r.y + 2, 40, 16);
            if (GUI.Button(stop, s_stop, EditorStyles.miniButton))
            {
                AgentAccess.Current = AgentAccess.Level.Off;
                Toast(Loc.T("The AI helper is switched off. You can switch it on again in Help › Connect an AI helper…."), MessageType.Info, 5f);
                return;
            }
            right = stop.x - 4;
            if (canUndo)
            {
                var undo = new Rect(right - 40, r.y + 2, 40, 16);
                if (GUI.Button(undo, s_undo, EditorStyles.miniButton))
                    EditorApplication.delayCall += () => { if (this != null) UndoHelperEdit(); };
                right = undo.x - 4;
            }

            s_agentStrip ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            s_agentStrip.normal.textColor = StudioGUI.CardTextDim;
            GUI.Label(new Rect(r.x + 8, r.y + 2, Mathf.Max(0f, right - r.x - 8), 16), _agentStripContent, s_agentStrip);
        }

        internal void UndoHelperEdit()
        {
            if (!AgentApi.UndoLast(this, "undo_last").ok)
                Toast(Loc.T("Unity’s Edit menu shows which action Undo will take back."));
            Repaint();
        }
    }
}
