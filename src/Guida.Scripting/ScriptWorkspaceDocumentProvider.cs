using System.Text;

namespace Guida.Scripting;

/// <summary>
/// Loads script source documents from an <see cref="IScriptWorkspace" />.
/// </summary>
public sealed class ScriptWorkspaceDocumentProvider : IScriptDocumentProvider
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IScriptWorkspace _workspace;

    /// <summary>
    /// Creates a workspace-backed document provider.
    /// </summary>
    public ScriptWorkspaceDocumentProvider(IScriptWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        _workspace = workspace;
    }

    /// <inheritdoc />
    public async Task<ScriptDocumentResult<ScriptDocument>> LoadAsync(
        string documentId,
        ScriptDocumentLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Failed(
                ScriptDocumentErrorCode.InvalidId,
                documentId ?? string.Empty,
                "Document id cannot be empty.");
        }

        var file = await _workspace
            .ReadFileAsync(documentId, cancellationToken)
            .ConfigureAwait(false);
        if (!file.Success)
        {
            return ScriptDocumentResult<ScriptDocument>.Failed(
                MapWorkspaceError(documentId, file.Error!));
        }

        try
        {
            var source = StripUtf8Bom(StrictUtf8.GetString(file.Value!.Content.Span));
            var id = file.Value.Path;
            var language = options?.Language ?? ScriptLanguageDetector.Detect(id);

            return ScriptDocumentResult<ScriptDocument>.Succeeded(new ScriptDocument
            {
                Id = id,
                Name = id,
                Source = source,
                Language = language
            });
        }
        catch (DecoderFallbackException exception)
        {
            return Failed(
                ScriptDocumentErrorCode.UnsupportedEncoding,
                documentId,
                exception.Message);
        }
    }

    private static ScriptDocumentError MapWorkspaceError(
        string documentId,
        ScriptWorkspaceError workspaceError) =>
        workspaceError.Code switch
        {
            ScriptWorkspaceErrorCode.InvalidPath => new ScriptDocumentError(
                ScriptDocumentErrorCode.InvalidId,
                documentId,
                workspaceError.Message),
            ScriptWorkspaceErrorCode.NotFound => new ScriptDocumentError(
                ScriptDocumentErrorCode.NotFound,
                documentId,
                workspaceError.Message),
            ScriptWorkspaceErrorCode.NotAFile or ScriptWorkspaceErrorCode.NotADirectory => new ScriptDocumentError(
                ScriptDocumentErrorCode.NotAFile,
                documentId,
                workspaceError.Message),
            ScriptWorkspaceErrorCode.AccessDenied => new ScriptDocumentError(
                ScriptDocumentErrorCode.AccessDenied,
                documentId,
                workspaceError.Message),
            _ => new ScriptDocumentError(
                ScriptDocumentErrorCode.IoError,
                documentId,
                workspaceError.Message)
        };

    private static ScriptDocumentResult<ScriptDocument> Failed(
        ScriptDocumentErrorCode code,
        string documentId,
        string message) =>
        ScriptDocumentResult<ScriptDocument>.Failed(
            new ScriptDocumentError(code, documentId, message));

    private static string StripUtf8Bom(string source) =>
        source.Length > 0 && source[0] == '\uFEFF' ? source[1..] : source;
}
