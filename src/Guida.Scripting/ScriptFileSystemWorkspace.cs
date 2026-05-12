namespace Guida.Scripting;

/// <summary>
/// Local filesystem-backed workspace guarded by a physical root sandbox.
/// </summary>
public sealed class ScriptFileSystemWorkspace : IScriptWorkspace
{
    private readonly ScriptWorkspaceSandbox _sandbox;
    private readonly ScriptFileSystemWorkspaceOptions _options;

    /// <summary>
    /// Creates a local filesystem workspace rooted at <paramref name="rootPath" />.
    /// </summary>
    public ScriptFileSystemWorkspace(
        string rootPath,
        ScriptFileSystemWorkspaceOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _options = options ?? new ScriptFileSystemWorkspaceOptions();
        _sandbox = new ScriptWorkspaceSandbox(rootPath);
        RootPath = _sandbox.RootPath;

        if (!Directory.Exists(RootPath))
        {
            if (!_options.CreateRoot)
            {
                throw new DirectoryNotFoundException(
                    $"Workspace root '{RootPath}' does not exist.");
            }

            Directory.CreateDirectory(RootPath);
        }

        if (!Directory.Exists(RootPath))
        {
            throw new DirectoryNotFoundException(
                $"Workspace root '{RootPath}' does not exist.");
        }

        if (_options.DenyReparsePoints && IsReparsePoint(RootPath))
        {
            throw new UnauthorizedAccessException(
                $"Workspace root '{RootPath}' is a reparse point.");
        }
    }

    /// <summary>
    /// Canonical physical root path.
    /// </summary>
    public string RootPath { get; }

    /// <inheritdoc />
    public bool IsReadOnly => _options.IsReadOnly;

