using Codex;
using Xunit;

public class RulesTests
{
    private static Source S(string kind, string? rank = null) => new() { K = kind, Name = "x", Rank = rank };

    [Fact]
    public void Open_world_sources_are_always_shown()
    {
        foreach (var kind in new[] { "world", "fate", "leve", "questmob", "totem", "quest", "default", "carnivale" })
            Assert.True(Kinds.Visible(S(kind), new HashSet<string>()), kind);
    }

    [Fact]
    public void Instances_hunts_and_maps_need_their_checkbox()
    {
        foreach (var kind in new[] { "dungeon", "trial", "raid", "guildhest", "tdungeon", "wanted", "map", "unknown" })
        {
            Assert.False(Kinds.Visible(S(kind), new HashSet<string>()), kind);
            Assert.True(Kinds.Visible(S(kind), new HashSet<string> { kind }), kind);
        }
    }

    [Fact]
    public void B_rank_hunts_are_always_shown_but_A_and_S_ranks_wait_for_the_checkbox()
    {
        Assert.True(Kinds.Visible(S("hunt", "B"), new HashSet<string>()));
        Assert.False(Kinds.Visible(S("hunt", "A"), new HashSet<string>()));
        Assert.False(Kinds.Visible(S("hunt", "S"), new HashSet<string>()));
        Assert.True(Kinds.Visible(S("hunt", "S"), new HashSet<string> { "hunt" }));
    }

    [Fact]
    public void Bands_are_1_15_16_30_then_tens_to_100()
    {
        Assert.Equal((1, 15), Kinds.BandOf(1)); Assert.Equal((1, 15), Kinds.BandOf(15));
        Assert.Equal((16, 30), Kinds.BandOf(16)); Assert.Equal((31, 40), Kinds.BandOf(31));
        Assert.Equal((91, 100), Kinds.BandOf(100)); Assert.Null(Kinds.BandOf(0)); Assert.Null(Kinds.BandOf(101));
    }

    [Fact]
    public void A_source_needs_territory_map_and_coordinates_for_a_flag()
    {
        Assert.False(new Source { Terr = 148, Map = 4 }.HasMapPosition);
        Assert.False(new Source { Xy = new[] { 24f, 16f } }.HasMapPosition);
        Assert.True(new Source { Terr = 148, Map = 4, Xy = new[] { 24f, 16f } }.HasMapPosition);
    }

    [Fact]
    public void Overall_progress_reads_as_a_count_and_a_floored_percentage()
    {
        Assert.Equal("37 of 124 obtained, 29%", Progress.Summary(37, 124, 0));
        Assert.Equal("124 of 124 obtained, 100%", Progress.Summary(124, 124, 0));
        Assert.Equal("199 of 200 obtained, 99%", Progress.Summary(199, 200, 0));
        Assert.Equal("0 of 0 obtained, 0%", Progress.Summary(0, 0, 0));
        Assert.Equal("3 of 50 obtained, 6% · 12 hidden by Include", Progress.Summary(3, 50, 12));
    }

    [Fact]
    public void Only_the_categories_a_list_uses_get_a_checkbox_in_the_driver_order()
    {
        var plain = new Entry { Sources = { S("world"), S("hunt", "B") } };
        var mixed = new Entry { Sources = { S("hunt", "A"), S("dungeon"), S("unknown"), S("map") } };
        Assert.Empty(Kinds.PresentOptIn(new[] { plain }));
        Assert.Equal(new[] { "hunt", "map", "dungeon", "unknown" }, Kinds.PresentOptIn(new[] { plain, mixed }).Select(o => o.Key));
    }

    [Fact]
    public void Hunts_are_labelled_by_rank()
    {
        Assert.Equal("B-rank hunt", Kinds.SourceLabel(S("hunt", "B")));
        Assert.Equal("S-rank hunt", Kinds.SourceLabel(S("hunt", "S")));
        Assert.Equal("Open world", Kinds.SourceLabel(S("world")));
    }

