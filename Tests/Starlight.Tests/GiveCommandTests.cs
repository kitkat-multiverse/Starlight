using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
using Starlight.Common;
using Starlight.Commands;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;
using Xunit;

namespace Starlight.Tests;

public sealed class GiveCommandTests
{
    [Fact]
    public async Task Execute_OnlinePlayer_AddsMaterialAndSendsChange()
    {
        var players = new PlayerManager();
        var data = Data();
        data.MaterialData[100015] = new MaterialData { Id = 100015, StackLimit = 9999 };
        var (player, sent) = Player(uid: 1001, data);
        var inventory = player.Module<InventoryModule>();

        Assert.True(players.Add(player));
        await inventory.OnLogin();
        sent.Clear();

        var command = new GiveCommand(players, data);
        await command.ExecuteAsync(["1001", "100015", "3"], CancellationToken.None);

        Assert.True(inventory.TryGetMaterial(itemId: 100015, out var material));
        Assert.Equal(expected: 3u, material.Count);

        Assert.Equal(expected: 2, sent.Count);

        var change = Assert.IsType<StoreItemChangeNotify>(sent[0]);
        var item = Assert.Single(change.ItemList);
        Assert.Equal(expected: 100015u, item.ItemId);
        Assert.Equal(expected: 3u, item.Material?.Count);

        var hint = Assert.IsType<ItemAddHintNotify>(sent[1]);
        var added = Assert.Single(hint.ItemList);
        Assert.Equal(expected: 100015u, added.ItemId);
        Assert.Equal(expected: 3u, added.Count);
    }

    [Fact]
    public async Task Execute_MissingCount_DefaultsToOne()
    {
        var players = new PlayerManager();
        var data = Data();
        data.MaterialData[100015] = new MaterialData { Id = 100015, StackLimit = 9999 };
        var (player, _) = Player(uid: 1001, data);
        Assert.True(players.Add(player));

        var command = new GiveCommand(players, data);
        await command.ExecuteAsync(["1001", "100015"], CancellationToken.None);

        var inventory = player.Module<InventoryModule>();
        Assert.True(inventory.TryGetMaterial(itemId: 100015, out var material));
        Assert.Equal(expected: 1u, material.Count);
    }

    [Fact]
    public async Task Execute_WeaponModifiers_CreateLeveledRefinedCopies()
    {
        var players = new PlayerManager();
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        var inventory = player.Module<InventoryModule>();

        data.WeaponData[11501] = new WeaponData {
            Id = 11501,
            SkillAffix = [1234]
        };

        Assert.True(players.Add(player));
        await inventory.OnLogin();
        sent.Clear();

        var command = new GiveCommand(players, data);
        await command.ExecuteAsync(["1001", "11501", "lvl90r5x2"], CancellationToken.None);

        Assert.Equal(expected: 2, inventory.Weapons.Count);

        Assert.All(inventory.Weapons, weapon => {
            Assert.Equal(expected: 90u, weapon.Level);
            Assert.Equal(expected: 6u, weapon.PromoteLevel);
            Assert.Equal(expected: 5u, weapon.Refinement);
        });

        var change = Assert.IsType<StoreItemChangeNotify>(sent[0]);
        Assert.Equal(expected: 2, change.ItemList.Count);

        Assert.All(change.ItemList, item => {
            Assert.Equal(expected: 90u, item.Equip?.Weapon?.Level);
            Assert.Equal(expected: 4u, item.Equip?.Weapon?.AffixMap[1234]);
        });
    }

    [Fact]
    public async Task Execute_BulkSelectors_GrantParsedMaterialsAndWeapons()
    {
        var players = new PlayerManager();
        var data = Data();
        data.MaterialData[100015] = new MaterialData { Id = 100015, StackLimit = 9999 };
        data.MaterialData[100016] = new MaterialData { Id = 100016, StackLimit = 9999 };
        data.MaterialData[100017] = new MaterialData { Id = 100017, ItemType = "ITEM_VIRTUAL" };
        data.MaterialData[100018] = new MaterialData { Id = 100018, UseOnGain = true };
        data.MaterialData[100100] = new MaterialData { Id = 100100, StackLimit = 9999 };
        data.WeaponData[11501] = new WeaponData { Id = 11501, SkillAffix = [1234] };
        var (player, sent) = Player(uid: 1001, data);
        var inventory = player.Module<InventoryModule>();

        Assert.True(players.Add(player));
        await inventory.OnLogin();
        sent.Clear();
        var command = new GiveCommand(players, data);

        await command.ExecuteAsync(["1001", "materials", "x7"], CancellationToken.None);
        await command.ExecuteAsync(["1001", "weapons", "lvl40r3x2"], CancellationToken.None);

        Assert.Equal(expected: 2, inventory.Materials.Count);
        Assert.All(inventory.Materials, material => Assert.Equal(expected: 7u, material.Count));
        Assert.Equal(expected: 2, inventory.Weapons.Count);

        Assert.All(inventory.Weapons, weapon => {
            Assert.Equal(expected: 40u, weapon.Level);
            Assert.Equal(expected: 3u, weapon.Refinement);
        });
        Assert.DoesNotContain(sent, message => message is ItemAddHintNotify);
    }

