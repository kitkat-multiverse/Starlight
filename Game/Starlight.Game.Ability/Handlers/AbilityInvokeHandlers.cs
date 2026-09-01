using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers;

/// <summary>
/// Base interface for handlers that process ability invokes on the server.
/// Concrete handlers are discovered and registered automatically by <see cref="AbilityServiceCollectionExtensions.AddAbilityInvokeHandlers"/>.
/// </summary>
public interface IAbilityInvokeHandler
{
    int Order => 0;
    ValueTask HandleAsync(AbilityContext context);
}

public interface IAbilityActionHandler : IAbilityInvokeHandler
{
    string ActionType { get; }
}

public interface IAbilityMixinHandler : IAbilityInvokeHandler
{
    string MixinType { get; }
}

public interface IAbilityArgumentHandler : IAbilityInvokeHandler
{
    AbilityInvokeArgument ArgumentType { get; }
}

public abstract class AbilityActionHandler : IAbilityActionHandler
{
    protected AbilityActionHandler()
    {
        ActionType = AbilityHandlerTypeName.FromHandlerType(GetType());
    }

    protected AbilityActionHandler(string actionType)
    {
        ActionType = AbilityHandlerTypeName.Require(actionType, nameof(actionType), "action");
    }

    public string ActionType { get; }
    public virtual int Order => 0;
    public abstract ValueTask HandleAsync(AbilityContext context);
}

public abstract class AbilityMixinHandler : IAbilityMixinHandler
{
    protected AbilityMixinHandler()
    {
        MixinType = AbilityHandlerTypeName.FromHandlerType(GetType());
    }

    protected AbilityMixinHandler(string mixinType)
    {
        MixinType = AbilityHandlerTypeName.Require(mixinType, nameof(mixinType), "mixin");
    }

    public string MixinType { get; }
    public virtual int Order => 0;
    public abstract ValueTask HandleAsync(AbilityContext context);
}

internal static class AbilityHandlerTypeName
{
    private const string Suffix = "Handler";

    public static string FromHandlerType(Type handlerType)
    {
        var name = handlerType.Name;

        if (!name.EndsWith(Suffix, StringComparison.Ordinal) || name.Length == Suffix.Length)
        {
            throw new InvalidOperationException(
                $"{handlerType.FullName} must end with '{Suffix}' when using convention-based ability handler registration.");
        }

        return name[..^Suffix.Length];
    }

    public static string Require(string value, string parameterName, string kind) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Ability {kind} type cannot be empty.", parameterName);
}

public abstract class AbilityArgumentHandler(AbilityInvokeArgument argumentType) : IAbilityArgumentHandler
{
    public AbilityInvokeArgument ArgumentType { get; } = argumentType;
    public virtual int Order => 0;
    public abstract ValueTask HandleAsync(AbilityContext context);
}

/// <summary>
/// Keeps track of the registered ability invoke handlers and dispatches invokes to them.
/// Argument handlers run first, followed by matching action and mixin handlers.
/// Handlers with a lower <see cref="IAbilityInvokeHandler.Order"/> run first.
/// </summary>
public sealed class AbilityInvokeHandlerRegistry
{
    private readonly IReadOnlyDictionary<AbilityInvokeArgument, IAbilityInvokeHandler[]> _arguments;
    private readonly IReadOnlyDictionary<string, IAbilityInvokeHandler[]> _actions;
    private readonly IReadOnlyDictionary<string, IAbilityInvokeHandler[]> _mixins;

    public AbilityInvokeHandlerRegistry(IEnumerable<IAbilityInvokeHandler> handlers)
    {
        var materialized = handlers.ToArray();

        _arguments = materialized
            .OfType<IAbilityArgumentHandler>()
            .GroupBy(handler => handler.ArgumentType)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(handler => handler.Order).Cast<IAbilityInvokeHandler>().ToArray());

        _actions = materialized
            .OfType<IAbilityActionHandler>()
            .GroupBy(handler => handler.ActionType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(handler => handler.Order).Cast<IAbilityInvokeHandler>().ToArray(),
                StringComparer.Ordinal);

        _mixins = materialized
            .OfType<IAbilityMixinHandler>()
            .GroupBy(handler => handler.MixinType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(handler => handler.Order).Cast<IAbilityInvokeHandler>().ToArray(),
                StringComparer.Ordinal);
    }

    public async ValueTask DispatchAsync(AbilityContext context)
    {
        if (_arguments.TryGetValue(context.Invoke.ArgumentType, out var argumentHandlers))
            await Execute(argumentHandlers, context);

        if (context.Action is { Type.Length: > 0 } action &&
            _actions.TryGetValue(action.Type, out var actionHandlers))
            await Execute(actionHandlers, context);

        if (context.Mixin is { Type.Length: > 0 } mixin &&
            _mixins.TryGetValue(mixin.Type, out var mixinHandlers))
            await Execute(mixinHandlers, context);
    }

    private static async ValueTask Execute(
        IEnumerable<IAbilityInvokeHandler> handlers,
        AbilityContext context
    )
    {
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(context);
        }
    }
}
