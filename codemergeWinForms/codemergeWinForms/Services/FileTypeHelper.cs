namespace codemergeWinForms.Services
{
    /// <summary>
    /// Centralise la classification des fichiers par extension ou nom connu.
    /// </summary>
    public static class FileTypeHelper
    {
        private static readonly Dictionary<string, string> MarkdownLanguagesByFileName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dockerfile"] = "docker",
            ["Makefile"] = "makefile",
            ["Doxyfile"] = "ini",
            ["CMakeLists.txt"] = "cmake",
            ["Jenkinsfile"] = "groovy",
            ["Gemfile"] = "ruby",
            ["Rakefile"] = "ruby",
            ["Vagrantfile"] = "ruby",
            ["Podfile"] = "ruby",
            ["Fastfile"] = "ruby",
            ["Brewfile"] = "ruby"
        };

        private static readonly Dictionary<string, string> MarkdownLanguagesByExtension = new(StringComparer.OrdinalIgnoreCase)
        {
            [".c"] = "c",
            [".h"] = "c",
            [".cpp"] = "cpp",
            [".cc"] = "cpp",
            [".cxx"] = "cpp",
            [".hpp"] = "cpp",
            [".hh"] = "cpp",

            [".cs"] = "csharp",
            [".csproj"] = "xml",
            [".vbproj"] = "xml",
            [".fsproj"] = "xml",
            [".vcxproj"] = "xml",
            [".resx"] = "xml",
            [".config"] = "xml",
            [".props"] = "xml",
            [".targets"] = "xml",
            [".sln"] = "plaintext",

            [".java"] = "java",
            [".kt"] = "kotlin",
            [".kts"] = "kotlin",
            [".groovy"] = "groovy",
            [".scala"] = "scala",

            [".js"] = "javascript",
            [".mjs"] = "javascript",
            [".cjs"] = "javascript",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".jsx"] = "javascript",
            [".vue"] = "vue",
            [".svelte"] = "svelte",

            [".html"] = "html",
            [".htm"] = "html",
            [".css"] = "css",
            [".scss"] = "scss",
            [".sass"] = "sass",
            [".less"] = "less",

            [".py"] = "python",
            [".rb"] = "ruby",
            [".php"] = "php",
            [".go"] = "go",
            [".rs"] = "rust",
            [".swift"] = "swift",
            [".lua"] = "lua",
            [".dart"] = "dart",
            [".pl"] = "perl",
            [".r"] = "r",

            [".gd"] = "gdscript",
            [".shader"] = "glsl",
            [".glsl"] = "glsl",
            [".hlsl"] = "hlsl",

            [".fs"] = "fsharp",
            [".fsx"] = "fsharp",
            [".elm"] = "elm",
            [".clj"] = "clojure",

            [".json"] = "json",
            [".xml"] = "xml",
            [".yml"] = "yaml",
            [".yaml"] = "yaml",
            [".toml"] = "toml",
            [".ini"] = "ini",
            [".cfg"] = "ini",
            [".csv"] = "csv",

            [".sql"] = "sql",

            [".sh"] = "bash",
            [".bash"] = "bash",
            [".zsh"] = "bash",
            [".ps1"] = "powershell",
            [".bat"] = "batch",

            [".md"] = "markdown",
            [".txt"] = "plaintext",
            [".log"] = "plaintext",

            [".dockerfile"] = "docker",
            [".env"] = "bash",
            [".editorconfig"] = "ini",
            [".gitmodules"] = "ini",
            [".npmrc"] = "ini",
            [".coveragerc"] = "ini",
            [".gitignore"] = "gitignore",
            [".gitattributes"] = "gitignore"
        };

        public static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".lz", ".lz4", ".lzma", ".zst", ".cab",
            ".jar", ".war", ".ear", ".nupkg", ".snupkg", ".vsix", ".whl",

            ".exe", ".dll", ".so", ".dylib", ".bin", ".msi", ".app", ".deb", ".rpm", ".com", ".scr", ".sys", ".drv", ".ocx",
            ".appx", ".appxbundle", ".msix", ".msixbundle", ".apk", ".ipa",

            ".o", ".obj", ".a", ".lib", ".class", ".pyc", ".pyo", ".pyd", ".pdb", ".ilk", ".idb", ".pch", ".ipch", ".wasm", ".dex",

            ".doc", ".docx", ".docm",
            ".xls", ".xlsx", ".xlsm",
            ".ppt", ".pptx", ".pptm",

            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma",
            ".mp4", ".avi", ".mov", ".mkv", ".webm", ".flv", ".wmv", ".mpeg", ".mpg", ".m4v", ".3gp",

            ".ttf", ".otf", ".woff", ".woff2", ".eot",

            ".sqlite", ".sqlite3", ".db", ".db3", ".mdb", ".accdb", ".sdf", ".parquet",

            ".psd", ".ai", ".sketch", ".fig",

            ".iso", ".dmg"
        };

        public static readonly HashSet<string> TextExtensions = new(MarkdownLanguagesByExtension.Keys, StringComparer.OrdinalIgnoreCase);

        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
        };

        /// <summary>
        /// Determine si un fichier est une image exportable.
        /// </summary>
        public static bool IsImageFile(string path)
            => ImageExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// Determine si un fichier est un PDF.
        /// </summary>
        public static bool IsPdfFile(string path)
            => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Determine si un fichier est un texte connu.
        /// </summary>
        public static bool IsKnownTextFile(string path)
        {
            var fileName = Path.GetFileName(path);

            return TextExtensions.Contains(Path.GetExtension(path))
                || MarkdownLanguagesByFileName.ContainsKey(fileName)
                || IsDotEnvLikeFile(fileName);
        }

        /// <summary>
        /// Retourne le langage Markdown associe a un fichier connu.
        /// </summary>
        public static string GetMarkdownLanguage(string path)
        {
            var fileName = Path.GetFileName(path);

            if (MarkdownLanguagesByFileName.TryGetValue(fileName, out var specialLanguage))
                return specialLanguage;

            if (IsDotEnvLikeFile(fileName))
                return "bash";

            return MarkdownLanguagesByExtension.TryGetValue(Path.GetExtension(path), out var language)
                ? language
                : "plaintext";
        }

        /// <summary>
        /// Determine si un fichier est un binaire connu.
        /// </summary>
        public static bool IsKnownBinaryFile(string path)
            => BinaryExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// Determine si un fichier peut etre coche dans l'arbre.
        /// </summary>
        public static bool IsSelectableInTree(string path)
            => IsImageFile(path)
            || IsPdfFile(path)
            || IsKnownTextFile(path);

        /// <summary>
        /// Determine si un fichier ne correspond a aucun type connu par extension.
        /// </summary>
        public static bool IsUnknownFileType(string path)
            => !IsImageFile(path)
            && !IsPdfFile(path)
            && !IsKnownTextFile(path)
            && !IsKnownBinaryFile(path);

        /// <summary>
        /// Determine si un contenu binaire ressemble a un fichier non texte.
        /// </summary>
        public static bool LooksBinaryContent(byte[] bytes)
        {
            if (bytes.Length == 0)
                return false;

            var sampleLength = Math.Min(bytes.Length, 4096);
            var nonPrintableCount = 0;

            for (var i = 0; i < sampleLength; i++)
            {
                var value = bytes[i];

                if (value == 0x00)
                    return true;

                if (value == 0x7F)
                {
                    nonPrintableCount++;
                    continue;
                }

                if (value < 0x20 && value is not (byte)'\t' and not (byte)'\n' and not (byte)'\r' and not 0x0C)
                    nonPrintableCount++;
            }

            return nonPrintableCount * 100 / sampleLength > 30;
        }

        private static bool IsDotEnvLikeFile(string fileName)
            => fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase);
    }
}
