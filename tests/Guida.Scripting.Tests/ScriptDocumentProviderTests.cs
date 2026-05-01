using System.Text;
using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptDocumentProviderTests
{
    [Fact]
    public void Document_provider_can_be_registered_and_retrieved_from_host_context()
    {
        var provider = new FakeDocumentProvider();
        var context = ScriptHostContext.Empty.WithCapability<IScriptDocumentProvider>(provider);

        Assert.True(context.TryGetCapability<IScriptDocumentProvider>(out var found));
        Assert.Same(provider, found);
        Assert.Same(provider, context.GetCapability<IScriptDocumentProvider>());
    }

    [Fact]
    public void Missing_document_provider_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptDocumentProvider>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptDocumentProvider>(out _));
        Assert.Equal(typeof(IScriptDocumentProvider).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptDocumentProvider), result.Error);
    }

    [Fact]
    public async Task Workspace_provider_loads_document_from_workspace()
    {
        var workspace = new FakeWorkspace();
        workspace.Files["scripts/job.js"] = "return 42"u8.ToArray();
        var provider = new ScriptWorkspaceDocumentProvider(workspace);

        var result = await provider.LoadAsync("scripts/job.js");

        Assert.True(result.Success);
        Assert.Equal("scripts/job.js", result.Value?.Id);
        Assert.Equal("scripts/job.js", result.Value?.Name);
        Assert.Equal("return 42", result.Value?.Source);
        Assert.Equal(ScriptLanguage.JavaScript, result.Value?.Language);
        Assert.Null(result.Value?.SourceUri);
        Assert.Null(result.Value?.Version);
        Assert.Null(result.Value?.LastModifiedAt);
    }

    [Fact]
    public async Task Workspace_provider_decodes_utf8_bom_source()
    {
        var workspace = new FakeWorkspace();
        workspace.Files["scripts/job.js"] = [0xEF, 0xBB, 0xBF, .. "return 42"u8.ToArray()];
        var provider = new ScriptWorkspaceDocumentProvider(workspace);

        var result = await provider.LoadAsync("scripts/job.js");

        Assert.True(result.Success);
        Assert.Equal("return 42", result.Value?.Source);
    }

    [Theory]
    [InlineData("job.js", ScriptLanguage.JavaScript)]
    [InlineData("job.ts", ScriptLanguage.TypeScript)]
    [InlineData("job.lua", ScriptLanguage.Lua)]
    [InlineData("job.janet", ScriptLanguage.Janet)]
    public async Task Workspace_provider_detects_language_from_document_id(
        string documentId,
        ScriptLanguage expectedLanguage)
    {
        var workspace = new FakeWorkspace();
        workspace.Files[documentId] = "source"u8.ToArray();
        var provider = new ScriptWorkspaceDocumentProvider(workspace);

        var result = await provider.LoadAsync(documentId);

        Assert.True(result.Success);
        Assert.Equal(expectedLanguage, result.Value?.Language);
    }

    [Fact]
    public async Task Workspace_provider_language_override_takes_precedence()
    {
        var workspace = new FakeWorkspace();
        workspace.Files["scripts/job.js"] = "print('hello')"u8.ToArray();
        var provider = new ScriptWorkspaceDocumentProvider(workspace);

        var result = await provider.LoadAsync(
            "scripts/job.js",
            new ScriptDocumentLoadOptions { Language = ScriptLanguage.Lua });

        Assert.True(result.Success);
        Assert.Equal(ScriptLanguage.Lua, result.Value?.Language);
    }

    [Fact]
    public void ScriptDocument_to_execution_request_preserves_source_identity_and_context()
    {
        var hostContext = new ScriptHostContext { Logger = new FakeLogger() };
        var document = new ScriptDocument
        {
            Id = "scripts/job.lua",
            Name = "scripts/job.lua",
            Source = "return 42",
            Language = ScriptLanguage.Lua,
            SourceUri = new Uri("guida-workspace:///scripts/job.lua"),
            Version = "v1",
            LastModifiedAt = DateTimeOffset.UtcNow
        };

        var request = document.ToExecutionRequest(hostContext);

        Assert.Equal(document.Source, request.Source);
        Assert.Equal(document.Name, request.Name);
        Assert.Equal(document.Language, request.Language);
        Assert.Same(hostContext, request.HostContext);
    }

    [Theory]
    [InlineData(ScriptWorkspaceErrorCode.InvalidPath, ScriptDocumentErrorCode.InvalidId)]
    [InlineData(ScriptWorkspaceErrorCode.NotFound, ScriptDocumentErrorCode.NotFound)]
    [InlineData(ScriptWorkspaceErrorCode.NotAFile, ScriptDocumentErrorCode.NotAFile)]
    [InlineData(ScriptWorkspaceErrorCode.NotADirectory, ScriptDocumentErrorCode.NotAFile)]
    [InlineData(ScriptWorkspaceErrorCode.AccessDenied, ScriptDocumentErrorCode.AccessDenied)]
    [InlineData(ScriptWorkspaceErrorCode.IoError, ScriptDocumentErrorCode.IoError)]
    public async Task Workspace_provider_maps_workspace_errors_to_document_errors(
        ScriptWorkspaceErrorCode workspaceCode,
        ScriptDocumentErrorCode expectedCode)
    {
        var workspace = new FakeWorkspace
        {
            ReadError = new ScriptWorkspaceError(
                workspaceCode,
                "scripts/job.js",
                "workspace failed")
        };
        var provider = new ScriptWorkspaceDocumentProvider(workspace);

        var result = await provider.LoadAsync("scripts/job.js");

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.Equal("scripts/job.js", result.Error?.DocumentId);
        Assert.Equal("workspace failed", result.Error?.Message);
    }

    [Fact]
    public async Task Workspace_provider_rejects_empty_document_id()
    {
        var provider = new ScriptWorkspaceDocumentProvider(new FakeWorkspace());

        var result = await provider.LoadAsync(" ");

        Assert.False(result.Success);
        Assert.Equal(ScriptDocumentErrorCode.InvalidId, result.Error?.Code);
    }

    [Fact]
    public async Task Workspace_provider_returns_unsupported_encoding_for_invalid_utf8()
    {
        var workspace = new FakeWorkspace();
        workspace.Files["scripts/job.js"] = [0xC3, 0x28];
        var provider = new ScriptWorkspaceDocumentProvider(workspace);

        var result = await provider.LoadAsync("scripts/job.js");

        Assert.False(result.Success);
        Assert.Equal(ScriptDocumentErrorCode.UnsupportedEncoding, result.Error?.Code);
    }

    [Fact]
    public async Task Workspace_provider_propagates_cancellation()
    {
        var provider = new ScriptWorkspaceDocumentProvider(new FakeWorkspace());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.LoadAsync("scripts/job.js", cancellationToken: cancellationTokenSource.Token));
    }

    [Fact]
    public void Document_error_converts_to_failed_execution_result()
    {
        var error = new ScriptDocumentError(
            ScriptDocumentErrorCode.NotFound,
            "scripts/missing.js",
            "Document was not found.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(error.Message, result.Error);
    }

    private sealed class FakeDocumentProvider : IScriptDocumentProvider
    {
        public Task<ScriptDocumentResult<ScriptDocument>> LoadAsync(
            string documentId,
            ScriptDocumentLoadOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ScriptDocumentResult<ScriptDocument>.Succeeded(new ScriptDocument
            {
                Id = documentId,
                Name = documentId,
                Source = string.Empty,
                Language = options?.Language ?? ScriptLanguage.Unknown
            }));
    }

    private sealed class FakeWorkspace : IScriptWorkspace
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public ScriptWorkspaceError? ReadError { get; init; }

        public bool IsReadOnly => true;

        public Task<ScriptWorkspaceResult<ScriptWorkspaceEntry>> GetEntryAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ScriptWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>> ListAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ScriptWorkspaceResult<ScriptWorkspaceFileContent>> ReadFileAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReadError is not null)
            {
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(ReadError));
            }

            var normalized = ScriptWorkspacePath.Normalize(path);
            if (!normalized.Success)
            {
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(normalized.Error!));
            }

            if (!Files.TryGetValue(normalized.Value!, out var content))
            {
                return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Failed(
                    new ScriptWorkspaceError(
                        ScriptWorkspaceErrorCode.NotFound,
                        path,
                        "Workspace file was not found.")));
            }

            return Task.FromResult(ScriptWorkspaceResult<ScriptWorkspaceFileContent>.Succeeded(
                new ScriptWorkspaceFileContent
                {
                    Path = normalized.Value!,
                    Content = content
                }));
        }

        public Task<ScriptWorkspaceResult> WriteFileAsync(
            string path,
            ReadOnlyMemory<byte> content,
            ScriptWorkspaceWriteOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLogger : IScriptLogger
    {
        public void Log(ScriptLogEntry entry)
        {
        }
    }
}
