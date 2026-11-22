using System;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The "AI helper" window: how to connect an AI app through MCP for Unity, what the helper may do (Off by default)
    /// and what it did lately. It only shows state and sets the person's own preferences. It installs nothing: the
    /// only outward actions are opening a web page and copying the install address, each on a click.
    /// </summary>
    internal class AgentWindow : EditorWindow
    {
        internal const string StateNotInstalled = "not_installed", StateUnsupported = "unsupported", StateReady = "ready";

        const string MenuPath = "Window/MCP for Unity/Toggle MCP Window";

        /// <summary>Set by tests to draw a state this computer is not in. Empty means the real state.</summary>
        [NonSerialized] static string s_preview;

        [SerializeField] Vector2 _scroll;
        [NonSerialized] bool _menuMissing, _copied;
        [NonSerialized] string[] _recent;
        [NonSerialized] int _recentKey = -1;

        static GUIStyle s_title, s_body, s_dim, s_step, s_box;
        static string[] s_levels => new[] { Loc.T("Off"), Loc.T("Look only"), Loc.T("Look and edit") };
        static GUIContent s_copy => new GUIContent(Loc.T("Copy install address"), Loc.T("Copies the address to paste into Package Manager"));
        static GUIContent s_pkgMgr => new GUIContent(Loc.T("Package Manager"), Loc.T("Opens Unity's Package Manager"));
        static GUIContent s_page => new GUIContent(Loc.T("Its page"), Loc.T("Opens MCP for Unity's page in your browser"));
        static GUIContent s_openMcp => new GUIContent(Loc.T("Open MCP for Unity"));
        static GUIContent s_stop => new GUIContent(Loc.T("Stop"), Loc.T("Switch the AI helper off now"));
        static GUIContent s_undo => new GUIContent(Loc.T("Undo"), Loc.T("Undo the helper's last step"));
        static GUIContent s_saves => new GUIContent(Loc.T("Allow saves until Unity closes"), Loc.T("Skips the Allow question when the helper saves a new PNG or map. Resets when you close Unity"));
        static GUIContent s_pictures => new GUIContent(Loc.T("Allow pictures until Unity closes"), Loc.T("Lets the helper see a picture of your texture without asking each time. The picture goes to your AI service. Resets when you close Unity"));

        /// <summary>Opens the window (Help menu, "Connect an AI helper…").</summary>
        internal static void Open()
        {
            var w = GetWindow<AgentWindow>(Loc.T("AI helper"));
            w.minSize = new Vector2(380, 420);
            w.Show();
        }

        /// <summary>"not_installed", "unsupported" or "ready", or the state a test asked the window to show.</summary>
        internal static string StateWord
        {
            get
            {
                if (!string.IsNullOrEmpty(s_preview)) return s_preview;
                if (!McpDetect.Installed) return StateNotInstalled;
                return McpDetect.Supported && McpDetect.BridgeLoaded ? StateReady : StateUnsupported;
            }
        }

        /// <summary>For tests: draws a state this computer is not in. Pass "" to go back to the real one.</summary>
        internal static string Preview(string state)
        {
            s_preview = state == StateNotInstalled || state == StateUnsupported || state == StateReady ? state : null;
            foreach (var w in Resources.FindObjectsOfTypeAll<AgentWindow>()) if (w != null) w.Repaint();
            return StateWord;
        }

        void OnEnable()
        {
            titleContent = new GUIContent(Loc.T("AI helper"));
            AgentAccess.Changed += Repaint;
        }

        void OnDisable() => AgentAccess.Changed -= Repaint;

        static Color Tint(Color c, float alpha) { c.a = alpha; return c; }

        static void EnsureStyles()
        {
            if (s_title != null) return;
            s_title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, wordWrap = true, margin = new RectOffset(0, 0, 4, 4) };
            s_body = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12, richText = false };
            s_dim = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            s_step = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, wordWrap = true, margin = new RectOffset(0, 0, 8, 2) };
            s_box = new GUIStyle { padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(0, 0, 6, 6) };
        }

        static void Divider()
        {
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, StudioGUI.CardDivider);
        }

        static void Note(string text, Color tint)
        {
            var r = EditorGUILayout.BeginVertical(s_box);
            if (Event.current.type == EventType.Repaint)
            {
                StudioGUI.DrawRoundedBox(r, Tint(tint, 0.14f), Tint(tint, 0.7f), 4f);
            }
            GUILayout.Label(text, s_body);
            EditorGUILayout.EndVertical();
        }

        void OnGUI()
        {
            EnsureStyles();
            string state = StateWord;
            // No horizontal bar: the content is laid out to fit, and a few stray pixels must not add a bar of their own.
            _scroll = EditorGUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUI.skin.scrollView);
            GUILayout.Space(6);
            var pad = new RectOffset(12, 12, 0, 8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(pad.left);
            GUILayout.BeginVertical();

            GUILayout.Label(Loc.T("Let an AI helper work with you"), s_title);
            GUILayout.Label(Loc.T("Your AI app (Claude Code, Cursor, VS Code and others) can pick parts, select pieces, recolour and add layers. You see every change and Ctrl+Z undoes it. This is optional: everything in Avistry works without it."), s_body);

            DrawInstall(state);
            DrawConnect(state);
            DrawAccess(state);

            GUILayout.Label(Loc.T("Try asking: \"Make the hoodie sleeves pastel pink.\""), s_step);
            GUILayout.Space(6);
            Divider();
            DrawSafety();
            DrawRecent();

            GUILayout.EndVertical();
            GUILayout.Space(pad.right);
            GUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        void DrawInstall(string state)
        {
            string status = state == StateNotInstalled ? Loc.T("Not installed")
                : state == StateUnsupported ? Loc.T("Needs version 10.x")
                : Loc.T("Installed") + (McpDetect.Version != null ? " " + McpDetect.Version : "");
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("1. Install MCP for Unity"), s_step);
            GUILayout.FlexibleSpace();
            var prev = GUI.contentColor;
            GUI.contentColor = state == StateReady ? StudioGUI.CardGood : state == StateUnsupported ? StudioGUI.CardDanger : StudioGUI.CardTextDim;
            GUILayout.Label(status, EditorStyles.miniBoldLabel);
            GUI.contentColor = prev;
            GUILayout.EndHorizontal();

            if (state == StateUnsupported)
                GUILayout.Label(Loc.T("This version of MCP for Unity isn't supported yet (we need 10.x). Everything else works; the helper tools are off."), s_body);
            else
                GUILayout.Label(Loc.T("Free and optional. A separate, open-source package that connects AI apps to Unity. We don't make it, install it or vouch for it. In Package Manager choose + then \"Add package from git URL\", and paste the address."), s_body);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(s_copy)) { EditorGUIUtility.systemCopyBuffer = McpDetect.InstallAddress; _copied = true; }
            if (GUILayout.Button(s_pkgMgr)) EditorApplication.ExecuteMenuItem("Window/Package Manager");
            if (GUILayout.Button(s_page, GUILayout.MinWidth(70))) Application.OpenURL(McpDetect.PageUrl);
            GUILayout.EndHorizontal();
            if (_copied) GUILayout.Label(Loc.T("Copied. Paste it into the Package Manager box."), s_dim);
        }

        void DrawConnect(string state)
        {
            GUILayout.Label(Loc.T("2. Connect your AI app"), s_step);
            GUILayout.Label(Loc.T("Open Window > MCP for Unity and follow its setup. It runs a server on this computer."), s_body);
            using (new EditorGUI.DisabledScope(state == StateNotInstalled))
            {
                if (GUILayout.Button(s_openMcp, GUILayout.Width(150)))
                    _menuMissing = !EditorApplication.ExecuteMenuItem(MenuPath);
            }
            if (_menuMissing) GUILayout.Label(Loc.T("Use Window > MCP for Unity."), s_dim);
        }

        void DrawAccess(string state)
        {
            GUILayout.Label(Loc.T("3. Choose what the helper may do"), s_step);
            bool ready = state == StateReady;
            // Off is always reachable: a level saved earlier must be switchable off even when the bridge is gone.
            GUILayout.BeginHorizontal();
            int level = (int)AgentAccess.Current;
            int picked = level;
            var levels = s_levels;
            for (int i = 0; i < levels.Length; i++)
                using (new EditorGUI.DisabledScope(!ready && i != 0))
                    if (GUILayout.Toggle(level == i, levels[i], i == 0 ? EditorStyles.miniButtonLeft : i == levels.Length - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid, GUILayout.ExpandWidth(true)) && level != i)
                        picked = i;
            if (picked != level) AgentAccess.Current = (AgentAccess.Level)picked;
            using (new EditorGUI.DisabledScope(AgentAccess.Current == AgentAccess.Level.Off))
                if (GUILayout.Button(s_stop, EditorStyles.miniButton, GUILayout.MinWidth(52))) AgentAccess.Current = AgentAccess.Level.Off;
            GUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!ready))
            {
                using (new EditorGUI.DisabledScope(AgentAccess.Current == AgentAccess.Level.Off))
                {
                    bool saves = EditorGUILayout.ToggleLeft(s_saves, AgentAccess.AllowSaves);
                    if (saves != AgentAccess.AllowSaves) AgentAccess.AllowSaves = saves;
                    bool pics = EditorGUILayout.ToggleLeft(s_pictures, AgentAccess.AllowPictures);
                    if (pics != AgentAccess.AllowPictures) AgentAccess.AllowPictures = pics;
                }
            }
            GUILayout.Label(ready
                ? Loc.T("It starts Off. Saving and pictures ask you first, unless ticked above.")
                : Loc.T("The choices turn on once MCP for Unity 10.x is installed."), s_dim);
        }

        static void DrawSafety()
        {
            GUILayout.Label(Loc.T("Safe by design"), s_step);
            GUILayout.Label(Loc.T("Without asking: look, select pieces, recolour, add and change layers (each one Ctrl+Z step)."), s_body);
            GUILayout.Label(Loc.T("Asks you first: save a new PNG, make maps, see a picture of your texture."), s_body);
            GUILayout.Label(Loc.T("Never: overwrite your originals, delete files or run code."), s_body);
            Note(Loc.T("MCP for Unity has its own tools, and one of them can run any code in Unity. Keep your AI app's approval prompts on and connect only apps you trust. MCP for Unity collects usage data by default; its documentation explains how to turn that off."), StudioGUI.CardDanger);
            GUILayout.Label(Loc.T("Names of parts, materials and textures go to the AI service you use. Pictures go only if you allow them. Your skill progress is never sent."), s_dim);
        }

        void DrawRecent()
        {
            GUILayout.Label(Loc.T("Recent helper actions (this session)"), s_step);
            var log = AgentAccess.Recent;
            if (log.Count == 0) { GUILayout.Label(Loc.T("Nothing yet."), s_dim); return; }

            double now = EditorApplication.timeSinceStartup;
            int key = log.Count * 100000 + (int)(now / 15);
            if (key != _recentKey || _recent == null)
            {
                _recentKey = key;
                _recent = new string[log.Count];
                for (int i = 0; i < log.Count; i++)
                {
                    var a = log[log.Count - 1 - i];
                    _recent[i] = a.text + " · " + Ago(now - a.time);
                }
            }
            bool canUndo = AgentApi.CanUndoLast();
            for (int i = 0; i < _recent.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(_recent[i], s_dim);
                if (i == 0 && canUndo && GUILayout.Button(s_undo, EditorStyles.miniButton, GUILayout.MinWidth(48)))
                {
                    var w = AvatarTextureWindow.AutoFind(false);
                    if (w != null) EditorApplication.delayCall += () => { if (w != null) { AgentApi.UndoLast(w, "undo_last"); Repaint(); } };
                }
                GUILayout.EndHorizontal();
            }
        }

        static string Ago(double seconds) => seconds < 15 ? Loc.T("just now") : seconds < 60 ? Loc.T("under a minute ago") : Loc.F("{0} min ago", (int)seconds / 60);
    }

    partial class AvatarTextureWindow
    {
        /// <summary>
        /// The AI helper window's state: "not_installed", "unsupported" or "ready". With a state name it makes the window
        /// draw that state until called with "" (for screenshots on a computer that is in a different state).
        /// </summary>
        internal string AutoAgentWindowState(string show = null) => show == null ? AgentWindow.StateWord : AgentWindow.Preview(show);
    }
}
