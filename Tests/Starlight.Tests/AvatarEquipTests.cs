using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
using Starlight.Common;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;
using Starlight.Game.World;
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
    public async Task SetPlayerBornData_ConcurrentRequests_OnlyInitializeOneTraveler()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        player.State.BornState = NetPlayerState.Types.PlayerBornState.Pending;
        var born = player.Module<BornModule>();

        await Task.WhenAll(
            born.OnSetPlayerBornData(new SetPlayerBornDataReq {
                AvatarId = 10000005,
                NickName = "Aether"
            }),
            born.OnSetPlayerBornData(new SetPlayerBornDataReq {
                AvatarId = 10000007,
                NickName = "Lumine"
            }));

        var responses = sent.OfType<SetPlayerBornDataRsp>().ToArray();
        Assert.Equal(expected: 2, responses.Length);
        Assert.Single(responses, response => response.Retcode == 0);

        Assert.Single(
            responses,
            response => response.Retcode == (int)Retcode.RETCODE_REPEAT_SET_PLAYER_BORN_DATA);

        var traveler = Assert.Single(player.State.Avatars);
        Assert.Equal(traveler.AvatarId, player.State.BornAvatarId);

        Assert.Equal(
            traveler.AvatarId == 10000005 ? "Aether" : "Lumine",
            player.Profile.Nickname);
    }

    [Fact]
    public async Task SetPlayerBornData_PersistsNicknameAndSelectedTraveler()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        player.State.BornState = NetPlayerState.Types.PlayerBornState.Pending;

        await player.Module<BornModule>().OnSetPlayerBornData(
            new SetPlayerBornDataReq {
                AvatarId = 10000007,
                NickName = "Lumine"
            });

        var response = Assert.Single(sent.OfType<SetPlayerBornDataRsp>());
        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal("Lumine", player.Profile.Nickname);
        Assert.Equal(expected: 10000007u, player.State.BornAvatarId);

        Assert.Equal(
            NetPlayerState.Types.PlayerBornState.Complete,
            player.State.BornState);

        var traveler = Assert.Single(player.Module<TeamModule>().Current.Avatars);
        Assert.Equal(expected: 10000007u, traveler.AvatarId);
        Assert.Single(player.State.Avatars, state => state.AvatarId == 10000007);

        Assert.Equal(expected: 4, player.State.AvatarTeams.Count);
        var team = player.State.AvatarTeams.Single(state => state.TeamId == 1);
        Assert.Equal(expected: 1u, team.TeamId);
        Assert.Equal(traveler.Guid, Assert.Single(team.AvatarGuids));
        Assert.Equal(traveler.Guid, team.CurrentAvatarGuid);
        Assert.Equal(team.TeamId, player.State.CurrentAvatarTeamId);

        var teamNotify = Assert.Single(sent.OfType<AvatarTeamUpdateNotify>());
        Assert.Equal(expected: 4, teamNotify.AvatarTeamMap.Count);

        var nicknameNotify = Assert.Single(sent.OfType<PlayerNicknameNotify>());
        Assert.Equal("Lumine", nicknameNotify.Nickname);

        var snapshot = new SavePlayerReq {
            Uid = player.Uid,
            State = player.State,
            Profile = player.Profile
        };
        var restored = SavePlayerReq.Parser.ParseFrom(snapshot.ToByteArray());

        Assert.Equal("Lumine", restored.Profile?.Nickname);
        Assert.Equal(expected: 10000007u, restored.State?.BornAvatarId);

        var (reconnected, _) = Player(uid: 1001, data);
        reconnected.State = restored.State ?? new NetPlayerState();
        reconnected.Profile = restored.Profile ?? new NetPlayerProfile();
        var login = await reconnected.Module<AvatarModule>().OnLogin();

        Assert.Equal("Lumine", reconnected.Profile.Nickname);

        Assert.Equal(
            expected: 10000007u,
            Assert.Single(reconnected.Module<TeamModule>().Current.Avatars).AvatarId);

        var restoredTeam = reconnected.Module<TeamModule>().Current;
        Assert.Equal(expected: 1u, restoredTeam.Id);
        Assert.Equal(expected: 10000007u, Assert.Single(restoredTeam.Avatars).AvatarId);
        Assert.Equal(restoredTeam.Avatars[0].Guid, restoredTeam.CurrentAvatarGuid);
        Assert.Equal(restoredTeam.Id, login.CurAvatarTeamId);
        Assert.Equal(restoredTeam.CurrentAvatarGuid, login.ChooseAvatarGuid);
        Assert.Equal(restoredTeam.Name, login.AvatarTeamMap[restoredTeam.Id].TeamName);

        Assert.Equal(
            restoredTeam.Avatars.Select(avatar => avatar.Guid),
            login.AvatarTeamMap[restoredTeam.Id].AvatarGuidList);
    }

    [Fact]
    public async Task WearEquip_UpdatesAvatarNotifiesAndPersists()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
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

        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal(avatar.Guid, response.AvatarGuid);
        Assert.Equal(weapon.Guid, response.EquipGuid);
        Assert.Equal(weapon.Guid, avatar.WeaponGuid);

        var notify = Assert.IsType<AvatarEquipChangeNotify>(Assert.Single(sent));
        Assert.Equal(avatar.Guid, notify.AvatarGuid);
        Assert.Equal(weapon.Guid, notify.EquipGuid);
        Assert.Equal(weapon.ItemId, notify.ItemId);
        Assert.Equal(expected: 6u, notify.EquipType);

        var persistedAvatar = Assert.Single(player.State.Avatars);
        Assert.Equal(weapon.Guid, persistedAvatar.WeaponGuid);

        var persisted = NetPlayerState.Parser.ParseFrom(player.State.ToByteArray());
        var (reconnected, _) = Player(uid: 1001, data);
        reconnected.State = persisted;
        await reconnected.Module<AvatarModule>().OnLogin();

        var restored = reconnected.Module<AvatarModule>().Avatars[10000005];
        Assert.Equal(weapon.Guid, restored.WeaponGuid);
        Assert.Equal(weapon.ItemId, restored.WeaponItemId);
    }

    [Fact]
    public async Task SetUpAvatarTeam_UpdatesNotifiesAndPersists()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var first = avatars.Avatars[10000005];
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);
        sent.Clear();

        var response = await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = 1,
            CurAvatarGuid = second.Guid,
            AvatarTeamGuidList = { second.Guid, first.Guid }
        });

        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal(expected: 1u, response.TeamId);
        Assert.Equal(second.Guid, response.CurAvatarGuid);
        Assert.Equal([second.Guid, first.Guid], response.AvatarTeamGuidList);

        var current = teams.Current;
        Assert.Equal([second.Guid, first.Guid], current.Avatars.Select(avatar => avatar.Guid));
        Assert.Equal(second.Guid, current.CurrentAvatarGuid);

        var state = player.State.AvatarTeams.Single(state => state.TeamId == 1);
        Assert.Equal([second.Guid, first.Guid], state.AvatarGuids);
        Assert.Equal(second.Guid, state.CurrentAvatarGuid);

        var notify = Assert.IsType<AvatarTeamUpdateNotify>(Assert.Single(sent));
        Assert.Equal([second.Guid, first.Guid], notify.AvatarTeamMap[1].AvatarGuidList);

        var persisted = NetPlayerState.Parser.ParseFrom(player.State.ToByteArray());
        var (reconnected, _) = Player(uid: 1001, data);
        reconnected.State = persisted;
        await reconnected.Module<AvatarModule>().OnLogin();

        var restored = reconnected.Module<TeamModule>().Current;
        Assert.Equal([second.Guid, first.Guid], restored.Avatars.Select(avatar => avatar.Guid));
        Assert.Equal(second.Guid, restored.CurrentAvatarGuid);
    }

    [Fact]
    public async Task SetUpAvatarTeam_InvalidRoster_DoesNotMutateState()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var original = teams.Current;
        sent.Clear();

        var duplicate = await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = original.Id,
            CurAvatarGuid = original.CurrentAvatarGuid,
            AvatarTeamGuidList = { original.CurrentAvatarGuid, original.CurrentAvatarGuid }
        });

        Assert.Equal((int)Retcode.RETCODE_DUPLICATE_AVATAR, duplicate.Retcode);
        Assert.Empty(sent);

        var current = teams.Current;

        Assert.Equal(original.Avatars.Select(avatar => avatar.Guid),
            current.Avatars.Select(avatar => avatar.Guid));
        Assert.Equal(original.CurrentAvatarGuid, current.CurrentAvatarGuid);
    }

    [Fact]
    public async Task SetUpAvatarTeam_NotifyContainsEverySavedTeam()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var first = avatars.Avatars[10000005];
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);

        var create = await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = 2,
            CurAvatarGuid = second.Guid,
            AvatarTeamGuidList = { second.Guid }
        });

        Assert.Equal(expected: 0, create.Retcode);
        Assert.Equal(expected: 4, teams.Teams.Count);

        sent.Clear();

        await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = 1,
            CurAvatarGuid = first.Guid,
            AvatarTeamGuidList = { first.Guid, second.Guid }
        });

        var notify = Assert.IsType<AvatarTeamUpdateNotify>(Assert.Single(sent));
        Assert.Equal(expected: 4, notify.AvatarTeamMap.Count);
        Assert.Equal([first.Guid, second.Guid], notify.AvatarTeamMap[1].AvatarGuidList);
        Assert.Equal([second.Guid], notify.AvatarTeamMap[2].AvatarGuidList);
    }

    [Fact]
    public async Task SetUpAvatarTeam_EmptySlot_SelectsFirstAddedAvatar()
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);

        var response = await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = 2,
            CurAvatarGuid = 0,
            AvatarTeamGuidList = { second.Guid }
        });

        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal(second.Guid, response.CurAvatarGuid);

        var team = teams.Teams[2];
        Assert.Equal(second.Guid, team.CurrentAvatarGuid);

        Assert.Equal(
            second.Guid,
            player.State.AvatarTeams.Single(state => state.TeamId == 2).CurrentAvatarGuid);
    }

    [Fact]
    public async Task SetUpAvatarTeam_ConcurrentRequests_KeepRuntimeAndStateConsistent()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var first = avatars.Avatars[10000005];
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);
        sent.Clear();

        var responses = await Task.WhenAll(
            Enumerable.Range(start: 0, count: 100).Select(index => {
                var members = index % 2 == 0 ? new[] { first.Guid, second.Guid } : [second.Guid, first.Guid];

                return teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
                    TeamId = 1,
                    CurAvatarGuid = members[0],
                    AvatarTeamGuidList = [.. members]
                });
            }));

        Assert.All(responses, response => Assert.Equal(expected: 0, response.Retcode));

        var current = teams.Current;
        var state = player.State.AvatarTeams.Single(state => state.TeamId == 1);
        Assert.Equal(current.Avatars.Select(avatar => avatar.Guid), state.AvatarGuids);
        Assert.Equal(current.CurrentAvatarGuid, state.CurrentAvatarGuid);
        Assert.Contains(current.CurrentAvatarGuid, state.AvatarGuids);
    }

    [Fact]
    public async Task ChangeAvatar_UpdatesSelectionAndPersists()
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var first = avatars.Avatars[10000005];
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);

        await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = 1,
            CurAvatarGuid = first.Guid,
            AvatarTeamGuidList = { first.Guid, second.Guid }
        });

        var response = await teams.OnChangeAvatar(new ChangeAvatarReq {
            Guid = second.Guid,
            SkillId = 123
        });

        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal(second.Guid, response.CurGuid);
        Assert.Equal(expected: 123u, response.SkillId);
        Assert.Equal(second.Guid, teams.Current.CurrentAvatarGuid);

        Assert.Equal(
            second.Guid,
            player.State.AvatarTeams.Single(state => state.TeamId == 1).CurrentAvatarGuid);

        var persisted = NetPlayerState.Parser.ParseFrom(player.State.ToByteArray());
        var (reconnected, _) = Player(uid: 1001, data);
        reconnected.State = persisted;
        await reconnected.Module<AvatarModule>().OnLogin();

        Assert.Equal(second.Guid, reconnected.Module<TeamModule>().Current.CurrentAvatarGuid);
    }

    [Theory]
    [InlineData("change")]
    [InlineData("setup")]
    [InlineData("choose")]
    public async Task TeamChange_ReplacementAppearReferencesDisappearedEntity(string operation)
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data, includeWorld: true);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();
        var world = player.Module<WorldModule>();
        var scene = player.Module<SceneModule>();

        await avatars.OnLogin();
        var first = avatars.Avatars[10000005];
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);

        if (operation == "change")
        {
            await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
                TeamId = 1,
                CurAvatarGuid = first.Guid,
                AvatarTeamGuidList = { first.Guid, second.Guid }
            });
        } else if (operation == "choose")
        {
            await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
                TeamId = 2,
                CurAvatarGuid = second.Guid,
                AvatarTeamGuidList = { second.Guid }
            });
        }

        world.EnterOwnWorld();
        _ = scene.OnEnterSceneReady(new EnterSceneReadyReq { EnterSceneToken = 1 }).ToArray();

        var enter = Assert.Single(
            scene.OnSceneInit(new SceneInitFinishReq { EnterSceneToken = 1 })
                .OfType<PlayerEnterSceneInfoNotify>());

        switch (operation)
        {
            case "change":
                await teams.OnChangeAvatar(new ChangeAvatarReq { Guid = second.Guid });
                break;
            case "setup":
                await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
                    TeamId = 1,
                    CurAvatarGuid = second.Guid,
                    AvatarTeamGuidList = { first.Guid, second.Guid }
                });
                break;
            case "choose":
                await teams.OnChooseCurAvatarTeam(new ChooseCurAvatarTeamReq { TeamId = 2 });
                break;
        }

        var notifications = scene.OnTeamChanged().ToArray();
        var disappear = Assert.Single(notifications.OfType<SceneEntityDisappearNotify>());
        var appear = Assert.Single(notifications.OfType<SceneEntityAppearNotify>());
        var disappearedEntityId = Assert.Single(disappear.EntityList);

        Assert.Equal(VisionType.VISION_TYPE_REPLACE, disappear.DisappearType);
        Assert.Equal(VisionType.VISION_TYPE_REPLACE, appear.AppearType);
        Assert.Equal(enter.CurAvatarEntityId, disappearedEntityId);
        Assert.Equal(disappearedEntityId, appear.Param);
    }

    [Fact]
    public async Task ChangeAvatar_AvatarOutsideCurrentTeam_IsRejected()
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var original = teams.Current;
        var (outside, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(outside);

        var response = await teams.OnChangeAvatar(new ChangeAvatarReq { Guid = outside.Guid });

        Assert.Equal((int)Retcode.RETCODE_AVATAR_NOT_EXIST_IN_TEAM, response.Retcode);
        Assert.Equal(original.CurrentAvatarGuid, teams.Current.CurrentAvatarGuid);

        Assert.Equal(
            original.CurrentAvatarGuid,
            player.State.AvatarTeams.Single(state => state.TeamId == 1).CurrentAvatarGuid);
    }

    [Fact]
    public async Task ChooseCurAvatarTeam_SwitchesAndPersistsCurrentTeam()
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();
        var teams = player.Module<TeamModule>();

        await avatars.OnLogin();
        var (second, added) = await avatars.AddAvatar(10000007);
        Assert.True(added);
        Assert.NotNull(second);

        await teams.OnSetUpAvatarTeam(new SetUpAvatarTeamReq {
            TeamId = 2,
            CurAvatarGuid = second.Guid,
            AvatarTeamGuidList = { second.Guid }
        });

        var response = await teams.OnChooseCurAvatarTeam(
            new ChooseCurAvatarTeamReq { TeamId = 2 });

        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal(expected: 2u, response.CurTeamId);
        Assert.Equal(expected: 2u, teams.Current.Id);
        Assert.Equal(expected: 2u, player.State.CurrentAvatarTeamId);
        Assert.Equal(second.Guid, teams.Current.CurrentAvatarGuid);

        var persisted = NetPlayerState.Parser.ParseFrom(player.State.ToByteArray());
        var (reconnected, _) = Player(uid: 1001, data);
        reconnected.State = persisted;
        var login = await reconnected.Module<AvatarModule>().OnLogin();

        Assert.Equal(expected: 2u, reconnected.Module<TeamModule>().Current.Id);
        Assert.Equal(expected: 2u, login.CurAvatarTeamId);
        Assert.Equal(second.Guid, login.ChooseAvatarGuid);
    }

    [Fact]
    public async Task ChooseCurAvatarTeam_EmptyTeam_IsRejected()
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data);
        var teams = player.Module<TeamModule>();

        await player.Module<AvatarModule>().OnLogin();

        var response = await teams.OnChooseCurAvatarTeam(
            new ChooseCurAvatarTeamReq { TeamId = 2 });

        Assert.Equal((int)Retcode.RETCODE_AVATAR_NOT_EXIST_IN_TEAM, response.Retcode);
        Assert.Equal(expected: 1u, teams.Current.Id);
        Assert.Equal(expected: 1u, player.State.CurrentAvatarTeamId);
    }

    [Fact]
    public async Task WearEquip_WeaponOwnedByAnotherAvatar_SwapsWeapons()
    {
        var data = Data();
        var (player, sent) = Player(uid: 1001, data);
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

        Assert.Equal(expected: 0, response.Retcode);
        Assert.Equal(secondWeapon, first.WeaponGuid);
        Assert.Equal(firstWeapon, second.WeaponGuid);

        Assert.Collection(
            sent,
            message => {
                var unequip = Assert.IsType<AvatarEquipChangeNotify>(message);
                Assert.Equal(second.Guid, unequip.AvatarGuid);
                Assert.Equal(expected: 0ul, unequip.EquipGuid);
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

    [Fact]
    public async Task AddAvatar_ConcurrentCalls_AddOnlyOneAvatar()
    {
        var data = Data();
        var (player, _) = Player(uid: 1001, data);
        var avatars = player.Module<AvatarModule>();

        var results = await Task.WhenAll(
            Enumerable.Range(start: 0, count: 100)
                .Select(_ => Task.Run(() => avatars.AddAvatar(10000007))));

        Assert.Single(results, result => result.Added);
        Assert.Single(avatars.Avatars.Values, avatar => avatar.AvatarId == 10000007);
        Assert.Single(player.State.Avatars, state => state.AvatarId == 10000007);
    }

    private static (StarlightPlayer Player, List<IMessage> Sent) Player(
        uint uid,
        GameData data,
        bool includeWorld = false
    )
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var registry = new ModuleRegistry();
        var guidManager = new GuidManager(serverId: 1);
        registry.AddModule<InventoryModule>((_, player) => new InventoryModule(player, guidManager, data));
        registry.AddModule<AvatarModule>((_, player) => new AvatarModule(player, data, guidManager));
        registry.AddModule<TeamModule>((_, player) => new TeamModule(player));
        registry.AddModule<BornModule>((_, player) => new BornModule(player));

        if (includeWorld)
        {
            var worlds = new WorldManager();
            registry.AddModule<WorldModule>((_, player) => new WorldModule(player, worlds));
            registry.AddModule<SceneModule>((_, player) => new SceneModule(player));
        }

        registry.Build();

        var (client, server) = DirectTunnel.CreatePair();
        var sent = new List<IMessage>();

        _ = client.Subscribe(GameSubjects.OutboundPacket, message => {
            lock (sent)
            {
                sent.Add(message.Decode<IMessage>());
            }

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

        AddAvatar(data, avatarId: 10000005, depotId: 500, weaponId: 11501);
        AddAvatar(data, avatarId: 10000007, depotId: 700, weaponId: 11502);
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
