using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Quick material (end of the Recolour tab): tint, glow, rim, matcap, shadow, outline, metal and glitter for Poiyomi and
    /// lilToon. It writes only to a material Avistry made (Exporter.IsToolCopy); for anything else it shows the values and
    /// offers "Make an editable copy". Edits are Undo steps on the material and do not touch the texture, so they are not MarkEdited.
    /// </summary>
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _openQuickMaterial;

        // lilToon rebuilds its shaders when a feature is first used in a project; ask once per feature and editor session.
        static readonly HashSet<string> s_lilToonAsked = new HashSet<string>();
        const string QuickCancelled = "Cancelled.";

        // The control list is cheap to build but OnGUI asks for it on every event, so keep it until the material or shader changes.
        [System.NonSerialized] Material _qmMat;
        [System.NonSerialized] Shader _qmShader;
        [System.NonSerialized] List<QControl> _qmControls;
        [System.NonSerialized] string _qmLang;

        List<QControl> QuickControls(Material m)
        {
            if (_qmControls == null || _qmMat != m || _qmShader != m.shader || _qmLang != Loc.Current)
            {
                _qmLang = Loc.Current;
                _qmControls = QuickMaterialProfiles.For(m);
                _qmMat = m;
                _qmShader = m.shader;
            }
            return _qmControls;
        }

        /// <summary>An unfinished recolour owns this material's colours: two writers would fight over them.</summary>
        bool QuickRecolourBusy => _material != null && (RecolourChanged || _rcNeedsFinalize) && _rcEntries.Any(e => e.material == _material);

        bool QuickEditable(out string why)
        {
            why = null;
            if (_material == null) why = Loc.T("No material.");
            else if (QuickRecolourBusy) why = Loc.T("Finish the recolour above first (Keep changes or Reset).");
            else if (!Exporter.IsToolCopy(_material)) why = Loc.T("This is your own material, so Avistry won't change it. Make an editable copy to use these controls.");
            return why == null;
        }

        void DrawQuickMaterial()
        {
            var m = _material;
            if (m == null) return;
            var family = ShaderProfiles.Detect(m.shader);
            if (family == ShaderFamily.Other)
            {
                StudioGUI.BeginGroup(Loc.T("Quick material"));
                GUILayout.Label(Loc.T("Quick material works with Poiyomi and lilToon."), StudioGUI.Hint);
                StudioGUI.EndGroup();
                return;
            }
            var controls = QuickControls(m);
            string summary = Loc.F("{0} · {1} controls", ShaderProfiles.FamilyLabel(family), QuickMaterialProfiles.GroupCount(controls));
            if (!StudioGUI.BeginFoldGroup(ref _openQuickMaterial, Loc.T("Quick material"), summary, null,
                    Loc.T("Tint, glow, rim light, matcap, shadow, outline, metal and glitter. Click to fold."), "material"))
            {
                StudioGUI.EndGroup();
                return;
            }

            bool editable = QuickEditable(out string why);
            bool locked = family == ShaderFamily.Poiyomi && MaterialWriter.IsPoiyomiLocked(m);
            GUILayout.Label(Loc.T("Hue, saturation and brightness of the whole material are in the Recolour controls above."), StudioGUI.Hint);

            if (!editable)
            {
                GUILayout.Label(why, StudioGUI.CheckLabel);
                if (!QuickRecolourBusy && GUILayout.Button(new GUIContent(Loc.T("Make an editable copy"), Loc.T("Saves a copy of this material next to it and puts it on the part. Ctrl+Z puts the original back.")), GUILayout.Height(24)))
                    EditorApplication.delayCall += () => { var err = MakeEditableCopy(); if (err != null && err != QuickCancelled && !err.EndsWith(".mat")) Toast(err, MessageType.Warning); };
            }
            if (locked)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Loc.T("This material is locked by Poiyomi. Only settings marked Animated can change. Unlock it in the Poiyomi inspector, or make an editable copy."), StudioGUI.CheckLabel);
                    StudioGUI.Info("shader-lock");
                }
            }

            using (new EditorGUI.DisabledScope(!editable))
            {
                foreach (var c in controls)
                {
                    if (editable && locked && (c.kind == QKind.Toggle || !QuickMaterialProfiles.IsAnimated(m, c))) continue;
                    DrawQuickControl(m, c);
                }
                if (editable && !locked && m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") == null && controls.Any(c => c.id == "emission" && (bool)QuickMaterialProfiles.Read(m, c)))
                    GUILayout.Label(Loc.T("This glow has no map, so it covers the whole part. Pick a glow colour and lower the strength if it is too bright."), StudioGUI.Hint);
            }
            StudioGUI.EndGroup();
        }

        void DrawQuickControl(Material m, QControl c)
        {
            if (!QuickMaterialProfiles.Applies(m, c))
            {
                GUILayout.Label(c.gateNote, StudioGUI.Hint);
                return;
            }
            var content = new GUIContent(c.label, c.tip);
            bool dependent = c.toggle != null;
            using (new EditorGUI.DisabledScope(!QuickMaterialProfiles.IsOn(m, c)))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (dependent) GUILayout.Space(14);
                EditorGUI.BeginChangeCheck();
                object value = null;
                switch (c.kind)
                {
                    case QKind.Toggle:
                        value = EditorGUILayout.ToggleLeft(content, (bool)QuickMaterialProfiles.Read(m, c));
                        break;
                    case QKind.Range:
                        value = EditorGUILayout.Slider(content, (float)QuickMaterialProfiles.Read(m, c), c.min, c.max);
                        break;
                    default:
                        value = EditorGUILayout.ColorField(content, (Color)QuickMaterialProfiles.Read(m, c), true, c.kind == QKind.Colour, c.kind == QKind.ColourHdr || c.hdr);
                        break;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    if (NeedsLilToonAsk(m, c, value))
                    {
                        // A modal dialog in the middle of the layout pass breaks the IMGUI groups, so ask after this event.
                        var cc = c;
                        EditorApplication.delayCall += () =>
                        {
                            string e = SetQuick(cc, value);
                            if (e != null && e != QuickCancelled) Toast(e, MessageType.Warning);
                        };
                    }
                    else
                    {
                        string err = SetQuick(c, value);
                        if (err != null && err != QuickCancelled) Toast(err, MessageType.Warning);
                    }
                }
            }
        }

        bool NeedsLilToonAsk(Material m, QControl c, object value) =>
            c.kind == QKind.Toggle && ToBool(value) && ShaderProfiles.Detect(m.shader) == ShaderFamily.LilToon && !s_lilToonAsked.Contains(c.group);

        /// <summary>
        /// Changes one control on the open material, like the Inspector would: one Undo step per change, Poiyomi keywords and lilToon
        /// settings kept in line. Returns null, or why nothing changed.
        /// </summary>
        string SetQuick(QControl c, object value)
        {
            var m = _material;
            if (c == null) return Loc.T("No such control.");
            if (!QuickEditable(out string why)) return why;
            if (!m.HasProperty(c.prop)) return Loc.F("This material has no {0} setting.", c.label.ToLowerInvariant());
            var family = ShaderProfiles.Detect(m.shader);
            bool isToggle = c.kind == QKind.Toggle;
            if (family == ShaderFamily.Poiyomi && MaterialWriter.IsPoiyomiLocked(m) && (isToggle || !QuickMaterialProfiles.IsAnimated(m, c)))
                return Loc.T("This material is locked by Poiyomi, so that setting can't change. Unlock it in the Poiyomi inspector.");

            bool on = isToggle && ToBool(value);
            // Asked before anything changes, so Cancel leaves the material and the Undo stack as they were.
            if (on && family == ShaderFamily.LilToon && !s_lilToonAsked.Contains(c.group))
            {
                if (!StudioDialog.Ask(Loc.T("lilToon setting"), Loc.T("lilToon can rebuild its shaders to add this effect, and again for each new effect. It can take a minute."), Loc.T("Continue"), Loc.T("Cancel"))) return QuickCancelled;
                s_lilToonAsked.Add(c.group);
            }

            // Recorded first so Ctrl+Z also undoes a shader swap that EnsureEditable makes on a section-locked Poiyomi material.
            Undo.RecordObject(m, "Change material: " + c.label);
            MaterialWriter.EnsureEditable(new[] { m });
            switch (c.kind)
            {
                case QKind.Toggle:
                    m.SetFloat(c.prop, on ? c.onValue : 0f);
                    if (on && c.onSet != null && m.HasProperty(c.onSet) && m.GetFloat(c.onSet) <= 0f) m.SetFloat(c.onSet, c.onSetValue);
                    break;
                case QKind.Range:
                    float v = System.Convert.ToSingle(value);
                    m.SetFloat(c.prop, Mathf.Clamp(c.invert ? c.min + c.max - v : v, c.min, c.max));
                    break;
                default:
                    var col = (Color)value;
                    if (c.kind == QKind.ColourHdr) col.a = m.GetColor(c.prop).a; // the picker has no alpha here; keep the shader's own
                    m.SetColor(c.prop, col);
                    break;
            }
            if (isToggle && family == ShaderFamily.Poiyomi) MaterialWriter.FixPoiyomiKeywords(new[] { m });
            // lilToon needs its mirrors and feature settings after a switch or the main tint; plain slider and colour drags skip it so they stay light.
            if (family == ShaderFamily.LilToon && (isToggle || c.id == "tint")) MaterialWriter.AfterLilToonEdit(new[] { m });
            // An open recolour captured the old colours as its baseline and would put them back on its next Apply or Reset.
            int ri = _rcEntries.FindIndex(e => e.material == m);
            if (ri >= 0)
            {
                var fresh = Recolour.Capture(m);
                fresh.unlockedByUs = _rcEntries[ri].unlockedByUs;
                _rcEntries[ri] = fresh;
            }
            EditorUtility.SetDirty(m);
            SceneView.RepaintAll();
            Repaint();
            return null;
        }

        static bool ToBool(object v) => v is bool b ? b : System.Convert.ToSingle(v) > 0.5f;

        /// <summary>
        /// Saves a copy of the open material next to it (the same naming as a save) and puts it on the parts that used it. The layers,
        /// selection and brush stay: the rebuild afterwards only looks at the new material. Returns the copy's path, or why nothing was made.
        /// </summary>
        string MakeEditableCopy()
        {
            var m = _material;
            if (m == null) return Loc.T("No material.");
            if (QuickRecolourBusy) return Loc.T("Finish the recolour above first (Keep changes or Reset).");
            if (Exporter.IsToolCopy(m)) return Loc.T("This material is already an Avistry copy.");
            var users = _users.Count > 0 ? new List<TargetResolver.SlotRef>(_users) : new List<TargetResolver.SlotRef> { new TargetResolver.SlotRef { renderer = _renderer, materialIndex = _materialIndex } };
            int parts = users.Where(u => u.renderer != null).Select(u => u.renderer).Distinct().Count();
            string name = Path.GetFileName(Exporter.FreeMaterialPath(m, false));
            if (!StudioDialog.Ask(Loc.T("Make an editable copy"),
                    Loc.F("Avistry makes {0} next to the original and puts it on {1} part(s). The original stays as it is. Ctrl+Z puts the original back.", name, parts), Loc.T("Make copy"), Loc.T("Cancel")))
                return "Cancelled.";

            string path = Exporter.CopyMaterial(m, users);
            if (path == null) return Loc.T("The copy could not be made.");
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            // A locked Poiyomi original gives a locked copy: unlock the copy only (it saved as an asset first, which the optimizer needs).
            if (copy != null && ShaderProfiles.Detect(copy.shader) == ShaderFamily.Poiyomi && MaterialWriter.IsPoiyomiLocked(copy))
            {
                ListOriginalAsLockedUser(copy, m);
                if (!MaterialWriter.UnlockPoiyomi(new[] { copy }))
                    Toast(Loc.T("The copy is still locked. Unlock it in the Poiyomi inspector to change more than the Animated settings."), MessageType.Warning);
            }
            _needsRebuild = true;
            Repaint();
            return path;
        }

        /// <summary>
        /// Poiyomi's unlock deletes the locked shader folder unless the material's AllLockedGUIDS tag names another material that
        /// still uses it. A material locked by an older Poiyomi has no such tag, so the copy would delete the original's shader.
        /// Naming the original there keeps the folder.
        /// </summary>
        static void ListOriginalAsLockedUser(Material copy, Material original)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
            if (string.IsNullOrEmpty(guid)) return;
            string tag = copy.GetTag("AllLockedGUIDS", false, "");
            if (tag.Split(',').Contains(guid)) return;
            copy.SetOverrideTag("AllLockedGUIDS", tag.Length == 0 ? guid : tag + "," + guid);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
        }
    }
}
