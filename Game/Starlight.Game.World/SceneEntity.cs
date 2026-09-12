using Starlight.Game.Resources;
using Starlight.Protocol;

namespace Starlight.Game.World;

/// <summary>
///     Base for runtime entities in a scene.
///     Fight properties live here and are shared directly with the entity's ability component.
/// </summary>
public abstract class SceneEntity
{
    protected SceneEntity(Scene scene, SceneEntityInfo info, FightPropertyStore fightProperties)
    {
        Scene = scene;
        Info = info;
        FightProperties = fightProperties;

        FightProperties.Changed += OnFightPropertyChanged;
        FightProperties.Replaced += RebuildFightPropertyInfo;
        RebuildFightPropertyInfo();
    }

    public Scene Scene { get; }
    public SceneEntityInfo Info { get; }
    public FightPropertyStore FightProperties { get; }

    public uint EntityId => Info.EntityId;
    public bool IsAlive => Info.LifeState == 1;
    public MotionInfo? MotionInfo => Info.MotionInfo;

    public uint AttackTargetId { get; set; }

    public uint LastMoveReliableSeq
    {
        get => Info.LastMoveReliableSeq;
        set => Info.LastMoveReliableSeq = value;
    }

    public uint LastMoveSceneTimeMs
    {
        get => Info.LastMoveSceneTimeMs;
        set => Info.LastMoveSceneTimeMs = value;
    }

    /// <summary>The peer allowed to authoritatively move/control this entity.</summary>
    public abstract uint AuthorityPeerId { get; }

    public virtual bool RemoveFromSceneOnDeath => true;

    public float GetFightProperty(uint property) => FightProperties.Get(property);
    public float GetFightProperty(FightProperty property) => FightProperties.Get(property);

    public bool HasFightProperty(uint property) => FightProperties.ContainsKey(property);
    public bool HasFightProperty(FightProperty property) => HasFightProperty((uint)property);

    public void SetFightProperty(uint property, float value) => FightProperties.Set(property, value);
    public void SetFightProperty(FightProperty property, float value) => FightProperties.Set(property, value);

    public void AddFightProperty(uint property, float amount) => FightProperties.Add(property, amount);
    public void AddFightProperty(FightProperty property, float amount) => FightProperties.Add(property, amount);

    public virtual EntityDamageResult Damage(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f || !IsAlive ||
            !HasFightProperty(FightProperty.FIGHT_PROP_CUR_HP))
            return EntityDamageResult.None;

        var previousHp = GetFightProperty(FightProperty.FIGHT_PROP_CUR_HP);

        if (float.IsNaN(previousHp) || previousHp <= 0f || float.IsPositiveInfinity(previousHp))
            return EntityDamageResult.None;

        var currentHp = Math.Max(val1: 0f, previousHp - amount);

        if (currentHp.Equals(previousHp))
            return EntityDamageResult.None;

        SetFightProperty(FightProperty.FIGHT_PROP_CUR_HP, currentHp);

        var died = currentHp <= 0f;
        var hpDebtCleared = 0f;

        if (died)
        {
            if (HasFightProperty(FightProperty.FIGHT_PROP_CUR_HP_DEBTS))
            {
                hpDebtCleared = GetFightProperty(FightProperty.FIGHT_PROP_CUR_HP_DEBTS);

                if (hpDebtCleared != 0f)
                    SetFightProperty(FightProperty.FIGHT_PROP_CUR_HP_DEBTS, value: 0f);
            }

            Info.LifeState = 2;
        }

        return new EntityDamageResult(
            Applied: true,
            previousHp,
            currentHp,
            previousHp - currentHp,
            died,
            hpDebtCleared);
    }

    public virtual void OnDeath(uint attackerId)
    {
        // lua, etc.
    }

    internal void Detach()
    {
        FightProperties.Changed -= OnFightPropertyChanged;
        FightProperties.Replaced -= RebuildFightPropertyInfo;
    }

    private void OnFightPropertyChanged(uint property, float value)
    {
        if (property == 0)
            return;

        var pair = Info.FightPropList.FirstOrDefault(item => item.PropType == property);

        if (pair is null)
            Info.FightPropList.Add(new FightPropPair { PropType = property, PropValue = value });
        else
            pair.PropValue = value;
    }

    private void RebuildFightPropertyInfo()
    {
        Info.FightPropList.Clear();

        foreach (var (property, value) in FightProperties)
        {
            if (property == 0)
                continue;

            Info.FightPropList.Add(new FightPropPair { PropType = property, PropValue = value });
        }
    }
}

public readonly record struct EntityDamageResult(
    bool Applied,
    float PreviousHp,
    float CurrentHp,
    float EffectiveDamage,
    bool Died,
    float ClearedHpDebt
)
{
    public static EntityDamageResult None => default;
}
