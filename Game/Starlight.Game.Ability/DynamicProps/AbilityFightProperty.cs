namespace Starlight.Game.Ability.DynamicProps;

internal static class AbilityFightProperty
{
    public const uint HealAdd = 26;
    public const uint HealedAdd = 27;
    public const uint CurHp = 1010;
    public const uint MaxHp = 2000;
    public const uint CurAttack = 2001;
    public const uint CurHpDebts = 2004;
    public const uint CurHpPaidDebts = 2005;

    public static bool TryGetId(string name, out uint id)
    {
        id = name switch {
            "FIGHT_PROP_HEAL_ADD" => HealAdd,
            "FIGHT_PROP_HEALED_ADD" => HealedAdd,
            "FIGHT_PROP_CUR_HP" => CurHp,
            "FIGHT_PROP_MAX_HP" => MaxHp,
            "FIGHT_PROP_CUR_ATTACK" => CurAttack,
            "FIGHT_PROP_CUR_HP_DEBTS" => CurHpDebts,
            "FIGHT_PROP_CUR_HP_PAID_DEBTS" => CurHpPaidDebts,
            _ => 0
        };
        return id != 0;
    }
}
