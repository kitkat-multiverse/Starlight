using System.Collections.Frozen;

namespace Starlight.SDK.Common;

public static class GameLanguage
{
    public const string English = "en";
    public const string ChineseTraditional = "zh-tw";
    public const string ChineseSimplified = "zh-cn";
    public const string Japanese = "ja-jp";
    public const string Portuguese = "pt";

    public static readonly FrozenSet<string> All = new[] {
        English,
        ChineseTraditional,
        ChineseSimplified,
        Japanese,
        Portuguese
    }.ToFrozenSet();
}
