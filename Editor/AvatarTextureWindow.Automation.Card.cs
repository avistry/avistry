namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        /// <summary>
        /// The sections the info popup draws for a concept, in order, comma-separated: kicker, title, also, lede, what, when,
        /// example, try, watch, unity, after, related, footer (empty fields are left out). "unknown concept" for a bad id.
        /// </summary>
        internal string AutoPopupSections(string conceptId)
        {
            var e = Glossary.Get(conceptId);
            return e == null ? "unknown concept" : ConceptCard.SectionsOf(e);
        }
    }
}
