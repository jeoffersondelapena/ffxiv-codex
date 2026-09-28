using System.Security.Cryptography;
using System.Text.Json;
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
        path = Path.Combine(pi.GetPluginConfigDirectory(), "state", Key(cid) + ".json");
        if (File.Exists(path))
        {
            try { beasts = new(JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path))?.Beasts ?? new()); }
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
            File.WriteAllText(path, JsonSerializer.Serialize(new StateFile { Beasts = beasts.OrderBy(x => x).ToList() }));
        }
        catch (Exception e) { log.Error($"[Codex] State could not be saved: {e.Message}"); }
    }

    private static string Key(ulong cid)
        => Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(cid)))[..16].ToLowerInvariant();

    private sealed class StateFile { public List<int> Beasts { get; set; } = new(); }
}
