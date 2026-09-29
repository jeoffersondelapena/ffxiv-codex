namespace Codex;

// per-list Include sets, kept free of Dalamud so the tests can compile them
public static class KindSets
{
    // a list with no saved set starts with the default-on kinds ticked
    public static HashSet<string> For(Dictionary<string, List<string>> saved, string list)
        => new(saved.TryGetValue(list, out var v) ? v : Kinds.DefaultOn);

    // version 2 gave the default-on kinds a box; sets saved before that must keep showing them
    public static bool Upgrade(Dictionary<string, List<string>> saved, int version)
    {
        if (version >= 2) return false;
        foreach (var set in saved.Values)
            foreach (var kind in Kinds.DefaultOn)
                if (!set.Contains(kind)) set.Add(kind);
        return true;
    }
}
