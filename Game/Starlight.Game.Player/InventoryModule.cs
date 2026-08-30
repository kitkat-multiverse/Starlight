using System.Diagnostics.CodeAnalysis;
using Starlight.Common;
using Starlight.Game.Modules;
using Starlight.Game.Resources;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;
using Starlight.Rpc.Proto;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

/// <summary>Owns one player's material stacks and equipment instances.</summary>
public sealed class InventoryModule(IPlayer player, GuidManager guidManager, GameData data) : IModule
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
    private bool _loaded;
    private bool _loggedIn;

    public IReadOnlyCollection<MaterialItem> Materials
    {
        get
        {
            lock (player.StateLock)
            {
                LoadState();
                return [.. _materials.Values];
            }
        }
    }

    public IReadOnlyCollection<WeaponItem> Weapons
    {
        get
        {
            lock (player.StateLock)
            {
                LoadState();
                return [.. _weapons.Values];
            }
        }
    }

    public bool TryGetMaterial(uint itemId, [NotNullWhen(true)] out MaterialItem? item)
    {
        lock (player.StateLock)
        {
            LoadState();
            return _materials.TryGetValue(itemId, out item);
        }
    }

    public bool TryGetWeapon(ulong guid, [NotNullWhen(true)] out WeaponItem? item)
    {
        lock (player.StateLock)
        {
            LoadState();
            return _weapons.TryGetValue(guid, out item);
        }
    }

    public async Task<MaterialItem> AddMaterial(uint itemId, uint count)
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);
        AddedItem change;

        lock (player.StateLock)
        {
            LoadState();

            if (!_materials.ContainsKey(itemId) && _materials.Count >= MaterialCountLimit)
                throw new InvalidOperationException("The material inventory is full.");

            change = AddMaterialCore(itemId, count);
        }

        if (change.Count > 0)
            await NotifyAdded([change]);

        return (MaterialItem)change.Item;
    }

    public async Task<IReadOnlyList<MaterialItem>> AddMaterials(
        IEnumerable<uint> itemIds,
        uint count,
        bool showHint = true
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);

        var changes = new List<AddedItem>();

        lock (player.StateLock)
        {
            LoadState();

            foreach (var itemId in itemIds.Distinct())
            {
                // Existing stacks can still be updated when every material slot is occupied.
                if (!_materials.ContainsKey(itemId) && _materials.Count >= MaterialCountLimit)
                    continue;

                var change = AddMaterialCore(itemId, count);

                if (change.Count > 0)
                    changes.Add(change);
            }
        }

        await NotifyAdded(changes, showHint);
        return [.. changes.Select(change => (MaterialItem)change.Item)];
    }

    public async Task<IReadOnlyList<WeaponItem>> AddWeapons(
        IEnumerable<WeaponData> weapons,
        uint amount = 1,
        uint level = 1,
        uint refinement = 1,
        bool showHint = true
    )
    {
        ArgumentOutOfRangeException.ThrowIfZero(amount);
        level = Math.Clamp(level, min: 1u, max: 90u);
        refinement = Math.Clamp(refinement, min: 1u, max: 5u);

        var changes = new List<AddedItem>();

        lock (player.StateLock)
        {
            LoadState();

            foreach (var weaponData in weapons)
            {
                for (var copy = 0u; copy < amount; copy++)
                {
                    if (_weapons.Count >= WeaponCountLimit)
                        break;

                    changes.Add(AddWeaponCore(weaponData, NextGuid(), level, refinement));
                }

                if (_weapons.Count >= WeaponCountLimit)
                    break;
            }
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
        bool showHint = true
    )
    {
        AddedItem? change = null;
        WeaponItem item;

        lock (player.StateLock)
        {
            LoadState();

            if (_weapons.TryGetValue(guid, out var existing))
                return existing;

            if (_weapons.Count >= WeaponCountLimit)
                throw new InvalidOperationException("The weapon inventory is full.");

            change = AddWeaponCore(
                data,
                guid,
                Math.Clamp(level, min: 1u, max: 90u),
                Math.Clamp(refinement, min: 1u, max: 5u));
            item = (WeaponItem)change.Value.Item;
        }

        await NotifyAdded([change.Value], showHint);
        return item;
    }

    public async Task<bool> RemoveMaterial(uint itemId, uint count)
    {
        IMessage? notification = null;

        lock (player.StateLock)
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
                    notification = new StoreItemDelNotify {
                        StoreType = StoreType.STORE_TYPE_PACK,
                        GuidList = { item.Guid }
                    };
                }
            } else
            {
                _materialState[itemId].Count = item.Count;

                if (_loggedIn)
                {
                    notification = new StoreItemChangeNotify {
                        StoreType = StoreType.STORE_TYPE_PACK,
                        ItemList = { item.ToProtocol() }
                    };
                }
            }
        }

        if (notification is not null)
            await player.Send(notification);

        return true;
    }

    [Lifecycle(LifecycleEvent.PlayerLogin, LifecycleOrder.HighPriority)]
    public async Task OnLogin()
    {
        List<Item> items;

        lock (player.StateLock)
        {
            LoadState();

            items = [
                .. _materials.Values.Select(item => item.ToProtocol()),
                .. _weapons.Values.Select(item => item.ToProtocol())
            ];
        }

        await player.Send(new StoreWeightLimitNotify {
            StoreType = StoreType.STORE_TYPE_PACK,
            MaterialCountLimit = MaterialCountLimit,
            WeaponCountLimit = WeaponCountLimit,
            ReliquaryCountLimit = 2000,
            WeightLimit = PackWeightLimit
        });

        await player.Send(new PlayerStoreNotify {
            StoreType = StoreType.STORE_TYPE_PACK,
            WeightLimit = PackWeightLimit,
            ItemList = items
        });

        lock (player.StateLock)
        {
            _loggedIn = true;
        }
    }

    private AddedItem AddMaterialCore(uint itemId, uint requestedCount)
    {
        if (!data.MaterialData.TryGetValue(itemId, out var materialData))
            throw new ArgumentException($"Material {itemId} does not exist.", nameof(itemId));

        var stackLimit = Math.Max(materialData.StackLimit, val2: 1u);

        if (!_materials.TryGetValue(itemId, out var item))
        {
            var addedCount = Math.Min(requestedCount, stackLimit);

            item = new MaterialItem {
                ItemId = itemId,
                Guid = NextGuid(),
                Count = addedCount
            };

            _materials.Add(itemId, item);

            var state = new NetMaterial {
                ItemId = item.ItemId,
                Guid = item.Guid,
                Count = item.Count
            };

            _materialState.Add(itemId, state);
            player.State.Materials.Add(state);

            return new AddedItem(item, item.ToProtocol(), addedCount, IsNew: true);
        }

        if (item.Count >= stackLimit)
            return new AddedItem(item, item.ToProtocol(), Count: 0, IsNew: false);

        var availableSpace = stackLimit - item.Count;
        var added = Math.Min(requestedCount, availableSpace);

        item.Count += added;
        _materialState[itemId].Count = item.Count;

        return new AddedItem(item, item.ToProtocol(), added, IsNew: false);
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
        return new AddedItem(item, item.ToProtocol(), Count: 1, IsNew: true);
    }

    /// <summary>Hydrates the module once from the state DbGate attached to the player.</summary>
    internal void LoadState()
    {
        lock (player.StateLock)
        {
            if (_loaded)
                return;

            _loaded = true;

            foreach (var state in player.State.Materials.Take(MaterialCountLimit))
            {
                if (state.ItemId == 0 || state.Guid == 0 || state.Count == 0
                    || _materials.ContainsKey(state.ItemId))
                    continue;

                if (!data.MaterialData.TryGetValue(state.ItemId, out var materialData))
                    continue;

                var stackLimit = Math.Max(materialData.StackLimit, val2: 1u);
                state.Count = Math.Min(state.Count, stackLimit);

                _materials.Add(state.ItemId, new MaterialItem {
                    ItemId = state.ItemId,
                    Guid = state.Guid,
                    Count = state.Count
                });
                _materialState.Add(state.ItemId, state);
            }

            foreach (var state in player.State.Weapons.Take(WeaponCountLimit))
            {
                if (state.ItemId == 0 || state.Guid == 0 || _weapons.ContainsKey(state.Guid))
                    continue;

                _weapons.Add(state.Guid, new WeaponItem {
                    ItemId = state.ItemId,
                    Guid = state.Guid,
                    GadgetId = state.GadgetId,
                    Level = Math.Clamp(state.Level, min: 1u, max: 90u),
                    Refinement = Math.Clamp(state.Refinement, min: 1u, max: 5u),
                    PromoteLevel = state.PromoteLevel,
                    AffixId = state.AffixId
                });
                _weaponState.Add(state.Guid, state);
            }
        }
    }

    private async Task NotifyAdded(IEnumerable<AddedItem> added, bool showHint = true)
    {
        lock (player.StateLock)
        {
            if (!_loggedIn)
                return;
        }

        var chunks = added.Chunk(NotifyChunkSize).ToArray();

        foreach (var (index, chunk) in chunks.Index())
        {
            await player.Send(new StoreItemChangeNotify {
                StoreType = StoreType.STORE_TYPE_PACK,
                ItemList = [.. chunk.Select(change => change.Protocol)]
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

    private ulong NextGuid() => guidManager.GenGuid(GuidManager.GuidType.Item);

    private readonly record struct AddedItem(
        InventoryItem Item,
        Item Protocol,
        uint Count,
        bool IsNew
    );
}
