namespace Starlight.Game.Ability;

public sealed class AbilityRuntimeConfig(Func<bool> isAbilityLoggingEnabled)
{
    public bool LogAbilities => isAbilityLoggingEnabled();
}
