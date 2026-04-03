namespace codemergeWinForms.Services
{
    /// <summary>
    /// Determine le langage Markdown a utiliser pour un fichier selon son nom/extension.
    /// </summary>
    public static class LanguageDetector
    {
        /// <summary>
        /// Retourne le tag de langage Markdown correspondant a un chemin de fichier.
        /// </summary>
        /// <param name="filePath">Chemin ou nom de fichier a analyser.</param>
        /// <returns>Identifiant de langage a utiliser dans un bloc de code Markdown.</returns>
        public static string GetMarkdownLanguage(string filePath)
            => FileTypeHelper.GetMarkdownLanguage(filePath);
    }
}
