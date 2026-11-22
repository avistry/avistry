using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>The click popup of an info icon: one glossary entry as a <see cref="ConceptCard"/>, with links to related ones.</summary>
    internal class InfoPopup : PopupWindowContent
    {
        const int MaxBack = 8;

        static string s_current;
        static readonly List<string> s_back = new List<string>(MaxBack + 1);
        static Floating s_floating;

        readonly string _id;
        Rect _screenAnchor;
        Vector2 _scroll;

        public InfoPopup(string conceptId) { _id = conceptId; }

        /// <summary>The concept whose popup is open now, or null. The icon uses it to draw its "open" state.</summary>
        internal static string Current => s_current;

        /// <summary>
        /// Opens the popup below <paramref name="anchor"/>. Inside OnGUI the anchor is a GUI rect; outside it, a screen rect.
        /// Opening a card marks it Read, unless <paramref name="markRead"/> is false (tests outside the sandbox).
        /// </summary>
        public static void Show(Rect anchor, string conceptId, bool markRead = true)
        {
            if (Glossary.Get(conceptId) == null) return;
            s_back.Clear();
            if (markRead) Learn.MarkRead(conceptId);
            s_current = conceptId;
            if (Event.current != null)
                PopupWindow.Show(anchor, new InfoPopup(conceptId) { _screenAnchor = GUIUtility.GUIToScreenRect(anchor) });
            else
                Floating.ShowAt(anchor, conceptId);
        }

        /// <summary>The window size the popup for this concept opens with.</summary>
        internal static Vector2 SizeOf(string conceptId)
        {
            var e = Glossary.Get(conceptId);
            return e == null ? Vector2.zero : ConceptCard.PopupSize(e);
        }

        public override Vector2 GetWindowSize() => SizeOf(_id);

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
        }

        public override void OnGUI(Rect rect)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                editorWindow.Close();
                return;
            }
            if (e.type == EventType.MouseMove) editorWindow.Repaint();
            Act(DrawCard(new Rect(0, 0, rect.width, rect.height), _id, ref _scroll), _id, _screenAnchor, editorWindow);
        }

        public override void OnClose()
        {
            if (s_current == _id) s_current = null;
        }

        static CardResult DrawCard(Rect area, string conceptId, ref Vector2 scroll)
        {
            var res = default(CardResult);
            var entry = Glossary.Get(conceptId);
            if (entry != null) ConceptCard.DrawPopup(area, entry, ref scroll, ref res, s_back.Count > 0);
            return res;
        }

        // The card only reports clicks. What they do happens here, after drawing and (for anything that opens a window) a frame later.
        static void Act(CardResult res, string conceptId, Rect screenAnchor, EditorWindow window)
        {
            if (res.GoTo != null)
            {
                Swap(res.GoTo, screenAnchor, conceptId);
                window.Close();
            }
            else if (res.Back)
            {
                if (s_back.Count == 0) return;
                string previous = s_back[s_back.Count - 1];
                s_back.RemoveAt(s_back.Count - 1);
                Swap(previous, screenAnchor, null);
                window.Close();
            }
            else if (res.OpenTree)
            {
                EditorApplication.delayCall += () => ConceptCard.OpenInTree(conceptId);
                window.Close();
            }
            else if (res.ShowMe)
            {
                EditorApplication.delayCall += () => ConceptCard.ShowMeHandler?.Invoke(conceptId);
                window.Close();
            }
            else if (res.Close) window.Close();
        }

        // Closing and reopening, because a popup cannot change its size once it is shown. delayCall: not while drawing.
        static void Swap(string conceptId, Rect screenAnchor, string pushed)
        {
            if (Glossary.Get(conceptId) == null) return;
            if (pushed != null)
            {
                s_back.Add(pushed);
                if (s_back.Count > MaxBack) s_back.RemoveAt(0);
            }
            Learn.MarkRead(conceptId);
            s_current = conceptId;
            EditorApplication.delayCall += () => Floating.ShowAt(screenAnchor, conceptId);
        }

        /// <summary>The same card in a small window, for when no GUI is being drawn (and for swapping to a related concept).</summary>
        internal class Floating : EditorWindow
        {
            string _id;
            Rect _anchor;
            Vector2 _scroll;

            internal static void ShowAt(Rect screenAnchor, string conceptId)
            {
                if (Glossary.Get(conceptId) == null) return;
                if (s_floating != null) s_floating.Close();
                var w = CreateInstance<Floating>();
                w._id = conceptId;
                w._anchor = screenAnchor;
                w.wantsMouseMove = true;
                s_floating = w;
                w.ShowAsDropDown(screenAnchor, SizeOf(conceptId));
            }

            void OnGUI()
            {
                // After a domain reload the id is gone.
                if (string.IsNullOrEmpty(_id)) { Close(); return; }
                var e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); Close(); return; }
                if (e.type == EventType.MouseMove) Repaint();
                Act(DrawCard(new Rect(0, 0, position.width, position.height), _id, ref _scroll), _id, _anchor, this);
            }

            void OnLostFocus() => Close();

            void OnDestroy()
            {
                if (s_floating == this) s_floating = null;
                if (s_current == _id) s_current = null;
            }
        }
    }
}
