using Starlight.Game.Modules;

namespace Starlight.Game.Player;

public sealed class StarlightPlayer : IPlayer
{
    public StarlightPlayer()
    {
        Avatars = new AvatarModule(this);
    }

    public AvatarModule Avatars { get; set; }
}
