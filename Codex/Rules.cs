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

    public string? Tp { get; set; }
    public string? Aethernet { get; set; }

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

    // The tracker's category order; a list only shows the ones its sources use.
    public static readonly (string Key, string Label)[] OptIn =
    {
        ("dungeon", "Dungeons"), ("trial", "Trials"), ("raid", "Raids"), ("carnivale", "Masked Carnivale"),
        ("guildhest", "Guildhests"), ("tdungeon", "Treasure dungeons"), ("hunt", "A/S-rank hunts"), ("map", "Treasure maps"),
        ("unknown", "Location unknown"),
    };

    public static readonly (int Lo, int Hi)[] Bands =
        { (1, 15), (16, 30), (31, 40), (41, 50), (51, 60), (61, 70), (71, 80), (81, 90), (91, 100) };

    public static bool IsOptIn(Source s)
        => s.K == "hunt" ? s.Rank != "B" : Array.IndexOf(AlwaysOn, s.K) < 0;

    public static bool Visible(Source s, HashSet<string> enabled)
        => s.K == "hunt" ? s.Rank == "B" || enabled.Contains("hunt") : Array.IndexOf(AlwaysOn, s.K) >= 0 || enabled.Contains(s.K);

    public static List<(string Key, string Label)> PresentOptIn(IEnumerable<Entry> entries)
    {
        var present = new HashSet<string>();
        foreach (var e in entries)
            foreach (var s in e.Sources)
                if (IsOptIn(s)) present.Add(s.K);
        return OptIn.Where(o => present.Contains(o.Key)).ToList();
    }

    // An entry sits at its lowest visible source, the way the tracker's level filter reads it.
    public static int? ShownLevel(Entry e, HashSet<string> enabled)
    {
        int? best = null;
        foreach (var s in e.Sources)
            if (Visible(s, enabled) && (best == null || s.Lv < best)) best = s.Lv;
        return best;
    }

    public static string Label(string kind)
    {
        foreach (var (key, label) in OptIn) if (key == kind) return label;
        return kind switch
        {
            "world" => "Open world", "fate" => "FATE", "leve" => "Levequest", "questmob" => "Quest enemy",
            "totem" => "Whalaqee totem", "quest" => "Quest reward", "default" => "Known from the start", _ => kind,
        };
    }

    public static readonly (string Key, string Label)[] Sorts =
        { ("lv", "by level"), ("no", "by number"), ("no-desc", "by number, descending"), ("az", "A to Z"), ("za", "Z to A") };

    // The tracker's search: the entry's name, or a shown source's name, place or note.
    public static bool Matches(Entry e, IEnumerable<Source> shown, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return true;
        if (e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        return shown.Any(s => $"{s.Name} {s.Loc} {s.Note}".Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<T> Sorted<T>(IEnumerable<T> rows, Func<T, Entry> entry, Func<T, int> level, string sort)
        => sort switch
        {
            "no" => rows.OrderBy(r => entry(r).Id),
            "no-desc" => rows.OrderByDescending(r => entry(r).Id),
            "az" => rows.OrderBy(r => entry(r).Name, StringComparer.OrdinalIgnoreCase),
            "za" => rows.OrderByDescending(r => entry(r).Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(level).ThenBy(r => entry(r).Id),
        };

    public static string LevelText(Source s)
        => s.LvMax is int max && max != s.Lv ? $"Lv {s.Lv}-{max}" : $"Lv {s.Lv}";

    // What a source kind takes, for the row tooltip; the tracker leaves this to the reader.
    public static string Hint(string kind) => kind switch
    {
        "leve" => "Levequest: accept it from a levemete first (it uses an allowance); the enemy appears only while the leve runs",
        "fate" => "FATE: the enemy appears only while the FATE is up",
        "hunt" => "Hunt mark: roams the zone and spawns on a timer",
        "questmob" => "Quest enemy: appears only during that quest",
        "quest" => "Quest reward",
        "totem" => "Whalaqee totem: bought from Wayward Gaheel Ja once its requirement is met",
        "carnivale" => "Masked Carnivale stage in Ul'dah",
        "map" => "Treasure map: appears from the map's chest",
        "dungeon" or "trial" or "raid" or "guildhest" or "tdungeon" => "Inside that duty",
        "world" => "Open world: always there",
        "default" => "Known from the start",
        _ => "",
    };

    public static string SourceLabel(Source s)
        => s.K == "hunt" && !string.IsNullOrEmpty(s.Rank) ? $"{s.Rank}-rank hunt" : Label(s.K);

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
