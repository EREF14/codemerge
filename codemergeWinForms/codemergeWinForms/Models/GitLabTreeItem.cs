namespace codemergeWinForms.Models
{
    /// <summary>
    /// Represente un element retourne par l'API GitLab dans l'arborescence du depot.
    /// </summary>
    public class GitLabTreeItem
    {
        /// <summary>
        /// Chemin complet de l'element dans le depot.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Type d'element GitLab, par exemple <c>blob</c> ou <c>tree</c>.
        /// </summary>
        public string Type { get; set; } = string.Empty;
    }
}
