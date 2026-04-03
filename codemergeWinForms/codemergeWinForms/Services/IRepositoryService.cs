using codemergeWinForms.Models;

namespace codemergeWinForms.Services
{
    public interface IRepositoryService
    {
        Task<List<GitLabTreeItem>> GetRepositoryTreeAsync(string repositoryId, string branch);

        Task<string> GetFileContentAsync(string repositoryId, string filePath, string branch);

        Task<byte[]> GetFileBytesAsync(string repositoryId, string filePath, string branch);

        Task<long?> GetFileSizeAsync(string repositoryId, string filePath, string branch);

        Task<(string Name, string WebUrl)> GetProjectMetadataAsync(string repositoryId);

        Task<List<string>> GetBranchesAsync(string repositoryId);

        string BuildBranchUrl(string repositoryWebUrl, string branch);
    }
}
