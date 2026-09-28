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
}
