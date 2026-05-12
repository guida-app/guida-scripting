namespace Guida.Scripting.Engines;

/// <summary>
/// Registers optional concrete script engines with the core engine factory.
/// </summary>
public static class ScriptEngineFactoryEngineExtensions
{
    public static ScriptEngineFactory RegisterClearScriptEngine(this ScriptEngineFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.Register(ScriptLanguage.JavaScript, context => new ClearScriptEngine(context));
        factory.Register(ScriptLanguage.TypeScript, context => new ClearScriptEngine(context));
        return factory;
    }

    public static ScriptEngineFactory RegisterLuaCSharpEngine(this ScriptEngineFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.Register(ScriptLanguage.Lua, context => new LuaCSharpScriptEngine(context));
        return factory;
    }

    public static ScriptEngineFactory RegisterJanetSharpEngine(this ScriptEngineFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.Register(ScriptLanguage.Janet, context => new JanetSharpScriptEngine(context));
        return factory;
    }

    public static ScriptEngineFactory RegisterStandardEngines(this ScriptEngineFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.RegisterClearScriptEngine();
        factory.RegisterLuaCSharpEngine();
        factory.RegisterJanetSharpEngine();
        return factory;
    }
}
