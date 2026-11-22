using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Avistry.Textures
{
    /// <summary>
    /// The interface language. UI strings are written in English in the code and wrapped in <see cref="T"/>; the English
    /// text is the key into Editor/Glossary/&lt;lang&gt;.json. A string without a translation shows in English.
    /// </summary>
    internal static class Loc
    {
        internal const string PrefKey = "Avistry.Language";
        internal static readonly string[] Codes = { "en", "ja", "ko", "zh-CN" };
        // Each name in its own language, so a person who cannot read the current language still finds theirs.
        internal static readonly string[] Names = { "English", "日本語", "한국어", "简体中文" };

        static string s_current;
        static string s_unity;

        /// <summary>The stored choice: a language code, or empty to follow Unity's editor language.</summary>
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

        /// <summary>The language in use: the setting, or Unity's editor language when it is one we have, or English.</summary>
        internal static string Current
        {
            get
            {
                if (s_current != null) return s_current;
                // Unity refuses (and logs) EditorPrefs while it constructs a window or asset, even when the exception
                // is caught, so the setting is read only from Resolve. Until then English stands in.
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

        /// <summary>False when the last look at the setting was refused, so "en" was only a stand-in.</summary>
        internal static bool Resolved { get; private set; }

        internal static string NameOf(string code)
        {
            int i = Array.IndexOf(Codes, code);
            return i >= 0 ? Names[i] : code;
        }

        /// <summary>Unity's editor language (Preferences > Languages) as one of <see cref="Codes"/>; English when it is another one.</summary>
        internal static string UnityLanguage
        {
            get
            {
                if (s_unity != null) return s_unity;
                // LocalizationDatabase is internal; a missing member only means we cannot follow Unity, so English it is.
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

        /// <summary>The text in the current language, or <paramref name="en"/> itself when there is no translation.</summary>
        internal static string T(string en)
        {
            if (string.IsNullOrEmpty(en) || English) return en;
            return Glossary.TryString(en, out var text) ? text : en;
        }

        /// <summary><see cref="T"/>, then string.Format. The English text keeps {0}-style holes.</summary>
        internal static string F(string en, params object[] args) => string.Format(T(en), args);
    }
}
