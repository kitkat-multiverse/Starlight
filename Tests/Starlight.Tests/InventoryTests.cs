using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;
using Xunit;

namespace Starlight.Tests;

public sealed class InventoryTests
{
    [Fact]
    public async Task OnLogin_SendsTheSeededInventory()
    {
        var (player, sent) = Player(uid: 1001);
        var inventory = new InventoryModule(player);

        var material = await inventory.AddMaterial(itemId: 100015, count: 3);
        Assert.Empty(sent);

        await inventory.OnLogin();

        var limits = Assert.IsType<StoreWeightLimitNotify>(sent[0]);
        Assert.Equal(StoreType.STORE_TYPE_PACK, limits.StoreType);

        var store = Assert.IsType<PlayerStoreNotify>(sent[1]);
        var item = Assert.Single(store.ItemList);
        Assert.Equal(material.Guid, item.Guid);
        Assert.Equal(expected: 100015u, item.ItemId);
        Assert.Equal(expected: 3u, item.Material?.Count);
    }

    [Fact]
    public async Task AddMaterial_AfterLogin_SendsAChange()
    {
        var (player, sent) = Player(uid: 1001);
        var inventory = new InventoryModule(player);
        await inventory.OnLogin();
        sent.Clear();

        await inventory.AddMaterial(itemId: 100015, count: 2);

        Assert.Equal(expected: 2, sent.Count);

        var change = Assert.IsType<StoreItemChangeNotify>(sent[0]);
        var item = Assert.Single(change.ItemList);
        Assert.Equal(expected: 2u, item.Material?.Count);

        var hint = Assert.IsType<ItemAddHintNotify>(sent[1]);
        Assert.Equal((uint)ActionReasonType.ACTION_REASON_TYPE_GM, hint.Reason);
        var added = Assert.Single(hint.ItemList);
        Assert.True(added.IsNew);
        Assert.Equal(item.Guid, added.Guid);
        Assert.Equal(expected: 100015u, added.ItemId);
        Assert.Equal(expected: 2u, added.Count);
    }

    [Fact]
    public async Task RemoveMaterial_LastItem_SendsADeletion()
    {
        var (player, sent) = Player(uid: 1001);
        var inventory = new InventoryModule(player);
        var material = await inventory.AddMaterial(itemId: 100015, count: 2);
        await inventory.OnLogin();
        sent.Clear();

        Assert.True(await inventory.RemoveMaterial(itemId: 100015, count: 2));

        var deletion = Assert.IsType<StoreItemDelNotify>(Assert.Single(sent));
        Assert.Equal(material.Guid, Assert.Single(deletion.GuidList));
        Assert.Empty(inventory.Materials);
    }

    [Fact]
    public async Task AddMaterials_StopsAtAdvertisedMaterialCapacity()
    {
        var (player, sent) = Player(uid: 1001);
        var inventory = new InventoryModule(player);
        await inventory.OnLogin();
        sent.Clear();

        var added = await inventory.AddMaterials(
            Enumerable.Range(start: 100000, count: 2001).Select(id => (uint)id),
            count: 1,
            showHint: false);

        Assert.Equal(expected: 2000, added.Count);
        Assert.Equal(expected: 2000, inventory.Materials.Count);
        Assert.Equal(expected: 20, sent.Count);
        Assert.All(sent, message => Assert.IsType<StoreItemChangeNotify>(message));
    }

    [Fact]
    public async Task OnLogin_RestoresInventoryFromPersistedPlayerState()
    {
        var (firstPlayer, _) = Player(uid: 1001);
        var firstInventory = new InventoryModule(firstPlayer);

        await firstInventory.AddMaterial(itemId: 100015, count: 7);

        var weapon = await firstInventory.AddWeapons(
            [new WeaponData { Id = 11501, GadgetId = 500001, SkillAffix = [1234] }],
            level: 90,
            refinement: 5);

        var persisted = NetPlayerState.Parser.ParseFrom(firstPlayer.State.ToByteArray());
        var (secondPlayer, sent) = Player(uid: 1001);
        secondPlayer.State = persisted;
        var secondInventory = new InventoryModule(secondPlayer);

        await secondInventory.OnLogin();

        Assert.True(secondInventory.TryGetMaterial(itemId: 100015, out var material));
        Assert.Equal(expected: 7u, material.Count);

        Assert.True(secondInventory.TryGetWeapon(weapon[0].Guid, out var restoredWeapon));
        Assert.Equal(expected: 90u, restoredWeapon.Level);
        Assert.Equal(expected: 5u, restoredWeapon.Refinement);
        Assert.Equal(expected: 500001u, restoredWeapon.GadgetId);

        var store = Assert.IsType<PlayerStoreNotify>(sent[1]);
        Assert.Equal(expected: 2, store.ItemList.Count);
    }

    private static (StarlightPlayer Player, List<IMessage> Sent) Player(uint uid)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var registry = new ModuleRegistry().Build();
        var (client, server) = DirectTunnel.CreatePair();
        var sent = new List<IMessage>();

        _ = client.Subscribe(GameSubjects.OutboundPacket, message => {
            sent.Add(message.Decode<IMessage>());
            return Task.CompletedTask;
        });

        return (new StarlightPlayer(services, registry, server) { Uid = uid }, sent);
    }
}
