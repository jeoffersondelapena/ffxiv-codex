using Codex;
using Xunit;

public class RulesTests
{
    private static Source S(string kind, string? rank = null) => new() { K = kind, Name = "x", Rank = rank };

    [Fact]
    public void Open_world_sources_are_always_shown()
    {
        foreach (var kind in new[] { "world", "fate", "leve", "questmob", "totem", "quest", "default" })
            Assert.True(Kinds.Visible(S(kind), new HashSet<string>()), kind);
    }

    [Fact]
    public void Instances_hunts_and_maps_need_their_checkbox()
    {
        foreach (var kind in new[] { "dungeon", "trial", "raid", "carnivale", "guildhest", "tdungeon", "map", "unknown" })
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
    public void Only_the_categories_a_list_uses_get_a_checkbox_in_the_trackers_order()
    {
        var plain = new Entry { Sources = { S("world"), S("hunt", "B") } };
        var mixed = new Entry { Sources = { S("hunt", "A"), S("dungeon") } };
        Assert.Empty(Kinds.PresentOptIn(new[] { plain }));
        Assert.Equal(new[] { "dungeon", "hunt" }, Kinds.PresentOptIn(new[] { plain, mixed }).Select(o => o.Key));
    }

    [Fact]
    public void Hunts_are_labelled_by_rank()
    {
        Assert.Equal("B-rank hunt", Kinds.SourceLabel(S("hunt", "B")));
        Assert.Equal("S-rank hunt", Kinds.SourceLabel(S("hunt", "S")));
        Assert.Equal("Open world", Kinds.SourceLabel(S("world")));
    }

    [Fact]
    public void An_entry_sits_at_its_lowest_visible_source()
    {
        var e = new Entry { Sources = { new Source { K = "dungeon", Lv = 50 }, new Source { K = "world", Lv = 28 }, new Source { K = "hunt", Rank = "A", Lv = 20 } } };
        Assert.Equal(28, Kinds.ShownLevel(e, new HashSet<string>()));
        Assert.Equal(20, Kinds.ShownLevel(e, new HashSet<string> { "hunt" }));
        Assert.Null(Kinds.ShownLevel(new Entry { Sources = { new Source { K = "raid", Lv = 60 } } }, new HashSet<string>()));
    }
}
