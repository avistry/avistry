using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
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
            var key = (t, method, match == null ? -1 : 0);
            if (MethodCache.TryGetValue(key, out var mi)) return mi;
            mi = t.GetMethods(AnyStatic).FirstOrDefault(m => m.Name == method && (match == null || match(m.GetParameters())));
            MethodCache[key] = mi;
            return mi;
        }


        const string SectionPrefix = "Hidden/SectionLocked/";

        public static bool IsSectionLocked(Material m) => m != null && m.shader != null && m.shader.name.StartsWith(SectionPrefix);

        public static Shader SourceShader(Shader s)
        {
            if (s == null || !s.name.StartsWith(SectionPrefix)) return s;
            var mi = FindMethod("Thry.ThryEditor.SectionLock", "GetSourceShader", p => p.Length == 1 && p[0].ParameterType == typeof(Shader));
            try { return (mi?.Invoke(null, new object[] { s }) as Shader) ?? s; }
            catch { return s; }
        }

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
                try { return (bool)mi.Invoke(null, new object[] { m }); } catch {  }
            }
            return m.shader.name.StartsWith("Hidden/Locked/");
        }

        const string ThryOld = "Thry.ThryEditor.ShaderOptimizer", ThryNew = "Thry.ShaderOptimizer";

        static MethodInfo SetLockedMethod => FindMethod(ThryNew, "SetLockedForAllMaterials", p => p.Length == 6);

        public static bool CanUnlockPoiyomi => FindMethod(ThryOld, "UnlockMaterials", p => p.Length == 2) != null || SetLockedMethod != null;

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
                    var progress = Enum.ToObject(mi.GetParameters()[1].ParameterType, 0);
                    return (bool)mi.Invoke(null, new object[] { list, progress });
                }
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

        public static void FixPoiyomiKeywords(IEnumerable<Material> materials)
        {
            var list = materials.Where(m => m != null && ShaderProfiles.Detect(m.shader) == ShaderFamily.Poiyomi).ToList();
            if (list.Count == 0) return;
            var mi = FindMethod("Thry.ShaderEditor", "FixKeywords", p => p.Length == 1);
            try { mi?.Invoke(null, new object[] { list }); }
            catch (Exception e) { Debug.LogWarning("[Avistry] Couldn't sync Poiyomi keywords: " + e.Message); }
        }


        public static void AfterLilToonEdit(IEnumerable<Material> materials)
        {
            var list = materials.Where(m => m != null && ShaderProfiles.Detect(m.shader) == ShaderFamily.LilToon).ToList();
            if (list.Count == 0) return;
            foreach (var m in list)
            {
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

        static void EnsureLilToonFeatures(List<Material> materials)
        {
            var settingType = FindType("lilToonSetting");
            if (settingType == null) return;
            var load = settingType.GetMethod("LoadShaderSetting", AnyStatic);
            var fromMat = settingType.GetMethod("SetupShaderSettingFromMaterial", AnyStatic);
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
                if (JsonUtility.ToJson(setting) == before) return;
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
            public bool problem;
            public string fixLabel;
            public Action fix;
            public bool advancedOnly;
        }

        static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;

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
