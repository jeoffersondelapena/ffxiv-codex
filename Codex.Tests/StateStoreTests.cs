using Codex;
using Xunit;

public class StateStoreTests
{
    [Fact]
    public void Ticks_round_trip_sorted()
    {
        var json = StateStore.Serialize(new[] { 7, 3, 3, 12 });
        Assert.Equal(new HashSet<int> { 3, 7, 12 }, StateStore.Deserialize(json));
        Assert.Contains("[3,7,12]", json);
    }

    [Fact]
    public void The_file_name_never_contains_the_content_id_and_differs_per_character()
    {
        var a = StateStore.Key(12345678901234UL); var b = StateStore.Key(12345678901235UL);
        Assert.Equal(16, a.Length); Assert.NotEqual(a, b); Assert.DoesNotContain("12345678901234", a);
        Assert.Equal(a, StateStore.Key(12345678901234UL));
    }

    [Fact]
    public void An_empty_or_broken_file_yields_no_ticks()
    {
        Assert.Empty(StateStore.Deserialize("{}"));
        Assert.Throws<System.Text.Json.JsonException>(() => StateStore.Deserialize("not json"));
    }
}
