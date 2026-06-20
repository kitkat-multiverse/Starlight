using System.Collections.Frozen;

namespace Starlight.SDK.Common;

public static class GameBiz
{
    public const string Global = "hk4e_global";

    public const string China = "hk4e_cn";

    public static readonly FrozenSet<string> All = new[] { Global, China }.ToFrozenSet();
}
