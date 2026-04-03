namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Contrat d'extraction des noms de fonctions et de leurs spans depuis un contenu source.
    /// </summary>
    public interface IFunctionExtractor
    {
        /// <summary>
        /// Extrait les noms de fonctions depuis un texte source.
        /// </summary>
        /// <param name="content">Contenu source a analyser.</param>
        /// <returns>Enumeration des noms de fonctions detectees.</returns>
        IEnumerable<string> Extract(string content);

        /// <summary>
        /// Extrait les positions de debut/fin de chaque fonction detectee.
        /// </summary>
        /// <param name="content">Contenu source a analyser.</param>
        /// <returns>Enumeration des spans de fonctions.</returns>
        IEnumerable<FunctionSpan> ExtractSpans(string content);
    }
}