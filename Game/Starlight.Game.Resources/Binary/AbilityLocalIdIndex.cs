namespace Starlight.Game.Resources.Binary;

internal static class AbilityLocalIdIndex
{
    private enum ContainerType : long
    {
        Action = 1,
        Mixin = 2,
        ModifierAction = 3,
        ModifierMixin = 4
    }

    public static void Initialize(AbilityConfig ability)
    {
        var actions = new Dictionary<int, AbilityConfigNode>();
        var mixins = new Dictionary<int, AbilityConfigNode>();
        ability.ModifierNames = ability.Modifiers.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var mixinGenerator = new Generator(ContainerType.Mixin);
        mixinGenerator.InitializeMixins(ability.AbilityMixins, mixins, actions);

        long modifierIndex = 0;
        long modifierMixinIndex = 0;

        foreach (var name in ability.ModifierNames)
        {
            var modifier = ability.Modifiers[name];
            long configIndex = 0;

            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnAdded, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnRemoved, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnBeingHit, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnAttackLanded, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnHittingOther, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnThinkInterval, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnKill, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnCrash, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnAvatarIn, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnAvatarOut, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnReconnect, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnChangeAuthority, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnVehicleIn, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnVehicleOut, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnZoneEnter, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnZoneExit, actions);
            InitializeModifierActions(modifierIndex, configIndex++, modifier.OnHeal, actions);
            InitializeModifierActions(modifierIndex, configIndex, modifier.OnBeingHealed, actions);

            if (modifier.ModifierMixins.Count != 0)
            {
                var generator = new Generator(ContainerType.ModifierMixin) {
                    ModifierIndex = modifierIndex,
                    MixinIndex = modifierMixinIndex
                };
                generator.InitializeMixins(modifier.ModifierMixins, mixins, actions);
                modifierMixinIndex = generator.MixinIndex;
            }

            modifierIndex++;
        }

        var actionGenerator = new Generator(ContainerType.Action);
        long abilityConfigIndex = 0;
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnAdded, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnRemoved, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnAbilityStart, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnKill, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnFieldEnter, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnFieldExit, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnAttach, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnDetach, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnAvatarIn, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnAvatarOut, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnTriggerAvatarRay, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex++;
        actionGenerator.InitializeActions(ability.OnVehicleIn, actions);
        actionGenerator.ConfigIndex = abilityConfigIndex;
        actionGenerator.InitializeActions(ability.OnVehicleOut, actions);

        ability.ActionsByLocalId = actions;
        ability.MixinsByLocalId = mixins;
    }

    private static void InitializeModifierActions(
        long modifierIndex,
        long configIndex,
        List<AbilityConfigNode> nodes,
        Dictionary<int, AbilityConfigNode> actions
    )
    {
        if (nodes.Count == 0)
            return;

        new Generator(ContainerType.ModifierAction) {
            ModifierIndex = modifierIndex,
            ConfigIndex = configIndex
        }.InitializeActions(nodes, actions);
    }

    private sealed class Generator(ContainerType type)
    {
        public long ModifierIndex { get; set; }
        public long ConfigIndex { get; set; }
        public long MixinIndex { get; set; }
        private long ActionIndex { get; set; }

        public void InitializeActions(
            IReadOnlyList<AbilityConfigNode> nodes,
            Dictionary<int, AbilityConfigNode> actions,
            bool preserveActionIndex = false
        )
        {
            if (!preserveActionIndex)
                ActionIndex = 0;

            foreach (var node in nodes)
            {
                ActionIndex++;
                actions[(int)GetLocalId()] = node;

                if (node.Actions.Count != 0)
                {
                    InitializeActions(node.Actions, actions, preserveActionIndex: true);
                } else
                {
                    if (node.SuccessActions.Count != 0)
                        InitializeActions(node.SuccessActions, actions, preserveActionIndex: true);

                    if (node.FailActions.Count != 0)
                        InitializeActions(node.FailActions, actions, preserveActionIndex: true);
                }
            }

            if (!preserveActionIndex)
                ActionIndex = 0;
        }

        public void InitializeMixins(
            IReadOnlyList<AbilityConfigNode> nodes,
            Dictionary<int, AbilityConfigNode> mixins,
            Dictionary<int, AbilityConfigNode> actions
        )
        {
            foreach (var node in nodes)
            {
                mixins[(int)GetLocalId()] = node;

                InitializeActions(node.SuccActions, actions, preserveActionIndex: true);
                InitializeActions(node.Actions, actions, preserveActionIndex: true);
                InitializeActions(node.OnStageReady, actions, preserveActionIndex: true);
                InitializeActions(node.OnEatFood, actions, preserveActionIndex: true);
                InitializeActions(node.OnEnterArea, actions, preserveActionIndex: true);
                InitializeActions(node.OnExitArea, actions, preserveActionIndex: true);
                InitializeActions(node.OnSelectStart, actions, preserveActionIndex: true);
                InitializeActions(node.OnSelectEnd, actions, preserveActionIndex: true);
                InitializeActions(node.OnBeingHit, actions, preserveActionIndex: true);
                InitializeActions(node.OnAttackLanded, actions, preserveActionIndex: true);
                InitializeActions(node.OnHittingOther, actions, preserveActionIndex: true);
                InitializeActions(node.OnThinkInterval, actions, preserveActionIndex: true);
                InitializeActions(node.OnKill, actions, preserveActionIndex: true);
                InitializeActions(node.OnCrash, actions, preserveActionIndex: true);
                InitializeActions(node.OnAvatarIn, actions, preserveActionIndex: true);
                InitializeActions(node.OnAvatarOut, actions, preserveActionIndex: true);
                InitializeActions(node.OnReconnect, actions, preserveActionIndex: true);
                InitializeActions(node.OnChangeAuthority, actions, preserveActionIndex: true);
                InitializeActions(node.OnVehicleIn, actions, preserveActionIndex: true);
                InitializeActions(node.OnVehicleOut, actions, preserveActionIndex: true);
                InitializeActions(node.OnZoneEnter, actions, preserveActionIndex: true);
                InitializeActions(node.OnZoneExit, actions, preserveActionIndex: true);
                InitializeActions(node.OnHeal, actions, preserveActionIndex: true);
                InitializeActions(node.OnBeingHealed, actions, preserveActionIndex: true);

                MixinIndex++;
            }
        }

        private long GetLocalId() => type switch {
            ContainerType.Action => (long)type + (ConfigIndex << 3) + (ActionIndex << 9),
            ContainerType.Mixin => (long)type + (MixinIndex << 3) + (ConfigIndex << 9) + (ActionIndex << 15),
            ContainerType.ModifierAction =>
                (long)type + (ModifierIndex << 3) + (ConfigIndex << 9) + (ActionIndex << 15),
            ContainerType.ModifierMixin =>
                (long)type + (ModifierIndex << 3) + (MixinIndex << 9) + (ConfigIndex << 15) + (ActionIndex << 21),
            _ => -1
        };
    }
}
