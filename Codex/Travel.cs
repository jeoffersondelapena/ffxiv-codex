using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Codex;

// Telepo teleports; Lifestream only rides a city aethernet (its own teleport form never resolved a shard); vnavmesh walks.
public sealed unsafe class Travel
{
    private enum Step { Idle, ChangingJob, Teleporting, AtAetheryte, Aethernet, WaitingForNav, Mounting, Walking, Dismounting }

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
    private readonly ICallGateSubscriber<bool> lsBusy;
    private readonly ICallGateSubscriber<uint> lsActive;
    private readonly ICallGateSubscriber<string, bool> lsAethernet;
    private readonly ICallGateSubscriber<object> lsAbort;
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<float> navProgress;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPoint;
    private readonly ICallGateSubscriber<Vector3, bool, bool> moveTo;
    private readonly ICallGateSubscriber<bool> pathfinding;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<object> stop;

    private Step step = Step.Idle;
    private Source? target;
    private string targetName = "";
    private byte wantedJob;
    private uint teleportAetheryte;
    private uint teleportTerritory;
    private Vector3 aetherytePos;
    private string? shard;
    private bool approached;
    private Vector3 floor;
    private Vector3? lastPos;
    private DateTime lastMoved;
    private DateTime since;
    private DateTime lastReport;
    private DateTime lastAction;

