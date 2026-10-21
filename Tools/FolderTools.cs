using System.ComponentModel;
using System.Net;
using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;

namespace ApsSamples.Tools;

// Folder tools for any agent. Folders are listed and created under "Project Files" unless a
// parent folder ID is given (new folders can't sit at the project root), and every call runs
// under the agent's service account.
public class FolderTools(DataManagementClient dataManagementClient, AgentToolContext context)
{
    private const string ProjectFilesFolderName = "Project Files";
    private const int PageLimit = 200;

    [Description("Lists the subfolders of a folder in the current project. Without a parent folder ID it lists the folders under \"Project Files\". Returns the parent folder's ID and name and each subfolder's ID and name. Use it to find the folder ID to pass to the other folder tools.")]
    public async Task<string> ListFoldersAsync(
        [Description("ID of the folder to list, as returned by ListFolders. Leave empty to list the folders under \"Project Files\".")] string? parentFolderId = null)
    {
        try
        {
            var projectId = context.RequireProjectId();
            var accessToken = await context.GetAccessTokenAsync();

            var parent = await ResolveParentAsync(projectId, parentFolderId, accessToken);
            var folders = await ListChildFoldersAsync(projectId, parent.Id, accessToken);

            return ToolResults.Json(new
            {
                ParentFolderId = parent.Id,
                ParentFolderName = parent.Name,
                Folders = folders
            });
        }
        catch (Exception ex)
        {
            return Failure("list the folders", ex);
        }
    }

    [Description("Creates one folder in the current project, under \"Project Files\" unless a parent folder ID is given. If a folder with that name already exists under the parent, it is returned instead of creating a duplicate. Returns the folder's ID and name and whether it was created. To create several folders or nested folders at once, use CreateFolderStructure instead.")]
    public async Task<string> CreateFolderAsync(
        [Description("Name of the new folder. It can't contain < > : \" / \\ | ? * ' ¢ ™ $ ® and is at most 255 characters.")] string folderName,
        [Description("ID of the parent folder, as returned by ListFolders. Leave empty to create the folder under \"Project Files\".")] string? parentFolderId = null)
    {
        var nameError = FolderPathValidator.ValidateName(folderName);
        if (nameError is not null)
        {
            return nameError;
        }

        var name = folderName.Trim();
        try
        {
            var projectId = context.RequireProjectId();
            var accessToken = await context.GetAccessTokenAsync();

            var parent = await ResolveParentAsync(projectId, parentFolderId, accessToken);
            var (folder, created) = await GetOrCreateChildAsync(projectId, parent.Id, name, new(), accessToken);

            return ToolResults.Json(new
            {
                FolderId = folder.Id,
                FolderName = folder.Name,
                Created = created,
                ParentFolderId = parent.Id,
                ParentFolderName = parent.Name
            });
        }
        catch (Exception ex)
        {
            return Failure($"create the folder '{name}'", ex);
        }
    }

    [Description("Creates a folder structure in the current project from a list of folder paths, under \"Project Files\" unless a parent folder ID is given. Each path uses \"/\" between levels, for example \"01 WIP/Architecture\". Existing folders are reused (matched by name, ignoring case), so running it again creates nothing new. Returns, per path, the deepest folder's ID, the segments that were created and the ones that already existed, or the error for that path.")]
    public async Task<string> CreateFolderStructureAsync(
        [Description("Folder paths to create, using \"/\" between levels, for example [\"01 WIP/Architecture\", \"01 WIP/Structure\"]. At most 200 paths and 25 levels.")] List<string> folderPaths,
        [Description("ID of the folder to create the structure in, as returned by ListFolders. Leave empty to create it under \"Project Files\".")] string? parentFolderId = null)
    {
        folderPaths ??= [];
        var countError = FolderPathValidator.ValidatePathCount(folderPaths.Count);
        if (countError is not null)
        {
            return countError;
        }

        string projectId;
        string accessToken;
        FolderInfo root;
        try
        {
            projectId = context.RequireProjectId();
            accessToken = await context.GetAccessTokenAsync();
            root = await ResolveParentAsync(projectId, parentFolderId, accessToken);
        }
        catch (Exception ex)
        {
            return Failure("create the folder structure", ex);
        }

        // Child listings per parent folder ID, so paths sharing a prefix don't re-list it.
        var childrenByParent = new Dictionary<string, List<FolderInfo>>(StringComparer.Ordinal);
        var results = new List<FolderPathResult>();

        foreach (var path in folderPaths)
        {
            var result = new FolderPathResult { Path = path };
            results.Add(result);

            if (!FolderPathValidator.TryParsePath(path, out var segments, out var pathError))
            {
                result.Error = pathError;
                continue;
            }

            var currentId = root.Id;
            var segment = string.Empty;
            try
            {
                foreach (var s in segments)
                {
                    segment = s;
                    var (folder, created) = await GetOrCreateChildAsync(projectId, currentId, segment, childrenByParent, accessToken);
                    (created ? result.Created : result.Existing).Add(folder.Name);
                    currentId = folder.Id;
                }

                result.FolderId = currentId;
            }
            catch (Exception ex)
            {
                result.Error = Failure($"create the folder '{segment}'", ex);
            }
        }

        return ToolResults.Json(new
        {
            ParentFolderId = root.Id,
            ParentFolderName = root.Name,
            Results = results
        });
    }

