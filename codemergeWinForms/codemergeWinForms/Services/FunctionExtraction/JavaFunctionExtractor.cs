using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions Java via expressions regulieres.
    /// </summary>
    public class JavaFunctionExtractor : IFunctionExtractor
    {
        /// <summary>
        /// Extrait les noms de methodes Java detectees.
        /// </summary>
        /// <param name="content">Code source Java a analyser.</param>
        /// <returns>Enumeration des noms de methodes detectees.</returns>
        public IEnumerable<string> Extract(string content)
        {
            // public void foo(...) {   / private static int bar(...) {
            var rx = new Regex(
                @"^\s*(?:public|private|protected|static|final|synchronized|abstract|native|\s)+\s*" +
                @"(?:[\w<>\[\], ?\.]+)\s+" +
                @"(?<name>[A-Za-z_]\w*)\s*\(",
                RegexOptions.Multiline);

            var keywords = new HashSet<string> { "if", "for", "while", "switch", "catch", "return", "new", "throw", "do" };

            foreach (Match m in rx.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (!keywords.Contains(name))
                    yield return name;
            }
        }

        /// <summary>
        /// Extrait des spans de methodes Java sur une base lineaire entre signatures detectees.
        /// </summary>
        /// <param name="content">Code source Java a analyser.</param>
        /// <returns>Enumeration des spans de methodes (approximation entre deux signatures).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            // version simple: on retourne juste un span “à la ligne”
            // (tu pourras améliorer en comptant { } plus tard)
            var rx = new Regex(
                @"^\s*(?:public|private|protected|static|final|synchronized|abstract|native|\s)+\s*" +
                @"(?:[\w<>\[\], ?\.]+)\s+" +
                @"(?<name>[A-Za-z_]\w*)\s*\(",
                RegexOptions.Multiline);

            var matches = rx.Matches(content).Cast<Match>().ToList();
            if (matches.Count == 0) yield break;

            for (int i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
                var name = matches[i].Groups["name"].Value;
                yield return new FunctionSpan(name, start, end);
            }
        }
    }
}
