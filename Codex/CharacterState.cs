using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Codex;

// beasts ticked by hand, one file per character keyed by a hash of the content id
public sealed class CharacterState
{
    private readonly IDalamudPluginInterface pi;
    private readonly GameState game;
    private readonly IPluginLog log;
    private ulong loadedFor;
    private string? path;
    private HashSet<int> beasts = new();

    public CharacterState(IDalamudPluginInterface pi, GameState game, IPluginLog log)
    {
        this.pi = pi; this.game = game; this.log = log;
    }

    public bool Ready => loadedFor != 0;

    public void Refresh()
    {
        var cid = game.ContentId;
        if (cid == loadedFor) return;
        Save();
        loadedFor = cid; beasts = new(); path = null;
        if (cid == 0) return;
        path = Path.Combine(pi.GetPluginConfigDirectory(), "state", StateStore.Key(cid) + ".json");
        if (File.Exists(path))
        {
            try { beasts = StateStore.Deserialize(File.ReadAllText(path)); }
            catch (Exception e) { log.Warning($"[Codex] State file could not be read, starting empty: {e.Message}"); }
        }
        log.Information($"[Codex] Character state loaded ({beasts.Count} beast(s) ticked)");
    }

    public bool IsBeastDone(int id) => beasts.Contains(id);

    public void SetBeast(int id, bool done)
    {
        if (done ? beasts.Add(id) : beasts.Remove(id))
        {
            log.Information($"[Codex] Beast {id} {(done ? "ticked" : "unticked")}");
            Save();
        }
    }

    public void Save()
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, StateStore.Serialize(beasts));
        }
        catch (Exception e) { log.Error($"[Codex] State could not be saved: {e.Message}"); }
    }
}
