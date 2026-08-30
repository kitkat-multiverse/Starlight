using Starlight.Common;
using Starlight.Game.Modules;
using Starlight.Game.Resources;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

public sealed class AvatarModule(IPlayer player, GameData data, GuidManager guidManager) : IModule
{
    #region Beach Simulator

    private const uint TeamId = 1;
    private static readonly uint[] TeamAvatarIds = [10000005];

    #endregion

    private Avatar[] _team = [];
    private readonly Dictionary<uint, Avatar> _avatars = [];
    private readonly Dictionary<uint, NetAvatar> _avatarState = [];
    private bool _loaded;

    /// The avatars the player walks in with, in slot order.
    public IReadOnlyList<Avatar> Team
    {
        get
        {
            lock (player.StateLock)
            {
                LoadState();
                return [.. _team];
            }
        }
    }

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
            return new AvatarDataNotify {
                CurAvatarTeamId = TeamId,
                ChooseAvatarGuid = _team[0].Guid,
                OwnedFlycloakList = [Avatar.DefaultFlycloak],
                AvatarList = [.. _avatars.Values.Select(avatar => avatar.Info())],
                AvatarTeamMap = {
                    [TeamId] = new AvatarTeam {
                        TeamName = $"Team {TeamId}",
                        AvatarGuidList = [.._team.Select(avatar => avatar.Guid)]
                    }
                }
            };
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
        var notifications = new List<AvatarEquipChangeNotify>();

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

            if (avatar.WeaponGuid == weapon.Guid)
                return response;

            if (!inventory.TryGetWeapon(avatar.WeaponGuid, out var previousWeapon))
            {
                response.Retcode = (int)Retcode.RETCODE_ITEM_NOT_EXIST;
                return response;
            }

            var otherAvatar = _avatars.Values.FirstOrDefault(candidate =>
                candidate.Guid != avatar.Guid && candidate.WeaponGuid == weapon.Guid);

            if (otherAvatar is not null)
            {
                // Clear the old owner's slot before assigning the replacement so the client never
                // sees one GUID equipped by two avatars at once.
                notifications.Add(new AvatarEquipChangeNotify {
                    AvatarGuid = otherAvatar.Guid,
                    EquipType = 6 // EQUIP_WEAPON
                });

                SetWeapon(otherAvatar, previousWeapon);
                notifications.Add(CreateWeaponChangeNotify(otherAvatar, previousWeapon));
            }

            SetWeapon(avatar, weapon);
            notifications.Add(CreateWeaponChangeNotify(avatar, weapon));
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
        uint constellation = 0
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

            var state = new NetAvatar {
                AvatarId = avatar.AvatarId,
                Guid = avatar.Guid,
                Level = avatar.Level,
                Constellation = avatar.Constellation,
                BornTime = avatar.BornTime,
                WeaponGuid = avatar.WeaponGuid
            };
            _avatarState.Add(avatarId, state);
            player.State.Avatars.Add(state);
        }

        var weapon = await player.Module<InventoryModule>()
            .AddWeapon(
                data.WeaponData[avatar.WeaponItemId],
                avatar.WeaponGuid,
                showHint: false);

        // Establish the starter-weapon equip before publishing the new avatar so both client
        // stores receive the same weapon GUID.
        await player.Send(new AvatarEquipChangeNotify {
            AvatarGuid = avatar.Guid,
            EquipGuid = weapon.Guid,
            ItemId = weapon.ItemId,
            EquipType = 6, // EQUIP_WEAPON
            Weapon = weapon.ToSceneProtocol()
        });

        AvatarInfo avatarInfo;

        lock (player.StateLock)
        {
            avatarInfo = avatar.Info();
        }

        await player.Send(new AvatarAddNotify {
            Avatar = avatarInfo,
            IsInTeam = false
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
                    state.WeaponGuid);

                // State written before weapon persistence used the deterministic starter GUID.
                if (state.WeaponGuid == 0)
                    state.WeaponGuid = avatar.WeaponGuid;

                _avatars.Add(avatar.AvatarId, avatar);
                _avatarState.Add(avatar.AvatarId, state);
            }

            // A brand-new player receives the starter roster once. It immediately becomes part of
            // the persisted state, so reconnects preserve its GUID and born time.
            foreach (var (slot, avatarId) in TeamAvatarIds.Index())
            {
                if (_avatars.ContainsKey(avatarId) || !CanCreate(avatarId))
                    continue;

                var guid = (ulong)player.Uid << 32 | (uint)(slot * 2 + 1);
                var avatar = Avatar.Create(data, avatarId, guid);

                var state = new NetAvatar {
                    AvatarId = avatar.AvatarId,
                    Guid = avatar.Guid,
                    Level = avatar.Level,
                    Constellation = avatar.Constellation,
                    BornTime = avatar.BornTime,
                    WeaponGuid = avatar.WeaponGuid
                };

                _avatars.Add(avatarId, avatar);
                _avatarState.Add(avatarId, state);
                player.State.Avatars.Add(state);
            }

            _team = TeamAvatarIds
                .Where(_avatars.ContainsKey)
                .Select(id => _avatars[id])
                .ToArray();
        }
    }

    private void SetWeapon(Avatar avatar, WeaponItem weapon)
    {
        lock (player.StateLock)
        {
            avatar.EquipWeapon(weapon);
            _avatarState[avatar.AvatarId].WeaponGuid = weapon.Guid;
        }
    }

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
