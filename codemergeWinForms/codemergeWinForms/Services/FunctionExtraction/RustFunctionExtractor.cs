using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions Rust nommees et leurs spans via regex + appariement d'accolades.
    /// </summary>
    public class RustFunctionExtractor : IFunctionExtractor
    {
        private static readonly Regex RxFunction = new(
            @"^\s*(?:pub(?:\s*\([^)]*\))?\s+)?(?:default\s+)?(?:const\s+)?(?:async\s+)?(?:unsafe\s+)?(?:extern\s+(?:""[^""]*""\s+)?)?fn\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^;\r\n{]*(?:>|\r?\n))?\s*\(",
            RegexOptions.Multiline);

        /// <summary>
        /// Extrait les noms de fonctions Rust detectees.
        /// </summary>
        /// <param name="content">Code source Rust a analyser.</param>
        /// <returns>Enumeration des noms de fonctions detectees.</returns>
        public IEnumerable<string> Extract(string content)
            => ExtractSpans(content).Select(s => s.Name);

        /// <summary>
        /// Extrait les spans des fonctions Rust detectees.
        /// </summary>
        /// <param name="content">Code source Rust a analyser.</param>
        /// <returns>Enumeration des spans de fonctions (debut inclusif, fin exclusive).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            foreach (Match match in RxFunction.Matches(content))
            {
                string name = match.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int bodyStart = FindBodyStart(content, match.Index);
                if (bodyStart < 0)
                    continue;

                int bodyEnd = FindMatchingBrace(content, bodyStart);
                if (bodyEnd < 0)
                    continue;

                int start = ExpandStartToIncludeLeadingTrivia(content, match.Index);
                yield return new FunctionSpan(name, start, bodyEnd + 1);
            }
        }

        /// <summary>
        /// Trouve l'accolade ouvrante du corps d'une fonction, en ignorant commentaires et chaines.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="signatureStart">Index de debut de signature.</param>
        /// <returns>Index de l'accolade ouvrante, ou -1 si la fonction n'a pas de corps.</returns>
        private static int FindBodyStart(string content, int signatureStart)
        {
            int parenDepth = 0;
            int bracketDepth = 0;
            int angleDepth = 0;

            for (int i = signatureStart; i < content.Length; i++)
            {
                if (TrySkipTrivia(ref i, content))
                    continue;

                char c = content[i];

                if (c == '(')
                {
                    parenDepth++;
                    continue;
                }

                if (c == ')')
                {
                    if (parenDepth > 0)
                        parenDepth--;
                    continue;
                }

                if (c == '[')
                {
                    bracketDepth++;
                    continue;
                }

                if (c == ']')
                {
                    if (bracketDepth > 0)
                        bracketDepth--;
                    continue;
                }

                if (c == '<')
                {
                    angleDepth++;
                    continue;
                }

                if (c == '>')
                {
                    if (angleDepth > 0)
                        angleDepth--;
                    continue;
                }

                if (parenDepth == 0 && bracketDepth == 0 && angleDepth == 0)
                {
                    if (c == '{')
                        return i;

                    if (c == ';')
                        return -1;
                }
            }

            return -1;
        }

        /// <summary>
        /// Trouve l'accolade fermante correspondant a une accolade ouvrante.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="openBraceIndex">Index de l'accolade ouvrante.</param>
        /// <returns>Index de l'accolade fermante, ou -1 si introuvable.</returns>
        private static int FindMatchingBrace(string content, int openBraceIndex)
        {
            int depth = 0;

            for (int i = openBraceIndex; i < content.Length; i++)
            {
                if (TrySkipTrivia(ref i, content))
                    continue;

                if (content[i] == '{')
                {
                    depth++;
                    continue;
                }

                if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Etend le debut d'une fonction pour inclure commentaires doc, commentaires bloc et attributs.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="start">Index de debut initial.</param>
        /// <returns>Index de debut etendu.</returns>
        private static int ExpandStartToIncludeLeadingTrivia(string content, int start)
            => FunctionExtractionTextHelper.ExpandStartToIncludeLeadingLines(content, start, ClassifyLeadingTrivia);

        /// <summary>
        /// Ignore commentaires et litteraux Rust a partir de l'index courant.
        /// </summary>
        /// <param name="i">Index courant (avance apres le bloc ignore).</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si un bloc a ete ignore, sinon <c>false</c>.</returns>
        private static bool TrySkipTrivia(ref int i, string s)
        {
            if (TrySkipLineComment(ref i, s))
                return true;

            if (TrySkipBlockComment(ref i, s))
                return true;

            if (TrySkipRawString(ref i, s))
                return true;

            if (TrySkipQuotedString(ref i, s))
                return true;

            if (TrySkipCharLiteral(ref i, s))
                return true;

            return false;
        }

        /// <summary>
        /// Ignore un commentaire de ligne Rust a partir de la position courante.
        /// </summary>
        /// <param name="i">Index courant, avance jusqu'a la fin du commentaire.</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si un commentaire de ligne a ete ignore, sinon <c>false</c>.</returns>
        private static bool TrySkipLineComment(ref int i, string s)
        {
            if (i + 1 >= s.Length || s[i] != '/' || s[i + 1] != '/')
                return false;

            i += 2;
            while (i < s.Length && s[i] != '\n')
                i++;

            return true;
        }

        /// <summary>
        /// Ignore un commentaire de bloc Rust, y compris les blocs imbriques.
        /// </summary>
        /// <param name="i">Index courant, avance jusqu'apres la fin du bloc.</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si un commentaire de bloc a ete ignore, sinon <c>false</c>.</returns>
        private static bool TrySkipBlockComment(ref int i, string s)
        {
            if (i + 1 >= s.Length || s[i] != '/' || s[i + 1] != '*')
                return false;

            i += 2;
            int depth = 1;

            while (i < s.Length && depth > 0)
            {
                if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*')
                {
                    depth++;
                    i += 2;
                    continue;
                }

                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '/')
                {
                    depth--;
                    i += 2;
                    continue;
                }

                i++;
            }

            i = Math.Min(i, s.Length) - 1;
            return true;
        }

        /// <summary>
        /// Ignore une raw string Rust a partir de la position courante.
        /// </summary>
        /// <param name="i">Index courant, avance jusqu'a la fin du litteral.</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si une raw string a ete ignoree, sinon <c>false</c>.</returns>
        private static bool TrySkipRawString(ref int i, string s)
        {
            int start = i;

            if (s[i] == 'b')
            {
                if (i + 1 >= s.Length || s[i + 1] != 'r')
                    return false;
                i++;
            }

            if (s[i] != 'r')
            {
                i = start;
                return false;
            }

            int j = i + 1;
            int hashCount = 0;

            while (j < s.Length && s[j] == '#')
            {
                hashCount++;
                j++;
            }

            if (j >= s.Length || s[j] != '"')
            {
                i = start;
                return false;
            }

            j++;

            while (j < s.Length)
            {
                if (s[j] == '"')
                {
                    int k = j + 1;
                    int matchedHashes = 0;

                    while (k < s.Length && matchedHashes < hashCount && s[k] == '#')
                    {
                        matchedHashes++;
                        k++;
                    }

                    if (matchedHashes == hashCount)
                    {
                        i = k - 1;
                        return true;
                    }
                }

                j++;
            }

            i = s.Length - 1;
            return true;
        }

        /// <summary>
        /// Ignore une chaine Rust entre guillemets standards a partir de la position courante.
        /// </summary>
        /// <param name="i">Index courant, avance jusqu'a la fin du litteral.</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si une chaine a ete ignoree, sinon <c>false</c>.</returns>
        private static bool TrySkipQuotedString(ref int i, string s)
        {
            int start = i;

            if (s[i] == 'b')
            {
                if (i + 1 >= s.Length || s[i + 1] != '"')
                    return false;
                i++;
            }

            if (s[i] != '"')
            {
                i = start;
                return false;
            }

            i++;

            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (s[i] == '"')
                    return true;

                i++;
            }

            i = s.Length - 1;
            return true;
        }

        /// <summary>
        /// Ignore un litteral de caractere Rust a partir de la position courante.
        /// </summary>
        /// <param name="i">Index courant, avance jusqu'a la fin du litteral si valide.</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si un litteral de caractere a ete ignore, sinon <c>false</c>.</returns>
        private static bool TrySkipCharLiteral(ref int i, string s)
        {
            int start = i;

            if (s[i] == 'b')
            {
                if (i + 1 >= s.Length || s[i + 1] != '\'')
                    return false;
                i++;
            }

            if (s[i] != '\'')
            {
                i = start;
                return false;
            }

            if (i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_'))
            {
                i = start;
                return false;
            }

            i++;

            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (s[i] == '\'')
                    return true;

                if (s[i] == '\n' || s[i] == '\r')
                {
                    i = start;
                    return false;
                }

                i++;
            }

            i = start;
            return false;
        }

        private static LeadingLineDecision ClassifyLeadingTrivia(string trimmed, bool inBlockComment)
        {
            if (inBlockComment)
            {
                return trimmed.StartsWith("/*")
                    ? LeadingLineDecision.IncludeAndExitBlockComment
                    : LeadingLineDecision.Include;
            }

            if (trimmed.StartsWith("#[") || trimmed.StartsWith("#!["))
                return LeadingLineDecision.Include;

            if (trimmed.StartsWith("///") || trimmed.StartsWith("//!") || trimmed.StartsWith("//"))
                return LeadingLineDecision.Include;

            if (trimmed.EndsWith("*/") || trimmed.StartsWith("*"))
            {
                return trimmed.StartsWith("/*")
                    ? LeadingLineDecision.Include
                    : LeadingLineDecision.IncludeAndEnterBlockComment;
            }

            return trimmed.StartsWith("/*")
                ? LeadingLineDecision.Include
                : LeadingLineDecision.Stop;
        }
    }
}
