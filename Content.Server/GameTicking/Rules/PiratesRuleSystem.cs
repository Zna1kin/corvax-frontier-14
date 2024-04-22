using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Communications;
using Content.Server.RandomMetadata;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost.Roles.Events;
using Content.Server.Humanoid;
using Content.Server.Mind;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Store.Components;
using Content.Server.Store.Systems;
using Content.Shared.CCVar;
using Content.Shared.Dataset;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Pirates;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Store;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Server.Maps;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Configuration;

namespace Content.Server.GameTicking.Rules;

public sealed class PiratesRuleSystem : GameRuleSystem<PiratesRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PricingSystem _pricingSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly MapLoaderSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly RandomMetadataSystem _randomMetadata = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    [ValidatePrototypeId<AntagPrototype>]
    public const string PiratesId = "Pirates";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartAttemptEvent>(OnStartAttempt);
        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnPlayersSpawning);
///        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndTextEvent);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);

        SubscribeLocalEvent<PirateComponent, GhostRoleSpawnerUsedEvent>(OnPlayersGhostSpawning);
        SubscribeLocalEvent<PirateComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<PirateComponent, ComponentInit>(OnComponentInit);
    }

    /// <summary>
    ///     Returns true when the player with UID opUid is a nuclear pirate. Prevents random
    ///     people from using the war declarator outside of the game mode.
    /// </summary>
    public bool TryGetRuleFromPirate(EntityUid opUid, [NotNullWhen(true)] out (PiratesRuleComponent, GameRuleComponent)? comps)
    {
        comps = null;
        var query = EntityQueryEnumerator<PiratesRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var ruleEnt, out var pirates, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(ruleEnt, gameRule))
                continue;

            if (_mind.TryGetMind(opUid, out var mind, out _))
            {
                var found = pirates.PiratePlayers.Values.Any(v => v == mind);
                if (found)
                {
                    comps = (pirates, gameRule);
                    return true;
                }
            }
        }

        return false;
    }

    private void OnComponentInit(EntityUid uid, PirateComponent component, ComponentInit args)
    {
        var query = EntityQueryEnumerator<PiratesRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var ruleEnt, out var pirates, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(ruleEnt, gameRule))
                continue;

            // If entity has a prior mind attached, add them to the players list.
            if (!_mind.TryGetMind(uid, out var mind, out _))
                continue;

            var name = MetaData(uid).EntityName;
            pirates.PiratePlayers.Add(name, mind);
        }
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        var query = EntityQueryEnumerator<PiratesRuleComponent>();
        while (query.MoveNext(out var uid, out var pirates))
        {
            switch (ev.New)
            {
                case GameRunLevel.InRound:
                    OnRoundStart(uid, pirates);
                    break;
            }
        }
    }

    /// <summary>
    /// Loneops can only spawn if there is no pirates active
    /// </summary>
    public bool CheckLoneOpsSpawn()
    {
        return !EntityQuery<PiratesRuleComponent>().Any();
    }

    private void OnRoundStart(EntityUid uid, PiratesRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // TODO: This needs to try and target a Nanotrasen station. At the very least,
        // we can only currently guarantee that NT stations are the only station to
        // exist in the base game.

        var eligible = new List<Entity<StationEventEligibleComponent, NpcFactionMemberComponent>>();
        var eligibleQuery = EntityQueryEnumerator<StationEventEligibleComponent, NpcFactionMemberComponent>();
        while (eligibleQuery.MoveNext(out var eligibleUid, out var eligibleComp, out var member))
        {
            if (!_npcFaction.IsFactionHostile(component.Faction, eligibleUid, member))
                continue;

            eligible.Add((eligibleUid, eligibleComp, member));
        }

        if (eligible.Count == 0)
            return;

        var filter = Filter.Empty();
        var query = EntityQueryEnumerator<PirateComponent, ActorComponent>();
        while (query.MoveNext(out _, out var pirates, out var actor))
        {
            NotifyPirate(actor.PlayerSession, pirates, component);
            filter.AddPlayer(actor.PlayerSession);
        }
    }

    private void OnPlayersSpawning(RulePlayerSpawningEvent ev)
    {
        var query = EntityQueryEnumerator<PiratesRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var pirates, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(uid, gameRule))
                continue;

            // Dear lord what is happening HERE.
            var everyone = new List<ICommonSession>(ev.PlayerPool);
            var prefList = new List<ICommonSession>();
            var frstPrefList = new List<ICommonSession>();
            var cptnPrefList = new List<ICommonSession>();
            var pirates = new List<ICommonSession>();

            // Basically copied verbatim from traitor code
            var PlayersPerPirate = pirates.PlayersPerPirate;
            var maxPirates = pirates.MaxPirates;

            // The LINQ expression ReSharper keeps suggesting is completely unintelligible so I'm disabling it
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var player in everyone)
            {
                if (!ev.Profiles.ContainsKey(player.UserId))
                {
                    continue;
                }

                var profile = ev.Profiles[player.UserId];
                if (profile.AntagPreferences.Contains(pirates.PirateRoleProto.Id))
                {
                    prefList.Add(player);
                }
                if (profile.AntagPreferences.Contains(pirates.FirstmateRoleProto.Id))
	            {
	                frstPrefList.Add(player);
	            }
                if (profile.AntagPreferences.Contains(pirates.CaptaibRoleProto.Id))
                {
                    cptnPrefList.Add(player);
                }
            }

            var numPirates = MathHelper.Clamp(_playerManager.PlayerCount / PlayersPerPirate, 1, maxPirates);

            for (var i = 0; i < numPirates; i++)
            {
                // TODO: Please fix this if you touch it.
                ICommonSession pirate;
                // Only one commander, so we do it at the start
                if (i == 0)
                {
                    if (cptnPrefList.Count == 0)
                    {
                        if (frstPrefList.Count == 0)
                        {
                            if (prefList.Count == 0)
                            {
                                if (everyone.Count == 0)
                                {
                                    Logger.InfoS("preset", "Insufficient ready players to fill up with pirates, stopping the selection");
                                    break;
                                }
                                pirate = _random.PickAndTake(everyone);
                                Logger.InfoS("preset", "Insufficient preferred pirate commanders, agents or nukies, picking at random.");
                            }
                            else
                            {
                                pirate = _random.PickAndTake(prefList);
                                everyone.Remove(pirate);
                                Logger.InfoS("preset", "Insufficient preferred pirate commander or agents, picking at random from regular op list.");
                            }
                        }
                        else
                        {
                            pirate = _random.PickAndTake(frstPrefList);
                            everyone.Remove(pirate);
                            prefList.Remove(pirate);
                            Logger.InfoS("preset", "Insufficient preferred pirate commanders, picking an agent");
                        }
                    }
                    else
                    {
                        pirate = _random.PickAndTake(cptnPrefList);
                        everyone.Remove(pirate);
                        prefList.Remove(pirate);
                        frstPrefList.Remove(pirate);
                        Logger.InfoS("preset", "Selected a preferred pirate commander.");
                    }
                }
                else if (i == 1)
                {
                    if (frstPrefList.Count == 0)
                    {
                        if (prefList.Count == 0)
                        {
                            if (everyone.Count == 0)
                            {
                                Logger.InfoS("preset", "Insufficient ready players to fill up with pirates, stopping the selection");
                                break;
                            }
                            pirate = _random.PickAndTake(everyone);
                            Logger.InfoS("preset", "Insufficient preferred pirate commanders, agents or nukies, picking at random.");
                        }
                        else
                        {
                            pirate = _random.PickAndTake(prefList);
                            everyone.Remove(pirate);
                            Logger.InfoS("preset", "Insufficient preferred pirate commander or agents, picking at random from regular op list.");
                        }
                    }
                    else
                    {
                        pirate = _random.PickAndTake(frstPrefList);
                        everyone.Remove(pirate);
                        prefList.Remove(pirate);
                        Logger.InfoS("preset", "Insufficient preferred pirate commanders, picking an agent");
                    }

                }
                else
                {
                    pirate = _random.PickAndTake(prefList);
                    everyone.Remove(pirate);
                    Logger.InfoS("preset", "Selected a preferred pirate commander.");
                }

                pirates.Add(pirate);
            }

            SpawnPirates(numPirates, pirates, false, pirates);

            foreach (var session in pirates)
            {
                ev.PlayerPool.Remove(session);
                GameTicker.PlayerJoinGame(session);

                if (!_mind.TryGetMind(session, out var mind, out _))
                    continue;

                var name = session.AttachedEntity == null
                    ? string.Empty
                    : Name(session.AttachedEntity.Value);
                pirates.PiratePlayers[name] = mind;
            }
        }
    }

    private void OnPlayersGhostSpawning(EntityUid uid, PirateComponent component, GhostRoleSpawnerUsedEvent args)
    {
        var spawner = args.Spawner;

        if (!TryComp<PirateSpawnerComponent>(spawner, out var pirateSpawner))
            return;

        HumanoidCharacterProfile? profile = null;
        if (TryComp(args.Spawned, out ActorComponent? actor))
            profile = _prefs.GetPreferences(actor.PlayerSession.UserId).SelectedCharacter as HumanoidCharacterProfile;

        // todo: this is kinda awful for multi-nukies
        foreach (var pirates in EntityQuery<PiratesRuleComponent>())
        {
            if (pirateSpawner.PirateStartingGear == null
                || pirateSpawner.PirateRolePrototype == null)
            {
                // I have no idea what is going on with nuke ops code, but I'm pretty sure this shouldn't be possible.
                Log.Error($"Invalid nuke op spawner: {ToPrettyString(spawner)}");
                continue;
            }

            SetupPirateEntity(uid, pirateSpawner.PirateStartingGear, profile, pirates);

            pirates.PirateMindPendingData.Add(uid, pirateSpawner.PirateRolePrototype);
        }
    }

    private void OnMindAdded(EntityUid uid, PirateComponent component, MindAddedMessage args)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out var mind))
            return;

        foreach (var (pirates, gameRule) in EntityQuery<PiratesRuleComponent, GameRuleComponent>())
        {
            if (pirate.PirateMindPendingData.TryGetValue(uid, out var role))
            {
                role ??= pirates.PirateRoleProto;
                _roles.MindAddRole(mindId, new PiratesRoleComponent { PrototypeId = role });
                pirates.PirateMindPendingData.Remove(uid);
            }

            if (mind.Session is not { } playerSession)
                return;

            if (pirates.PiratePlayers.ContainsValue(mindId))
                return;

            pirates.PiratePlayers.Add(Name(uid), mindId);

            if (GameTicker.RunLevel != GameRunLevel.InRound)
                return;

            NotifyPirate(playerSession, component, pirates);
        }
    }

    private (string Name, string Role, string Gear) GetPirateSpawnDetails(int spawnNumber, PiratesRuleComponent component )
    {
        string role;
        string gear;

        // Spawn the Commander then Agent first.
        switch (spawnNumber)
        {
            case 0:
                role = component.CaptaibRoleProto;
                gear = component.PirateCaptainStartGearProto;
                break;
            case 1:
                role = component.FirstmateRoleProto;
                gear = component.FirstmateStartGearProto;
                break;
            default:
                role = component.PirateRoleProto;
                gear = component.PirateStartGearProto;
                break;
        }

        return (role, gear);
    }

    /// <summary>
    ///     Adds missing nuke pirate components, equips starting gear and renames the entity.
    /// </summary>
    private void SetupPirateEntity(EntityUid mob, string name, string gear, HumanoidCharacterProfile? profile, PiratesRuleComponent component)
    {
        _metaData.SetEntityName(mob, name);
        EnsureComp<PirateComponent>(mob);

        if (profile != null)
        {
            _humanoid.LoadProfile(mob, profile);
        }

        if (component.StartingGearPrototypes.TryGetValue(gear, out var gearPrototype))
            _stationSpawning.EquipStartingGear(mob, gearPrototype, profile);

        _npcFaction.RemoveFaction(mob, "NanoTrasen", false);
        _npcFaction.AddFaction(mob, "Syndicate");
    }

    private void SpawnPirates(int spawnCount, List<ICommonSession> sessions, bool addSpawnPoints, PiratesRuleComponent component)
    {
        var spawns = new List<EntityCoordinates>();

        // TODO: This should spawn the nukies in regardless and transfer if possible; rest should go to shot roles.
        for(var i = 0; i < spawnCount; i++)
        {
            var spawnDetails = GetPirateSpawnDetails(i, component);
            var PiratesAntag = _prototypeManager.Index<AntagPrototype>(spawnDetails.Role);

            if (sessions.TryGetValue(i, out var session))
            {
                var profile = _prefs.GetPreferences(session.UserId).SelectedCharacter as HumanoidCharacterProfile;
                if (!_prototypeManager.TryIndex(profile?.Species ?? SharedHumanoidAppearanceSystem.DefaultSpecies, out SpeciesPrototype? species))
                {
                    species = _prototypeManager.Index<SpeciesPrototype>(SharedHumanoidAppearanceSystem.DefaultSpecies);
                }

                var mob = Spawn(species.Prototype, _random.Pick(spawns));
                SetupPirateEntity(mob, spawnDetails.Name, spawnDetails.Gear, profile, component);
                var newMind = _mind.CreateMind(session.UserId, spawnDetails.Name);
                _mind.SetUserId(newMind, session.UserId);
                _roles.MindAddRole(newMind, new PiratesRoleComponent { PrototypeId = spawnDetails.Role });

                // Automatically de-admin players who are being made pirates
                if (_cfg.GetCVar(CCVars.AdminDeadminOnJoin) && _adminManager.IsAdmin(session))
                    _adminManager.DeAdmin(session);

                _mind.TransferTo(newMind, mob);
            }
            else if (addSpawnPoints)
            {
                var spawnPoint = Spawn(component.GhostSpawnPointProto, _random.Pick(spawns));
                var ghostRole = EnsureComp<GhostRoleComponent>(spawnPoint);
                EnsureComp<GhostRoleMobSpawnerComponent>(spawnPoint);
                ghostRole.RoleName = Loc.GetString(PiratesAntag.Name);
                ghostRole.RoleDescription = Loc.GetString(PiratesAntag.Objective);

                var pirateSpawner = EnsureComp<PirateSpawnerComponent>(spawnPoint);
                pirateSpawner.PirateRolePrototype = spawnDetails.Role;
                pirateSpawner.PirateStartingGear = spawnDetails.Gear;
            }
        }
    }

    private void SpawnPiratesForGhostRoles(EntityUid uid, PiratesRuleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // Basically copied verbatim from traitor code
        var PlayersPerPirate = component.PlayersPerPirate;
        var maxPirates = component.MaxPirates;

        var playerPool = _playerManager.Sessions.ToList();
        var numPirates = MathHelper.Clamp(playerPool.Count / PlayersPerPirate, 1, maxPirates);

        var pirates = new List<ICommonSession>();
        SpawnPirates(numPirates, pirates, true, component);
    }

    /// <summary>
    /// Display a greeting message and play a sound for a nukie
    /// </summary>
    private void NotifyPirate(ICommonSession session, PirateComponent pirates, PiratesRuleComponent piratesRule)
    {
        _chatManager.DispatchServerMessage(session, Loc.GetString("pirates-welcome"));
        _audio.PlayGlobal(pirates.GreetSoundNotification, session);
    }

    //For admins forcing someone to pirates.
    public void MakeLonePirate(EntityUid mindId, MindComponent mind)
    {
        if (!mind.OwnedEntity.HasValue)
            return;

        //ok hardcoded value bad but so is everything else here
        _roles.MindAddRole(mindId, new PiratesRoleComponent { PrototypeId = PiratesId }, mind);
        if (mind.CurrentEntity != null)
        {
            foreach (var (pirates, _) in EntityQuery<PiratesRuleComponent, GameRuleComponent>())
            {
                pirates.PiratePlayers.Add(mind.CharacterName!, mindId);
            }
        }

        SetOutfitCommand.SetOutfit(mind.OwnedEntity.Value, "PirateGear", EntityManager);
    }

    private void OnStartAttempt(RoundStartAttemptEvent ev)
    {
        var query = EntityQueryEnumerator<PiratesRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var pirates, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(uid, gameRule))
                continue;

            var minPlayers = pirates.MinPlayers;
            if (!ev.Forced && ev.Players.Length < minPlayers)
            {
                _chatManager.SendAdminAnnouncement(Loc.GetString("pirates-not-enough-ready-players",
                    ("readyPlayersCount", ev.Players.Length), ("minimumPlayers", minPlayers)));
                ev.Cancel();
                continue;
            }

            if (ev.Players.Length != 0)
                continue;

            _chatManager.DispatchServerAnnouncement(Loc.GetString("pirates-no-one-ready"));
            ev.Cancel();
        }
    }

    protected override void Started(EntityUid uid, PiratesRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        // TODO: Loot table or something
        foreach (var proto in new[]
                 {
                     component.PirateCaptainStartGearProto,
                     component.FirstmateStartGearProto,
                     component.PirateStartGearProto
                 })
        {
            component.StartingGearPrototypes.Add(proto, _prototypeManager.Index<StartingGearPrototype>(proto));
        }

        // Add pre-existing nuke pirates to the credit list.
        var query = EntityQuery<PirateComponent, MindContainerComponent, MetaDataComponent>(true);
        foreach (var (_, mindComp, metaData) in query)
        {
            if (!mindComp.HasMind)
                continue;

            component.PiratePlayers.Add(metaData.EntityName, mindComp.Mind.Value);
        }

        if (GameTicker.RunLevel == GameRunLevel.InRound)
            SpawnPiratesForGhostRoles(uid, component);
    }
}
