namespace Guida.Scripting;

/// <summary>
/// Resolves logical workspace paths beneath a physical root directory.
/// </summary>
public sealed class ScriptWorkspaceSandbox
{
    private readonly string _rootWithSeparator;
    private readonly StringComparison _pathComparison;

    /// <summary>
    /// Creates a physical-root workspace sandbox.
    /// </summary>
    public ScriptWorkspaceSandbox(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        RootPath = Path.GetFullPath(rootPath);
        _rootWithSeparator = EnsureTrailingSeparator(Path.TrimEndingDirectorySeparator(RootPath));
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    /// <summary>
    /// Canonical physical root path.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Resolves a logical workspace path to a physical path beneath <see cref="RootPath" />.
    /// </summary>
    public ScriptWorkspaceResult<string> Resolve(string path)
    {
        var normalized = ScriptWorkspacePath.Normalize(path);
        if (!normalized.Success)
        {
            return ScriptWorkspaceResult<string>.Failed(normalized.Error!);
        }

        try
        {
            var logicalPath = normalized.Value ?? string.Empty;
            var physicalPath = logicalPath.Length == 0
                ? RootPath
                : Path.Combine(RootPath, logicalPath.Replace('/', Path.DirectorySeparatorChar));
            var fullPath = Path.GetFullPath(physicalPath);

            if (!IsInsideRoot(fullPath))
            {
                return ScriptWorkspaceResult<string>.Failed(new ScriptWorkspaceError(
                    ScriptWorkspaceErrorCode.AccessDenied,
                    path,
                    "Resolved workspace path escapes the sandbox root."));
            }

            return ScriptWorkspaceResult<string>.Succeeded(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ScriptWorkspaceResult<string>.Failed(new ScriptWorkspaceError(
                ScriptWorkspaceErrorCode.InvalidPath,
                path,
                exception.Message));
        }
    }

    private bool IsInsideRoot(string fullPath) =>
        string.Equals(fullPath, RootPath, _pathComparison) ||
        fullPath.StartsWith(_rootWithSeparator, _pathComparison);

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
}
