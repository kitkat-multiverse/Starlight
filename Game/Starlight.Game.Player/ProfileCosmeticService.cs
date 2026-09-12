using Starlight.Game.Resources;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.Player;

public sealed class ProfileCosmeticService(GameData data)
{
    public const uint DefaultNameCardId = 210001;

    public void EnsureDefaults(IPlayer player)
    {
        lock (player.StateLock)
        {
            EnsureNameCardDefaultCore(player);

            if (player.Profile.NameCardId != DefaultNameCardId &&
                (!player.State.UnlockedNameCardIds.Contains(player.Profile.NameCardId) ||
                 !IsNameCard(player.Profile.NameCardId)))
            {
                player.Profile.NameCardId = DefaultNameCardId;
            }

            if (player.Profile.PictureId != 0 && !IsProfilePictureUnlockedCore(player, player.Profile.PictureId))
                player.Profile.PictureId = 0;

            if (player.Profile.PictureId == 0 &&
                player.State.BornState != NetPlayerState.Types.PlayerBornState.Pending)
            {
                player.Profile.PictureId = FindDefaultProfilePicture(player);
            }
        }
    }

    public bool IsProfilePictureUnlocked(IPlayer player, uint pictureId)
    {
        lock (player.StateLock)
        {
            return IsProfilePictureUnlockedCore(player, pictureId);
        }
    }

    public bool TrySetProfilePicture(IPlayer player, uint pictureId)
    {
        lock (player.StateLock)
        {
            if (!IsProfilePictureUnlockedCore(player, pictureId))
                return false;

            player.Profile.PictureId = pictureId;
            return true;
        }
    }

    public bool UnlockProfilePicture(IPlayer player, uint pictureId)
    {
        lock (player.StateLock)
        {
            if (!data.ProfilePictureData.TryGetValue(pictureId, out var picture) ||
                picture.UnlockType is ProfilePictureUnlockType.Avatar or ProfilePictureUnlockType.Default)
            {
                return false;
            }

            if (player.State.UnlockedProfilePictureIds.Contains(pictureId))
                return false;

            player.State.UnlockedProfilePictureIds.Add(pictureId);
            return true;
        }
    }

    public IReadOnlyList<uint> GetSpecialProfilePictures(IPlayer player)
    {
        lock (player.StateLock)
        {
            return player.State.UnlockedProfilePictureIds
                .Where(id => data.ProfilePictureData.TryGetValue(id, out var picture) &&
                             picture.UnlockType is ProfilePictureUnlockType.Item or
                                 ProfilePictureUnlockType.Costume or
                                 ProfilePictureUnlockType.ParentQuest)
                .Distinct()
                .Order()
                .ToArray();
        }
    }

    public bool TrySetNameCard(IPlayer player, uint nameCardId)
    {
        lock (player.StateLock)
        {
            EnsureNameCardDefaultCore(player);

            if (!player.State.UnlockedNameCardIds.Contains(nameCardId) || !IsNameCard(nameCardId))
                return false;

            player.Profile.NameCardId = nameCardId;
            return true;
        }
    }

    public bool UnlockNameCard(IPlayer player, uint nameCardId)
    {
        if (nameCardId == 0 || !IsNameCard(nameCardId))
            return false;

        lock (player.StateLock)
        {
            EnsureNameCardDefaultCore(player);

            if (player.State.UnlockedNameCardIds.Contains(nameCardId))
                return false;

            player.State.UnlockedNameCardIds.Add(nameCardId);
            return true;
        }
    }

    public IReadOnlyList<uint> GetUnlockedNameCards(IPlayer player)
    {
        lock (player.StateLock)
        {
            EnsureNameCardDefaultCore(player);

            return player.State.UnlockedNameCardIds
                .Where(IsNameCard)
                .Distinct()
                .Order()
                .ToArray();
        }
    }

    public ProfilePicture ToProtocol(IPlayer player)
    {
        lock (player.StateLock)
        {
            EnsureDefaults(player);
            return new ProfilePicture { PictureId = player.Profile.PictureId };
        }
    }

    private bool IsProfilePictureUnlockedCore(IPlayer player, uint pictureId)
    {
        if (!data.ProfilePictureData.TryGetValue(pictureId, out var picture))
            return false;

        return picture.UnlockType switch {
            ProfilePictureUnlockType.Default => true,
            ProfilePictureUnlockType.Avatar => picture.UnlockParam != 0 &&
                                               player.Module<AvatarModule>().OwnsAvatar(picture.UnlockParam),
            ProfilePictureUnlockType.Item or
                ProfilePictureUnlockType.Costume or
                ProfilePictureUnlockType.ParentQuest => player.State.UnlockedProfilePictureIds.Contains(pictureId),
            _ => false
        };
    }

    private uint FindDefaultProfilePicture(IPlayer player)
    {
        if (player.State.BornAvatarId != 0)
        {
            var travelerPicture = data.ProfilePictureData.Values
                .Where(picture => picture.UnlockType == ProfilePictureUnlockType.Avatar &&
                                  picture.UnlockParam == player.State.BornAvatarId)
                .OrderBy(picture => picture.Priority)
                .ThenBy(picture => picture.Id)
                .FirstOrDefault();

            if (travelerPicture is not null && IsProfilePictureUnlockedCore(player, travelerPicture.Id))
                return travelerPicture.Id;
        }

        return data.ProfilePictureData.Values
            .Where(picture => IsProfilePictureUnlockedCore(player, picture.Id))
            .OrderBy(picture => picture.Priority)
            .ThenBy(picture => picture.Id)
            .Select(picture => picture.Id)
            .FirstOrDefault();
    }

    private bool IsNameCard(uint nameCardId)
        => nameCardId == DefaultNameCardId ||
           data.MaterialData.TryGetValue(nameCardId, out var material) &&
           string.Equals(material.MaterialType, "MATERIAL_NAMECARD", StringComparison.Ordinal);

    private static void EnsureNameCardDefaultCore(IPlayer player)
    {
        if (!player.State.UnlockedNameCardIds.Contains(DefaultNameCardId))
            player.State.UnlockedNameCardIds.Add(DefaultNameCardId);

        if (player.Profile.NameCardId == 0)
            player.Profile.NameCardId = DefaultNameCardId;
    }
}
