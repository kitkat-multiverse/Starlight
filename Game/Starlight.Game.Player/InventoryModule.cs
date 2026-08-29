using System.Diagnostics.CodeAnalysis;
using Starlight.Game.Modules;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

/// <summary>Owns one player's material stacks and equipment instances.</summary>
public sealed class InventoryModule(IPlayer player) : IModule
{
    private const uint PackWeightLimit = 30000;
    private const int MaterialCountLimit = 2000;
    private const int WeaponCountLimit = 2000;
    private const int NotifyChunkSize = 100;
    private static readonly TimeSpan BulkNotifyInterval = TimeSpan.FromMilliseconds(25);

    private readonly Dictionary<uint, MaterialItem> _materials = [];
    private readonly Dictionary<ulong, WeaponItem> _weapons = [];
    private readonly Dictionary<uint, NetMaterial> _materialState = [];
    private readonly Dictionary<ulong, NetWeapon> _weaponState = [];

    // The low range is currently occupied by starter avatars and their weapons. This partition
    // keeps temporary inventory GUIDs separate until the allocator is persisted in DbGate.
    private ulong _nextGuid = 0x20000000;
    private bool _loaded;
    private bool _loggedIn;

    public IReadOnlyCollection<MaterialItem> Materials => _materials.Values;
    public IReadOnlyCollection<WeaponItem> Weapons => _weapons.Values;

    public bool TryGetMaterial(uint itemId, [NotNullWhen(true)] out MaterialItem? item)
    {
        LoadState();
        return _materials.TryGetValue(itemId, out item);
    }

    public bool TryGetWeapon(ulong guid, [NotNullWhen(true)] out WeaponItem? item)
    {
        LoadState();
        return _weapons.TryGetValue(guid, out item);
    }

    public async Task<MaterialItem> AddMaterial(uint itemId, uint count)
    {
        LoadState();
        ArgumentOutOfRangeException.ThrowIfZero(count);

        if (!_materials.ContainsKey(itemId) && _materials.Count >= MaterialCountLimit)
            throw new InvalidOperationException("The material inventory is full.");

        var change = AddMaterialCore(itemId, count);
        await NotifyAdded([change]);
        return (MaterialItem)change.Item;
    }

    public async Task<IReadOnlyList<MaterialItem>> AddMaterials(
        IEnumerable<uint> itemIds,
        uint count,
        bool showHint = true)
    {
        LoadState();
        ArgumentOutOfRangeException.ThrowIfZero(count);

        var changes = new List<AddedItem>();

        foreach (var itemId in itemIds.Distinct())
        {
            // Existing stacks can still be updated when every material slot is occupied.
            if (!_materials.ContainsKey(itemId) && _materials.Count >= MaterialCountLimit)
                continue;

            changes.Add(AddMaterialCore(itemId, count));
        }

        await NotifyAdded(changes, showHint);
        return [.. changes.Select(change => (MaterialItem)change.Item)];
    }

    public async Task<IReadOnlyList<WeaponItem>> AddWeapons(
        IEnumerable<WeaponData> weapons,
        uint amount = 1,
        uint level = 1,
        uint refinement = 1,
        bool showHint = true)
    {
        LoadState();
        ArgumentOutOfRangeException.ThrowIfZero(amount);
        level = Math.Clamp(level, 1u, 90u);
        refinement = Math.Clamp(refinement, 1u, 5u);

        var changes = new List<AddedItem>();

        foreach (var data in weapons)
        {
            for (var copy = 0u; copy < amount; copy++)
            {
                if (_weapons.Count >= WeaponCountLimit)
                    break;

                changes.Add(AddWeaponCore(data, NextGuid(), level, refinement));
            }

            if (_weapons.Count >= WeaponCountLimit)
                break;
        }

        await NotifyAdded(changes, showHint);
        return [.. changes.Select(change => (WeaponItem)change.Item)];
    }

    /// <summary>Adds an externally allocated weapon, such as an avatar's starter weapon.</summary>
    public async Task<WeaponItem> AddWeapon(
        WeaponData data,
        ulong guid,
        uint level = 1,
        uint refinement = 1,
        bool showHint = true)
    {
        LoadState();
        if (_weapons.TryGetValue(guid, out var existing))
            return existing;

        if (_weapons.Count >= WeaponCountLimit)
            throw new InvalidOperationException("The weapon inventory is full.");

        var change = AddWeaponCore(
            data,
            guid,
            Math.Clamp(level, 1u, 90u),
            Math.Clamp(refinement, 1u, 5u));

        await NotifyAdded([change], showHint);
        return (WeaponItem)change.Item;
    }

