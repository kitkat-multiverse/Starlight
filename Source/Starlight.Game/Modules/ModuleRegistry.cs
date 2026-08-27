using System.Collections.Frozen;
using Starlight.Game.Player;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Modules;

/// <summary>Invokes a single module's handler for a message, sending any reply through the player.</summary>
public delegate ValueTask ModuleHandler(IModule module, IPlayer player, IMessage message);

/// <summary>
/// Central, version-agnostic table mapping module types to a dense index and message types to the
/// handlers that should run for them. Components register modules and handlers at startup; after
/// <see cref="Build"/> the registry is read-only and every lookup on the hot path is an array index
/// or a <see cref="FrozenDictionary{TKey,TValue}"/> read.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly List<Func<IServiceProvider, IPlayer, IModule>> _factories = [];
    private readonly Dictionary<Type, int> _moduleIndex = [];
    private readonly Dictionary<Type, List<PendingHandler>> _handlers = [];

    private Func<IServiceProvider, IPlayer, IModule>[] _compiledFactories = [];
    private FrozenDictionary<Type, int> _compiledIndex = FrozenDictionary<Type, int>.Empty;
    private FrozenDictionary<Type, CompiledHandler[]> _table = FrozenDictionary<Type, CompiledHandler[]>.Empty;

    public bool Immutable { get; private set; }

    /// <summary>Registers a module type and its per-player factory. Idempotent per type.</summary>
    public void AddModule<TModule>(Func<IServiceProvider, IPlayer, TModule> factory) where TModule : class, IModule
    {
        ThrowIfImmutable();

        if (_moduleIndex.ContainsKey(typeof(TModule)))
            return;

        _moduleIndex[typeof(TModule)] = _factories.Count;
        _factories.Add(factory);
    }

    /// <summary>Registers a handler for <typeparamref name="TMessage"/> living on <typeparamref name="TModule"/>.</summary>
    public void AddHandler<TModule, TMessage>(int priority, ModuleHandler handler)
        where TModule : class, IModule
        where TMessage : class, IMessage
    {
        ThrowIfImmutable();

        if (!_handlers.TryGetValue(typeof(TMessage), out var list))
            _handlers[typeof(TMessage)] = list = [];

        list.Add(new PendingHandler(typeof(TModule), priority, handler));
    }

    /// <summary>Freezes the registry into its read-only lookup form, returning it. Call once, after all components register.</summary>
    public ModuleRegistry Build()
    {
        _compiledFactories = _factories.ToArray();
        _compiledIndex = _moduleIndex.ToFrozenDictionary();

        _table = _handlers.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderByDescending(h => h.Priority)
                .Select(h => new CompiledHandler(
                    _moduleIndex.TryGetValue(h.ModuleType, out var index) ?
                        index :
                        throw new InvalidOperationException(
                            $"A handler is registered for '{h.ModuleType.Name}', but that module was never added."),
                    h.Handler))
                .ToArray());

        Immutable = true;

        return this;
    }

    private void ThrowIfImmutable()
    {
        if (Immutable)
        {
            throw new InvalidOperationException(
                "The module registry has already been built; registrations after Build() never run.");
        }
    }

    /// <summary>Number of registered module types; the size of each player's module array.</summary>
    public int ModuleCount => _compiledFactories.Length;

    /// <summary>Builds a fresh module set for <paramref name="player"/>, indexed by module index.</summary>
    public IModule[] CreateModules(IServiceProvider provider, IPlayer player)
    {
        var modules = new IModule[_compiledFactories.Length];

        for (var i = 0; i < modules.Length; i++)
            modules[i] = _compiledFactories[i](provider, player);
        return modules;
    }

    /// <summary>The dense index of <typeparamref name="TModule"/>, for resolving an instance from a player's array.</summary>
    public int IndexOf<TModule>() where TModule : class, IModule => _compiledIndex[typeof(TModule)];

    /// <summary>Runs every handler registered for the runtime type of <paramref name="message"/>, in priority order.</summary>
    public async ValueTask Dispatch(IPlayer player, IModule[] modules, IMessage message)
    {
        if (!_table.TryGetValue(message.GetType(), out var handlers))
            return;

        foreach (var handler in handlers)
        {
            await handler.Invoke(modules[handler.ModuleIndex], player, message);
        }
    }

    private readonly record struct PendingHandler(Type ModuleType, int Priority, ModuleHandler Handler);

    private readonly record struct CompiledHandler(int ModuleIndex, ModuleHandler Invoke);
}
