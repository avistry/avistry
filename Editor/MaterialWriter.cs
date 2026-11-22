using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// Writes to Poiyomi / lilToon materials the way their own editors do, so edits don't silently fail:
    /// - Poiyomi: locked materials bake every non-texture value into the shader, so colour/slider changes do nothing
    ///   until the material is unlocked (Thry ShaderOptimizer). Keywords must follow feature toggles (Thry FixKeywords).
    /// - lilToon: _MainTex/_Color have hidden mirrors (_BaseMap, _BaseColorMap, _BaseColor); multi shaders need
    ///   SetupMultiMaterial; features used by a material must be enabled in lilToonSetting or they're compiled out.
    /// Everything is found by reflection, so the tool still compiles in projects without these shaders.
    /// </summary>
    public static class MaterialWriter
    {

        static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        static Type FindType(string fullName)
        {
            if (TypeCache.TryGetValue(fullName, out var t)) return t;
            t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(x => x != null);
            TypeCache[fullName] = t;
            return t;
        }

        const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static readonly Dictionary<(Type, string, int), MethodInfo> MethodCache = new Dictionary<(Type, string, int), MethodInfo>();

        static MethodInfo FindMethod(string typeName, string method, Func<ParameterInfo[], bool> match = null)
        {
            var t = FindType(typeName);
            if (t == null) return null;
            // Every caller passes one fixed predicate per (type, name), so the third key part only separates "no filter".
            var key = (t, method, match == null ? -1 : 0);
            if (MethodCache.TryGetValue(key, out var mi)) return mi;
            mi = t.GetMethods(AnyStatic).FirstOrDefault(m => m.Name == method && (match == null || match(m.GetParameters())));
            MethodCache[key] = mi;
            return mi;
        }


        const string SectionPrefix = "Hidden/SectionLocked/";

        /// <summary>
        /// Poiyomi's editor swaps in a temporary "Hidden/SectionLocked/…" shader while a material is being edited. Colours
        /// and sliders stay live on it, but toggles and dropdowns are baked in, and it only exists in memory.
        /// </summary>
        public static bool IsSectionLocked(Material m) => m != null && m.shader != null && m.shader.name.StartsWith(SectionPrefix);

        /// <summary>The real shader behind a section-locked one (or the shader itself).</summary>
        public static Shader SourceShader(Shader s)
        {
            if (s == null || !s.name.StartsWith(SectionPrefix)) return s;
            var mi = FindMethod("Thry.ThryEditor.SectionLock", "GetSourceShader", p => p.Length == 1 && p[0].ParameterType == typeof(Shader));
            try { return (mi?.Invoke(null, new object[] { s }) as Shader) ?? s; }
            catch { return s; }
        }

        /// <summary>
        /// Puts section-locked materials back on their real shader before we change toggles or copy them
        /// (a copy saved with an in-memory shader would come out pink). Poiyomi re-applies its section lock
        /// the next time the material is edited in its inspector.
        /// </summary>
        public static int EnsureEditable(IEnumerable<Material> materials)
        {
            int reverted = 0;
            var revert = FindMethod("Thry.ThryEditor.SectionLock", "Revert", p => p.Length == 1 && p[0].ParameterType == typeof(Material));
            foreach (var m in materials)
            {
                if (!IsSectionLocked(m)) continue;
                try
                {
                    if (revert != null && (bool)revert.Invoke(null, new object[] { m })) { reverted++; continue; }
                }
                catch (Exception e) { Debug.LogWarning("[Avistry] Poiyomi section-lock revert failed: " + e.Message); }
                // Fallback: point the material back at its source shader ourselves.
                var src = SourceShader(m.shader);
                if (src != null && src != m.shader) { m.shader = src; reverted++; }
            }
            return reverted;
        }


        public static bool IsPoiyomiLocked(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (ShaderProfiles.Detect(m.shader) != ShaderFamily.Poiyomi) return false;
            var mi = FindMethod(ThryOld, "IsMaterialLocked", p => p.Length == 1) ?? FindMethod(ThryNew, "IsMaterialLocked", p => p.Length == 1);
            if (mi != null)
            {
                try { return (bool)mi.Invoke(null, new object[] { m }); } catch { /* fall back to the name */ }
            }
            return m.shader.name.StartsWith("Hidden/Locked/");
        }

        // Older Poiyomi keeps the optimizer inside ThryEditor; Poiyomi 9.1 moved it to Thry.ShaderOptimizer with one method for both directions.
        const string ThryOld = "Thry.ThryEditor.ShaderOptimizer", ThryNew = "Thry.ShaderOptimizer";

        static MethodInfo SetLockedMethod => FindMethod(ThryNew, "SetLockedForAllMaterials", p => p.Length == 6);

        public static bool CanUnlockPoiyomi => FindMethod(ThryOld, "UnlockMaterials", p => p.Length == 2) != null || SetLockedMethod != null;

        /// <summary>Unlocks Poiyomi materials through Thry's ShaderOptimizer (Poiyomi re-locks them automatically on upload).</summary>
        public static bool UnlockPoiyomi(IEnumerable<Material> materials) => CallLock("UnlockMaterials", materials);

        public static bool LockPoiyomi(IEnumerable<Material> materials) => CallLock("LockMaterials", materials);

        static bool CallLock(string method, IEnumerable<Material> materials)
        {
            var list = materials.Where(m => m != null).ToList();
            if (list.Count == 0) return true;
            var mi = FindMethod(ThryOld, method, p => p.Length == 2);
            var setLocked = mi == null ? SetLockedMethod : null;
            if (mi == null && setLocked == null) { Debug.LogWarning("[Avistry] Poiyomi's ShaderOptimizer wasn't found; lock/unlock the materials in the Poiyomi inspector."); return false; }
            try
            {
                if (mi != null)
                {
                    var progress = Enum.ToObject(mi.GetParameters()[1].ParameterType, 0); // ProgressBar.None
                    return (bool)mi.Invoke(null, new object[] { list, progress });
                }
                // (materials, lockState, showProgressbar, showDialog, allowCancel, shaderOptimizer): lockState 0 unlocks, 1 locks. The materials must be saved assets.
                var ps = setLocked.GetParameters();
                object mats = ps[0].ParameterType.IsAssignableFrom(typeof(List<Material>)) ? list : (object)list.ToArray();
                object state = ps[1].ParameterType.IsEnum ? Enum.ToObject(ps[1].ParameterType, method == "LockMaterials" ? 1 : 0) : Convert.ChangeType(method == "LockMaterials" ? 1 : 0, ps[1].ParameterType);
                var result = setLocked.Invoke(null, new object[] { mats, state, false, false, false, null });
                return !(result is bool ok) || ok;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>Re-sync shader keywords with feature toggles (needed after scripted toggle/texture changes).</summary>
        public static void FixPoiyomiKeywords(IEnumerable<Material> materials)
        {
            var list = materials.Where(m => m != null && ShaderProfiles.Detect(m.shader) == ShaderFamily.Poiyomi).ToList();
            if (list.Count == 0) return;
            var mi = FindMethod("Thry.ShaderEditor", "FixKeywords", p => p.Length == 1);
            try { mi?.Invoke(null, new object[] { list }); }
            catch (Exception e) { Debug.LogWarning("[Avistry] Couldn't sync Poiyomi keywords: " + e.Message); }
        }


        /// <summary>Call after changing a lilToon material from script (texture, colour or feature values).</summary>
        public static void AfterLilToonEdit(IEnumerable<Material> materials)
        {
            var list = materials.Where(m => m != null && ShaderProfiles.Detect(m.shader) == ShaderFamily.LilToon).ToList();
            if (list.Count == 0) return;
            foreach (var m in list)
            {
                // Hidden mirrors that other tools (and URP/lightmap baking) read.
                if (m.HasProperty("_MainTex"))
                {
                    var tex = m.GetTexture("_MainTex");
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                    if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", tex);
                }
                if (m.HasProperty("_Color") && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_Color"));
                if (m.shader.name.Contains("Multi"))
                {
                    var setup = FindMethod("lilToon.lilMaterialUtils", "SetupMultiMaterial", p => p.Length == 1 && p[0].ParameterType == typeof(Material));
                    try { setup?.Invoke(null, new object[] { m }); } catch (Exception e) { Debug.LogWarning("[Avistry] lilToon multi setup failed: " + e.Message); }
                }
                EditorUtility.SetDirty(m);
            }
            EnsureLilToonFeatures(list);
        }

        /// <summary>
        /// lilToon compiles out features no material uses (lilToonSetting). If our edit starts using one
        /// (e.g. hue/saturation), switch it on and re-apply the setting, like lilToon's inspector does.
        /// </summary>
        static void EnsureLilToonFeatures(List<Material> materials)
        {
            var settingType = FindType("lilToonSetting");
            if (settingType == null) return;
            var load = settingType.GetMethod("LoadShaderSetting", AnyStatic);
            var fromMat = settingType.GetMethod("SetupShaderSettingFromMaterial", AnyStatic);
            // Texture-driven features (e.g. the colour-adjust mask) are switched on by CheckTextures(ref setting, material).
            var fromTextures = settingType.GetMethods(AnyStatic).FirstOrDefault(m => m.Name == "CheckTextures" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(Material));
            var save = settingType.GetMethod("SaveShaderSetting", AnyStatic);
            var apply = settingType.GetMethods(AnyStatic).FirstOrDefault(m => m.Name == "ApplyShaderSetting" && m.GetParameters().Length == 2);
            if (load == null || fromMat == null || save == null || apply == null) return;
            try
            {
                var args = new object[] { null };
                load.Invoke(null, args);
                var setting = args[0];
                if (setting == null) return;
                string before = JsonUtility.ToJson(setting);
                foreach (var m in materials)
                {
                    var a = new object[] { m, setting };
                    fromMat.Invoke(null, a);
                    setting = a[1];
                    if (fromTextures != null)
                    {
                        var t = new object[] { setting, m };
                        fromTextures.Invoke(null, t);
                        setting = t[0];
                    }
                }
                if (JsonUtility.ToJson(setting) == before) return; // nothing new switched on
                save.Invoke(null, new[] { setting });
                apply.Invoke(null, new object[] { setting, "Avistry" });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avistry] Couldn't update lilToon shader settings (open a lilToon material once to refresh): " + e.Message);
            }
        }


        public struct ImportIssue
        {
            public string text;
            public bool problem;      // true: something is wrong; false: a recommendation
            public string fixLabel;   // null when there's no safe one-click fix
            public Action fix;
            public bool advancedOnly; // shown only in Advanced mode
        }

        static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

        /// <summary>
        /// Import-setting checks from the Poiyomi docs ("Textures and Colors") and lilToon's texture notes:
        /// colour vs data (sRGB), normal-map type, Crunch, JPG sources, non-power-of-two sizes, mipmap filter.
        /// </summary>
        public static List<ImportIssue> ImportIssues(TextureSlot slot)
        {
            if (slot == null) return new List<ImportIssue>();
            int id = slot.texture != null ? slot.texture.GetInstanceID() : 0;
            if (id == 0) return BuildImportIssues(slot);
            double now = EditorApplication.timeSinceStartup;
            int key = id * 31 + (int)slot.role * 2 + (slot.mixedRoles ? 1 : 0);
            if (s_issueCache.TryGetValue(key, out var e) && e.path == slot.assetPath && e.lang == Loc.Current)
            {
                if (now - e.time < IssueCacheSeconds) return e.list;
                // Throttle expired: only rebuild when the importer changed (reimport, or edits pending in an Inspector).
                var imp = AssetImporter.GetAtPath(slot.assetPath);
                int dirty = imp != null ? EditorUtility.GetDirtyCount(imp) : 0;
                var hash = imp != null ? AssetDatabase.GetAssetDependencyHash(slot.assetPath) : default;
                if (dirty == e.dirty && hash == e.hash) { e.time = now; return e.list; }
            }
            if (s_issueCache.Count > 32) s_issueCache.Clear();
            var importer = AssetImporter.GetAtPath(slot.assetPath);
            s_issueCache[key] = new IssueCacheEntry
            {
                path = slot.assetPath, lang = Loc.Current, time = now, list = BuildImportIssues(slot),
                dirty = importer != null ? EditorUtility.GetDirtyCount(importer) : 0,
                hash = importer != null ? AssetDatabase.GetAssetDependencyHash(slot.assetPath) : default,
            };
            return s_issueCache[key].list;
        }

        class IssueCacheEntry { public string path, lang; public double time; public List<ImportIssue> list; public int dirty; public Hash128 hash; }
        const double IssueCacheSeconds = 1.0;
        static readonly Dictionary<int, IssueCacheEntry> s_issueCache = new Dictionary<int, IssueCacheEntry>();

        /// <summary>Drops the cached import-issue lists (call after changing an importer).</summary>
        public static void InvalidateImportIssues() => s_issueCache.Clear();

        static List<ImportIssue> BuildImportIssues(TextureSlot slot)
        {
            var list = new List<ImportIssue>();
            var role = ImportProblem(slot, out var roleFix);
            if (role != null) list.Add(new ImportIssue { text = role, problem = true, fixLabel = Loc.T("Fix import setting"), fix = roleFix });

            var importer = AssetImporter.GetAtPath(slot.assetPath) as TextureImporter;
            if (importer == null) return list;

            string ext = System.IO.Path.GetExtension(slot.assetPath).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
                list.Add(new ImportIssue { text = Loc.T("This texture is a JPG: its compression blurs detail and it can't store transparency. PNG is recommended (saving here makes a PNG).") });

            if (importer.crunchedCompression)
                list.Add(new ImportIssue
                {
                    text = Loc.T("Crunch compression is on. It shrinks downloads but smears detail; Poiyomi's docs advise against it for avatar textures."),
                    problem = true, fixLabel = Loc.T("Turn off Crunch"),
                    fix = () => { importer.crunchedCompression = false; importer.SaveAndReimport(); InvalidateImportIssues(); },
                });

            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (!IsPowerOfTwo(w) || !IsPowerOfTwo(h))
                list.Add(new ImportIssue { text = Loc.F("Size {0}×{1} isn't a power of two (like 1024 or 2048), so it uses more memory and can look soft.", w, h) });

            if (importer.mipmapEnabled && importer.mipmapFilter != TextureImporterMipFilter.KaiserFilter && slot.role != TextureRole.Normal)
                list.Add(new ImportIssue
                {
                    text = Loc.T("Mipmap filter is Box. VRChat recommends Kaiser: textures stay sharper when seen from a distance."),
                    advancedOnly = true,
                    fixLabel = Loc.T("Use Kaiser"),
                    fix = () => { importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter; importer.SaveAndReimport(); InvalidateImportIssues(); },
                });
            return list;
        }

        /// <summary>
        /// Main texture transparency vs the material's render mode (Poiyomi Rendering Preset / lilToon Rendering Mode).
        /// <paramref name="hasTransparency"/> comes from the pixels (an alpha channel alone isn't enough: it's often all opaque).
        /// </summary>
        public static string TransparencyIssue(Material m, bool hasTransparency)
        {
            if (m == null) return null;
            var family = ShaderProfiles.Detect(m.shader);
            if (family == ShaderFamily.Poiyomi && m.HasProperty("_Mode"))
            {
                int mode = Mathf.RoundToInt(m.GetFloat("_Mode"));
                if (hasTransparency && mode == 0)
                    return Loc.T("The texture has see-through areas, but the Rendering Preset is Opaque, so they show solid. If they should be see-through, set the Rendering Preset to Cutout (or Transparent) in the material.");
                if (!hasTransparency && (mode == 2 || mode == 3))
                    return Loc.T("The Rendering Preset is Transparent/Fade but the texture has no see-through areas. Opaque or Cutout looks the same and costs less.");
            }
            else if (family == ShaderFamily.LilToon)
            {
                string n = SourceShader(m.shader).name;
                bool transparent = n.Contains("Transparent"), cutout = n.Contains("Cutout");
                if (hasTransparency && !transparent && !cutout)
                    return Loc.T("The texture has see-through areas, but the Rendering Mode (描画モード) is Opaque, so they show solid. Switch it to Cutout or Transparent in the lilToon material if they should be see-through.");
                if (!hasTransparency && transparent)
                    return Loc.T("The Rendering Mode is Transparent but the texture has no see-through areas. Opaque or Cutout looks the same and costs less.");
            }
            return null;
        }

        /// <summary>
        /// A wrong import setting for how the shader uses this texture (null if fine). Fix() changes the importer.
        /// </summary>
        public static string ImportProblem(TextureSlot slot, out Action fix)
        {
            fix = null;
            if (slot == null || slot.mixedRoles) return null;
            var importer = AssetImporter.GetAtPath(slot.assetPath) as TextureImporter;
            if (importer == null) return null;
            switch (slot.role)
            {
                case TextureRole.Mask when importer.textureType == TextureImporterType.Default && importer.sRGBTexture:
                    fix = () => { importer.sRGBTexture = false; importer.SaveAndReimport(); InvalidateImportIssues(); };
                    return Loc.T("Imported as a colour image (sRGB on), but the shader reads it as a mask, so its values come out wrong.");
                case TextureRole.Color when importer.textureType == TextureImporterType.Default && !importer.sRGBTexture:
                    fix = () => { importer.sRGBTexture = true; importer.SaveAndReimport(); InvalidateImportIssues(); };
                    return Loc.T("Imported with sRGB off, but it's a colour texture, so colours look washed out.");
                case TextureRole.Normal when importer.textureType != TextureImporterType.NormalMap:
                    fix = () => { importer.textureType = TextureImporterType.NormalMap; importer.SaveAndReimport(); InvalidateImportIssues(); };
                    return Loc.T("Not imported as a normal map, so lighting on it is wrong.");
            }
            return null;
        }
    }
}
