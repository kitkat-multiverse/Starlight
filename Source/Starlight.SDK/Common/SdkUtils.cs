namespace Starlight.SDK.Common;

public static class SdkUtils
{
    private const int MaxDeviceIdLength = 128;

    public static bool IsValidGameBiz(string? biz)
        => !string.IsNullOrEmpty(biz) && GameBiz.All.Contains(biz);

    public static bool IsValidLanguage(string? language)
        => !string.IsNullOrEmpty(language) && GameLanguage.All.Contains(language);

    public static bool IsValidAppId(int appId)
        => Enum.IsDefined(typeof(ApplicationId), appId);

    public static bool IsValidDeviceId(string value)
    {
        if (value.Length > MaxDeviceIdLength || value.Contains('|'))
            return false;

        foreach (var ch in value)
        {
            if (char.IsControl(ch))
                return false;
        }

        return true;
    }
}
