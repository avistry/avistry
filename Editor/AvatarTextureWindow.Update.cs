using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        static string UpdateAskText => Loc.T("Check GitHub once a day for a new Avistry version? Only asks GitHub for the latest version number; nothing about you or your project is sent.");
        static string UpdateHowText => Loc.T("To update: in VCC open Manage Project, then update Avistry. Or download the .unitypackage from the release page.");
        static GUIContent UpdateAskContent => new GUIContent(UpdateAskText);
        bool _updateHow;

        void HookUpdateCheck()
        {
            UpdateCheck.Changed -= Repaint;
            UpdateCheck.Changed += Repaint;
            EditorApplication.delayCall += UpdateCheck.MaybeCheckAutomatically;
        }

        /// <summary>Height of the update line: the opt-in question, or the "is out" notice. 0 when there is nothing to show.</summary>
        float UpdateBannerHeight(float width)
        {
            if (UpdateCheck.NotAsked) return 33f + StudioGUI.Hint.CalcHeight(UpdateAskContent, width - 16f);
            if (UpdateCheck.TryGetNotice(out _, out _)) return _updateHow ? 78f : 30f;
            return 0f;
        }

        void DrawUpdateBanner(Rect r)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, StudioGUI.HeaderBg);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), StudioGUI.HeaderLine);
            }
            float W(GUIContent c) => EditorStyles.miniButton.CalcSize(c).x + 8f;
            if (UpdateCheck.NotAsked)
            {
                GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 29), UpdateAskContent, StudioGUI.Hint);
                var on = new GUIContent(Loc.T("Turn on"));
                var no = new GUIContent(Loc.T("No thanks"));
                float y = r.yMax - 25;
                var noR = new Rect(r.xMax - 8 - W(no), y, W(no), 20);
                var onR = new Rect(noR.x - 4 - W(on), y, W(on), 20);
                if (GUI.Button(onR, on, EditorStyles.miniButton)) { UpdateCheck.SetEnabled(true); UpdateCheck.MaybeCheckAutomatically(); Repaint(); }
                if (GUI.Button(noR, no, EditorStyles.miniButton)) { UpdateCheck.SetEnabled(false); Repaint(); }
                return;
            }
            if (!UpdateCheck.TryGetNotice(out string v, out string url)) return;
            var news = new GUIContent(Loc.T("What's new"));
            var how = new GUIContent(Loc.T("How to update"));
            var skip = new GUIContent(Loc.T("Skip this version"));
            float bw = W(news) + W(how) + W(skip) + 12f;
            if (bw > r.width - 16f) { skip.text = Loc.T("Skip"); bw = W(news) + W(how) + W(skip) + 12f; }
            GUI.Label(new Rect(r.x + 8, r.y + 5, Mathf.Max(40f, r.width - 16f - bw), 18), Loc.F("Avistry {0} is out.", v), EditorStyles.miniBoldLabel);
            var skipR = new Rect(r.xMax - 8 - W(skip), r.y + 4, W(skip), 20);
            var howR = new Rect(skipR.x - 4 - W(how), skipR.y, W(how), 20);
            var newsR = new Rect(howR.x - 4 - W(news), skipR.y, W(news), 20);
            if (GUI.Button(newsR, news, EditorStyles.miniButton)) UpdateCheck.OpenUrl(url);
            if (GUI.Button(howR, how, EditorStyles.miniButton)) _updateHow = !_updateHow;
            if (GUI.Button(skipR, skip, EditorStyles.miniButton)) { _updateHow = false; UpdateCheck.Skip(v); }
            if (_updateHow) GUI.Label(new Rect(r.x + 8, r.y + 28, r.width - 16, r.height - 32), UpdateHowText, StudioGUI.Hint);
        }
    }
}