    public async Task<bool> RemoveMaterial(uint itemId, uint count)
    {
        LoadState();
        if (!_materials.TryGetValue(itemId, out var item) || count == 0 || item.Count < count)
            return false;

        item.Count -= count;

        if (item.Count == 0)
        {
            _materials.Remove(itemId);

            if (_materialState.Remove(itemId, out var state))
                player.State.Materials.Remove(state);

            if (_loggedIn)
            {
                await player.Send(new StoreItemDelNotify {
                    StoreType = StoreType.STORE_TYPE_PACK,
                    GuidList = { item.Guid }
                });
            }
        }
        else
        {
            _materialState[itemId].Count = item.Count;

            if (_loggedIn)
            {
                await player.Send(new StoreItemChangeNotify {
                    StoreType = StoreType.STORE_TYPE_PACK,
                    ItemList = { item.ToProtocol() }
                });
            }
        }

        return true;
    }

    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public async Task OnLogin()
    {
        LoadState();

        await player.Send(new StoreWeightLimitNotify {
            StoreType = StoreType.STORE_TYPE_PACK,
            MaterialCountLimit = MaterialCountLimit,
            WeaponCountLimit = WeaponCountLimit,
            ReliquaryCountLimit = 2000,
            WeightLimit = PackWeightLimit
        });

        var items = _materials.Values.Select(item => item.ToProtocol())
            .Concat(_weapons.Values.Select(item => item.ToProtocol()));

        await player.Send(new PlayerStoreNotify {
            StoreType = StoreType.STORE_TYPE_PACK,
            WeightLimit = PackWeightLimit,
            ItemList = [.. items]
        });

        _loggedIn = true;
    }

    private AddedItem AddMaterialCore(uint itemId, uint count)
    {
        if (!_materials.TryGetValue(itemId, out var item))
        {
            item = new MaterialItem {
                ItemId = itemId,
                Guid = NextGuid(),
                Count = count
            };

            _materials.Add(itemId, item);
            var state = new NetMaterial { ItemId = item.ItemId, Guid = item.Guid, Count = item.Count };
            _materialState.Add(itemId, state);
            player.State.Materials.Add(state);
            return new AddedItem(item, count, IsNew: true);
        }

        item.Count = checked(item.Count + count);
        _materialState[itemId].Count = item.Count;
        return new AddedItem(item, count, IsNew: false);
    }

    private AddedItem AddWeaponCore(WeaponData data, ulong guid, uint level, uint refinement)
    {
        var item = new WeaponItem {
            ItemId = data.Id,
            Guid = guid,
            GadgetId = data.GadgetId,
            Level = level,
            Refinement = refinement,
            PromoteLevel = WeaponItem.PromoteLevelFor(level),
            AffixId = data.SkillAffix.FirstOrDefault()
        };

        _weapons.Add(guid, item);
        var state = new NetWeapon {
            ItemId = item.ItemId,
            Guid = item.Guid,
            GadgetId = item.GadgetId,
            Level = item.Level,
            Refinement = item.Refinement,
            PromoteLevel = item.PromoteLevel,
            AffixId = item.AffixId
        };
        _weaponState.Add(guid, state);
        player.State.Weapons.Add(state);
        return new AddedItem(item, Count: 1, IsNew: true);
    }

    /// <summary>Hydrates the module once from the state DbGate attached to the player.</summary>
    internal void LoadState()
    {
        if (_loaded)
            return;

        _loaded = true;

        foreach (var state in player.State.Materials.Take(MaterialCountLimit))
        {
            if (state.ItemId == 0 || state.Guid == 0 || state.Count == 0
                || _materials.ContainsKey(state.ItemId))
                continue;

            _materials.Add(state.ItemId, new MaterialItem {
                ItemId = state.ItemId,
                Guid = state.Guid,
                Count = state.Count
            });
            _materialState.Add(state.ItemId, state);
            AdvanceGuid(state.Guid);
        }

        foreach (var state in player.State.Weapons.Take(WeaponCountLimit))
        {
            if (state.ItemId == 0 || state.Guid == 0 || _weapons.ContainsKey(state.Guid))
                continue;

            _weapons.Add(state.Guid, new WeaponItem {
                ItemId = state.ItemId,
                Guid = state.Guid,
                GadgetId = state.GadgetId,
                Level = Math.Clamp(state.Level, 1u, 90u),
                Refinement = Math.Clamp(state.Refinement, 1u, 5u),
                PromoteLevel = state.PromoteLevel,
                AffixId = state.AffixId
            });
            _weaponState.Add(state.Guid, state);
            AdvanceGuid(state.Guid);
        }
    }

    private void AdvanceGuid(ulong guid)
    {
        if ((uint)(guid >> 32) == player.Uid)
            _nextGuid = Math.Max(_nextGuid, (uint)guid);
    }

    private async Task NotifyAdded(IEnumerable<AddedItem> added, bool showHint = true)
    {
        if (!_loggedIn)
            return;

        var chunks = added.Chunk(NotifyChunkSize).ToArray();

        foreach (var (index, chunk) in chunks.Index())
        {
            await player.Send(new StoreItemChangeNotify {
                StoreType = StoreType.STORE_TYPE_PACK,
                ItemList = [.. chunk.Select(change => change.Item.ToProtocol())]
            });

            if (showHint)
            {
                await player.Send(new ItemAddHintNotify {
                    Reason = (uint)ActionReasonType.ACTION_REASON_TYPE_GM,
                    ItemList = [
                        .. chunk.Select(change => new ItemHint {
                            IsNew = change.IsNew,
                            Guid = change.Item.Guid,
                            ItemId = change.Item.ItemId,
                            Count = change.Count
                        })
                    ]
                });
            }

            // Give the client's inventory thread and KCP ACK path time to drain large grants.
            if (chunks.Length > 1 && index < chunks.Length - 1)
                await Task.Delay(BulkNotifyInterval, player.Closing);
        }
    }

    private ulong NextGuid() => (ulong)player.Uid << 32 | ++_nextGuid;

    private readonly record struct AddedItem(InventoryItem Item, uint Count, bool IsNew);
}
