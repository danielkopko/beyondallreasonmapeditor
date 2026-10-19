using MapCore;

namespace MapEditor;

/// <summary>
/// Working folders for open maps: %LOCALAPPDATA%\BarMapEditor\workspaces\&lt;guid&gt;, one per open map,
/// deleted when the map is closed. The source archive is never written to.
/// </summary>
static class Workspaces
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarMapEditor", "workspaces");

    public static string Create()
    {
        var dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Deletes a workspace; refuses anything that is not a folder inside <see cref="Root"/>.</summary>
    /// <returns>Null on success, otherwise why it could not be deleted.</returns>
    public static string? Delete(string dir)
    {
        if (!FileSystemUtil.IsSameOrUnder(dir, Root) || FileSystemUtil.SamePath(dir, Root))
            return $"Not deleting {dir}: it is not an editor workspace.";
        try
        {
            FileSystemUtil.DeleteTree(dir);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not delete the workspace {dir}: {ex.Message}";
        }
    }
}
