using UnityEditor;
using UnityEngine;

namespace Avistry.Textures
{
    internal static class GlossaryValidator
    {
        [MenuItem("Tools/Avistry/Validate glossary")]
        static void Run()
        {
            Glossary.Reload();
            var issues = Glossary.Validate();
            foreach (var i in issues) Debug.LogWarning("Glossary: " + i);
            StudioDialog.Ask(Loc.T("Glossary"), issues.Count == 0 ? Loc.T("No problems found.") : issues.Count + " problem(s); see the Console.", Loc.T("OK"));
        }
    }
}
