using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptWorkspaceTests
{
    [Fact]
    public void Workspace_can_be_registered_and_retrieved_from_host_context()
    {
        var workspace = new FakeWorkspace();
        var context = ScriptHostContext.Empty.WithCapability<IScriptWorkspace>(workspace);

        Assert.True(context.TryGetCapability<IScriptWorkspace>(out var found));
        Assert.Same(workspace, found);
        Assert.Same(workspace, context.GetCapability<IScriptWorkspace>());
    }

    [Fact]
    public void Missing_workspace_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptWorkspace>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptWorkspace>(out _));
        Assert.Equal(typeof(IScriptWorkspace).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains("IScriptWorkspace", result.Error);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(".", "")]
    [InlineData("scripts/job.js", "scripts/job.js")]
    [InlineData(@"scripts\job.js", "scripts/job.js")]
    [InlineData("scripts//./nested///job.js", "scripts/nested/job.js")]
    public void Workspace_path_normalization_accepts_relative_logical_paths(
        string path,
        string expected)
    {
        var result = ScriptWorkspacePath.Normalize(path);

        Assert.True(result.Success);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("scripts/../outside.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData(@"C:\absolute\path.txt")]
    [InlineData("https://example.test/file.txt")]
    [InlineData("file:///tmp/file.txt")]
    public void Workspace_path_normalization_rejects_unsafe_paths(string path)
    {
        var result = ScriptWorkspacePath.Normalize(path);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, result.Error.Code);
    }

    [Fact]
    public void Workspace_path_normalization_rejects_nul_characters()
    {
        var result = ScriptWorkspacePath.Normalize("scripts/\0/file.txt");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, result.Error?.Code);
    }

    [Theory]
    [InlineData("scripts", "helper.js", "scripts/helper.js")]
    [InlineData("workflows/demo/scripts", "../lib/util.js", "workflows/demo/lib/util.js")]
    [InlineData("", "scripts/main.js", "scripts/main.js")]
    [InlineData(".", "scripts/main.js", "scripts/main.js")]
    [InlineData(@"workflows\demo\scripts", @"..\lib\util.js", "workflows/demo/lib/util.js")]
    [InlineData("scripts/nested", "./helper.js", "scripts/nested/helper.js")]
    [InlineData("scripts/nested", "child//./helper.js", "scripts/nested/child/helper.js")]
    public void Workspace_relative_resolution_resolves_from_base_directory(
        string baseDirectory,
        string relativePath,
        string expected)
    {
        var result = ScriptWorkspacePath.ResolveRelative(baseDirectory, relativePath);

        Assert.True(result.Success);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("", "../outside.txt")]
    [InlineData("scripts", "../../outside.txt")]
    [InlineData("workflows/demo/scripts", "../../../../outside.txt")]
    public void Workspace_relative_resolution_rejects_paths_escaping_root(
        string baseDirectory,
        string relativePath)
    {
        var result = ScriptWorkspacePath.ResolveRelative(baseDirectory, relativePath);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, result.Error.Code);
    }

    [Theory]
    [InlineData("scripts", "/absolute/path.txt")]
    [InlineData("scripts", @"C:\absolute\path.txt")]
    [InlineData("scripts", "https://example.test/file.txt")]
    [InlineData("scripts", "file:///tmp/file.txt")]
    [InlineData("../base", "helper.js")]
    [InlineData("scripts/../base", "helper.js")]
    [InlineData("/base", "helper.js")]
    [InlineData(@"C:\base", "helper.js")]
    [InlineData("https://example.test/base", "helper.js")]
    public void Workspace_relative_resolution_rejects_unsafe_paths(
        string baseDirectory,
        string relativePath)
    {
        var result = ScriptWorkspacePath.ResolveRelative(baseDirectory, relativePath);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, result.Error.Code);
    }

    [Fact]
    public void Workspace_relative_resolution_rejects_nul_characters()
    {
        var invalidBase = ScriptWorkspacePath.ResolveRelative("scripts/\0", "helper.js");
        var invalidRelative = ScriptWorkspacePath.ResolveRelative("scripts", "helper\0.js");

        Assert.False(invalidBase.Success);
        Assert.False(invalidRelative.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, invalidBase.Error?.Code);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, invalidRelative.Error?.Code);
    }

    [Fact]
    public void Sandbox_resolves_logical_paths_beneath_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "guida-scripting-workspace");
        var sandbox = new ScriptWorkspaceSandbox(root);

        var result = sandbox.Resolve("scripts/job.js");

        Assert.True(result.Success);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "scripts", "job.js")),
            result.Value);
    }

    [Fact]
    public void Sandbox_resolves_root_to_canonical_root_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "guida-scripting-workspace", ".");
        var sandbox = new ScriptWorkspaceSandbox(root);

        var result = sandbox.Resolve("");

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(root), result.Value);
    }

    [Fact]
    public void Sandbox_rejects_paths_that_would_escape_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "guida-scripting-workspace");
        var sandbox = new ScriptWorkspaceSandbox(root);

        var result = sandbox.Resolve("../outside.txt");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, result.Error?.Code);
    }

    [Fact]
    public void Workspace_result_helpers_model_success_and_failure()
    {
        var error = new ScriptWorkspaceError(
            ScriptWorkspaceErrorCode.NotFound,
            "missing.txt",
            "File was not found.");

        var success = ScriptWorkspaceResult<string>.Succeeded("ok");
        var failure = ScriptWorkspaceResult<string>.Failed(error);
        var emptySuccess = ScriptWorkspaceResult.Succeeded();
        var emptyFailure = ScriptWorkspaceResult.Failed(error);

        Assert.True(success.Success);
        Assert.Equal("ok", success.Value);
        Assert.Null(success.Error);
        Assert.False(failure.Success);
        Assert.Null(failure.Value);
        Assert.Same(error, failure.Error);
        Assert.True(emptySuccess.Success);
        Assert.False(emptyFailure.Success);
        Assert.Same(error, emptyFailure.Error);
    }

    [Fact]
    public void Workspace_error_converts_to_failed_execution_result()
    {
        var error = new ScriptWorkspaceError(
            ScriptWorkspaceErrorCode.AccessDenied,
            "secret.txt",
            "Access denied.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal("Access denied.", result.Error);
    }

    [Fact]
    public void Write_options_default_to_overwrite_without_creating_directories()
    {
        var options = new ScriptWorkspaceWriteOptions();

        Assert.True(options.Overwrite);
        Assert.False(options.CreateDirectories);
    }

    [Fact]
    public async Task Read_only_workspace_returns_read_only_for_writes()
    {
        var workspace = new FakeWorkspace { IsReadOnly = true };

        var result = await workspace.WriteFileAsync(
            "scripts/job.js",
            "return 42"u8.ToArray());

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.ReadOnly, result.Error?.Code);
    }

    private sealed class FakeWorkspace : IScriptWorkspace
    {
        public bool IsReadOnly { get; init; }

        public Task<ScriptWorkspaceResult<ScriptWorkspaceEntry>> GetEntryAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var normalized = ScriptWorkspacePath.Normalize(path);
            if (!normalized.Success)
            {
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Failed(normalized.Error!));
            }

            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceEntry>.Succeeded(new ScriptWorkspaceEntry
            {
                Path = normalized.Value ?? string.Empty,
                Name = Path.GetFileName(normalized.Value) ?? string.Empty,
                Kind = ScriptWorkspaceEntryKind.File
            }));
        }

        public Task<ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>> ListAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Succeeded(
                Array.Empty<ScriptWorkspaceEntry>()));

        public Task<ScriptWorkspaceResult<ScriptWorkspaceFileContent>> ReadFileAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var normalized = ScriptWorkspacePath.Normalize(path);
            if (!normalized.Success)
            {
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(normalized.Error!));
            }

            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Succeeded(
                new ScriptWorkspaceFileContent
                {
                    Path = normalized.Value ?? string.Empty,
                    Content = ReadOnlyMemory<byte>.Empty
                }));
        }

        public Task<ScriptWorkspaceResult> WriteFileAsync(
            string path,
            ReadOnlyMemory<byte> content,
            ScriptWorkspaceWriteOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (IsReadOnly)
            {
                return Task.FromResult(ScriptWorkspaceResult.Failed(new ScriptWorkspaceError(
                    ScriptWorkspaceErrorCode.ReadOnly,
                    path,
                    "Workspace is read-only.")));
            }

            var normalized = ScriptWorkspacePath.Normalize(path);
            return Task.FromResult(normalized.Success
                ? ScriptWorkspaceResult.Succeeded()
                : ScriptWorkspaceResult.Failed(normalized.Error!));
        }
    }
}
