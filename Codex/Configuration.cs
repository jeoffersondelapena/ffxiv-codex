using Dalamud.Configuration;

namespace Codex;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string List { get; set; } = "blu";
    public Dictionary<string, List<string>> EnabledKinds { get; set; } = new();
    public Dictionary<string, bool> UnobtainedOnly { get; set; } = new();
    public bool Travel { get; set; } = false;
    public uint MountId { get; set; } = 0;
    public string Sort { get; set; } = "lv";
    public bool GroupByBand { get; set; } = true;
    public bool ContinueAfterLeve { get; set; } = true;
    public float MountDistance { get; set; } = 15f;

    public HashSet<string> KindsFor(string list)
        => new(EnabledKinds.TryGetValue(list, out var v) ? v : new List<string>());

    public void SetKind(string list, string kind, bool on)
    {
        var set = KindsFor(list);
        if (on) set.Add(kind); else set.Remove(kind);
        EnabledKinds[list] = set.ToList();
    }
}
