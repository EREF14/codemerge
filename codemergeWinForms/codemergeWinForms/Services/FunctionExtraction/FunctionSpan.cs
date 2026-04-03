namespace codemergeWinForms.Services.FunctionExtraction;

/// <summary>
/// Decrit un bloc de fonction dans un texte source, avec son nom et ses indices de debut/fin.
/// </summary>
/// <param name="Name">Nom de la fonction detectee.</param>
/// <param name="StartIndex">Index de debut inclusif dans le contenu source.</param>
/// <param name="EndIndex">Index de fin exclusif dans le contenu source.</param>
public record FunctionSpan(string Name, int StartIndex, int EndIndex);