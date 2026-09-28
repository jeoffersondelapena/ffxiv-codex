using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace Codex;

// teleport with Lifestream, then walk with vnavmesh, waiting for its mesh like GatherBuddy Reborn does
public sealed class Travel
{
    private enum Step { Idle, Teleporting, WaitingForNav, Walking }

    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly ICallGateSubscriber<string, object> lsExecute;
    private readonly ICallGateSubscriber<bool> lsBusy;
    private readonly ICallGateSubscriber<bool> navReady;
    private readonly ICallGateSubscriber<float> navProgress;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, bool, bool> moveTo;
    private readonly ICallGateSubscriber<bool> moving;
    private readonly ICallGateSubscriber<object> stop;

    private Step step = Step.Idle;
    private Source? target;
    private string targetName = "";
    private DateTime since;
    private DateTime lastReport;

    public Travel(IDalamudPluginInterface pi, IClientState clientState, IPluginLog log, IChatGui chat)
    {
        this.clientState = clientState; this.log = log; this.chat = chat;
        lsExecute    = pi.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        lsBusy       = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        navReady     = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        navProgress  = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        pointOnFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        moveTo       = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        moving       = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        stop         = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool Active => step != Step.Idle;
    public string Status { get; private set; } = "";

    public void Go(Source source, string entryName)
    {
        if (!source.HasMapPosition)
        {
            chat.Print($"[Codex] {entryName}: no map position known for {source.Name}; flag only.");
            return;
        }
        Cancel();
        target = source; targetName = entryName; since = DateTime.Now; lastReport = DateTime.MinValue;
        if (clientState.TerritoryType == source.Terr)
        {
            step = Step.WaitingForNav;
            Report($"already in {source.Loc}; waiting for the navmesh");
            return;
        }
        try
        {
            lsExecute.InvokeAction($"tp {source.Loc}");
            step = Step.Teleporting;
            Report($"teleporting to {source.Loc}");
        }
        catch (IpcNotReadyError)
        {
            chat.PrintError("[Codex] Lifestream is not loaded; cannot teleport. The map flag is set.");
            log.Warning("[Codex] Travel: Lifestream IPC not ready");
            step = Step.Idle;
        }
    }

    public void Cancel()
    {
        if (step == Step.Walking) { try { stop.InvokeAction(); } catch (IpcNotReadyError) { } }
        if (step != Step.Idle) log.Information("[Codex] Travel cancelled");
        step = Step.Idle; Status = "";
    }

    public void OnUpdate(IFramework framework)
    {
        if (step == Step.Idle || target == null) return;
        try
        {
            switch (step)
            {
                case Step.Teleporting:
                    if (clientState.TerritoryType == target.Terr && !lsBusy.InvokeFunc())
                    {
                        step = Step.WaitingForNav; Report("arrived; waiting for the navmesh");
                    }
                    else if (Elapsed() > 120) Fail("teleport did not complete in two minutes");
                    break;
                case Step.WaitingForNav:
                    if (navReady.InvokeFunc())
                    {
                        var (x, z) = MapToWorld(target.Xy![0], target.Xy[1], target.Size, target.OffX, target.OffY);
                        var floor = pointOnFloor.InvokeFunc(new Vector3(x, 1024f, z), false, 5f);
                        if (floor == null) { Fail($"no walkable ground near ({target.Xy[0]}, {target.Xy[1]})"); break; }
                        if (moveTo.InvokeFunc(floor.Value, false)) { step = Step.Walking; Report($"walking to ({target.Xy[0]:0.0}, {target.Xy[1]:0.0})"); }
                        else Fail("vnavmesh refused the path request");
                    }
                    else if ((DateTime.Now - lastReport).TotalSeconds > 5)
                    {
                        Report($"waiting for the navmesh ({navProgress.InvokeFunc() * 100:0}%)");
                        if (Elapsed() > 300) Fail("navmesh not ready after five minutes");
                    }
                    break;
                case Step.Walking:
                    if (!moving.InvokeFunc())
                    {
                        chat.Print($"[Codex] Arrived near {target.Name} for {targetName}.");
                        log.Information($"[Codex] Travel done: {targetName} via {target.Name} in {target.Loc}");
                        step = Step.Idle; Status = "";
                    }
                    else if (Elapsed() > 600) Fail("walk took longer than ten minutes");
                    break;
            }
        }
        catch (IpcNotReadyError e)
        {
            Fail($"a helper plugin is not loaded ({e.Message})");
        }
    }

    private double Elapsed() => (DateTime.Now - since).TotalSeconds;

    private void Report(string what)
    {
        Status = what; lastReport = DateTime.Now;
        log.Information($"[Codex] Travel: {what}");
    }

    private void Fail(string why)
    {
        chat.PrintError($"[Codex] Travel stopped: {why}.");
        log.Warning($"[Codex] Travel stopped: {why}");
        step = Step.Idle; Status = "";
    }

    // map coordinates to world X/Z, the inverse of the game's map formula
    public static (float X, float Z) MapToWorld(float mapX, float mapY, int sizeFactor, int offsetX, int offsetY)
    {
        var c = sizeFactor / 100f;
        var x = ((mapX - 1f) * 2048f / (41f / c) - 1024f) / c - offsetX;
        var z = ((mapY - 1f) * 2048f / (41f / c) - 1024f) / c - offsetY;
        return (x, z);
    }
}
