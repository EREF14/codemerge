using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions Python et leurs spans selon l'indentation.
    /// </summary>
    public class PythonFunctionExtractor : IFunctionExtractor
    {
        // On se concentre sur les fonctions
        // (si tu veux aussi les classes plus tard, on pourra les gérer séparément)
        private static readonly Regex Rx =
            new(@"^(?<indent>[ \t]*)(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\(",
                RegexOptions.Multiline);

        /// <summary>
        /// Extrait les noms de fonctions Python detectees.
        /// </summary>
        /// <param name="content">Code source Python a analyser.</param>
        /// <returns>Enumeration des noms de fonctions detectees.</returns>
        public IEnumerable<string> Extract(string content)
            => ExtractSpans(content).Select(s => s.Name);

        /// <summary>
        /// Extrait les spans des fonctions Python detectees.
        /// </summary>
        /// <param name="content">Code source Python a analyser.</param>
        /// <returns>Enumeration des spans de fonctions (debut inclusif, fin exclusive).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            var matches = Rx.Matches(content).Cast<Match>().ToList();
            if (matches.Count == 0)
                yield break;

            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var name = m.Groups["name"].Value;
                var indent = m.Groups["indent"].Value.Length;

                int start = ExpandStartToIncludeLeadingComments(content, m.Index);
                int end = FindPythonBlockEnd(content, m.Index, indent);

                if (end > start)
                    yield return new FunctionSpan(name, start, end);
            }
        }

        /// <summary>
        /// Etend le debut d'une fonction pour inclure les commentaires precedents.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="start">Index de debut initial.</param>
        /// <returns>Index de debut etendu.</returns>
        private static int ExpandStartToIncludeLeadingComments(string content, int start)
            => FunctionExtractionTextHelper.ExpandStartToIncludeLeadingLines(content, start, ClassifyLeadingTrivia);

        /// <summary>
        /// Trouve la fin d'un bloc de fonction Python en s'appuyant sur l'indentation.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="defIndex">Index du mot-cle de definition de fonction.</param>
        /// <param name="baseIndent">Indentation de la ligne de definition.</param>
        /// <returns>Index de fin exclusif du bloc de fonction.</returns>
        private static int FindPythonBlockEnd(string content, int defIndex, int baseIndent)
        {
            int defLineStart = FunctionExtractionTextHelper.FindLineStart(content, defIndex);

            // 1) Trouver la fin réelle de la signature (la ligne qui contient le ':' final)
            int signatureEndLineStart = FindPythonSignatureEndLineStart(content, defLineStart);
            int nextLineStart = FindNextLineStart(content, signatureEndLineStart);

            if (nextLineStart < 0)
                return content.Length;

            int i = nextLineStart;

            while (i < content.Length)
            {
                int lineStart = i;
                int lineEnd = FindLineEnd(content, lineStart);
                string line = content.Substring(lineStart, lineEnd - lineStart);
                string trimmed = line.Trim();

                // Ligne vide -> on la laisse dans le bloc
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    i = FindNextLineStart(content, lineStart);
                    if (i < 0)
                        return content.Length;
                    continue;
                }

                int indent = CountIndent(line);

                // Commentaire
                if (trimmed.StartsWith("#"))
                {
                    // commentaire indenté => appartient à la fonction
                    if (indent > baseIndent)
                    {
                        i = FindNextLineStart(content, lineStart);
                        if (i < 0)
                            return content.Length;
                        continue;
                    }

                    // commentaire au niveau du def => appartient à la suite
                    return lineStart;
                }

                // Retour à l'indentation du def ou moins => fin du bloc
                if (indent <= baseIndent)
                    return lineStart;

                i = FindNextLineStart(content, lineStart);
                if (i < 0)
                    return content.Length;
            }

            return content.Length;
        }

        /// <summary>
        /// Trouve la ligne qui termine la signature Python multi-ligne (celle avec ':').
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="defLineStart">Index de debut de la ligne <c>def</c>.</param>
        /// <returns>Index de debut de la ligne qui ferme la signature.</returns>
        private static int FindPythonSignatureEndLineStart(string content, int defLineStart)
        {
            int i = defLineStart;
            int parenDepth = 0;
            bool seenColon = false;

            while (i < content.Length)
            {
                int lineStart = i;
                int lineEnd = FindLineEnd(content, lineStart);
                string line = content.Substring(lineStart, lineEnd - lineStart);

                for (int j = 0; j < line.Length; j++)
                {
                    char c = line[j];

                    // ignorer les commentaires Python
                    if (c == '#')
                        break;

                    // ignorer grossièrement les strings simples
                    // (suffisant ici pour une signature)
                    if (c == '\'' || c == '"')
                    {
                        char quote = c;
                        j++;

                        while (j < line.Length)
                        {
                            if (line[j] == '\\')
                            {
                                j += 2;
                                continue;
                            }

                            if (line[j] == quote)
                                break;

                            j++;
                        }

                        continue;
                    }

                    if (c == '(' || c == '[' || c == '{')
                        parenDepth++;
                    else if (c == ')' || c == ']' || c == '}')
                        parenDepth--;
                    else if (c == ':' && parenDepth == 0)
                    {
                        seenColon = true;
                        break;
                    }
                }

                if (seenColon)
                    return lineStart;

                i = FindNextLineStart(content, lineStart);
                if (i < 0)
                    break;
            }

            return defLineStart;
        }

        /// <summary>
        /// Compte le niveau d'indentation initial d'une ligne.
        /// </summary>
        /// <param name="line">Ligne de texte a mesurer.</param>
        /// <returns>Nombre de caracteres d'indentation en debut de ligne.</returns>
        private static int CountIndent(string line)
        {
            int count = 0;
            while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
                count++;
            return count;
        }

        private static LeadingLineDecision ClassifyLeadingTrivia(string trimmed, bool inBlockComment)
            => trimmed.StartsWith("#")
                ? LeadingLineDecision.Include
                : LeadingLineDecision.Stop;

        /// <summary>
        /// Retourne l'index de debut de la ligne suivante.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="lineStart">Index de debut de la ligne courante.</param>
        /// <returns>Index de debut de ligne suivante, ou -1 s'il n'y en a plus.</returns>
        private static int FindNextLineStart(string content, int lineStart)
        {
            int nl = content.IndexOf('\n', lineStart);
            if (nl < 0 || nl + 1 >= content.Length)
                return -1;
            return nl + 1;
        }

        /// <summary>
        /// Retourne l'index de fin de ligne (caractere '\\n' ou fin de texte).
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="lineStart">Index de debut de ligne.</param>
        /// <returns>Index de fin de ligne.</returns>
        private static int FindLineEnd(string content, int lineStart)
        {
            int nl = content.IndexOf('\n', lineStart);
            return nl < 0 ? content.Length : nl;
        }
    }
}
