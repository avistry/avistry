using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// What the person has read and done in the skill tree. One EditorPrefs value per machine holding concept ids only:
    /// no times, names, paths or counts. Nothing here ever locks a feature.
    /// </summary>
    internal static class Learn
    {
        internal enum State { New, Read, Done }

        internal const string PrefKey = "Avistry.Learn.v1";
        const string BackupKey = "Avistry.Learn.Backup";
        internal const string GraduateName = "Avistry graduate";
        /// <summary>The value stored in "celebrated" once the graduate medal was shown.</summary>
        internal const int GraduateMark = Glossary.TierCount + 1;

        [Serializable] class Store { public int v = 1; public List<string> read = new List<string>(), done = new List<string>(); public List<int> celebrated = new List<int>(); }
        class Book { public readonly HashSet<string> read = new HashSet<string>(), done = new HashSet<string>(); public readonly HashSet<int> celebrated = new HashSet<int>(); }

        static Book s_real, s_sandbox;
        static bool s_inSandbox, s_dirty, s_flushQueued, s_warned;
        static int s_suppress;
        static string s_sandboxBackup;

        /// <summary>A concept became Done through <see cref="Did"/> or <see cref="GotIt"/> (not through a silent bulk mark).</summary>
        internal static event Action<string> ConceptDone;
        /// <summary>Anything in the store changed; windows repaint.</summary>
        internal static event Action Changed;

        static Learn()
        {
            // A write waiting for the next frame must not be lost to a reload or to closing Unity.
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
        }

        /// <summary>
        /// Keeps everything in memory and never reads or writes EditorPrefs. Switching it on starts from an empty book;
        /// switching it off drops that book. For tests.
        /// </summary>
        internal static bool Sandbox
        {
            get => s_inSandbox;
            set
            {
                if (s_inSandbox == value) return;
                s_inSandbox = value;
                s_sandbox = value ? new Book() : null;
                s_sandboxBackup = null;
                Changed?.Invoke();
            }
        }

        /// <summary>Marks nothing while any scope is open. The AI helper wraps every call in one. Dispose it once.</summary>
        internal readonly struct Suppression : IDisposable
        {
            public void Dispose() { if (s_suppress > 0) s_suppress--; }
        }

        internal static Suppression Suppress() { s_suppress++; return default; }
        internal static bool Suppressed => s_suppress > 0;

        static Book Cur => s_inSandbox ? s_sandbox : (s_real ??= LoadReal());

        internal static State StateOf(string conceptId)
        {
            if (conceptId == null) return State.New;
            var b = Cur;
            return b.done.Contains(conceptId) ? State.Done : b.read.Contains(conceptId) ? State.Read : State.New;
        }

        internal static bool IsDone(string conceptId) => conceptId != null && Cur.done.Contains(conceptId);

        /// <summary>Runs once per user action. One dictionary lookup, no allocation, never throws.</summary>
        internal static void Did(string learnEvent)
        {
            if (s_suppress > 0) return;
            try
            {
                var ids = Glossary.ConceptsDoneBy(learnEvent);
                if (ids == null) return;
                for (int i = 0; i < ids.Length; i++) MarkDone(ids[i], true);
            }
            catch (Exception ex)
            {
                if (!s_warned) { s_warned = true; Debug.LogWarning("[Avistry] Skill progress could not be updated (" + ex.Message + ")."); }
            }
        }

        /// <summary>The "I've got it" button.</summary>
        internal static void GotIt(string conceptId)
        {
            if (s_suppress > 0 || !Glossary.Has(conceptId)) return;
            MarkDone(conceptId, true);
        }

        /// <summary>"Mark as not done": the card keeps its Read mark.</summary>
        internal static void Unmark(string conceptId)
        {
            if (s_suppress > 0 || conceptId == null) return;
            if (Cur.done.Remove(conceptId)) { MarkDirty(); Changed?.Invoke(); }
        }

        internal static void MarkRead(string conceptId)
        {
            if (s_suppress > 0 || string.IsNullOrEmpty(conceptId)) return;
            var b = Cur;
            if (b.done.Contains(conceptId) || b.read.Contains(conceptId) || !Glossary.Has(conceptId)) return;
            b.read.Add(conceptId);
            MarkDirty();
            Changed?.Invoke();
        }

        /// <summary>"I know Unity already": marks a whole tier Done with no toast or medal.</summary>
        internal static void MarkTierDone(int tier)
        {
            if (s_suppress > 0) return;
            bool any = false;
            foreach (var e in Glossary.All)
                if (!e.bonus && Glossary.TierOf(e) == tier) any |= Cur.done.Add(e.id);
            if (!any) return;
            MarkDirty();
            Changed?.Invoke();
        }

        static void MarkDone(string id, bool notify)
        {
            if (!Cur.done.Add(id)) return;
            MarkDirty();
            if (notify) ConceptDone?.Invoke(id);
            Changed?.Invoke();
        }

        /// <summary>Counted concepts that are Done.</summary>
        internal static int DoneCount()
        {
            int n = 0;
            var all = Glossary.All;
            var done = Cur.done;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].bonus && done.Contains(all[i].id)) n++;
            return n;
        }

        internal static int TierDoneCount(int tier)
        {
            int n = 0;
            var all = Glossary.All;
            var done = Cur.done;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].bonus && Glossary.TierOf(all[i]) == tier && done.Contains(all[i].id)) n++;
            return n;
        }

        internal static bool TierComplete(int tier)
        {
            int size = Glossary.TierSize(tier);
            return size > 0 && TierDoneCount(tier) >= size;
        }

        internal static bool IsGraduate => Glossary.CountedCount > 0 && DoneCount() >= Glossary.CountedCount;

        /// <summary>The highest tier with any progress (0 when there is none). The unseen dots use it.</summary>
        internal static int HighestTierWithProgress()
        {
            int top = 0;
            var all = Glossary.All;
            var done = Cur.done;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].bonus && done.Contains(all[i].id)) top = Mathf.Max(top, Glossary.TierOf(all[i]));
            return top;
        }

        /// <summary>
        /// Up to <paramref name="max"/> concepts to try next: not Done, and everything they come after is Done.
        /// By tier, then by order. Bonus nodes are never listed.
        /// </summary>
        internal static void NextUp(List<string> into, int max)
        {
            into.Clear();
            var all = Glossary.All;
            var done = Cur.done;
            for (int tier = 1; tier <= Glossary.TierCount && into.Count < max; tier++)
                for (int i = 0; i < all.Count && into.Count < max; i++)
                {
                    var e = all[i];
                    if (e.bonus || Glossary.TierOf(e) != tier || done.Contains(e.id)) continue;
                    bool ready = true;
                    if (e.requires != null)
                        for (int r = 0; r < e.requires.Length && ready; r++) ready = done.Contains(e.requires[r]);
                    if (ready) into.Add(e.id);
                }
        }

        internal static bool IsCelebrated(int mark) => Cur.celebrated.Contains(mark);

        internal static void SetCelebrated(int mark)
        {
            if (!Cur.celebrated.Add(mark)) return;
            MarkDirty();
        }

        /// <summary>Clears progress. A copy is kept until Unity closes so <see cref="BringBack"/> can undo it.</summary>
        internal static void Reset()
        {
            var b = Cur;
            Backup = ToJson(b);
            b.read.Clear();
            b.done.Clear();
            b.celebrated.Clear();
            if (!s_inSandbox)
            {
                s_dirty = false;
                EditorPrefs.DeleteKey(PrefKey);
            }
            Changed?.Invoke();
        }

        internal static bool HasBackup => !string.IsNullOrEmpty(Backup);

        internal static void BringBack()
        {
            if (!HasBackup) return;
            var restored = FromJson(Backup, false);
            var b = Cur;
            b.read.Clear(); b.done.Clear(); b.celebrated.Clear();
            b.read.UnionWith(restored.read);
            b.done.UnionWith(restored.done);
            b.celebrated.UnionWith(restored.celebrated);
            Backup = null;
            MarkDirty();
            Changed?.Invoke();
        }

        static string Backup
        {
            get => s_inSandbox ? s_sandboxBackup : SessionState.GetString(BackupKey, "");
            set { if (s_inSandbox) s_sandboxBackup = value; else SessionState.SetString(BackupKey, value ?? ""); }
        }

        // At most one write per frame, and only when something changed.
        static void MarkDirty()
        {
            if (s_inSandbox) return;
            s_dirty = true;
            if (s_flushQueued) return;
            s_flushQueued = true;
            EditorApplication.delayCall += Flush;
        }

        internal static void Flush()
        {
            s_flushQueued = false;
            if (!s_dirty || s_real == null) return;
            s_dirty = false;
            try { EditorPrefs.SetString(PrefKey, ToJson(s_real)); }
            catch (Exception ex) { Debug.LogWarning("[Avistry] Skill progress could not be saved (" + ex.Message + ")."); }
        }

        static Book LoadReal() => FromJson(EditorPrefs.GetString(PrefKey, ""), true);

        // Unknown ids are dropped so renamed concepts stay compatible, unless the glossary itself failed to load.
        static Book FromJson(string json, bool dropUnknown)
        {
            var book = new Book();
            if (string.IsNullOrEmpty(json)) return book;
            Store s;
            try { s = JsonUtility.FromJson<Store>(json); }
            catch (Exception) { return book; }
            if (s == null || s.v != 1) return book;
            bool check = dropUnknown && Glossary.All.Count > 0;
            if (s.read != null) foreach (var id in s.read) if (!check || Glossary.Has(id)) book.read.Add(id);
            if (s.done != null) foreach (var id in s.done) if (!check || Glossary.Has(id)) book.done.Add(id);
            if (s.celebrated != null) foreach (var n in s.celebrated) book.celebrated.Add(n);
            return book;
        }

        // Sorted so the same progress always gives the same text.
        static string ToJson(Book b)
        {
            var s = new Store();
            s.read.AddRange(b.read);
            s.done.AddRange(b.done);
            s.celebrated.AddRange(b.celebrated);
            s.read.Sort(StringComparer.Ordinal);
            s.done.Sort(StringComparer.Ordinal);
            s.celebrated.Sort();
            return JsonUtility.ToJson(s);
        }
    }
}
