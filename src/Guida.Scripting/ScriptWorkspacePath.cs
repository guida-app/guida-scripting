namespace Guida.Scripting;

/// <summary>
/// Normalizes and validates logical workspace-relative paths.
/// </summary>
public static class ScriptWorkspacePath
{
    /// <summary>
    /// Normalizes a logical workspace path to slash-separated relative form.
    /// </summary>
    public static ScriptWorkspaceResult<string> Normalize(string path)
    {
        if (path is null)
        {
            return Invalid(string.Empty, "Workspace path cannot be null.");
        }

        if (path.IndexOf('\0') >= 0)
        {
            return Invalid(path, "Workspace path cannot contain NUL characters.");
        }

        if (IsWindowsDrivePath(path))
        {
            return Invalid(path, "Workspace path must not be drive-rooted.");
        }

        if (HasUriScheme(path))
        {
            return Invalid(path, "Workspace path must not be a URI.");
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return Invalid(path, "Workspace path must be relative.");
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                return Invalid(path, "Workspace path must not contain traversal segments.");
            }

            segments.Add(segment);
        }

        return ScriptWorkspaceResult<string>.Succeeded(string.Join('/', segments));
    }

    /// <summary>
    /// Resolves a relative path against a normalized workspace directory path.
    /// </summary>
    public static ScriptWorkspaceResult<string> ResolveRelative(
        string baseDirectoryPath,
        string relativePath)
    {
        var normalizedBase = Normalize(baseDirectoryPath);
        if (!normalizedBase.Success)
        {
            return ScriptWorkspaceResult<string>.Failed(normalizedBase.Error!);
        }

        if (relativePath is null)
        {
            return Invalid(string.Empty, "Workspace path cannot be null.");
        }

        if (relativePath.IndexOf('\0') >= 0)
        {
            return Invalid(relativePath, "Workspace path cannot contain NUL characters.");
        }

        if (IsWindowsDrivePath(relativePath))
        {
            return Invalid(relativePath, "Workspace path must not be drive-rooted.");
        }

        if (HasUriScheme(relativePath))
        {
            return Invalid(relativePath, "Workspace path must not be a URI.");
        }

        var normalizedRelative = relativePath.Replace('\\', '/');
        if (normalizedRelative.StartsWith('/'))
        {
            return Invalid(relativePath, "Workspace path must be relative.");
        }

        var segments = new List<string>();
        if (!string.IsNullOrEmpty(normalizedBase.Value))
        {
            segments.AddRange(normalizedBase.Value.Split('/'));
        }

        foreach (var segment in normalizedRelative.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return Invalid(relativePath, "Workspace path must not escape the workspace root.");
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return ScriptWorkspaceResult<string>.Succeeded(string.Join('/', segments));
    }

    private static ScriptWorkspaceResult<string> Invalid(string path, string message) =>
        ScriptWorkspaceResult<string>.Failed(new ScriptWorkspaceError(
            ScriptWorkspaceErrorCode.InvalidPath,
            path,
            message));

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= 2 &&
        char.IsAsciiLetter(path[0]) &&
        path[1] == ':';

    private static bool HasUriScheme(string path)
    {
        var colonIndex = path.IndexOf(':');
        if (colonIndex <= 0)
        {
            return false;
        }

        if (!char.IsAsciiLetter(path[0]))
        {
            return false;
        }

        for (var index = 1; index < colonIndex; index++)
        {
            var character = path[index];
            if (!char.IsAsciiLetterOrDigit(character) &&
                character != '+' &&
                character != '-' &&
                character != '.')
            {
                return false;
            }
        }

        return true;
    }
}