    public Travel(IDalamudPluginInterface pi, IClientState clientState, IObjectTable objects, ICondition condition, IDataManager data,
                  IAetheryteList aetherytes, GameState game, Configuration config, IPluginLog log, IChatGui chat)
    {
        this.clientState = clientState; this.objects = objects; this.condition = condition; this.data = data; this.aetherytes = aetherytes;
        this.game = game; this.config = config; this.log = log; this.chat = chat;
        lsBusy       = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        lsActive     = pi.GetIpcSubscriber<uint>("Lifestream.GetActiveAetheryte");
        lsAethernet  = pi.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
        lsAbort      = pi.GetIpcSubscriber<object>("Lifestream.Abort");
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

    private sealed record Route(uint Aetheryte, uint Territory, Vector3 AetherytePos, string? Shard, string Why);

    // whether a trip can start at all, and why not
    public (bool Ok, string Why) CanGo(Source stop)
    {
        if (!stop.HasMapPosition) return (false, "No map position known");
        if (!navReady.HasFunction) return (false, "vnavmesh is not loaded");
        if (clientState.TerritoryType == stop.Terr) return (true, "");
        var route = PlanRoute(stop);
        if (route.Aetheryte == 0) return (false, route.Why);
        if (route.Shard != null && !lsBusy.HasFunction) return (false, "Lifestream is not loaded (needed for the aethernet)");
        return (true, "");
    }

    // the nearest attuned aetheryte in the zone; failing that, the zone's main aetheryte plus the nearest shard of the district
    private Route PlanRoute(Source stop)
    {
        var goal = WorldOf(stop);
        var sheet = data.GetExcelSheet<Aetheryte>();
        var attuned = new HashSet<uint>();
        for (var i = 0; i < aetherytes.Length; i++) if (aetherytes[i] is { } a) attuned.Add(a.AetheryteId);
        if (sheet == null) return new Route(0, 0, default, null, "no aetheryte data");
        Aetheryte? best = null; var bestDist = float.MaxValue;
        foreach (var a in sheet)
        {
            if (!a.IsAetheryte || a.Territory.RowId != stop.Terr || !attuned.Contains(a.RowId)) continue;
            var d = Vector3.Distance(Flat(PositionOf(a) ?? goal), Flat(goal));
            if (d < bestDist) { best = a; bestDist = d; }
        }
        if (best is { } here) return new Route(here.RowId, here.Territory.RowId, PositionOf(here) ?? goal, null, "");
        var mainId = data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(stop.Terr)?.Aetheryte.RowId ?? 0;
        var main = mainId != 0 ? sheet.GetRowOrDefault(mainId) : null;
        if (main == null || !attuned.Contains(mainId)) return new Route(0, 0, default, null, $"No attuned aetheryte for {stop.Loc}");
        var shardName = stop.Tp;
        if (shardName == null)
        {
            var (name, dist) = NearestShard(stop, goal);
            shardName = name;
            if (shardName == null) return new Route(0, 0, default, null, $"No aethernet shard of {stop.Loc} is on the map");
            log.Debug($"[Codex] Travel: nearest shard to {stop.Name} is {shardName} ({dist:0} yalms)");
        }
        return new Route(mainId, main.Value.Territory.RowId, PositionOf(main.Value) ?? goal, shardName, "");
    }

    // the map markers carry every shard's spot; a marker's key is its Aetheryte row and its X/Y are map pixels
    public List<(string Name, Vector3 Pos)> Shards(uint terr)
    {
        var found = new List<(string, Vector3)>();
        var map = data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(terr)?.Map.ValueNullable;
        var markers = data.GetSubrowExcelSheet<MapMarker>();
        var aetherytes = data.GetExcelSheet<Aetheryte>();
        if (map == null || markers == null || aetherytes == null) return found;
        var c = map.Value.SizeFactor / 100f;
        if (!markers.HasRow(map.Value.MapMarkerRange)) return found;
        foreach (var m in markers[map.Value.MapMarkerRange])
        {
            if (m.DataType is not (3 or 4)) continue;
            var a = aetherytes.GetRowOrDefault(m.DataKey.RowId);
            if (a == null || a.Value.IsAetheryte) continue;
            var name = a.Value.AethernetName.ValueNullable?.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            found.Add((name, new Vector3((m.X - 1024f) / c - map.Value.OffsetX, 0, (m.Y - 1024f) / c - map.Value.OffsetY)));
        }
        return found;
    }

    private (string? Name, float Dist) NearestShard(Source stop, Vector3 goal)
    {
        string? best = null; var bestDist = float.MaxValue;
        foreach (var (name, pos) in Shards(stop.Terr))
        {
            var d = Vector3.Distance(Flat(pos), Flat(goal));
            if (d < bestDist) { best = name; bestDist = d; }
        }
        return (best, bestDist);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    private static Vector3? PositionOf(Aetheryte a)
    {
        foreach (var l in a.Level)
            if (l.RowId != 0 && l.ValueNullable is { } lv) return new Vector3(lv.X, lv.Y, lv.Z);
        return null;
    }

    private static Vector3 WorldOf(Source s)
    {
        if (s.World is { Length: 3 } w) return new Vector3(w[0], w[1], w[2]);
        var (x, z) = MapMath.MapToWorld(s.Xy![0], s.Xy[1], s.Size, s.OffX, s.OffY);
        return new Vector3(x, 0, z);
    }

    private static string JobName(byte job) => job == 43 ? "Beastmaster" : "Blue Mage";

    public void Go(Source stop, string entryName, byte job)
    {
        Cancel();
        var (ok, why) = CanGo(stop);
        if (!ok) { chat.Print($"[Codex] {entryName}: {why}; the flag is on the map."); return; }
        target = stop; targetName = entryName; lastReport = DateTime.MinValue; wantedJob = job;
        // a teleport cast right after a gearset request loses one of the two; the trip waits for the job to change
        if (game.CurrentJob != job)
        {
            var done = game.EquipJob(job, log);
            if (done == null) chat.Print($"[Codex] No gearset saved for {JobName(job)}; going as is.");
            else if (!done.Value.Equipped) chat.Print($"[Codex] The game refused gearset '{done.Value.Name}'; going as is.");
            else { Enter(Step.ChangingJob, $"switching to {JobName(job)} (gearset '{done.Value.Name}')"); return; }
        }
        Depart();
    }

    private void Depart()
    {
        var stop = target!;
        var entryName = targetName;
        if (clientState.TerritoryType == stop.Terr)
        {
            chat.Print($"[Codex] {entryName}: heading to {stop.Name}, already in {stop.Loc}.");
            Enter(Step.WaitingForNav, "waiting for the navmesh");
            return;
        }
        var route = PlanRoute(stop);
        teleportAetheryte = route.Aetheryte; teleportTerritory = route.Territory; aetherytePos = route.AetherytePos; shard = route.Shard; approached = false;
        var tp = Telepo.Instance();
        if (tp == null) { Fail("the teleport interface is missing"); return; }
        chat.Print($"[Codex] {entryName}: heading to {stop.Name} in {stop.Loc}" + (shard != null ? $" by way of the {shard} aethernet." : "."));
        tp->Teleport(teleportAetheryte, 0);
        Enter(Step.Teleporting, "teleporting");
    }

    public void Cancel()
    {
        if (step is Step.Walking or Step.Dismounting or Step.AtAetheryte) { try { stop.InvokeAction(); } catch (IpcNotReadyError) { } }
        if (step is Step.Aethernet) { try { lsAbort.InvokeAction(); } catch (IpcNotReadyError) { } }
        if (step != Step.Idle) log.Information("[Codex] Travel cancelled");
        step = Step.Idle; Status = ""; target = null;
    }

    public void OnUpdate(IFramework framework)
    {
        if (step == Step.Idle || target == null) return;
        try
        {
            switch (step)
            {
                case Step.ChangingJob:
                    if (game.CurrentJob == wantedJob) { chat.Print($"[Codex] Switched to {JobName(wantedJob)}."); Depart(); }
                    else if (Elapsed() > 5) { chat.Print($"[Codex] The gearset did not change the job (is the soul crystal in it?); going as is."); Depart(); }
                    break;
                case Step.Teleporting:
                    if (clientState.TerritoryType == teleportTerritory && Elapsed() > 3 && !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51])
                    {
                        if (shard != null) Enter(Step.AtAetheryte, $"at the aetheryte; asking Lifestream for the {shard} aethernet");
                        else if (clientState.TerritoryType == target.Terr) Enter(Step.WaitingForNav, "arrived; waiting for the navmesh");
                        else Fail($"landed in another zone than {target.Loc}");
                    }
                    else if (Elapsed() > 180) Fail("the teleport did not complete in three minutes");
                    break;
                case Step.AtAetheryte:
                    // Lifestream only rides from an aetheryte it sees as active, which takes standing next to it
                    if (lsActive.InvokeFunc() != 0)
                    {
                        if (approached && pathRunning.InvokeFunc()) { try { stop.InvokeAction(); } catch (IpcNotReadyError) { } }
                        if ((DateTime.Now - lastAction).TotalSeconds < 2) break;
                        lastAction = DateTime.Now;
                        if (lsAethernet.InvokeFunc(shard!)) Enter(Step.Aethernet, $"riding the aethernet to {shard}");
                        else if (Elapsed() > 30) Fail($"Lifestream declined the aethernet to {shard}");
                    }
                    else if (!approached && Elapsed() > 5 && navReady.InvokeFunc())
                    {
                        approached = true;
                        // the aetheryte's own object is the one sure position; the table's placement rows are empty for many
                        var obj = objects.FirstOrDefault(o => o.ObjectKind == ObjectKind.Aetheryte && o.DataId == teleportAetheryte);
                        var at = obj?.Position ?? aetherytePos;
                        var spot = nearestPoint.InvokeFunc(at, 5f, 5f) ?? at;
                        moveTo.InvokeFunc(spot, false);
                        Report("walking up to the aetheryte" + (obj == null ? " (its object was not in view; using the table)" : ""));
                    }
                    else if (Elapsed() > 60) Fail("Lifestream never saw the aetheryte as active");
                    break;
                case Step.Aethernet:
                    if (!lsBusy.InvokeFunc() && Elapsed() > 2 && clientState.TerritoryType == target.Terr)
                        Enter(Step.WaitingForNav, "arrived; waiting for the navmesh");
                    else if (Elapsed() > 60) Fail($"the aethernet ride to {shard} did not complete");
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
                    // right after a teleport the game refuses the mount for a moment; a city refuses it for good
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
                        else Finish();
                    }
                    else if (active && (DateTime.Now - lastMoved).TotalSeconds > 10)
                    {
                        try { stop.InvokeAction(); } catch (IpcNotReadyError) { }
                        Fail($"stuck {away:0} yalms from {target.Name}; the spot may sit on another level, the flag is on the map");
                    }
                    else if (Elapsed() > 600) Fail("walk took longer than ten minutes");
                    break;
                case Step.Dismounting:
                    if (!Mounted) Finish();
                    else if (Elapsed() > 10) Finish(", still mounted");
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

    private void Report(string text)
    {
        lastReport = DateTime.Now;
        Status = $"{targetName}: {text}";
        log.Information($"[Codex] Travel ({targetName}): {text}");
    }

    private void Finish(string suffix = "")
    {
        var text = $"Arrived near {target!.Name} for {targetName}{suffix}";
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
