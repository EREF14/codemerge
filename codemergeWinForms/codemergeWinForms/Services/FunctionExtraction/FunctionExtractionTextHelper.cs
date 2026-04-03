namespace codemergeWinForms.Services.FunctionExtraction
{
    internal enum LeadingLineDecision
    {
        Stop,
        Include,
        IncludeAndEnterBlockComment,
        IncludeAndExitBlockComment
    }

    /// <summary>
    /// Mutualise les operations de navigation dans le texte utilisees par les extracteurs.
    /// </summary>
    internal static class FunctionExtractionTextHelper
    {
        internal static int ExpandStartToIncludeLeadingLines(
            string content,
            int start,
            Func<string, bool, LeadingLineDecision> classifyLine,
            bool includeBlankLinesInResult = false,
            bool requireIncludedTriviaBeforeBlankLines = false)
        {
            int functionLineStart = FindLineStart(content, start);
            int currentLineStart = functionLineStart;
            int candidateStart = functionLineStart;
            bool hasIncludedTrivia = false;
            bool inBlockComment = false;

            while (currentLineStart > 0)
            {
                int previousLineEnd = currentLineStart - 1;
                int previousLineStart = FindLineStart(content, previousLineEnd);
                string line = content.Substring(previousLineStart, previousLineEnd - previousLineStart + 1);
                string trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (includeBlankLinesInResult
                        && (!requireIncludedTriviaBeforeBlankLines || hasIncludedTrivia))
                    {
                        candidateStart = previousLineStart;
                    }

                    currentLineStart = previousLineStart;
                    continue;
                }

                switch (classifyLine(trimmed, inBlockComment))
                {
                    case LeadingLineDecision.Include:
                        candidateStart = previousLineStart;
                        hasIncludedTrivia = true;
                        currentLineStart = previousLineStart;
                        continue;

                    case LeadingLineDecision.IncludeAndEnterBlockComment:
                        candidateStart = previousLineStart;
                        hasIncludedTrivia = true;
                        inBlockComment = true;
                        currentLineStart = previousLineStart;
                        continue;

                    case LeadingLineDecision.IncludeAndExitBlockComment:
                        candidateStart = previousLineStart;
                        hasIncludedTrivia = true;
                        inBlockComment = false;
                        currentLineStart = previousLineStart;
                        continue;

                    default:
                        return candidateStart;
                }
            }

            return candidateStart;
        }

        internal static int FindLineStart(string content, int index)
        {
            int i = Math.Min(index, content.Length);
            while (i > 0 && content[i - 1] != '\n')
                i--;
            return i;
        }
    }
}
