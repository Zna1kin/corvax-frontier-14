using Content.Server.NPC.Components;
using Content.Server.RoundEnd;
using Content.Server.StationEvents.Events;
using Content.Shared.Dataset;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Utility;


namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(PiratesRuleSystem), typeof(LoneOpsSpawnRule))]
public sealed partial class PiratesRuleComponent : Component
{
    // TODO Replace with GameRuleComponent.minPlayers
    /// <summary>
    /// The minimum needed amount of players
    /// </summary>
    [DataField]
    public int MinPlayers = 10;

    /// <summary>
    ///     This INCLUDES the operatives. So a value of 3 is satisfied by 2 players & 1 operative
    /// </summary>
    [DataField]
    public int PlayersPerPirate = 10;

    [DataField]
    public int MaxPirates = 5;

    [DataField]
    public EntProtoId SpawnPointProto = "SpawnPointPirates";

    [DataField]
    public EntProtoId GhostSpawnPointProto = "SpawnPointPirates";

    [DataField]
    public ProtoId<AntagPrototype> CaptaibRoleProto = "PirateCaptain";

    [DataField]
    public ProtoId<AntagPrototype> PirateRoleProto = "Pirate";

    [DataField]
    public ProtoId<AntagPrototype> FirstmateRoleProto = "Firstmate";

    [DataField]
    public ProtoId<StartingGearPrototype> PirateCaptainStartGearProto = "PirateCaptainGear";

    [DataField]
    public ProtoId<StartingGearPrototype> FirstmateStartGearProto = "PirateFirstmateGear";

    [DataField]
    public ProtoId<StartingGearPrototype> PirateStartGearProto = "PirateGear";

    /// <summary>
    ///     Cached starting gear prototypes.
    /// </summary>
    [DataField]
    public Dictionary<string, StartingGearPrototype> StartingGearPrototypes = new ();

    /// <summary>
    ///     Data to be used in <see cref="OnMindAdded"/> for an operative once the Mind has been added.
    /// </summary>
    [DataField]
    public Dictionary<EntityUid, string> PirateMindPendingData = new();

    /// <summary>
    ///     Players who played as an operative at some point in the round.
    ///     Stores the mind as well as the entity name
    /// </summary>
    [DataField]
    public Dictionary<string, EntityUid> PiratePlayers = new();

    [DataField(required: true)]
    public ProtoId<NpcFactionPrototype> Faction = default!;
}
