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
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly WindowSystem windows = new("Codex");
    private readonly MainWindow main;

    public Configuration Config { get; }
    public CodexData Data { get; private set; }
    public CharacterState State { get; }
    public GameState Game { get; } = new();

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IClientState clientState, IDataManager data, IGameGui gameGui, IChatGui chat, IPluginLog log)
    {
        this.pi = pi; this.commands = commands; this.clientState = clientState; this.log = log;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        Data = CodexData.Load(pi, log);
        State = new CharacterState(pi, Game, log);
        main = new MainWindow(this, gameGui, chat, log, data);
        windows.AddWindow(main);
        pi.UiBuilder.Draw += windows.Draw;
        pi.UiBuilder.OpenMainUi += Open;
        pi.UiBuilder.OpenConfigUi += Open;
        clientState.Logout += OnLogout;
        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open Codex. '/codex reload' re-reads the data file." });
        log.Information("[Codex] Loaded");
    }

    public void SaveConfig() => pi.SavePluginConfig(Config);

    private void Open() => main.IsOpen = true;

    private void OnLogout(int type, int code) => State.Save();

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("reload", StringComparison.OrdinalIgnoreCase)) Data = CodexData.Load(pi, log);
        else main.IsOpen = !main.IsOpen;
    }

    public void Dispose()
    {
        commands.RemoveHandler(Command);
        clientState.Logout -= OnLogout;
        pi.UiBuilder.Draw -= windows.Draw;
        pi.UiBuilder.OpenMainUi -= Open;
        pi.UiBuilder.OpenConfigUi -= Open;
        State.Save();
        windows.RemoveAllWindows();
        log.Information("[Codex] Unloaded");
    }
}
