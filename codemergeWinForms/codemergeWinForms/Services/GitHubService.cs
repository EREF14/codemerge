using codemergeWinForms.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace codemergeWinForms.Services
{
    public class GitHubService : IRepositoryService
    {
        private const string ApiBaseUrl = "https://api.github.com";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _client;

        public GitHubService(string? token)
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("CodeMergeWinForms");

            if (!string.IsNullOrWhiteSpace(token))
            {
                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token.Trim());
            }
        }

        public async Task<List<GitLabTreeItem>> GetRepositoryTreeAsync(string repositoryId, string branch)
        {
            ValidateRequired(repositoryId, nameof(repositoryId), "Repository cannot be null or empty.");
            ValidateRequired(branch, nameof(branch), "Branch cannot be null or empty.");

            var (owner, repo) = ParseRepositoryId(repositoryId);

            try
            {
                var url =
                    $"{ApiBaseUrl}/repos/{Encode(owner)}/{Encode(repo)}/git/trees/{Encode(branch)}?recursive=1";

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitHub tree request failed.", repositoryId, branch);

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("truncated", out var truncatedProperty)
                    && truncatedProperty.ValueKind == JsonValueKind.True)
                {
                    throw new InvalidOperationException(
                        $"GitHub a tronque l'arborescence du depot '{repositoryId}' sur la branche '{branch}'.");
                }

                if (!doc.RootElement.TryGetProperty("tree", out var treeProperty)
                    || treeProperty.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("GitHub tree response does not contain a valid 'tree' array.");
                }

                var items = new List<GitLabTreeItem>();

                foreach (var entry in treeProperty.EnumerateArray())
                {
                    if (!entry.TryGetProperty("path", out var pathProperty)
                        || !entry.TryGetProperty("type", out var typeProperty))
                    {
                        continue;
                    }

                    var path = pathProperty.GetString();
                    var type = typeProperty.GetString();

                    if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(type))
                        continue;

                    items.Add(new GitLabTreeItem
                    {
                        Path = path,
                        Type = type
                    });
                }

                return items;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve repository tree for repository '{repositoryId}' on branch '{branch}'.",
                    ex);
            }
        }

        public async Task<string> GetFileContentAsync(string repositoryId, string filePath, string branch)
        {
            var bytes = await GetFileBytesAsync(repositoryId, filePath, branch);
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<byte[]> GetFileBytesAsync(string repositoryId, string filePath, string branch)
        {
            ValidateFileRequest(repositoryId, filePath, branch);

            var (owner, repo) = ParseRepositoryId(repositoryId);

            try
            {
                var url =
                    $"{ApiBaseUrl}/repos/{Encode(owner)}/{Encode(repo)}/contents/{Encode(filePath)}?ref={Encode(branch)}";

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitHub file request failed.", repositoryId, branch, filePath);

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("type", out var typeProperty)
                    || !string.Equals(typeProperty.GetString(), "file", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Le chemin '{filePath}' du depot '{repositoryId}' n'est pas un fichier GitHub.");
                }

                if (!doc.RootElement.TryGetProperty("content", out var contentProperty))
                    throw new JsonException("GitHub file response does not contain property 'content'.");

                var content = contentProperty.GetString()
                    ?? throw new JsonException("GitHub file content is null.");

                return Convert.FromBase64String(content.Replace("\n", string.Empty).Replace("\r", string.Empty));
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve file bytes for '{filePath}' in repository '{repositoryId}' on branch '{branch}'.",
                    ex);
            }
        }

        public async Task<long?> GetFileSizeAsync(string repositoryId, string filePath, string branch)
        {
            ValidateFileRequest(repositoryId, filePath, branch);

            var (owner, repo) = ParseRepositoryId(repositoryId);

            try
            {
                var url =
                    $"{ApiBaseUrl}/repos/{Encode(owner)}/{Encode(repo)}/contents/{Encode(filePath)}?ref={Encode(branch)}";

                using var response = await _client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("size", out var sizeProperty)
                    && sizeProperty.ValueKind == JsonValueKind.Number
                    && sizeProperty.TryGetInt64(out var size))
                {
                    return size;
                }

                return null;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve file size for '{filePath}' in repository '{repositoryId}' on branch '{branch}'.",
                    ex);
            }
        }

        public async Task<(string Name, string WebUrl)> GetProjectMetadataAsync(string repositoryId)
        {
            ValidateRequired(repositoryId, nameof(repositoryId), "Repository cannot be null or empty.");

            var (owner, repo) = ParseRepositoryId(repositoryId);

            try
            {
                var url = $"{ApiBaseUrl}/repos/{Encode(owner)}/{Encode(repo)}";

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitHub repository request failed.", repositoryId);

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("name", out var nameProperty))
                    throw new JsonException("GitHub response does not contain property 'name'.");

                if (!doc.RootElement.TryGetProperty("html_url", out var urlProperty))
                    throw new JsonException("GitHub response does not contain property 'html_url'.");

                var name = nameProperty.GetString()
                    ?? throw new JsonException("GitHub repository name is null.");

                var webUrl = urlProperty.GetString()
                    ?? throw new JsonException("GitHub repository URL is null.");

                return (name, webUrl);
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve repository metadata for repository '{repositoryId}'.",
                    ex);
            }
        }

        public async Task<List<string>> GetBranchesAsync(string repositoryId)
        {
            ValidateRequired(repositoryId, nameof(repositoryId), "Repository cannot be null or empty.");

            var (owner, repo) = ParseRepositoryId(repositoryId);

            try
            {
                var branches = new List<string>();
                var page = 1;

                while (true)
                {
                    var url =
                        $"{ApiBaseUrl}/repos/{Encode(owner)}/{Encode(repo)}/branches?per_page=100&page={page}";

                    using var response = await _client.GetAsync(url);
                    await EnsureSuccessAsync(response, "GitHub branches request failed.", repositoryId);

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        throw new JsonException("GitHub branches response is not a JSON array.");

                    var pageCount = 0;

                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (!element.TryGetProperty("name", out var nameProperty))
                            continue;

                        var name = nameProperty.GetString();

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        branches.Add(name);
                        pageCount++;
                    }

                    if (pageCount == 0)
                        break;

                    page++;
                }

                return branches;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve branches for repository '{repositoryId}'.",
                    ex);
            }
        }

        public string BuildBranchUrl(string repositoryWebUrl, string branch)
            => $"{repositoryWebUrl.TrimEnd('/')}/tree/{Uri.EscapeDataString(branch)}";

        private static (string Owner, string Repository) ParseRepositoryId(string repositoryId)
        {
            var value = repositoryId.Trim();
            var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                throw new ArgumentException(
                    "Le depot GitHub doit etre saisi au format owner/repository.",
                    nameof(repositoryId));
            }

            return (parts[0], parts[1]);
        }

        private static string Encode(string value) => Uri.EscapeDataString(value);

        private static void ValidateFileRequest(string repositoryId, string filePath, string branch)
        {
            ValidateRequired(repositoryId, nameof(repositoryId), "Repository cannot be null or empty.");
            ValidateRequired(filePath, nameof(filePath), "File path cannot be null or empty.");
            ValidateRequired(branch, nameof(branch), "Branch cannot be null or empty.");
        }

        private static void ValidateRequired(string value, string parameterName, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(message, parameterName);
        }

        private static async Task EnsureSuccessAsync(
            HttpResponseMessage response,
            string message,
            string repositoryId,
            string? branch = null,
            string? filePath = null)
        {
            if (response.IsSuccessStatusCode)
                return;

            var details = new StringBuilder();
            details.Append(message);
            details.Append($" Status: {(int)response.StatusCode} {response.ReasonPhrase}.");
            details.Append($" Repository: '{repositoryId}'.");

            if (!string.IsNullOrWhiteSpace(branch))
                details.Append($" Branch: '{branch}'.");

            if (!string.IsNullOrWhiteSpace(filePath))
                details.Append($" File: '{filePath}'.");

            var body = await SafeReadBodyAsync(response);

            if (!string.IsNullOrWhiteSpace(body))
                details.Append($" Body: {body}");

            throw new HttpRequestException(details.ToString());
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
