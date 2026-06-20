namespace Starlight.Kcp.Internals;

public static class KcpConstants
{
    public const int KCP_RTO_NDL = 20;
    public const int KCP_RTO_MIN = 100;
    public const int KCP_RTO_DEF = 200;
    public const int KCP_RTO_MAX = 60000;

    public const int KCP_CMD_PUSH = 81;
    public const int KCP_CMD_ACK = 82;
    public const int KCP_CMD_WASK = 83;
    public const int KCP_CMD_WINS = 84;

    public const int KCP_ASK_SEND = 1;
    public const int KCP_ASK_TELL = 2;

    public const int KCP_WND_SND = 32;
    public const int KCP_WND_RCV = 256;

    public const int KCP_MTU_DEF = 1400;
    public const int KCP_INTERVAL = 100;
    public const int KCP_OVERHEAD = 28;
    public const int KCP_EXTRA_OVERHEAD_DEFAULT = 0;
    public const int KCP_EXTRA_OVERHEAD_HYV_V1 = 4;
    public const int KCP_OVERHEAD_HYV_V1 = KCP_OVERHEAD + KCP_EXTRA_OVERHEAD_HYV_V1;
    public const int KCP_DEADLINK = 20;

    public const int KCP_THRESH_INIT = 2;
    public const int KCP_THRESH_MIN = 2;

    public const int KCP_PROBE_INIT = 7000;
    public const int KCP_PROBE_LIMIT = 120000;
}
