using codemergeWinForms.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace codemergeWinForms.Services
{
    /// <summary>
    /// Encapsule les appels HTTP vers l'API GitLab.
    /// </summary>
    public class GitLabService : IRepositoryService
    {
        private const string ApiBaseUrl = "https://git.s2.rpn.ch/api/v4";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _client;

        /// <summary>
        /// Initialise le service GitLab avec un token Bearer.
        /// </summary>
        /// <param name="token">Token d'acces API GitLab.</param>
        public GitLabService(string token)
        {
            ValidateRequired(token, nameof(token), "Token cannot be null or empty.");

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        /// <summary>
        /// Recupere recursivement l'arborescence complete d'un depot pour une branche.
        /// </summary>
        public async Task<List<GitLabTreeItem>> GetRepositoryTreeAsync(string projectId, string branch)
        {
            ValidateRequired(projectId, nameof(projectId), "Project ID cannot be null or empty.");
            ValidateRequired(branch, nameof(branch), "Branch cannot be null or empty.");

            try
            {
                var allItems = new List<GitLabTreeItem>();
                var page = 1;
                var encodedProjectId = Encode(projectId);
                var encodedBranch = Encode(branch);

                while (true)
                {
                    var url =
                        $"{ApiBaseUrl}/projects/{encodedProjectId}/repository/tree" +
                        $"?ref={encodedBranch}&recursive=true&per_page=100&page={page}";

                    using var response = await _client.GetAsync(url);
                    await EnsureSuccessAsync(response, "GitLab tree request failed.", projectId, branch);

                    var json = await response.Content.ReadAsStringAsync();
                    var items = JsonSerializer.Deserialize<List<GitLabTreeItem>>(json, JsonOptions)
                        ?? throw new JsonException("GitLab returned null when deserializing repository tree.");

                    if (items.Count == 0)
                        break;

                    allItems.AddRange(items);
                    page++;
                }

                return allItems;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve repository tree for project '{projectId}' on branch '{branch}'.",
                    ex);
            }
        }

        /// <summary>
        /// Recupere le contenu texte brut d'un fichier du depot.
        /// </summary>
        public async Task<string> GetFileContentAsync(string projectId, string filePath, string branch)
        {
            ValidateFileRequest(projectId, filePath, branch);

            try
            {
                var url = BuildRawFileUrl(projectId, filePath, branch);

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitLab file content request failed.", projectId, branch, filePath);

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve file content for '{filePath}' in project '{projectId}' on branch '{branch}'.",
                    ex);
            }
        }

        /// <summary>
        /// Recupere le contenu binaire brut d'un fichier du depot.
        /// </summary>
        public async Task<byte[]> GetFileBytesAsync(string projectId, string filePath, string branch)
        {
            ValidateFileRequest(projectId, filePath, branch);

            try
            {
                var url = BuildRawFileUrl(projectId, filePath, branch);

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitLab file download failed.", projectId, branch, filePath);

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve file bytes for '{filePath}' in project '{projectId}' on branch '{branch}'.",
                    ex);
            }
        }

        /// <summary>
        /// Recupere la taille d'un fichier sans telecharger son contenu complet.
        /// </summary>
        public async Task<long?> GetFileSizeAsync(string projectId, string filePath, string branch)
        {
            ValidateFileRequest(projectId, filePath, branch);

            try
            {
                var metadataUrl = BuildFileMetadataUrl(projectId, filePath, branch);

                using var metadataRequest = new HttpRequestMessage(HttpMethod.Head, metadataUrl);
                using var metadataResponse = await _client.SendAsync(metadataRequest, HttpCompletionOption.ResponseHeadersRead);

                if (metadataResponse.IsSuccessStatusCode
                    && TryGetLongHeader(metadataResponse.Headers, "X-Gitlab-Size", out var gitlabSize))
                {
                    return gitlabSize;
                }

                using var metadataContentResponse = await _client.GetAsync(metadataUrl);

                if (!metadataContentResponse.IsSuccessStatusCode)
                    return null;

                var metadataJson = await metadataContentResponse.Content.ReadAsStringAsync();
                using var metadataDocument = JsonDocument.Parse(metadataJson);

                if (metadataDocument.RootElement.TryGetProperty("size", out var sizeProperty)
                    && sizeProperty.ValueKind == JsonValueKind.Number
                    && sizeProperty.TryGetInt64(out var jsonSize))
                    return jsonSize;

                return null;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve file size for '{filePath}' in project '{projectId}' on branch '{branch}'.",
                    ex);
            }
        }

        /// <summary>
        /// Recupere les metadonnees principales du projet GitLab.
        /// </summary>
        public async Task<(string Name, string WebUrl)> GetProjectMetadataAsync(string projectId)
        {
            ValidateRequired(projectId, nameof(projectId), "Project ID cannot be null or empty.");

            try
            {
                var url = $"{ApiBaseUrl}/projects/{Encode(projectId)}";

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitLab project request failed.", projectId);

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("name", out var nameProperty))
                    throw new JsonException("GitLab response does not contain property 'name'.");

                if (!doc.RootElement.TryGetProperty("web_url", out var webUrlProperty))
                    throw new JsonException("GitLab response does not contain property 'web_url'.");

                var projectName = nameProperty.GetString()
                    ?? throw new JsonException("GitLab project name is null.");

                var webUrl = webUrlProperty.GetString()
                    ?? throw new JsonException("GitLab project web URL is null.");

                return (projectName, webUrl);
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve project metadata for project '{projectId}'.",
                    ex);
            }
        }

        /// <summary>
        /// Recupere la liste des branches disponibles pour un projet.
        /// </summary>
        public async Task<List<string>> GetBranchesAsync(string projectId)
        {
            ValidateRequired(projectId, nameof(projectId), "Project ID cannot be null or empty.");

            try
            {
                var url = $"{ApiBaseUrl}/projects/{Encode(projectId)}/repository/branches?per_page=100";

                using var response = await _client.GetAsync(url);
                await EnsureSuccessAsync(response, "GitLab branches request failed.", projectId);

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    throw new JsonException("GitLab branches response is not a JSON array.");

                var branches = new List<string>();

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("name", out var nameProperty))
                        continue;

                    var name = nameProperty.GetString();

                    if (!string.IsNullOrWhiteSpace(name))
                        branches.Add(name);
                }

                return branches;
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Failed to retrieve branches for project '{projectId}'.",
                    ex);
            }
        }

        public string BuildBranchUrl(string repositoryWebUrl, string branch)
            => $"{repositoryWebUrl.TrimEnd('/')}/-/tree/{Uri.EscapeDataString(branch)}";

        private static string BuildRawFileUrl(string projectId, string filePath, string branch)
            => $"{ApiBaseUrl}/projects/{Encode(projectId)}/repository/files/{Encode(filePath)}/raw?ref={Encode(branch)}";

        private static string BuildFileMetadataUrl(string projectId, string filePath, string branch)
            => $"{ApiBaseUrl}/projects/{Encode(projectId)}/repository/files/{Encode(filePath)}?ref={Encode(branch)}";

        private static string Encode(string value) => Uri.EscapeDataString(value);

        private static void ValidateFileRequest(string projectId, string filePath, string branch)
        {
            ValidateRequired(projectId, nameof(projectId), "Project ID cannot be null or empty.");
            ValidateRequired(filePath, nameof(filePath), "File path cannot be null or empty.");
            ValidateRequired(branch, nameof(branch), "Branch cannot be null or empty.");
        }

        private static void ValidateRequired(string value, string parameterName, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(message, parameterName);
        }

        private static bool TryGetLongHeader(HttpResponseHeaders headers, string name, out long value)
        {
            value = 0;

            return headers.TryGetValues(name, out var values)
                && long.TryParse(values.FirstOrDefault(), out value);
        }

        private static async Task EnsureSuccessAsync(
            HttpResponseMessage response,
            string message,
            string projectId,
            string? branch = null,
            string? filePath = null)
        {
            if (response.IsSuccessStatusCode)
                return;

            var details = new StringBuilder();
            details.Append(message);
            details.Append($" Status: {(int)response.StatusCode} {response.ReasonPhrase}.");
            details.Append($" Project: '{projectId}'.");

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