    [Fact]
    public async Task Execute_AvatarModifiers_SetLevelConstellationAndStarterWeapon()
    {
        var players = new PlayerManager();
        var data = Data();
        data.WeaponData[11501] = new WeaponData { Id = 11501, SkillAffix = [1234] };

        data.AvatarData[10000007] = new AvatarData {
            Id = 10000007,
            InitialWeapon = 11501,
            SkillDepotId = 700,
            HpBase = 100,
            AttackBase = 20,
            DefenseBase = 10,
            CritChanceBase = 0.05f,
            CritDamageBase = 0.5f
        };

        data.AvatarSkillDepotData[700] = new AvatarSkillDepotData {
            Id = 700,
            Skills = [701, 702],
            EnergySkill = 703,
            Talents = [710, 711, 712, 713, 714, 715]
        };
        data.Avatars[10000007] = new AvatarConfig();

        var (player, sent) = Player(uid: 1001, data);
        Assert.True(players.Add(player));
        await player.Module<InventoryModule>().OnLogin();
        sent.Clear();

        var command = new GiveCommand(players, data);
        await command.ExecuteAsync(["1001", "10000007", "lvl80c4"], CancellationToken.None);

        var avatar = player.Module<AvatarModule>().Avatars[10000007];
        Assert.Equal(expected: 80u, avatar.Level);
        Assert.Equal(expected: 4u, avatar.Constellation);
        Assert.Equal(expected: 4, avatar.Talents.Count);

        Assert.Contains(player.Module<InventoryModule>().Weapons,
            weapon => weapon.Guid == avatar.WeaponGuid);

        Assert.Collection(
            sent,
            message => Assert.IsType<StoreItemChangeNotify>(message),
            message => Assert.IsType<AvatarEquipChangeNotify>(message),
            message => Assert.IsType<AvatarAddNotify>(message));

        var equip = Assert.IsType<AvatarEquipChangeNotify>(sent[1]);
        Assert.Equal(avatar.Guid, equip.AvatarGuid);
        Assert.Equal(avatar.WeaponGuid, equip.EquipGuid);
        Assert.Equal(expected: 6u, equip.EquipType);

        var added = Assert.IsType<AvatarAddNotify>(sent[^1]);
        Assert.Equal(expected: 80, added.Avatar?.PropMap[(uint)PlayerProperty.Level].Val);
        Assert.Equal(expected: 5, added.Avatar?.PropMap[(uint)PlayerProperty.BreakLevel].Val);
        Assert.Equal(expected: 4, added.Avatar?.TalentIdList.Count);

        var persisted = NetPlayerState.Parser.ParseFrom(player.State.ToByteArray());
        var (reconnected, _) = Player(uid: 1001, data);
        reconnected.State = persisted;

        var (restored, wasAdded) = await reconnected.Module<AvatarModule>()
            .AddAvatar(10000007);

        Assert.False(wasAdded);
        Assert.Equal(expected: 80u, restored.Level);
        Assert.Equal(expected: 4u, restored.Constellation);
        await reconnected.Module<InventoryModule>().OnLogin();

        Assert.True(reconnected.Module<InventoryModule>()
            .TryGetWeapon(restored.WeaponGuid, out _));
    }

    private static (StarlightPlayer Player, List<IMessage> Sent) Player(uint uid, GameData data)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var registry = new ModuleRegistry();
        var guidManager = new GuidManager(serverId: 1);
        registry.AddModule<InventoryModule>((_, player) => new InventoryModule(player, guidManager, data));

        if (data.AvatarData.Count > 0)
            registry.AddModule<AvatarModule>((_, player) => new AvatarModule(player, data, guidManager));

        registry.Build();

        var (client, server) = DirectTunnel.CreatePair();
        var sent = new List<IMessage>();

        _ = client.Subscribe(GameSubjects.OutboundPacket, message => {
            sent.Add(message.Decode<IMessage>());
            return Task.CompletedTask;
        });

        return (new StarlightPlayer(services, registry, server) { Uid = uid }, sent);
    }

    private static GameData Data()
        => new(new ConfigurationBuilder().Build());
}
