using Starlight.Common;
using Starlight.Game.Modules;
using Starlight.Game.Resources;
using Starlight.Protocol;
using Starlight.Rpc.Proto;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public sealed class AvatarModule(IPlayer player, GameData data, GuidManager guidManager, IWeaponEntityService? weaponEntities = null) : IModule
{
    private readonly Dictionary<uint, Avatar> _avatars = [];
    private readonly Dictionary<uint, NetAvatar> _avatarState = [];
    private bool _loaded;

    /// Every avatar the player currently owns, keyed by avatar ID.
    public IReadOnlyDictionary<uint, Avatar> Avatars
    {
        get
        {
            lock (player.StateLock)
            {
                LoadState();
                return new Dictionary<uint, Avatar>(_avatars);
            }
        }
    }

    public bool OwnsAvatar(uint avatarId)
    {
        lock (player.StateLock)
        {
            LoadState();
            return _avatars.ContainsKey(avatarId);
        }
    }

    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public async Task<AvatarDataNotify> OnLogin()
    {
        Avatar[] avatars;

        lock (player.StateLock)
        {
            LoadState();
            avatars = [.. _avatars.Values];
        }

        var inventory = player.Module<InventoryModule>();
        inventory.LoadState();

        foreach (var avatar in avatars)
        {
            if (inventory.TryGetWeapon(avatar.WeaponGuid, out var equipped))
            {
                lock (player.StateLock)
                {
                    avatar.EquipWeapon(equipped);
                }

                continue;
            }

            // Repair old or incomplete state with the avatar's initial weapon.
            var weapon = await inventory.AddWeapon(
                data.WeaponData[avatar.WeaponItemId],
                avatar.WeaponGuid);

            lock (player.StateLock)
            {
                avatar.EquipWeapon(weapon);
            }
        }

        lock (player.StateLock)
        {
            var teams = player.Module<TeamModule>().Teams;
            var current = teams.GetValueOrDefault(player.State.CurrentAvatarTeamId);

            var notify = new AvatarDataNotify {
                CurAvatarTeamId = current?.Id ?? 0,
                ChooseAvatarGuid = current?.CurrentAvatarGuid ?? 0,
                OwnedFlycloakList = [Avatar.DefaultFlycloak],
                AvatarList = [.. _avatars.Values.Select(avatar => avatar.Info())]
            };

            foreach (var team in teams.Values)
            {
                notify.AvatarTeamMap.Add(team.Id, team.Info());
            }

            return notify;
        }
    }

    /// <summary>Equips a weapon, moving or swapping it when another avatar is using it.</summary>
    [Opcode]
    public async Task<WearEquipRsp> OnWearEquip(WearEquipReq msg)
    {
        var response = new WearEquipRsp {
            AvatarGuid = msg.AvatarGuid,
            EquipGuid = msg.EquipGuid
        };
        var notifications = new List<IMessage>();

        lock (player.StateLock)
        {
            LoadState();

            var avatar = _avatars.Values.FirstOrDefault(candidate => candidate.Guid == msg.AvatarGuid);

            if (avatar is null)
            {
                response.Retcode = (int)Retcode.RETCODE_ITEM_INVALID_TARGET;
                return response;
            }

            var inventory = player.Module<InventoryModule>();
            inventory.LoadState();

            if (!inventory.TryGetWeapon(msg.EquipGuid, out var weapon))
            {
                response.Retcode = (int)Retcode.RETCODE_ITEM_NOT_EXIST;
                return response;
            }

            EquipWeapon(avatar, weapon, shouldRecalculate: true, notifications);
        }

        foreach (var notification in notifications)
        {
            await player.Send(notification);
        }

        return response;
    }

    /// <summary>Grants an avatar outside the active team and notifies the client.</summary>
    public async Task<(Avatar? Avatar, bool Added)> AddAvatar(
        uint avatarId,
        uint level = 1,
        uint constellation = 0,
        bool isInTeam = false
    )
    {
        Avatar avatar;

        lock (player.StateLock)
        {
            if (!CanCreate(avatarId))
                return (null, false);

            LoadState();

            if (_avatars.TryGetValue(avatarId, out var existing))
                return (existing, false);

            var guid = guidManager.GenGuid(GuidManager.GuidType.Avatar);
            avatar = Avatar.Create(data, avatarId, guid, level, constellation);

            _avatars.Add(avatarId, avatar);

            var state = CreateState(avatar);
            _avatarState.Add(avatarId, state);
            player.State.Avatars.Add(state);
        }

        var weapon = await player.Module<InventoryModule>()
            .AddWeapon(
                data.WeaponData[avatar.WeaponItemId],
                avatar.WeaponGuid,
                showHint: false);

        AvatarInfo avatarInfo;
        AvatarEquipChangeNotify equipNotify;

        lock (player.StateLock)
        {
            avatar.EquipWeapon(weapon);
            weaponEntities?.Equip(player, avatar, weapon);
            SyncState(avatar, _avatarState[avatar.AvatarId]);
            avatarInfo = avatar.Info();
            equipNotify = CreateWeaponChangeNotify(avatar, weapon);
        }

        await player.Send(equipNotify);

        await player.Send(new AvatarAddNotify {
            Avatar = avatarInfo,
            IsInTeam = isInTeam
        });

        return (avatar, true);
    }

    private void LoadState()
    {
        lock (player.StateLock)
        {
            if (_loaded)
                return;

            _loaded = true;

            foreach (var state in player.State.Avatars)
            {
                if (state.AvatarId == 0 || state.Guid == 0 || _avatars.ContainsKey(state.AvatarId)
                    || !CanCreate(state.AvatarId))
                    continue;

                var avatar = Avatar.Create(
                    data,
                    state.AvatarId,
                    state.Guid,
                    state.Level,
                    state.Constellation,
                    state.BornTime,
                    state.WeaponGuid,
                    state.SkillDepotId,
                    state.TalentIdList,
                    state.SkillLevelMap);

                // State written before weapon persistence used the deterministic starter GUID.
                if (state.WeaponGuid == 0)
                    state.WeaponGuid = avatar.WeaponGuid;

                SyncState(avatar, state);
                _avatars.Add(avatar.AvatarId, avatar);
                _avatarState.Add(avatar.AvatarId, state);
            }

            if (player.State.BornState != NetPlayerState.Types.PlayerBornState.Pending)
            {
                var starterAvatarId = player.State.BornAvatarId is AetherId or LumineId ? player.State.BornAvatarId : AetherId;

                // A brand-new player receives the starter roster once. It immediately becomes part of
                // the persisted state, so reconnects preserve its GUID and born time.
                if (!_avatars.ContainsKey(starterAvatarId) && CanCreate(starterAvatarId))
                {
                    var guid = (ulong)player.Uid << 32 | 1;
                    var avatar = Avatar.Create(data, starterAvatarId, guid);

                    var state = CreateState(avatar);

                    _avatars.Add(starterAvatarId, avatar);
                    _avatarState.Add(starterAvatarId, state);
                    player.State.Avatars.Add(state);
                }
            }
        }
    }

    private void EquipWeapon(
        Avatar avatar,
        WeaponItem weapon,
        bool shouldRecalculate,
        List<IMessage> notifications,
        bool refreshWeaponEntity = false
    )
    {
        var otherAvatar = weapon.EquipAvatarId == 0 ? null : _avatars.GetValueOrDefault(weapon.EquipAvatarId);

        if (otherAvatar is not null)
        {
            if (otherAvatar.UnequipWeapon() is not null)
                notifications.Add(CreateWeaponClearNotify(otherAvatar));

            if (avatar.Weapon is {} toSwap)
                EquipWeapon(otherAvatar, toSwap, shouldRecalculate: false, notifications);

            otherAvatar.Recalculate();

            if (weaponEntities?.RefreshAvatarAbilities(player, otherAvatar) is {} otherAbilityChange)
                notifications.Add(otherAbilityChange);

            SyncState(otherAvatar, _avatarState[otherAvatar.AvatarId]);
        } else if (avatar.Weapon is not null)
        {
            avatar.UnequipWeapon();
        }

        avatar.EquipWeapon(weapon, recalculate: false);
        weaponEntities?.Equip(player, avatar, weapon, refreshWeaponEntity);
        notifications.Add(CreateWeaponChangeNotify(avatar, weapon));

        if (shouldRecalculate)
        {
            avatar.Recalculate();

            if (weaponEntities?.RefreshAvatarAbilities(player, avatar) is {} abilityChange)
                notifications.Add(abilityChange);
        }

        SyncState(avatar, _avatarState[avatar.AvatarId]);
    }

    private static NetAvatar CreateState(Avatar avatar)
    {
        var state = new NetAvatar();
        SyncState(avatar, state);
        return state;
    }

    private static void SyncState(Avatar avatar, NetAvatar state)
    {
        state.AvatarId = avatar.AvatarId;
        state.Guid = avatar.Guid;
        state.Level = avatar.Level;
        state.Constellation = avatar.Constellation;
        state.BornTime = avatar.BornTime;
        state.WeaponGuid = avatar.WeaponGuid;
        state.SkillDepotId = avatar.SkillDepotId;

        state.TalentIdList.Clear();
        state.TalentIdList.AddRange(avatar.AllTalentIds.Order());

        state.SkillLevelMap.Clear();

        foreach (var (skillId, level) in avatar.AllSkillLevels)
        {
            state.SkillLevelMap[skillId] = level;
        }
    }

    private const uint AetherId = 10000005;
    private const uint LumineId = 10000007;

    public async Task<Avatar?> InitializeTraveler(uint avatarId)
    {
        // Prevent players from getting a different avatar via modifying packets.
        if (avatarId is not AetherId and not LumineId)
            return null;

        lock (player.StateLock)
        {
            LoadState();

            if (player.State.BornState !=
                NetPlayerState.Types.PlayerBornState.Pending)
            {
                return null;
            }
        }

        var (avatar, _) = await AddAvatar(avatarId, level: 1, constellation: 0, isInTeam: true);

        if (avatar is null)
            return null;

        AvatarTeamUpdateNotify notify;

        lock (player.StateLock)
        {
            player.State.BornAvatarId = avatarId;

            player.State.BornState =
                NetPlayerState.Types.PlayerBornState.Complete;

            player.Module<TeamModule>().Initialize(avatar);

            var teams = player.Module<TeamModule>().Teams;

            notify = new AvatarTeamUpdateNotify();

            foreach (var team in teams.Values)
            {
                notify.AvatarTeamMap.Add(team.Id, team.Info());
            }
        }

        await player.Send(notify);
        return avatar;
    }

    private static AvatarEquipChangeNotify CreateWeaponClearNotify(Avatar avatar)
        => new() {
            AvatarGuid = avatar.Guid,
            EquipType = 6 // EQUIP_WEAPON
        };

    private static AvatarEquipChangeNotify CreateWeaponChangeNotify(Avatar avatar, WeaponItem weapon)
        => new() {
            AvatarGuid = avatar.Guid,
            EquipGuid = weapon.Guid,
            ItemId = weapon.ItemId,
            EquipType = 6, // EQUIP_WEAPON
            Weapon = weapon.ToSceneProtocol()
        };

    private bool CanCreate(uint avatarId)
        => data.AvatarData.TryGetValue(avatarId, out var avatar)
           && data.AvatarSkillDepotData.ContainsKey(avatar.SkillDepotId)
           && data.WeaponData.ContainsKey(avatar.InitialWeapon)
           && data.Avatars.ContainsKey(avatarId);
}
