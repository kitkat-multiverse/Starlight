namespace Starlight.Game.Resources;

public static class AbilityResourceHash
{
    public static uint Compute(string value)
    {
        var hash = 0u;

        foreach (var character in value)
        {
            hash = unchecked(hash * 131 + character);
        }
        return hash;
    }
}
