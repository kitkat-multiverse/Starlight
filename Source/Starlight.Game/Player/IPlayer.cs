using Starlight.Game.Modules;

namespace Starlight.Game.Player;

public interface IPlayer
{
    #region Handler Modules

    AvatarModule Avatars { get; set; }

    #endregion
}
