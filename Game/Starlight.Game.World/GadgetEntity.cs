using Starlight;
using Starlight.Game.Resources;
using Starlight.Protocol;

namespace Starlight.Game.World;

public enum GadgetEntityKind
{
    Server,
    Client,
    Trifle
}

public sealed class GadgetEntity : SceneEntity
{
    private GadgetEntity(
        Scene scene,
        SceneEntityInfo info,
        GadgetEntityKind kind,
        uint gadgetId,
        uint itemId = 0,
        bool sightGroupWithOwner = false,
        bool aliveByOwner = false,
        bool trueLifeTimeByOwner = false,
        LifeByOwner lifeByOwnerType = LifeByOwner.LIFE_BY_OWNER_NONE
    ) : base(scene, info, new FightPropertyStore())
    {
        Kind = kind;
        GadgetId = gadgetId;
        ItemId = itemId;
        SightGroupWithOwner = sightGroupWithOwner;
        AliveByOwner = aliveByOwner;
        TrueLifeTimeByOwner = trueLifeTimeByOwner;
        LifeByOwnerType = lifeByOwnerType;
    }

    public GadgetEntityKind Kind { get; }
    public uint GadgetId { get; }
    public uint ItemId { get; }
    public bool SightGroupWithOwner { get; }
    public bool AliveByOwner { get; }
    public bool TrueLifeTimeByOwner { get; }
    public LifeByOwner LifeByOwnerType { get; }
    public uint OwnerEntityId => Info.Gadget?.OwnerEntityId ?? 0;
    public uint PropOwnerEntityId => Info.Gadget?.PropOwnerEntityId ?? 0;
    public override uint AuthorityPeerId => Info.Gadget?.AuthorityPeerId ?? 0;

    public static GadgetEntity CreateServer(
        Scene scene,
        uint gadgetId,
        Vector position,
        Vector rotation,
        uint campId,
        uint campType,
        uint ownerEntityId,
        uint targetEntityId,
        bool sightGroupWithOwner,
        bool aliveByOwner
    )
    {
        var gadget = new SceneGadgetInfo {
            GadgetId = gadgetId,
            OwnerEntityId = ownerEntityId,
            PropOwnerEntityId = ownerEntityId,
            AuthorityPeerId = scene.World.HostPeerId,
            IsEnableInteract = true
        };

        if (ownerEntityId != 0)
        {
            gadget.AbilityGadget = new AbilityGadgetInfo {
                CampId = campId,
                CampTargetType = campType,
                TargetEntityId = targetEntityId
            };
        }

        return new GadgetEntity(
            scene,
            CreateInfo(
                scene.World.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_GADGET),
                position,
                rotation,
                gadget,
                position),
            GadgetEntityKind.Server,
            gadgetId,
            sightGroupWithOwner: sightGroupWithOwner,
            aliveByOwner: aliveByOwner);
    }

    public static GadgetEntity CreateClient(Scene scene, uint authorityPeerId, EvtCreateGadgetNotify notify)
    {
        var client = new ClientGadgetInfo {
            CampId = notify.CampId,
            CampType = notify.CampType,
            Guid = notify.Guid,
            OwnerEntityId = notify.OwnerEntityId,
            TargetEntityId = notify.TargetEntityId,
            AsyncLoad = notify.IsAsyncLoad,
            IsPeerIdFromPlayer = notify.IsPeerIdFromPlayer
        };
        client.TargetEntityIdList.AddRange(notify.TargetEntityIdList);
        client.TargetLockPointIndexList.AddRange(notify.TargetLockPointIndexList);

        var gadget = new SceneGadgetInfo {
            GadgetId = notify.ConfigId,
            OwnerEntityId = notify.OwnerEntityId,
            PropOwnerEntityId = notify.PropOwnerEntityId,
            AuthorityPeerId = authorityPeerId,
            IsEnableInteract = true,
            ClientGadget = client
        };

        return new GadgetEntity(
            scene,
            CreateInfo(notify.EntityId, notify.InitPos, notify.InitEulerAngles, gadget),
            GadgetEntityKind.Client,
            notify.ConfigId,
            sightGroupWithOwner: notify.SightGroupWithOwner,
            trueLifeTimeByOwner: notify.IsTrueLifeTimeByOwner,
            lifeByOwnerType: notify.LifeByOwnerType);
    }

    public static GadgetEntity CreateTrifle(
        Scene scene,
        uint itemId,
        uint gadgetId,
        ulong guid,
        Vector position,
        Vector rotation
    )
    {
        var gadget = new SceneGadgetInfo {
            GadgetId = gadgetId,
            BornType = GadgetBornType.GADGET_BORN_TYPE_IN_AIR,
            AuthorityPeerId = scene.World.HostPeerId,
            IsEnableInteract = true,
            TrifleGadget = new TrifleGadget {
                Item = new Item {
                    ItemId = itemId,
                    Guid = guid,
                    Material = new Material { Count = 1 }
                }
            }
        };

        return new GadgetEntity(
            scene,
            CreateInfo(scene.World.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_GADGET), position, rotation, gadget),
            GadgetEntityKind.Trifle,
            gadgetId,
            itemId);
    }

    private static SceneEntityInfo CreateInfo(
        uint entityId,
        Vector? position,
        Vector? rotation,
        SceneGadgetInfo gadget,
        Vector? bornPosition = null
    ) => new() {
        EntityType = ProtEntityType.PROT_ENTITY_TYPE_GADGET,
        EntityId = entityId,
        LifeState = 1,
        MotionInfo = new MotionInfo {
            Pos = Copy(position),
            Rot = Copy(rotation),
            Speed = new Vector()
        },
        EntityClientData = new EntityClientData(),
        EntityAuthorityInfo = new EntityAuthorityInfo {
            AbilityInfo = new AbilitySyncStateInfo(),
            BornPos = Copy(bornPosition),
            ClientExtraInfo = new EntityClientExtraInfo { SkillAnchorPosition = new Vector() }
        },
        PropList = [
            new PropPair { Type = (uint)PlayerProperty.Level, PropValue = PlayerProperty.Level.Value(1) }
        ],
        Gadget = gadget
    };

    private static Vector Copy(Vector? value) =>
        value is null ? new Vector() : new Vector { X = value.X, Y = value.Y, Z = value.Z };
}
