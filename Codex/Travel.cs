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
    private readonly GameState game;
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly ICallGateSubscriber<string, object> lsExecute;
    private readonly ICallGateSubscriber<bool> lsBusy;
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<float> navProgress;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, bool, bool> moveTo;
    private readonly ICallGateSubscriber<bool> pathfinding;
    private readonly ICallGateSubscriber<bool> pathRunning;
    private readonly ICallGateSubscriber<object> stop;

    private Step step = Step.Idle;
    private Step afterSend = Step.Idle;
    private Source? target;
    private string targetName = "";
    private string? pending;
    private bool zoneOnly;
    private uint startTerritory;
    private Vector3 floor;
    private DateTime since;
    private DateTime lastReport;
    private DateTime lastAction;

    public Travel(IDalamudPluginInterface pi, IClientState clientState, IObjectTable objects, ICondition condition, IDataManager data,
                  GameState game, Configuration config, IPluginLog log, IChatGui chat)
    {
        this.clientState = clientState; this.objects = objects; this.condition = condition; this.data = data; this.game = game;
        this.config = config; this.log = log; this.chat = chat;
        lsExecute    = pi.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        lsBusy       = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        navReady     = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navProgress  = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        pointOnFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        moveTo       = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathfinding  = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        pathRunning  = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        stop         = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool Active => step != Step.Idle;
    public string Status { get; private set; } = "";

    public void Go(Source source, string entryName)
    {
        Cancel();
        target = source; targetName = entryName; lastReport = DateTime.MinValue;
        zoneOnly = !source.HasMapPosition;
        if (zoneOnly && string.IsNullOrEmpty(source.Loc))
        {
            chat.Print($"[Codex] {entryName}: no location known for {source.Name}; flag only.");
            target = null;
            return;
        }
        if (!zoneOnly && clientState.TerritoryType == source.Terr)
        {
            Enter(Step.WaitingForNav, $"already in {source.Loc}; waiting for the navmesh");
            return;
        }
        var where = source.Tp ?? source.Loc!;
        chat.Print(zoneOnly ? $"[Codex] {entryName}: heading to {source.Loc}, where {source.Name} roams."
                            : $"[Codex] {entryName}: heading to {source.Name} in {source.Loc}.");
        Queue($"tp {where}", Step.Teleporting, $"teleporting to {where}");
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
                        var (x, z) = MapMath.MapToWorld(target.Xy![0], target.Xy[1], target.Size, target.OffX, target.OffY);
                        var found = pointOnFloor.InvokeFunc(new Vector3(x, 1024f, z), false, 5f);
                        if (found == null) { Fail($"no walkable ground near ({target.Xy[0]}, {target.Xy[1]})"); break; }
                        floor = found.Value;
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
                    if (close || (!active && Elapsed() > 3))
                    {
                        if (Mounted) { lastAction = DateTime.MinValue; Enter(Step.Dismounting, "dismounting"); }
                        else Finish($"Arrived near {target.Name} for {targetName}");
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
