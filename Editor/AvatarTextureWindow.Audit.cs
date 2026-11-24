using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        List<MaterialAudit.Finding> _auditFindings;
        Transform _auditRoot;
        int _auditMaterialCount;
        bool _auditShowInfo;
        bool _auditBusy;

        void DrawAuditGroup()
        {
            var root = MaterialAudit.FindAvatarRoot(_renderer);
            if (root != _auditRoot) { _auditRoot = root; _auditFindings = null; }

            string badge = null;
            if (_auditFindings != null)
            {
                int toLook = _auditFindings.Count(f => f.severity != MaterialAudit.Severity.Info);
                badge = toLook > 0 ? Loc.F("{0} to look at", toLook) : Loc.T("all good");
            }
            bool open = StudioGUI.BeginFoldGroup(ref _foldAudit, Loc.T("Check materials"), badge, badge,
                Loc.T("Finds pink or broken materials, missing textures and wrong texture settings, with safe fixes"), "shader");
            if (!open) { StudioGUI.EndGroup(); return; }

            if (root == null)
            {
                GUILayout.Label(Loc.T("Pick a part of the avatar first; then this checks every material on that avatar."), StudioGUI.Hint);
                StudioGUI.EndGroup();
                return;
            }

            GUILayout.Label(Loc.F("Looks through every material on ‘{0}’ (hidden parts too) for pink materials, " +
                                  "missing images and image settings that look wrong. Nothing changes until you press a fix button.", root.name), StudioGUI.Hint);
            GUILayout.Space(2);

            using (new EditorGUI.DisabledScope(_auditBusy))
            {
                var label = _auditFindings == null
                    ? StudioGUI.Icon("Search Icon", Loc.T(" Check materials"), Loc.T("Scan all materials on this avatar"))
                    : StudioGUI.Icon("Refresh", Loc.T(" Check again"), Loc.T("Scan all materials on this avatar again"));
                StudioGUI.Count("audit.check");
                if (GUILayout.Button(label, GUILayout.Height(24)))
                    EditorApplication.delayCall += () => RunAudit(true);
            }

            if (_auditFindings == null) { StudioGUI.EndGroup(); return; }
            StudioGUI.Count("audit.findings");

            var problems = _auditFindings.Where(f => f.severity == MaterialAudit.Severity.Problem).ToList();
            var warnings = _auditFindings.Where(f => f.severity == MaterialAudit.Severity.Warning).ToList();
            var infos = _auditFindings.Where(f => f.severity == MaterialAudit.Severity.Info).ToList();

            if (problems.Count == 0 && warnings.Count == 0)
                StudioGUI.CheckRow(StudioGUI.Check.Ok, Loc.F("Nothing broken on {0} material{1}.", _auditMaterialCount, (_auditMaterialCount == 1 ? "" : "s")));

            DrawAuditSection(Loc.T("Broken: needs fixing"), problems, StudioGUI.Check.Problem);
            DrawAuditSection(Loc.T("Worth a look"), warnings, StudioGUI.Check.Note);

            if (infos.Count > 0)
            {
                GUILayout.Space(4);
                StudioGUI.Count("audit.goodToKnow");
                _auditShowInfo = StudioGUI.Foldout(_auditShowInfo, new GUIContent(Loc.F("Good to know ({0})", infos.Count)));
                if (_auditShowInfo) DrawAuditSection(null, infos, StudioGUI.Check.Note);
            }
            StudioGUI.EndGroup();
        }

        void DrawAuditSection(string title, List<MaterialAudit.Finding> list, StudioGUI.Check mark)
        {
            if (list.Count == 0) return;
            if (title != null)
            {
                GUILayout.Space(4);
                GUILayout.Label($"{title} ({list.Count})", StudioGUI.GroupTitle);
            }
            foreach (var f in list)
            {
                string who = f.material != null ? $" <color=#999999>· {f.material.name}</color>" : "";
                StudioGUI.CheckRow(mark, $"<b>{f.title}</b>{who}");
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(20);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(f.detail, StudioGUI.Hint);
                        var usedBy = f.UsedBy;
                        if (usedBy != null) GUILayout.Label(Loc.F("On: {0}", usedBy), StudioGUI.Hint);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            if (f.ping != null && GUILayout.Button(new GUIContent(Loc.T("Select"), Loc.T("Show it in the Project window or Hierarchy, and in the Inspector")), EditorStyles.miniButton, GUILayout.Width(56)))
                            {
                                Selection.activeObject = f.ping;
                                EditorGUIUtility.PingObject(f.ping);
                                Learn.Did(LearnEvents.AssetPinged);
                            }
                            if (f.fix != null)
                            {
                                using (new EditorGUI.DisabledScope(_auditBusy))
                                {
                                    var fix = f;
                                    if (GUILayout.Button(new GUIContent(f.fixLabel, f.fixTooltip), EditorStyles.miniButton))
                                        EditorApplication.delayCall += () => ApplyAuditFix(fix);
                                }
                            }
                        }
                    }
                }
                GUILayout.Space(3);
            }
        }

        void RunAudit(bool announce)
        {
            if (_auditRoot == null) return;
            _auditBusy = true;
            try
            {
                _auditFindings = MaterialAudit.Scan(_auditRoot);
                _auditMaterialCount = _auditRoot.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                _auditFindings = new List<MaterialAudit.Finding>();
                Toast(Loc.T("The material check hit an error (details in the Console)."), MessageType.Error, 6f);
                return;
            }
            finally { _auditBusy = false; }

            if (announce)
            {
                Learn.Did(LearnEvents.AuditRun);
                int broken = _auditFindings.Count(f => f.severity == MaterialAudit.Severity.Problem);
                int warn = _auditFindings.Count(f => f.severity == MaterialAudit.Severity.Warning);
                if (broken + warn == 0) Toast(Loc.F("Checked {0} materials: nothing broken.", _auditMaterialCount), MessageType.Info, 4f);
                else Toast(Loc.F("Checked {0} materials: {1} broken, {2} worth a look. See ‘Check materials’.", _auditMaterialCount, broken, warn), broken > 0 ? MessageType.Warning : MessageType.Info, 6f);
            }
            Repaint();
        }

        void ApplyAuditFix(MaterialAudit.Finding f)
        {
            if (f == null || f.fix == null) return;
            if (!string.IsNullOrEmpty(f.confirm) && !StudioDialog.Ask(Loc.T("Avistry"), f.confirm, f.fixLabel, Msg.BtnCancel)) return;
            _auditBusy = true;
            try
            {
                Undo.SetCurrentGroupName(f.fixLabel);
                f.fix();
                Toast(Loc.F("Done: {0}", f.fixLabel.TrimEnd('.')) + (f.material != null ? $" ({f.material.name})." : ".") + Loc.T(" (Material changes: Ctrl+Z. Image setting changes: switch back in the image's Inspector.)"), MessageType.Info, 6f);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Toast(Loc.F("Couldn't {0}: {1}", f.fixLabel.ToLowerInvariant(), e.Message), MessageType.Error, 6f);
            }
            finally { _auditBusy = false; }
            _needsRebuild = true;
            RunAudit(false);
        }
    }
}
