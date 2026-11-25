using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        void RefreshWindowTitle()
        {
            string label = _automationSandbox ? Loc.T("Avistry Textures (test)") : Loc.T("Avistry Textures");
            titleContent = new GUIContent(label, DiagnosticsReport.Logo, Loc.F("Avistry {0}", Version));
        }

        string BuildDiagnostics() => DiagnosticsReport.Build(_renderer, _material, _slots);

        internal void ShowErrorDialog(string title, System.Exception e)
        {
            if (e is System.OperationCanceledException) return;
            if (SupportSession.LogSaveFailure(e)) OfferDiagnostics(title);
            else Toast(title, UnityEditor.MessageType.Error);
        }

        internal void OfferDiagnostics(string reason)
        {
            int choice = StudioDialog.AskComplex(Loc.T("Avistry"), reason, Loc.T("Review report"), Msg.BtnCancel, Msg.BtnOpenConsole);
            if (choice == 0) CopyDiagnosticsFlow();
            else if (choice == 2) OpenConsole();
        }

        internal string CopyDiagnosticsFlow()
        {
            string text = BuildDiagnostics();
            if (!DiagnosticsReport.SkipPreview) DiagnosticsWindow.Show(text);
            return text;
        }
    }
}