    /// <inheritdoc />
    public Task<ScriptWorkspaceResult<ScriptWorkspaceEntry>> GetEntryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = Resolve(path);
        if (!resolved.Success)
        {
            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Failed(resolved.Error!));
        }

        var guard = CheckExistingPathAllowed(resolved.Value!.PhysicalPath, path);
        if (!guard.Success)
        {
            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Failed(guard.Error!));
        }

        try
        {
            if (File.Exists(resolved.Value.PhysicalPath))
            {
                var fileInfo = new FileInfo(resolved.Value.PhysicalPath);
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Succeeded(
                    CreateEntry(resolved.Value.LogicalPath, fileInfo)));
            }

            if (Directory.Exists(resolved.Value.PhysicalPath))
            {
                var directoryInfo = new DirectoryInfo(resolved.Value.PhysicalPath);
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Succeeded(
                    CreateEntry(resolved.Value.LogicalPath, directoryInfo)));
            }

            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Failed(
                Error(ScriptWorkspaceErrorCode.NotFound, path, "Workspace entry was not found.")));
        }
        catch (Exception exception) when (IsExpectedIoException(exception))
        {
            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Failed(
                IoError(path, exception)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>> ListAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = Resolve(path);
        if (!resolved.Success)
        {
            return Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Failed(resolved.Error!));
        }

        var guard = CheckExistingPathAllowed(resolved.Value!.PhysicalPath, path);
        if (!guard.Success)
        {
            return Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Failed(guard.Error!));
        }

        try
        {
            if (File.Exists(resolved.Value.PhysicalPath))
            {
                return Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Failed(
                    Error(ScriptWorkspaceErrorCode.NotADirectory, path, "Workspace path is not a directory.")));
            }

            if (!Directory.Exists(resolved.Value.PhysicalPath))
            {
                return Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Failed(
                    Error(ScriptWorkspaceErrorCode.NotFound, path, "Workspace directory was not found.")));
            }

            var entries = Directory
                .EnumerateFileSystemEntries(resolved.Value.PhysicalPath)
                .Select(entryPath => CreateEntry(GetChildLogicalPath(resolved.Value.LogicalPath, entryPath), entryPath))
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Succeeded(entries));
        }
        catch (Exception exception) when (IsExpectedIoException(exception))
        {
            return Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Failed(
                IoError(path, exception)));
        }
    }

    /// <inheritdoc />
    public async Task<ScriptWorkspaceResult<ScriptWorkspaceFileContent>> ReadFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = Resolve(path);
        if (!resolved.Success)
        {
            return ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(resolved.Error!);
        }

        var guard = CheckExistingPathAllowed(resolved.Value!.PhysicalPath, path);
        if (!guard.Success)
        {
            return ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(guard.Error!);
        }

        try
        {
            if (Directory.Exists(resolved.Value.PhysicalPath))
            {
                return ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(
                    Error(ScriptWorkspaceErrorCode.NotAFile, path, "Workspace path is not a file."));
            }

            if (!File.Exists(resolved.Value.PhysicalPath))
            {
                return ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(
                    Error(ScriptWorkspaceErrorCode.NotFound, path, "Workspace file was not found."));
            }

            var content = await File.ReadAllBytesAsync(
                resolved.Value.PhysicalPath,
                cancellationToken).ConfigureAwait(false);

            return ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Succeeded(
                new ScriptWorkspaceFileContent
                {
                    Path = resolved.Value.LogicalPath,
                    Content = content
                });
        }
        catch (Exception exception) when (IsExpectedIoException(exception))
        {
            return ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(IoError(path, exception));
        }
    }

    /// <inheritdoc />
    public async Task<ScriptWorkspaceResult> WriteFileAsync(
        string path,
        ReadOnlyMemory<byte> content,
        ScriptWorkspaceWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsReadOnly)
        {
            return ScriptWorkspaceResult.Failed(
                Error(ScriptWorkspaceErrorCode.ReadOnly, path, "Workspace is read-only."));
        }

        var writeOptions = options ?? new ScriptWorkspaceWriteOptions();
        var resolved = Resolve(path);
        if (!resolved.Success)
        {
            return ScriptWorkspaceResult.Failed(resolved.Error!);
        }

        if (resolved.Value!.LogicalPath.Length == 0)
        {
            return ScriptWorkspaceResult.Failed(
                Error(ScriptWorkspaceErrorCode.NotAFile, path, "Workspace root is not a file."));
        }

        var parentPath = Path.GetDirectoryName(resolved.Value.PhysicalPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return ScriptWorkspaceResult.Failed(
                Error(ScriptWorkspaceErrorCode.InvalidPath, path, "Workspace path has no parent directory."));
        }

        var parentGuard = CheckExistingAncestorsAllowed(parentPath, path);
        if (!parentGuard.Success)
        {
            return parentGuard;
        }

        var targetGuard = CheckExistingPathAllowed(resolved.Value.PhysicalPath, path);
        if (!targetGuard.Success)
        {
            return targetGuard;
        }

        try
        {
            if (Directory.Exists(resolved.Value.PhysicalPath))
            {
                return ScriptWorkspaceResult.Failed(
                    Error(ScriptWorkspaceErrorCode.NotAFile, path, "Workspace path is not a file."));
            }

            if (File.Exists(resolved.Value.PhysicalPath) && !writeOptions.Overwrite)
            {
                return ScriptWorkspaceResult.Failed(
                    Error(ScriptWorkspaceErrorCode.AlreadyExists, path, "Workspace file already exists."));
            }

            if (!Directory.Exists(parentPath))
            {
                if (!writeOptions.CreateDirectories)
                {
                    return ScriptWorkspaceResult.Failed(
                        Error(ScriptWorkspaceErrorCode.NotFound, path, "Parent workspace directory was not found."));
                }

                Directory.CreateDirectory(parentPath);
            }

            var fileMode = writeOptions.Overwrite ? FileMode.Create : FileMode.CreateNew;
            await using var stream = new FileStream(
                resolved.Value.PhysicalPath,
                fileMode,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);

            return ScriptWorkspaceResult.Succeeded();
        }
        catch (IOException exception) when (!writeOptions.Overwrite && File.Exists(resolved.Value.PhysicalPath))
        {
            return ScriptWorkspaceResult.Failed(
                Error(ScriptWorkspaceErrorCode.AlreadyExists, path, exception.Message));
        }
        catch (Exception exception) when (IsExpectedIoException(exception))
        {
            return ScriptWorkspaceResult.Failed(IoError(path, exception));
        }
    }

    private ResolvedPathResult Resolve(string path)
    {
        var normalized = ScriptWorkspacePath.Normalize(path);
        if (!normalized.Success)
        {
            return ResolvedPathResult.Failed(normalized.Error!);
        }

        var resolved = _sandbox.Resolve(normalized.Value ?? string.Empty);
        return resolved.Success
            ? ResolvedPathResult.Succeeded(normalized.Value ?? string.Empty, resolved.Value!)
            : ResolvedPathResult.Failed(resolved.Error!);
    }

    private ScriptWorkspaceResult CheckExistingPathAllowed(string physicalPath, string logicalPath)
    {
        if (!_options.DenyReparsePoints)
        {
            return ScriptWorkspaceResult.Succeeded();
        }

        var currentPath = physicalPath;
        while (!string.IsNullOrWhiteSpace(currentPath) && IsInsideRootOrEqual(currentPath))
        {
            if ((File.Exists(currentPath) || Directory.Exists(currentPath)) &&
                IsReparsePoint(currentPath))
            {
                return ScriptWorkspaceResult.Failed(
                    Error(ScriptWorkspaceErrorCode.AccessDenied, logicalPath, "Workspace path contains a reparse point."));
            }

            if (string.Equals(currentPath, RootPath, GetPathComparison()))
            {
                break;
            }

            currentPath = Path.GetDirectoryName(currentPath);
        }

        return ScriptWorkspaceResult.Succeeded();
    }

    private ScriptWorkspaceResult CheckExistingAncestorsAllowed(string physicalPath, string logicalPath)
    {
        if (!_options.DenyReparsePoints)
        {
            return ScriptWorkspaceResult.Succeeded();
        }

        var currentPath = physicalPath;
        while (!string.IsNullOrWhiteSpace(currentPath) && IsInsideRootOrEqual(currentPath))
        {
            if ((File.Exists(currentPath) || Directory.Exists(currentPath)) &&
                IsReparsePoint(currentPath))
            {
                return ScriptWorkspaceResult.Failed(
                    Error(ScriptWorkspaceErrorCode.AccessDenied, logicalPath, "Workspace path contains a reparse point."));
            }

            if (string.Equals(currentPath, RootPath, GetPathComparison()))
            {
                break;
            }

            currentPath = Path.GetDirectoryName(currentPath);
        }

        return ScriptWorkspaceResult.Succeeded();
    }

    private bool IsInsideRootOrEqual(string physicalPath)
    {
        var comparison = GetPathComparison();
        var root = Path.TrimEndingDirectorySeparator(RootPath);
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalPath));
        return string.Equals(path, root, comparison) ||
            path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

    private static ScriptWorkspaceEntry CreateEntry(string logicalPath, string physicalPath)
    {
        if (File.Exists(physicalPath))
        {
            return CreateEntry(logicalPath, new FileInfo(physicalPath));
        }

        return CreateEntry(logicalPath, new DirectoryInfo(physicalPath));
    }

    private static ScriptWorkspaceEntry CreateEntry(string logicalPath, FileInfo fileInfo) =>
        new()
        {
            Path = logicalPath,
            Name = logicalPath.Length == 0 ? fileInfo.Name : Path.GetFileName(logicalPath),
            Kind = ScriptWorkspaceEntryKind.File,
            Length = fileInfo.Length,
            LastModifiedAt = fileInfo.LastWriteTimeUtc
        };

    private static ScriptWorkspaceEntry CreateEntry(string logicalPath, DirectoryInfo directoryInfo) =>
        new()
        {
            Path = logicalPath,
            Name = logicalPath.Length == 0 ? directoryInfo.Name : Path.GetFileName(logicalPath),
            Kind = ScriptWorkspaceEntryKind.Directory,
            LastModifiedAt = directoryInfo.LastWriteTimeUtc
        };

    private static string GetChildLogicalPath(string parentLogicalPath, string childPhysicalPath)
    {
        var childName = Path.GetFileName(childPhysicalPath);
        return parentLogicalPath.Length == 0 ? childName : $"{parentLogicalPath}/{childName}";
    }

    private static ScriptWorkspaceError Error(
        ScriptWorkspaceErrorCode code,
        string path,
        string message) =>
        new(code, path, message);

    private static ScriptWorkspaceError IoError(string path, Exception exception) =>
        new(ScriptWorkspaceErrorCode.IoError, path, exception.Message);

    private static bool IsExpectedIoException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record ResolvedPath(string LogicalPath, string PhysicalPath);

    private sealed record ResolvedPathResult
    {
        private ResolvedPathResult(bool success, ResolvedPath? value, ScriptWorkspaceError? error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public bool Success { get; }

        public ResolvedPath? Value { get; }

        public ScriptWorkspaceError? Error { get; }

        public static ResolvedPathResult Succeeded(string logicalPath, string physicalPath) =>
            new(true, new ResolvedPath(logicalPath, physicalPath), null);

        public static ResolvedPathResult Failed(ScriptWorkspaceError error) =>
            new(false, null, error);
    }
}
