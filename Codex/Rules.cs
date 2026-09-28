namespace Codex;

public sealed class Source
{
    public string K { get; set; } = "";
    public string Name { get; set; } = "";
    public string? T { get; set; }
    public string? Loc { get; set; }
    public float[]? Xy { get; set; }
    public int Lv { get; set; }
    public int? LvMax { get; set; }
    public string? Rank { get; set; }
    public string? Note { get; set; }
    public bool Rec { get; set; }
    public uint Terr { get; set; }
    public uint Map { get; set; }
    public int Size { get; set; } = 100;
    public int OffX { get; set; }
    public int OffY { get; set; }

    public bool HasMapPosition => Terr != 0 && Map != 0 && Xy is { Length: 2 };
}

public sealed class Entry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
    public int MinLv { get; set; }
    public int UnlockLink { get; set; }
    public List<Source> Sources { get; set; } = new();
}

public static class Kinds
{
    public static readonly string[] AlwaysOn = { "world", "fate", "leve", "questmob", "totem", "quest", "default" };

    public static readonly (string Key, string Label)[] OptIn =
    {
        ("dungeon", "Dungeons"), ("trial", "Trials"), ("raid", "Raids"), ("carnivale", "Masked Carnivale"),
        ("guildhest", "Guildhests"), ("tdungeon", "Treasure dungeons"), ("hunt", "A/S-rank hunts"), ("map", "Treasure maps"),
        ("unknown", "Location unknown"),
    };

    public static readonly (int Lo, int Hi)[] Bands =
        { (1, 15), (16, 30), (31, 40), (41, 50), (51, 60), (61, 70), (71, 80), (81, 90), (91, 100) };

    public static bool Visible(Source s, HashSet<string> enabled)
        => s.K == "hunt" ? s.Rank == "B" || enabled.Contains("hunt") : Array.IndexOf(AlwaysOn, s.K) >= 0 || enabled.Contains(s.K);

    public static string Label(string kind)
    {
        foreach (var (key, label) in OptIn) if (key == kind) return label;
        return kind switch
        {
            "world" => "Open world", "fate" => "FATE", "leve" => "Levequest", "questmob" => "Quest enemy",
            "totem" => "Whalaqee totem", "quest" => "Quest reward", "default" => "Known from the start", _ => kind,
        };
    }

    public static (int Lo, int Hi)? BandOf(int level)
    {
        foreach (var b in Bands) if (level >= b.Lo && level <= b.Hi) return b;
        return null;
    }
}

public static class Progress
{
    // Floored so 100% only ever means complete.
    public static string Summary(int done, int total, int hidden)
    {
        var pct = total == 0 ? 0 : (int)(100L * done / total);
        var s = $"{done} of {total} obtained, {pct}%";
        return hidden > 0 ? s + $" · {hidden} hidden by Include" : s;
    }
}
