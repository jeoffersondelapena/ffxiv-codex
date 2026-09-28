using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Codex;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/codex";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly WindowSystem windows = new("Codex");
    private readonly MainWindow main;

    public Configuration Config { get; }
    public CodexData Data { get; private set; }
    public CharacterState State { get; }
    public GameState Game { get; } = new();
    public Travel Travel { get; }

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IClientState clientState,
                  IObjectTable objects, ICondition condition, IDataManager data, IGameGui gameGui, IChatGui chat, IPluginLog log)
    {
        this.pi = pi; this.commands = commands; this.framework = framework; this.clientState = clientState; this.log = log; this.chat = chat;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Data = CodexData.Load(pi, log);
        State = new CharacterState(pi, Game, log);
        Travel = new Travel(pi, clientState, objects, condition, data, Game, Config, log, chat);
        main = new MainWindow(this, gameGui, chat, log, data);
        windows.AddWindow(main);
        pi.UiBuilder.Draw += windows.Draw;
        pi.UiBuilder.OpenMainUi += Open;
        pi.UiBuilder.OpenConfigUi += Open;
        framework.Update += Travel.OnUpdate;
        clientState.Logout += OnLogout;
        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open Codex. '/codex reload' re-reads the data file, '/codex stop' cancels a trip, '/codex import' applies state/import.json to this character." });
        log.Information("[Codex] Loaded");
    }

    public void SaveConfig() => pi.SavePluginConfig(Config);

    private void Open() => main.IsOpen = true;

    private void OnLogout(int type, int code) => State.Save();

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "reload":
                Data = CodexData.Load(pi, log);
                break;
            case "stop":
                Travel.Cancel();
                break;
            case "import":
                Import();
                break;
            default:
                main.IsOpen = !main.IsOpen;
                break;
        }
    }

    private void Import()
    {
        var path = Path.Combine(pi.GetPluginConfigDirectory(), "state", "import.json");
        if (!State.Ready) { chat.PrintError("[Codex] Log in first, then run /codex import."); return; }
        if (!File.Exists(path)) { chat.PrintError("[Codex] Nothing to import: no state/import.json."); return; }
        try
        {
            var ids = StateStore.Deserialize(File.ReadAllText(path));
            var added = State.Import(ids);
            File.Move(path, Path.ChangeExtension(path, ".done.json"), true);
            chat.Print($"[Codex] Imported {added} beast tick(s) for this character; {ids.Count - added} were already ticked.");
            log.Information($"[Codex] Import applied: {added} added of {ids.Count}");
        }
        catch (Exception e)
        {
            chat.PrintError($"[Codex] Import failed: {e.Message}");
            log.Error($"[Codex] Import failed: {e}");
        }
    }

    public void Dispose()
    {
        commands.RemoveHandler(Command);
        clientState.Logout -= OnLogout;
        framework.Update -= Travel.OnUpdate;
        pi.UiBuilder.Draw -= windows.Draw;
        pi.UiBuilder.OpenMainUi -= Open;
        pi.UiBuilder.OpenConfigUi -= Open;
        Travel.Cancel();
        State.Save();
        windows.RemoveAllWindows();
        log.Information("[Codex] Unloaded");
    }
}
