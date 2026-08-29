namespace Starlight.Common
{
    /// <summary>
    /// Reimplementation of hk4e GuidMgr (gameserver/src/misc/guid_mgr.cpp).
    /// CB1 layout: [unix time:32][sequence low 12 bits:12][server id:8][0x1:type].
    /// Original code from KazusaGI CB1
    /// </summary>
    public sealed class GuidManager
    {
        public enum GuidType : uint
        {
            None = 0,
            Avatar = 1,
            Item = 2,
            Mail = 3,
        }

        private static readonly object SequenceLock = new();
        private static uint _sequence;
        private readonly uint _serverId;

        public GuidManager(uint serverId = 1)
        {
            if (serverId > 0xFF)
                throw new ArgumentOutOfRangeException(nameof(serverId), "hk4e GUID server id is 8-bit");
            _serverId = serverId;
        }

        public ulong GenGuid(GuidType type)
        {
            uint seq;
            lock (SequenceLock)
                seq = ++_sequence;

            uint now = unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ulong low = ((ulong)(seq & 0xFFF) << 20)
                        | ((ulong)(_serverId & 0xFF) << 12)
                        | 0x10UL
                        | ((ulong)type & 0xFUL);
            return ((ulong)now << 32) | low;
        }
    }
}