    [Fact]
    public void The_driving_source_follows_the_kind_order_then_the_level()
    {
        var wanted = new Source { K = "wanted", Lv = 1 }; var world = new Source { K = "world", Lv = 30 }; var dungeon = new Source { K = "dungeon", Lv = 50 };
        var e = new Entry { Sources = { wanted, dungeon, world } };
        Assert.Same(world, Kinds.Driver(e, new HashSet<string>()));
        Assert.Equal(30, Kinds.ShownLevel(e, new HashSet<string>()));
        Assert.Same(world, Kinds.Driver(e, new HashSet<string> { "dungeon" }));
        var quest = new Source { K = "quest", Lv = 45 };
        Assert.Same(quest, Kinds.Driver(new Entry { Sources = { world, quest } }, new HashSet<string>()));
        var low = new Source { K = "world", Lv = 12 };
        Assert.Same(low, Kinds.Driver(new Entry { Sources = { world, low } }, new HashSet<string>()));
        Assert.Null(Kinds.Driver(new Entry { Sources = { dungeon } }, new HashSet<string>()));
        Assert.Same(dungeon, Kinds.Driver(new Entry { Sources = { dungeon } }, new HashSet<string> { "dungeon" }));
    }

    [Fact]
    public void Leve_beats_fate_and_party_content_comes_second_last()
    {
        Assert.True(Kinds.Rank("leve") < Kinds.Rank("fate"));
        Assert.True(Kinds.Rank("world") < Kinds.Rank("wanted"));
        Assert.True(Kinds.Rank("questmob") < Kinds.Rank("carnivale") && Kinds.Rank("carnivale") < Kinds.Rank("world"));
        Assert.True(Kinds.Rank("dungeon") < Kinds.Rank("unknown"));
        Assert.Equal(Kinds.Order.Length, Kinds.Rank("nonsense"));
    }

    [Fact]
    public void A_source_shows_its_level_range_when_it_has_one()
    {
        Assert.Equal("Lv 28", Kinds.LevelText(new Source { Lv = 28 }));
        Assert.Equal("Lv 28-31", Kinds.LevelText(new Source { Lv = 28, LvMax = 31 }));
        Assert.Equal("Lv 28", Kinds.LevelText(new Source { Lv = 28, LvMax = 28 }));
    }

    [Fact]
    public void Search_reads_the_name_and_the_shown_sources_case_insensitively()
    {
        var e = new Entry { Name = "Bad Breath", Sources = { new Source { K = "world", Name = "Stroper", Loc = "South Shroud", Note = "several spots" } } };
        Assert.True(Kinds.Matches(e, e.Sources, ""));
        Assert.True(Kinds.Matches(e, e.Sources, "bad"));
        Assert.True(Kinds.Matches(e, e.Sources, "STROPER"));
        Assert.True(Kinds.Matches(e, e.Sources, "south shroud"));
        Assert.True(Kinds.Matches(e, e.Sources, "spots"));
        Assert.False(Kinds.Matches(e, e.Sources, "toad"));
        Assert.False(Kinds.Matches(e, Array.Empty<Source>(), "stroper"));
    }

    [Fact]
    public void Sorting_follows_the_trackers_options()
    {
        var a = new Entry { Id = 3, Name = "Loom" }; var b = new Entry { Id = 1, Name = "Water Cannon" }; var c = new Entry { Id = 2, Name = "Bristle" };
        var levels = new Dictionary<int, int> { [3] = 50, [1] = 1, [2] = 20 };
        string Names(string sort) => string.Join(",", Kinds.Sorted(new[] { a, b, c }, e => e, e => levels[e.Id], sort).Select(e => e.Name));
        Assert.Equal("Water Cannon,Bristle,Loom", Names("lv"));
        Assert.Equal("Water Cannon,Bristle,Loom", Names("no"));
        Assert.Equal("Loom,Bristle,Water Cannon", Names("no-desc"));
        Assert.Equal("Bristle,Loom,Water Cannon", Names("az"));
        Assert.Equal("Water Cannon,Loom,Bristle", Names("za"));
    }

