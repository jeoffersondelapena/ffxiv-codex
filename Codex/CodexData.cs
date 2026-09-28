using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Codex;

public sealed class CodexData
{
    public int Schema { get; set; }
    public string Built { get; set; } = "";
    public List<Entry> Blu { get; set; } = new();
    public List<Entry> Bst { get; set; } = new();

    public List<Entry> For(string list) => list == "bst" ? Bst : Blu;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // a refreshed copy in the config folder wins over the one shipped next to the DLL
    public static CodexData Load(IDalamudPluginInterface pi, IPluginLog log)
    {
        var candidates = new[]
        {
            Path.Combine(pi.GetPluginConfigDirectory(), "codex-data.json"),
            Path.Combine(Path.GetDirectoryName(pi.AssemblyLocation.FullName)!, "codex-data.json"),
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var data = JsonSerializer.Deserialize<CodexData>(File.ReadAllText(path), Options) ?? new CodexData();
                log.Information($"[Codex] Data loaded from {path}: {data.Blu.Count} spells, {data.Bst.Count} beasts, built {data.Built}");
                return data;
            }
            catch (Exception e)
            {
                log.Error($"[Codex] Data file {path} could not be read: {e.Message}");
            }
        }
        log.Error("[Codex] No data file found; the window will be empty");
        return new CodexData();
    }
}
