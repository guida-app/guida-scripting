namespace Guida.Scripting;

/// <summary>
/// Creates script engines from registered host-provided engine factories.
/// </summary>
public sealed class ScriptEngineFactory
{
    private readonly Dictionary<ScriptLanguage, Func<ScriptEngineCreationContext, IScriptEngine>> _registrations = new();

    /// <summary>
    /// Registers or replaces the engine factory for a language.
    /// </summary>
    public void Register(
        ScriptLanguage language,
        Func<ScriptEngineCreationContext, IScriptEngine> engineFactory)
    {
        if (language == ScriptLanguage.Unknown)
        {
            throw new ArgumentException("Cannot register an engine for an unknown script language.", nameof(language));
        }

        ArgumentNullException.ThrowIfNull(engineFactory);

        _registrations[language] = engineFactory;
    }

    /// <summary>
    /// Returns whether an engine factory is registered for the language.
    /// </summary>
    public bool IsRegistered(ScriptLanguage language) => _registrations.ContainsKey(language);

    /// <summary>
    /// Creates an engine for a language.
    /// </summary>
    public IScriptEngine Create(ScriptLanguage language) =>
        Create(new ScriptEngineCreationContext { Language = language });

    /// <summary>
    /// Creates an engine using the provided creation context.
    /// </summary>
    public IScriptEngine Create(ScriptEngineCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Language == ScriptLanguage.Unknown)
        {
            throw new NotSupportedException("No script engine can be created for an unknown script language.");
        }

        if (!_registrations.TryGetValue(context.Language, out var engineFactory))
        {
            throw new NotSupportedException($"No script engine is registered for language '{context.Language}'.");
        }

        return engineFactory(context);
    }
}
