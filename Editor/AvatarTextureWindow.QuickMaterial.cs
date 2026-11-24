using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        [SerializeField] bool _openQuickMaterial;

        static readonly HashSet<string> s_lilToonAsked = new HashSet<string>();
        const string QuickCancelled = "Cancelled.";

        [System.NonSerialized] Material _qmMat;
        [System.NonSerialized] Shader _qmShader;
        [System.NonSerialized] List<QControl> _qmControls;
        [System.NonSerialized] string _qmLang;
        [System.NonSerialized] Material _animatedMaterial;
        [System.NonSerialized] Shader _animatedShader;
        [System.NonSerialized] List<PoiyomiAnimationSettings.Setting> _animatedSettings;
        [System.NonSerialized] double _animatedReadAt;
        bool _openPoiyomiAnimatedSettings;

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

        bool QuickRecolourBusy => _material != null && (RecolourChanged || _rcNeedsFinalize) && _rcEntries.Any(e => e.material == _material);

        bool QuickEditable(out string why)
        {
            why = null;
            if (_material == null) why = Loc.T("No material.");
            else if (QuickRecolourBusy) why = Loc.T("Finish the recolour above first (Keep changes or Reset).");
            else if (!Exporter.IsToolCopy(_material)) why = Loc.T("This is your own material, so Avistry won't change it. Make an editable copy to use these controls.");
            return why == null;
        }

        bool QuickTargetStillMatches(Renderer part, int index, Material material, Shader shader)
        {
            if (_needsRebuild || part == null || part != _renderer || index != _materialIndex || material == null ||
                material != _material || shader != material.shader) return false;
            var materials = part.sharedMaterials;
            return index >= 0 && index < materials.Length && materials[index] == material;
        }

        string QuickTargetChanged()
        {
            _needsRebuild = true;
            Repaint();
            return Loc.T("The material changed. Check the material in Pick and try again.");
        }

        EditorApplication.CallbackFunction OnCurrentQuickTarget(System.Func<string> action)
        {
            var part = _renderer;
            int index = _materialIndex;
            var material = _material;
            var shader = material.shader;
            return () =>
            {
                if (this == null) return;
                string error = QuickTargetStillMatches(part, index, material, shader) ? action() : QuickTargetChanged();
                if (error != null && error != QuickCancelled) Toast(error, MessageType.Warning);
            };
        }

        void DrawQuickMaterial()
        {
            var m = _material;
            if (m == null || m.shader == null) return;
            var family = ShaderProfiles.Detect(m.shader);
            bool poiyomi = family == ShaderFamily.Poiyomi;
            bool locked = poiyomi && MaterialWriter.IsPoiyomiLocked(m);
            var controls = QuickControls(m);
            string shaderName = family == ShaderFamily.Other ? m.shader.name : ShaderProfiles.FamilyLabel(family);
            string summary = Loc.F("{0} · {1} controls", shaderName, QuickMaterialProfiles.GroupCount(controls));
            string tooltip = family == ShaderFamily.Other ? null : Loc.T("Tint, glow, rim light, matcap, shadow, outline, metal and glitter.");
            if (!StudioGUI.BeginFoldGroup(ref _openQuickMaterial, Loc.T("Quick material"), summary, null, tooltip, "material"))
            {
                StudioGUI.EndGroup();
                return;
            }

            if (controls.Count == 0)
            {
                GUILayout.Label(Loc.T("This shader has no settings Quick material can edit. Open the material in the Inspector to edit it."), StudioGUI.Hint);
                if (poiyomi) DrawPoiyomiAnimatedSettings(m, locked);
                DrawOpenQuickMaterialInspector(m);
                StudioGUI.EndGroup();
                return;
            }
            bool editable = QuickEditable(out string why);
            if (family != ShaderFamily.Other)
                GUILayout.Label(Loc.T("Hue, saturation and brightness of the whole material are in the Recolour controls above."), StudioGUI.Hint);

            if (!editable)
            {
                GUILayout.Label(why, StudioGUI.CheckLabel);
                if (!QuickRecolourBusy && GUILayout.Button(new GUIContent(Loc.T("Make an editable copy"), Loc.T("Saves a copy in Assets/Avistry/Output and puts it on the part. Ctrl+Z puts the original back.")), GUILayout.Height(24)))
                    EditorApplication.delayCall += OnCurrentQuickTarget(() =>
                    {
                        string result = MakeEditableCopy();
                        return result != null && result.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase) ? null : result;
                    });
            }
            if (locked)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(editable ? Loc.T("This copy is locked by Poiyomi. Unlock it in the Inspector to change settings that aren't marked Animated.")
                        : Loc.T("This material is locked by Poiyomi. Only settings marked Animated can change. Unlock it in the Poiyomi inspector, or make an editable copy."), StudioGUI.CheckLabel);
                    StudioGUI.Info("shader-lock");
                }
            }
            if (poiyomi) DrawPoiyomiAnimatedSettings(m, locked);
            if (locked) DrawOpenQuickMaterialInspector(m);

            using (new EditorGUI.DisabledScope(!editable))
            {
                foreach (var c in controls)
                {
                    if (editable && locked && (c.kind == QKind.Toggle || !QuickMaterialProfiles.IsAnimated(m, c))) continue;
                    DrawQuickControl(m, c);
                }
                if (editable && !locked && m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") == null && controls.Any(c => c.id == "emission" && (bool)QuickMaterialProfiles.Read(m, c)))
                    GUILayout.Label(family == ShaderFamily.Other
                        ? Loc.T("Without a glow map, the whole part glows. Open Glow colour to change its brightness.")
                        : Loc.T("This glow has no map, so it covers the whole part. Pick a glow colour and lower the strength if it is too bright."), StudioGUI.Hint);
            }
            StudioGUI.EndGroup();
        }

        void DrawPoiyomiAnimatedSettings(Material material, bool locked)
        {
            double now = EditorApplication.timeSinceStartup;
            if (_animatedSettings == null || _animatedMaterial != material || _animatedShader != material.shader ||
                Event.current.type == EventType.Layout && now - _animatedReadAt > 1.0)
            {
                _animatedSettings = locked ? PoiyomiAnimationSettings.Read(material) : PoiyomiAnimationSettings.ReadUnlocked(material);
                _animatedMaterial = material;
                _animatedShader = material.shader;
                _animatedReadAt = now;
            }
            if (_animatedSettings.Count == 0) return;

            GUILayout.Label(locked ? Loc.T("Animations can still change settings marked Animated in Poiyomi.")
                : Loc.T("These marks let animations change settings after the material is locked."), StudioGUI.CheckLabel);
            _openPoiyomiAnimatedSettings = StudioGUI.Foldout(_openPoiyomiAnimatedSettings,
                new GUIContent(Loc.F("Settings marked Animated ({0})", _animatedSettings.Count)));
            StudioGUI.Count("recolour.poiyomiAnimated", GUILayoutUtility.GetLastRect());
            if (!_openPoiyomiAnimatedSettings) return;

            if (locked)
            {
                foreach (var setting in _animatedSettings) GUILayout.Label(PoiyomiSettingContent(setting), StudioGUI.Hint);
                GUILayout.Label(Loc.T("Clearing a mark stops animation toggles and sliders that depend on it while the material is locked."), StudioGUI.Hint);
                GUILayout.Label(Loc.T("Unlock an editable copy in the Poiyomi Inspector, then clear the mark here."), StudioGUI.Hint);
                return;
            }

            bool editable = CanClearPoiyomiAnimationMark(material, out var reason);
            if (!editable && reason != null) GUILayout.Label(reason, StudioGUI.Hint);
            var clear = new GUIContent(Exporter.IsVerifiedPoiyomiCopy(material) ? Loc.T("Clear mark") : Loc.T("Clear on copy"),
                Loc.T("Clear only this setting’s Animated mark. The change takes effect after locking."));
            float buttonWidth = EditorStyles.miniButton.CalcSize(clear).x + 6f;
            foreach (var setting in _animatedSettings)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(PoiyomiSettingContent(setting), StudioGUI.Hint, GUILayout.ExpandWidth(true));
                    using (new EditorGUI.DisabledScope(!editable))
                    {
                        bool clicked = GUILayout.Button(clear, EditorStyles.miniButton, GUILayout.Width(buttonWidth));
                        StudioGUI.Count("recolour.poiyomiClearAnimation", GUILayoutUtility.GetLastRect());
                        if (clicked) EditorApplication.delayCall += OnCurrentQuickTarget(() => ClearPoiyomiAnimationMark(setting));
                    }
                }
            }
        }

        static GUIContent PoiyomiSettingContent(PoiyomiAnimationSettings.Setting setting)
        {
            string label = setting.Renamed ? Loc.F("{0} (renamed)", setting.Label) : setting.Label;
            return new GUIContent(label, $"{label}\n{setting.Property}");
        }

        bool CanClearPoiyomiAnimationMark(Material material, out string reason)
        {
            reason = null;
            if (!PoiyomiAnimationSettings.HasEditableShader(material))
                reason = Loc.T("Unlock the material in the Poiyomi Inspector to clear an Animated mark.");
            else if (material.parent != null)
                reason = Loc.T("This material inherits settings. Use a separate material to change its Animated marks.");
            else if (QuickRecolourBusy)
                reason = Loc.T("Finish the recolour above first (Keep changes or Reset).");
            return reason == null;
        }

        string ClearPoiyomiAnimationMark(PoiyomiAnimationSettings.Setting setting)
        {
            var material = _material;
            if (!CanClearPoiyomiAnimationMark(material, out var reason)) return reason;
            var part = _renderer;
            int index = _materialIndex;
            var shader = material.shader;
            if (!QuickTargetStillMatches(part, index, material, shader)) return QuickTargetChanged();
            if (!PoiyomiAnimationSettings.HasMark(material, setting))
                return Loc.T("This mark changed. Open Animated settings again.");

            bool editCopy = Exporter.IsVerifiedPoiyomiCopy(material);
            var root = TargetResolver.AvatarRoot(part);
            var users = editCopy ? null : TargetResolver.FindMaterialUsers(root, material);
            bool StillMatches() => QuickTargetStillMatches(part, index, material, shader) &&
                CanClearPoiyomiAnimationMark(material, out _) && PoiyomiAnimationSettings.HasMark(material, setting) &&
                Exporter.IsVerifiedPoiyomiCopy(material) == editCopy &&
                (editCopy || root == TargetResolver.AvatarRoot(part) && users.SequenceEqual(TargetResolver.FindMaterialUsers(root, material)));

            string prompt = Loc.F("Clear the Animated mark for ‘{0}’? Lock the material again in Poiyomi to apply this change.", setting.Label) +
                "\n\n" + Loc.T("Animation toggles or sliders that use this mark may stop working. Some shared shader settings can still animate.") +
                "\n\n" + (editCopy ? Loc.T("The change applies to every part that uses this copy.")
                    : Loc.T("Avistry will save a copy and assign it to the parts using this material on the current avatar."));
            string action = Loc.T("Clear Animated mark");
            if (!StudioDialog.Ask(action, prompt, editCopy ? Loc.T("Clear mark") : Loc.T("Clear on copy"), Msg.BtnCancel)) return QuickCancelled;
            if (!StillMatches()) return QuickTargetChanged();

            if (editCopy)
            {
                Undo.FlushUndoRecordObjects();
                if (!StillMatches()) return QuickTargetChanged();
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(action);
                Undo.RecordObject(material, action);
                PoiyomiAnimationSettings.ClearMark(material, setting);
                EditorUtility.SetDirty(material);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
            }
            else
            {
                bool ReadyToEdit(Material copy) => copy != null && copy.shader == shader && copy.parent == null &&
                    PoiyomiAnimationSettings.HasEditableShader(copy);
                bool prepared = false;
                string path = Exporter.CopyMaterial(material, users, StillMatches, copy =>
                {
                    if (!ReadyToEdit(copy) || !PoiyomiAnimationSettings.HasMark(copy, setting)) return;
                    PoiyomiAnimationSettings.ClearMark(copy, setting);
                    EditorUtility.SetDirty(copy);
                    prepared = true;
                }, validatePreparedCopy: copy => prepared && ReadyToEdit(copy) &&
                    Exporter.IsVerifiedPoiyomiCopy(copy) && !PoiyomiAnimationSettings.IsMarked(copy, setting.Property));
                if (path == null) return StillMatches() ? Loc.T("The copy could not be made.") : QuickTargetChanged();
                _needsRebuild = true;
            }

            _animatedSettings = null;
            Toast(Loc.T("Animated mark cleared. Lock the material again in Poiyomi to apply the change."), MessageType.Info);
            Repaint();
            return null;
        }

        static void DrawOpenQuickMaterialInspector(Material m)
        {
            if (!GUILayout.Button(Loc.T("Show the material"), EditorStyles.miniButton)) return;
            Selection.activeObject = m;
            EditorGUIUtility.PingObject(m);
        }

        void DrawQuickControl(Material m, QControl c)
        {
            if (!QuickMaterialProfiles.Applies(m, c))
            {
                GUILayout.Label(c.gateNote, StudioGUI.Hint);
                return;
            }
            var content = new GUIContent(c.label, c.tip);
            bool dependent = c.toggle != null || c.toggleKeyword != null;
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
                        EditorApplication.delayCall += OnCurrentQuickTarget(() => SetQuick(c, value));
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

        string SetQuick(QControl c, object value)
        {
            var m = _material;
            if (c == null) return Loc.T("No such control.");
            if (!QuickEditable(out string why)) return why;
            var part = _renderer;
            int index = _materialIndex;
            var shader = m.shader;
            if (!QuickTargetStillMatches(part, index, m, shader)) return QuickTargetChanged();
            if (!m.HasProperty(c.prop)) return Loc.F("This material has no {0} setting.", c.label.ToLowerInvariant());
            if (!QuickMaterialProfiles.Applies(m, c)) return c.gateNote;
            if (!QuickMaterialProfiles.IsOn(m, c)) return Loc.T("Turn on this effect before changing its settings.");
            var family = ShaderProfiles.Detect(m.shader);
            bool isToggle = c.kind == QKind.Toggle;
            if (family == ShaderFamily.Poiyomi && MaterialWriter.IsPoiyomiLocked(m) && (isToggle || !QuickMaterialProfiles.IsAnimated(m, c)))
                return Loc.T("This material is locked by Poiyomi, so that setting can't change. Unlock it in the Poiyomi inspector.");

            bool on = isToggle && ToBool(value);
            if (on && family == ShaderFamily.LilToon && !s_lilToonAsked.Contains(c.group))
            {
                if (!StudioDialog.Ask(Loc.T("lilToon setting"), Loc.T("lilToon can rebuild its shaders to add this effect, and again for each new effect. It can take a minute."), Loc.T("Continue"), Loc.T("Cancel"))) return QuickCancelled;
                if (!QuickTargetStillMatches(part, index, m, shader)) return QuickTargetChanged();
                if (!QuickEditable(out why)) return why;
                s_lilToonAsked.Add(c.group);
            }

            Undo.RecordObject(m, "Change material: " + c.label);
            MaterialWriter.EnsureEditable(new[] { m });
            switch (c.kind)
            {
                case QKind.Toggle:
                    if (c.keyword == null) m.SetFloat(c.prop, on ? c.onValue : 0f);
                    else SetUnityGlow(m, c, on);
                    if (on && c.onSet != null && m.HasProperty(c.onSet) && m.GetFloat(c.onSet) <= 0f) m.SetFloat(c.onSet, c.onSetValue);
                    break;
                case QKind.Range:
                    float v = System.Convert.ToSingle(value);
                    m.SetFloat(c.prop, Mathf.Clamp(c.invert ? c.min + c.max - v : v, c.min, c.max));
                    break;
                default:
                    var col = (Color)value;
                    if (c.kind == QKind.ColourHdr) col.a = m.GetColor(c.prop).a;
                    m.SetColor(c.prop, col);
                    break;
            }
            if (isToggle && family == ShaderFamily.Poiyomi) MaterialWriter.FixPoiyomiKeywords(new[] { m });
            if (family == ShaderFamily.LilToon && (isToggle || c.id == "tint")) MaterialWriter.AfterLilToonEdit(new[] { m });
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

        static void SetUnityGlow(Material m, QControl c, bool on)
        {
            if (on)
            {
                var colour = m.GetColor(c.prop);
                if (colour.maxColorComponent <= 0f) m.SetColor(c.prop, new Color(1f, 1f, 1f, colour.a));
                m.EnableKeyword(c.keyword);
                m.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            else
            {
                m.DisableKeyword(c.keyword);
                m.globalIlluminationFlags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        string MakeEditableCopy()
        {
            var m = _material;
            if (m == null) return Loc.T("No material.");
            if (QuickRecolourBusy) return Loc.T("Finish the recolour above first (Keep changes or Reset).");
            if (Exporter.IsToolCopy(m)) return Loc.T("This material is already an Avistry copy.");
            var part = _renderer;
            int index = _materialIndex;
            var shader = m.shader;
            if (!QuickTargetStillMatches(part, index, m, shader)) return QuickTargetChanged();
            var users = TargetResolver.FindMaterialUsers(TargetResolver.AvatarRoot(part), m);
            bool CanAssign() => QuickTargetStillMatches(part, index, m, shader) && !QuickRecolourBusy &&
                users.SequenceEqual(TargetResolver.FindMaterialUsers(TargetResolver.AvatarRoot(part), m));
            int parts = users.Where(u => u.renderer != null).Select(u => u.renderer).Distinct().Count();
            string name = Path.GetFileName(Exporter.FreeMaterialPath(m, false));
            if (!StudioDialog.Ask(Loc.T("Make an editable copy"),
                    Loc.F("Avistry saves {0} in Assets/Avistry/Output and puts it on {1} part(s). Ctrl+Z puts the original back.", name, parts), Loc.T("Make copy"), Loc.T("Cancel")))
                return QuickCancelled;

            string path = Exporter.CopyMaterial(m, users, CanAssign);
            if (path == null) return CanAssign() ? Loc.T("The copy could not be made.") : QuickTargetChanged();
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
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
