using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starlight.Chat;
using Starlight.Commands;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Tunnel;
using Xunit;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Tests;

public sealed class ChatServiceTests
{
    [Fact]
    public async Task ServerFriend_CommandIsEchoedAndReplyComesFromServer()
    {
        var players = new PlayerManager();
        var (player, sent) = Player(uid: 1001);
        Assert.True(players.Add(player));

        var command = new EchoCommand();
        var config = Config();
        var serverFriend = new ServerFriendProfile(config);
        var chat = Chat(players, config, serverFriend, command);

        var retcode = await chat.SendTextAsync(player, serverFriend.Uid, "/echo \"hello world\"");

        Assert.Equal((int)Retcode.RETCODE_SUCC, retcode);
        Assert.Same(player, command.Invoker);

        Assert.Collection(
            sent,
            message => {
                var notify = Assert.IsType<PrivateChatNotify>(message);
                Assert.Equal(expected: 1001u, notify.ChatInfo?.Uid);
                Assert.Equal(expected: 99u, notify.ChatInfo?.ToUid);
                Assert.Equal("/echo \"hello world\"", notify.ChatInfo?.Text);
            },
            message => {
                var notify = Assert.IsType<PrivateChatNotify>(message);
                Assert.Equal(expected: 99u, notify.ChatInfo?.Uid);
                Assert.Equal(expected: 1001u, notify.ChatInfo?.ToUid);
                Assert.Equal("hello world", notify.ChatInfo?.Text);
            });

        var history = chat.PullPrivateChat(player.Uid, serverFriend.Uid, fromSequence: 0, pullNum: 10);
        Assert.Equal(expected: 2, history.Count);
        Assert.Equal(expected: 1u, history[0].Sequence);
        Assert.Equal(expected: 2u, history[1].Sequence);
    }

    [Fact]
    public async Task PrivateMessage_IsDeliveredToBothOnlinePlayers()
    {
        var players = new PlayerManager();
        var (sender, senderMessages) = Player(uid: 1001);
        var (target, targetMessages) = Player(uid: 1002);
        Assert.True(players.Add(sender));
        Assert.True(players.Add(target));

        var config = Config();
        var serverFriend = new ServerFriendProfile(config);
        var chat = Chat(players, config, serverFriend);

        var retcode = await chat.SendTextAsync(sender, target.Uid, "hello");

        Assert.Equal((int)Retcode.RETCODE_SUCC, retcode);
        var senderNotify = Assert.IsType<PrivateChatNotify>(Assert.Single(senderMessages));
        var targetNotify = Assert.IsType<PrivateChatNotify>(Assert.Single(targetMessages));
        Assert.Equal(senderNotify.ChatInfo?.Uid, targetNotify.ChatInfo?.Uid);
        Assert.Equal(senderNotify.ChatInfo?.ToUid, targetNotify.ChatInfo?.ToUid);
        Assert.Equal(senderNotify.ChatInfo?.Sequence, targetNotify.ChatInfo?.Sequence);
        Assert.Equal("hello", targetNotify.ChatInfo?.Text);

        var senderHistory = chat.PullPrivateChat(sender.Uid, target.Uid, fromSequence: 0, pullNum: 10);
        var targetHistory = chat.PullPrivateChat(target.Uid, sender.Uid, fromSequence: 0, pullNum: 10);
        Assert.Equal(senderHistory.Select(info => info.Sequence), targetHistory.Select(info => info.Sequence));
    }

    [Fact]
    public async Task History_IsBoundedPerConversation()
    {
        var players = new PlayerManager();
        var (player, _) = Player(uid: 1001);
        Assert.True(players.Add(player));

        var config = Config();
        config.HistoryLimit = 3;
        var serverFriend = new ServerFriendProfile(config);
        var chat = Chat(players, config, serverFriend);

        for (var index = 1; index <= 5; index++)
            await chat.SendTextAsync(player, serverFriend.Uid, $"message {index}");

        var history = chat.PullPrivateChat(player.Uid, serverFriend.Uid, fromSequence: 0, pullNum: 10);

        Assert.Equal(expected: 3, history.Count);
        Assert.Equal(["message 3", "message 4", "message 5"], history.Select(info => info.Text));
        Assert.Equal([3u, 4u, 5u], history.Select(info => info.Sequence));
    }

    [Fact]
    public void FriendList_ContainsConfiguredServerFriend()
    {
        var players = new PlayerManager();
        var (player, _) = Player(uid: 1001);
        var config = Config();
        config.ServerFriend.Nickname = "Paimon";
        var serverFriend = new ServerFriendProfile(config);
        var chat = Chat(players, config, serverFriend);
        var module = new ChatModule(chat, serverFriend, player);

        var response = module.OnGetPlayerFriendList(new GetPlayerFriendListReq());

        var friend = Assert.Single(response.FriendList);
        Assert.Equal(expected: 99u, friend.Uid);
        Assert.Equal("Paimon", friend.Nickname);
        Assert.Equal(FriendOnlineState.FRIEND_ONLINE_STATE_ONLINE, friend.OnlineState);
        Assert.True(friend.IsGameSource);
    }

    private static ChatService Chat(
        PlayerManager players,
        ChatConfig config,
        ServerFriendProfile serverFriend,
        params ICommand[] commands
    )
    {
        var dispatcher = new CommandDispatcher(
            new CommandRegistry(commands),
            NullLogger<CommandDispatcher>.Instance);

        return new ChatService(
            players,
            dispatcher,
            config,
            serverFriend,
            NullLogger<ChatService>.Instance);
    }

    private static ChatConfig Config()
        => new() {
            ServerFriend = new ServerFriendConfig {
                Uid = 99,
                Nickname = "Starlight",
                Signature = "Server",
                AdventureRank = 60,
                WorldLevel = 8,
                AvatarId = 10000007,
                NameCardId = 210001
            },
            HistoryLimit = 100,
            DefaultPullCount = 50,
            MaxPullCount = 100,
            MaxMessageLength = 1024
        };

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

    private sealed class EchoCommand : ICommand
    {
        public IPlayer? Invoker { get; private set; }
        public string Name => "echo";
        public string Description => "echo";
        public string Usage => "echo <text>";
        public string[] Aliases => [];

        public async Task ExecuteAsync(CommandContext context, string[] args)
        {
            Invoker = context.Invoker;
            await context.ReplyAsync(string.Join(separator: ' ', args));
        }
    }
}
