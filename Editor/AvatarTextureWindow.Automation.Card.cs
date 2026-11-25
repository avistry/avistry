namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        internal string AutoPopupSections(string conceptId)
        {
            var e = Glossary.Get(conceptId);
            return e == null ? "unknown concept" : ConceptCard.SectionsOf(e);
        }
    }
}
