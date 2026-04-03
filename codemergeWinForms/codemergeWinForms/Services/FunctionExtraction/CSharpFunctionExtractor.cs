using System.Text.RegularExpressions;

namespace codemergeWinForms.Services.FunctionExtraction
{
    /// <summary>
    /// Extrait les fonctions C# et leurs spans via analyse regex + appariement d'accolades.
    /// </summary>
    public class CSharpFunctionExtractor : IFunctionExtractor
    {
        private sealed class TypeSpanCandidate
        {
            public required string DisplayName { get; init; }

            public required int StartIndex { get; init; }

            public required int BodyStartIndex { get; init; }

            public required int EndIndex { get; init; }
        }

        // Méthodes (inclut async, generics, etc.)
        private static readonly Regex RxMethod = new(
            @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|new|partial|async)\s+)*" +
            @"(?:(?<ret>[\w<>\[\],\s\?]+)\s+)?" +
            @"(?<name>[@\w]+)\s*\([^;{]*\)\s*\{",
            RegexOptions.Multiline);

        private static readonly Regex RxType = new(
            @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|abstract|sealed|partial|new|file)\s+)*" +
            @"(?<kind>class|struct|interface|record)\s+(?<name>[@\w]+)[^;{]*\{",
            RegexOptions.Multiline);

        private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "if","for","foreach","while","switch","catch","using","lock","return","throw","do","else","try"
        };

        /// <summary>
        /// Extrait les noms de methodes C# detectees dans le contenu fourni.
        /// </summary>
        /// <param name="content">Code source C# a analyser.</param>
        /// <returns>Enumeration des noms de methodes detectees.</returns>
        public IEnumerable<string> Extract(string content)
        {
            foreach (Match m in RxMethod.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (Keywords.Contains(name)) continue;
                yield return name;
            }
        }

        /// <summary>
        /// Extrait les spans complets des methodes C# detectees.
        /// </summary>
        /// <param name="content">Code source C# a analyser.</param>
        /// <returns>Enumeration des spans de methodes (debut inclusif, fin exclusive).</returns>
        public IEnumerable<FunctionSpan> ExtractSpans(string content)
        {
            var methodSpans = ExtractMethodSpans(content).ToList();
            var typeSpans = ExtractLeafTypeSpans(content, methodSpans).ToList();

            foreach (var span in methodSpans
                .Concat(typeSpans)
                .OrderBy(span => span.StartIndex)
                .ThenBy(span => span.EndIndex))
            {
                yield return span;
            }
        }

        private static IEnumerable<FunctionSpan> ExtractMethodSpans(string content)
        {
            foreach (Match m in RxMethod.Matches(content))
            {
                var name = m.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (Keywords.Contains(name)) continue;

                int start = m.Index;

                // On cherche l'accolade ouvrante matchée par la regex (dernier '{' du match)
                int openBrace = content.LastIndexOf('{', m.Index + m.Length - 1, m.Length);
                if (openBrace < 0) continue;

                int end = FindMatchingBrace(content, openBrace);
                if (end < 0) continue;

                start = ExpandStartToIncludeLeadingComments(content, start);

                yield return new FunctionSpan(name, start, end + 1);
            }
        }

        private static IEnumerable<FunctionSpan> ExtractLeafTypeSpans(
            string content,
            IReadOnlyCollection<FunctionSpan> methodSpans)
        {
            var typeCandidates = new List<TypeSpanCandidate>();

            foreach (Match m in RxType.Matches(content))
            {
                var typeName = m.Groups["name"].Value;
                var typeKind = m.Groups["kind"].Value;

                if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(typeKind))
                    continue;

                int start = ExpandStartToIncludeLeadingComments(content, m.Index);

                int openBrace = content.LastIndexOf('{', m.Index + m.Length - 1, m.Length);
                if (openBrace < 0)
                    continue;

                int end = FindMatchingBrace(content, openBrace);
                if (end < 0)
                    continue;

                typeCandidates.Add(new TypeSpanCandidate
                {
                    DisplayName = $"{typeKind} {typeName}",
                    StartIndex = start,
                    BodyStartIndex = openBrace,
                    EndIndex = end + 1
                });
            }

            foreach (var candidate in typeCandidates)
            {
                if (ContainsNestedMethod(methodSpans, candidate))
                    continue;

                if (ContainsNestedType(typeCandidates, candidate))
                    continue;

                yield return new FunctionSpan(
                    candidate.DisplayName,
                    candidate.StartIndex,
                    candidate.EndIndex);
            }
        }

        private static bool ContainsNestedMethod(
            IReadOnlyCollection<FunctionSpan> methodSpans,
            TypeSpanCandidate candidate)
            => methodSpans.Any(span =>
                span.StartIndex > candidate.BodyStartIndex
                && span.StartIndex < candidate.EndIndex);

        private static bool ContainsNestedType(
            IReadOnlyCollection<TypeSpanCandidate> typeCandidates,
            TypeSpanCandidate candidate)
            => typeCandidates.Any(other =>
                !ReferenceEquals(other, candidate)
                && other.StartIndex > candidate.BodyStartIndex
                && other.StartIndex < candidate.EndIndex);

        /// <summary>
        /// Trouve l'accolade fermante correspondant a une accolade ouvrante donnee.
        /// </summary>
        /// <param name="s">Contenu source complet.</param>
        /// <param name="openIndex">Index de l'accolade ouvrante.</param>
        /// <returns>Index de l'accolade fermante correspondante, ou -1 si introuvable.</returns>
        private static int FindMatchingBrace(string s, int openIndex)
        {
            int depth = 0;
            bool inString = false;
            bool inChar = false;
            bool inLineComment = false;
            bool inBlockComment = false;
            bool verbatim = false;

            for (int i = openIndex; i < s.Length; i++)
            {
                char c = s[i];
                char n = (i + 1 < s.Length) ? s[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '*' && n == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (!inString && !inChar)
                {
                    if (c == '/' && n == '/') { inLineComment = true; i++; continue; }
                    if (c == '/' && n == '*') { inBlockComment = true; i++; continue; }
                }

                if (!inChar)
                {
                    if (!inString)
                    {
                        // start string
                        if (c == '@' && n == '"') { inString = true; verbatim = true; i++; continue; }
                        if (c == '"') { inString = true; verbatim = false; continue; }
                    }
                    else
                    {
                        if (verbatim)
                        {
                            // "" échappe "
                            if (c == '"' && n == '"') { i++; continue; }
                            if (c == '"') { inString = false; continue; }
                        }
                        else
                        {
                            if (c == '\\') { i++; continue; }
                            if (c == '"') { inString = false; continue; }
                        }
                        continue;
                    }
                }

                if (!inString)
                {
                    if (!inChar)
                    {
                        if (c == '\'') { inChar = true; continue; }
                    }
                    else
                    {
                        if (c == '\\') { i++; continue; }
                        if (c == '\'') { inChar = false; continue; }
                        continue;
                    }
                }

                if (inString || inChar) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Etend le debut d'une fonction pour inclure attributs et commentaires immediatement precedents.
        /// </summary>
        /// <param name="content">Contenu source complet.</param>
        /// <param name="start">Index de debut initial de la fonction.</param>
        /// <returns>Index de debut etendu.</returns>
        private static int ExpandStartToIncludeLeadingComments(string content, int start)
            => FunctionExtractionTextHelper.ExpandStartToIncludeLeadingLines(content, start, ClassifyLeadingTrivia);

        private static LeadingLineDecision ClassifyLeadingTrivia(string trimmed, bool inBlockComment)
        {
            if (inBlockComment)
            {
                return trimmed.StartsWith("/*") || trimmed.StartsWith("/**")
                    ? LeadingLineDecision.IncludeAndExitBlockComment
                    : LeadingLineDecision.Include;
            }

            if (trimmed.StartsWith("//"))
                return LeadingLineDecision.Include;

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
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
