using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions PHP et leurs spans a partir d'un code source.
    /// </summary>
    public class PhpFunctionExtractor : IFunctionExtractor
    {
        private static readonly Regex RxFunction = new(
            @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?function\s+&?\s*(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*(?::\s*[A-Za-z_\\\|\?\w]+)?\s*\{",
            RegexOptions.Multiline);

        /// <summary>
        /// Extrait les noms de fonctions PHP detectees.
        /// </summary>
        /// <param name="content">Code source PHP a analyser.</param>
        /// <returns>Enumeration des noms de fonctions detectees.</returns>
        public IEnumerable<string> Extract(string content)
            => ExtractSpans(content).Select(s => s.Name);

        /// <summary>
        /// Extrait les spans des fonctions PHP detectees.
        /// </summary>
        /// <param name="content">Code source PHP a analyser.</param>
        /// <returns>Enumeration des spans de fonctions (debut inclusif, fin exclusive).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            foreach (Match m in RxFunction.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int start = ExpandStartToIncludeLeadingComments(content, m.Index);

                int openBrace = content.IndexOf('{', m.Index);
                if (openBrace < 0)
                    continue;

                int end = FindMatchingBrace(content, openBrace);
                if (end < 0)
                    continue;

                yield return new FunctionSpan(name, start, end + 1);
            }
        }

        /// <summary>
        /// Etend le debut d'une fonction pour inclure commentaires/attributs precedents.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="start">Index de debut initial.</param>
        /// <returns>Index de debut etendu.</returns>
        private static int ExpandStartToIncludeLeadingComments(string content, int start)
            => FunctionExtractionTextHelper.ExpandStartToIncludeLeadingLines(
                content,
                start,
                ClassifyLeadingTrivia,
                includeBlankLinesInResult: true);

        /// <summary>
        /// Trouve l'accolade fermante associee a une accolade ouvrante PHP.
        /// </summary>
        /// <param name="s">Contenu source complet.</param>
        /// <param name="openIndex">Index de l'accolade ouvrante.</param>
        /// <returns>Index de l'accolade fermante, ou -1 si introuvable.</returns>
        private static int FindMatchingBrace(string s, int openIndex)
        {
            int depth = 0;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool inLineComment = false;
            bool inBlockComment = false;

            for (int i = openIndex; i < s.Length; i++)
            {
                char c = s[i];
                char next = (i + 1 < s.Length) ? s[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (!inSingleQuote && !inDoubleQuote)
                {
                    if (c == '/' && next == '/')
                    {
                        inLineComment = true;
                        i++;
                        continue;
                    }

                    if (c == '#')
                    {
                        inLineComment = true;
                        continue;
                    }

                    if (c == '/' && next == '*')
                    {
                        inBlockComment = true;
                        i++;
                        continue;
                    }
                }

                if (!inDoubleQuote && c == '\'' && !IsEscaped(s, i))
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (!inSingleQuote && c == '"' && !IsEscaped(s, i))
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                if (inSingleQuote || inDoubleQuote)
                    continue;

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Verifie si un caractere de quote est echappe par un nombre impair de backslashes.
        /// </summary>
        /// <param name="s">Contenu source complet.</param>
        /// <param name="index">Index du caractere a verifier.</param>
        /// <returns><c>true</c> si le caractere est echappe, sinon <c>false</c>.</returns>
        private static bool IsEscaped(string s, int index)
        {
            int backslashCount = 0;
            int i = index - 1;

            while (i >= 0 && s[i] == '\\')
            {
                backslashCount++;
                i--;
            }

            return backslashCount % 2 == 1;
        }

        private static LeadingLineDecision ClassifyLeadingTrivia(string trimmed, bool inBlockComment)
            => trimmed.StartsWith("//")
                || trimmed.StartsWith("#")
                || trimmed.StartsWith("/*")
                || trimmed.StartsWith("*")
                || trimmed.StartsWith("*/")
                || trimmed.StartsWith("#[")
                    ? LeadingLineDecision.Include
                    : LeadingLineDecision.Stop;
    }
}
