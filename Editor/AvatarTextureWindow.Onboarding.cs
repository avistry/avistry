using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static GUIContent NewToUnityText => new GUIContent(Loc.T(
            "New to Unity? The Hierarchy is the list of everything in your scene: click a part of your avatar there to start. " +
            "Nothing changes your files until you press Save, and Ctrl+Z undoes your edits."));

        /// <summary>The strip grows with the text, so a narrow panel wraps the sentence instead of cutting it off.</summary>
        static float NewToUnityStripHeight(float width) => Mathf.Max(78f, 33f + StudioGUI.Hint.CalcHeight(NewToUnityText, width - 16f));
        static bool? s_newToUnityDismissed;

        // EditorPrefs is a registry read on Windows; the cache is refreshed by the hooks and on dismiss.
        static bool NewToUnityDismissed => s_newToUnityDismissed ??= EditorPrefs.GetBool(StudioMode.NewToUnityKey, false);

        void DismissNewToUnity()
        {
            s_newToUnityDismissed = true;
            EditorPrefs.SetBool(StudioMode.NewToUnityKey, true);
            Repaint();
        }

        void DrawNewToUnityStrip(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.HeaderBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }
            StudioGUI.Count("onboarding.newToUnity");
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 29), NewToUnityText, StudioGUI.Hint);
            // Buttons are sized from their labels; when the three do not fit side by side the longest gets its short name.
            var treeC = new GUIContent(Loc.T("Start the skill tree"), Loc.T("Opens the skill tree on the first thing to try"));
            var glossaryC = new GUIContent(Loc.T("Glossary…"), Loc.T("Plain-language explanations of the words used here"));
            var gotItC = new GUIContent(Loc.T("Got it"), Loc.T("Hide this for good"));
            float W(GUIContent c) => EditorStyles.miniButton.CalcSize(c).x + 8f;
            if (W(treeC) + W(glossaryC) + W(gotItC) + 8f > r.width - 16f) treeC.text = Loc.T("Skill tree");
            float y = r.yMax - 25;
            var gotIt = new Rect(r.xMax - 8 - W(gotItC), y, W(gotItC), 20);
            var glossary = new Rect(gotIt.x - 4 - W(glossaryC), y, W(glossaryC), 20);
            var tree = new Rect(glossary.x - 4 - W(treeC), y, W(treeC), 20);
            if (GUI.Button(glossary, glossaryC, EditorStyles.miniButton)) GlossaryWindow.Open();
            if (GUI.Button(tree, treeC, EditorStyles.miniButton)) StartSkillTree();
            if (GUI.Button(gotIt, gotItC, EditorStyles.miniButton)) DismissNewToUnity();
        }

        static readonly List<string> s_nextUp = new List<string>(1);

        static void StartSkillTree()
        {
            Learn.NextUp(s_nextUp, 1);
            GlossaryWindow.Open(s_nextUp.Count > 0 ? s_nextUp[0] : null);
        }

        // Renderers under the selected object's avatar root, or in the open scenes when nothing is selected.
        List<Renderer> PartChoices()
        {
            IEnumerable<Renderer> all;
            var sel = Selection.activeGameObject;
            if (sel != null)
            {
                var any = ResolveRenderer(sel);
                var root = (any != null ? TargetResolver.AvatarRoot(any) : null) ?? sel.transform.root;
                all = root.GetComponentsInChildren<Renderer>(true);
            }
            else all = FindObjectsOfType<Renderer>(true);
            return all.Where(x => x != null && IsMeshRenderer(x) && x.gameObject.scene.IsValid() && x.sharedMaterials.Any(m => m != null)).ToList();
        }

        void DrawPartListButton()
        {
            StudioGUI.Count("pick.partList");
            if (GUILayout.Button(new GUIContent(Loc.T("Choose a part from a list…"), Loc.T("Lists the parts of the selected avatar, or of the open scenes when nothing is selected")), EditorStyles.miniButton))
                DrawPartListMenu();
        }

        void DrawPartListMenu()
        {
            var menu = new GenericMenu();
            var parts = PartChoices();
            if (parts.Count == 0) menu.AddDisabledItem(new GUIContent(Loc.T("No parts found. Open a scene with your avatar.")));
            foreach (var p in parts)
            {
                var part = p;
                var root = TargetResolver.AvatarRoot(part);
                string label = root != null && root != part.transform ? $"{root.name} / {part.name}" : part.name;
                menu.AddItem(new GUIContent(NoSubmenu(label)), part == _renderer, () => SetRendererChecked(part));
            }
            menu.ShowAsContext();
        }

        string SlotStatusText() => Msg.M25SlotStatus(_material != null);
    }
}
