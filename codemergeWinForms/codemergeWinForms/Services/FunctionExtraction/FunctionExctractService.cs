namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Route l'extraction des fonctions vers l'extracteur adapte au langage detecte.
    /// </summary>
    public class FunctionExtractService
    {
        private readonly IFunctionExtractor _csharp = new CSharpFunctionExtractor();
        private readonly IFunctionExtractor _jsts = new JsTsFunctionExtractor();
        private readonly IFunctionExtractor _java = new JavaFunctionExtractor();
        private readonly IFunctionExtractor _python = new PythonFunctionExtractor();
        private readonly IFunctionExtractor _lua = new LuaFunctionExtractor();
        private readonly IFunctionExtractor _php = new PhpFunctionExtractor();
        private readonly IFunctionExtractor _rust = new RustFunctionExtractor();

        /// <summary>
        /// Resolve l'extracteur approprie selon le langage Markdown.
        /// </summary>
        /// <param name="markdownLanguage">Tag de langage Markdown.</param>
        /// <returns>Extracteur correspondant, ou <c>null</c> si non supporte.</returns>
        private IFunctionExtractor? Resolve(string markdownLanguage) => markdownLanguage switch
        {
            "csharp" => _csharp,

            "javascript" => _jsts,
            "typescript" => _jsts,

            "java" => _java,
            "python" => _python,
            "lua" => _lua,

            "php" => _php,
            "rust" => _rust,

            _ => null
        };

        /// <summary>
        /// Extrait les noms de fonctions detectes dans un contenu source.
        /// </summary>
        /// <param name="markdownLanguage">Tag de langage Markdown du fichier.</param>
        /// <param name="content">Contenu source a analyser.</param>
        /// <returns>Enumeration des noms de fonctions trouves.</returns>
        public IEnumerable<string> ExtractFunctions(string markdownLanguage, string content)
            => Resolve(markdownLanguage)?.Extract(content) ?? Enumerable.Empty<string>();

        /// <summary>
        /// Extrait les emplacements (spans) des fonctions detectees dans un contenu source.
        /// </summary>
        /// <param name="markdownLanguage">Tag de langage Markdown du fichier.</param>
        /// <param name="content">Contenu source a analyser.</param>
        /// <returns>Enumeration des spans de fonctions trouves.</returns>
        public IEnumerable<FunctionSpan> ExtractFunctionSpans(string markdownLanguage, string content)
            => Resolve(markdownLanguage)?.ExtractSpans(content) ?? Enumerable.Empty<FunctionSpan>();
    }
}
