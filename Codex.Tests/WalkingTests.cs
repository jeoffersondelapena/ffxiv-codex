using System.Numerics;
using Codex;
using Xunit;

public class WalkingTests
{
    [Fact]
    public void The_walk_left_follows_the_path_not_the_straight_line()
    {
        Assert.Equal(0f, Walking.Left(Vector3.Zero, []));
        Assert.Equal(40f, Walking.Left(Vector3.Zero, [new Vector3(0, 0, 20), new Vector3(20, 0, 20)]));
    }

    [Fact]
    public void Sprint_only_on_foot_when_ready_on_a_long_enough_walk()
    {
        Assert.True(Walking.SprintNow(walking: true, mounted: false, ready: true, left: 120));
        Assert.False(Walking.SprintNow(walking: false, mounted: false, ready: true, left: 120));
        Assert.False(Walking.SprintNow(walking: true, mounted: true, ready: true, left: 120));
        Assert.False(Walking.SprintNow(walking: true, mounted: false, ready: false, left: 120));
        Assert.False(Walking.SprintNow(walking: true, mounted: false, ready: true, left: 12));
    }
}
