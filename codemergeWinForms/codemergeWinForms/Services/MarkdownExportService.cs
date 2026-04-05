using codemergeWinForms.Services.FunctionExtraction;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace codemergeWinForms.Services
{
    /// <summary>
    /// Genere un export Markdown d'un projet en regroupant fichiers, fonctions et assets.
    /// </summary>
    public class MarkdownExportService
    {
        private static readonly UTF8Encoding Utf8Strict = new(false, true);

        private static readonly Regex CharsetRegex = new(
            @"charset\s*=\s*[""']?(?<charset>[A-Za-z0-9._\-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex XmlEncodingRegex = new(
            @"encoding\s*=\s*[""'](?<charset>[A-Za-z0-9._\-]+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class RepositoryTreeNode
        {
            public required string Name { get; init; }

            public bool IsDirectory { get; set; }

            public Dictionary<string, RepositoryTreeNode> Children { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class FunctionEntry
        {
            public required string DisplayName { get; init; }

            public required string Anchor { get; init; }

            public required FunctionSpan Span { get; init; }

            public List<FunctionEntry> Children { get; } = new();
        }

        private readonly IRepositoryService _repositoryService;
        private readonly FunctionExtractService _functionExtract;

        static MarkdownExportService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// Initialise le service d'export Markdown avec ses dependances.
        /// </summary>
        public MarkdownExportService(IRepositoryService repositoryService, FunctionExtractService functionExtract)
        {
            _repositoryService = repositoryService;
            _functionExtract = functionExtract;
        }

        /// <summary>
        /// Construit le contenu Markdown complet d'un projet et retourne le nom de fichier cible.
        /// </summary>
        public async Task<(string PackageDirectoryPath, string MarkdownFilePath)> ExportProjectAsync(
            string projectId,
            string branch,
            List<string> selectedFiles,
            string outputDirectory,
            DateTime now)
        {
            ArgumentNullException.ThrowIfNull(selectedFiles);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            var (projectName, projectWebUrl) = await _repositoryService.GetProjectMetadataAsync(projectId);
            var repositoryItems = await _repositoryService.GetRepositoryTreeAsync(projectId, branch);
            var repositoryTreeText = BuildRepositoryTreeText(projectName, repositoryItems);
            string branchUrl = _repositoryService.BuildBranchUrl(projectWebUrl, branch);
            string timestamp = now.ToString("yyMMdd HHmmss");
            string safeProjectName = SanitizeFileNamePart(projectName);
            string safeBranch = SanitizeFileNamePart(branch);
            string fileName = $"{timestamp} {safeProjectName} [{safeBranch}].md";
            const string assetFolderName = "assets";
            string packageDirectoryName = Path.GetFileNameWithoutExtension(fileName);
            string packageDirectoryPath = GetUniqueDirectoryPath(outputDirectory, packageDirectoryName);

            Directory.CreateDirectory(packageDirectoryPath);

            var fileContents = new Dictionary<string, string>();
            var fileFunctions = new Dictionary<string, List<FunctionEntry>>();
            var imageFiles = new HashSet<string>();
            var pdfFiles = new HashSet<string>();
            var exportedFiles = new List<string>();

            foreach (var file in selectedFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;

                if (FileTypeHelper.IsKnownBinaryFile(file))
                    throw new InvalidOperationException(
                        $"Le fichier '{file}' a ete selectionne alors qu'il est detecte comme binaire.");

                if (FileTypeHelper.IsImageFile(file))
                {
                    await CopyAssetAsync(projectId, branch, file, packageDirectoryPath, assetFolderName);
                    imageFiles.Add(file);
                    fileFunctions[file] = new List<FunctionEntry>();
                    exportedFiles.Add(file);
                    continue;
                }

                if (FileTypeHelper.IsPdfFile(file))
                {
                    await CopyAssetAsync(projectId, branch, file, packageDirectoryPath, assetFolderName);
                    pdfFiles.Add(file);
                    fileFunctions[file] = new List<FunctionEntry>();
                    exportedFiles.Add(file);
                    continue;
                }

                var contentBytes = await _repositoryService.GetFileBytesAsync(projectId, file, branch);

                if (FileTypeHelper.IsUnknownFileType(file) && FileTypeHelper.LooksBinaryContent(contentBytes))
                {
                    throw new InvalidOperationException(
                        $"Le fichier '{file}' a ete selectionne alors qu'il a ete detecte comme binaire.");
                }

                var content = DecodeText(contentBytes, file);
                fileContents[file] = content;

                var language = LanguageDetector.GetMarkdownLanguage(file);
                fileFunctions[file] = FileTypeHelper.IsUnknownFileType(file)
                    ? new List<FunctionEntry>()
                    : BuildFunctionEntries(file, language, content);
                exportedFiles.Add(file);
            }

            var orderedExportedFiles = OrderExportedFiles(exportedFiles);
            var sb = new StringBuilder();

            sb.AppendLine($"# {EscapeMarkdownText(projectName)}");
            sb.AppendLine();
            sb.AppendLine($"Branche du depot : [{EscapeMarkdownText(branch)}]({branchUrl})");
            sb.AppendLine();
            sb.AppendLine($"## {EscapeMarkdownText(fileName)}");
            sb.AppendLine();
            sb.AppendLine("## Arborescence");
            sb.AppendLine();
            sb.AppendLine("~~~text");
            sb.AppendLine(repositoryTreeText);
            sb.AppendLine("~~~");
            sb.AppendLine();

            sb.AppendLine("## Table des matieres");
            sb.AppendLine();

            foreach (var file in orderedExportedFiles)
            {
                var fileAnchor = ToMarkdownAnchor($"file-{file}");
                sb.AppendLine($"- [{EscapeMarkdownText(file)}](#{fileAnchor})");

                foreach (var function in EnumerateFunctionEntries(fileFunctions[file]))
                {
                    sb.AppendLine($"  - [{EscapeMarkdownText(function.DisplayName)}](#{function.Anchor})");
                }
            }

            sb.AppendLine();

            foreach (var file in orderedExportedFiles)
            {
                var fileAnchor = ToMarkdownAnchor($"file-{file}");
                sb.AppendLine($"<a name=\"{fileAnchor}\"></a>");
                sb.AppendLine($"### {EscapeMarkdownText(file)}");
                sb.AppendLine();

                if (imageFiles.Contains(file))
                {
                    var markdownPath = ToMarkdownAssetPath(assetFolderName, file);
                    sb.AppendLine($"![{EscapeMarkdownText(Path.GetFileName(file))}]({markdownPath})");
                    sb.AppendLine();
                    continue;
                }

                if (pdfFiles.Contains(file))
                {
                    var markdownPath = ToMarkdownAssetPath(assetFolderName, file);
                    sb.AppendLine($"[{EscapeMarkdownText(Path.GetFileName(file))}]({markdownPath})");
                    sb.AppendLine();
                    continue;
                }

                var language = LanguageDetector.GetMarkdownLanguage(file);
                var content = fileContents[file];
                var functions = fileFunctions[file];

                var cursor = 0;

                foreach (var function in functions)
                {
                    var functionSpan = function.Span;

                    if (functionSpan.StartIndex > cursor)
                    {
                        var before = content.Substring(cursor, functionSpan.StartIndex - cursor);
                        AppendCodeBlockIfNotBlank(sb, language, before);
                    }

                    RenderFunctionEntry(sb, language, content, function);

                    cursor = functionSpan.EndIndex;
                }

                if (cursor < content.Length)
                {
                    var after = content.Substring(cursor);
                    AppendCodeBlockIfNotBlank(sb, language, after);
                }
            }

            var markdownFilePath = Path.Combine(packageDirectoryPath, fileName);
            await File.WriteAllTextAsync(markdownFilePath, sb.ToString(), new UTF8Encoding(false));
            return (packageDirectoryPath, markdownFilePath);
        }

        /// <summary>
        /// Construit la liste ordonnee des fonctions d'un fichier, en preservant les imbrications
        /// et en qualifiant les fonctions locales pour eviter les collisions de noms et d'ancres.
        /// </summary>
        private List<FunctionEntry> BuildFunctionEntries(string file, string language, string content)
        {
            var spans = _functionExtract
                .ExtractFunctionSpans(language, content)
                .Where(span =>
                    span.StartIndex >= 0
                    && span.EndIndex > span.StartIndex
                    && span.EndIndex <= content.Length)
                .OrderBy(span => span.StartIndex)
                .ThenByDescending(span => span.EndIndex)
                .ToList();

            if (spans.Count == 0)
                return new List<FunctionEntry>();

            var rootEntries = new List<FunctionEntry>();
            var stack = new Stack<(FunctionSpan Span, string QualifiedName)>();
            var entryStack = new Stack<FunctionEntry>();
            var usedQualifiedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var span in spans)
            {
                while (stack.Count > 0 && span.StartIndex >= stack.Peek().Span.EndIndex)
                {
                    stack.Pop();
                    entryStack.Pop();
                }

                while (stack.Count > 0 && !IsNestedWithin(span, stack.Peek().Span))
                {
                    stack.Pop();
                    entryStack.Pop();
                }

                var qualifiedName = stack.Count == 0
                    ? span.Name
                    : $"{stack.Peek().QualifiedName}.{span.Name}";

                var displayName = qualifiedName;

                if (usedQualifiedNames.TryGetValue(qualifiedName, out var occurrence))
                {
                    occurrence++;
                    usedQualifiedNames[qualifiedName] = occurrence;
                    displayName = $"{qualifiedName} ({occurrence})";
                }
                else
                {
                    usedQualifiedNames[qualifiedName] = 1;
                }

                var entry = new FunctionEntry
                {
                    DisplayName = displayName,
                    Anchor = ToMarkdownAnchor($"fn::{file}::{qualifiedName}::{span.StartIndex}"),
                    Span = span
                };

                if (entryStack.Count == 0)
                    rootEntries.Add(entry);
                else
                    entryStack.Peek().Children.Add(entry);

                stack.Push((span, qualifiedName));
                entryStack.Push(entry);
            }

            return rootEntries;
        }

        /// <summary>
        /// Trie les chemins exportes pour presenter le README racine en premier,
        /// puis les fichiers du dossier courant, puis les sous-dossiers, recursivement.
        /// </summary>
        private static List<string> OrderExportedFiles(IEnumerable<string> files)
        {
            var normalizedFiles = files
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Select(NormalizePath)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var orderedFiles = new List<string>();
            AppendOrderedFilesForDirectory(orderedFiles, normalizedFiles, string.Empty, isRootDirectory: true);
            return orderedFiles;
        }

        private static bool IsNestedWithin(FunctionSpan span, FunctionSpan candidateParent)
            => span.StartIndex >= candidateParent.StartIndex
                && span.EndIndex <= candidateParent.EndIndex
                && (span.StartIndex > candidateParent.StartIndex || span.EndIndex < candidateParent.EndIndex);

        private static IEnumerable<FunctionEntry> EnumerateFunctionEntries(IEnumerable<FunctionEntry> entries)
        {
            foreach (var entry in entries)
            {
                yield return entry;

                foreach (var child in EnumerateFunctionEntries(entry.Children))
                    yield return child;
            }
        }

        private static void RenderFunctionEntry(
            StringBuilder sb,
            string language,
            string content,
            FunctionEntry entry)
        {
            sb.AppendLine($"<a name=\"{entry.Anchor}\"></a>");
            sb.AppendLine($"#### {EscapeMarkdownText(entry.DisplayName)}");
            sb.AppendLine();

            if (entry.Children.Count == 0)
            {
                AppendCodeBlockIfNotBlank(
                    sb,
                    language,
                    content.Substring(entry.Span.StartIndex, entry.Span.EndIndex - entry.Span.StartIndex));
                return;
            }

            var cursor = entry.Span.StartIndex;

            foreach (var child in entry.Children.OrderBy(child => child.Span.StartIndex))
            {
                if (child.Span.StartIndex > cursor)
                {
                    var beforeChild = content.Substring(cursor, child.Span.StartIndex - cursor);
                    AppendCodeBlockIfNotBlank(sb, language, beforeChild);
                }

                RenderFunctionEntry(sb, language, content, child);
                cursor = child.Span.EndIndex;
            }

            if (cursor < entry.Span.EndIndex)
            {
                var afterChildren = content.Substring(cursor, entry.Span.EndIndex - cursor);
                AppendCodeBlockIfNotBlank(sb, language, afterChildren);
            }
        }

        /// <summary>
        /// Construit une representation texte de toute l'arborescence du depot.
        /// </summary>
        private static string BuildRepositoryTreeText(string projectName, IEnumerable<Models.GitLabTreeItem> items)
        {
            var root = new RepositoryTreeNode
            {
                Name = projectName,
                IsDirectory = true
            };

            foreach (var item in items
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                var normalizedPath = NormalizePath(item.Path);
                var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                    continue;

                var currentNode = root;

                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    var isDirectory = i < parts.Length - 1
                        || string.Equals(item.Type, "tree", StringComparison.OrdinalIgnoreCase);

                    if (!currentNode.Children.TryGetValue(part, out var childNode))
                    {
                        childNode = new RepositoryTreeNode
                        {
                            Name = part,
                            IsDirectory = isDirectory
                        };

                        currentNode.Children.Add(part, childNode);
                    }
                    else if (isDirectory)
                    {
                        childNode.IsDirectory = true;
                    }

                    currentNode = childNode;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{projectName}/");
            AppendRepositoryTreeNodeLines(sb, root, string.Empty);
            return sb.ToString().TrimEnd();
        }

        private static void AppendOrderedFilesForDirectory(
            List<string> orderedFiles,
            List<string> files,
            string currentDirectoryPath,
            bool isRootDirectory)
        {
            var directFiles = new List<string>();
            var childDirectories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var relativePath = currentDirectoryPath.Length == 0
                    ? file
                    : file[(currentDirectoryPath.Length + 1)..];

                var separatorIndex = relativePath.IndexOf('/');

                if (separatorIndex < 0)
                {
                    directFiles.Add(file);
                    continue;
                }

                var childDirectoryName = relativePath[..separatorIndex];

                if (!childDirectories.TryGetValue(childDirectoryName, out var childFiles))
                {
                    childFiles = new List<string>();
                    childDirectories.Add(childDirectoryName, childFiles);
                }

                childFiles.Add(file);
            }

            if (isRootDirectory)
            {
                foreach (var readmeFile in directFiles
                    .Where(IsRootReadmeFile)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    orderedFiles.Add(readmeFile);
                }

                foreach (var rootFile in directFiles
                    .Where(file => !IsRootReadmeFile(file))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    orderedFiles.Add(rootFile);
                }
            }
            else
            {
                foreach (var file in directFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                    orderedFiles.Add(file);
            }

            foreach (var childDirectory in childDirectories.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var childDirectoryPath = currentDirectoryPath.Length == 0
                    ? childDirectory
                    : $"{currentDirectoryPath}/{childDirectory}";

                AppendOrderedFilesForDirectory(
                    orderedFiles,
                    childDirectories[childDirectory],
                    childDirectoryPath,
                    isRootDirectory: false);
            }
        }

        private static void AppendRepositoryTreeNodeLines(
            StringBuilder sb,
            RepositoryTreeNode currentNode,
            string prefix)
        {
            var orderedChildren = currentNode.Children.Values
                .OrderByDescending(child => child.IsDirectory)
                .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < orderedChildren.Count; i++)
            {
                var child = orderedChildren[i];
                var isLast = i == orderedChildren.Count - 1;
                var connector = isLast ? "\\-- " : "|-- ";
                var childLabel = child.IsDirectory ? $"{child.Name}/" : child.Name;

                sb.Append(prefix);
                sb.Append(connector);
                sb.AppendLine(childLabel);

                if (!child.IsDirectory)
                    continue;

                var childPrefix = prefix + (isLast ? "    " : "|   ");
                AppendRepositoryTreeNodeLines(sb, child, childPrefix);
            }
        }

        private static string DecodeText(byte[] bytes, string filePath)
        {
            if (bytes.Length == 0)
                return string.Empty;

            if (TryDecodeUsingBom(bytes, out var decoded))
                return TrimBom(decoded);

            if (TryDecodeUsingDeclaredCharset(bytes, filePath, out decoded))
                return TrimBom(decoded);

            if (TryDecodeUtf8(bytes, out decoded))
                return TrimBom(decoded);

            return TrimBom(Encoding.Latin1.GetString(bytes));
        }

        private async Task CopyAssetAsync(
            string projectId,
            string branch,
            string file,
            string packageDirectoryPath,
            string assetFolderName)
        {
            var bytes = await _repositoryService.GetFileBytesAsync(projectId, file, branch);
            var assetRootPath = Path.Combine(packageDirectoryPath, assetFolderName);
            var outputAssetPath = Path.Combine(assetRootPath, file.Replace('/', Path.DirectorySeparatorChar));
            var outputDirectory = Path.GetDirectoryName(outputAssetPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            await File.WriteAllBytesAsync(outputAssetPath, bytes);
        }

        private static string GetUniqueDirectoryPath(string parentDirectory, string baseDirectoryName)
        {
            var candidate = Path.Combine(parentDirectory, baseDirectoryName);

            if (!Directory.Exists(candidate))
                return candidate;

            var suffix = 2;

            while (true)
            {
                candidate = Path.Combine(parentDirectory, $"{baseDirectoryName} ({suffix})");

                if (!Directory.Exists(candidate))
                    return candidate;

                suffix++;
            }
        }

        private static bool IsRootReadmeFile(string path)
        {
            if (path.Contains('/'))
                return false;

            var fileName = Path.GetFileName(path);

            return string.Equals(fileName, "README", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("README.", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
            => path.Replace('\\', '/');

        private static bool TryDecodeUsingBom(byte[] bytes, out string decoded)
        {
            decoded = string.Empty;

            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                decoded = Encoding.UTF8.GetString(bytes);
                return true;
            }

            if (bytes.Length >= 4
                && bytes[0] == 0xFF
                && bytes[1] == 0xFE
                && bytes[2] == 0x00
                && bytes[3] == 0x00)
            {
                decoded = Encoding.UTF32.GetString(bytes);
                return true;
            }

            if (bytes.Length >= 2
                && bytes[0] == 0xFF
                && bytes[1] == 0xFE)
            {
                decoded = Encoding.Unicode.GetString(bytes);
                return true;
            }

            if (bytes.Length >= 2
                && bytes[0] == 0xFE
                && bytes[1] == 0xFF)
            {
                decoded = Encoding.BigEndianUnicode.GetString(bytes);
                return true;
            }

            return false;
        }

        private static bool TryDecodeUsingDeclaredCharset(byte[] bytes, string filePath, out string decoded)
        {
            decoded = string.Empty;

            var extension = Path.GetExtension(filePath);

            if (!extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var sample = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 8192));
            var match = CharsetRegex.Match(sample);

            if (!match.Success)
                match = XmlEncodingRegex.Match(sample);

            if (!match.Success)
                return false;

            if (!TryGetEncoding(match.Groups["charset"].Value, out var encoding))
                return false;

            decoded = encoding.GetString(bytes);
            return true;
        }

        private static bool TryDecodeUtf8(byte[] bytes, out string decoded)
        {
            try
            {
                decoded = Utf8Strict.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                decoded = string.Empty;
                return false;
            }
        }

        private static bool TryGetEncoding(string charset, out Encoding encoding)
        {
            encoding = Encoding.UTF8;

            if (string.IsNullOrWhiteSpace(charset))
                return false;

            var normalized = charset.Trim().Trim('"', '\'').ToLowerInvariant();

            switch (normalized)
            {
                case "utf-8":
                case "utf8":
                    encoding = Encoding.UTF8;
                    return true;

                case "utf-16":
                case "utf-16le":
                case "unicode":
                    encoding = Encoding.Unicode;
                    return true;

                case "utf-16be":
                    encoding = Encoding.BigEndianUnicode;
                    return true;

                case "utf-32":
                    encoding = Encoding.UTF32;
                    return true;

                case "iso-8859-1":
                case "latin1":
                case "latin-1":
                    encoding = Encoding.Latin1;
                    return true;

                case "windows-1252":
                case "cp1252":
                    encoding = Encoding.GetEncoding(1252);
                    return true;
            }

            try
            {
                encoding = Encoding.GetEncoding(normalized);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string TrimBom(string text)
            => text.TrimStart('\uFEFF');

        /// <summary>
        /// Convertit une valeur libre en segment de nom de fichier compatible Windows.
        /// </summary>
        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "inconnu";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value
                .Select(c => invalidChars.Contains(c) ? '-' : c)
                .ToArray());

            sanitized = sanitized.Trim(' ', '.');

            return string.IsNullOrWhiteSpace(sanitized)
                ? "inconnu"
                : sanitized;
        }

        /// <summary>
        /// Convertit une chaine en ancre Markdown stable et lisible.
        /// </summary>
        private static string ToMarkdownAnchor(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "section";

            var sb = new StringBuilder();
            bool lastDash = false;

            foreach (var c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                    lastDash = false;
                }
                else if (!lastDash)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }

            var slug = sb.ToString().Trim('-');
            if (slug.Length == 0)
                slug = "section";

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 4)
                .ToLowerInvariant();

            return $"{slug}-{hash}";
        }

        /// <summary>
        /// Echappe les caracteres Markdown dans les libelles affiches.
        /// </summary>
        private static string EscapeMarkdownText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length + 8);

            foreach (var c in text)
            {
                if (c is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '!' or '|' or '<' or '>')
                    sb.Append('\\');

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Ajoute un bloc de code Markdown si le texte fourni n'est pas vide.
        /// </summary>
        private static void AppendCodeBlockIfNotBlank(StringBuilder sb, string language, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            sb.AppendLine($"~~~{language}");
            sb.AppendLine(text.TrimEnd());
            sb.AppendLine("~~~");
            sb.AppendLine();
        }

        /// <summary>
        /// Construit un chemin relatif Markdown encode pour un asset exporte.
        /// </summary>
        private static string ToMarkdownAssetPath(string assetFolderName, string file)
        {
            var relativePath = $"{assetFolderName}/{file}".Replace("\\", "/");

            return string.Join("/",
                relativePath
                    .Split('/')
                    .Select(Uri.EscapeDataString));
        }
    }
}
