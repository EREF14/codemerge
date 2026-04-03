using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions Lua (declarations nommees et assignees) avec leurs spans.
    /// </summary>
    public class LuaFunctionExtractor : IFunctionExtractor
    {
        private static readonly Regex RxNamedDecl =
            new(@"(?m)^\s*(?:local\s+)?function\s+(?<name>[A-Za-z_]\w*(?:[.:][A-Za-z_]\w*)*)\s*\(",
                RegexOptions.Compiled);

        private static readonly Regex RxAssignedDecl =
            new(@"(?m)^\s*(?:local\s+)?(?<name>[A-Za-z_]\w*(?:[.:][A-Za-z_]\w*)*)\s*=\s*function\s*\(",
                RegexOptions.Compiled);

        /// <summary>
        /// Extrait les noms de fonctions Lua detectees.
        /// </summary>
        /// <param name="content">Code source Lua a analyser.</param>
        /// <returns>Enumeration des noms de fonctions detectees.</returns>
        public IEnumerable<string> Extract(string content)
            => ExtractSpans(content).Select(s => s.Name);

        /// <summary>
        /// Extrait les spans des fonctions Lua detectees.
        /// </summary>
        /// <param name="content">Code source Lua a analyser.</param>
        /// <returns>Enumeration des spans de fonctions (debut inclusif, fin exclusive).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            var candidates = new List<(string Name, int Index)>();

            foreach (Match m in RxNamedDecl.Matches(content))
                candidates.Add((m.Groups["name"].Value.Trim(), m.Index));

            foreach (Match m in RxAssignedDecl.Matches(content))
                candidates.Add((m.Groups["name"].Value.Trim(), m.Index));

            candidates.Sort((a, b) => a.Index.CompareTo(b.Index));

            foreach (var candidate in candidates)
            {
                int start = ExpandStartToIncludeLeadingComments(content, candidate.Index);
                int end = FindLuaFunctionEnd(content, candidate.Index);

                if (end > start)
                    yield return new FunctionSpan(candidate.Name, start, end);
            }
        }

        /// <summary>
        /// Etend le debut d'une fonction pour inclure les commentaires '--' qui la precedent.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="start">Index de debut initial.</param>
        /// <returns>Index de debut etendu.</returns>
        private static int ExpandStartToIncludeLeadingComments(string content, int start)
            => FunctionExtractionTextHelper.ExpandStartToIncludeLeadingLines(content, start, ClassifyLeadingTrivia);

        /// <summary>
        /// Trouve la fin logique d'une fonction Lua en suivant la profondeur des blocs.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="startIndex">Index de debut de la fonction.</param>
        /// <returns>Index de fin exclusif de la fonction, ou -1 si introuvable.</returns>
        private static int FindLuaFunctionEnd(string content, int startIndex)
        {
            int i = startIndex;
            int depth = 0;

            while (i < content.Length)
            {
                SkipWhitespace(ref i, content);
                if (i >= content.Length)
                    break;

                if (TrySkipComment(ref i, content))
                    continue;

                if (TrySkipString(ref i, content))
                    continue;

                if (IsIdentifierStart(content[i]))
                {
                    string token = ReadIdentifier(ref i, content);

                    switch (token)
                    {
                        case "function":
                        case "if":
                        case "for":
                        case "while":
                        case "repeat":
                            depth++;
                            break;

                        case "until":
                        case "end":
                            depth--;
                            if (depth == 0)
                                return i;
                            break;
                    }

                    continue;
                }

                i++;
            }

            return -1;
        }

        /// <summary>
        /// Avance l'index sur les espaces/tabulations/nouvelles lignes.
        /// </summary>
        /// <param name="i">Index courant a faire avancer.</param>
        /// <param name="s">Contenu source complet.</param>
        private static void SkipWhitespace(ref int i, string s)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        /// <summary>
        /// Ignore un commentaire Lua (ligne ou bloc) a partir de l'index courant.
        /// </summary>
        /// <param name="i">Index courant (modifie pour avancer apres le commentaire).</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si un commentaire a ete ignore, sinon <c>false</c>.</returns>
        private static bool TrySkipComment(ref int i, string s)
        {
            // commentaire simple -- ...
            if (i + 1 < s.Length && s[i] == '-' && s[i + 1] == '-')
            {
                // commentaire bloc --[[ ... ]]
                if (i + 3 < s.Length && s[i + 2] == '[' && s[i + 3] == '[')
                {
                    i += 4;
                    while (i + 1 < s.Length && !(s[i] == ']' && s[i + 1] == ']'))
                        i++;
                    i = Math.Min(i + 2, s.Length);
                    return true;
                }

                i += 2;
                while (i < s.Length && s[i] != '\n')
                    i++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ignore une chaine Lua (simple, double ou longue) a partir de l'index courant.
        /// </summary>
        /// <param name="i">Index courant (modifie pour avancer apres la chaine).</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si une chaine a ete ignoree, sinon <c>false</c>.</returns>
        private static bool TrySkipString(ref int i, string s)
        {
            // long string [[ ... ]]
            if (i + 1 < s.Length && s[i] == '[' && s[i + 1] == '[')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == ']' && s[i + 1] == ']'))
                    i++;
                i = Math.Min(i + 2, s.Length);
                return true;
            }

            // "..." ou '...'
            if (s[i] == '"' || s[i] == '\'')
            {
                char q = s[i];
                i++;

                while (i < s.Length)
                {
                    if (s[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (s[i] == q)
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Indique si un caractere peut commencer un identifiant Lua.
        /// </summary>
        /// <param name="c">Caractere teste.</param>
        /// <returns><c>true</c> si le caractere peut commencer un identifiant.</returns>
        private static bool IsIdentifierStart(char c)
            => char.IsLetter(c) || c == '_';

        /// <summary>
        /// Indique si un caractere peut faire partie d'un identifiant Lua.
        /// </summary>
        /// <param name="c">Caractere teste.</param>
        /// <returns><c>true</c> si le caractere est valide dans un identifiant.</returns>
        private static bool IsIdentifierPart(char c)
            => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>
        /// Lit un identifiant Lua a partir de la position courante.
        /// </summary>
        /// <param name="i">Index de lecture (avance jusqu'apres l'identifiant).</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns>Identifiant lu.</returns>
        private static string ReadIdentifier(ref int i, string s)
        {
            int start = i;
            i++;

            while (i < s.Length && IsIdentifierPart(s[i]))
                i++;

            return s.Substring(start, i - start);
        }

        private static LeadingLineDecision ClassifyLeadingTrivia(string trimmed, bool inBlockComment)
            => trimmed.StartsWith("--")
                ? LeadingLineDecision.Include
                : LeadingLineDecision.Stop;
    }
}
