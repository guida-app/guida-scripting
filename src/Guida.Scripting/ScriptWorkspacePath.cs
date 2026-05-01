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
