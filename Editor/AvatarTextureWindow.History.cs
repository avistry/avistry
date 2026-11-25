using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        struct HistoryEntry
        {
            public int revision;
            public int group;
            public string name;
            public string recipeJson;
            public string activeLayerId;
        }

        const int HistoryMax = 200;
        const int HistoryShown = 20;
        const int HistoryCharacterBudget = 16 * 1024 * 1024;

        [SerializeField] int _historyRevision;
        [NonSerialized] bool _foldHistory;
        [NonSerialized] readonly List<HistoryEntry> _history = new List<HistoryEntry>();
        [NonSerialized] int _historyPos = -1;
        [NonSerialized] HistoryEntry _historyStart;
        [NonSerialized] int _nextHistoryRevision, _historyCharacters;

        void RecordHistory(int? undoGroup = null)
        {
            string json = EditorJsonUtility.ToJson(_recipe);
            var current = _historyPos < 0 ? _historyStart : _history[_historyPos];
            if (json == current.recipeJson) return;

            int group = undoGroup ?? Undo.GetCurrentGroup();
            string name = Undo.GetCurrentGroupName();
            if (string.IsNullOrEmpty(name)) name = "Edit";

            while (_history.Count > 0 && (_history.Count > _historyPos + 1 || _history[_history.Count - 1].group >= group))
            {
                int last = _history.Count - 1;
                _historyCharacters -= _history[last].recipeJson.Length;
                _history.RemoveAt(last);
            }

            Undo.RecordObject(this, name);
            _historyRevision = ++_nextHistoryRevision;
            _history.Add(new HistoryEntry
            {
                revision = _historyRevision, group = group, name = name, recipeJson = json, activeLayerId = _activeLayerId,
            });
            _historyCharacters += json.Length;
            while (_history.Count > 1 && (_history.Count > HistoryMax || _historyCharacters > HistoryCharacterBudget))
            {
                _historyCharacters -= _historyStart.recipeJson.Length;
                _historyStart = _history[0];
                _history.RemoveAt(0);
            }
            _historyPos = _history.Count - 1;
        }

        void ClearHistory()
        {
            _history.Clear();
            _historyPos = -1;
            _nextHistoryRevision = Mathf.Max(_nextHistoryRevision, _historyRevision);
            _historyStart = new HistoryEntry
            {
                revision = _historyRevision,
                recipeJson = _recipe != null ? EditorJsonUtility.ToJson(_recipe) : "",
                activeLayerId = _activeLayerId,
            };
            _historyCharacters = _historyStart.recipeJson.Length;
        }

        void OnUndoRedoEvent(in UndoRedoInfo _)
        {
            RepairRecipe("undo");
            _historyPos = _history.FindIndex(entry => entry.revision == _historyRevision);
            if (_historyPos < 0 && _historyRevision != _historyStart.revision) ClearHistory();
        }

        void GoToHistory(int index)
        {
            EndStroke();
            if (_needsRebuild) Rebuild();
            index = Mathf.Clamp(index, -1, _history.Count - 1);
            if (_recipe == null || index == _historyPos) return;

            var entry = index < 0 ? _historyStart : _history[index];
            var checkpoint = CreateInstance<TextureRecipe>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(entry.recipeJson, checkpoint);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                Undo.RecordObjects(new UnityEngine.Object[] { this, _recipe }, Loc.T("Restore Avistry layers"));
                _recipe.layers = checkpoint.layers;
            }
            finally { DestroyImmediate(checkpoint); }
            _historyRevision = entry.revision;
            _historyPos = index;
            _activeLayerId = entry.activeLayerId;
            if (_maskEditLayerId != _activeLayerId) _maskEditLayerId = null;
            RefreshEditedRecipe();
            _unsaved = _recipe.layers.Count > 0;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }

        void DrawHistory()
        {
            string title = _history.Count == 1 ? Loc.T("History (1 step)") : Loc.F("History ({0} steps)", _history.Count);
            if (!StudioGUI.BeginFoldGroup(ref _foldHistory, title, null, null,
                Loc.T("Restore a layer step from this edit. Ctrl+Z undoes the jump in one step."), "undo"))
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
            string start = _historyStart.name ?? (_history.Count == 0 ? Loc.T("Start (no steps yet)") : Loc.T("Start of this edit"));
            if (DrawHistoryRow(-1, start, _historyPos == -1, false, shown))
                jump = -1;
            EditorGUILayout.EndVertical();
            GUILayout.Label(Loc.T("Click a step to restore its layers. Other Unity edits stay in place."), StudioGUI.Hint);
            StudioGUI.EndGroup();

            if (jump != int.MinValue && jump != _historyPos)
            {
                int target = jump;
                EditorApplication.delayCall += () => { if (this != null) GoToHistory(target); };
            }
        }

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
