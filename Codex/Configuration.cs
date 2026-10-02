using Dalamud.Configuration;

namespace Codex;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string List { get; set; } = "blu";
    public Dictionary<string, List<string>> EnabledKinds { get; set; } = new();
    public Dictionary<string, bool> UnobtainedOnly { get; set; } = new();
    public string Sort { get; set; } = "lv";
    public bool AutoTravel { get; set; } = true;
    public uint MountId { get; set; } = 0;
    public float MountDistance { get; set; } = 15f;
    public bool Sprint { get; set; } = true;
    public bool GroupByBand { get; set; } = true;

    public StateStore.Settings ToSettings() => new()
    {
        List = List, Sort = Sort, AutoTravel = AutoTravel, MountId = MountId, MountDistance = MountDistance, Sprint = Sprint, GroupByBand = GroupByBand,
        EnabledKinds = EnabledKinds.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
        UnobtainedOnly = new(UnobtainedOnly),
    };

    public void Apply(StateStore.Settings s)
    {
        List = s.List; Sort = s.Sort; AutoTravel = s.AutoTravel; MountId = s.MountId; MountDistance = s.MountDistance; Sprint = s.Sprint; GroupByBand = s.GroupByBand;
        EnabledKinds = s.EnabledKinds.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        UnobtainedOnly = new(s.UnobtainedOnly);
    }

    public HashSet<string> KindsFor(string list)
        => new(EnabledKinds.TryGetValue(list, out var v) ? v : new List<string>());

    public void SetKind(string list, string kind, bool on)
    {
        var set = KindsFor(list);
        if (on) set.Add(kind); else set.Remove(kind);
        EnabledKinds[list] = set.ToList();
    }
}
