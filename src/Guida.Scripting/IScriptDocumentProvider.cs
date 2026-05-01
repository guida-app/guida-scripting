namespace Guida.Scripting;

/// <summary>
/// Loads script source documents by host-defined logical identifier.
/// </summary>
public interface IScriptDocumentProvider : IScriptHostCapability
{
    /// <summary>
    /// Loads one script source document.
    /// </summary>
    Task<ScriptDocumentResult<ScriptDocument>> LoadAsync(
        string documentId,
        ScriptDocumentLoadOptions? options = null,
        CancellationToken cancellationToken = default);
}
