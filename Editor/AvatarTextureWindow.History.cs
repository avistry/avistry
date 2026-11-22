using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The "History" list in 4 · Paint: the undo steps of this edit, newest first. It is a view on Unity's own undo
    /// (Ctrl+Z / Ctrl+Y keep working): clicking a step repeats undo or redo until that step is the current one.
    /// </summary>
    partial class AvatarTextureWindow
    {
        struct HistoryEntry
        {
            public int group;     // Unity undo group this step lives in
            public string name;   // what the step was ("Paint stroke", "Add layer"…)
        }

        const int HistoryMax = 200;   // entries kept
        const int HistoryShown = 20;  // rows shown

        [NonSerialized] bool _foldHistory; // not serialized, so going back a step never snaps the list shut
        [NonSerialized] readonly List<HistoryEntry> _history = new List<HistoryEntry>();
        [NonSerialized] int _historyPos = -1; // index of the last applied step (-1 = the start of this edit)

        /// <summary>Called after the tool recorded an undo step on the recipe (MarkEdited, end of a stroke).</summary>
        void RecordHistory()
        {
            int group = Undo.GetCurrentGroup();
            string name = Undo.GetCurrentGroupName();
            if (string.IsNullOrEmpty(name)) name = "Edit";

            // Stepping back and editing again: Unity drops the redo steps, so do we.
            if (_historyPos + 1 < _history.Count) _history.RemoveRange(_historyPos + 1, _history.Count - _historyPos - 1);
            // A step that was merged into an earlier group (strokes collapse theirs) replaces what it merged.
            while (_history.Count > 0 && _history[_history.Count - 1].group >= group) _history.RemoveAt(_history.Count - 1);

            _history.Add(new HistoryEntry { group = group, name = name });
            if (_history.Count > HistoryMax) _history.RemoveAt(0);
            _historyPos = _history.Count - 1;
        }

        void ClearHistory()
        {
            _history.Clear();
            _historyPos = -1;
        }

        /// <summary>Keeps the "current step" right when the user presses Ctrl+Z / Ctrl+Y (or a step is jumped to).</summary>
        void OnUndoRedoEvent(in UndoRedoInfo info)
        {
            // By group order rather than an exact match: strokes and harness steps collapse or renumber their groups, so
            // the group Unity reports can differ from the one recorded. After undoing group g, the steps still applied
            // are those before g; after redoing g, those up to and including g.
            RepairRecipe("undo");
            int g = info.undoGroup;
            int pos = -1;
            for (int i = 0; i < _history.Count; i++)
                if (info.isRedo ? _history[i].group <= g : _history[i].group < g) pos = i;
            _historyPos = pos;
        }

        /// <summary>Goes to just after step <paramref name="index"/> (-1 = the start): undo or redo until it is current.</summary>
        void GoToHistory(int index)
        {
            index = Mathf.Clamp(index, -1, _history.Count - 1);
            EndStroke();
            int stuck = 0; // other undo steps (selection changes…) sit between ours; give up if nothing moves for a long while
            for (int guard = 0; guard < 400 && _historyPos != index && stuck < 40; guard++)
            {
                int before = _historyPos;
                if (index < _historyPos) Undo.PerformUndo(); else Undo.PerformRedo();
                stuck = _historyPos == before ? stuck + 1 : 0;
            }
            _dirty = true;
            Repaint();
        }

        /// <summary>Drops steps Unity no longer has (undo history cleared, e.g. after a reload or play mode).</summary>
        void PruneHistory()
        {
            int top = Undo.GetCurrentGroup();
            while (_history.Count > 0 && _history[_history.Count - 1].group > top + 1) _history.RemoveAt(_history.Count - 1);
            if (_historyPos >= _history.Count) _historyPos = _history.Count - 1;
        }

        void DrawHistory()
        {
            PruneHistory();
            string title = _history.Count == 1 ? Loc.T("History (1 step)") : Loc.F("History ({0} steps)", _history.Count);
            if (!StudioGUI.BeginFoldGroup(ref _foldHistory, title, null, null, Loc.T("The steps of this edit. Click one to go back to it, or forward again. Ctrl+Z and Ctrl+Y work too."), "undo"))
            {
                StudioGUI.EndGroup();
                return;
            }
            StudioGUI.Count("paint.history");

            var list = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(list, StudioGUI.ListBg);
            int shown = 0;
            int jump = int.MinValue;
            for (int i = _history.Count - 1; i >= 0 && shown < HistoryShown; i--, shown++)
                if (DrawHistoryRow(i, _history[i].name, i == _historyPos, i > _historyPos, shown)) jump = i;
            if (_history.Count > HistoryShown)
                GUILayout.Label(Loc.F("…and {0} older step(s)", _history.Count - HistoryShown), StudioGUI.Hint);
            if (DrawHistoryRow(-1, _history.Count == 0 ? Loc.T("Start (no steps yet)") : Loc.T("Start of this edit"), _historyPos == -1, false, shown))
                jump = -1;
            EditorGUILayout.EndVertical();
            GUILayout.Label(Loc.T("Click a step to go back to it, or forward again if it's greyed. Ctrl+Z / Ctrl+Y still work."), StudioGUI.Hint);
            StudioGUI.EndGroup();

            if (jump != int.MinValue && jump != _historyPos)
            {
                int target = jump;
                EditorApplication.delayCall += () => { if (this != null) GoToHistory(target); };
            }
        }

        /// <summary>One history row. Returns true when it was clicked.</summary>
        bool DrawHistoryRow(int index, string name, bool current, bool undone, int visualIndex)
        {
            var e = Event.current;
            var r = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            if (e.type == EventType.Repaint)
            {
                if (current) EditorGUI.DrawRect(r, StudioGUI.RowActive);
                else if (visualIndex % 2 == 1) EditorGUI.DrawRect(r, StudioGUI.RowAlt);
            }
            string tip = index < 0 ? Loc.T("Go back to before the first step") : undone ? Loc.T("Undone. Click to redo up to this step.") : current ? Loc.T("You are here") : Loc.T("Click to go back to just after this step");
            string label = (current ? "▶ " : "   ") + name + (undone ? Loc.T("  (undone)") : "");
            GUI.Label(new Rect(r.x + 6, r.y, r.width - 8, r.height), new GUIContent(label, tip), undone ? StudioGUI.RowLabelDisabled : StudioGUI.RowLabel);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }

        // For the test harness (ats history).
        internal int AutoHistoryPos => _historyPos;
        internal List<string> AutoHistoryNames()
        {
            var l = new List<string>();
            foreach (var h in _history) l.Add(h.name);
            return l;
        }
        internal void AutoGoToHistory(int index) { GoToHistory(index); AutoSettle(); }
    }
}
