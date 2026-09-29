using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Codex;

// Lifestream for the teleport and a city aethernet hop, a mount for the long stretches, vnavmesh for the path.
public sealed class Travel
{
    private enum Step { Idle, WaitingForLifestream, Teleporting, Aethernet, WaitingForNav, Mounting, Walking, Dismounting }

    private const uint MountRoulette = 9;
    private const uint DismountAction = 23;

    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IDataManager data;
    private readonly IAetheryteList aetherytes;
    private readonly GameState game;
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly ICallGateSubscriber<string, object> lsExecute;
    private readonly ICallGateSubscriber<bool> lsBusy;
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<float> navProgress;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPoint;
    private readonly ICallGateSubscriber<Vector3, bool, bool> moveTo;
    private readonly ICallGateSubscriber<bool> pathfinding;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<object> stop;

    private Step step = Step.Idle;
    private Step afterSend = Step.Idle;
    private Source? target;
    private Source? tapped;
    private string targetName = "";
    private string? pending;
    private bool zoneOnly;
    private uint startTerritory;
    private Vector3 floor;
    private Vector3? lastPos;
    private DateTime lastMoved;
    private DateTime since;
    private DateTime lastReport;
    private DateTime lastAction;

    public Travel(IDalamudPluginInterface pi, IClientState clientState, IObjectTable objects, ICondition condition, IDataManager data,
                  IAetheryteList aetherytes, GameState game, Configuration config, IPluginLog log, IChatGui chat)
    {
        this.clientState = clientState; this.objects = objects; this.condition = condition; this.data = data; this.aetherytes = aetherytes; this.game = game;
        this.config = config; this.log = log; this.chat = chat;
        lsExecute    = pi.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        lsBusy       = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        navReady     = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navProgress  = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        pointOnFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        nearestPoint = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
        moveTo       = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathfinding  = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        pathRunning  = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        stop         = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool Active => step != Step.Idle;
    public string Status { get; private set; } = "";

    public bool LeveHeld(Source source)
        => source.Leve != null ? game.HasLeve(game.LeveId(data, source.Leve)) : game.HasAnyLeve();

    public Source NextStop(Source source) => Kinds.NextStop(source, source.Via == null || LeveHeld(source));

    // the quest giver, when a quest still stands in the way: the source's unlock quest, or an enemy's quest not yet taken
    private Source? QuestStop(Source source)
    {
        var locked = game.LockedBy(data, source);
        if (locked != null) return IssuerStop(locked, $"'{locked.Name}' (level {locked.Level}), which opens {Kinds.Opens(source)}");
        if (source.K != "questmob" || Kinds.QuestOf(source) is not string name || game.Quest(data, name) is not { } q) return null;
        if (QuestManager.IsQuestComplete(q.Id) || game.QuestAccepted(q.Id)) return null;
        return IssuerStop(q, $"'{q.Name}' (level {q.Level}); {source.Name} appears once it is under way");
    }

    private Source? IssuerStop(GameState.QuestInfo q, string why)
    {
        if (q.IssuerWorld == null) return null;
        var map = data.GetExcelSheet<Map>()?.GetRowOrDefault(q.IssuerMap);
        var zone = data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(q.IssuerTerr)?.PlaceName.ValueNullable?.Name.ExtractText();
        var (size, offX, offY) = map is { } mp ? (mp.SizeFactor, mp.OffsetX, mp.OffsetY) : ((ushort)100, (short)0, (short)0);
        var (x, y) = MapMath.WorldToMap(q.IssuerWorld[0], q.IssuerWorld[2], size, offX, offY);
        return new Source
        {
            K = "questgiver", QuestId = q.Id, Name = game.NpcName(data, q.IssuerNpc), Loc = zone, Xy = new[] { x, y }, Terr = q.IssuerTerr, Map = q.IssuerMap,
            Size = size, OffX = offX, OffY = offY, World = q.IssuerWorld, Note = why, Lv = q.Level,
        };
    }

    // where a tap would take the character right now
    public Source PlannedStop(Source source) => QuestStop(source) ?? NextStop(source);

    // whether a trip can start at all, and why not
    public (bool Ok, string Why) CanGo(Source stop, byte job)
    {
        if (Array.IndexOf(Kinds.Duties, stop.K) >= 0) return (false, "Inside a duty: queue for it instead");
        if (string.IsNullOrEmpty(stop.Loc) && !stop.HasMapPosition) return (false, "No location known");
        if (!lsBusy.HasFunction) return (false, "Lifestream is not loaded");
        if (stop.HasMapPosition && !navReady.HasFunction) return (false, "vnavmesh is not loaded");
        if (stop.Terr != 0 && !Attuned(stop.Terr)) return (false, $"No attuned aetheryte in {stop.Loc}");
        // no point standing before a quest giver who will not hand the quest over yet
        var gate = stop.QuestId != 0 ? game.QuestById(data, stop.QuestId) : game.LeveUnlock(data, stop.Npc);
        if (gate != null && game.QuestBlocker(data, gate, job) is string blocked) return (false, $"'{gate.Name}' {blocked}");
        return (true, "");
    }

    private bool Attuned(uint terr)
    {
        var main = data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(terr)?.Aetheryte.RowId ?? 0;
        for (var i = 0; i < aetherytes.Length; i++)
        {
            var a = aetherytes[i];
            if (a != null && (a.TerritoryId == terr || (main != 0 && a.AetheryteId == main))) return true;
        }
        return false;
    }

    public void Go(Source source, string entryName, byte job)
    {
        Cancel();
        tapped = source;
        var questStop = QuestStop(source);
        source = questStop ?? NextStop(source);
        target = source; targetName = entryName; lastReport = DateTime.MinValue;
        zoneOnly = !source.HasMapPosition;
        if (zoneOnly && string.IsNullOrEmpty(source.Loc))
        {
            chat.Print($"[Codex] {entryName}: no location known for {source.Name}; flag only.");
            target = null;
            return;
        }
        var (ok, why) = CanGo(source, job);
        if (!ok)
        {
            chat.Print($"[Codex] {entryName}: {why}; the flag is on the map.");
            target = null;
            return;
        }
        var leveLock = ReferenceEquals(source, tapped!.Via) && game.LeveLockedBy(data, source.Npc) is { } u
            ? $" The leves there stay locked until '{u.Name}' (level {u.Level}) is done; accept that quest first." : "";
        if (!zoneOnly && clientState.TerritoryType == source.Terr)
        {
            if (questStop != null || leveLock.Length > 0) chat.Print($"[Codex] {entryName}: heading to {source.Name} for {source.Note ?? tapped.Leve}.{leveLock}");
            Enter(Step.WaitingForNav, $"already in {source.Loc}; waiting for the navmesh");
            return;
        }
        var where = source.Tp ?? source.Loc!;
        chat.Print(zoneOnly ? $"[Codex] {entryName}: heading to {source.Loc}, where {source.Name} roams."
                 : questStop != null ? $"[Codex] {entryName}: heading to {source.Name} in {source.Loc} for {source.Note}."
                 : ReferenceEquals(source, tapped.Via) ? $"[Codex] {entryName}: heading to {source.Name}, the levemete, in {source.Loc} for '{tapped.Leve}'.{leveLock}"
                 : $"[Codex] {entryName}: heading to {source.Name} in {source.Loc}.");
        // `tp` only knows aetherytes; a bare shard name makes Lifestream teleport to that city and ride the aethernet
        Queue(source.Tp != null ? source.Tp : $"tp {where}", Step.Teleporting, $"teleporting to {where}");
    }

    public void Cancel()
    {
        if (step is Step.Walking or Step.Dismounting) { try { stop.InvokeAction(); } catch (IpcNotReadyError) { } }
        if (step != Step.Idle) log.Information("[Codex] Travel cancelled");
        step = Step.Idle; Status = ""; pending = null;
    }

    public void OnUpdate(IFramework framework)
    {
        if (step == Step.Idle || target == null) return;
        try
        {
            switch (step)
            {
                case Step.WaitingForLifestream:
                    if (!lsBusy.InvokeFunc())
                    {
                        lsExecute.InvokeAction(pending!);
                        log.Information($"[Codex] Travel: sent '{pending}' to Lifestream");
                        step = afterSend; since = DateTime.Now;
                    }
                    else if (Elapsed() > 30) Fail("Lifestream stayed busy for 30 seconds");
                    break;
                case Step.Teleporting:
                    var moved = clientState.TerritoryType != startTerritory;
                    var busy = lsBusy.InvokeFunc();
                    var there = zoneOnly ? moved : clientState.TerritoryType == target.Terr;
                    if (!busy && Elapsed() > 2 && there)
                        AfterTeleport();
                    else if (!busy && !moved && Elapsed() > 10)
                    {
                        if (zoneOnly) Finish($"Lifestream did not travel; you may already be in {target.Loc}");
                        else Fail($"Lifestream did not start the teleport to '{target.Tp ?? target.Loc}'");
                    }
                    else if (!busy && moved && !there && Elapsed() > 15)
                        Fail($"Lifestream stopped in another zone instead of {target.Loc}");
                    else if (Elapsed() > 180) Fail("teleport did not complete in three minutes");
                    break;
                case Step.Aethernet:
                    if (!lsBusy.InvokeFunc() && Elapsed() > 2 && clientState.TerritoryType == target.Terr)
                        Enter(Step.WaitingForNav, "arrived; waiting for the navmesh");
                    else if (Elapsed() > 60) Fail($"the aethernet hop to {target.Aethernet} did not complete");
                    break;
                case Step.WaitingForNav:
                    if (navReady.InvokeFunc())
                    {
                        if (target.World is { Length: 3 } w)
                        {
                            // an NPC's own spot carries its height; a top-down floor search in a layered city lands on the wrong level
                            var exact = new Vector3(w[0], w[1], w[2]);
                            floor = nearestPoint.InvokeFunc(exact, 3f, 3f) ?? exact;
                        }
                        else
                        {
                            var (x, z) = MapMath.MapToWorld(target.Xy![0], target.Xy[1], target.Size, target.OffX, target.OffY);
                            var found = pointOnFloor.InvokeFunc(new Vector3(x, 1024f, z), false, 5f);
                            if (found == null) { Fail($"no walkable ground near ({target.Xy[0]}, {target.Xy[1]})"); break; }
                            floor = found.Value;
                        }
                        var player = objects.LocalPlayer?.Position;
                        var far = player != null && Vector3.Distance(player.Value, floor) > config.MountDistance;
                        if (far && !Mounted) { lastAction = DateTime.MinValue; Enter(Step.Mounting, "mounting up"); }
                        else StartWalk();
                    }
                    else if ((DateTime.Now - lastReport).TotalSeconds > 5)
                    {
                        Report($"waiting for the navmesh ({navProgress.InvokeFunc() * 100:0}%)");
                        if (Elapsed() > 300) Fail("navmesh not ready after five minutes");
                    }
                    break;
                case Step.Mounting:
                    // Right after a teleport the game refuses the mount for a moment; a city refuses it for good.
                    if (Mounted) StartWalk();
                    else if (Elapsed() > 8) { log.Information("[Codex] Travel: no mount after 8 s (a city, or too soon after arriving); walking"); StartWalk(); }
                    else if ((DateTime.Now - lastAction).TotalSeconds > 1) { TryMount(); lastAction = DateTime.Now; }
                    break;
                case Step.Walking:
                    var pos = objects.LocalPlayer?.Position;
                    var close = pos != null && Vector3.Distance(pos.Value, floor) < 4f;
                    var active = pathfinding.InvokeFunc() || pathRunning.InvokeFunc();
                    var away = pos != null ? Vector3.Distance(pos.Value, floor) : 0f;
                    if (pos != null && (lastPos == null || Vector3.Distance(pos.Value, lastPos.Value) > 0.5f)) { lastPos = pos; lastMoved = DateTime.Now; }
                    if (close || (!active && Elapsed() > 3))
                    {
                        if (!close) { Fail($"no path reached {target.Name}; stopped {away:0} yalms away, the flag is on the map"); break; }
                        if (Mounted) { lastAction = DateTime.MinValue; Enter(Step.Dismounting, "dismounting"); }
                        else Finish($"Arrived near {target.Name} for {targetName}");
                    }
                    else if (active && (DateTime.Now - lastMoved).TotalSeconds > 10)
                    {
                        try { stop.InvokeAction(); } catch (IpcNotReadyError) { }
                        Fail($"stuck {away:0} yalms from {target.Name}; the spot may sit on another level, the flag is on the map");
                    }
                    else if (Elapsed() > 600) Fail("walk took longer than ten minutes");
                    break;
                case Step.Dismounting:
                    if (!Mounted) Finish($"Arrived near {target.Name} for {targetName}");
                    else if (Elapsed() > 10) Finish($"Arrived near {target.Name} for {targetName}, still mounted");
                    else if ((DateTime.Now - lastAction).TotalSeconds > 1.5 && game.CanUse(ActionType.GeneralAction, DismountAction))
                    {
                        game.Use(ActionType.GeneralAction, DismountAction);
                        lastAction = DateTime.Now;
                    }
                    break;
            }
        }
        catch (IpcNotReadyError e)
        {
            Fail($"a helper plugin is not loaded ({e.Message})");
        }
    }

    private bool Mounted => condition[ConditionFlag.Mounted];

    private void Queue(string command, Step next, string report)
    {
        pending = command; afterSend = next; startTerritory = clientState.TerritoryType;
        Enter(Step.WaitingForLifestream, report);
    }

    private void AfterTeleport()
    {
        if (zoneOnly) { Finish($"Teleported to {target!.Loc}; {target.Name} roams there, no fixed spot"); return; }
        if (target!.Aethernet != null && clientState.TerritoryType != target.Terr)
        {
            Queue(target.Aethernet, Step.Aethernet, $"aethernet to {target.Aethernet}");
            return;
        }
        Enter(Step.WaitingForNav, "arrived; waiting for the navmesh");
    }

    private bool TryMount()
    {
        var id = config.MountId;
        if (id != 0 && game.IsMountUnlocked(id) && game.CanUse(ActionType.Mount, id)) return game.Use(ActionType.Mount, id);
        return game.CanUse(ActionType.GeneralAction, MountRoulette) && game.Use(ActionType.GeneralAction, MountRoulette);
    }

    private void StartWalk()
    {
        lastPos = null; lastMoved = DateTime.Now;
        var fly = Mounted && FlyingUnlocked();
        var how = fly ? "flying" : Mounted ? "riding" : "walking";
        if (moveTo.InvokeFunc(floor, fly)) Enter(Step.Walking, $"{how} to ({target!.Xy![0]:0.0}, {target.Xy[1]:0.0})");
        else Fail("vnavmesh refused the path request");
    }

    private bool FlyingUnlocked()
    {
        var row = data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(clientState.TerritoryType);
        return row != null && game.FlyingUnlocked(row.Value.AetherCurrentCompFlgSet.RowId);
    }

    private double Elapsed() => (DateTime.Now - since).TotalSeconds;

    private void Enter(Step next, string report)
    {
        step = next; since = DateTime.Now;
        Report(report);
    }

    private void Report(string what)
    {
        Status = $"{targetName}: {what}"; lastReport = DateTime.Now;
        log.Information($"[Codex] Travel ({targetName}): {what}");
    }

    private void Finish(string text)
    {
        // one trip, one place: what comes next is said here and left to the player
        if (tapped != null && ReferenceEquals(target, tapped.Via))
        {
            var which = tapped.Leve != null ? $"'{tapped.Leve}'" : "a leve of the right level";
            var left = game.LeveAllowances;
            text += (game.LeveLockedBy(data, target!.Npc) is { } u ? $". {target.Name}'s leves stay locked until '{u.Name}' (level {u.Level}) is done; accept that quest here first, then {which}"
                                                                    : $". Accept {which} from {target.Name}") + (left >= 0 ? $" ({left} allowance(s) left)" : "")
                  + ", then Go again for the enemy";
        }
        else if (target!.K == "questgiver")
            text += $"; accept {target.Note}";
        else if (tapped is { K: "leve" } && ReferenceEquals(target, tapped))
            text += ". Initiate the leve here from your journal; the enemy appears while it runs";
        chat.Print($"[Codex] {text}.");
        log.Information($"[Codex] Travel done: {text}");
        step = Step.Idle; Status = ""; target = null;
    }

    private void Fail(string why)
    {
        chat.PrintError($"[Codex] Travel stopped: {why}.");
        log.Warning($"[Codex] Travel stopped: {why}");
        step = Step.Idle; Status = ""; target = null;
    }
}
