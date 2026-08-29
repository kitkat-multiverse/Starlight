using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
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

public sealed class AvatarEquipTests
{
    [Fact]
    public async Task WearEquip_UpdatesAvatarNotifiesAndPersists()
    {
        var data = Data();
        var (player, sent) = Player(1001, data);
        var avatars = player.Module<AvatarModule>();
        var inventory = player.Module<InventoryModule>();

        await avatars.OnLogin();
        await inventory.OnLogin();
        var weapon = Assert.Single(await inventory.AddWeapons([data.WeaponData[11502]]));
        sent.Clear();

        var avatar = avatars.Avatars[10000005];
        var response = await avatars.OnWearEquip(new WearEquipReq {
            AvatarGuid = avatar.Guid,
            EquipGuid = weapon.Guid
        });

        Assert.Equal(0, response.Retcode);
        Assert.Equal(avatar.Guid, response.AvatarGuid);
        Assert.Equal(weapon.Guid, response.EquipGuid);
        Assert.Equal(weapon.Guid, avatar.WeaponGuid);

        var notify = Assert.IsType<AvatarEquipChangeNotify>(Assert.Single(sent));
        Assert.Equal(avatar.Guid, notify.AvatarGuid);
        Assert.Equal(weapon.Guid, notify.EquipGuid);
        Assert.Equal(weapon.ItemId, notify.ItemId);
        Assert.Equal(6u, notify.EquipType);

        var persistedAvatar = Assert.Single(player.State.Avatars);
        Assert.Equal(weapon.Guid, persistedAvatar.WeaponGuid);

        var persisted = NetPlayerState.Parser.ParseFrom(player.State.ToByteArray());
        var (reconnected, _) = Player(1001, data);
        reconnected.State = persisted;
        await reconnected.Module<AvatarModule>().OnLogin();

        var restored = reconnected.Module<AvatarModule>().Avatars[10000005];
        Assert.Equal(weapon.Guid, restored.WeaponGuid);
        Assert.Equal(weapon.ItemId, restored.WeaponItemId);
    }

    [Fact]
    public async Task WearEquip_WeaponOwnedByAnotherAvatar_SwapsWeapons()
    {
        var data = Data();
        var (player, sent) = Player(1001, data);
        var avatars = player.Module<AvatarModule>();
        var inventory = player.Module<InventoryModule>();

        await avatars.OnLogin();
        await inventory.OnLogin();
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        sent.Clear();

        var first = avatars.Avatars[10000005];
        var firstWeapon = first.WeaponGuid;
        var secondWeapon = second.WeaponGuid;

        var response = await avatars.OnWearEquip(new WearEquipReq {
            AvatarGuid = first.Guid,
            EquipGuid = secondWeapon
        });

        Assert.Equal(0, response.Retcode);
        Assert.Equal(secondWeapon, first.WeaponGuid);
        Assert.Equal(firstWeapon, second.WeaponGuid);

        Assert.Collection(
            sent,
            message => {
                var unequip = Assert.IsType<AvatarEquipChangeNotify>(message);
                Assert.Equal(second.Guid, unequip.AvatarGuid);
                Assert.Equal(0ul, unequip.EquipGuid);
            },
            message => {
                var equip = Assert.IsType<AvatarEquipChangeNotify>(message);
                Assert.Equal(second.Guid, equip.AvatarGuid);
                Assert.Equal(firstWeapon, equip.EquipGuid);
            },
            message => {
                var equip = Assert.IsType<AvatarEquipChangeNotify>(message);
                Assert.Equal(first.Guid, equip.AvatarGuid);
                Assert.Equal(secondWeapon, equip.EquipGuid);
            });

        Assert.Equal(secondWeapon,
            player.State.Avatars.Single(state => state.AvatarId == first.AvatarId).WeaponGuid);
        Assert.Equal(firstWeapon,
            player.State.Avatars.Single(state => state.AvatarId == second.AvatarId).WeaponGuid);
    }

    private static (StarlightPlayer Player, List<IMessage> Sent) Player(uint uid, GameData data)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var registry = new ModuleRegistry();
        registry.AddModule<InventoryModule>(static (_, player) => new InventoryModule(player));
        registry.AddModule<AvatarModule>((_, player) => new AvatarModule(player, data));
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
    {
        var data = new GameData(new ConfigurationBuilder().Build());
        data.WeaponData[11501] = new WeaponData {
            Id = 11501,
            GadgetId = 500001,
            SkillAffix = [111]
        };
        data.WeaponData[11502] = new WeaponData {
            Id = 11502,
            GadgetId = 500002,
            SkillAffix = [112]
        };

        AddAvatar(data, 10000005, 500, 11501);
        AddAvatar(data, 10000007, 700, 11502);
        return data;
    }

    private static void AddAvatar(GameData data, uint avatarId, uint depotId, uint weaponId)
    {
        data.AvatarData[avatarId] = new AvatarData {
            Id = avatarId,
            InitialWeapon = weaponId,
            SkillDepotId = depotId,
            HpBase = 100,
            AttackBase = 20,
            DefenseBase = 10,
            CritChanceBase = 0.05f,
            CritDamageBase = 0.5f
        };
        data.AvatarSkillDepotData[depotId] = new AvatarSkillDepotData {
            Id = depotId,
            Skills = [depotId + 1],
            EnergySkill = depotId + 2
        };
        data.Avatars[avatarId] = new AvatarConfig();
    }
}
