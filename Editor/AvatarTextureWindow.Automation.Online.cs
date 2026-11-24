using System.Text;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal string AutoOnlineLinks(string conceptId)
        {
            var e = Glossary.Has(conceptId) ? Glossary.Get(conceptId) : null;
            return e == null ? "unknown concept" : ConceptCard.WebLinksOf(e).TrimEnd('\n');
        }

        internal string AutoOnlineMenu(string tabName, string selected)
        {
            var sb = new StringBuilder();
            var tab = OnlineGuide.ForTab(tabName);
            if (tab != null) sb.Append("thisTab ").Append(OnlineGuide.Url(tab)).Append('\n');
            foreach (var p in OnlineGuide.Pages) sb.Append(p.Key).Append(' ').Append(OnlineGuide.Url(p)).Append('\n');
            sb.Append("tree ").Append(OnlineGuide.SkillUrl(Glossary.Has(selected) ? selected : null));
            return sb.ToString();
        }
    }
}
