using codemergeWinForms.Models;
using codemergeWinForms.Services;
using codemergeWinForms.Services.FunctionExtraction;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace codemergeWinForms
{
    /// <summary>
    /// Fenetre principale de l'application WinForms pour charger l'arborescence GitLab
    /// et exporter une selection de fichiers en Markdown.
    /// </summary>
    public partial class Form1 : Form
    {
        private const long LargeUnknownFileThresholdBytes = 1_048_576;
        private const int FileSizeFetchConcurrency = 8;
        private const int UnknownFileAnalysisConcurrency = 4;
        private const string ApplicationDataFolderName = "CodeMerge";
        private const string LastTokenFileName = "lastToken.txt";
        private const string LastProjectIdFileName = "lastId.txt";
        private const string LastBranchFileName = "lastBranch.txt";
        private const string LastOutputDirectoryFileName = "lastOutputDirectory.txt";
        private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

        private bool _isPropagatingChecks;
        private ContextMenuStrip? _exportResultMenu;
        private ExportResultItem? _selectedExportResult;

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
            uint dwFlags,
            nint hToken,
            out nint ppszPath);

        private readonly List<string> typeGit = new();

        private sealed class ExportResultItem
        {
            public required string DisplayText { get; init; }

            public required string FilePath { get; init; }
        }

        /// <summary>
        /// Transporte les metadonnees associees a un noeud de fichier.
        /// </summary>
        private class TreeNodeInfo
        {
            /// <summary>
            /// Chemin complet du fichier represente.
            /// </summary>
            public string Path { get; set; } = string.Empty;

            /// <summary>
            /// Indique si le fichier peut etre coche par l'utilisateur.
            /// </summary>
            public bool Selectable { get; set; }

            /// <summary>
            /// Taille distante du fichier si elle a pu etre determinee.
            /// </summary>
            public long? Size { get; set; }

            /// <summary>
            /// Indique qu'une analyse du contenu est encore en cours.
            /// </summary>
            public bool IsPendingAnalysis { get; set; }

            /// <summary>
            /// Indique qu'une analyse de contenu a echoue.
            /// </summary>
            public bool HasAnalysisError { get; set; }
        }

        private sealed class PendingFileNode
        {
            public required TreeNode Node { get; init; }

            public required string Path { get; init; }

            public required string BaseText { get; init; }

            public required bool RequiresAnalysis { get; init; }

            public bool Selectable { get; set; }

            public long? Size { get; set; }

            public bool IsPendingAnalysis { get; set; }

            public bool HasAnalysisError { get; set; }
        }

        /// <summary>
        /// Initialise la fenetre et relie les evenements de l'arbre.
        /// </summary>
        public Form1()
        {
            InitializeComponent();
            tbxOutputDirectory.Text = GetDefaultOutputDirectory();
            LoadLastSessionValuesIfAvailable();
            trvTree.BeforeCheck += trvTree_BeforeCheck;
            trvTree.AfterCheck += trvTree_AfterCheck;
            trvTree.NodeMouseDoubleClick += trvTree_NodeMouseDoubleClick;
            trvTree.ShowNodeToolTips = true;
            FormClosed += Form1_FormClosed;
            cbxTypeGit.SelectedIndexChanged += cbxTypeGit_SelectedIndexChanged;
            ConfigureExportResultsList();
            SetBusyState(false);

            typeGit.Add("GitLab");
            typeGit.Add("GitHub");

            cbxTypeGit.DataSource = typeGit;
            cbxTypeGit.SelectedIndex = 0;
            UpdateRepositoryFieldLabels();
        }

        /// <summary>
        /// Charge l'arborescence distante du depot GitLab pour la branche selectionnee.
        /// </summary>
        private async void btnTree_Click(object sender, EventArgs e)
        {
            var token = tbxToken.Text.Trim();

            if (RequiresToken() && string.IsNullOrWhiteSpace(token))
            {
                ShowError("Le token est requis.", "Champ manquant");
                return;
            }

            var projectId = tbxId.Text.Trim();

            if (string.IsNullOrWhiteSpace(projectId))
            {
                ShowError(GetRepositoryIdentifierRequiredMessage(), "Champ manquant");
                return;
            }

            var branch = cbxBranch.Text.Trim();

            if (string.IsNullOrWhiteSpace(branch))
            {
                ShowError("La branche est requise.", "Champ manquant");
                return;
            }

            SetBusyState(true);

            try
            {
                var repositoryService = CreateRepositoryService(token);
                var items = await repositoryService.GetRepositoryTreeAsync(projectId, branch);
                await BuildTreeViewAsync(trvTree, items, repositoryService, projectId, branch);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message, "Parametre invalide");
            }
            catch (HttpRequestException ex)
            {
                ShowError(
                    $"Impossible de charger l'arborescence {GetSelectedProviderName()}.\n\nVerifie le token, le depot/projet et la branche.\n\nDetail : {ex.Message}",
                    $"Erreur {GetSelectedProviderName()}");
                LogException(ex);
            }
            catch (InvalidOperationException ex)
            {
                ShowError(
                    $"Impossible de charger l'arborescence du projet.\n\nDetail : {ex.Message}",
                    "Erreur");
                LogException(ex);
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Une erreur inattendue est survenue lors du chargement de l'arborescence.\n\nDetail : {ex.Message}",
                    "Erreur inattendue");
                LogException(ex);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        /// <summary>
        /// Charge les branches disponibles du projet GitLab et alimente la liste deroulante.
        /// </summary>
        private async void btnBranch_Click(object sender, EventArgs e)
        {
            var token = tbxToken.Text.Trim();

            if (RequiresToken() && string.IsNullOrWhiteSpace(token))
            {
                ShowError("Le token est requis.", "Champ manquant");
                return;
            }

            var projectId = tbxId.Text.Trim();

            if (string.IsNullOrWhiteSpace(projectId))
            {
                ShowError(GetRepositoryIdentifierRequiredMessage(), "Champ manquant");
                return;
            }

            SetBusyState(true);

            try
            {
                var repositoryService = CreateRepositoryService(token);
                var branches = await repositoryService.GetBranchesAsync(projectId);

                cbxBranch.Items.Clear();

                foreach (var branch in branches)
                    cbxBranch.Items.Add(branch);

                if (cbxBranch.Items.Count > 0)
                {
                    cbxBranch.SelectedIndex = 0;
                }
                else
                {
                    ShowInfo("Aucune branche n'a ete trouvee pour ce projet.", "Branches");
                }
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message, "Parametre invalide");
            }
            catch (HttpRequestException ex)
            {
                ShowError(
                    $"Impossible de recuperer les branches {GetSelectedProviderName()}.\n\nVerifie le token et le depot/projet.\n\nDetail : {ex.Message}",
                    $"Erreur {GetSelectedProviderName()}");
                LogException(ex);
            }
            catch (InvalidOperationException ex)
            {
                ShowError(
                    $"Impossible de recuperer les branches.\n\nDetail : {ex.Message}",
                    "Erreur");
                LogException(ex);
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Une erreur inattendue est survenue lors du chargement des branches.\n\nDetail : {ex.Message}",
                    "Erreur inattendue");
                LogException(ex);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        /// <summary>
        /// Exporte les fichiers coches en un document Markdown et l'enregistre localement.
        /// </summary>
        private async void btnMarkdown_Click(object sender, EventArgs e)
        {
            var token = tbxToken.Text.Trim();

            if (RequiresToken() && string.IsNullOrWhiteSpace(token))
            {
                ShowError("Le token est requis.", "Champ manquant");
                return;
            }

            var projectId = tbxId.Text.Trim();

            if (string.IsNullOrWhiteSpace(projectId))
            {
                ShowError(GetRepositoryIdentifierRequiredMessage(), "Champ manquant");
                return;
            }

            var branch = cbxBranch.Text.Trim();

            if (string.IsNullOrWhiteSpace(branch))
            {
                ShowError("La branche est requise.", "Champ manquant");
                return;
            }

            var selectedFiles = GetCheckedNodes(trvTree.Nodes)
                .Where(node => node.Nodes.Count == 0)
                .Where(node => node.Tag is TreeNodeInfo info && info.Selectable)
                .Select(node => ((TreeNodeInfo)node.Tag!).Path)
                .ToList();

            if (selectedFiles.Count == 0)
            {
                ShowError("Aucun fichier selectionne.", "Export impossible");
                return;
            }

            var outputDirectory = GetSelectedOutputDirectory();

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                ShowError("Le dossier de sortie est requis.", "Champ manquant");
                return;
            }

            SetBusyState(true);

            try
            {
                var repositoryService = CreateRepositoryService(token);
                var functionExtract = new FunctionExtractService();
                var exporter = new MarkdownExportService(repositoryService, functionExtract);
                Directory.CreateDirectory(outputDirectory);

                var (packageDirectoryPath, _) = await exporter.ExportProjectAsync(
                    projectId,
                    branch,
                    selectedFiles,
                    outputDirectory,
                    DateTime.Now);

                string? zipPath = null;

                if (chkCreateZip.Checked)
                    zipPath = CreateZipArchive(packageDirectoryPath, outputDirectory);

                if (!string.IsNullOrWhiteSpace(zipPath))
                    AddExportResult(zipPath);

                AddExportResult(packageDirectoryPath);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message, "Parametre invalide");
            }
            catch (HttpRequestException ex)
            {
                ShowError(
                    $"Erreur lors de la recuperation des donnees {GetSelectedProviderName()}.\n\nDetail : {ex.Message}",
                    $"Erreur {GetSelectedProviderName()}");
                LogException(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowError(
                    $"Acces refuse lors de l'ecriture du fichier.\n\nVerifie les permissions du dossier.\n\nDetail : {ex.Message}",
                    "Erreur fichier");
                LogException(ex);
            }
            catch (IOException ex)
            {
                ShowError(
                    $"Erreur lors de l'ecriture du fichier Markdown.\n\nDetail : {ex.Message}",
                    "Erreur fichier");
                LogException(ex);
            }
            catch (InvalidOperationException ex)
            {
                ShowError(
                    $"Impossible de generer l'export Markdown.\n\nDetail : {ex.Message}",
                    "Erreur export");
                LogException(ex);
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Une erreur inattendue est survenue pendant l'export.\n\nDetail : {ex.Message}",
                    "Erreur inattendue");
                LogException(ex);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        /// <summary>
        /// Construit visuellement l'arborescence WinForms a partir des chemins GitLab.
        /// </summary>
        private async Task BuildTreeViewAsync(
            TreeView treeView,
            List<GitLabTreeItem> items,
            IRepositoryService repositoryService,
            string projectId,
            string branch)
        {
            var pendingFiles = new List<PendingFileNode>();

            treeView.BeginUpdate();

            try
            {
                treeView.Nodes.Clear();

                var rootNode = new TreeNode("tout s\u00e9lectionner");
                treeView.Nodes.Add(rootNode);

                foreach (var item in items.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(item.Path))
                        continue;

                    var parts = item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0)
                        continue;

                    if (string.Equals(item.Type, "tree", StringComparison.OrdinalIgnoreCase))
                    {
                        EnsureDirectoryNode(rootNode, parts);
                        continue;
                    }

                    if (!string.Equals(item.Type, "blob", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parentNode = EnsureDirectoryNode(rootNode, parts.Take(parts.Length - 1));
                    var fileNode = GetOrCreateChildNode(parentNode.Nodes, parts[^1]);
                    var isUnknownType = FileTypeHelper.IsUnknownFileType(item.Path);
                    var pendingFile = new PendingFileNode
                    {
                        Node = fileNode,
                        Path = item.Path,
                        BaseText = parts[^1],
                        RequiresAnalysis = isUnknownType,
                        Selectable = !isUnknownType && FileTypeHelper.IsSelectableInTree(item.Path),
                        IsPendingAnalysis = isUnknownType,
                        HasAnalysisError = false
                    };

                    ApplyPendingFileNodeState(treeView, pendingFile);
                    pendingFiles.Add(pendingFile);
                }

                treeView.ExpandAll();
            }
            finally
            {
                treeView.EndUpdate();
            }

            var progress = new Progress<PendingFileNode>(file => ApplyPendingFileNodeState(treeView, file));
            var sizeTask = PopulateKnownFileSizesAsync(pendingFiles, repositoryService, projectId, branch, progress);
            var analysisTask = ClassifyUnknownFilesAsync(pendingFiles, repositoryService, projectId, branch, progress);

            await Task.Yield();
            await Task.WhenAll(sizeTask, analysisTask);
        }

        /// <summary>
        /// Propage l'etat coche/decoché d'un noeud vers tous ses enfants.
        /// </summary>
        private void trvTree_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            var node = e.Node;

            if (node is null || _isPropagatingChecks)
                return;

            if (node.Tag is TreeNodeInfo info && !info.Selectable)
            {
                SetNodeCheckedState(node, false);
                return;
            }

            if (e.Action == TreeViewAction.Unknown)
                return;

            _isPropagatingChecks = true;

            try
            {
                PropagateCheckedState(node, node.Checked);
            }
            finally
            {
                _isPropagatingChecks = false;
            }
        }

        /// <summary>
        /// Empeche la selection des noeuds non exportables et demande confirmation pour les gros fichiers inconnus.
        /// </summary>
        private void trvTree_BeforeCheck(object? sender, TreeViewCancelEventArgs e)
        {
            var node = e.Node;

            if (node is null || node.Checked)
                return;

            if (node.Tag is not TreeNodeInfo info)
                return;

            if (!info.Selectable)
            {
                e.Cancel = true;
                return;
            }

            if (!RequiresLargeFileConfirmation(info))
                return;

            if (e.Action == TreeViewAction.Unknown)
            {
                e.Cancel = true;
                return;
            }

            var message =
                $"Le fichier \"{info.Path}\" est volumineux et depasse {FormatFileSize(LargeUnknownFileThresholdBytes)}.\n" +
                $"Taille detectee : {FormatFileSize(info.Size)}.\n\n" +
                "Voulez-vous vraiment le selectionner ?";

            if (MessageBox.Show(
                    message,
                    "Confirmation requise",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private void trvTree_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is not TreeNodeInfo info)
                return;

            if (!info.Selectable)
                SetNodeCheckedState(e.Node, false);
        }

        /// <summary>
        /// Applique recursivement un etat coche/decoché a tous les descendants d'un noeud.
        /// </summary>
        private void PropagateCheckedState(TreeNode node, bool isChecked)
        {
            foreach (TreeNode child in node.Nodes)
            {
                if (child.Tag is TreeNodeInfo info)
                {
                    if (!info.Selectable)
                    {
                        SetNodeCheckedState(child, false);

                        continue;
                    }

                    if (isChecked && RequiresLargeFileConfirmation(info))
                    {
                        SetNodeCheckedState(child, false);

                        continue;
                    }
                }

                child.Checked = isChecked;
                PropagateCheckedState(child, isChecked);
            }
        }

        /// <summary>
        /// Recupere tous les noeuds coches d'une collection TreeView, recursivement.
        /// </summary>
        private List<TreeNode> GetCheckedNodes(TreeNodeCollection nodes)
        {
            var result = new List<TreeNode>();

            foreach (TreeNode node in nodes)
            {
                if (node.Checked)
                    result.Add(node);

                if (node.Nodes.Count > 0)
                    result.AddRange(GetCheckedNodes(node.Nodes));
            }

            return result;
        }

        private static TreeNode EnsureDirectoryNode(TreeNode rootNode, IEnumerable<string> parts)
        {
            var currentNode = rootNode;

            foreach (var part in parts)
                currentNode = GetOrCreateChildNode(currentNode.Nodes, part);

            return currentNode;
        }

        private static TreeNode GetOrCreateChildNode(TreeNodeCollection nodes, string text)
        {
            foreach (TreeNode existingNode in nodes)
            {
                if (string.Equals(existingNode.Text, text, StringComparison.Ordinal))
                    return existingNode;
            }

            var createdNode = new TreeNode(text);
            nodes.Add(createdNode);
            return createdNode;
        }

        private void ApplyPendingFileNodeState(TreeView treeView, PendingFileNode file)
        {
            file.Node.Tag = new TreeNodeInfo
            {
                Path = file.Path,
                Selectable = file.Selectable,
                Size = file.Size,
                IsPendingAnalysis = file.IsPendingAnalysis,
                HasAnalysisError = file.HasAnalysisError
            };

            if (file.IsPendingAnalysis)
            {
                file.Node.Text = $"{file.BaseText} (analyse...)";
                file.Node.ForeColor = Color.DarkGoldenrod;
                file.Node.ToolTipText = "Analyse du type de fichier en cours.";
                SetNodeCheckedState(file.Node, false);
                return;
            }

            file.Node.Text = file.HasAnalysisError
                ? $"{file.BaseText} (analyse impossible)"
                : file.BaseText;

            file.Node.ForeColor = file.Selectable
                ? treeView.ForeColor
                : file.HasAnalysisError
                    ? Color.IndianRed
                    : Color.Gray;

            file.Node.ToolTipText = file.Selectable
                ? string.Empty
                : file.HasAnalysisError
                    ? "Analyse du fichier impossible, selection desactivee."
                    : "Fichier binaire non exportable.";

            if (!file.Selectable)
                SetNodeCheckedState(file.Node, false);
        }

        private static async Task PopulateKnownFileSizesAsync(
            List<PendingFileNode> files,
            IRepositoryService repositoryService,
            string projectId,
            string branch,
            IProgress<PendingFileNode> progress)
        {
            using var semaphore = new SemaphoreSlim(FileSizeFetchConcurrency);

            var tasks = files.Select(async file =>
            {
                if (!file.Selectable || file.RequiresAnalysis)
                    return;

                await semaphore.WaitAsync();

                try
                {
                    file.Size = await repositoryService.GetFileSizeAsync(projectId, file.Path, branch);
                    progress.Report(file);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private static async Task ClassifyUnknownFilesAsync(
            List<PendingFileNode> files,
            IRepositoryService repositoryService,
            string projectId,
            string branch,
            IProgress<PendingFileNode> progress)
        {
            using var semaphore = new SemaphoreSlim(UnknownFileAnalysisConcurrency);

            var tasks = files.Select(async file =>
            {
                if (!file.RequiresAnalysis)
                    return;

                await semaphore.WaitAsync();

                try
                {
                    var bytes = await repositoryService.GetFileBytesAsync(projectId, file.Path, branch);
                    file.Size = bytes.LongLength;
                    file.Selectable = !FileTypeHelper.LooksBinaryContent(bytes);
                    file.IsPendingAnalysis = false;
                    progress.Report(file);
                }
                catch (Exception ex)
                {
                    file.Selectable = false;
                    file.IsPendingAnalysis = false;
                    file.HasAnalysisError = true;
                    progress.Report(file);
                    LogException(ex);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private void SetNodeCheckedState(TreeNode node, bool isChecked)
        {
            if (node.Checked == isChecked)
                return;

            var previousPropagationState = _isPropagatingChecks;
            _isPropagatingChecks = true;

            try
            {
                node.Checked = isChecked;
            }
            finally
            {
                _isPropagatingChecks = previousPropagationState;
            }
        }

        private IRepositoryService CreateRepositoryService(string token)
            => string.Equals(GetSelectedProviderName(), "GitHub", StringComparison.OrdinalIgnoreCase)
                ? new GitHubService(token)
                : new GitLabService(token);

        private string GetSelectedProviderName()
            => cbxTypeGit.SelectedItem as string ?? "GitLab";

        private bool RequiresToken()
            => !string.Equals(GetSelectedProviderName(), "GitHub", StringComparison.OrdinalIgnoreCase);

        private string GetRepositoryIdentifierRequiredMessage()
            => string.Equals(GetSelectedProviderName(), "GitHub", StringComparison.OrdinalIgnoreCase)
                ? "Le depot GitHub au format owner/repository est requis."
                : "L'identifiant du projet est requis.";

        private void cbxTypeGit_SelectedIndexChanged(object? sender, EventArgs e)
            => UpdateRepositoryFieldLabels();

        private void UpdateRepositoryFieldLabels()
        {
            if (string.Equals(GetSelectedProviderName(), "GitHub", StringComparison.OrdinalIgnoreCase))
            {
                lblId.Text = "Depot owner/repo :";
                lblToken.Text = "Token (optionnel, petits projets) :";
                return;
            }

            lblId.Text = "Id du projet :";
            lblToken.Text = "Token :";
        }

        private static bool RequiresLargeFileConfirmation(TreeNodeInfo info)
            => info.Size.HasValue
            && info.Size.Value >= LargeUnknownFileThresholdBytes
            && info.Selectable;

        private static string FormatFileSize(long? sizeInBytes)
        {
            if (!sizeInBytes.HasValue)
                return "taille inconnue";

            return $"{sizeInBytes.Value / 1024d / 1024d:F1} Mo";
        }

        private void Form1_FormClosed(object? sender, FormClosedEventArgs e)
        {
            try
            {
                SaveProtectedSessionValue(LastTokenFileName, tbxToken.Text);
                SaveSessionValue(LastProjectIdFileName, tbxId.Text);
                SaveSessionValue(LastBranchFileName, cbxBranch.Text);
                SaveSessionValue(LastOutputDirectoryFileName, tbxOutputDirectory.Text);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private void LoadLastSessionValuesIfAvailable()
        {
            try
            {
                tbxOutputDirectory.Text = GetDefaultOutputDirectory();
                LoadProtectedSessionValueIfAvailable(LastTokenFileName, value => tbxToken.Text = value);
                LoadSessionValueIfAvailable(LastProjectIdFileName, value => tbxId.Text = value);
                LoadSessionValueIfAvailable(LastBranchFileName, value => cbxBranch.Text = value);
                LoadSessionValueIfAvailable(LastOutputDirectoryFileName, value => tbxOutputDirectory.Text = value);

                if (string.IsNullOrWhiteSpace(tbxOutputDirectory.Text))
                    tbxOutputDirectory.Text = GetDefaultOutputDirectory();
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private static void SaveSessionValue(string fileName, string value)
        {
            var filePath = GetSessionFilePath(fileName);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                return;
            }

            Directory.CreateDirectory(GetSessionDirectoryPath());
            File.WriteAllText(filePath, value);
        }

        private static void SaveProtectedSessionValue(string fileName, string value)
        {
            var filePath = GetSessionFilePath(fileName);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                return;
            }

            Directory.CreateDirectory(GetSessionDirectoryPath());
            var clearBytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllText(filePath, Convert.ToBase64String(protectedBytes));
        }

        private static void LoadSessionValueIfAvailable(string fileName, Action<string> applyValue)
        {
            var filePath = GetSessionFilePath(fileName);

            if (!File.Exists(filePath))
                return;

            var savedValue = File.ReadAllText(filePath).Trim();

            if (!string.IsNullOrWhiteSpace(savedValue))
                applyValue(savedValue);
        }

        private static void LoadProtectedSessionValueIfAvailable(string fileName, Action<string> applyValue)
        {
            var filePath = GetSessionFilePath(fileName);

            if (!File.Exists(filePath))
                return;

            var savedValue = File.ReadAllText(filePath).Trim();

            if (string.IsNullOrWhiteSpace(savedValue))
                return;

            var protectedBytes = Convert.FromBase64String(savedValue);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var clearText = Encoding.UTF8.GetString(clearBytes).Trim();

            if (!string.IsNullOrWhiteSpace(clearText))
                applyValue(clearText);
        }

        private void ConfigureExportResultsList()
        {
            _exportResultMenu = new ContextMenuStrip();
            _exportResultMenu.Items.Add("Ouvrir", null, (_, _) => TryOpenSelectedExport());
            _exportResultMenu.Items.Add("Ouvrir l'emplacement", null, (_, _) => TryOpenSelectedExportLocation());

            listBox1.DrawMode = DrawMode.OwnerDrawFixed;
            listBox1.DrawItem += listBox1_DrawItem;
            listBox1.MouseMove += listBox1_MouseMove;
            listBox1.MouseLeave += listBox1_MouseLeave;
            listBox1.MouseDown += listBox1_MouseDown;
            listBox1.KeyDown += listBox1_KeyDown;
        }

        private void AddExportResult(string fullPath)
        {
            listBox1.Items.Insert(0, new ExportResultItem
            {
                DisplayText = Path.GetFileName(fullPath),
                FilePath = fullPath
            });

            listBox1.SelectedIndex = 0;
        }

        private void listBox1_DrawItem(object? sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            if (e.Index < 0 || e.Index >= listBox1.Items.Count)
                return;

            if (listBox1.Items[e.Index] is not ExportResultItem item)
                return;

            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var fileExists = File.Exists(item.FilePath) || Directory.Exists(item.FilePath);
            var textColor = isSelected
                ? SystemColors.HighlightText
                : fileExists
                    ? Color.RoyalBlue
                    : SystemColors.GrayText;

            using var textFont = new Font(
                e.Font ?? listBox1.Font,
                fileExists ? FontStyle.Underline : FontStyle.Regular);

            TextRenderer.DrawText(
                e.Graphics,
                item.DisplayText,
                textFont,
                e.Bounds,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            e.DrawFocusRectangle();
        }

        private void listBox1_MouseMove(object? sender, MouseEventArgs e)
        {
            var item = GetExportResultAt(e.Location);
            listBox1.Cursor = item is not null && (File.Exists(item.FilePath) || Directory.Exists(item.FilePath))
                ? Cursors.Hand
                : Cursors.Default;
        }

        private void listBox1_MouseLeave(object? sender, EventArgs e)
            => listBox1.Cursor = Cursors.Default;

        private void listBox1_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var index = listBox1.IndexFromPoint(e.Location);

            if (index < 0 || index >= listBox1.Items.Count)
                return;

            listBox1.SelectedIndex = index;

            ShowExportResultMenu(
                listBox1.Items[index] as ExportResultItem,
                e.Location);
        }

        private void listBox1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            if (listBox1.SelectedIndex < 0)
                return;

            var itemBounds = listBox1.GetItemRectangle(listBox1.SelectedIndex);

            ShowExportResultMenu(
                listBox1.SelectedItem as ExportResultItem,
                new Point(itemBounds.Left, itemBounds.Bottom));

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private ExportResultItem? GetExportResultAt(Point location)
        {
            var index = listBox1.IndexFromPoint(location);

            if (index < 0 || index >= listBox1.Items.Count)
                return null;

            var itemBounds = listBox1.GetItemRectangle(index);

            if (!itemBounds.Contains(location))
                return null;

            return listBox1.Items[index] as ExportResultItem;
        }

        private void ShowExportResultMenu(ExportResultItem? item, Point location)
        {
            if (item is null || _exportResultMenu is null)
                return;

            _selectedExportResult = item;
            _exportResultMenu.Show(listBox1, location);
        }

        private void TryOpenSelectedExport()
        {
            var item = _selectedExportResult;

            if (item is null)
                return;

            if (!EnsureExportPathExists(item))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Impossible d'ouvrir le fichier exporte.\n\nDetail : {ex.Message}",
                    "Ouverture impossible");
                LogException(ex);
            }
        }

        private void TryOpenSelectedExportLocation()
        {
            var item = _selectedExportResult;

            if (item is null)
                return;

            var fileExists = File.Exists(item.FilePath);
            var directoryExists = Directory.Exists(item.FilePath);
            var directory = Path.GetDirectoryName(item.FilePath);

            try
            {
                if (directoryExists)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.FilePath,
                        UseShellExecute = true
                    });
                    return;
                }

                if (fileExists)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.FilePath}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{directory}\"",
                        UseShellExecute = true
                    });
                    return;
                }
                ShowError(
                    $"Le fichier n'existe plus et son dossier est introuvable :\n{item.FilePath}",
                    "Emplacement introuvable");
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Impossible d'ouvrir l'emplacement du fichier.\n\nDetail : {ex.Message}",
                    "Ouverture impossible");
                LogException(ex);
            }
        }

        private bool EnsureExportPathExists(ExportResultItem item)
        {
            if (File.Exists(item.FilePath) || Directory.Exists(item.FilePath))
                return true;

            ShowError(
                $"L'element n'existe plus a cet emplacement :\n{item.FilePath}",
                "Element introuvable");

            return false;
        }

        private void SetBusyState(bool isBusy)
        {
            cbxTypeGit.Enabled = !isBusy;
            btnBranch.Enabled = !isBusy;
            btnTree.Enabled = !isBusy;
            btnChooseOutputDirectory.Enabled = !isBusy;
            chkCreateZip.Enabled = !isBusy;

            if (isBusy)
            {
                btnMarkdown.Enabled = false;
                return;
            }
            btnMarkdown.Enabled = HasAnySelectableFileNodes(trvTree.Nodes);
        }

        private void btnChooseOutputDirectory_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choisir le dossier de sortie",
                SelectedPath = GetSelectedOutputDirectory(),
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                tbxOutputDirectory.Text = dialog.SelectedPath;
        }

        private static string GetSessionDirectoryPath()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationDataFolderName);

        private static string GetSessionFilePath(string fileName)
            => Path.Combine(GetSessionDirectoryPath(), fileName);

        private string GetSelectedOutputDirectory()
            => string.IsNullOrWhiteSpace(tbxOutputDirectory.Text)
                ? GetDefaultOutputDirectory()
                : tbxOutputDirectory.Text.Trim();

        private static string GetDefaultOutputDirectory()
        {
            var downloadsPath = TryGetKnownFolderPath(DownloadsFolderId);

            if (!string.IsNullOrWhiteSpace(downloadsPath))
                return downloadsPath;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "Downloads");
        }

        private static string? TryGetKnownFolderPath(Guid knownFolderId)
        {
            try
            {
                if (SHGetKnownFolderPath(knownFolderId, 0, 0, out var pathPointer) != 0 || pathPointer == 0)
                    return null;

                try
                {
                    return Marshal.PtrToStringUni(pathPointer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string CreateZipArchive(string packageDirectoryPath, string outputDirectory)
        {
            var zipFileName = $"{Path.GetFileName(packageDirectoryPath)}.zip";
            var zipFilePath = GetUniqueFilePath(outputDirectory, zipFileName);

            ZipFile.CreateFromDirectory(
                packageDirectoryPath,
                zipFilePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: true);

            return zipFilePath;
        }

        private static string GetUniqueFilePath(string directory, string fileName)
        {
            var baseFileName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var candidate = Path.Combine(directory, fileName);

            if (!File.Exists(candidate))
                return candidate;

            var suffix = 2;

            while (true)
            {
                candidate = Path.Combine(directory, $"{baseFileName} ({suffix}){extension}");

                if (!File.Exists(candidate))
                    return candidate;

                suffix++;
            }
        }

        private static bool HasAnySelectableFileNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is TreeNodeInfo info && info.Selectable)
                    return true;

                if (HasAnySelectableFileNodes(node.Nodes))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Affiche une boite de dialogue d'erreur standardisee.
        /// </summary>
        private static void ShowError(string message, string title = "Erreur")
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        /// <summary>
        /// Affiche une boite de dialogue d'information standardisee.
        /// </summary>
        private static void ShowInfo(string message, string title = "Information")
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// Ecrit les details d'une exception dans le fichier de log local.
        /// </summary>
        private static void LogException(Exception ex)
        {
            try
            {
                File.AppendAllText(
                    "error.log",
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "Révéler")
            {
                tbxToken.UseSystemPasswordChar = false;
                button1.Text = "Masquer";
            }
            else
            {
                tbxToken.UseSystemPasswordChar = true;
                button1.Text = "Révéler";
            }
        }
    }
}
