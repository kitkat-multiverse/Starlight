using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Starlight.Commands;
using Starlight.Game.Player;
using Starlight.Protocol;
using Starlight.Rpc.Tunnel;

namespace Starlight.Chat;

public sealed class ChatService(
    PlayerManager players,
    CommandDispatcher commands,
    ChatConfig config,
    ServerFriendProfile serverFriend,
    ILogger<ChatService> logger
)
{
    private readonly ConcurrentDictionary<ConversationKey, ConversationHistory> _history = [];

    public async Task<int> SendTextAsync(IPlayer sender, uint targetUid, string text)
    {
        if (text.Length > Math.Max(val1: 1, config.MaxMessageLength))
            return (int)Retcode.RETCODE_PRIVATE_CHAT_CONTENT_TOO_LONG;

        if (string.IsNullOrEmpty(text))
            return (int)Retcode.RETCODE_SUCC;

        if (targetUid == serverFriend.Uid)
        {
            var info = Append(ChatInfoForText(sender.Uid, targetUid, text));
            await SendNotify(sender, info);

            if (IsCommand(text))
                await DispatchCommands(sender, text);

            return (int)Retcode.RETCODE_SUCC;
        }

        if (!players.TryGet(targetUid, out var target))
            return (int)Retcode.RETCODE_PLAYER_NOT_EXIST;

        var chat = Append(ChatInfoForText(sender.Uid, targetUid, text));
        await SendNotify(sender, chat);

        try
        {
            await SendNotify(target, chat);
        }
        catch (TunnelClosedException)
        {
            return (int)Retcode.RETCODE_PLAYER_NOT_EXIST;
        }

        return (int)Retcode.RETCODE_SUCC;
    }

    public async Task<int> SendIconAsync(IPlayer sender, uint targetUid, uint icon)
    {
        if (targetUid == serverFriend.Uid)
        {
            var info = Append(ChatInfoForIcon(sender.Uid, targetUid, icon));
            await SendNotify(sender, info);
            return (int)Retcode.RETCODE_SUCC;
        }

        if (!players.TryGet(targetUid, out var target))
            return (int)Retcode.RETCODE_PLAYER_NOT_EXIST;

        var chat = Append(ChatInfoForIcon(sender.Uid, targetUid, icon));
        await SendNotify(sender, chat);

        try
        {
            await SendNotify(target, chat);
        }
        catch (TunnelClosedException)
        {
            return (int)Retcode.RETCODE_PLAYER_NOT_EXIST;
        }

        return (int)Retcode.RETCODE_SUCC;
    }

    public async Task SendServerMessageAsync(IPlayer target, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var info = Append(ChatInfoForText(serverFriend.Uid, target.Uid, message));

        try
        {
            await SendNotify(target, info);
        }
        catch (TunnelClosedException)
        {
            logger.LogDebug("Dropped server chat reply because player {PlayerId} disconnected.", target.Uid);
        }
    }

    public IReadOnlyList<ChatInfo> PullPrivateChat(
        uint playerUid,
        uint targetUid,
        uint fromSequence,
        uint pullNum
    )
    {
        var key = ConversationKey.Create(playerUid, targetUid);

        if (!_history.TryGetValue(key, out var conversation))
            return [];

        var limit = PullLimit(pullNum);
        return conversation.Pull(fromSequence, limit);
    }

    public IReadOnlyList<ChatInfo> PullRecentChat(uint playerUid, uint pullNum, uint beginSequence)
    {
        var limit = PullLimit(pullNum);

        return _history
            .Where(pair => pair.Key.Contains(playerUid))
            .SelectMany(pair => pair.Value.Snapshot())
            .Where(info => beginSequence == 0 || info.Sequence > beginSequence)
            .OrderBy(info => info.Time)
            .ThenBy(info => info.Sequence)
            .TakeLast(limit)
            .ToArray();
    }

    private ChatInfo Append(ChatInfo info)
    {
        var key = ConversationKey.Create(info.Uid, info.ToUid);
        var conversation = _history.GetOrAdd(key, static _ => new ConversationHistory());
        return conversation.Append(info, Math.Max(val1: 1, config.HistoryLimit));
    }

    private async Task DispatchCommands(IPlayer sender, string text)
    {
        var output = new ChatCommandOutput(this, sender);

        var context = new CommandContext(
            CommandSource.Player,
            output,
            sender.Closing,
            sender,
            sender);

        foreach (var rawLine in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();

            if (!IsCommand(line))
                continue;

            await commands.DispatchAsync(line[1..], context);
        }
    }

    private int PullLimit(uint requested)
    {
        var max = Math.Max(val1: 1, config.MaxPullCount);
        var fallback = Math.Clamp(config.DefaultPullCount, min: 1, max);

        if (requested == 0)
            return fallback;

        return (int)Math.Min(requested, (uint)max);
    }

    private static bool IsCommand(string text)
        => text.Length > 1 && text[0] is '/' or '!';

    private static Task SendNotify(IPlayer player, ChatInfo info)
        => player.Send(new PrivateChatNotify { ChatInfo = info });

    private static ChatInfo ChatInfoForText(uint senderUid, uint targetUid, string text)
        => new() {
            Uid = senderUid,
            ToUid = targetUid,
            Time = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Text = text
        };

    private static ChatInfo ChatInfoForIcon(uint senderUid, uint targetUid, uint icon)
        => new() {
            Uid = senderUid,
            ToUid = targetUid,
            Time = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Icon = icon
        };

    private readonly record struct ConversationKey(uint FirstUid, uint SecondUid)
    {
        public static ConversationKey Create(uint first, uint second)
            => first <= second ? new ConversationKey(first, second) : new ConversationKey(second, first);

        public bool Contains(uint uid) => FirstUid == uid || SecondUid == uid;
    }

    private sealed class ConversationHistory
    {
        private readonly List<ChatInfo> _messages = [];
        private readonly object _sync = new();
        private uint _nextSequence = 1;

        public ChatInfo Append(ChatInfo info, int historyLimit)
        {
            lock (_sync)
            {
                info.Sequence = _nextSequence++;
                _messages.Add(info);

                var overflow = _messages.Count - historyLimit;

                if (overflow > 0)
                    _messages.RemoveRange(index: 0, overflow);

                return info;
            }
        }

        public IReadOnlyList<ChatInfo> Pull(uint fromSequence, int limit)
        {
            lock (_sync)
            {
                return _messages
                    .Where(info => fromSequence == 0 || info.Sequence > fromSequence)
                    .Take(limit)
                    .ToArray();
            }
        }

        public ChatInfo[] Snapshot()
        {
            lock (_sync)
            {
                return [.. _messages];
            }
        }
    }
}
