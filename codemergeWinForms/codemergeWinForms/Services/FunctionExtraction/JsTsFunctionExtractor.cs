using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions JavaScript/TypeScript (declarations, arrows et methodes de classe).
    /// </summary>
    public class JsTsFunctionExtractor : IFunctionExtractor
    {
        // function foo(...) { ... }
        // async function foo(...) { ... }
        private static readonly Regex RxFunction = new(
            @"^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s+(?<name>[\w$]+)\s*\(",
            RegexOptions.Multiline);

        // const foo = (...) => { ... }
        // const foo = async (...) => { ... }
        private static readonly Regex RxArrow = new(
            @"^\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[\w$]+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[\w$]+)\s*=>",
            RegexOptions.Multiline);

        // class method() { ... }
        // async method() { ... }
        private static readonly Regex RxMethod = new(
            @"^\s*(?:static\s+)?(?:async\s+)?(?<name>[\w$]+)\s*\([^)]*\)\s*\{",
            RegexOptions.Multiline);

        private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "if", "for", "while", "switch", "catch", "function", "else", "do", "try", "constructor"
        };

        /// <summary>
        /// Extrait les noms de fonctions JS/TS detectees.
        /// </summary>
        /// <param name="content">Code source JS/TS a analyser.</param>
        /// <returns>Enumeration des noms de fonctions detectees.</returns>
        public IEnumerable<string> Extract(string content)
            => ExtractSpans(content).Select(s => s.Name);

        /// <summary>
        /// Extrait les spans des fonctions JS/TS detectees.
        /// </summary>
        /// <param name="content">Code source JS/TS a analyser.</param>
        /// <returns>Enumeration des spans de fonctions (debut inclusif, fin exclusive).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            var matches = new List<(string Name, int Index)>();

            foreach (Match m in RxFunction.Matches(content))
                matches.Add((m.Groups["name"].Value, m.Index));

            foreach (Match m in RxArrow.Matches(content))
                matches.Add((m.Groups["name"].Value, m.Index));

            foreach (Match m in RxMethod.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (!Keywords.Contains(name))
                    matches.Add((name, m.Index));
            }

            matches = matches
                .OrderBy(m => m.Index)
                .GroupBy(m => (m.Name, m.Index))
                .Select(g => g.First())
                .ToList();

            foreach (var match in matches)
            {
                int start = ExpandStartToIncludeLeadingComments(content, match.Index);
                int bodyStart = FindBodyStart(content, match.Index);
                if (bodyStart < 0)
                    continue;

                int end;

                if (content[bodyStart] == '{')
                {
                    end = FindMatchingBrace(content, bodyStart);
                    if (end < 0)
                        continue;

                    yield return new FunctionSpan(match.Name, start, end + 1);
                }
                else
                {
                    // cas arrow expression: const f = x => x + 1;
                    end = FindStatementEnd(content, bodyStart);
                    if (end < 0)
                        continue;

                    yield return new FunctionSpan(match.Name, start, end + 1);
                }
            }
        }

        /// <summary>
        /// Etend le debut d'une fonction pour inclure les commentaires JS/TS et decorateurs qui la precedent.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="start">Index de debut initial.</param>
        /// <returns>Index de debut etendu.</returns>
        private static int ExpandStartToIncludeLeadingComments(string content, int start)
            => FunctionExtractionTextHelper.ExpandStartToIncludeLeadingLines(content, start, ClassifyLeadingTrivia);

        /// <summary>
        /// Trouve le debut du corps de fonction (accolade ouvrante ou expression arrow).
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="startIndex">Index de debut de la signature.</param>
        /// <returns>Index de debut du corps, ou -1 si introuvable.</returns>
        private static int FindBodyStart(string content, int startIndex)
        {
            bool seenArrow = false;

            for (int i = startIndex; i < content.Length; i++)
            {
                if (TrySkipStringOrComment(ref i, content))
                    continue;

                if (content[i] == '{')
                    return i;

                if (content[i] == '=' && i + 1 < content.Length && content[i + 1] == '>')
                {
                    seenArrow = true;
                    i++;
                    continue;
                }

                if (seenArrow && !char.IsWhiteSpace(content[i]))
                    return i;
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
                if (TrySkipStringOrComment(ref i, content))
                    continue;

                if (content[i] == '{')
                    depth++;
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Trouve la fin d'une expression de type statement (jusqu'au ';').
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="startIndex">Index de debut de l'expression.</param>
        /// <returns>Index du ';' terminal, ou -1 si introuvable.</returns>
        private static int FindStatementEnd(string content, int startIndex)
        {
            for (int i = startIndex; i < content.Length; i++)
            {
                if (TrySkipStringOrComment(ref i, content))
                    continue;

                if (content[i] == ';')
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Ignore une chaine ou un commentaire a partir de l'index courant.
        /// </summary>
        /// <param name="i">Index courant (modifie pour avancer apres le bloc ignore).</param>
        /// <param name="s">Contenu source complet.</param>
        /// <returns><c>true</c> si un bloc a ete ignore, sinon <c>false</c>.</returns>
        private static bool TrySkipStringOrComment(ref int i, string s)
        {
            // line comment
            if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '/')
            {
                i += 2;
                while (i < s.Length && s[i] != '\n')
                    i++;
                return true;
            }

            // block comment
            if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/'))
                    i++;
                i = Math.Min(i + 1, s.Length - 1);
                return true;
            }

            // strings
            if (s[i] == '"' || s[i] == '\'' || s[i] == '`')
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
                        break;

                    i++;
                }

                return true;
            }

            return false;
        }

        private static LeadingLineDecision ClassifyLeadingTrivia(string trimmed, bool inBlockComment)
        {
            if (inBlockComment)
            {
                return trimmed.StartsWith("/*") || trimmed.StartsWith("/**")
                    ? LeadingLineDecision.IncludeAndExitBlockComment
                    : LeadingLineDecision.Include;
            }

            if (trimmed.StartsWith("//") || trimmed.StartsWith("@"))
                return LeadingLineDecision.Include;

            if (trimmed.EndsWith("*/"))
            {
                return trimmed.StartsWith("/*") || trimmed.StartsWith("/**")
                    ? LeadingLineDecision.Include
                    : LeadingLineDecision.IncludeAndEnterBlockComment;
            }

            return trimmed.StartsWith("*")
                ? LeadingLineDecision.Include
                : LeadingLineDecision.Stop;
        }
    }
}
