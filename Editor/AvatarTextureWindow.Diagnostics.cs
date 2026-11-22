using UnityEngine;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        string BuildDiagnostics() => DiagnosticsReport.Build(_renderer, _material, _slots,
            _material != null ? ShaderProfiles.Detect(_material.shader).ToString() : "none");

        internal void ShowErrorDialog(string title, System.Exception e)
        {
            Debug.LogException(e);
            int r = StudioDialog.AskComplex(Loc.T("Avistry"), title, Loc.T("Copy diagnostics…"), Msg.BtnCancel, Msg.BtnOpenConsole);
            if (r == 0) CopyDiagnosticsFlow();
            else if (r == 2) OpenConsole();
        }

        internal string CopyDiagnosticsFlow()
        {
            var text = BuildDiagnostics();
            if (!DiagnosticsReport.SkipPreview) DiagnosticsWindow.Show(text);
            return text;
        }
    }
}
