using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class Loc
    {
        internal const string PrefKey = "Avistry.Language";
        internal static readonly string[] Codes = { "en", "ja", "ko", "zh-CN" };
        internal static readonly string[] Names = { "English", "日本語", "한국어", "简体中文" };

        static string s_current;
        static string s_unity;

        internal static string Setting
        {
            get => EditorPrefs.GetString(PrefKey, "");
            set
            {
                value = Array.IndexOf(Codes, value) >= 0 ? value : "";
                if (value == Setting) return;
                EditorPrefs.SetString(PrefKey, value);
                s_current = null;
                Glossary.Reload();
                InternalEditorUtility.RepaintAllViews();
            }
        }

        internal static string Current
        {
            get
            {
                if (s_current != null) return s_current;
                if (!s_loaded) { Resolved = false; return "en"; }
                Resolved = true;
                string s = Setting;
                return s_current = Array.IndexOf(Codes, s) >= 0 ? s : UnityLanguage;
            }
        }

        static bool s_loaded;

        [InitializeOnLoadMethod]
        static void Resolve()
        {
            s_loaded = true;
            s_current = null;
            _ = Current;
        }

        internal static bool English => Current == "en";

        internal static bool Resolved { get; private set; }

        internal static string NameOf(string code)
        {
            int i = Array.IndexOf(Codes, code);
            return i >= 0 ? Names[i] : code;
        }

        internal static string UnityLanguage
        {
            get
            {
                if (s_unity != null) return s_unity;
                SystemLanguage lang = SystemLanguage.English;
                try
                {
                    var t = typeof(Editor).Assembly.GetType("UnityEditor.LocalizationDatabase");
                    var p = t?.GetProperty("currentEditorLanguage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (p?.GetValue(null) is SystemLanguage l) lang = l;
                }
                catch (Exception) { }
                switch (lang)
                {
                    case SystemLanguage.Japanese: return s_unity = "ja";
                    case SystemLanguage.Korean: return s_unity = "ko";
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified: return s_unity = "zh-CN";
                    default: return s_unity = "en";
                }
            }
        }

        internal static string T(string en)
        {
            if (string.IsNullOrEmpty(en) || English) return en;
            return Glossary.TryString(en, out var text) ? text : en;
        }

        internal static string F(string en, params object[] args) => string.Format(T(en), args);
    }
}
