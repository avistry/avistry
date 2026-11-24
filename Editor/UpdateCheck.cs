using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Avistry.Textures
{
    internal static class UpdateCheck
    {
        const string Endpoint = "https://api.github.com/repos/avistry/avistry/releases/latest";
        const string UrlPrefix = "https://github.com/avistry/avistry/";
        const string ModeKey = "Avistry.UpdateCheck.Mode";
        const string LastKey = "Avistry.UpdateCheck.LastUtc";
        const string EtagKey = "Avistry.UpdateCheck.Etag";
        const string TagKey = "Avistry.UpdateCheck.LatestTag";
        const string UrlKey = "Avistry.UpdateCheck.LatestUrl";
        const string SkipKey = "Avistry.UpdateCheck.Skipped";
        const long DaySeconds = 24 * 60 * 60;

        internal static bool NotAsked => EditorPrefs.GetInt(ModeKey, 0) == 0;
        internal static bool Enabled => EditorPrefs.GetInt(ModeKey, 0) == 1;
        internal static void SetEnabled(bool on) => EditorPrefs.SetInt(ModeKey, on ? 1 : 2);

        internal static event Action Changed;

        static UnityWebRequest s_req;
        static bool s_manual;
        static bool s_shownByManual;
        static string s_installed;
        static string s_outcome = "";

        [InitializeOnLoadMethod]
        static void HookReload() => AssemblyReloadEvents.beforeAssemblyReload += AbortRequest;

        static void AbortRequest()
        {
            EditorApplication.update -= Poll;
            if (s_req == null) return;
            try { s_req.Abort(); } catch { }
            s_req.Dispose();
            s_req = null;
        }

        internal static bool Busy => s_req != null;

        internal static bool TryGetNotice(out string version, out string url)
        {
            version = null;
            url = EditorPrefs.GetString(UrlKey, "");
            string tag = EditorPrefs.GetString(TagKey, "");
            if (tag.Length == 0 || !IsOurUrl(url)) return false;
            if (!Enabled && !s_shownByManual) return false;
            string v = tag.TrimStart('v', 'V');
            if (v == EditorPrefs.GetString(SkipKey, "")) return false;
            if (CompareVersions(v, InstalledVersion()) <= 0) return false;
            version = v;
            return true;
        }

        internal static void Skip(string version)
        {
            EditorPrefs.SetString(SkipKey, version);
            Changed?.Invoke();
        }

        internal static bool IsOurUrl(string url) => !string.IsNullOrEmpty(url) && url.StartsWith(UrlPrefix, StringComparison.Ordinal);

        internal static void OpenUrl(string url)
        {
            if (IsOurUrl(url)) Application.OpenURL(url);
        }

        internal static void MaybeCheckAutomatically()
        {
            if (!Enabled || Busy) return;
            long last = long.TryParse(EditorPrefs.GetString(LastKey, "0"), out var l) ? l : 0;
            if (Now() - last < DaySeconds) return;
            Start(false);
        }

        internal static void CheckNow()
        {
            if (Busy) return;
            Start(true);
        }

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        static void Start(bool manual)
        {
            s_manual = manual;
            var req = UnityWebRequest.Get(Endpoint);
            req.timeout = 10;
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
            req.SetRequestHeader("User-Agent", "Avistry-UpdateCheck");
            string etag = EditorPrefs.GetString(EtagKey, "");
            if (etag.Length > 0 && EditorPrefs.GetString(TagKey, "").Length > 0) req.SetRequestHeader("If-None-Match", etag);
            req.SendWebRequest();
            s_req = req;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            var req = s_req;
            if (req == null) { EditorApplication.update -= Poll; return; }
            if (!req.isDone) return;
            EditorApplication.update -= Poll;
            s_req = null;
            bool reached = false;
            s_outcome = "";
            try
            {
                long code = req.responseCode;
                if (code == 304) { reached = true; s_outcome = "ok"; }
                else if (code == 200 && req.result == UnityWebRequest.Result.Success)
                {
                    reached = Parse(req.downloadHandler.text, out string tag, out string url);
                    s_outcome = reached ? "ok" : "bad";
                    if (reached)
                    {
                        EditorPrefs.SetString(TagKey, tag);
                        EditorPrefs.SetString(UrlKey, url);
                        EditorPrefs.SetString(EtagKey, req.GetResponseHeader("ETag") ?? "");
                    }
                }
                else if (code == 404) { reached = true; s_outcome = "none"; }
            }
            catch (Exception e)
            {
                s_outcome = "bad";
                if (s_manual) Debug.LogWarning("Avistry: could not read GitHub's answer. " + e.Message);
            }
            finally { req.Dispose(); }

            if (reached || s_outcome == "bad") EditorPrefs.SetString(LastKey, Now().ToString());
            bool manual = s_manual;
            s_manual = false;
            if (manual) s_shownByManual = true;
            Changed?.Invoke();
            if (!manual) return;
            if (s_outcome == "bad") StudioDialog.Ask(Loc.T("Avistry"), Loc.T("GitHub answered in a way Avistry does not understand."), Loc.T("OK"));
            else if (!reached) StudioDialog.Ask(Loc.T("Avistry"), Loc.T("Could not reach GitHub. Check your internet connection and try again later."), Loc.T("OK"));
            else if (s_outcome == "none") StudioDialog.Ask(Loc.T("Avistry"), Loc.T("No release has been published yet."), Loc.T("OK"));
            else if (TryGetNotice(out string v, out _)) StudioDialog.Ask(Loc.T("Avistry"), Loc.F("Avistry {0} is out. Use What's new in the side panel to see what changed.", v), Loc.T("OK"));
            else StudioDialog.Ask(Loc.T("Avistry"), Loc.T("You have the latest version."), Loc.T("OK"));
        }

        [Serializable] class ReleaseDto { public string tag_name; public string html_url; }
        [Serializable] class PackageDto { public string version; }

        internal static bool Parse(string json, out string tag, out string url)
        {
            tag = url = null;
            var dto = JsonUtility.FromJson<ReleaseDto>(json);
            if (dto == null || string.IsNullOrEmpty(dto.tag_name) || !IsOurUrl(dto.html_url)) return false;
            if (!TryParseVersion(dto.tag_name, out _, out _)) return false;
            tag = dto.tag_name;
            url = dto.html_url;
            return true;
        }

        internal static string InstalledVersion()
        {
            if (s_installed == null) s_installed = ReadInstalledVersion();
            return s_installed;
        }

        static string ReadInstalledVersion()
        {
            string v = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpdateCheck).Assembly)?.version;
            if (!string.IsNullOrEmpty(v)) return v;
            try
            {
                string path = AssetDatabase.GUIDToAssetPath("1edb04b6be5e7464bacfc5093dc8a3ed");
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && System.IO.File.Exists(path))
                {
                    var dto = JsonUtility.FromJson<PackageDto>(System.IO.File.ReadAllText(path));
                    if (!string.IsNullOrEmpty(dto?.version)) return dto.version;
                }
            }
            catch (Exception e) { Debug.LogWarning("Avistry: could not read its own version. " + e.Message); }
            return "0.0.0";
        }

        static bool TryParseVersion(string s, out int[] core, out string pre)
        {
            core = new int[3];
            pre = "";
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().TrimStart('v', 'V');
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-');
            if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); }
            var parts = s.Split('.');
            if (parts.Length > 3) return false;
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out core[i]) || core[i] < 0) return false;
            return true;
        }

        internal static int CompareVersions(string a, string b)
        {
            if (!TryParseVersion(a, out var ca, out var pa)) { ca = new int[3]; pa = ""; }
            if (!TryParseVersion(b, out var cb, out var pb)) { cb = new int[3]; pb = ""; }
            for (int i = 0; i < 3; i++)
                if (ca[i] != cb[i]) return ca[i].CompareTo(cb[i]);
            if (pa.Length == 0 && pb.Length == 0) return 0;
            if (pa.Length == 0) return 1;
            if (pb.Length == 0) return -1;
            var xa = pa.Split('.');
            var xb = pb.Split('.');
            for (int i = 0; i < Math.Max(xa.Length, xb.Length); i++)
            {
                if (i >= xa.Length) return -1;
                if (i >= xb.Length) return 1;
                bool na = int.TryParse(xa[i], out int ia), nb = int.TryParse(xb[i], out int ib);
                int c = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(xa[i], xb[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        [MenuItem("Tools/Avistry/Check for Updates")]
        static void MenuCheck() => CheckNow();
    }
}
