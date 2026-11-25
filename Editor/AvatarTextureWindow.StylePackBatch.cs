using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        sealed partial class StylePackReview
        {
            sealed class BatchRow
            {
                internal StylePack.Entry Entry;
                internal StylePackTargets.Resolution Target;
                internal string Problem;
                internal string[] Details;
                internal Hash128 TextureHash;
                internal uint TextureVersion;
                internal int Enabled, Issues;
                internal bool Checked, Expanded;

                internal IEnumerable<(Renderer part, int slot, string property)> Claims =>
                    Target.Slot.properties.Select(property => (Target.Renderer, Entry.materialIndex, property));
            }

            [SerializeField] bool _showBatchReview, _batchCancelled;
            [NonSerialized] StylePack.Manifest _batchManifest;
            [NonSerialized] List<BatchRow> _batchRows;
            [NonSerialized] BatchRow _batchPending;
            [NonSerialized] StylePack.Edit _batchEdit;
            [NonSerialized] IEnumerator<int> _batchReader;
            [NonSerialized] (long length, DateTime write) _batchFile;
            [NonSerialized] string _batchProblem;
            [NonSerialized] MessageType _batchMessageType;
            [NonSerialized] bool _batchChecking, _batchReady, _batchApplying;
            [NonSerialized] int[] _batchSelection;
            [NonSerialized] StylePackApply.Receipt[] _batchReceipts;
            [NonSerialized] Vector2 _batchScroll;

            void BeginBatchReview()
            {
                ClearReview();
                StopBatchReview();
                _showBatchReview = true;
                _batchCancelled = false;
                _batchManifest = null;
                _batchRows = new List<BatchRow>();
                _batchProblem = null;
                _batchReceipts = null;
                _batchScroll = Vector2.zero;
                _batchReady = false;
                _batchChecking = true;
                EditorApplication.update += PrepareBatchReview;
                Repaint();
            }

            void StopBatchReview()
            {
                EditorApplication.update -= PrepareBatchReview;
                _batchChecking = false;
                ReleaseBatchEntry();
            }

            void CancelBatchReview()
            {
                StopBatchReview();
                _batchReady = false;
                _batchCancelled = true;
                SetBatchMessage(Loc.T("Style pack review cancelled."), MessageType.Info);
                Repaint();
            }

            void ReleaseBatchEntry()
            {
                _batchReader?.Dispose();
                _batchReader = null;
                _batchEdit?.Dispose();
                _batchEdit = null;
                if (_batchPending?.Target?.Geometry != null)
                {
                    _batchPending.Target.Geometry.ReleaseSurfaceMesh();
                    _batchPending.Target.Geometry = null;
                }
                _batchPending = null;
            }

            static (long length, DateTime write) BatchFileStamp(string path)
            {
                var file = new FileInfo(path);
                return (file.Length, file.LastWriteTimeUtc);
            }

            void PrepareBatchReview()
            {
                if (this == null || _owner == null) { StopBatchReview(); return; }
                if (EditorBusy) { Repaint(); return; }
                try
                {
                    if (_batchManifest == null)
                    {
                        _batchFile = BatchFileStamp(_path);
                        _batchManifest = StylePack.Read(_path);
                        return;
                    }
                    if (_batchReader != null)
                    {
                        ReadBatchLayers();
                        return;
                    }
                    if (_batchRows.Count == _batchManifest.entries.Length)
                    {
                        if (_batchFile != BatchFileStamp(_path)) throw new IOException();
                        _batchReady = true;
                        StopBatchReview();
                        return;
                    }

                    var entry = _batchManifest.entries[_batchRows.Count];
                    var target = StylePackTargets.Resolve(_root, entry);
                    _batchPending = new BatchRow { Entry = entry, Target = target, Problem = StylePackApply.Check(target) };
                    if (_batchPending.Problem != null) { FinishBatchEntry(); return; }
                    _batchPending.TextureHash = AssetDatabase.GetAssetDependencyHash(target.Slot.assetPath);
                    _batchPending.TextureVersion = target.Slot.texture.updateCount;
                    _batchEdit = new StylePack.Edit();
                    _batchReader = StylePack.ReadEditSteps(_path, _batchManifest, entry, target.LayoutMatches, _batchEdit).GetEnumerator();
                }
                catch (Exception error)
                {
                    StopBatchReview();
                    SetBatchMessage(error is FileNotFoundException or DirectoryNotFoundException
                        ? Loc.T("The style pack file is missing. Open it again from its new location.")
                        : Loc.T("Couldn’t finish reviewing the style pack. Check the file and review it again."), MessageType.Warning);
                }
                finally { Repaint(); }
            }

            void ReadBatchLayers()
            {
                double until = EditorApplication.timeSinceStartup + .01;
                do
                {
                    if (!_batchReader.MoveNext()) { FinishBatchEntry(); return; }
                }
                while (EditorApplication.timeSinceStartup < until);
            }

            void FinishBatchEntry()
            {
                var row = _batchPending;
                if (_batchEdit != null)
                {
                    row.Enabled = _batchEdit.Layers.Count(layer => layer.enabled && layer.opacity > 0f);
                    row.Issues = _batchEdit.Issues.Count;
                    row.Details = _batchEdit.Issues.Take(5).Select(detail => PackIssueText(detail.layer, detail.issue, detail.artwork)).ToArray();
                    if (row.Enabled == 0) row.Problem = Loc.T("This edit has no enabled layers that can be applied.");
                }
                if (BatchRowProblem(row) == null && row.Issues == 0)
                {
                    var claimed = _batchRows.Where(other => other.Checked).SelectMany(other => other.Claims).ToHashSet();
                    row.Checked = !row.Claims.Any(claimed.Contains);
                }
                _batchRows.Add(row);
                ReleaseBatchEntry();
            }

            string BatchRowProblem(BatchRow row)
            {
                if (row.Problem != null) return row.Problem;
                if (_owner != null && _owner._recipe?.layers.Count > 0 && _owner.IsEditedSlot(row.Target.Renderer, row.Entry.materialIndex))
                    return Loc.T("This material slot has an open edit. Finish it or switch parts before applying here.");
                return null;
            }

            void DrawBatchReview()
            {
                bool back;
                using (new EditorGUI.DisabledScope(_batchApplying))
                    back = GUILayout.Button(Loc.T("Back to edit preview"), EditorStyles.miniButton);
                if (back)
                {
                    StopBatchReview();
                    _batchRows = null;
                    _batchManifest = null;
                    _showBatchReview = false;
                    QueueReview();
                    return;
                }
                GUILayout.Label(_batchManifest?.name ?? Loc.T("Review all edits"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Loc.T("Avatar root"), _root != null ? _root.name : Loc.T("(none)"));
                if (_batchReceipts != null)
                {
                    DrawBatchReceipts();
                    return;
                }
                if (_batchChecking)
                {
                    string label = EditorBusy ? PreparationLabel() : _batchManifest == null ? Loc.T("Reading the style pack…") :
                        Loc.F("Checking edits ({0} of {1})…", _batchRows.Count, _batchManifest.entries.Length);
                    GUILayout.Label(label, StudioGUI.Hint);
                    if (GUILayout.Button(Loc.T("Cancel review"))) CancelBatchReview();
                }
                if (_batchProblem != null) EditorGUILayout.HelpBox(_batchProblem, _batchMessageType);
                using (var scroll = new EditorGUILayout.ScrollViewScope(_batchScroll, false, false))
                {
                    _batchScroll = scroll.scrollPosition;
                    if (_batchRows != null)
                    {
                        var claimed = new HashSet<(Renderer part, int slot, string property)>();
                        foreach (var row in _batchRows)
                            if (row.Checked && BatchRowProblem(row) == null) claimed.UnionWith(row.Claims);
                        foreach (var row in _batchRows) DrawBatchRow(row, claimed);
                    }
                }
                if (!_batchChecking && !_batchReady && GUILayout.Button(Loc.T("Review again"))) BeginBatchReview();
                if (!_batchReady) return;
                int[] selected = Enumerable.Range(0, _batchRows.Count)
                    .Where(i => _batchRows[i].Checked && BatchRowProblem(_batchRows[i]) == null).ToArray();
                GUILayout.Label(Loc.F("Checked edits: {0}", selected.Length), StudioGUI.Hint);
                bool apply;
                using (new EditorGUI.DisabledScope(selected.Length == 0 || _batchApplying || EditorBusy))
                    apply = GUILayout.Button(Loc.T("Apply checked edits"));
                StudioGUI.Count("pack.applyCheckedEdits", GUILayoutUtility.GetLastRect());
                if (!apply) return;
                _batchSelection = selected;
                _batchApplying = true;
                EditorApplication.delayCall += ApplyCheckedBatch;
            }

            void DrawBatchRow(BatchRow row, HashSet<(Renderer part, int slot, string property)> claimed)
            {
                string problem = BatchRowProblem(row);
                bool overlaps = problem == null && !row.Checked && row.Claims.Any(claimed.Contains);
                string name = row.Entry.name ?? row.Entry.sourceName ?? Loc.T("Part");
                string context = string.Join(" / ", row.Entry.rendererPath ?? Array.Empty<string>());
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    bool next;
                    using (new EditorGUI.DisabledScope(!_batchReady || _batchApplying || problem != null || overlaps))
                        next = EditorGUILayout.ToggleLeft(new GUIContent(name, context), row.Checked && problem == null);
                    if (problem == null && next != row.Checked)
                    {
                        row.Checked = next;
                        if (next) claimed.UnionWith(row.Claims);
                        else claimed.ExceptWith(row.Claims);
                    }
                    if (!string.IsNullOrEmpty(context) && context != name) GUILayout.Label(context, StudioGUI.Hint);
                    if (problem != null) GUILayout.Label(problem, StudioGUI.CheckLabel);
                    else if (overlaps) GUILayout.Label(Loc.T("Uncheck the overlapping edit to choose this one."), StudioGUI.Hint);
                    else GUILayout.Label(Loc.F("Enabled layers: {0}", row.Enabled), StudioGUI.Hint);
                    if (row.Issues == 0) return;
                    row.Expanded = StudioGUI.Foldout(row.Expanded, new GUIContent(Loc.F("Layer issues ({0})", row.Issues)));
                    if (!row.Expanded) return;
                    foreach (string detail in row.Details) GUILayout.Label(detail, StudioGUI.Hint);
                    if (row.Issues > row.Details.Length)
                        GUILayout.Label(Loc.T("Open the individual preview to see all layers."), StudioGUI.Hint);
                }
            }

            void SetBatchMessage(string text, MessageType type)
            {
                _batchProblem = text;
                _batchMessageType = type;
            }

            bool BatchStillReviewed()
            {
                if (this == null || _owner == null || _root == null || !_batchReady || _batchSelection is not { Length: > 0 })
                    return false;
                try { if (_batchFile != BatchFileStamp(_path)) return false; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                return _batchSelection.All(i => BatchRowProblem(_batchRows[i]) == null);
            }

            void ApplyCheckedBatch()
            {
                try
                {
                    if (EditorBusy || !BatchStillReviewed())
                    {
                        _batchReady = false;
                        SetBatchMessage(Loc.T("The pack or avatar changed. Review the edits again before applying."), MessageType.Warning);
                        return;
                    }
                    bool targetsReviewed = false;
                    bool ReviewStillApplies()
                    {
                        if (!BatchStillReviewed()) return false;
                        if (!targetsReviewed)
                            targetsReviewed = _batchSelection.All(i =>
                            {
                                var row = _batchRows[i];
                                return row.Target.StillMatches() && row.Target.Slot.texture.updateCount == row.TextureVersion &&
                                    AssetDatabase.GetAssetDependencyHash(row.Target.Slot.assetPath) == row.TextureHash;
                            });
                        return targetsReviewed;
                    }
                    var result = StylePackApply.Apply(_path, _batchManifest, _root, _batchSelection, (label, progress) =>
                        EditorUtility.DisplayCancelableProgressBar(Loc.T("Apply style pack"), label, progress), ReviewStillApplies);
                    if (result.Cancelled) SetBatchMessage(Loc.T("Applying the style pack was cancelled."), MessageType.Info);
                    else if (result.Problem != null)
                    {
                        _batchReady = false;
                        SetBatchMessage(result.Problem, MessageType.Warning);
                    }
                    else
                    {
                        _batchReceipts = result.Receipts;
                        _batchReady = false;
                        _batchScroll = Vector2.zero;
                        if (_owner != null) { _owner._needsRebuild = true; _owner.Repaint(); }
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"[Avistry] Could not apply the style pack ({error.GetType().Name}).");
                    _batchReady = false;
                    SetBatchMessage(Loc.T("Couldn’t apply the style pack. Review the edits and try again."), MessageType.Warning);
                }
                finally
                {
                    _batchApplying = false;
                    _batchSelection = null;
                    EditorUtility.ClearProgressBar();
                    if (this != null) Repaint();
                }
            }

            void DrawBatchReceipts()
            {
                GUILayout.Label(Loc.F("Applied edits: {0}", _batchReceipts.Length), EditorStyles.boldLabel);
                GUILayout.Label(Loc.T("One Undo restores the avatar’s earlier materials. The saved edits stay in the project."), StudioGUI.Hint);
                using (var scroll = new EditorGUILayout.ScrollViewScope(_batchScroll, false, false))
                {
                    _batchScroll = scroll.scrollPosition;
                    foreach (var receipt in _batchReceipts)
                    {
                        var entry = _batchManifest.entries[receipt.EntryIndex];
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            GUILayout.Label(entry.name ?? entry.sourceName ?? Loc.T("Part"), EditorStyles.boldLabel);
                            if (receipt.IssueCount > 0) GUILayout.Label(Loc.F("Layer issues ({0})", receipt.IssueCount), StudioGUI.Hint);
                            if (receipt.PaddingWarning != null) GUILayout.Label(receipt.PaddingWarning, StudioGUI.Hint);
                            if (GUILayout.Button(Loc.T("Show saved edit"), EditorStyles.miniButton))
                            {
                                var edit = AssetDatabase.LoadAssetAtPath<TextureRecipe>(receipt.RecipePath);
                                if (edit != null) { Selection.activeObject = edit; EditorGUIUtility.PingObject(edit); }
                            }
                        }
                    }
                }
            }
        }
    }
}
