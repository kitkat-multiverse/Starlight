using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Chat;
using Starlight.Common;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using Starlight.Social;
using Xunit;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Tests;

public sealed class SocialProfileTests
{
    [Fact]
    public async Task AvatarProfilePictures_AreDerivedFromOwnedAvatars()
    {
        var data = Data();
        var (player, _) = Player(data);
        var cosmetics = new ProfileCosmeticService(data);

        player.State.BornState = NetPlayerState.Types.PlayerBornState.Pending;
        cosmetics.EnsureDefaults(player);
        Assert.Equal(expected: 0u, player.Profile.PictureId);

        await player.Module<AvatarModule>().InitializeTraveler(10000005);
        cosmetics.EnsureDefaults(player);

        Assert.Equal(expected: 1u, player.Profile.PictureId);
        Assert.True(cosmetics.IsProfilePictureUnlocked(player, pictureId: 1));
        Assert.False(cosmetics.IsProfilePictureUnlocked(player, pictureId: 2));

        await player.Module<AvatarModule>().AddAvatar(10000007);

        Assert.True(cosmetics.IsProfilePictureUnlocked(player, pictureId: 2));
        Assert.True(cosmetics.TrySetProfilePicture(player, pictureId: 2));
        Assert.Equal(expected: 2u, player.Profile.PictureId);
    }

    [Fact]
    public void SpecialProfilePictures_RequirePersistedUnlock()
    {
        var data = Data();
        var (player, _) = Player(data);
        var cosmetics = new ProfileCosmeticService(data);

        Assert.False(cosmetics.IsProfilePictureUnlocked(player, pictureId: 200010));
        Assert.True(cosmetics.UnlockProfilePicture(player, pictureId: 200010));
        Assert.True(cosmetics.IsProfilePictureUnlocked(player, pictureId: 200010));
        Assert.Contains(expected: 200010u, cosmetics.GetSpecialProfilePictures(player));
    }

    [Fact]
    public void NameCards_DefaultAndSelectionArePersistedInPlayerState()
    {
        var data = Data();
        var (player, _) = Player(data);
        var cosmetics = new ProfileCosmeticService(data);

        cosmetics.EnsureDefaults(player);

        Assert.Equal(ProfileCosmeticService.DefaultNameCardId, player.Profile.NameCardId);
        Assert.Contains(ProfileCosmeticService.DefaultNameCardId, player.State.UnlockedNameCardIds);
        Assert.False(cosmetics.TrySetNameCard(player, nameCardId: 210099));

        Assert.True(cosmetics.UnlockNameCard(player, nameCardId: 210099));
        Assert.True(cosmetics.TrySetNameCard(player, nameCardId: 210099));
        Assert.Equal(expected: 210099u, player.Profile.NameCardId);
    }

    [Fact]
    public async Task SocialModule_ReturnsRealPlayerProfileAndValidatesSelections()
    {
        var data = Data();
        var players = new PlayerManager();
        var (player, _) = Player(data);
        player.Uid = 1001;
        player.Profile.Nickname = "Traveler";
        player.State.BornState = NetPlayerState.Types.PlayerBornState.Pending;
        await player.Module<AvatarModule>().InitializeTraveler(10000005);
        Assert.True(players.Add(player));

        var cosmetics = new ProfileCosmeticService(data);
        var serverFriend = new ServerFriendProfile(new ChatConfig());
        var module = new SocialModule(cosmetics, players, serverFriend, player);
        await module.OnLogin();

        var detail = module.OnGetPlayerSocialDetail(new GetPlayerSocialDetailReq { Uid = player.Uid });
        Assert.Equal((int)Retcode.RETCODE_SUCC, detail.Retcode);
        Assert.NotNull(detail.DetailData);
        Assert.Equal("Traveler", detail.DetailData.Nickname);
        Assert.NotNull(detail.DetailData.ProfilePicture);
        Assert.Equal(expected: 1u, detail.DetailData.ProfilePicture.PictureId);
        Assert.Equal(ProfileCosmeticService.DefaultNameCardId, detail.DetailData.NameCardId);

        var rejected = module.OnSetPlayerHeadImage(new SetPlayerHeadImageReq { AvatarId = 2 });
        Assert.Equal((int)Retcode.RETCODE_PROFILE_PICTURE_NOT_UNLOCKED, rejected.Retcode);
    }

    private static (StarlightPlayer Player, List<IMessage> Sent) Player(GameData data)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var registry = new ModuleRegistry();
        var guidManager = new GuidManager(serverId: 1);

        registry.AddModule<InventoryModule>((_, player) => new InventoryModule(player, guidManager, data));
        registry.AddModule<AvatarModule>((_, player) => new AvatarModule(player, data, guidManager));
        registry.AddModule<TeamModule>((_, player) => new TeamModule(player));
        registry.Build();

        var (client, server) = DirectTunnel.CreatePair();
        var sent = new List<IMessage>();

        _ = client.Subscribe(GameSubjects.OutboundPacket, message => {
            sent.Add(message.Decode<IMessage>());
            return Task.CompletedTask;
        });

        return (new StarlightPlayer(services, registry, server), sent);
    }

    private static GameData Data()
    {
        var data = new GameData(new ConfigurationBuilder().Build());

        data.WeaponData[11501] = new WeaponData { Id = 11501, GadgetId = 500001 };
        data.WeaponData[11502] = new WeaponData { Id = 11502, GadgetId = 500002 };
        data.MaterialData[210001] = new MaterialData { Id = 210001, MaterialType = "MATERIAL_NAMECARD" };
        data.MaterialData[210099] = new MaterialData { Id = 210099, MaterialType = "MATERIAL_NAMECARD" };
        AddAvatar(data, avatarId: 10000005, depotId: 500, weaponId: 11501);
        AddAvatar(data, avatarId: 10000007, depotId: 700, weaponId: 11502);

        AddPicture(data, id: 1, unlockParam: 10000005, "PROFILE_PICTURE_UNLOCK_BY_AVATAR", priority: 1);
        AddPicture(data, id: 2, unlockParam: 10000007, "PROFILE_PICTURE_UNLOCK_BY_AVATAR", priority: 2);
        AddPicture(data, id: 200010, unlockParam: 320002, "PROFILE_PICTURE_UNLOCK_BY_ITEM", priority: 100);
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
        data.AvatarSkillDepotData[depotId] = new AvatarSkillDepotData { Id = depotId };
        data.Avatars[avatarId] = new AvatarConfig();
    }

    private static void AddPicture(GameData data, uint id, uint unlockParam, string unlockType, uint priority)
    {
        var picture = new ProfilePictureData {
            Id = id,
            UnlockParam = unlockParam,
            UnlockTypeName = unlockType,
            Priority = priority
        };
        picture.OnLoad();
        data.ProfilePictureData[id] = picture;
    }
}
