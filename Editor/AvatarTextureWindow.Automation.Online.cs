using System.Text;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        /// <summary>
        /// The website links a concept's card shows, one per line ("video URL" or "page URL"), without opening them.
        /// "unknown concept" for an id that is not in the glossary.
        /// </summary>
        internal string AutoOnlineLinks(string conceptId)
        {
            var e = Glossary.Has(conceptId) ? Glossary.Get(conceptId) : null;
            return e == null ? "unknown concept" : ConceptCard.WebLinksOf(e).TrimEnd('\n');
        }

        /// <summary>
        /// What the Online guide menu would open, one "label URL" per line, for a tab name (or "" for the menu without a tab),
        /// then the skill tree footer link for <paramref name="selected"/>. Opens nothing.
        /// </summary>
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
