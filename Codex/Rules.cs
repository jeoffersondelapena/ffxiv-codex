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
    public string? Leve { get; set; }
    public Source? Via { get; set; }
    public uint Npc { get; set; }
    public float[]? World { get; set; }
    public string? Unlock { get; set; }
    public uint QuestId { get; set; }

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
    public static readonly string[] AlwaysOn = { "world", "fate", "leve", "questmob", "totem", "gourd", "quest", "default", "carnivale" };
    public static readonly string[] Duties = { "dungeon", "trial", "raid", "guildhest", "tdungeon" };

    // given things first, then certainty, then waiting, then luck; party content second last
    public static readonly string[] Order =
    {
        "default", "quest", "totem", "gourd", "questmob", "carnivale", "world", "leve", "fate", "hunt", "wanted", "map",
        "dungeon", "trial", "raid", "guildhest", "tdungeon", "unknown",
    };

    public static int Rank(string kind)
    {
        var i = Array.IndexOf(Order, kind);
        return i < 0 ? Order.Length : i;
    }

    // The source that drives a row: first kind in the order with a visible, still usable source, lowest level within it.
    // A locked source keeps its place: the plan is the same, the unlock quest just comes first.
    public static Source? Driver(Entry e, HashSet<string> enabled, Func<Source, bool>? usable = null)
        => e.Sources.Where(s => Visible(s, enabled) && (usable == null || usable(s))).OrderBy(s => Rank(s.K)).ThenBy(s => s.Lv).FirstOrDefault();

    // what an unlock quest opens, for the trip's wording
    public static string Opens(Source s) => s.K switch
    {
        "carnivale" => "that Carnivale stage",
        "gourd" => "the Kornago gourds",
        _ => "this source",
    };

    // a quest enemy exists only while its quest runs; the note carries the quest's name
    public static string? QuestOf(Source s)
        => s.K == "questmob" && s.Note != null && s.Note.StartsWith("quest: ") ? s.Note["quest: ".Length..] : null;

    // a Carnivale stage is played until cleared, then never again; the note carries the stage number
    public static int? StageOf(Source s)
        => s.K == "carnivale" && s.Note != null && s.Note.StartsWith("Stage ") && int.TryParse(s.Note["Stage ".Length..], out var n) ? n : null;

    // The tracker's category order; a list only shows the ones its sources use.
    public static readonly (string Key, string Label)[] OptIn =
    {
        ("dungeon", "Dungeons"), ("trial", "Trials"), ("raid", "Raids"),
        ("guildhest", "Guildhests"), ("tdungeon", "Treasure dungeons"), ("hunt", "A/S-rank hunts"), ("wanted", "Wanted targets"), ("map", "Treasure maps"),
        ("unknown", "Location unknown"),
    };

    public static readonly (int Lo, int Hi)[] Bands =
        { (1, 15), (16, 30), (31, 40), (41, 50), (51, 60), (61, 70), (71, 80), (81, 90), (91, 100) };

    // one line per Include box, so the row explains itself on hover
    public static string IncludeTip(string key) => key switch
    {
        "dungeon" => "Dungeon enemies. Party content; the learn is guaranteed while level-synced.",
        "trial" => "Trial bosses. Party content; the learn is guaranteed while level-synced.",
        "raid" => "Raid enemies. Party content; the learn is guaranteed while level-synced.",
        "guildhest" => "Guildhest enemies. Party content; the learn is guaranteed while level-synced.",
        "tdungeon" => "Enemies inside treasure-map dungeons. Needs a party and a lucky portal roll.",
        "hunt" => "A- and S-rank marks. They spawn on long timers and are often contested. B ranks are always shown.",
        "wanted" => "Rare enemies that may appear during leves of one level from one levemete. Whether one shows up is luck, and each try costs an allowance.",
        "map" => "Enemies that spawn from treasure maps. Needs a map item and a lucky roll.",
        "unknown" => "Entries the data has no location for.",
        _ => "",
    };

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
        // the row reads left to right the way the driver order ranks the kinds
        return OptIn.Where(o => present.Contains(o.Key)).OrderBy(o => Rank(o.Key)).ToList();
    }

    public static int? ShownLevel(Entry e, HashSet<string> enabled, Func<Source, bool>? usable = null) => Driver(e, enabled, usable)?.Lv;

    public static string Label(string kind)
    {
        foreach (var (key, label) in OptIn) if (key == kind) return label;
        return kind switch
        {
            "world" => "Open world", "fate" => "FATE", "leve" => "Levequest", "wanted" => "Wanted target", "questmob" => "Quest enemy",
            "totem" => "Whalaqee totem", "gourd" => "Kornago gourd", "quest" => "Quest reward", "questgiver" => "Quest giver", "default" => "Known from the start", _ => kind,
        };
    }

    public static readonly (string Key, string Label)[] Sorts =
        { ("lv", "by level"), ("lv-desc", "by level, descending"), ("no", "by number"), ("no-desc", "by number, descending"), ("az", "A to Z"), ("za", "Z to A") };

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
            "lv-desc" => rows.OrderByDescending(level).ThenBy(r => entry(r).Id),
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
        "wanted" => "Wanted target: a rare chance during leves of one level from one levemete; each try costs an allowance",
        "questmob" => "Quest enemy: appears only during that quest",
        "quest" => "Quest reward",
        "totem" => "Whalaqee totem: bought from Wayward Gaheel Ja once its requirement is met",
        "gourd" => "Kornago gourd: bought from the Kornago merchant in Central Shroud with Remnants of Resilience from the Crucible of the Unbroken",
        "carnivale" => "Masked Carnivale stage, entered at the Celestium in Ul'dah; the learn is not guaranteed",
        "map" => "Treasure map: appears from the map's chest",
        "dungeon" or "trial" or "raid" or "guildhest" or "tdungeon" => "Inside that duty; the learn is guaranteed while level-synced",
        "world" => "Open world: always there",
        "default" => "Known from the start",
        _ => "",
    };

    // A leve is accepted from its levemete first; once the game says the leve is held, the tap goes to the enemy.
    public static Source NextStop(Source s, bool leveHeld)
        => s.Via is { HasMapPosition: true } && !leveHeld ? s.Via : s;

    // wiki titles carry disambiguation the game's names do not
    public static string LeveKey(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, @"\s*\((L|Levequest|Quest)\)\s*$", "").Trim().ToLowerInvariant();

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
