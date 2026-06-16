using static Starlight.Kcp.Internals.KcpConstants;

namespace Starlight.Kcp.Internals;

public enum KcpVersion
{
    KCP_UNKNOWN,
    KCP_BASE,
    KCP_HYV_V1
}

public static class KcpVersionExtensions
{
    public static int Overhead(this KcpVersion version) => version switch {
        KcpVersion.KCP_BASE => KCP_OVERHEAD,
        KcpVersion.KCP_HYV_V1 => KCP_OVERHEAD_HYV_V1,
        _ => KCP_OVERHEAD
    };

    public static bool HasExtraHash(this KcpVersion version) => version switch {
        KcpVersion.KCP_HYV_V1 => true,
        _ => false
    };
}