    [Fact]
    public void Every_include_box_explains_itself()
    {
        foreach (var (key, _) in Kinds.OptIn)
            Assert.False(string.IsNullOrEmpty(Kinds.IncludeTip(key)), key);
        Assert.Contains("allowance", Kinds.IncludeTip("wanted"));
        Assert.Contains("B ranks", Kinds.IncludeTip("hunt"));
        Assert.Equal("", Kinds.IncludeTip("world"));
    }

    [Fact]
    public void Every_kind_the_lists_use_has_a_hint()
    {
        foreach (var kind in Kinds.AlwaysOn.Concat(Kinds.OptIn.Select(o => o.Key)).Where(k => k != "unknown"))
            Assert.False(string.IsNullOrEmpty(Kinds.Hint(kind)), kind);
        Assert.Contains("levemete", Kinds.Hint("leve"));
        Assert.Equal("", Kinds.Hint("unknown"));
    }

    [Fact]
    public void A_leve_goes_to_its_levemete_first_and_to_the_enemy_on_the_next_tap()
    {
        var via = new Source { K = "leve", Name = "Kikiri", Terr = 145, Map = 22, Xy = new[] { 14f, 23f } };
        var leve = new Source { K = "leve", Name = "Battle Drake", Leve = "A Cold-blooded Business", Via = via, Terr = 145, Map = 22, Xy = new[] { 14f, 23f } };
        Assert.Same(via, Kinds.NextStop(leve, leveHeld: false));
        Assert.Same(leve, Kinds.NextStop(leve, leveHeld: true));
        var plain = new Source { K = "world", Name = "Sundrake" };
        Assert.Same(plain, Kinds.NextStop(plain, leveHeld: false));
        var unplaced = new Source { K = "leve", Name = "Angry Sow", Via = new Source { Name = "somebody" } };
        Assert.Same(unplaced, Kinds.NextStop(unplaced, leveHeld: false));
    }

    [Fact]
    public void Wiki_leve_titles_match_the_games_names_without_their_suffixes()
    {
        Assert.Equal("goblin up sharlayan", Kinds.LeveKey("Goblin Up Sharlayan (L)"));
        Assert.Equal("birds of a feather", Kinds.LeveKey("Birds of a Feather (Levequest)"));
        Assert.Equal("reeking havoc", Kinds.LeveKey("Reeking Havoc"));
    }

    [Fact]
    public void A_quest_enemy_whose_quest_is_done_no_longer_drives_the_row()
    {
        var mob = new Source { K = "questmob", Name = "Doctore", Lv = 40, Note = "quest: Mr. Slipshod and Ms. Uptight" };
        var world = new Source { K = "world", Name = "Dullahan", Lv = 45 };
        var e = new Entry { Sources = { world, mob } };
        Assert.Equal("Mr. Slipshod and Ms. Uptight", Kinds.QuestOf(mob));
        Assert.Null(Kinds.QuestOf(world));
        Assert.Same(mob, Kinds.Driver(e, new HashSet<string>()));
        Assert.Same(world, Kinds.Driver(e, new HashSet<string>(), s => Kinds.QuestOf(s) == null));
        Assert.Equal("ancient wisdom", Kinds.LeveKey("Ancient Wisdom (Quest)"));
    }

    [Fact]
    public void A_carnivale_source_names_its_stage_and_is_skipped_once_cleared()
    {
        var stage = new Source { K = "carnivale", Name = "Azulmagia", Lv = 50, Note = "Stage 25" };
        var world = new Source { K = "world", Name = "Gigas", Lv = 45 };
        var e = new Entry { Sources = { world, stage } };
        Assert.Equal(25, Kinds.StageOf(stage));
        Assert.Null(Kinds.StageOf(world));
        Assert.Null(Kinds.StageOf(new Source { K = "carnivale", Name = "x", Lv = 50 }));
        Assert.Same(stage, Kinds.Driver(e, new HashSet<string>()));
        Assert.Same(world, Kinds.Driver(e, new HashSet<string>(), s => Kinds.StageOf(s) == null));
    }
}