    // The given folder, or "Project Files" when no ID is given.
    private async Task<FolderInfo> ResolveParentAsync(string projectId, string? parentFolderId, string accessToken)
    {
        if (!string.IsNullOrWhiteSpace(parentFolderId))
        {
            var folder = await dataManagementClient.GetFolderAsync(
                projectId: projectId,
                folderId: parentFolderId.Trim(),
                accessToken: accessToken);

            return new FolderInfo
            {
                Id = folder?.Data?.Id ?? parentFolderId.Trim(),
                Name = folder?.Data?.Attributes?.Name ?? folder?.Data?.Attributes?.DisplayName ?? string.Empty
            };
        }

        var topFolders = await dataManagementClient.GetProjectTopFoldersAsync(
            hubId: context.RequireHubId(),
            projectId: projectId,
            projectFilesOnly: true,
            accessToken: accessToken);

        var projectFiles =
            topFolders.Data?.FirstOrDefault(f => string.Equals(f.Attributes?.Name, ProjectFilesFolderName, StringComparison.OrdinalIgnoreCase)) ??
            topFolders.Data?.FirstOrDefault();

        if (string.IsNullOrEmpty(projectFiles?.Id))
        {
            throw new InvalidOperationException($"The \"{ProjectFilesFolderName}\" folder was not found in this project.");
        }

        return new FolderInfo { Id = projectFiles.Id, Name = projectFiles.Attributes?.Name ?? ProjectFilesFolderName };
    }

    private async Task<List<FolderInfo>> ListChildFoldersAsync(string projectId, string folderId, string accessToken)
    {
        var folders = new List<FolderInfo>();

        for (var pageNumber = 0; ; pageNumber++)
        {
            var contents = await dataManagementClient.GetFolderContentsAsync(
                projectId: projectId,
                folderId: folderId,
                filterType: [FilterType.Folders],
                pageNumber: pageNumber,
                pageLimit: PageLimit,
                accessToken: accessToken);

            var page = contents?.Data?.OfType<FolderData>().ToList() ?? [];
            folders.AddRange(page.Select(f => new FolderInfo
            {
                Id = f.Id ?? string.Empty,
                Name = f.Attributes?.Name ?? f.Attributes?.DisplayName ?? string.Empty
            }));

            if (page.Count == 0 || contents?.Links?.Next is null)
            {
                break;
            }
        }

        return folders;
    }

    // Reuses the child folder with that name (ignoring case) or creates it. Listings are cached
    // in childrenByParent, and a newly created folder starts with an empty listing.
    private async Task<(FolderInfo Folder, bool Created)> GetOrCreateChildAsync(
        string projectId, string parentId, string name, Dictionary<string, List<FolderInfo>> childrenByParent, string accessToken)
    {
        var children = await GetChildFoldersCachedAsync(projectId, parentId, childrenByParent, accessToken);
        var existing = FindByName(children, name);
        if (existing is not null)
        {
            return (existing, false);
        }

        try
        {
            var created = await CreateChildFolderAsync(projectId, parentId, name, accessToken);
            children.Add(created);
            childrenByParent[created.Id] = [];
            return (created, true);
        }
        catch (Exception ex) when (ToolResults.StatusCodeOf(ex) == HttpStatusCode.Conflict)
        {
            // The folder appeared since the listing (e.g. created concurrently): list again and reuse it.
            childrenByParent.Remove(parentId);
            children = await GetChildFoldersCachedAsync(projectId, parentId, childrenByParent, accessToken);
            existing = FindByName(children, name);
            if (existing is not null)
            {
                return (existing, false);
            }

            throw;
        }
    }

    private async Task<List<FolderInfo>> GetChildFoldersCachedAsync(
        string projectId, string parentId, Dictionary<string, List<FolderInfo>> childrenByParent, string accessToken)
    {
        if (!childrenByParent.TryGetValue(parentId, out var children))
        {
            children = await ListChildFoldersAsync(projectId, parentId, accessToken);
            childrenByParent[parentId] = children;
        }

        return children;
    }

    private async Task<FolderInfo> CreateChildFolderAsync(string projectId, string parentId, string name, string accessToken)
    {
        var payload = new FolderPayload
        {
            Data = new FolderPayloadData
            {
                Type = TypeFolder.Folders,
                Attributes = new FolderPayloadDataAttributes
                {
                    Name = name,
                    Extension = new FolderPayloadDataAttributesExtension
                    {
                        Type = "folders:autodesk.bim360:Folder",
                        VarVersion = "1.0"
                    }
                },
                Relationships = new FolderPayloadDataRelationships
                {
                    Parent = new FolderPayloadDataRelationshipsParent
                    {
                        Data = new FolderPayloadDataRelationshipsParentData
                        {
                            Type = TypeFolder.Folders,
                            Id = parentId
                        }
                    }
                }
            }
        };

        var folder = await dataManagementClient.CreateFolderAsync(
            projectId: projectId,
            folderPayload: payload,
            accessToken: accessToken);

        var id = folder?.Data?.Id;
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException($"Creating the folder '{name}' returned no folder ID.");
        }

        return new FolderInfo { Id = id, Name = folder?.Data?.Attributes?.Name ?? name };
    }

    private static FolderInfo? FindByName(IEnumerable<FolderInfo> folders, string name) =>
        folders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Failure(string action, Exception ex) => ToolResults.StatusCodeOf(ex) switch
    {
        HttpStatusCode.Conflict => ToolResults.Error(action, ex) + " A folder or file with that name already exists there.",
        HttpStatusCode.Forbidden => ToolResults.Error(action, ex) + " The agent's service account needs Edit or Manage permission on the parent folder.",
        _ => ToolResults.Error(action, ex),
    };
}

public class FolderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// The outcome of one path in CreateFolderStructure: the deepest folder's ID, the segment names
// that were created and the ones that already existed, or why the path failed.
public class FolderPathResult
{
    public string Path { get; set; } = string.Empty;
    public string? FolderId { get; set; }
    public List<string> Created { get; set; } = [];
    public List<string> Existing { get; set; } = [];
    public string? Error { get; set; }
}
