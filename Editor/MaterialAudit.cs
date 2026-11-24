using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    public static class MaterialAudit
    {
        public enum Severity { Problem, Warning, Info }

        public class Finding
        {
            public Severity severity;
            public Material material;
            public List<TargetResolver.SlotRef> users = new List<TargetResolver.SlotRef>();
            public string title;
            public string detail;
            public string fixLabel;
            public string fixTooltip;
            public string confirm;
            public Action fix;
            public UnityEngine.Object ping;

            public string UsedBy
            {
                get
                {
                    var names = users.Where(u => u.renderer != null).Select(u => u.renderer.name).Distinct().ToList();
                    if (names.Count == 0) return null;
                    string s = string.Join(", ", names.Take(3));
                    return names.Count > 3 ? Loc.F("{0} (+{1} more)", s, names.Count - 3) : s;
                }
            }
        }

        const string LockedPrefix = "Hidden/Locked/";
        const string ErrorShaderName = "Hidden/InternalErrorShader";
        static readonly string[] ModelExtensions = { ".fbx", ".obj", ".blend", ".dae", ".3ds", ".max", ".ma", ".mb" };


        public static Transform FindAvatarRoot(Renderer r)
        {
            if (r == null) return null;
            var start = r.transform;
            if (s_rootFrom == start && s_rootVersion == s_hierarchyVersion && EditorApplication.timeSinceStartup - s_rootTime < 1.0)
                return s_rootResult;
            if (!s_hooked) { s_hooked = true; EditorApplication.hierarchyChanged += () => s_hierarchyVersion++; }
            var descriptor = DescriptorType;
            Transform topAnimator = null, result = null;
            for (var t = start; t != null; t = t.parent)
            {
                if (descriptor != null && t.GetComponent(descriptor) != null) { result = t; break; }
                if (t.GetComponent<Animator>() != null) topAnimator = t;
            }
            if (result == null) result = topAnimator != null ? topAnimator : start.root;
            s_rootFrom = start; s_rootResult = result; s_rootVersion = s_hierarchyVersion; s_rootTime = EditorApplication.timeSinceStartup;
            return result;
        }

        static Transform s_rootFrom, s_rootResult;
        static int s_hierarchyVersion, s_rootVersion;
        static double s_rootTime;
        static bool s_hooked;

        static Type s_descriptorType;
        static bool s_descriptorResolved;
        static Type DescriptorType
        {
            get
            {
                if (!s_descriptorResolved)
                {
                    s_descriptorResolved = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try { s_descriptorType = asm.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "VRCAvatarDescriptor" && typeof(Component).IsAssignableFrom(x)); }
                        catch (Exception) { }
                        if (s_descriptorType != null) break;
                    }
                }
                return s_descriptorType;
            }
        }


        public static List<Finding> Scan(Transform avatarRoot)
        {
            var findings = new List<Finding>();
            if (avatarRoot == null) return findings;

            var users = new Dictionary<Material, List<TargetResolver.SlotRef>>();
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var slot = new TargetResolver.SlotRef { renderer = r, materialIndex = i };
                    if (mats[i] == null)
                    {
                        if (i >= 1 && r is ParticleSystemRenderer)
                        {
                            var ps = r.GetComponent<ParticleSystem>();
                            if (ps == null || !ps.trails.enabled) continue;
                        }
                        findings.Add(EmptySlot(slot));
                        continue;
                    }
                    if (!users.TryGetValue(mats[i], out var list)) users[mats[i]] = list = new List<TargetResolver.SlotRef>();
                    list.Add(slot);
                }
                var extra = ExtraSlots(r);
                if (extra != null) findings.Add(extra);
            }

            foreach (var kv in users)
            {
                var m = kv.Key;
                int before = findings.Count;
                CheckShader(m, findings);
                CheckMissingTextures(m, findings);
                CheckPlaceholder(m, kv.Value, renderers, findings);
                CheckMainTexture(m, kv.Value, findings);
                for (int i = before; i < findings.Count; i++)
                {
                    findings[i].material = m;
                    findings[i].users = kv.Value;
                    if (findings[i].ping == null) findings[i].ping = m;
                }
            }

            CheckTextureImports(users, findings);

            return findings
                .OrderBy(f => (int)f.severity)
                .ThenBy(f => f.material != null ? f.material.name : "")
                .ToList();
        }


        static Finding EmptySlot(TargetResolver.SlotRef slot)
        {
            var r = slot.renderer;
            return new Finding
            {
                severity = Severity.Problem,
                users = new List<TargetResolver.SlotRef> { slot },
                title = Loc.F("‘{0}’ has an empty slot", r.name),
                detail = Loc.F("‘{0}’ has an empty slot, so that part shows pink or vanishes in game. " +
                               "Drag a material into the empty Element {1} in the part's Materials list.", r.name, slot.materialIndex) +
                         (r.gameObject.activeInHierarchy ? "" : Loc.T(" (This part is switched off now, but it can be turned on in game.)")),
                ping = r,
            };
        }

        static Finding ExtraSlots(Renderer r)
        {
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) return null;
            var mesh = TargetResolver.GetSharedMesh(r);
            if (mesh == null) return null;
            int mats = r.sharedMaterials.Length, parts = mesh.subMeshCount;
            if (mats <= parts) return null;
            return new Finding
            {
                severity = Severity.Info,
                title = Loc.F("‘{0}’ has more materials than parts", r.name),
                detail = Loc.F("‘{0}’ has {1} materials for {2} part(s). The extras draw the last part again, slowing the avatar and sometimes flickering. " +
                               "Set the Materials list size to {2} unless it's an outline trick.", r.name, mats, parts),
                users = Enumerable.Range(parts, mats - parts).Select(i => new TargetResolver.SlotRef { renderer = r, materialIndex = i }).ToList(),
                ping = r,
            };
        }


        static void CheckShader(Material m, List<Finding> findings)
        {
            var s = m.shader;
            string locked = s != null && s.name.StartsWith(LockedPrefix) ? s.name : null;
            var original = OriginalShader(m);
            string originalName = m.GetTag("OriginalShader", false, "");
            bool hasLockTag = !string.IsNullOrEmpty(originalName);

            if (s == null || s.name == ErrorShaderName)
            {
                if (hasLockTag && original != null)
                {
                    findings.Add(new Finding
                    {
                        severity = Severity.Problem,
                        title = Loc.T("Pink: its locked Poiyomi shader is missing"),
                        detail = Loc.F("This material is pink because its locked Poiyomi shader was deleted. " +
                                       "Switching back to normal Poiyomi ({0}) fixes it. Poiyomi relocks it on upload.", originalName),
                        fixLabel = Loc.T("Unlock it"),
                        fixTooltip = Loc.F("Switch the material back to {0} (Ctrl+Z undoes it)", originalName),
                        fix = () => Unlock(m, original),
                    });
                }
                else
                {
                    var family = GuessFamily(m);
                    string what = hasLockTag ? Loc.F("the Poiyomi shader it was locked from ({0}) isn't installed either", originalName)
                                : family == ShaderFamily.Poiyomi ? Loc.T("its Poiyomi shader isn't installed (or is a different version)")
                                : family == ShaderFamily.LilToon ? Loc.T("its lilToon shader isn't installed")
                                : Loc.T("its shader isn't installed");
                    string todo = family == ShaderFamily.LilToon ? Loc.T("Import lilToon (from VCC or BOOTH), then it fixes itself.")
                                : family == ShaderFamily.Poiyomi || hasLockTag ? Loc.T("Import the Poiyomi version it was made with (Poiyomi's site has old ones), then unlock it in the Poiyomi inspector.")
                                : Loc.T("Import the shader package the outfit asks for, or pick another shader at the top of the material's Inspector.");
                    findings.Add(new Finding
                    {
                        severity = Severity.Problem,
                        title = Loc.T("Pink: the shader is missing"),
                        detail = Loc.F("This material is pink: {0}. {1}", what, todo),
                    });
                }
                return;
            }

            bool hasError = ShaderUtil.ShaderHasError(s);
            if (hasError || !s.isSupported)
            {
                string firstError = ShaderUtil.GetShaderMessages(s)
                    .Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    .Select(x => x.message).FirstOrDefault();
                string why = hasError ? Loc.T("its shader has errors") : Loc.T("its shader can't run on this computer's graphics");
                var f = new Finding
                {
                    severity = Severity.Problem,
                    title = Loc.T("Pink: the shader doesn't work"),
                    detail = Loc.F("This material is pink: {0}.", why) + (firstError != null ? Loc.F(" (Unity says: “{0}”)", Trim(firstError, 140)) : ""),
                    ping = s,
                };
                if (locked != null && original != null && !ShaderUtil.ShaderHasError(original) && MaterialWriter.CanUnlockPoiyomi)
                {
                    f.detail += Loc.T(" It's a locked Poiyomi copy, so the button rebuilds it: it unlocks the material and locks it again.");
                    f.fixLabel = Loc.T("Rebuild the shader");
                    f.fixTooltip = Loc.T("Unlock and re-lock this Poiyomi material so Poiyomi makes a fresh locked shader");
                    f.fix = () => Relock(m, original);
                }
                else if (locked != null && original != null)
                {
                    f.detail += Loc.T(" It's a locked Poiyomi copy, so the button switches it back to normal Poiyomi.");
                    f.fixLabel = Loc.T("Unlock it");
                    f.fixTooltip = Loc.F("Switch the material back to {0} (Ctrl+Z undoes it)", original.name);
                    f.fix = () => Unlock(m, original);
                }
                else
                    f.detail += Loc.T(" Reimport the shader folder (right-click it, then Reimport), or pick another shader in the material's Inspector.");
                findings.Add(f);
                return;
            }

            if (MaterialWriter.IsSectionLocked(m))
            {
                findings.Add(new Finding
                {
                    severity = Severity.Warning,
                    title = Loc.T("Stuck on Poiyomi's temporary editing shader"),
                    detail = Loc.T("Poiyomi's temporary editing shader is in use. It disappears when Unity closes and the material turns pink. " +
                                   "The button puts it back on its real shader (the look doesn't change)."),
                    fixLabel = Loc.T("Put back the real shader"),
                    fixTooltip = Loc.T("Switch from the temporary editing shader to the real Poiyomi shader"),
                    fix = () =>
                    {
                        Undo.RecordObject(m, "Restore Poiyomi shader");
                        MaterialWriter.EnsureEditable(new[] { m });
                        EditorUtility.SetDirty(m);
                    },
                });
                return;
            }

            if (locked == null) return;

            string version = OldVersion(originalName) ?? OldVersion(locked);
            if (hasLockTag && original == null)
            {
                string cache = AssetDatabase.GetAssetPath(s);
                findings.Add(new Finding
                {
                    severity = Severity.Warning,
                    title = Loc.T("Locked to a Poiyomi version that isn't installed") + (version != null ? $" ({version})" : ""),
                    detail = Loc.T("It looks fine, but you can't change its settings, because the Poiyomi version it was locked with isn't installed") +
                             (string.IsNullOrEmpty(cache) ? "" : Loc.F(" (its locked copy is in {0})", System.IO.Path.GetDirectoryName(cache)?.Replace('\\', '/'))) +
                             Loc.T(". Don't delete that folder or the material turns pink. " +
                                   "To edit it, import that Poiyomi version, or switch it to the current Poiyomi and redo the settings."),
                    ping = s,
                });
                return;
            }

            if (version != null)
            {
                findings.Add(new Finding
                {
                    severity = Severity.Info,
                    title = Loc.F("Uses an old Poiyomi version ({0})", version),
                    detail = Loc.T("Old Poiyomi version. It works, and updating is optional. To update: unlock it, pick the current Poiyomi shader " +
                                   "in the material's Inspector, and check it still looks right."),
                });
            }
        }

        static Shader OriginalShader(Material m)
        {
            string guid = m.GetTag("OriginalShaderGUID", false, "");
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var byGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (byGuid != null) return byGuid;
            }
            string name = m.GetTag("OriginalShader", false, "");
            return string.IsNullOrEmpty(name) ? null : Shader.Find(name);
        }

        static string OldVersion(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return null;
            var match = Regex.Match(shaderName, @"Old Versions/([0-9][0-9.]*)");
            return match.Success ? match.Groups[1].Value : null;
        }

        static ShaderFamily GuessFamily(Material m)
        {
            var so = new SerializedObject(m);
            var floats = so.FindProperty("m_SavedProperties.m_Floats");
            if (floats == null) return ShaderFamily.Other;
            for (int i = 0; i < floats.arraySize; i++)
            {
                string n = floats.GetArrayElementAtIndex(i).FindPropertyRelative("first")?.stringValue;
                if (n == null) continue;
                if (n == "_lilToonVersion") return ShaderFamily.LilToon;
                if (n == "_ShaderOptimizerEnabled" || n == "shader_master_label" || n.StartsWith("m_start_") || n.StartsWith("s_start_")) return ShaderFamily.Poiyomi;
            }
            return ShaderFamily.Other;
        }

        static void Unlock(Material m, Shader original)
        {
            Undo.RecordObject(m, "Unlock material");
            if (MaterialWriter.CanUnlockPoiyomi) MaterialWriter.UnlockPoiyomi(new[] { m });
            if (m.shader == null || m.shader.name == ErrorShaderName || m.shader.name.StartsWith(LockedPrefix))
                if (original != null) m.shader = original;
            EditorUtility.SetDirty(m);
        }

        static void Relock(Material m, Shader original)
        {
            Unlock(m, original);
            if (m.shader != null && !m.shader.name.StartsWith(LockedPrefix))
                MaterialWriter.LockPoiyomi(new[] { m });
        }


        static void CheckMissingTextures(Material m, List<Finding> findings)
        {
            var missing = new List<string>();
            var so = new SerializedObject(m);
            var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (envs == null) return;
            for (int i = 0; i < envs.arraySize; i++)
            {
                var e = envs.GetArrayElementAtIndex(i);
                var tex = e.FindPropertyRelative("second.m_Texture");
                if (tex == null || tex.objectReferenceValue != null || tex.objectReferenceInstanceIDValue == 0) continue;
                string prop = e.FindPropertyRelative("first")?.stringValue;
                if (!string.IsNullOrEmpty(prop)) missing.Add(prop);
            }
            if (missing.Count == 0) return;

            bool main = missing.Contains("_MainTex");
            var names = missing.Select(p => ShaderProfiles.FriendlyName(p, null)).ToList();
            findings.Add(new Finding
            {
                severity = main ? Severity.Problem : Severity.Warning,
                title = main ? Loc.T("Its main image file is missing") : Loc.F("Links to {0} missing image file{1}", missing.Count, missing.Count == 1 ? "" : "s"),
                detail = Loc.F("The material points to image files that were moved or deleted: {0}. ", string.Join(", ", names)) +
                         (main ? Loc.T("Without the main image the part is a plain colour. ") : Loc.T("If those effects aren't used it looks the same, but the links are broken. ")) +
                         Loc.T("Restore the files (Unity reconnects them), or clear the broken links with the button (Ctrl+Z undoes it)."),
                fixLabel = Loc.T("Clear broken links"),
                fixTooltip = Loc.T("Empty the image slots that point to missing files. No files are deleted."),
                fix = () =>
                {
                    Undo.RecordObject(m, "Clear missing textures");
                    foreach (var p in missing) if (m.HasProperty(p)) m.SetTexture(p, null);
                    var so2 = new SerializedObject(m);
                    var envs2 = so2.FindProperty("m_SavedProperties.m_TexEnvs");
                    for (int i = 0; envs2 != null && i < envs2.arraySize; i++)
                    {
                        var t = envs2.GetArrayElementAtIndex(i).FindPropertyRelative("second.m_Texture");
                        if (t != null && t.objectReferenceValue == null && t.objectReferenceInstanceIDValue != 0) t.objectReferenceValue = null;
                    }
                    so2.ApplyModifiedProperties();
                    EditorUtility.SetDirty(m);
                },
            });
        }

        static void CheckMainTexture(Material m, List<TargetResolver.SlotRef> users, List<Finding> findings)
        {
            if (users.All(u => u.renderer is ParticleSystemRenderer || u.renderer is TrailRenderer || u.renderer is LineRenderer)) return;
            var family = ShaderProfiles.Detect(m.shader);
            if (family == ShaderFamily.Other || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") != null) return;
            if (findings.Any(f => f.material == null && f.title == Loc.T("Its main image file is missing"))) return;
            if (!m.HasProperty("_Color")) return;
            var c = m.GetColor("_Color");
            if (c.r < 0.9f || c.g < 0.9f || c.b < 0.9f) return;
            findings.Add(new Finding
            {
                severity = Severity.Info,
                title = Loc.T("No main image"),
                detail = Loc.T("No main image, so this part is plain white. Fine if intended; otherwise drag an image into Main Texture in the material's Inspector."),
            });
        }

        static Dictionary<Mesh, List<Renderer>> s_meshCache;
        static double s_meshCacheTime = -10;
        static Dictionary<Mesh, List<Renderer>> SceneRenderersByMesh()
        {
            if (s_meshCache != null && EditorApplication.timeSinceStartup - s_meshCacheTime < 1.0 && s_hierarchyVersion == s_meshCacheVersion) return s_meshCache;
            var d = new Dictionary<Mesh, List<Renderer>>();
            foreach (var o in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
            {
                var mesh = TargetResolver.GetSharedMesh(o);
                if (mesh == null) continue;
                if (!d.TryGetValue(mesh, out var l)) d[mesh] = l = new List<Renderer>();
                l.Add(o);
            }
            s_meshCache = d; s_meshCacheTime = EditorApplication.timeSinceStartup; s_meshCacheVersion = s_hierarchyVersion;
            return d;
        }
        static int s_meshCacheVersion;

        static void CheckPlaceholder(Material m, List<TargetResolver.SlotRef> users, Renderer[] avatarRenderers, List<Finding> findings)
        {
            if (users.All(u => u.renderer is ParticleSystemRenderer || u.renderer is TrailRenderer || u.renderer is LineRenderer)) return;
            string path = AssetDatabase.GetAssetPath(m);
            bool embedded = !string.IsNullOrEmpty(path) && ModelExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
            bool builtin = path == "Resources/unity_builtin_extra" && m.name == "Default-Material";
            if (!embedded && !builtin) return;

            var f = new Finding
            {
                severity = Severity.Warning,
                title = Loc.T("Still uses the model's placeholder material"),
                detail = Loc.F("This is a placeholder material from the 3D file ({0}). ", m.shader != null ? m.shader.name : Loc.T("no shader")) +
                         Loc.T("It can't be edited and won't match your toon look. Drag in a Poiyomi or lilToon material."),
            };

            var avatarSet = new HashSet<Renderer>(avatarRenderers);
            var byMesh = SceneRenderersByMesh();
            var plan = new List<KeyValuePair<TargetResolver.SlotRef, Material>>();
            foreach (var u in users)
            {
                var mesh = TargetResolver.GetSharedMesh(u.renderer);
                if (mesh == null) continue;
                Material replacement = null;
                if (!byMesh.TryGetValue(mesh, out var sameMesh)) continue;
                foreach (var other in sameMesh)
                {
                    if (other == null || avatarSet.Contains(other)) continue;
                    var om = other.sharedMaterials;
                    if (u.materialIndex >= om.Length || om[u.materialIndex] == null) continue;
                    string op = AssetDatabase.GetAssetPath(om[u.materialIndex]);
                    if (string.IsNullOrEmpty(op) || !op.EndsWith(".mat")) continue;
                    replacement = om[u.materialIndex];
                    break;
                }
                if (replacement != null) plan.Add(new KeyValuePair<TargetResolver.SlotRef, Material>(u, replacement));
            }
            if (plan.Count > 0)
            {
                var names = string.Join(", ", plan.Select(p => p.Value.name).Distinct());
                f.detail += Loc.F(" Another copy of this part in the scene uses ‘{0}’; the button puts that material on this part too (Ctrl+Z undoes it).", names);
                f.fixLabel = Loc.F("Use ‘{0}’", Trim(names, 30));
                f.fixTooltip = Loc.T("Put the material from the other copy of this part onto this part. Nothing else changes.");
                f.fix = () =>
                {
                    foreach (var p in plan)
                    {
                        var r = p.Key.renderer;
                        if (r == null) continue;
                        Undo.RecordObject(r, "Replace placeholder material");
                        var mats = r.sharedMaterials;
                        if (p.Key.materialIndex < mats.Length && mats[p.Key.materialIndex] == m) mats[p.Key.materialIndex] = p.Value;
                        r.sharedMaterials = mats;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                    }
                };
            }
            findings.Add(f);
        }


        static void CheckTextureImports(Dictionary<Material, List<TargetResolver.SlotRef>> users, List<Finding> findings)
        {
            var seen = new HashSet<string>();
            var crunched = new Dictionary<string, Texture2D>();
            var slotsOf = new Dictionary<Material, List<TextureSlot>>();
            var materialsOf = new Dictionary<string, List<string>>();
            var rolesOf = new Dictionary<string, HashSet<TextureRole>>();
            foreach (var m in users.Keys)
            {
                if (m.shader == null || m.shader.name == ErrorShaderName) continue;
                try { slotsOf[m] = TargetResolver.GetTextureSlots(m); } catch { continue; }
                foreach (var s in slotsOf[m])
                {
                    if (string.IsNullOrEmpty(s.assetPath)) continue;
                    if (!materialsOf.TryGetValue(s.assetPath, out var names)) materialsOf[s.assetPath] = names = new List<string>();
                    if (!names.Contains(m.name)) names.Add(m.name);
                    if (!rolesOf.TryGetValue(s.assetPath, out var roles)) rolesOf[s.assetPath] = roles = new HashSet<TextureRole>();
                    roles.Add(s.role);
                    if (s.mixedRoles) { roles.Add(TextureRole.Color); roles.Add(TextureRole.Mask); }
                }
            }
            foreach (var kv in users)
            {
                var m = kv.Key;
                if (!slotsOf.TryGetValue(m, out var slots)) continue;
                foreach (var slot in slots)
                {
                    if (string.IsNullOrEmpty(slot.assetPath)) continue;
                    var importer = AssetImporter.GetAtPath(slot.assetPath) as TextureImporter;
                    if (importer == null) continue;
                    if (importer.crunchedCompression) crunched[slot.assetPath] = slot.texture;
                    if (slot.featureOff || seen.Contains(slot.assetPath)) continue;
                    if (rolesOf.TryGetValue(slot.assetPath, out var allRoles) && allRoles.Count > 1) continue;

                    var problem = MaterialWriter.ImportProblem(slot, out var fix);
                    if (problem == null) continue;
                    seen.Add(slot.assetPath);
                    var usedBy = materialsOf.TryGetValue(slot.assetPath, out var mn) ? mn : new List<string>();
                    findings.Add(new Finding
                    {
                        severity = Severity.Warning,
                        material = m,
                        users = kv.Value,
                        title = Loc.F("Image ‘{0}’ is read the wrong way", slot.texture.name),
                        detail = Loc.F("Used as “{0}” ({1}). {2} ", slot.displayName, TextureSlot.RoleLabel(slot.role), problem) +
                                 (usedBy.Count > 1
                                     ? Loc.F("The button changes how Unity reads the image (not the picture), for every material that uses it ({0}).", string.Join(", ", usedBy.Take(4)))
                                     : Loc.T("The button changes how Unity reads the image (not the picture), for every material that uses it.")) +
                                 Loc.T(" You can change it back in the image's Inspector."),
                        fixLabel = Loc.T("Fix import setting"),
                        fixTooltip = Loc.T("Change how Unity reads this image to match how the shader uses it"),
                        fix = fix,
                        ping = slot.texture,
                    });
                }
            }

            if (crunched.Count > 0)
            {
                var paths = crunched.Keys.ToList();
                findings.Add(new Finding
                {
                    severity = Severity.Info,
                    title = paths.Count == 1 ? Loc.T("1 image uses Crunch") : Loc.F("{0} images use Crunch", paths.Count),
                    detail = Loc.T("Crunch shrinks the download but smears colour. Turning it off makes the avatar a little bigger. ") +
                             Loc.F("Images: {0}{1}.", string.Join(", ", crunched.Values.Where(t => t != null).Select(t => t.name).Take(6)), paths.Count > 6 ? "…" : ""),
                    fixLabel = Loc.T("Turn off Crunch"),
                    fixTooltip = Loc.F("Turn off Crunch on {0} image(s) and reimport them (can take a while)", paths.Count),
                    confirm = Loc.F("{0} image(s) use Crunch. It shrinks downloads but smears colour. Turning it off makes the avatar a little bigger. Unity reimports them (can take a minute). Undo it in each image's Inspector.", paths.Count),
                    fix = () =>
                    {
                        try
                        {
                            AssetDatabase.StartAssetEditing();
                            foreach (var p in paths)
                            {
                                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                                if (imp == null || !imp.crunchedCompression) continue;
                                imp.crunchedCompression = false;
                                imp.SaveAndReimport();
                            }
                        }
                        finally { AssetDatabase.StopAssetEditing(); }
                    },
                    ping = crunched.Values.FirstOrDefault(t => t != null),
                });
            }
        }

        static string Trim(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
